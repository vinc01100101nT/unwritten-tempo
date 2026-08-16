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
                Debug.LogWarning($"[Character3DPose] \"{name}\" has no AnimatorController — posing skipped for it. " +
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

    // Jump/Fall clip-phase driver: play the clip and either HOLD it at `freezeSeconds` (airborne pose) or, once
    // `releaseToEnd`, resume from that frame and play the landing tail to the end. Works by scaling anim.speed to 0
    // at the freeze frame (freeze) or to `mult` and snapping past it (release). `mult` is the stance's × mult — the
    // takeoff crouch and landing tail play at that speed, while the airborne HOLD stays a hard 0 regardless. The
    // freeze POINT (freezeN) is a normalized clip fraction, so it's unaffected by mult. No allocation — struct state
    // info + cached clip length. Falls back to a plain hold if the clip has no dedicated one-shot state.
    public void PlayClipPhase(CharacterAction src, string clip, float freezeSeconds, bool releaseToEnd, float mult = 1f)
    {
        if (string.IsNullOrEmpty(clip) || !stateHash.TryGetValue(clip, out int h)) { Play(src, clip); return; }

        float len = clipLen.TryGetValue(clip, out float l) && l > 0.01f ? l : 1f;
        float freezeN = Mathf.Clamp01(freezeSeconds / len);

        // First frame in this state: CrossFade in at the start, playing forward.
        if (currentState != h)
        {
            anim.CrossFadeInFixedTime(h, crossFade, 0);
            currentState = h;
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
            anim.speed = t >= freezeN ? 0f : mult;       // hold on the freeze frame while airborne; play up to it at mult before that
        }
    }

    public void SetActionSpeed(CharacterAction a, float multiplier)
    {
        // Scale clip PLAYBACK by the live multiplier — for one-shot states (Attack/Cast/… FitWindow or stat sync) AND
        // for the locomotion blend (Walk/Run MoveGait). On the blend, anim.speed is the CADENCE (how fast the legs
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

    float cachedHeight;
    // World-space rendered height (union of this model's renderer bounds), cached on first successful read. Height
    // barely varies with pose and callers ask rarely (once per NPC bark), so computing it once is enough.
    public float VisualWorldHeight
    {
        get
        {
            if (cachedHeight > 0f) return cachedHeight;
            var rends = GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return 0f;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            cachedHeight = b.size.y;
            return cachedHeight;
        }
    }

    /// <summary>World-space facing/aim (XZ) — the model yaws toward it. Called by the 3D visual brain.</summary>
    public void SetFacing(Vector3 worldDir)
    {
        worldDir.y = 0f;
        if (worldDir.sqrMagnitude > 1e-6f) facing = worldDir;
    }

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
        }

        if (facing.sqrMagnitude > 1e-4f)
        {
            Quaternion look = Quaternion.LookRotation(facing);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }
    }
}
