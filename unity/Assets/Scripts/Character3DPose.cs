using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The 3D presentation SINK: implements <see cref="IStancePose"/> by driving an <see cref="Animator"/> (and
/// yawing the model) — the live-3D twin of <see cref="BakedBillboard8"/>. <see cref="StanceController"/> pushes
/// the resolved <see cref="CharacterAction"/> + speed here every frame; locomotion (Idle/Walk/Run) blends via a
/// 1-D <c>Speed</c> tree, one-shots (Attack/Jump/Roll/Hurt/PickUp/Die/Cast/Sit) CrossFade to their own state.
///
/// <para>Because StanceController drives this directly, there's no second "what action?" decision here — it only
/// renders. State names come from <see cref="Character3DClipMap"/> (the shared action→clip SSOT), so they match
/// whatever the AnimatorController builder authored. No per-frame allocation: hashes + clip lengths cached in Awake.</para>
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

    Animator anim;
    static readonly int Loco = Animator.StringToHash("Locomotion");
    static readonly int PSpeed = Animator.StringToHash("Speed");

    // Cached: which one-shot actions actually have a state, their state hash, and every action's clip length.
    readonly Dictionary<CharacterAction, int> stateHash = new Dictionary<CharacterAction, int>();
    readonly Dictionary<CharacterAction, float> clipLen = new Dictionary<CharacterAction, float>();

    Vector3 facing = Vector3.forward;
    float speedTarget, speedVel;
    int currentState;     // the state hash we last CrossFaded to (Loco or a one-shot)

    void Awake()
    {
        anim = GetComponent<Animator>();
        currentState = Loco;

        // Discover one-shot states by name (CharacterAction.ToString(), matching the builder) — a state only
        // counts if the controller actually has it, so a missing action silently falls back to Idle/Locomotion.
        foreach (var kv in Character3DClipMap.ClipFor)
        {
            if (Character3DClipMap.IsLocomotion(kv.Key)) continue;
            int h = Animator.StringToHash(kv.Key.ToString());
            if (anim.HasState(0, h)) stateHash[kv.Key] = h;
        }

        // Clip lengths for NaturalDuration (FitWindow fit). Match each imported clip to an action by its
        // Quaternius suffix via the shared map, so Attack/Roll can be fit to their runtime window.
        var rac = anim.runtimeAnimatorController;
        if (rac != null)
            foreach (var clip in rac.animationClips)
            {
                if (clip == null) continue;
                string suffix = Clean(clip.name);
                foreach (var kv in Character3DClipMap.ClipFor)
                    if (kv.Value == suffix) clipLen[kv.Key] = clip.length;
            }
    }

    static string Clean(string n)
    {
        int bar = n.LastIndexOf('|');
        return bar >= 0 ? n.Substring(bar + 1) : n;
    }

    // ---- IStancePose ----

    public void Play(CharacterAction a)
    {
        if (Character3DClipMap.IsLocomotion(a))
        {
            speedTarget = a == CharacterAction.Run ? 1f : a == CharacterAction.Walk ? 0.5f : 0f;
            anim.speed = 1f;
            GoTo(Loco);
            return;
        }
        // One-shot: CrossFade to its state (guarded so re-driving the same action each frame doesn't restart it).
        if (stateHash.TryGetValue(a, out int h)) GoTo(h);
        else { speedTarget = 0f; anim.speed = 1f; GoTo(Loco); }   // no state for it → idle rather than freeze
    }

    public void PlayFall(CharacterAction src, float startFrac) => Play(src);   // 3D just holds the Jump/fall state

    public void SetActionSpeed(CharacterAction a, float multiplier)
    {
        // Locomotion speed is the blend param, not clip speed; only one-shots (Attack/Roll FitWindow, stat sync)
        // scale the Animator's playback so the clip fits its window. Play() resets anim.speed for locomotion.
        if (!Character3DClipMap.IsLocomotion(a)) anim.speed = Mathf.Max(0.01f, multiplier);
    }

    public bool HasAction(CharacterAction a) =>
        Character3DClipMap.IsLocomotion(a) || stateHash.ContainsKey(a);

    public float NaturalDuration(CharacterAction a) => clipLen.TryGetValue(a, out float l) ? l : 0f;

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
