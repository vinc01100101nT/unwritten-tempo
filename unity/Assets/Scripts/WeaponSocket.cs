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
    GameObject mountedPair; // the same weapon in the other hand, when it is wielded as a pair; otherwise null
    int bladesOut;          // how many of this body's thrown weapons are in the air — the hand is empty while any are

    // ---- Drawn or put away: what the server last said, and where each copy sits because of it ----
    DrawSaid said;          // default = Sheathed: a body nobody has spoken for yet starts at peace
    WeaponGrip grip;        // the mounted model's grip (hand place AND put-away place), or null
    Transform handBone, pairHandBone, stowBone;
    readonly float[] handFrac = new float[4];   // put away main/pair, take out main/pair — share of the clip
    bool mainInHand = true, pairInHand = true;
    bool placedOnce;
    // The flourish: while these shares of the put-away pass (and, backwards, of the take-out), the main copy turns flat
    // over the fist this many times, lifted this far off it.
    float spinFrom, spinTo, spinTurns, spinLift, shownSpinShare;
    // A weapon that turns about its own middle slides this far through the fist (hand bone's space) at full lift.
    Vector3 spinSlide;
    // Played where the weapon leaves and where it arrives when it changes place (the row's handoverVfx), or null.
    VfxSequence handoverVfx;

    /// <summary>Where a closed right fist grips, in the hand bone's space — the point every weapon model is fitted to
    /// (the import-weapon-models skill). A weapon turned over in the fingers turns about it.</summary>
    static readonly Vector3 FistPoint = new Vector3(0.011f, 0.18756053f, 0.12156433f);

    /// <summary>The hand of the person at this keyboard. Their model is a SEPARATE object riding their avatar, not a
    /// child of it, so a search under the avatar never finds this socket; the body builder names it here instead.</summary>
    public static WeaponSocket OnMyAvatar { get; private set; }

    /// <summary>Name <paramref name="socket"/> as the hand of the person at this keyboard — called by the builder of
    /// their model each time it builds one.</summary>
    public static void MarkMine(WeaponSocket socket)
    {
        OnMyAvatar = socket;
        // Their model is rebuilt on its own schedule; it starts from what the server last said, not from peace.
        var me = NetPlayer.Mine;
        if (socket != null && me != null) socket.ShowDraw(me.CurrentDraw);
    }

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

    void ApplyFlights()
    {
        bool canRest = grip != null && grip.stowAuthored;   // a weapon with nowhere to rest is hidden while put away
        if (mounted != null) mounted.SetActive(bladesOut == 0 && (mainInHand || canRest));
        if (mountedPair != null) mountedPair.SetActive(bladesOut == 0 && (pairInHand || canRest));
    }

    /// <summary>The server's word on this body's weapon: on the body, coming out, in hand, or going back. Each copy
    /// changes place at its hand's frame of the take-out or put-away clip, worked out from the shared clock.</summary>
    public void ShowDraw(DrawSaid now) { said = now; ApplyDraw(); }

    void Update()
    {
        if (said.Phase == DrawPhase.Drawing || said.Phase == DrawPhase.Sheathing) ApplyDraw();
    }

    void ApplyDraw()
    {
        var phase = said.Phase;
        float progress = said.Progress(Mirror.NetworkTime.time);
        var pose = anim != null ? anim.GetComponent<Character3DPose>() : null;
        if (pose != null) pose.SetDraw(phase, progress);
        bool main = InHand(phase, progress, 0), pair = InHand(phase, progress, 1);
        if (!placedOnce || main != mainInHand || pair != pairInHand)
        {
            // a weapon seen changing place mid-move (not one placed for the first time) shows its handover effect
            bool handover = placedOnce && main != mainInHand && (phase == DrawPhase.Drawing || phase == DrawPhase.Sheathing);
            var from = mounted != null ? mounted.transform.position : Vector3.zero;
            placedOnce = true;
            mainInHand = main; pairInHand = pair;
            Place(mounted, mainInHand, false);
            Place(mountedPair, pairInHand, true);
            if (handover && handoverVfx != null && mounted != null && !Application.isBatchMode)
                VfxDirector.Play(handoverVfx, new VfxContext { origin = from, target = mounted.transform.position, scaleMult = 1f });
            shownSpinShare = 0f;
            ApplyFlights();
        }
        float share = mainInHand ? SpinShare(phase, progress) : 0f;
        if (share == shownSpinShare) return;
        shownSpinShare = share;
        if (mounted == null || grip == null || mounted.transform.parent != handBone) return;
        SpunOverFist(grip, share, spinTurns, spinLift, spinSlide, out var pos, out var rot);
        mounted.transform.localPosition = pos; mounted.transform.localRotation = rot;
    }

    /// <summary>How far through the put-away's spin window this body's weapon is (0..1); 0 outside it or with no spin.
    /// The take-out is the put-away played backwards, so it is asked where the put-away would be.</summary>
    float SpinShare(DrawPhase phase, float progress)
    {
        if (spinTurns == 0f) return 0f;
        if (phase == DrawPhase.Sheathing) return WeaponDrawMath.SpinWindowShare(progress, spinFrom, spinTo);
        if (phase == DrawPhase.Drawing) return WeaponDrawMath.SpinWindowShare(1f - progress, spinFrom, spinTo);
        return 0f;
    }

    /// <summary>The put-away's spin window of a weapon's stance row, as shares of the put-away clip (0..1). False when
    /// the row asks for no spin.</summary>
    public static bool SpinWindow(WeaponStanceBinding b, float sheatheFrames, out float from, out float to)
    {
        from = to = 0f;
        if (b == null || b.spinTurns == 0f || b.spinEndFrame <= b.spinStartFrame || b.spinStartFrame < 0 || sheatheFrames <= 0f) return false;
        from = Mathf.Clamp01(b.spinStartFrame / sheatheFrames); to = Mathf.Clamp01(b.spinEndFrame / sheatheFrames);
        return true;
    }

    /// <summary>Where a spun weapon sits in its hand bone at <paramref name="windowShare"/> of its spin window (0 = at
    /// its grip).</summary>
    public static void SpunOverFist(WeaponGrip grip, float windowShare, float turns, float lift, out Vector3 localPosition, out Quaternion localRotation)
        => SpunOverFist(grip, windowShare, turns, lift, Vector3.zero, out localPosition, out localRotation);

    /// <summary>The same, for a weapon that turns about its own middle: it slides <paramref name="slide"/> (hand bone's
    /// space, see <see cref="MiddleSlide"/>) through the fist as it lifts, and that slide turns with it.</summary>
    public static void SpunOverFist(WeaponGrip grip, float windowShare, float turns, float lift, Vector3 slide, out Vector3 localPosition, out Quaternion localRotation)
    {
        float degrees = WeaponDrawMath.SpinTurnShare(windowShare) * turns * 360f, up = WeaponDrawMath.SpinLiftShare(windowShare);
        TurnedOverFist(grip, degrees, up * lift, out localPosition, out localRotation);
        if (slide == Vector3.zero) return;
        localPosition += Quaternion.AngleAxis(grip.hand == WeaponHand.Left ? -degrees : degrees, Vector3.right) * (slide * up);
    }

    /// <summary>How far a weapon held at its grip must slide along its length, in the hand bone's space, for the middle
    /// of its model to sit where the fist closes. <paramref name="modelMiddle"/> is the model's middle in the space of
    /// the model's own root.</summary>
    public static Vector3 MiddleSlide(Vector3 gripPosition, Vector3 gripEuler, Vector3 gripScale, bool leftHand, Vector3 modelMiddle)
    {
        var rot = Quaternion.Euler(gripEuler);
        var middle = gripPosition + rot * Vector3.Scale(gripScale, modelMiddle);
        var pivot = leftHand ? new Vector3(-FistPoint.x, FistPoint.y, FistPoint.z) : FistPoint;
        var along = (rot * Vector3.up).normalized;
        return -Vector3.Dot(middle - pivot, along) * along;
    }

    /// <summary>Where a weapon sits in its hand bone when it is turned <paramref name="degrees"/> flat over the fist and
    /// lifted <paramref name="lift"/> off it: turned about the hand's own x through the point where the fist closes, so
    /// the blade sweeps in the plane of the forearm and the thumb, and lifted along that same x. 0 and 0 give the grip.</summary>
    public static void TurnedOverFist(WeaponGrip grip, float degrees, float lift, out Vector3 localPosition, out Quaternion localRotation)
    {
        bool left = grip.hand == WeaponHand.Left;
        var pivot = left ? new Vector3(-FistPoint.x, FistPoint.y, FistPoint.z) : FistPoint;
        var q = Quaternion.AngleAxis(left ? -degrees : degrees, Vector3.right);
        localPosition = pivot + q * (grip.localPosition - pivot) + new Vector3(left ? -lift : lift, 0f, 0f);
        localRotation = q * Quaternion.Euler(grip.localEuler);
    }

    bool InHand(DrawPhase phase, float progress, int copy)
    {
        switch (phase)
        {
            case DrawPhase.Drawn: return true;
            case DrawPhase.Drawing: return progress >= handFrac[2 + copy];
            case DrawPhase.Sheathing: return progress < handFrac[copy];
            default: return false;
        }
    }

    /// <summary>Put one copy in its hand, or on its resting place on the body — at the same size either way.</summary>
    void Place(GameObject copy, bool inHand, bool isPair)
    {
        if (copy == null) return;
        var hand = isPair ? pairHandBone : handBone;
        var t = copy.transform;
        var pos = grip != null ? grip.localPosition : Vector3.zero;
        var rot = grip != null ? Quaternion.Euler(grip.localEuler) : Quaternion.identity;
        var scale = grip != null ? grip.localScale : Vector3.one;
        if (isPair) { pos.x = -pos.x; rot = new Quaternion(rot.x, -rot.y, -rot.z, rot.w); scale.x = -scale.x; }
        if (inHand || stowBone == null)
        {
            if (hand != null && t.parent != hand) t.SetParent(hand, false);
            t.localPosition = pos; t.localRotation = rot; t.localScale = scale;
            return;
        }
        t.SetParent(stowBone, false);
        t.localPosition = isPair ? grip.stowPairPosition : grip.stowPosition;
        t.localRotation = Quaternion.Euler(isPair ? grip.stowPairEuler : grip.stowEuler);
        // The resting bone may carry a different scale from the hand; the weapon keeps its size in the world.
        var h = hand != null ? hand.lossyScale : Vector3.one; var b = stowBone.lossyScale;
        t.localScale = new Vector3(scale.x * Ratio(h.x, b.x), scale.y * Ratio(h.y, b.y), scale.z * Ratio(h.z, b.z));
    }

    static float Ratio(float a, float b) => Mathf.Abs(b) > 1e-6f ? a / b : 1f;

    /// <summary>Each hand's handover moment as a share of its clip (0..1), from the weapon's binding: an unset frame
    /// keeps the weapon in hand for the whole move. Worked out when the weapon changes, never per frame.</summary>
    void ResolveHandovers(WeaponType type, bool mainIsRight)
    {
        handFrac[0] = handFrac[1] = 1f;   // put away: let go at the end
        handFrac[2] = handFrac[3] = 0f;   // take out: in hand from the start
        spinTurns = spinLift = spinFrom = spinTo = 0f;
        spinSlide = Vector3.zero; handoverVfx = null;
        var b = StanceCatalog.Weapon(type);
        if (b == null) return;
        handoverVfx = b.handoverVfx;
        var model = shown != null ? shown.equipModel : null;
        var mesh = model != null ? model.GetComponentInChildren<MeshFilter>() : null;   // perf-ok: on weapon change
        if (b.spinAboutMiddle && grip != null && mesh != null && mesh.sharedMesh != null)
            spinSlide = MiddleSlide(grip.localPosition, grip.localEuler, grip.localScale, grip.hand == WeaponHand.Left,
                                    model.transform.InverseTransformPoint(mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center)));
        float sheatheFrames = ClipFrames(b.sheatheClip), unsheatheFrames = ClipFrames(b.unsheatheClip);
        if (SpinWindow(b, sheatheFrames, out spinFrom, out spinTo)) { spinTurns = b.spinTurns; spinLift = b.spinLift; }
        int sMain = mainIsRight ? b.sheatheRightFrame : b.sheatheLeftFrame, sPair = mainIsRight ? b.sheatheLeftFrame : b.sheatheRightFrame;
        int uMain = mainIsRight ? b.unsheatheRightFrame : b.unsheatheLeftFrame, uPair = mainIsRight ? b.unsheatheLeftFrame : b.unsheatheRightFrame;
        if (sheatheFrames > 0f && sMain >= 0) handFrac[0] = Mathf.Clamp01(sMain / sheatheFrames);
        if (sheatheFrames > 0f && sPair >= 0) handFrac[1] = Mathf.Clamp01(sPair / sheatheFrames);
        if (unsheatheFrames > 0f && uMain >= 0) handFrac[2] = Mathf.Clamp01(uMain / unsheatheFrames);
        if (unsheatheFrames > 0f && uPair >= 0) handFrac[3] = Mathf.Clamp01(uPair / unsheatheFrames);
    }

    float ClipFrames(string clipName)
    {
        if (string.IsNullOrEmpty(clipName) || anim == null || anim.runtimeAnimatorController == null) return 0f;
        foreach (var c in anim.runtimeAnimatorController.animationClips)   // perf-ok: on weapon change
            if (c != null && c.name == clipName) return c.length * c.frameRate;
        return 0f;
    }

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
        if (mountedPair != null) { Destroy(mountedPair); mountedPair = null; }
        shown = weapon;
        grip = null; handBone = pairHandBone = stowBone = null;
        placedOnce = false;
        // The arms follow the weapon's TYPE, model or not — the same way the swing is picked.
        var pose = anim != null ? anim.GetComponent<Character3DPose>() : null;
        if (pose != null) pose.SetHeldWeapon(weapon != null ? weapon.weaponType : WeaponType.None);

        var model = weapon != null ? weapon.equipModel : null;
        if (model == null) { ApplyDraw(); return; }

        grip = model.GetComponent<WeaponGrip>();
        var hand = HandSocketResolver.Find(anim, grip != null ? grip.hand : WeaponHand.Right);
        if (hand == null) { GameLog.Warn(LogCat.Unwritten, $"WeaponSocket: no hand bone found on '{name}' for '{weapon.displayName}'."); return; }

        var pos = grip != null ? grip.localPosition : Vector3.zero;
        var rot = grip != null ? Quaternion.Euler(grip.localEuler) : Quaternion.identity;
        var scale = grip != null ? grip.localScale : Vector3.one;
        mounted = Mount(model, hand, pos, rot, scale);
        handBone = hand;
        if (grip != null && grip.stowAuthored && anim != null && anim.isHuman) stowBone = anim.GetBoneTransform(grip.stowBone);
        ResolveHandovers(weapon.weaponType, grip == null || grip.hand == WeaponHand.Right);

        // A pair weapon fills the other hand too, with the same model: one item equipped, both hands armed. The two
        // hand bones are mirror images of each other across their local x, so the other hand's copy is the grip
        // mirrored across x — position, turn and the mesh itself (a negative x scale), so an axe's edge faces the
        // mirrored way rather than backwards.
        if (WeaponTypes.IsDualWield(weapon.weaponType))
        {
            var otherSide = grip != null && grip.hand == WeaponHand.Left ? WeaponHand.Right : WeaponHand.Left;
            var other = HandSocketResolver.Find(anim, otherSide);
            pairHandBone = other;
            if (other != null)
                mountedPair = Mount(model, other, new Vector3(-pos.x, pos.y, pos.z),
                                    new Quaternion(rot.x, -rot.y, -rot.z, rot.w), new Vector3(-scale.x, scale.y, scale.z));
        }

        // The weapon mounts after OccludedSilhouetteTarget already flagged the body, so re-flag now: the new gear
        // gets the coverage bit (so it hides the body behind it) but never the paint bit (it must not x-ray itself).
        var silhouette = GetComponentInParent<OccludedSilhouetteTarget>();
        if (silhouette != null) silhouette.Refresh();

        // A weapon swapped while a blade is in the air mounts empty-handed, like the one it replaced; and the new
        // weapon goes straight to wherever the server says this body's weapon is.
        ApplyDraw();
    }

    GameObject Mount(GameObject model, Transform hand, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var m = Instantiate(model, hand, false);
        m.name = model.name;
        m.hideFlags = HideFlags.DontSaveInEditor;   // runtime child of the SAVED player rig — needs its own tag
        var t = m.transform;
        t.localPosition = pos; t.localRotation = rot; t.localScale = scale;
        return m;
    }
}
