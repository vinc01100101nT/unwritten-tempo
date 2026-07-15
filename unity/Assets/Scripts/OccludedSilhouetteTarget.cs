using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a character (the player) as a target for the x-ray occlusion silhouette drawn by
/// <see cref="OccludedSilhouetteFeature"/>. It flags each child <see cref="Renderer"/> with dedicated
/// <see cref="Renderer.renderingLayerMask"/> bits the feature filters on — so the silhouette is PLAYER-ONLY even
/// though the player shares the crowd physics <b>layer</b> with the mobs (a renderer's rendering-layer mask is
/// independent of its GameObject layer, so physics is untouched).
///
/// <para>Two bits, because an equipped weapon must be treated differently from the body:
/// <list type="bullet">
/// <item><see cref="Bit"/> (PAINT) — the character BODY. Painted blue where it's hidden behind scene geometry.</item>
/// <item><see cref="CoverBit"/> (COVERAGE) — body AND held equipment. Marks "this pixel belongs to the player and
/// is visible here", so the body behind your OWN sword counts as visible and is NOT x-rayed. Equipment gets only
/// this bit, never <see cref="Bit"/> — so the weapon covers the body but is itself never drawn as a silhouette.</item>
/// </list>
/// Equipment is recognised by its authored <see cref="WeaponGrip"/> (present on every weapon prefab root).</para>
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

    void Start() => Refresh();

    /// <summary>Re-flag every child renderer. Call after a runtime model swap or equip change.</summary>
    public void Refresh()
    {
        GetComponentsInChildren(true, scratch);   // fills the shared buffer — no per-call alloc
        for (int i = 0; i < scratch.Count; i++)
        {
            var r = scratch[i];
            if (r == null) continue;
            bool isEquipment = r.GetComponentInParent<WeaponGrip>() != null;
            if (isEquipment)
                r.renderingLayerMask = (r.renderingLayerMask & ~Bit) | CoverBit;   // covers the body, never painted
            else
                r.renderingLayerMask |= Bit | CoverBit;                            // the body: painted + self-covering
        }
    }
}
