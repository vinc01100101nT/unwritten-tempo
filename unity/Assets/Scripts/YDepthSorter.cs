using UnityEngine;

/// <summary>
/// Per-object top-down depth: sets sprite draw order from how far DOWN it is on screen, so a
/// lower object draws IN FRONT of a higher one. Deterministic and flicker-free (each object gets
/// a distinct integer order from its Y). Sorts by the BOTTOM of the sprite (its feet/base) — the
/// visually-correct ground-contact line — which is pivot-independent.
///
/// Two modes, picked automatically:
///   • SELF mode — there's a SpriteRenderer on THIS object (player, mob, NPC, single-tile prop):
///     sorts that one renderer by its own base.
///   • GROUP mode — no SpriteRenderer here, but there are child sprites (a multi-tile prop like a
///     tree/house whose tiles were grouped under one parent): treats the WHOLE group as one unit,
///     sorting every child by the LOWEST child's base. This is what stops a big tree's canopy from
///     popping in front of you when you walk under it — the sort line is the trunk's foot, so depth
///     only flips when you pass the base, not each leafy cell.
///
/// Tilemaps (ground/decor/overhead) and FX do NOT use this — they sit at fixed bands (see
/// <see cref="DepthSortRuntime"/>). Runs in edit mode too so the Scene view previews depth.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class YDepthSorter : MonoBehaviour
{
    [Tooltip("Extra world-units nudge on the sort line (the bottom/foot). Negative = treated as " +
             "standing slightly lower, so it draws in front a touch sooner. Usually 0.")]
    public float sortOffset = 0f;

    SpriteRenderer self;        // SELF mode (this object has a SpriteRenderer)
    SpriteRenderer[] group;     // GROUP mode (sort all child sprites as one unit)

    void Awake() => Cache();
    void OnEnable() => Cache();

    void Cache()
    {
        self = GetComponent<SpriteRenderer>();
        group = self == null ? GetComponentsInChildren<SpriteRenderer>(true) : null;
    }

    void LateUpdate()
    {
        // In the editor the hierarchy can change between frames — re-scan in GROUP mode so the
        // preview stays correct. At play time the cached arrays are stable.
        if (!Application.isPlaying && self == null) Cache();

        if (self != null)
        {
            int o = DepthSortRuntime.OrderForY(self.bounds.min.y + sortOffset);
            if (self.sortingOrder != o) self.sortingOrder = o;
            return;
        }

        if (group == null || group.Length == 0) return;

        float footY = float.MaxValue;
        foreach (var r in group) if (r != null) footY = Mathf.Min(footY, r.bounds.min.y);
        if (footY == float.MaxValue) return;

        int order = DepthSortRuntime.OrderForY(footY + sortOffset);
        foreach (var r in group)
            if (r != null && r.sortingOrder != order) r.sortingOrder = order;   // whole tree = one depth
    }
}
