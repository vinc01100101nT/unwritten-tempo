using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The shared, author-time core that bakes a rigged 3D model into <b>true 8-direction</b> billboard sprite
/// strips — one unique strip per <see cref="Facing8"/> octant, packed horizontally (one frame per cell) so
/// <see cref="SpriteSheetCache.Row"/> slices them and <see cref="BakedBillboard8"/> plays them. Both the
/// <see cref="SpriteBaker8"/> window and the <c>CharacterDef</c> one-click builder call this, so the
/// hard-won render path lives in exactly ONE place.
///
/// <para>It renders an offscreen orthographic camera at a fixed pitch on an isolated layer, yaws the model in
/// 45° steps for the 8 octants, samples each clip frame-by-frame via <see cref="AnimationMode"/>, and captures
/// to a transparent RenderTexture. THREE non-obvious requirements make off-screen skinned baking work and must
/// not be removed: kill leftover rigs first; <c>updateWhenOffscreen</c> (don't cull skinning); and
/// <c>forceMatrixRecalculationPerRender</c> (re-skin on the manual cam.Render, which otherwise draws bind pose).</para>
/// </summary>
public static class SpriteBakeCore
{
    /// <summary>Facing8 octant order — file suffixes AND the BakedBillboard8 strip-array order.</summary>
    public static readonly string[] DirNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    const int IsolationLayer = 31;          // render only the bake rig (layer 31 is conventionally free)
    static readonly Vector3 RigOrigin = new Vector3(0f, 5000f, 0f);   // far from any open scene geometry

    public struct Settings
    {
        public int cellSize;        // px per frame (square cell); also the sprite ppu (1 cell = 1 world unit)
        public bool pixelate;       // point-filter crunchy look vs supersampled hi-res
        public int superSample;     // hi-res supersample factor (ignored when pixelate)
        public float cameraPitch;   // degrees down
        public float framePadding;  // extra view room around the model bounds
        public float yawOffset;     // aligns front→camera (S)
        public bool clockwise;      // octant→yaw handedness

        public static Settings Default => new Settings
        { cellSize = 96, pixelate = true, superSample = 4, cameraPitch = 30f, framePadding = 1.25f, yawOffset = 180f, clockwise = true };
    }

    public struct Action { public string name; public AnimationClip clip; public int fps; public int frames; }

    public static int FrameCount(in Action a) => a.frames > 0 ? a.frames : Mathf.Max(1, Mathf.CeilToInt(a.clip.length * a.fps));

    /// <summary>Import texture-size ceiling. A horizontal strip wider than this gets downscaled on import —
    /// which shrinks the cell HEIGHT too, breaking the fixed-cell runtime slicing (sprite turns invisible).</summary>
    public const int MaxTexSize = 8192;

    /// <summary>Max frames one strip can hold at <paramref name="cellSize"/> px before it would exceed
    /// <see cref="MaxTexSize"/>. High FPS × long clips would otherwise blow past it.</summary>
    public static int FrameCap(int cellSize) => Mathf.Max(1, MaxTexSize / Mathf.Max(1, cellSize));

    public static int MaxFrames(IList<Action> actions)
    {
        int m = 1;
        foreach (var a in actions) if (a.clip != null) m = Mathf.Max(m, FrameCount(a));
        return m;
    }

    public static int EstimateImages(IList<Action> actions)
    {
        int t = 0;
        foreach (var a in actions) if (a.clip != null) t += DirNames.Length * FrameCount(a);
        return t;
    }

    public static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "BakedChar";
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim();
    }

    /// <summary>Bake every action's 8 strips into <paramref name="outDir"/> (<c>&lt;name&gt;_&lt;DIR&gt;.png</c>),
    /// configure their import settings, and return success. Caller must pass a non-null model and only actions
    /// with non-null clips.</summary>
    public static bool Bake(GameObject model, Texture2D albedo, Settings s, IList<Action> actions, string outDir)
    {
        Directory.CreateDirectory(outDir);
        DestroyStaleRigs();   // a leftover rig at RigOrigin would be captured as a frozen ghost (the old "double")

        GameObject pivot = null, inst = null, lightGO = null;
        Camera cam = null;
        RenderTexture rt = null;
        List<Material> tempMats = null;
        int frameCap = FrameCap(s.cellSize);                  // never let a strip exceed MaxTexSize (→ import downscale → invisible)
        int maxFrames = Mathf.Min(MaxFrames(actions), frameCap);

        bool ok = true;
        AnimationMode.StartAnimationMode();   // supported EDIT-MODE sampler (a manual PlayableGraph won't drive an Animator outside Play)
        try
        {
            pivot = new GameObject("BakePivot") { hideFlags = HideFlags.HideAndDontSave };
            pivot.transform.position = RigOrigin;
            inst = (GameObject)Object.Instantiate(model, RigOrigin, Quaternion.identity, pivot.transform);
            SetLayerRecursive(inst, IsolationLayer);
            tempMats = albedo != null ? ApplyAlbedo(inst, albedo) : null;

            // Off-screen skinned baking needs BOTH: don't cull skinning, AND re-skin on each manual render.
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                smr.forceMatrixRecalculationPerRender = true;
            }

            // One global framing reused for every dir/frame so size + grounding are stable. Frame from the UNION of
            // bounds across all animation POSES (not just the bind pose) so a tall pose — a jump apex, a raised-arms
            // swing — doesn't overflow the cell and clip the head. Horizontal uses max(x,z) so a side-facing
            // silhouette isn't cropped either. (Coarse sampling: the extremes are all the framing needs.)
            Bounds b = ComputeBounds(inst);
            foreach (var a in actions)
            {
                if (a.clip == null) continue;
                int fc = Mathf.Min(FrameCount(a), frameCap);
                int step = Mathf.Max(1, fc / 12);
                for (int f = 0; f < fc; f += step)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(inst, a.clip, fc > 1 ? (f / (float)fc) * a.clip.length : 0f);
                    AnimationMode.EndSampling();
                    b.Encapsulate(ComputeBounds(inst));
                }
            }
            // Frame in SCREEN space: the camera is PITCHED, so it mixes the model's DEPTH into the on-screen
            // vertical extent — a tall jump/attack pose with limbs thrown out projects TALLER than its raw height.
            // (Ignoring this is what clipped the head at frame-padding 1.) Use the across-yaw horizontal radius
            // (x/z swap as the model turns) for both width and the depth term so nothing clips at any of 8 facings.
            float pitch  = s.cameraPitch * Mathf.Deg2Rad;
            float radius = Mathf.Max(b.size.x, b.size.z);
            float screenH = b.size.y * Mathf.Cos(pitch) + radius * Mathf.Sin(pitch);
            float size = Mathf.Max(screenH, radius) * 0.5f * s.framePadding;
            if (size < 1e-3f) size = 1f;

            int ss = s.pixelate ? 1 : Mathf.Max(1, s.superSample);
            int renderCell = s.cellSize * ss;

            rt = new RenderTexture(renderCell, renderCell, 24, RenderTextureFormat.ARGB32) { antiAliasing = s.pixelate ? 1 : 8 };
            cam = BuildCamera(b, size, rt, s);
            lightGO = BuildLight().gameObject;

            int done = 0, totalSteps = EstimateImages(actions);
            foreach (var a in actions)
            {
                int nFrames = Mathf.Min(FrameCount(a), frameCap);   // cap so the strip stays within MaxTexSize
                for (int d = 0; d < DirNames.Length; d++)
                {
                    pivot.transform.rotation = YawFor(d, s);
                    var strip = new Texture2D(s.cellSize * nFrames, s.cellSize, TextureFormat.RGBA32, false);
                    ClearTransparent(strip);

                    for (int f = 0; f < nFrames; f++)
                    {
                        float t = nFrames > 1 ? (f / (float)nFrames) * a.clip.length : 0f;
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(inst, a.clip, t);
                        AnimationMode.EndSampling();

                        var cell = Capture(cam, rt, renderCell, s.cellSize, ss);
                        strip.SetPixels(f * s.cellSize, 0, s.cellSize, s.cellSize, cell.GetPixels());
                        Object.DestroyImmediate(cell);

                        if (EditorUtility.DisplayCancelableProgressBar("Baking 8-Dir Sprites",
                            $"{a.name} · {DirNames[d]} · frame {f + 1}/{nFrames}",
                            totalSteps > 0 ? (float)done / totalSteps : 0f)) { ok = false; break; }
                        done++;
                    }
                    strip.Apply();
                    File.WriteAllBytes($"{outDir}/{Sanitize(a.name)}_{DirNames[d]}.png", strip.EncodeToPNG());
                    Object.DestroyImmediate(strip);
                    if (!ok) break;
                }
                if (!ok) break;
            }
        }
        finally
        {
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            EditorUtility.ClearProgressBar();
            if (cam != null) { cam.targetTexture = null; Object.DestroyImmediate(cam.gameObject); }
            if (rt != null) Object.DestroyImmediate(rt);
            if (pivot != null) Object.DestroyImmediate(pivot);
            if (lightGO != null) Object.DestroyImmediate(lightGO);
            if (tempMats != null) foreach (var m in tempMats) if (m != null) Object.DestroyImmediate(m);
            DestroyStaleRigs();
        }

        AssetDatabase.Refresh();
        if (ok)
            foreach (var a in actions)
                for (int d = 0; d < DirNames.Length; d++)
                    ConfigureImport($"{outDir}/{Sanitize(a.name)}_{DirNames[d]}.png", s, maxFrames);
        AssetDatabase.Refresh();
        return ok;
    }

    // ---- render helpers (the hard-won path — do not change without re-validating the bake) ----

    static Texture2D Capture(Camera cam, RenderTexture rt, int renderCell, int cellSize, int ss)
    {
        var prev = RenderTexture.active;
        cam.Render();
        RenderTexture.active = rt;
        var full = new Texture2D(renderCell, renderCell, TextureFormat.RGBA32, false);
        full.ReadPixels(new Rect(0, 0, renderCell, renderCell), 0, 0);
        full.Apply();
        RenderTexture.active = prev;
        if (ss <= 1) return full;
        var small = Downscale(full, cellSize, ss);
        Object.DestroyImmediate(full);
        return small;
    }

    static Texture2D Downscale(Texture2D src, int outSize, int ss)
    {
        var srcPx = src.GetPixels();
        int sw = src.width;
        var outPx = new Color[outSize * outSize];
        for (int y = 0; y < outSize; y++)
            for (int x = 0; x < outSize; x++)
            {
                float r = 0, g = 0, bl = 0, a = 0;
                for (int sy = 0; sy < ss; sy++)
                    for (int sx = 0; sx < ss; sx++)
                    {
                        var c = srcPx[(y * ss + sy) * sw + (x * ss + sx)];
                        r += c.r * c.a; g += c.g * c.a; bl += c.b * c.a; a += c.a;
                    }
                int n = ss * ss;
                float alpha = a / n;
                outPx[y * outSize + x] = alpha > 1e-4f ? new Color(r / a, g / a, bl / a, alpha) : new Color(0, 0, 0, 0);
            }
        var outTex = new Texture2D(outSize, outSize, TextureFormat.RGBA32, false);
        outTex.SetPixels(outPx);
        outTex.Apply();
        return outTex;
    }

    static void DestroyStaleRigs()
    {
        foreach (var n in new[] { "BakePivot", "BakeCamera", "BakeLight" })
        {
            GameObject g; int guard = 0;
            while ((g = GameObject.Find(n)) != null && guard++ < 64) Object.DestroyImmediate(g);
        }
    }

    static Camera BuildCamera(Bounds b, float size, RenderTexture rt, Settings s)
    {
        var go = new GameObject("BakeCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = size;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.cullingMask = 1 << IsolationLayer;
        cam.allowMSAA = !s.pixelate;
        cam.targetTexture = rt;

        var rot = Quaternion.Euler(s.cameraPitch, 0f, 0f);
        Vector3 fwd = rot * Vector3.forward;
        // Centre on the bounds — the projection-aware `size` guarantees the whole (tallest/widest) pose fits with no
        // clipping at any yaw. The old bottom-align added a margin that ate headroom; exact feet placement in the
        // cell is recovered post-bake by GroundingFeetOffset, so centring costs no grounding accuracy.
        Vector3 lookTarget = b.center;
        float dist = size * 6f;
        go.transform.rotation = rot;
        go.transform.position = lookTarget - fwd * dist;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = dist * 2f + b.size.magnitude;
        return cam;
    }

    static Light BuildLight()
    {
        var go = new GameObject("BakeLight") { hideFlags = HideFlags.HideAndDontSave };
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.1f;
        l.cullingMask = 1 << IsolationLayer;
        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        return l;
    }

    static Quaternion YawFor(int octant, Settings s)
        => Quaternion.Euler(0f, s.yawOffset + (s.clockwise ? 1f : -1f) * octant * 45f, 0f);

    static Bounds ComputeBounds(GameObject root)
    {
        var rends = root.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(root.transform.position, Vector3.one);
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    static void ClearTransparent(Texture2D tex)
    {
        var px = new Color[tex.width * tex.height];   // default Color is (0,0,0,0)
        tex.SetPixels(px);
    }

    static List<Material> ApplyAlbedo(GameObject inst, Texture2D tex)
    {
        var created = new List<Material>();
        var litShader = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            var shared = r.sharedMaterials;
            var clones = new Material[shared.Length];
            for (int i = 0; i < shared.Length; i++)
            {
                var m = shared[i] != null ? new Material(shared[i]) : new Material(litShader);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
                clones[i] = m;
                created.Add(m);
            }
            r.sharedMaterials = clones;
        }
        return created;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }

    static void ConfigureImport(string path, Settings s, int maxFrames)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.filterMode = s.pixelate ? FilterMode.Point : FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.spritePixelsPerUnit = s.cellSize;   // 1 cell = 1 world unit; scale the character via its transform
        ti.npotScale = TextureImporterNPOTScale.None;
        int needed = s.cellSize * Mathf.Max(1, maxFrames);
        ti.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(needed), 256, 8192);
        ti.SaveAndReimport();
    }

    /// <summary>Measure the baked feet line and return the <see cref="BakedBillboard8.feetOffset"/> that grounds
    /// the feet when the object origin sits on the floor. The art is bottom-aligned in each (square, center-pivot)
    /// cell, so with 1 cell = 1 world unit the grounding offset is <c>0.5 - feetFromBottom</c>, where
    /// <c>feetFromBottom</c> is the lowest opaque pixel row of the Idle (else first) action's South strip, first
    /// frame. Reads the PNG directly (no "Read/Write" import flag needed) and scales with the sprite, so it's
    /// correct at any <c>worldScale</c>. Falls back to 0.45 (the old hand-tuned default) if it can't read.</summary>
    public static float GroundingFeetOffset(string outDir, IList<Action> actions)
    {
        string action = null;
        foreach (var a in actions)
            if (a.clip != null)
            {
                if (action == null) action = a.name;
                if (a.name == CharacterAction.Idle.ToString()) { action = a.name; break; }   // prefer Idle's stance
            }
        if (action == null) return 0.45f;

        string path = $"{outDir}/{Sanitize(action)}_S.png";
        if (!File.Exists(path)) return 0.45f;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(tex); return 0.45f; }

        int cell = tex.height;                 // square cell; the first frame is the leftmost cell
        var px = tex.GetPixels(0, 0, cell, cell);
        int lowest = -1;                       // Texture2D y=0 is the BOTTOM row, so this scans up from the floor
        for (int y = 0; y < cell && lowest < 0; y++)
            for (int x = 0; x < cell; x++)
                if (px[y * cell + x].a > 0.06f) { lowest = y; break; }
        Object.DestroyImmediate(tex);
        if (lowest < 0) return 0.45f;

        return 0.5f - lowest / (float)cell;
    }

    /// <summary>Load the 8 directional strips (as imported sprite textures) for an action from a bake folder.</summary>
    public static BakedBillboard8.Dir8 LoadDir8(string outDir, string actionName)
    {
        var d = new BakedBillboard8.Dir8 { strips = new Texture2D[8] };
        for (int i = 0; i < DirNames.Length; i++)
            d.strips[i] = AssetDatabase.LoadAssetAtPath<Texture2D>($"{outDir}/{Sanitize(actionName)}_{DirNames[i]}.png");
        return d;
    }
}
