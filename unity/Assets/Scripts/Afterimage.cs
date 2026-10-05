using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Frozen copies of a character, left behind at the spots it just was, fading out — the "several of me at once" read
/// a fast relocation should have. Pooled and shared: ANY unit can leave one (a dashing player, a blinking mob), so
/// this is a general presentation service, not one skill's trick.
///
/// <para><b>Why a baked mesh and not a cloned rig.</b> A ghost must be a SNAPSHOT — the pose at one instant, standing
/// still while the real body moves on. Cloning the character would give a second animated rig that keeps moving, plus
/// its Animator, controller and scripts. Instead each skinned renderer is asked to bake its CURRENT deformed shape
/// into a plain mesh, which is then drawn once and never updated. One-off cost per ghost, zero per-frame work beyond
/// fading a colour.</para>
///
/// <para><b>Copy, never re-derive.</b> The ghost takes the source renderer's resolved world position and rotation
/// verbatim, and applies NO scale of its own — the bake already delivers the body at its real size. Measuring the
/// character and re-fitting it, or re-applying a scale the source had already accounted for, is the exact mistake
/// that made a cloned body render ten times too large twice in this project, and it caught this class a third
/// time.</para>
///
/// <para><b>Never serialized.</b> The pool host and every ghost carry <see cref="HideFlags.DontSaveInEditor"/>, so a
/// ghost created in edit mode can't be baked into a scene and re-spawn as a phantom on every load.</para>
/// </summary>
public static class Afterimage
{
    // One ghost = one baked mesh + the renderer drawing it. Meshes are reused across ghosts (BakeMesh overwrites),
    // so a long run of dashes allocates nothing after the pool has warmed.
    class Ghost
    {
        public GameObject go;
        public MeshFilter filter;
        public MeshRenderer renderer;
        public Mesh mesh;
        public float born, life;
        public Color tint;
    }

    static Transform host;
    static Material ghostMat;
    static readonly Stack<Ghost> pool = new Stack<Ghost>(16);
    static readonly List<Ghost> live = new List<Ghost>(16);
    // Reused so capturing costs no garbage on the combat path — a dance can capture dozens of times in a few seconds.
    static readonly List<SkinnedMeshRenderer> sourceScratch = new List<SkinnedMeshRenderer>(8);
    static MaterialPropertyBlock mpb;

    static readonly int ColorId = Shader.PropertyToID("_BaseColor");

    /// <summary>Leave a fading copy of <paramref name="body"/> exactly where it stands right now. No-op when the body
    /// has no skinned renderers (a billboard mob has nothing to snapshot) — silently doing nothing is right here: a
    /// missing ghost is a cosmetic absence, never a gameplay change.</summary>
    /// <param name="seconds">How long the copy takes to fade away.</param>
    /// <param name="tint">Colour of the copy, alpha = how solid it starts.</param>
    public static void Capture(Transform body, float seconds, Color tint)
    {
        if (body == null || seconds <= 0f) return;
        body.GetComponentsInChildren(false, sourceScratch);   // false = skip disabled; a hidden part shouldn't ghost
        for (int i = 0; i < sourceScratch.Count; i++)
        {
            var src = sourceScratch[i];
            if (src == null || src.sharedMesh == null) continue;
            var g = Rent();
            src.BakeMesh(g.mesh);   // the deformed shape AT THIS INSTANT

            var t = g.go.transform;
            // Position and rotation come verbatim from the source; SCALE MUST NOT. The bake already delivers the body
            // at its real world size — measured on this character, whose renderer sits at scale 10.35: the baked mesh
            // came out 0.32 across against a 0.35 body on screen, i.e. 1:1, not 1:10.35. Re-applying that scale here
            // made every ghost ten times too big. Nothing about the source's size is recomputed — it is simply already
            // baked in, and the host sits at identity so local == world.
            t.SetPositionAndRotation(src.transform.position, src.transform.rotation);
            t.localScale = Vector3.one;

            g.filter.sharedMesh = g.mesh;
            // A renderer draws submesh i with material i — one material against a multi-submesh body would silently
            // draw only the first piece. The array is rebuilt only when the count actually changes.
            if (g.renderer.sharedMaterials.Length != g.mesh.subMeshCount)
            {
                var mats = new Material[Mathf.Max(1, g.mesh.subMeshCount)];
                for (int m = 0; m < mats.Length; m++) mats[m] = EnsureMaterial();
                g.renderer.sharedMaterials = mats;
            }
            g.born = Time.time; g.life = seconds; g.tint = tint;
            g.go.SetActive(true);
            Fade(g, 0f);
            live.Add(g);
        }
        sourceScratch.Clear();
        Ticker.Ensure();
    }

    // Fade by ELAPSED FRACTION rather than a per-ghost timer field, so a ghost that spawns while another is halfway
    // through is not affected by it — every copy runs its own life from its own birth.
    static void Fade(Ghost g, float t01)
    {
        var c = g.tint;
        c.a = g.tint.a * (1f - Mathf.Clamp01(t01));
        mpb.SetColor(ColorId, c);
        g.renderer.SetPropertyBlock(mpb);
    }

    static void Step()
    {
        float now = Time.time;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var g = live[i];
            // Its scene closed under it, and the host with every copy went too (MapManager.DestroyWhatOutlivedItsScene).
            if (g.go == null) { live.RemoveAt(i); continue; }
            float t01 = g.life <= 0f ? 1f : (now - g.born) / g.life;
            if (t01 >= 1f)
            {
                g.go.SetActive(false);
                live.RemoveAt(i);
                pool.Push(g);
                continue;
            }
            Fade(g, t01);
        }
    }

    static Ghost Rent()
    {
        EnsureHost();
        if (pool.Count > 0) return pool.Pop();

        var go = new GameObject("Afterimage") { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.SetParent(host, false);
        var g = new Ghost
        {
            go = go,
            filter = go.AddComponent<MeshFilter>(),
            renderer = go.AddComponent<MeshRenderer>(),
            mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave },
        };
        g.mesh.MarkDynamic();   // re-baked every reuse
        g.renderer.sharedMaterial = EnsureMaterial();
        g.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        g.renderer.receiveShadows = false;
        g.renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        return g;
    }

    static void EnsureHost()
    {
        if (host != null) return;
        pool.Clear(); live.Clear();
        // DontSaveInEditor: a RUNTIME-ONLY pool host must never be serialized into a scene (the phantom-clone trap).
        host = new GameObject("AfterimageSystem") { hideFlags = HideFlags.DontSaveInEditor }.transform;
        host.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        if (mpb == null) mpb = new MaterialPropertyBlock();
    }

    // Unlit and transparent on purpose: a ghost is a light-up silhouette, not a second lit character competing with
    // the real one. Unlit also means no shadow, no probe and no light loop per copy — a dozen of them cost nothing.
    static Material EnsureMaterial()
    {
        if (ghostMat != null) return ghostMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        ghostMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        // URP's Unlit needs its surface flipped to Transparent explicitly — the keywords and render queue are what
        // actually decide blending, not the colour's alpha.
        ghostMat.SetFloat("_Surface", 1f);                 // 0 = Opaque, 1 = Transparent
        ghostMat.SetFloat("_Blend", 0f);                   // Alpha blend
        ghostMat.SetFloat("_ZWrite", 0f);
        ghostMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        ghostMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        ghostMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        ghostMat.DisableKeyword("_ALPHATEST_ON");
        ghostMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return ghostMat;
    }

    /// <summary>Drops every ghost and the pool. Called on a scene change so nothing survives holding a stale mesh.</summary>
    public static void Clear()
    {
        for (int i = 0; i < live.Count; i++) if (live[i]?.go != null) live[i].go.SetActive(false);
        live.Clear();
        pool.Clear();
        if (host != null) Object.Destroy(host.gameObject);
        host = null;
    }

    // The fade needs a per-frame beat, and a static class has none. One hidden driver, created with the pool and
    // living exactly as long as it does — cheaper and less error-prone than every caller remembering to pump it.
    class Ticker : MonoBehaviour
    {
        static Ticker instance;
        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("AfterimageTicker") { hideFlags = HideFlags.DontSaveInEditor };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Ticker>();
        }
        void LateUpdate() => Step();   // LATE: after the animator has posed the rig, so a same-frame bake is current
    }
}
