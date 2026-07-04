using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The 3D presentation SINK: implements <see cref="IStancePose"/> by driving an <see cref="Animator"/> (and
/// yawing the model) — the live-3D twin of <see cref="BakedBillboard8"/>. <see cref="StanceController"/> pushes
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

    void Awake() => Reinitialize();

    /// <summary>(Re)build the state-hash + clip-length caches from the CURRENT controller. Called on Awake, and by the
    /// Stance Composer after it rebuilds the controller so a clip change reflects live without leaving Play.</summary>
    public void Reinitialize()
    {
        if (anim == null) anim = GetComponent<Animator>();
        currentState = Loco;
        stateHash.Clear();
        clipLen.Clear();

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
                int h = Animator.StringToHash(name);
                if (anim.HasState(0, h)) stateHash[name] = h;
            }

        mixamoNameSet.Clear();
        foreach (var n in mixamoClipNames)
            if (!string.IsNullOrEmpty(n)) mixamoNameSet.Add(Clean(n));
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
        // blend at the action's speed (unassigned → rests at idle, never a substituted clip).
        if (!string.IsNullOrEmpty(clip) && stateHash.TryGetValue(clip, out int h)) { GoTo(h); return; }
        speedTarget = a == CharacterAction.Run ? 1f : a == CharacterAction.Walk ? 0.5f : 0f;
        anim.speed = 1f;
        GoTo(Loco);
    }

    public void PlayFall(CharacterAction src, string clip, float startFrac) => Play(src, clip);   // 3D just holds the fall state

    public void SetActionSpeed(CharacterAction a, float multiplier)
    {
        // Scale playback only while on a one-shot clip state (Attack/Cast/… FitWindow or stat sync). On the locomotion
        // blend, speed is the blend param not clip speed — Play() already reset anim.speed there.
        if (currentState != Loco) anim.speed = Mathf.Max(0.01f, multiplier);
    }

    public bool HasAction(CharacterAction a, string clip) =>
        CharacterActions.IsLocomotion(a) || (!string.IsNullOrEmpty(clip) && stateHash.ContainsKey(clip));

    public float NaturalDuration(CharacterAction a, string clip) =>
        !string.IsNullOrEmpty(clip) && clipLen.TryGetValue(clip, out float l) ? l : 0f;

    public void ResumeSelfDrive() { speedTarget = 0f; anim.speed = 1f; GoTo(Loco); }

    /// <summary>World-space facing/aim (XZ) — the model yaws toward it. Called by the 3D visual brain.</summary>
    public void SetFacing(Vector3 worldDir)
    {
        worldDir.y = 0f;
        if (worldDir.sqrMagnitude > 1e-6f) facing = worldDir;
    }

    void GoTo(int hash)
    {
        if (hash == currentState) return;
        anim.CrossFadeInFixedTime(hash, crossFade, 0);
        currentState = hash;
    }

    void Update()
    {
        float s = anim.GetFloat(PSpeed);
        anim.SetFloat(PSpeed, Mathf.SmoothDamp(s, speedTarget, ref speedVel, speedSmooth));

        if (facing.sqrMagnitude > 1e-4f)
        {
            Quaternion look = Quaternion.LookRotation(facing);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }
    }
}
