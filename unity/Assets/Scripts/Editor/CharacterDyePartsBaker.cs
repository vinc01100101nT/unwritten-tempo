using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Works out every character model's recolourable parts and saves them to <see cref="CharacterDyeParts"/>. Run it
/// after adding or changing a character model (the asset rules check names any model left out):
/// <c>CharacterDyePartsBaker.BakeAll()</c> through the live editor. It writes only that one asset, and only when it
/// is run.
///
/// <para>The detection is the one the game used to run on the player's machine, unchanged: sample the texture at
/// every triangle's centre weighted by its area, so only colours the model actually wears count; drop colours under
/// 2% of the worn surface, skin, eyes and outline black.</para>
/// </summary>
public static class CharacterDyePartsBaker
{
    const string ModelFolder = "Assets/1_Authored/Art/Characters/Models";
    const string TablePath = "Assets/3_Data/Resources/" + CharacterDyeParts.ResourcePath + ".asset";
    const string BareBodyFolder = "Assets/2_Generated/Characters/Bodies";

    // Must match the eight colour pairs the character shader takes.
    const int MaxParts = 8;

    // Skin / eyes / mouth families never offered as dyeable parts (so character creation only offers clothing).
    // Deliberately NO near-black entry: a dark OUTFIT sits close to eye/outline black, and filtering black would
    // wrongly hide dark clothing. True outline black is dropped as background in Tally, and eye pixels are too few to
    // survive the coverage cut.
    static readonly Color[] Protected =
    {
        new Color(0.9373f, 0.5686f, 0.3098f),   // skin / tan
        new Color(0.8196f, 0.2941f, 0.2039f),   // mouth / red accent
        new Color(0.9490f, 0.9176f, 0.9451f),   // eye white / highlight
    };
    const float ProtectFilterTol = 0.14f;

    /// <summary>Bake every model in the character model folder. Returns one line per model for the caller to read.</summary>
    public static string BakeAll()
    {
        var table = AssetDatabase.LoadAssetAtPath<CharacterDyeParts>(TablePath);
        bool created = table == null;
        if (created) table = ScriptableObject.CreateInstance<CharacterDyeParts>();
        table.entries.Clear();

        var report = new System.Text.StringBuilder();
        var guids = AssetDatabase.FindAssets("t:GameObject", new[] { ModelFolder });
        var paths = new List<string>();
        foreach (var g in guids) paths.Add(AssetDatabase.GUIDToAssetPath(g));
        paths.Sort(System.StringComparer.Ordinal);

        foreach (var path in paths)
        {
            // The BARE body the game wears when there is one (HeadPieceBaker): a hair's colour is the hair's to
            // change, never a clothes part — and a hat cut off the body is no part of it at all.
            var bare = AssetDatabase.LoadAssetAtPath<GameObject>(
                BareBodyFolder + "/" + System.IO.Path.GetFileNameWithoutExtension(path) + ".prefab");
            var model = bare != null ? bare : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) continue;
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            string textures = CharacterDye3D.SignatureOf(renderers);
            var parts = Detect(renderers, out bool byWornSurface);
            table.entries.Add(new CharacterDyeParts.Entry
            {
                modelFile = System.IO.Path.GetFileNameWithoutExtension(path),
                textures = textures,
                parts = parts.ToArray(),
            });
            report.Append(System.IO.Path.GetFileName(path)).Append(": ").Append(parts.Count).Append(" part(s)")
                  .Append(byWornSurface ? "" : " (mesh not readable — whole texture scanned)").Append('\n');
        }

        if (created) AssetDatabase.CreateAsset(table, TablePath);
        else EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
        CharacterDyeParts.Forget();
        return report.ToString();
    }

    static List<Color> Detect(SkinnedMeshRenderer[] renderers, out bool sampledByUv)
    {
        var detected = new List<Color>();
        var counts   = new Dictionary<int, float>();
        var reps     = new Dictionary<int, Color32>();
        var readback = new Dictionary<Texture, PixelBuf>();
        sampledByUv = false;

        foreach (var r in renderers)
        {
            if (r == null || r.sharedMaterial == null) continue;
            var mesh = r.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            var uv    = mesh.uv;
            var verts = mesh.vertices;
            var tris  = mesh.triangles;
            if (uv == null || uv.Length == 0 || tris == null || tris.Length < 3) continue;
            if (uv.Length < verts.Length) continue;
            var tex = CharacterDye3D.TextureOf(r.sharedMaterial);
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
            BuildParts(counts, reps, MaxParts, minFrac: 0.02f, detected);
            return detected;
        }

        // A mesh that is not readable: scan the whole texture (coarser — may list colours the model does not wear).
        Texture atlas = null;
        foreach (var r in renderers)
            if (r != null && r.sharedMaterial != null && (atlas = CharacterDye3D.TextureOf(r.sharedMaterial)) != null) break;
        if (atlas != null)
        {
            var buf = ReadbackSRGB(atlas);
            for (int i = 0; i < buf.px.Length; i++) Tally(buf.px[i], 1f, counts, reps);
            BuildParts(counts, reps, MaxParts, minFrac: 0.01f, detected);
        }
        return detected;
    }

    struct PixelBuf { public Color32[] px; public int w, h; }

    // GPU readback of a (non-CPU-readable) texture into authored-sRGB pixels.
    static PixelBuf ReadbackSRGB(Texture atlas)
    {
        int w = atlas.width, h = atlas.height;
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(atlas, rt);
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply(false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var px = tex.GetPixels32();
        Object.DestroyImmediate(tex);
        return new PixelBuf { px = px, w = w, h = h };
    }

    // Add one sample (weighted by worn area) to the colour histogram (skips transparent + outline black).
    static void Tally(Color32 p, float w, Dictionary<int, float> counts, Dictionary<int, Color32> reps)
    {
        if (p.a < 128) return;
        if (p.r < 24 && p.g < 24 && p.b < 24) return;
        const int quant = 12;
        int key = (p.r / quant) << 16 | (p.g / quant) << 8 | (p.b / quant);
        if (counts.TryGetValue(key, out float c)) counts[key] = c + w;
        else { counts[key] = w; reps[key] = p; }
    }

    // Top colours by coverage, skin/eyes dropped, capped at maxParts, anything under minFrac of the sampled weight dropped.
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
            if (IsProtected(col)) continue;
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
}
