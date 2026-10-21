using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a body as a target for the x-ray occlusion silhouette drawn by <see cref="OccludedSilhouetteFeature"/>:
/// the player always, and a creature for as long as <see cref="Show"/> says so (see <see cref="NetMob"/>). It flags
/// each child <see cref="Renderer"/> with dedicated <see cref="Renderer.renderingLayerMask"/> bits the feature
/// filters on — so only marked bodies are drawn, even though every unit shares the crowd physics <b>layer</b> (a
/// renderer's rendering-layer mask is independent of its GameObject layer, so physics is untouched).
///
/// <para>Two bits, because an equipped weapon must be treated differently from the body:
/// <list type="bullet">
/// <item><see cref="Bit"/> (PAINT) — the character BODY. Painted blue where it's hidden behind scene geometry.</item>
/// <item><see cref="CoverBit"/> (COVERAGE) — body AND held equipment. Marks "this pixel belongs to the player and
/// is visible here", so the body behind your OWN sword counts as visible and is NOT x-rayed. Equipment gets only
/// this bit, never <see cref="Bit"/> — so the weapon covers the body but is itself never drawn as a silhouette.</item>
/// </list>
/// Equipment is recognised by its authored <see cref="WeaponGrip"/> (present on every weapon prefab root). Effects
/// (particles, trails, lines) get neither bit: only mesh and skinned-mesh renderers are ever flagged.</para>
///
/// Applied at <see cref="Start"/> and re-applied by <see cref="WeaponSocket"/> whenever the weapon changes (the
/// weapon mounts AFTER this runs, and can be swapped at runtime), so a freshly-equipped model always gets flagged.
/// </summary>
[DisallowMultipleComponent]
public class OccludedSilhouetteTarget : MonoBehaviour
{
    /// <summary>PAINT bit — the body, drawn as the silhouette. Shared with <see cref="OccludedSilhouetteFeature"/>.</summary>
    public const uint Bit = 1u << 8;

    /// <summary>COVERAGE bit — body + equipment; marks the player's visible area so held gear doesn't x-ray the body.</summary>
    public const uint CoverBit = 1u << 9;

    static readonly List<Renderer> scratch = new List<Renderer>(16);

    // Whether this body is drawn through scenery at all. On unless somebody says otherwise, which is the player.
    bool shown = true;

    void Start() => Refresh();

    /// <summary>Start or stop drawing this body through scenery. Asked again with the same answer, it does nothing.</summary>
    public void Show(bool on)
    {
        if (shown == on) return;
        shown = on;
        Refresh();
    }

    /// <summary>Re-flag every child renderer. Call after a runtime model swap or equip change.</summary>
    public void Refresh()
    {
        GetComponentsInChildren(true, scratch);   // fills the shared buffer — no per-call alloc
        for (int i = 0; i < scratch.Count; i++)
        {
            var r = scratch[i];
            if (r == null) continue;
            // Only solid models take part, and a flat creature's own picture, which IS its body. An effect riding the
            // body — a weapon's enchant, an armour set's aura, any particle, trail, line or effect sprite — would be
            // painted as a flat silhouette slab, and would hide the body behind it.
            bool solid = r is SkinnedMeshRenderer || r is MeshRenderer
                      || (r is SpriteRenderer && r.GetComponent<BillboardAnimator3D>() != null);
            if (!shown || !solid) { r.renderingLayerMask &= ~(Bit | CoverBit); continue; }
            bool isEquipment = r.GetComponentInParent<WeaponGrip>() != null;
            if (isEquipment)
                r.renderingLayerMask = (r.renderingLayerMask & ~Bit) | CoverBit;   // covers the body, never painted
            else
                r.renderingLayerMask |= Bit | CoverBit;                            // the body: painted + self-covering
        }
    }
}
