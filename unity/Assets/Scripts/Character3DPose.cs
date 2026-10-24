using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The 3D presentation SINK: implements <see cref="IStancePose"/> by driving an <see cref="Animator"/> (and
/// yawing the model) — the sole stance sink for players + townsfolk. <see cref="StanceController"/> pushes
/// the resolved <see cref="CharacterAction"/> + speed here every frame; locomotion (Idle/Walk/Run) blends via a
/// 1-D <c>Speed</c> tree, one-shots (Attack/Jump/Roll/Hurt/PickUp/Die/Cast/Sit) CrossFade to their own state.
///
/// <para>Because StanceController drives this directly, there's no second "what action?" decision here — it only
/// renders. Each drive carries the authored CLIP NAME (owned by the Stance Composer — there is NO hardcoded
/// action→clip map); one-shot states are named by that clip, so the pose CrossFades to the state named exactly
/// like the clip. An empty/unknown clip → the character just rests (locomotion idle), never a substituted clip.
/// No per-frame allocation: state hashes + clip lengths cached by name in Awake.</para>
/// </summary>
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(0)]
public class Character3DPose : MonoBehaviour, IStancePose
{
    /// <summary>Which locomotion gait the pose is currently driving, decided PURELY by the clip pushed in via
    /// <see cref="Play"/> (Walk clip → Walk, Run clip → Run, Sprint clip → Sprint, anything else → None). The footstep
    /// detector reads this to pick the authored L/R plants of the gait that's actually playing — no move-speed guess.</summary>
    public enum LocoGait { None, Walk, Run, Sprint }
    public LocoGait Gait { get; private set; }

    [Tooltip("How fast the model yaws to face its travel/aim direction (deg/sec).")]
    public float turnSpeed = 900f;
    [Tooltip("Seconds to ease the locomotion Speed blend (Idle↔Walk↔Run).")]
    public float speedSmooth = 0.1f;
    [Tooltip("CrossFade time into a one-shot state (Attack/Jump/…).")]
    public float crossFade = 0.08f;
    [Tooltip("Clip names sourced from Mixamo (set by 'Use 3D Caster'). Lets grounding tweaks target ONLY Mixamo " +
             "one-shots and leave the model's own Quaternius clips alone. See Character3DVisual.mixamoFootOffsetY.")]
    public List<string> mixamoClipNames = new List<string>();

    Animator anim;
    static readonly int Loco = Animator.StringToHash("Locomotion");
    static readonly int PSpeed = Animator.StringToHash("Speed");

    // Cached BY CLIP NAME: which clips actually have a one-shot state (+ its hash) and every clip's length. The
    // builder names each one-shot state after its clip, so the composer's clip string is the CrossFade key.
    readonly Dictionary<string, int> stateHash = new Dictionary<string, int>();
    readonly Dictionary<string, float> clipLen = new Dictionary<string, float>();
    readonly Dictionary<string, int> clipFrames = new Dictionary<string, int>();
    readonly HashSet<string> mixamoNameSet = new HashSet<string>();
    readonly List<AnimatorClipInfo> clipInfoBuf = new List<AnimatorClipInfo>();

    /// <summary>True while the DOMINANT clip currently playing (highest blend weight) came from Mixamo. Scoped by the
    /// actual clip, not the state name, because controller states are named by ACTION and a Mixamo clip can play from
    /// a one-shot state OR the locomotion blend tree. Lets the visual apply a Mixamo-only grounding offset without
    /// touching the model's own Quaternius clips. No alloc: fills a reused buffer.</summary>
    public bool CurrentIsMixamo
    {
        get
        {
            if (mixamoNameSet.Count == 0 || anim == null) return false;
            anim.GetCurrentAnimatorClipInfo(0, clipInfoBuf);
            float best = 0f; string bestName = null;
            for (int i = 0; i < clipInfoBuf.Count; i++)
            {
                var ci = clipInfoBuf[i];
                if (ci.clip == null) continue;
                if (ci.weight > best) { best = ci.weight; bestName = Clean(ci.clip.name); }
            }
            return bestName != null && mixamoNameSet.Contains(bestName);
        }
    }

    Vector3 facing = Vector3.forward;
    float speedTarget, speedVel;
    int currentState;     // the state hash we last CrossFaded to (Loco or a one-shot)

    // The Walk/Run locomotion CLIP names (from the authored library, via StanceCatalog) — the 3D loco blend picks its
    // Speed position by matching the driven clip to these, so Clip(3D) is the SOLE owner of what 3D locomotion plays.
    // (CharacterAction is billboard-only and never touches the 3D blend.) Idle is the default 0, so it needs no name.
    string walkClip = "", runClip = "", sprintClip = "";

    // How fast the Walk / Run / Sprint clip walks on THIS body, in the model's own unscaled units a second — looked up
    // once per body from the measurements on each binding (slot 0/1/2), then only multiplied by the live world scale.
    readonly float[] nativeStride = new float[3];
    readonly bool[] strideResolved = new bool[3];
    static readonly HashSet<string> unmeasuredReported = new HashSet<string>();

    /// <summary>How fast <paramref name="b"/>'s walk, run or sprint clip walks on this body at normal playback, in world
    /// units a second. False for any other stance, or when the clip has never been measured.</summary>
    public bool TryClipSpeed(StanceBinding b, out float worldSpeed)
    {
        worldSpeed = 0f;
        int slot = b == null ? -1 : b.id == StanceId.Walk ? 0 : b.id == StanceId.Run ? 1 : b.id == StanceId.Sprint ? 2 : -1;
        if (slot < 0 || anim == null) return false;
        if (!strideResolved[slot]) { nativeStride[slot] = ResolveStride(b); strideResolved[slot] = true; }
        if (nativeStride[slot] <= 0f) return false;
        worldSpeed = nativeStride[slot] * transform.lossyScale.y;
        return true;
    }

    // This body's own measurement when the binding has one for its avatar. A body added after the clip was measured
    // gets the middle of every measured body, scaled by its own Humanoid size, and is reported once so it is measured.
    float ResolveStride(StanceBinding b)
    {
        string avatar = anim.avatar != null ? anim.avatar.name : "";
        if (b.bodyStrides != null)
            for (int i = 0; i < b.bodyStrides.Count; i++)
                if (b.bodyStrides[i] != null && b.bodyStrides[i].avatar == avatar) return b.bodyStrides[i].speed;
        if (b.speedPerHumanScale <= 0f || anim.avatar == null || !anim.avatar.isHuman) return 0f;
        if (unmeasuredReported.Add(b.id + "/" + avatar))
            GameLog.Warn(LogCat.Unwritten, $"Character3DPose: the {b.id} clip '{b.clip}' was never measured on body " +
                         $"'{avatar}', so its legs use the average body's stride. Pick the clip again in the Stance " +
                         "Composer (or press its step button) to measure every body.", this);
        return b.speedPerHumanScale * anim.humanScale;
    }

    void Awake() => Reinitialize();

    // Every Animator call warns ONCE PER CALL when nothing is driving it — three per frame from Update alone, which
    // buries the console under thousands of lines. Reinitialize already tolerates a missing controller; the per-frame
    // path did not, so it is gated here. The report fires ONCE and names the object, so a body genuinely missing its
    // controller is still found rather than silently swallowed.
    bool warnedNoController;
    bool Posable
    {
        get
        {
            if (anim == null) anim = GetComponent<Animator>();
            if (anim != null && anim.runtimeAnimatorController != null) return true;
            if (!warnedNoController)
            {
                warnedNoController = true;
                GameLog.Warn(LogCat.Unwritten, $"Character3DPose: \"{name}\" has no AnimatorController — posing skipped for it. " +
                                 "Reported once, not per frame.", this);
            }
            return false;
        }
    }

    /// <summary>(Re)build the state-hash + clip-length caches from the CURRENT controller. Called on Awake, and by the
    /// Stance Composer after it rebuilds the controller so a clip change reflects live without leaving Play.</summary>
    public void Reinitialize()
    {
        if (anim == null) anim = GetComponent<Animator>();
        currentState = Loco;
        stateHash.Clear();
        clipLen.Clear();
        clipFrames.Clear();
        System.Array.Clear(strideResolved, 0, strideResolved.Length);

        // Discover states + clip lengths straight from the controller, keyed by the CLIP NAME. A one-shot state is
        // named after its clip (the builder does this), so a clip counts as a one-shot only if a state with that
        // name exists — the locomotion clips (blend-tree children, no same-named state) are length-cached but never
        // become one-shot states. clipLen feeds NaturalDuration (FitWindow fit) for every clip.
        var rac = anim.runtimeAnimatorController;
        if (rac != null)
            foreach (var clip in rac.animationClips)
            {
                if (clip == null) continue;
                string name = Clean(clip.name);
                clipLen[name] = clip.length;
                clipFrames[name] = Mathf.Max(1, Mathf.RoundToInt(clip.length * clip.frameRate));
                int h = Animator.StringToHash(name);
                if (anim.HasState(0, h)) stateHash[name] = h;
            }

        mixamoNameSet.Clear();
        foreach (var n in mixamoClipNames)
            if (!string.IsNullOrEmpty(n)) mixamoNameSet.Add(Clean(n));

        // Cache the loco clip names the same authored library fed into the blend tree, so Play matches by CLIP.
        var w = StanceCatalog.Get(StanceId.Walk);   walkClip   = w != null ? w.clip ?? "" : "";
        var r = StanceCatalog.Get(StanceId.Run);    runClip    = r != null ? r.clip ?? "" : "";
        var sp = StanceCatalog.Get(StanceId.Sprint); sprintClip = sp != null ? sp.clip ?? "" : "";

        ResolveWeaponLayers();
    }

    // ---- How the held weapon is held: three layers of the controller over the base layer ----
    // Layer 1 (arms and fingers only) = the weapon's carry pose, over the shared Idle/Walk/Run/Sprint legs.
    // Layer 2 (whole body) = the weapon's own standing idle, over everything while the body stands still.
    // Layer 3 (arms and fingers only) = the weapon being put away or taken out, over whatever the legs are doing.
    // Layers 4 and 5 (one arm each) = the carry pose of a weapon that holds only that arm; the other swings with the legs.
    // Layers 1, 2, 4 and 5 show only while the weapon is in hand; at peace the body is the shared one, hands empty.

    /// <summary>The controller parameter the "Draw" layer's states are scrubbed by: the draw's progress, 0..1.</summary>
    public const string DrawTimeParam = "DrawTime";
    static readonly int PDrawTime = Animator.StringToHash(DrawTimeParam);

    struct WeaponLayer { public int layer; public int hash; public float written; }
    WeaponLayer carry = new WeaponLayer { layer = 1 };   // hash 0 = the shared arms
    WeaponLayer stand = new WeaponLayer { layer = 2 };   // hash 0 = the shared Idle
    WeaponLayer draw = new WeaponLayer { layer = 3 };    // the put-away or take-out clip playing now
    WeaponLayer carryRight = new WeaponLayer { layer = 4 }, carryLeft = new WeaponLayer { layer = 5 };   // one-arm carry
    // the put-away or take-out of a weapon whose move turns the torso and head too (its row's putAwayMovesUpperBody)
    WeaponLayer drawUpper = new WeaponLayer { layer = 6 };
    bool drawOnUpper;                                    // this weapon's put-away lives on drawUpper, not on draw
    int sheatheHash, unsheatheHash;                      // 0 = this weapon simply moves, with no clip
    bool hasDrawParam;
    DrawPhase drawPhase = DrawPhase.Sheathed;
    float drawProgress;
    WeaponType heldWeapon = WeaponType.None;
    bool travel;            // the stance shown this frame is Idle/Walk/Run/Sprint
    bool standing;          // the stance shown this frame is Idle

    public void SetShownStance(StanceId id)
    {
        travel = StanceCatalog.IsLocomotion(id);
        standing = id == StanceId.Idle;
        bool dying = id == StanceId.Die;
        if (dying == showingDeath) return;
        showingDeath = dying;
        DeathShown?.Invoke(dying);
    }

    bool showingDeath;

    /// <summary>This body started (true) or stopped (false) showing its death — said once at each change, never per
    /// frame. The weapon socket drops the weapon on it (<see cref="WeaponSocket"/>).</summary>
    public event System.Action<bool> DeathShown;

    /// <summary>The weapon type this body holds — told by its <see cref="WeaponSocket"/> whenever the weapon changes.</summary>
    public void SetHeldWeapon(WeaponType w) { heldWeapon = w; ResolveWeaponLayers(); }

    /// <summary>Where this body's weapon is and how far through a draw or a put-away — told by its
    /// <see cref="WeaponSocket"/> every frame from the server's word.</summary>
    public void SetDraw(DrawPhase phase, float progress) { drawPhase = phase; drawProgress = progress; }

    // Asked when the weapon or the controller changes, never per frame.
    void ResolveWeaponLayers()
    {
        // The carry pose is found on whichever of the three carry layers the controller built it into.
        carry.hash = StateOn(carry.layer, StanceCatalog.CarryPose(heldWeapon));
        carryRight.hash = StateOn(carryRight.layer, StanceCatalog.CarryPose(heldWeapon));
        carryLeft.hash = StateOn(carryLeft.layer, StanceCatalog.CarryPose(heldWeapon));
        stand.hash = StateOn(stand.layer, StanceCatalog.WeaponIdle(heldWeapon));
        var b = StanceCatalog.Weapon(heldWeapon);
        drawOnUpper = b != null && b.putAwayMovesUpperBody;
        int drawLayer = drawOnUpper ? drawUpper.layer : draw.layer;
        sheatheHash = b != null ? StateOn(drawLayer, b.sheatheClip) : 0;
        unsheatheHash = b != null ? StateOn(drawLayer, b.unsheatheClip) : 0;
        hasDrawParam = false;
        if (anim != null && anim.runtimeAnimatorController != null)
            foreach (var p in anim.parameters) if (p.nameHash == PDrawTime) { hasDrawParam = true; break; }   // perf-ok: on weapon change
    }

    // A controller built before a layer existed has fewer layers: nothing is asked of a layer it does not have.
    int StateOn(int layer, string clip)
    {
        if (string.IsNullOrEmpty(clip) || anim == null || anim.runtimeAnimatorController == null || anim.layerCount <= layer) return 0;
        int h = Animator.StringToHash(clip);
        return anim.HasState(layer, h) ? h : 0;
    }

    // Eases a weapon layer in while <paramref name="on"/> and the held weapon has a clip for it, out otherwise, over the
    // same time the body cross-fades into a one-shot — so the layer and the body change together. Everything is read
    // back from the Animator rather than remembered: a body switched off and on (a banked creature reused, one thawed
    // far from the camera, a controller rebuilt) comes back with its layers reset, and a remembered "already there"
    // would leave it showing nothing.
    void UpdateWeaponLayer(ref WeaponLayer l, bool on)
    {
        if (l.hash == 0 && l.written == 0f) return;
        if (!anim.isActiveAndEnabled || anim.layerCount <= l.layer) return;
        float target = on && l.hash != 0 ? 1f : 0f;
        float weight = anim.GetLayerWeight(l.layer);
        if (target > 0f && anim.GetCurrentAnimatorStateInfo(l.layer).shortNameHash != l.hash)
        {
            if (!anim.IsInTransition(l.layer))
            {
                // Already showing another weapon's clip: blend across. Showing nothing: enter it outright, since the
                // layer weight is the fade.
                if (weight > 0.001f) anim.CrossFadeInFixedTime(l.hash, crossFade, l.layer);
                else anim.Play(l.hash, l.layer, 0f);
            }
            // The weight does not rise until the layer holds the clip: an empty state never shows at any weight.
            if (weight <= 0.001f) target = 0f;
        }
        float next = Mathf.MoveTowards(weight, target, Time.deltaTime / Mathf.Max(0.01f, crossFade));
        if (next != weight) anim.SetLayerWeight(l.layer, next);
        l.written = next;
    }

    static string Clean(string n)
    {
        int bar = n.LastIndexOf('|');
        return bar >= 0 ? n.Substring(bar + 1) : n;
    }

    // ---- IStancePose ----

    public void Play(CharacterAction a, string clip)
    {
        // The AUTHORED clip wins: if it has its own state, CrossFade to it — even for a stance whose 2D 'action' is a
        // locomotion one (e.g. a held Casting/Combat/Guard pose parked on action Idle). Guarded so re-driving the same
        // clip each frame doesn't restart it. Only clips WITHOUT a dedicated state — the Idle/Walk/Run locomotion
        // clips (they live in the blend tree, not a one-shot state) or an unassigned stance — fall through to the
        // blend, positioned by the CLIP (unassigned → rests at idle, never a substituted clip).
        if (!string.IsNullOrEmpty(clip) && stateHash.TryGetValue(clip, out int h)) { GoTo(h); return; }
        // Loco blend position from the CLIP (Clip(3D) is the SSOT) — NOT the 2D CharacterAction (billboard-only).
        // An unknown/empty clip → rests at idle (0), never a substituted clip.
        bool isSprint = !string.IsNullOrEmpty(clip) && clip == sprintClip;
        bool isRun  = !isSprint && !string.IsNullOrEmpty(clip) && clip == runClip;
        bool isWalk = !isSprint && !isRun && !string.IsNullOrEmpty(clip) && clip == walkClip;
        speedTarget = isSprint ? 2f : isRun ? 1f : isWalk ? 0.5f : 0f;
        Gait = isSprint ? LocoGait.Sprint : isRun ? LocoGait.Run : isWalk ? LocoGait.Walk : LocoGait.None;
        if (Posable) anim.speed = 1f;
        GoTo(Loco);
    }

    public void PlayFrom(CharacterAction a, string clip, float normalizedStart)
    {
        // Only a clip with its OWN one-shot state can be entered part-way — the locomotion blend has no single
        // timeline to seek into, and a body joining someone mid-walk is already in the right place anyway.
        if (!Posable || string.IsNullOrEmpty(clip) || !stateHash.TryGetValue(clip, out int h)) { Play(a, clip); return; }

        // Played rather than cross-faded: a cross-fade always enters at the start of the target state, which is
        // the one thing this call exists to avoid.
        anim.Play(h, 0, Mathf.Clamp01(normalizedStart));
        anim.speed = 1f;
        currentState = h;
    }

    // Jump/Fall clip-phase driver: play the clip and either HOLD it at `freezeSeconds` (airborne pose) or, once
    // `releaseToEnd`, resume from that frame and play the landing tail to the end. Works by scaling anim.speed to 0
    // at the freeze frame (freeze) or to `mult` and snapping past it (release). `mult` is the stance's × mult — the
    // takeoff crouch and landing tail play at that speed, while the airborne HOLD stays a hard 0 regardless. The
    // freeze POINT (freezeN) is a normalized clip fraction, so it's unaffected by mult. No allocation — struct state
    // info + cached clip length. Falls back to a plain hold if the clip has no dedicated one-shot state.
    public void PlayClipPhase(CharacterAction src, string clip, float freezeSeconds, bool releaseToEnd, float mult = 1f,
                              bool restart = false, float aimSeconds = -1f)
    {
        if (string.IsNullOrEmpty(clip) || !stateHash.TryGetValue(clip, out int h)) { Play(src, clip); return; }

        float len = clipLen.TryGetValue(clip, out float l) && l > 0.01f ? l : 1f;
        float freezeN = Mathf.Clamp01(freezeSeconds / len);

        // First frame in this state: CrossFade in at the start, playing forward. A held pose that has just begun
        // (restart) re-enters even when the body is already in this clip — a bow cast right after a bow shot must start
        // from the draw, not hold wherever the shot had reached.
        if (currentState != h || restart)
        {
            currentState = h;
            anim.CrossFadeInFixedTime(h, crossFade, 0);
            anim.speed = mult;
            return;
        }
        // Still crossfading INTO the jump state (or the animator's current state isn't the jump clip yet): keep
        // playing at speed and DON'T freeze. Reading normalizedTime here returns the OUTGOING state (e.g. the
        // looping Run, whose accumulated time is far past freezeN) — zeroing speed on that would pause the animator
        // mid-transition and stick us on a run frame forever. This is the fix for that stuck-frame bug.
        var st = anim.GetCurrentAnimatorStateInfo(0);
        if (anim.IsInTransition(0) || st.shortNameHash != h) { anim.speed = mult; return; }

        float t = st.normalizedTime;
        if (releaseToEnd)
        {
            if (t < freezeN) anim.Play(h, 0, freezeN);   // short hop: snap up to the freeze frame before playing the tail
            anim.speed = mult;                           // play out the landing tail to the end (non-looping holds last frame)
        }
        else
        {
            // Given a point to be at, the clip is PUT there every frame (stopped, so nothing drifts): it follows its
            // schedule exactly, whatever the frame rate. Otherwise: play up to the freeze frame at mult, then hold.
            if (aimSeconds >= 0f) { anim.Play(h, 0, Mathf.Min(aimSeconds, freezeSeconds) / len); anim.speed = 0f; }
            else anim.speed = t >= freezeN ? 0f : mult;
        }
    }

    public void SetActionSpeed(CharacterAction a, float multiplier)
    {
        // Scale clip PLAYBACK by the live multiplier — for one-shot states (Attack/Cast/… FitWindow or stat sync) AND
        // for the locomotion blend (Walk/Run). On the blend, anim.speed is the CADENCE (how fast the legs
        // churn) and is orthogonal to PSpeed (which only picks the walk↔run pose), so a Slow/Haste gait mult belongs
        // here. Play() resets anim.speed to 1 each frame right before this, so this is the sole live speed owner.
        anim.speed = Mathf.Max(0.01f, multiplier);
    }

    public bool HasAction(CharacterAction a, string clip) =>
        CharacterActions.IsLocomotion(a) || (!string.IsNullOrEmpty(clip) && stateHash.ContainsKey(clip));

    public float NaturalDuration(CharacterAction a, string clip) =>
        !string.IsNullOrEmpty(clip) && clipLen.TryGetValue(clip, out float l) ? l : 0f;

    public int NaturalFrameCount(CharacterAction a, string clip) =>
        !string.IsNullOrEmpty(clip) && clipFrames.TryGetValue(clip, out int n) ? n : 0;

    /// <summary>Report WHERE in the Walk/Run cycle the animation currently is, so the footstep detector can fire on
    /// the real foot-plant FRAME instead of a timer. Returns false whenever a one-shot state (Attack/Jump/…) is
    /// playing or we're mid-CrossFade — i.e. only true while the locomotion blend is the live state.
    /// <paramref name="frac"/> is the 0→1 position within the (looping) cycle (a plant frame lands at
    /// frame ÷ totalFrames); <paramref name="gait"/> is the ACTIVE locomotion gait (Walk/Run, or None when idle), so
    /// the caller picks that gait's authored plant frames and gates out standing still WITHOUT reading any move-speed
    /// or blend threshold. Frame-locked, so it already tracks gait speed / Haste (anim.speed scales normalizedTime)
    /// with nothing to hand-sync. Returns false while a one-shot (Attack/Jump/…) or its transition is playing.</summary>
    public bool TryGetLocoPhase(out float frac, out LocoGait gait)
    {
        frac = 0f; gait = LocoGait.None;
        if (!Posable) return false;
        var st = anim.GetCurrentAnimatorStateInfo(0);
        if (st.shortNameHash != Loco) return false;   // a one-shot (or the transition into one) is playing → no steps
        float nt = st.normalizedTime;
        frac = nt - Mathf.Floor(nt);
        gait = Gait;
        return true;
    }

    public void ResumeSelfDrive() { speedTarget = 0f; if (Posable) anim.speed = 1f; GoTo(Loco); }

    float cachedHeight, cachedWidth;
    // World-space rendered height (union of this model's renderer bounds), cached on first successful read. Size
    // barely varies with pose and callers ask rarely (once per NPC bark), so computing it once is enough.
    public float VisualWorldHeight { get { Measure(); return cachedHeight; } }

    /// <summary>World-space rendered WIDTH — the wider of the two horizontal extents, so it does not shrink when the
    /// body happens to be stood side-on to the camera. Read from the SAME bounds union as the height, in one pass:
    /// a second walk over every renderer to answer a sibling question is the duplicate this cache exists to avoid.
    /// </summary>
    public float VisualWorldWidth { get { Measure(); return cachedWidth; } }

    // Fills both caches from one bounds union. The height cache doubles as the "already measured" flag, exactly as
    // it did before, so this stays the memo shape rather than becoming per-call work.
    void Measure()
    {
        if (cachedHeight > 0f) return;
        var rends = GetComponentsInChildren<Renderer>();
        // The body alone: a worn hat is not the person's height (see Character3DBody.MeasureHeight).
        bool any = false;
        var b = new Bounds();
        for (int i = 0; i < rends.Length; i++)
        {
            if (HeadPiece.IsPiece(rends[i])) continue;
            if (!any) { b = rends[i].bounds; any = true; }
            else b.Encapsulate(rends[i].bounds);
        }
        if (!any) return;
        cachedHeight = b.size.y;
        cachedWidth = Mathf.Max(b.size.x, b.size.z);
    }

    /// <summary>World-space facing/aim (XZ) — the model yaws toward it. Called by the 3D visual brain.</summary>
    public void SetFacing(Vector3 worldDir, bool snap = false)
    {
        worldDir.y = 0f;
        if (worldDir.sqrMagnitude <= 1e-6f) return;
        facing = worldDir;
        if (snap) snapFacing = true;
    }

    // Set the frame an attack starts; the next turn lands at once and the flag clears.
    bool snapFacing;

    void GoTo(int hash)
    {
        if (hash == currentState || !Posable) return;
        anim.CrossFadeInFixedTime(hash, crossFade, 0);
        currentState = hash;
    }

    void Update()
    {
        // Facing is this component's OTHER job and needs no controller — a body with no animation should still turn.
        if (Posable)
        {
            float s = anim.GetFloat(PSpeed);
            anim.SetFloat(PSpeed, Mathf.SmoothDamp(s, speedTarget, ref speedVel, speedSmooth));
            bool inHand = drawPhase == DrawPhase.Drawn;
            UpdateWeaponLayer(ref carry, travel && inHand);
            UpdateWeaponLayer(ref carryRight, travel && inHand);
            UpdateWeaponLayer(ref carryLeft, travel && inHand);
            UpdateWeaponLayer(ref stand, standing && inHand);
            bool moving = drawPhase == DrawPhase.Drawing || drawPhase == DrawPhase.Sheathing;
            int moveHash = drawPhase == DrawPhase.Drawing ? unsheatheHash : sheatheHash;
            if (moving) { if (drawOnUpper) drawUpper.hash = moveHash; else draw.hash = moveHash; }
            if (moving && hasDrawParam && moveHash != 0) anim.SetFloat(PDrawTime, drawProgress);
            UpdateWeaponLayer(ref draw, moving && travel && hasDrawParam && !drawOnUpper);
            UpdateWeaponLayer(ref drawUpper, moving && travel && hasDrawParam && drawOnUpper);
        }

        if (facing.sqrMagnitude > 1e-4f)
        {
            Quaternion look = Quaternion.LookRotation(facing);
            transform.rotation = snapFacing ? look : Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }
        snapFacing = false;
    }
}
