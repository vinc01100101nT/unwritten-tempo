using UnityEngine;

/// <summary>
/// Mounts the equipped weapon's 3D model in the character's hand and keeps it in sync with what's equipped. Lives on
/// the live-3D visual (beside the <see cref="Animator"/> + <see cref="Character3DPose"/>); on equip changes it reads
/// <see cref="Character"/>'s Weapon slot and (re)parents that <see cref="Item.equipModel"/> under the hand bone with
/// its authored <see cref="WeaponGrip"/>. Once parented, the skinned Humanoid rig carries the weapon through every
/// animation frame — swings, casts, idle sway — with no per-frame code here.
///
/// <para>All work happens on the (rare) equipment-changed event, never per frame. The mounted instance is cached and
/// only rebuilt when the equipped weapon def actually changes, so re-firing <c>OnStatsChanged</c> (e.g. a level-up)
/// costs one ref compare.</para>
/// </summary>
[DefaultExecutionOrder(50)]
public class WeaponSocket : MonoBehaviour
{
    [Tooltip("The rig whose hand holds the weapon. Auto-found (this object or a child) if empty.")]
    public Animator anim;

    Character subscribed;
    Item shown;             // the weapon def whose model is currently mounted (null = nothing shown)
    GameObject mounted;     // the live instantiated weapon, or null

    void Awake()
    {
        if (anim == null) anim = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        // Character.Instance is up by Start (same contract Character3DVisual relies on for its own subscribe).
        subscribed = Character.Instance;
        if (subscribed != null) subscribed.OnStatsChanged += Refresh;
        Refresh();
    }

    void OnDestroy()
    {
        if (subscribed != null) subscribed.OnStatsChanged -= Refresh;
    }

    void Refresh()
    {
        var weapon = Character.Instance != null ? Character.Instance.GetEquipped(EquipSlotType.Weapon) : null;
        // Same weapon still equipped → nothing to rebuild (the common case: OnStatsChanged fired for level/stats).
        if (weapon == shown) return;
        Remount(weapon);
    }

    void Remount(Item weapon)
    {
        if (mounted != null) { Destroy(mounted); mounted = null; }
        shown = weapon;

        var model = weapon != null ? weapon.equipModel : null;
        if (model == null) return;

        var grip = model.GetComponent<WeaponGrip>();
        var hand = HandSocketResolver.Find(anim, grip != null ? grip.hand : WeaponHand.Right);
        if (hand == null) { GameLog.Warn(LogCat.Unwritten, $"WeaponSocket: no hand bone found on '{name}' for '{weapon.displayName}'."); return; }

        mounted = Instantiate(model, hand, false);
        mounted.name = model.name;
        mounted.hideFlags = HideFlags.DontSaveInEditor;   // runtime child of the SAVED player rig — needs its own tag
        var t = mounted.transform;
        t.localPosition = grip != null ? grip.localPosition : Vector3.zero;
        t.localRotation = grip != null ? Quaternion.Euler(grip.localEuler) : Quaternion.identity;
        t.localScale    = grip != null ? grip.localScale : Vector3.one;

        // The weapon mounts after OccludedSilhouetteTarget already flagged the body, so re-flag now: the new gear
        // gets the coverage bit (so it hides the body behind it) but never the paint bit (it must not x-ray itself).
        var silhouette = GetComponentInParent<OccludedSilhouetteTarget>();
        if (silhouette != null) silhouette.Refresh();
    }
}
