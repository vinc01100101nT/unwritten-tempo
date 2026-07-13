using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-cloth-part dye for the live-3D Quaternius player. Recolors individual outfit parts by baking a
/// recolored copy of the character atlas (<c>Graphics.Blit</c> through <c>Unwritten/AtlasRecolor</c>) and
/// assigning it back to each renderer's <c>_BaseMap</c> via a <see cref="MaterialPropertyBlock"/> — so
/// URP Lit keeps doing the lighting, no material instances are created, and only THIS player recolors
/// (NPCs sharing the atlas are untouched). Skin and eyes are never listed as parts, so they pass through.
///
/// <para>SINGLE SOURCE OF TRUTH: the persisted dye (source→target hex pairs) lives on
/// <see cref="Character"/>; this component is a computed VIEW that reads it (on
/// <see cref="Character.OnAppearanceChanged"/>) and writes back through <see cref="Character.SetDye"/>.</para>
///
/// <para>Not a hot path: a bake happens only when the dye changes or a save loads — never per frame.</para>
/// </summary>
[DisallowMultipleComponent]
public class CharacterDye3D : MonoBehaviour
{
    const int MaxParts = 8;   // must match MAXPARTS in AtlasRecolor.shader
    const float MatchTol = 0.12f;

    // Skin / eyes / mouth families NEVER offered as dyeable parts (so the wardrobe only shows clothing).
    // Deliberately NO near-black entry: a dark OUTFIT sits close to eye/outline black, and filtering black
    // would wrongly hide dark clothing. True outline black is dropped as background in ScanColors, and eye
    // pixels are too few to survive the min-coverage cut, so they never reach the parts list anyway.
    static readonly Color[] Protected =
    {
        new Color(0.9373f, 0.5686f, 0.3098f),   // skin / tan
        new Color(0.8196f, 0.2941f, 0.2039f),   // mouth / red accent
        new Color(0.9490f, 0.9176f, 0.9451f),   // eye white / highlight
    };
    const float ProtectFilterTol = 0.14f;
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");   // URP Lit
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");   // Standard / legacy fallback

    // The texture slot a material actually samples (read + overridden with the same id, or the dye misses).
    static int TexIdFor(Material m)
        => m.HasProperty(BaseMapId) ? BaseMapId : MainTexId;

    Material recolorMat;
    SkinnedMeshRenderer[] renderers;
    MaterialPropertyBlock mpb;

    // One baked atlas per distinct source texture (body vs hair/hat may differ).
    readonly Dictionary<Texture, RenderTexture> bakedByAtlas = new Dictionary<Texture, RenderTexture>();

    // The current recolor map (authored sRGB). Parallel lists; index-matched.
    readonly List<Color> sources = new List<Color>();
    readonly List<Color> targets = new List<Color>();

    // Reused shader-array scratch so Apply allocates nothing after the first call.
    readonly Vector4[] srcScratch = new Vector4[MaxParts];
    readonly Vector4[] dstScratch = new Vector4[MaxParts];

    Character boundCharacter;

    // Distinct dyeable cloth colors of this character (skin/eyes filtered out), detected once at runtime.
    List<Color> detected;

    void Awake()
    {
        renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        mpb = new MaterialPropertyBlock();
        var sh = Shader.Find("Unwritten/AtlasRecolor");
        if (sh != null) recolorMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
    }

    void OnEnable()
    {
        // Bind to the persistent character (may not exist yet on frame one — poll cheaply in Update until it does).
        TryBind();
    }

    void Update()
    {
        if (boundCharacter == null) TryBind();
    }

    void TryBind()
    {
        var c = Character.Instance;
        if (c == null || c == boundCharacter) return;
        boundCharacter = c;
        boundCharacter.OnAppearanceChanged += PullFromCharacter;
        PullFromCharacter();
    }

    void OnDisable()
    {
        if (boundCharacter != null) boundCharacter.OnAppearanceChanged -= PullFromCharacter;
        boundCharacter = null;
    }

    void OnDestroy()
    {
        if (recolorMat != null) Destroy(recolorMat);
        foreach (var rt in bakedByAtlas.Values) if (rt != null) rt.Release();
        bakedByAtlas.Clear();
    }

    // ── Public API (a dye UI calls these) ────────────────────────────────────────
    /// <summary>Recolor the part whose atlas color is <paramref name="source"/> to <paramref name="target"/>.
    /// Persists through <see cref="Character"/> and re-bakes. Pass <paramref name="target"/> == source to reset.</summary>
    public void SetPartColor(Color source, Color target)
    {
        int i = IndexOf(source);
        if (i < 0) { sources.Add(source); targets.Add(target); }
        else targets[i] = target;
        PushToCharacter();   // writes the SSOT + fires OnAppearanceChanged → PullFromCharacter → Apply
    }

    /// <summary>Clear the dye on a part (revert to its original atlas color).</summary>
    public void ClearPart(Color source)
    {
        int i = IndexOf(source);
        if (i < 0) return;
        sources.RemoveAt(i);
        targets.RemoveAt(i);
        PushToCharacter();
    }

    /// <summary>Drop any dye entries whose source isn't one of this character's parts. Prevents a previous
    /// character's dyes from lingering and overflowing the shader's fixed slot count (they'd shove the current
    /// character's picks past the limit, so they'd never render).</summary>
    public void RetainParts(IReadOnlyList<Color> valid)
    {
        bool changed = false;
        for (int i = sources.Count - 1; i >= 0; i--)
        {
            bool keep = false;
            for (int j = 0; j < valid.Count; j++)
                if (Approximately(sources[i], valid[j])) { keep = true; break; }
            if (!keep) { sources.RemoveAt(i); targets.RemoveAt(i); changed = true; }
        }
        if (changed) PushToCharacter();   // persist the pruned map + re-bake
    }

    /// <summary>The chosen color for a part, or the part's own color if it hasn't been dyed.</summary>
    public Color TargetFor(Color source)
    {
        int i = IndexOf(source);
        return i >= 0 ? targets[i] : source;
    }

    /// <summary>The character's distinct dyeable cloth colors (skin/eyes filtered out), detected once at
    /// runtime and cached. Drives the in-game wardrobe's part swatches.
    ///
    /// <para>Samples the atlas ONLY at the texels the mesh actually maps to (its UV footprint), so unworn
    /// atlas regions / other-part swatches never surface as phantom "clothing" the character can't display.
    /// Requires Read/Write-enabled meshes (Mesh.uv); when a mesh isn't readable it falls back to scanning the
    /// whole atlas (coarser — may list unworn colors). Enable Read/Write via
    /// <c>Tools ▸ unwritten ▸ Rigs ▸ Enable Read-Write on Character Models</c>.</para></summary>
    public IReadOnlyList<Color> DetectableParts(int maxParts = 8)
    {
        EnsureRenderers();              // may invalidate `detected` if the mesh was swapped
        if (detected != null) return detected;
        detected = new List<Color>();
        if (renderers == null) return detected;

        int want = Mathf.Min(maxParts, MaxParts);
        var counts   = new Dictionary<int, float>();   // weighted by worn surface area
        var reps     = new Dictionary<int, Color32>();
        var readback = new Dictionary<Texture, PixelBuf>();
        bool sampledByUv = false;

        // PRIMARY: sample each mesh TRIANGLE's interior (centroid), weighted by its surface area — so only the
        // colors this character actually WEARS surface, big worn parts dominate, and unworn atlas swatches (other
        // characters share the atlas) never appear. Centroid (not per-vertex) avoids UV seams sampling a
        // neighbouring swatch, which is what surfaced phantom colors before.
        foreach (var r in renderers)
        {
            if (r == null || r.sharedMaterial == null) continue;
            var mesh = r.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;   // Mesh.uv/vertices/triangles need Read/Write
            var uv    = mesh.uv;
            var verts = mesh.vertices;
            var tris  = mesh.triangles;
            if (uv == null || uv.Length == 0 || tris == null || tris.Length < 3) continue;
            if (uv.Length < verts.Length) continue;
            var m   = r.sharedMaterial;
            var tex = m.GetTexture(TexIdFor(m)) ?? m.mainTexture;
            if (tex == null) continue;

            if (!readback.TryGetValue(tex, out var buf)) { buf = ReadbackSRGB(tex); readback[tex] = buf; }
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
                float cu = (uv[ia].x + uv[ib].x + uv[ic].x) / 3f;
                float cv = (uv[ia].y + uv[ib].y + uv[ic].y) / 3f;
                int x = Mathf.Clamp((int)(Mathf.Clamp01(cu) * (buf.w - 1) + 0.5f), 0, buf.w - 1);
                int y = Mathf.Clamp((int)(Mathf.Clamp01(cv) * (buf.h - 1) + 0.5f), 0, buf.h - 1);
                float area = Vector3.Cross(verts[ib] - verts[ia], verts[ic] - verts[ia]).magnitude * 0.5f;
                Tally(buf.px[y * buf.w + x], area, counts, reps);
            }
            sampledByUv = true;
        }

        if (sampledByUv)
        {
            // Keep only swatches covering ≥2% of worn surface — drops tiny seam/sliver phantoms.
            BuildParts(counts, reps, want, minFrac: 0.02f, detected);
            if (detected.Count == 0) detected = null;   // transient empty scan: retry next call, don't cache
            return detected ?? new List<Color>();
        }

        // FALLBACK: mesh not Read/Write-enabled → scan the whole atlas (coarse; may list unworn colors).
        // Enable Read/Write via Tools ▸ unwritten ▸ Rigs ▸ Enable Read-Write on Character Models to get the
        // accurate worn-only list above.
        Texture atlas = null;
        foreach (var r in renderers)
            if (r != null && r.sharedMaterial != null)
            {
                var m = r.sharedMaterial;
                atlas = m.GetTexture(TexIdFor(m)) ?? m.mainTexture;
                if (atlas != null) break;
            }
        if (atlas != null)
        {
            var buf = ReadbackSRGB(atlas);
            for (int i = 0; i < buf.px.Length; i++) Tally(buf.px[i], 1f, counts, reps);
            BuildParts(counts, reps, want, minFrac: 0.01f, detected);
        }
        if (detected.Count == 0) detected = null;   // transient empty scan: retry next call, don't cache
        return detected ?? new List<Color>();
    }

    struct PixelBuf { public Color32[] px; public int w, h; }

    // GPU readback of a (non-CPU-readable) atlas into authored-sRGB pixels. Runs once per open, never per frame.
    static PixelBuf ReadbackSRGB(Texture atlas)
    {
        int w = atlas.width, h = atlas.height;
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(atlas, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply(false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var px = tex.GetPixels32();
        Destroy(tex);
        return new PixelBuf { px = px, w = w, h = h };
    }

    // Add one atlas sample (weighted by worn area) to the color histogram (skips transparent + outline black).
    static void Tally(Color32 p, float w, Dictionary<int, float> counts, Dictionary<int, Color32> reps)
    {
        if (p.a < 128) return;
        if (p.r < 24 && p.g < 24 && p.b < 24) return;   // background / outline black
        const int quant = 12;
        int key = (p.r / quant) << 16 | (p.g / quant) << 8 | (p.b / quant);
        if (counts.TryGetValue(key, out float c)) counts[key] = c + w;
        else { counts[key] = w; reps[key] = p; }
    }

    // Top colors by coverage, skin/eyes dropped, capped at maxParts. Drops any swatch below minFrac of the
    // total sampled weight (worn surface area) so unworn / seam-sliver colors never surface as phantom parts.
    static void BuildParts(Dictionary<int, float> counts, Dictionary<int, Color32> reps,
                           int maxParts, float minFrac, List<Color> outColors)
    {
        float total = 0f;
        foreach (var kv in counts) total += kv.Value;
        if (total <= 0f) return;
        float minW = total * minFrac;

        var ordered = new List<KeyValuePair<int, float>>(counts);
        ordered.Sort((a, b) => b.Value.CompareTo(a.Value));
        foreach (var kv in ordered)
        {
            if (outColors.Count >= maxParts) break;
            if (kv.Value < minW) continue;
            Color col = (Color)reps[kv.Key];
            if (IsProtected(col)) continue;   // skin / eyes never offered
            outColors.Add(col);
        }
    }

    static bool IsProtected(Color c)
    {
        float t2 = ProtectFilterTol * ProtectFilterTol * 3f;
        foreach (var p in Protected)
        {
            float dr = c.r - p.r, dg = c.g - p.g, db = c.b - p.b;
            if (dr * dr + dg * dg + db * db <= t2) return true;
        }
        return false;
    }

    // ── Character binding (SSOT) ─────────────────────────────────────────────────
    void PullFromCharacter()
    {
        // A dye belongs to the specific character it was made on — it must NOT ride onto a different character
        // just because they share a base color (both navy, etc.). The dye is stamped with a signature of the
        // worn TEXTURE (each Quaternius character has its own, even when the mesh is shared); if the persisted
        // stamp doesn't match the worn texture, the dye is foreign → drop it. An un-stamped legacy dye (null,
        // unknown origin) also fails the match, so it's cleared once — after which every dye carries its stamp.
        if (boundCharacter != null && boundCharacter.DyeSources != null && boundCharacter.DyeSources.Length > 0)
        {
            string sig = ModelSignature();
            if (!string.IsNullOrEmpty(sig) && boundCharacter.DyeModel != sig)
            {
                sources.Clear();
                targets.Clear();
                boundCharacter.SetDye(System.Array.Empty<string>(), System.Array.Empty<string>(), sig);
                return;   // SetDye re-fires OnAppearanceChanged → PullFromCharacter (now clean) → Apply
            }
        }

        DecodeInto(boundCharacter != null ? boundCharacter.DyeSources : null,
                   boundCharacter != null ? boundCharacter.DyeTargets : null);
        Apply();
    }

    void PushToCharacter()
    {
        if (boundCharacter == null) { Apply(); return; }
        boundCharacter.SetDye(Encode(sources), Encode(targets), ModelSignature());   // fires OnAppearanceChanged → PullFromCharacter → Apply
    }

    // A stable fingerprint of the worn character — the sorted, distinct names of the TEXTURES its materials
    // sample (what the dye actually recolors). Keyed on texture, NOT mesh: Quaternius bodies often share one
    // mesh across characters (BlueSoldier & Soldier are the same geometry) but each has its own texture, so a
    // mesh-name key would collide and let dye bleed between them. Off the hot path (dye change / load only).
    string ModelSignature()
    {
        EnsureRenderers();
        if (renderers == null || renderers.Length == 0) return null;
        var names = new List<string>();
        foreach (var r in renderers)
        {
            if (r == null || r.sharedMaterial == null) continue;
            var m   = r.sharedMaterial;
            var tex = m.GetTexture(TexIdFor(m)) ?? m.mainTexture;
            if (tex == null) continue;
            if (!names.Contains(tex.name)) names.Add(tex.name);
        }
        if (names.Count == 0) return null;
        names.Sort(System.StringComparer.Ordinal);
        return string.Join("|", names);
    }

    int IndexOf(Color source)
    {
        for (int i = 0; i < sources.Count; i++)
            if (Approximately(sources[i], source)) return i;
        return -1;
    }

    static bool Approximately(Color a, Color b)
        => Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;

    static string[] Encode(List<Color> cols)
    {
        var arr = new string[cols.Count];
        for (int i = 0; i < cols.Count; i++) arr[i] = "#" + ColorUtility.ToHtmlStringRGB(cols[i]);
        return arr;
    }

    void DecodeInto(string[] src, string[] dst)
    {
        sources.Clear();
        targets.Clear();
        if (src == null || dst == null) return;
        int n = Mathf.Min(src.Length, dst.Length);
        for (int i = 0; i < n; i++)
            if (ColorUtility.TryParseHtmlString(src[i], out var s) && ColorUtility.TryParseHtmlString(dst[i], out var t))
            { sources.Add(s); targets.Add(t); }
    }

    // ── Bake + assign ────────────────────────────────────────────────────────────
    // Re-fetch the mesh renderers if the cache is empty or went stale (e.g. this component was reused across a
    // model swap, or Awake ran before the mesh existed). Cheap; only re-queries when actually stale.
    void EnsureRenderers()
    {
        if (renderers != null && renderers.Length > 0)
        {
            bool stale = false;
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] == null) { stale = true; break; }
            if (!stale) return;
        }
        renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        // The mesh changed → the detected part-colors and baked atlases belong to the old body; drop them.
        detected = null;
        foreach (var rt in bakedByAtlas.Values) if (rt != null) rt.Release();
        bakedByAtlas.Clear();
    }

    void Apply()
    {
        if (recolorMat == null) return;
        EnsureRenderers();
        if (renderers == null) return;

        // Feed EVERY detected part as a source (dyed or not), so the shader's nearest-part (Voronoi) match can
        // give each part its own pixels and no dyed part bleeds into a neighbor. Undyed parts map to themselves
        // (dst == src) → the shader passes them through. Skip the work entirely when nothing is dyed.
        var partsList = sources.Count > 0 ? DetectableParts() : null;
        int n = partsList != null ? Mathf.Min(partsList.Count, MaxParts) : 0;
        for (int i = 0; i < MaxParts; i++)
        {
            srcScratch[i] = i < n ? (Vector4)partsList[i]            : Vector4.zero;
            dstScratch[i] = i < n ? (Vector4)TargetFor(partsList[i]) : Vector4.zero;
        }
        recolorMat.SetInteger("_Count", n);
        recolorMat.SetFloat("_Tol", MatchTol);
        recolorMat.SetVectorArray("_Src", srcScratch);
        recolorMat.SetVectorArray("_Dst", dstScratch);

        foreach (var r in renderers)
        {
            if (r == null || r.sharedMaterial == null) continue;
            int texId = TexIdFor(r.sharedMaterial);
            var atlas = r.sharedMaterial.GetTexture(texId) ?? r.sharedMaterial.mainTexture;
            if (atlas == null) continue;

            // n == 0 → nothing dyed: restore the original atlas (clears any prior override) and skip the bake.
            if (n == 0)
            {
                r.GetPropertyBlock(mpb);
                mpb.SetTexture(texId, atlas);
                r.SetPropertyBlock(mpb);
                continue;
            }

            var rt = BakedFor(atlas);
            Graphics.Blit(atlas, rt, recolorMat);
            r.GetPropertyBlock(mpb);
            mpb.SetTexture(texId, rt);
            r.SetPropertyBlock(mpb);
        }
    }

    RenderTexture BakedFor(Texture atlas)
    {
        if (bakedByAtlas.TryGetValue(atlas, out var rt) && rt != null) return rt;
        // sRGB so the URP-Lit sample of the result matches the original sRGB atlas round-trip.
        rt = new RenderTexture(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            filterMode = FilterMode.Point,   // Quaternius atlases are point-sampled flat swatches
            name = "DyedAtlas_" + atlas.name,
        };
        bakedByAtlas[atlas] = rt;
        return rt;
    }
}
