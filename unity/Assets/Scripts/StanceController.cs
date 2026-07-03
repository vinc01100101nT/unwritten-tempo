using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The single runtime CHOKE POINT that drives a <see cref="BakedBillboard8"/>'s pose from the data-authored
/// <see cref="StanceLibrary"/> — the persistent-state sibling of <see cref="VfxCue"/>. Nothing else calls
/// <see cref="BakedBillboard8.Play"/> for a unit that has one; callers feed intent here and this resolves it every
/// frame, so two systems can never fight over the billboard (the dual-path bug).
///
/// <para>Two layers, highest first:
/// <list type="number">
/// <item><b>Action stance</b> (<see cref="DriveStance"/>) — the transient the gameplay brain drives one at a time
/// (Idle/Walk/Run/Attack/Jump/Roll/Hurt/PickUp/Die/Fall). A FitWindow stance is fed a RUNTIME window (swing/dash time)
/// so its clip fits the window — data a static library can't own without hand-copying it (an SSOT violation).</item>
/// <item><b>Held stance</b> (<see cref="Push"/>/<see cref="Pop"/>) — persistent baselines (Combat/Guard/Casting/Sit);
/// the highest-<see cref="StanceBinding.priority"/> active one wins.</item>
/// </list>
/// Fallback when neither is set: a self-driving unit (<see cref="selfDrive"/> = true) hands back to
/// <see cref="BakedBillboard8"/>'s movement-sampled Idle/Walk/Run; the player (<see cref="selfDrive"/> = false) plays
/// the authored Idle stance.</para>
///
/// <para>Resolves in <c>LateUpdate</c> at execution order −50 — after gameplay brains that feed it
/// (<see cref="BakedPlayerVisual"/> at −100) and before <see cref="BakedBillboard8"/>'s render at 0.</para>
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public class StanceController : MonoBehaviour
{
    [Tooltip("Fallback when NO action or held stance is active. true = hand back to BakedBillboard8 self-drive " +
             "(NPCs/mobs sample their own movement); false = play the authored Idle stance (the player).")]
    public bool selfDrive = true;

    /// <summary>The player's stance controller — the one gameplay systems (cast, combat, sit) push held stances onto.
    /// Set when <see cref="BakedPlayerVisual"/> claims its controller via <see cref="BecomePlayer"/>. Mirrors
    /// <see cref="Character.Instance"/> / <c>StatusController.Player</c>.</summary>
    public static StanceController Player { get; private set; }

    // The presentation sink this choke point drives — a baked billboard OR a live-3D Character3DPose. Resolved
    // via the IStancePose interface so swapping the visual backend doesn't touch this resolver.
    IStancePose pose;
    Player3DController playerCtrl;   // optional — resolves the MoveGait source

    // Active held stances in push order (oldest first). Push/Pop mutate on transitions, not per frame; the per-frame
    // resolve only INDEX-reads this list, so the hot path allocates nothing.
    readonly List<StanceId> active = new List<StanceId>();

    // Transient action-stance layer — beats every held stance. Set each frame it holds; ClearAction drops it.
    bool hasAction;
    StanceId actionId;
    float actionWindow;              // runtime window seconds for a FitWindow action (0 = none)

    bool driving;                    // a stance is overriding self-drive — so we hand back to it ONCE
    StanceId currentResolved;

    /// <summary>The stance currently winning (action or held).</summary>
    public StanceId CurrentStance => currentResolved;
    /// <summary>True while a stance is overriding the baked billboard's self-drive.</summary>
    public bool IsDriving => driving;

    void Awake()
    {
        pose = GetComponent<IStancePose>();
        if (pose == null) pose = GetComponentInChildren<IStancePose>();
        playerCtrl = GetComponent<Player3DController>();
    }

    /// <summary>Point the MoveGait source at the player controller (the gait-mult owner) — for a rig whose billboard
    /// rides a separate object from the controller (e.g. the player visual).</summary>
    public void SetGaitSource(Player3DController p) { if (p != null) playerCtrl = p; }

    /// <summary>Claim this as the PLAYER's controller: register it as <see cref="Player"/> (so cast/combat systems
    /// can push held stances onto it) and switch its fallback to hold the authored Idle stance instead of self-drive.</summary>
    public void BecomePlayer() { Player = this; selfDrive = false; }

    void OnDestroy() { if (Player == this) Player = null; }

    // ---- action-stance layer (transient) ----

    /// <summary>Drive an action stance THIS frame (overrides any held stance). <paramref name="window"/> is the
    /// runtime duration a FitWindow stance fits its clip to (e.g. attacker.SwingTime, dash length); ignored for other
    /// sources. Call every frame the action holds; <see cref="ClearAction"/> when it ends.</summary>
    public void DriveStance(StanceId id, float window = 0f)
    {
        hasAction = true; actionId = id; actionWindow = window;
    }

    /// <summary>Drop the action stance so the held-stance layer (or the fallback) resolves.</summary>
    public void ClearAction() { hasAction = false; }

    // ---- held-stance layer ----

    /// <summary>Enter a held stance (idempotent). Participates in the priority resolve until <see cref="Pop"/>.</summary>
    public void Push(StanceId id) { if (!active.Contains(id)) active.Add(id); }

    /// <summary>Leave a held stance (idempotent).</summary>
    public void Pop(StanceId id) => active.Remove(id);

    /// <summary>Push or pop by a bool — the natural call for a condition (e.g. Set(Combat, attacker.Engaged)).</summary>
    public void Set(StanceId id, bool on) { if (on) Push(id); else Pop(id); }

    /// <summary>Drop every held stance.</summary>
    public void Clear() => active.Clear();

    public bool IsActive(StanceId id) => active.Contains(id);

    void LateUpdate()
    {
        if (pose == null) return;

        // 1) Action stance beats everything.
        if (hasAction)
        {
            var b = StanceCatalog.Get(actionId);
            if (b != null) { driving = true; currentResolved = actionId; ApplyStance(b, actionWindow); return; }
        }

        // 2) Highest-priority active, BAKED held stance. Ties break toward the most-recently pushed (active is in push
        // order, so scanning with >= keeps the later one). Zero-alloc index loop.
        StanceBinding best = null;
        int bestPri = int.MinValue;
        for (int i = 0; i < active.Count; i++)
        {
            var b = StanceCatalog.Get(active[i]);
            if (b == null || !pose.HasAction(b.action)) continue;
            if (b.priority >= bestPri) { bestPri = b.priority; best = b; }
        }
        if (best != null)
        {
            driving = true; currentResolved = best.id;
            ApplyStance(best, 0f);
            return;
        }

        // 3) Nothing active — hand back to the fallback.
        if (selfDrive)
        {
            if (driving) { pose.ResumeSelfDrive(); driving = false; }   // ONCE — ResumeSelfDrive re-bases movement sampling
        }
        else
        {
            var idle = StanceCatalog.Get(StanceId.Idle);
            if (idle != null) ApplyStance(idle, 0f); else pose.Play(CharacterAction.Idle);
            currentResolved = StanceId.Idle;
            driving = false;
        }
    }

    // Play a resolved binding on the billboard: its action, its playback mode, and its live-modulated speed.
    void ApplyStance(StanceBinding b, float window)
    {
        if (b.playMode == StancePlayMode.FallTail) { pose.PlayFall(b.action, b.fallPhase); return; }

        pose.Play(b.action);
        float m = b.speedMult;
        switch (b.speedSource)
        {
            case StanceSpeedSource.AttackSpeed: m *= Character.Instance != null ? Character.Instance.AttackSpeedMult : 1f; break;
            case StanceSpeedSource.CastSpeed:   m *= Character.Instance != null ? Character.Instance.CastSpeedMult   : 1f; break;
            case StanceSpeedSource.MoveGait:    m *= playerCtrl != null ? playerCtrl.GaitSpeedMult : 1f; break;
            case StanceSpeedSource.FitWindow:
                float n = pose.NaturalDuration(b.action);
                if (window > 0.01f && n > 0.01f) m *= n / window;
                break;
        }
        pose.SetActionSpeed(b.action, Mathf.Max(0.01f, m));
    }
}
