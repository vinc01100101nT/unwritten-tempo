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
    UnitEquipment gear;     // the worn set this body shows, when it is not the person at this keyboard's
    Item shown;             // the weapon def whose model is currently mounted (null = nothing shown)
    GameObject mounted;     // the live instantiated weapon, or null
    int bladesOut;          // how many of this body's thrown weapons are in the air — the hand is empty while any are

    /// <summary>The hand of the person at this keyboard. Their model is a SEPARATE object riding their avatar, not a
    /// child of it, so a search under the avatar never finds this socket; the body builder names it here instead.</summary>
    public static WeaponSocket OnMyAvatar { get; private set; }

    /// <summary>Name <paramref name="socket"/> as the hand of the person at this keyboard — called by the builder of
    /// their model each time it builds one.</summary>
    public static void MarkMine(WeaponSocket socket) => OnMyAvatar = socket;

    /// <summary>The hand that holds the weapon of the body <paramref name="drawnBody"/> — a socket under it (another
    /// person's body, a creature's), or the hand of the person at this keyboard when it is their avatar.</summary>
    public static WeaponSocket Of(Transform drawnBody)
    {
        if (drawnBody == null) return null;
        var under = drawnBody.GetComponentInChildren<WeaponSocket>();   // perf-ok: once per throw seen
        if (under != null) return under;
        var me = Character.Instance;
        return me != null && me.Body == drawnBody ? OnMyAvatar : null;
    }

    /// <summary>The model the hand is showing (its prefab), or null — what a thrown weapon's flight draws.</summary>
    public GameObject ShownModel => shown != null ? shown.equipModel : null;

    /// <summary>The weapon in the hand as it is mounted, or null — its size is what the flying copy is drawn at.</summary>
    public Transform Mounted => mounted != null ? mounted.transform : null;

    /// <summary>One of this body's thrown weapons has left the hand. The hand stays empty until every one is back.</summary>
    public void HideForFlight() { bladesOut++; ApplyFlights(); }

    /// <summary>One of this body's thrown weapons is back (or its flight ended without coming back).</summary>
    public void ShowAfterFlight() { if (bladesOut > 0) bladesOut--; ApplyFlights(); }

    /// <summary>Nothing of this body's is in the air any more — a body banked or rebuilt starts with its weapon in hand.</summary>
    public void ResetFlights() { bladesOut = 0; ApplyFlights(); }

    void ApplyFlights() { if (mounted != null) mounted.SetActive(bladesOut == 0); }

    void Awake()
    {
        if (anim == null) anim = GetComponentInChildren<Animator>();
    }

    /// <summary>Show the weapon <paramref name="worn"/> holds instead of the player's — a creature's 3D body. Redrawn
    /// whenever that worn set changes, a new life included. Call before this body's first frame.</summary>
    public void ShowWeaponOf(UnitEquipment worn)
    {
        if (gear != null) gear.OnChanged -= Refresh;
        gear = worn;
        if (gear != null) gear.OnChanged += Refresh;
        Refresh();
    }

    void Start()
    {
        if (gear != null) return;   // told whose weapon to show; the player's is not this body's
        // Character.Instance is up by Start (same contract Character3DVisual relies on for its own subscribe).
        subscribed = Character.Instance;
        if (subscribed != null) subscribed.OnStatsChanged += Refresh;
        Refresh();
    }

    void OnDestroy()
    {
        if (subscribed != null) subscribed.OnStatsChanged -= Refresh;
        if (gear != null) gear.OnChanged -= Refresh;
    }

    void Refresh()
    {
        var weapon = gear != null ? WeaponReach.WeaponOf(gear)
                   : Character.Instance != null ? Character.Instance.GetEquipped(EquipSlotType.Weapon) : null;
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

        // A weapon swapped while a blade is in the air mounts empty-handed, like the one it replaced.
        ApplyFlights();
    }
}
