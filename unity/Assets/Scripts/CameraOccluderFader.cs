using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fades any SOLID prop (house, tree, wall) that sits between the camera and the player so the player is
/// never hidden behind geometry. Each frame it sphere-casts from the camera toward the player; every
/// <see cref="MeshRenderer"/> the probe touches is swapped to a transparent clone of its material and its
/// alpha is eased down to <see cref="fadedAlpha"/>. When a prop is no longer in the way it eases back to
/// fully opaque and its original materials are restored.
///
/// PERF (see PERFORMANCE.md): steady-state allocation is ~0.
///   • ONE <see cref="Physics.SphereCastNonAlloc"/> per frame into a reused buffer (mirrors Pathfinder3D).
///   • Renderers are gathered into a reused <see cref="List{T}"/> (the no-alloc GetComponentsInChildren).
///   • Transparent materials are cloned ONCE per source material and cached; alpha is pushed through one
///     reused <see cref="MaterialPropertyBlock"/> (the CharacterDye pattern) — no per-frame material churn.
/// Only MeshRenderers fade, so the player/mob billboards (SpriteRenderers) and the ground are never touched.
/// </summary>
[DisallowMultipleComponent]
public class CameraOccluderFader : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("Who must stay visible. Auto-filled from the follow camera's target if left empty.")]
    public Transform target;
    [Tooltip("Which layers can occlude (and so get faded). Ground is excluded by default.")]
    public LayerMask blockMask = ~0;

    [Header("Fade")]
    [Range(0f, 1f)] public float fadedAlpha = 0.527f;  // how see-through an occluder gets
    public float fadeSpeed = 10f;                       // alpha units per second (in & out)

    [Header("Probe")]
    public float castRadius = 0f;      // thickness of the camera→player probe (0 = thin raycast)
    public float aimHeight  = 1.1f;    // aim above the target's feet (chest/head, not the floor)
    public float nearClip   = 0.4f;    // ignore hits right on the lens

    static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

    Camera cam;
    MaterialPropertyBlock mpb;
    readonly RaycastHit[] hits = new RaycastHit[32];
    readonly List<Renderer> probe = new List<Renderer>(8);          // reused per hit
    readonly Dictionary<Renderer, Entry> faded = new Dictionary<Renderer, Entry>();
    readonly Dictionary<Material, Material> transparentOf = new Dictionary<Material, Material>();
    readonly List<Material> created = new List<Material>();          // clones we own → destroy on disable
    readonly List<Renderer> toRemove = new List<Renderer>();        // reused

    class Entry { public Material[] original; public float alpha; public bool blocking; }

    void Reset()
    {
        int g = LayerMask.NameToLayer("Ground");
        blockMask = g >= 0 ? ~(1 << g) : ~0;   // everything except the floor
    }

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        cam = GetComponent<Camera>();
        if (blockMask == 0) blockMask = ~0;    // a "Nothing" mask would never fade anything
    }

    void LateUpdate()   // after CameraFollow3D has moved the camera
    {
        if (cam == null) { cam = GetComponent<Camera>() ?? Camera.main; if (cam == null) return; }
        if (target == null)
        {
            var f = CameraFollow3D.Main;
            if (f != null) target = f.target;
            if (target == null) return;
        }

        // Mark every currently-faded prop as "not blocking" — the probe below re-flags the ones still in the way.
        foreach (var e in faded.Values) e.blocking = false;

        Vector3 origin = cam.transform.position;
        Vector3 to = (target.position + Vector3.up * aimHeight) - origin;
        float dist = to.magnitude;
        if (dist > nearClip + 0.05f)
        {
            Vector3 dir = to / dist;
            int n = Physics.SphereCastNonAlloc(origin + dir * nearClip, castRadius, dir, hits,
                                               dist - nearClip, blockMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = hits[i].collider;
                if (col == null) continue;
                col.GetComponentsInChildren(false, probe);    // no-alloc fill of the reused list
                for (int r = 0; r < probe.Count; r++) Mark(probe[r]);
            }
        }

        // Ease everyone toward their target alpha; restore + drop the ones that are fully opaque again.
        toRemove.Clear();
        float step = fadeSpeed * Time.deltaTime;
        foreach (var kv in faded)
        {
            var rend = kv.Key; var e = kv.Value;
            if (rend == null) { toRemove.Add(rend); continue; }   // prop was destroyed mid-fade

            e.alpha = Mathf.MoveTowards(e.alpha, e.blocking ? fadedAlpha : 1f, step);
            if (!e.blocking && e.alpha >= 0.999f)
            {
                rend.SetPropertyBlock(null);
                rend.sharedMaterials = e.original;   // back to the real (opaque) assets
                toRemove.Add(rend);
            }
            else
            {
                rend.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorID, new Color(1f, 1f, 1f, e.alpha));
                rend.SetPropertyBlock(mpb);
            }
        }
        for (int i = 0; i < toRemove.Count; i++) faded.Remove(toRemove[i]);
    }

    // Flag a renderer as currently blocking; swap it to its transparent clone the first time we see it.
    void Mark(Renderer rend)
    {
        if (!(rend is MeshRenderer)) return;            // skip sprite billboards (player/mobs) & non-mesh fx
        if (target != null && rend.transform.IsChildOf(target)) return;

        if (faded.TryGetValue(rend, out var e)) { e.blocking = true; return; }

        var src = rend.sharedMaterials;
        var clone = new Material[src.Length];
        for (int i = 0; i < src.Length; i++) clone[i] = Transparent(src[i]);

        faded[rend] = new Entry { original = src, alpha = 1f, blocking = true };
        rend.sharedMaterials = clone;
    }

    // A cached, blend-mode clone of a source URP/Lit material (built once, shared by every occluder using it).
    Material Transparent(Material src)
    {
        if (src == null) return null;
        if (transparentOf.TryGetValue(src, out var m) && m != null) return m;

        m = new Material(src) { name = src.name + " (Fade)" };
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_Surface", 1f);            // URP: 0 = Opaque, 1 = Transparent
        m.SetFloat("_Blend", 0f);              // alpha blend
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_AlphaClip", 0f);          // foliage clones blend by texture alpha instead of clipping
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_Cull", 0f);               // double-sided so a faded house doesn't reveal hollow back faces
        m.renderQueue = (int)RenderQueue.Transparent;

        transparentOf[src] = m;
        created.Add(m);
        return m;
    }

    void OnDisable()   // restore everything and free our clones (scene swap / teardown)
    {
        foreach (var kv in faded)
            if (kv.Key != null) { kv.Key.SetPropertyBlock(null); kv.Key.sharedMaterials = kv.Value.original; }
        faded.Clear();

        for (int i = 0; i < created.Count; i++) if (created[i] != null) Destroy(created[i]);
        created.Clear();
        transparentOf.Clear();
    }
}
