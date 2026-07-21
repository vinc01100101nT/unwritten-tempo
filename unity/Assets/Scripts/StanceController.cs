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

    /// <summary>Gait multiplier for a unit that has NO <see cref="Player3DController"/> to read one from (an AI NPC).
    /// The <see cref="StanceSpeedSource.MoveGait"/> case uses this when <see cref="playerCtrl"/> is null, so an NPC
    /// driver (e.g. <see cref="TownNPC3DVisual"/>) can scale Walk cadence to its own ground speed. 1 = clip's natural
    /// cadence. Ignored for the player (its live controller owns the gait).</summary>
    public float externalGaitMult = 1f;

    // One held stance: its id + the FitWindow duration its clip is fit to (0 = none). Kept as a single unit so the id
    // and its window can't drift apart.
    struct Held { public StanceId id; public float window; }

    // Active held stances in push order (oldest first). Push/Pop mutate on transitions, not per frame; the per-frame
    // resolve only INDEX-reads this list, so the hot path allocates nothing.
    readonly List<Held> active = new List<Held>();

    int IndexOf(StanceId id)
    {
        for (int i = 0; i < active.Count; i++) if (active[i].id == id) return i;
        return -1;
    }

    // Transient action-stance layer — beats every held stance. Set each frame it holds; ClearAction drops it.
    bool hasAction;
    StanceId actionId;
    float actionWindow;              // runtime window seconds for a FitWindow action (0 = none); for Jump/Fall = clip freeze seconds
    bool actionRelease;             // Jump/Fall only: release the frozen clip and play the landing tail (see PlayClipPhase)

    // Self-expiring action OVERRIDE — a fire-and-forget one-shot (a skill release) that outranks even the brain's
    // per-frame action, so it can't be stomped by locomotion mid-play. Set once via PlayActionFor; auto-clears when
    // Time.time passes overrideUntil. overrideWindow is the FitWindow length its clip is fit to (plays through once).
    StanceId overrideId;
    float overrideUntil;
    float overrideWindow;

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
    public void DriveStance(StanceId id, float window = 0f, bool release = false)
    {
        hasAction = true; actionId = id; actionWindow = window; actionRelease = release;
    }

    /// <summary>Drop the action stance so the held-stance layer (or the fallback) resolves.</summary>
    public void ClearAction() { hasAction = false; }

    /// <summary>Fire a one-shot action OVERRIDE that plays once and outranks the per-frame brain action (used for a
    /// skill's release cue). <paramref name="seconds"/> ≤ 0 auto-derives the clip's natural length so it plays through
    /// exactly once. Call ONCE at the release moment — it self-expires; no per-frame calls, no allocation.
    /// Returns the resolved play duration so the caller can root the caster for exactly that long (aftercast delay) —
    /// the clip length stays the single source of truth for both the visual and the lock.
    /// <paramref name="durationScale"/> stretches (&gt;1) or compresses (&lt;1) the whole beat — pass the attack-speed
    /// motion scale so a faster caster's release animation AND its recovery both shorten together. Since release
    /// stances are FitWindow, feeding the scaled window makes the clip actually play at that speed, so the visible
    /// animation and the returned lock length can never drift apart.</summary>
    public float PlayActionFor(StanceId id, float seconds = 0f, float durationScale = 1f)
    {
        float dur = seconds;
        if (dur <= 0f && pose != null)
        {
            var b = StanceCatalog.Get(id);
            if (b != null) dur = pose.NaturalDuration(b.action, b.clip);
        }
        if (dur <= 0.05f) dur = 0.6f;   // clip missing/absent → a sane default beat
        dur *= Mathf.Max(0.01f, durationScale);
        overrideId = id; overrideWindow = dur; overrideUntil = Time.time + dur;
        return dur;
    }

    /// <summary>Cancel any active one-shot action override (e.g. death must beat a lingering release cue).</summary>
    public void CancelActionOverride() { overrideUntil = 0f; }

    // ---- held-stance layer ----

    /// <summary>Enter a held stance (idempotent). Participates in the priority resolve until <see cref="Pop"/>.
    /// <paramref name="window"/> is the FitWindow duration the stance's clip is fit to (e.g. the cast-bar length for
    /// Casting); ignored by non-FitWindow sources. Re-pushing an active stance refreshes its window.</summary>
    public void Push(StanceId id, float window = 0f)
    {
        int i = IndexOf(id);
        if (i < 0) active.Add(new Held { id = id, window = window });
        else active[i] = new Held { id = id, window = window };
    }

    /// <summary>Leave a held stance (idempotent).</summary>
    public void Pop(StanceId id)
    {
        int i = IndexOf(id);
        if (i >= 0) active.RemoveAt(i);
    }

    /// <summary>Push or pop by a bool — the natural call for a condition (e.g. Set(Combat, attacker.Engaged)).</summary>
    public void Set(StanceId id, bool on) { if (on) Push(id); else Pop(id); }

    /// <summary>Drop every held stance.</summary>
    public void Clear() => active.Clear();

    public bool IsActive(StanceId id) => IndexOf(id) >= 0;

    /// <summary>The natural clip length (seconds) of a stance on THIS visual, 0 if absent. Lets the jump controller
    /// size the landing-tail root to the actual clip (clipLen − airborneFreeze) without owning a copy of the length.</summary>
    public float NaturalDurationOf(StanceId id)
    {
        var b = StanceCatalog.Get(id);
        return b != null && pose != null ? pose.NaturalDuration(b.action, b.clip) : 0f;
    }

    /// <summary>The authored FRAME COUNT of a stance's clip on THIS visual, 0 if absent. Lets a caller turn an authored
    /// frame index (e.g. the Roll binding's rootFromFrame) into a normalized point in the clip.</summary>
    public int NaturalFrameCountOf(StanceId id)
    {
        var b = StanceCatalog.Get(id);
        return b != null && pose != null ? pose.NaturalFrameCount(b.action, b.clip) : 0;
    }

    void LateUpdate()
    {
        if (pose == null) return;

        // 0) One-shot action override (skill release) — beats even the brain's per-frame action until it expires.
        if (Time.time < overrideUntil)
        {
            var ob = StanceCatalog.Get(overrideId);
            if (ob != null) { driving = true; currentResolved = overrideId; ApplyStance(ob, overrideWindow); return; }
        }

        // 1) Action stance beats everything.
        if (hasAction)
        {
            var b = StanceCatalog.Get(actionId);
            if (b != null) { driving = true; currentResolved = actionId; ApplyStance(b, actionWindow, actionRelease); return; }
        }

        // 2) Highest-priority active, BAKED held stance. Ties break toward the most-recently pushed (active is in push
        // order, so scanning with >= keeps the later one). Zero-alloc index loop.
        StanceBinding best = null;
        float bestWindow = 0f;
        int bestPri = int.MinValue;
        for (int i = 0; i < active.Count; i++)
        {
            var b = StanceCatalog.Get(active[i].id);
            if (b == null || !pose.HasAction(b.action, b.clip)) continue;
            if (b.priority >= bestPri) { bestPri = b.priority; best = b; bestWindow = active[i].window; }
        }
        if (best != null)
        {
            driving = true; currentResolved = best.id;
            ApplyStance(best, bestWindow);
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
            if (idle != null) ApplyStance(idle, 0f); else pose.Play(CharacterAction.Idle, "");
            currentResolved = StanceId.Idle;
            driving = false;
        }
    }

    // Play a resolved binding on the billboard: its action, its playback mode, and its live-modulated speed.
    void ApplyStance(StanceBinding b, float window, bool release = false)
    {
        // Jump + Fall drive the clip in phases: `window` carries the freeze point (seconds), `release` plays the
        // landing tail. Fall (ledge walk-off) reuses the Jump binding's airborneFreeze — the brain feeds it here.
        if (b.id == StanceId.Jump || b.id == StanceId.Fall) { pose.PlayClipPhase(b.action, b.clip, window, release, Mathf.Max(0.01f, b.speedMult)); return; }

        pose.Play(b.action, b.clip);
        float m = b.speedMult;
        switch (b.speedSource)
        {
            case StanceSpeedSource.AttackSpeed: m *= Character.Instance != null ? Character.Instance.AttackSpeedMult : 1f; break;
            case StanceSpeedSource.CastSpeed:   m *= Character.Instance != null ? Character.Instance.CastSpeedMult   : 1f; break;
            case StanceSpeedSource.MoveGait:    m *= playerCtrl != null ? playerCtrl.GaitSpeedMult : externalGaitMult; break;
            case StanceSpeedSource.FitWindow:
                float n = pose.NaturalDuration(b.action, b.clip);
                if (window > 0.01f && n > 0.01f) m *= n / window;
                break;
        }
        pose.SetActionSpeed(b.action, Mathf.Max(0.01f, m));
    }
}
