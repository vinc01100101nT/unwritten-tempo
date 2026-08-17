using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The single runtime CHOKE POINT that drives a unit's <see cref="IStancePose"/> (the live-3D
/// <see cref="Character3DPose"/>) from the data-authored <see cref="StanceLibrary"/> — the persistent-state sibling
/// of <see cref="VfxCue"/>. Nothing else calls <see cref="IStancePose.Play"/> for a unit that has one; callers feed
/// intent here and this resolves it every frame, so two systems can never fight over the pose (the dual-path bug).
///
/// <para>Two layers, highest first:
/// <list type="number">
/// <item><b>Action stance</b> (<see cref="DriveStance"/>) — the transient the gameplay brain drives one at a time
/// (Idle/Walk/Run/Attack/Jump/Roll/Hurt/PickUp/Die/Fall). A FitWindow stance is fed a RUNTIME window (swing/dash time)
/// so its clip fits the window — data a static library can't own without hand-copying it (an SSOT violation).</item>
/// <item><b>Held stance</b> (<see cref="Push"/>/<see cref="Pop"/>) — persistent baselines (Combat/Guard/Casting/Sit);
/// the highest-<see cref="StanceBinding.priority"/> active one wins.</item>
/// </list>
/// Fallback when neither is set: a self-driving unit (<see cref="selfDrive"/> = true) hands back to the sink's
/// movement-sampled Idle/Walk/Run; the player (<see cref="selfDrive"/> = false) plays the authored Idle stance.</para>
///
/// <para>Resolves in <c>LateUpdate</c> at execution order −50 — after gameplay brains that feed it
/// (<see cref="Character3DVisual"/> at −100) and before <see cref="Character3DPose"/>'s render at 0.</para>
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public class StanceController : MonoBehaviour
{
    [Tooltip("Fallback when NO action or held stance is active. true = hand back to the sink's self-drive " +
             "(NPCs sample their own movement); false = play the authored Idle stance (the player).")]
    public bool selfDrive = true;

    /// <summary>The player's stance controller — the one gameplay systems (cast, combat, sit) push held stances onto.
    /// Set when <see cref="Character3DVisual"/> claims its controller via <see cref="BecomePlayer"/>. Mirrors
    /// <see cref="Character.Instance"/> / <c>StatusController.Player</c>.</summary>
    public static StanceController Player { get; private set; }

    // The presentation sink this choke point drives — a baked billboard OR a live-3D Character3DPose. Resolved
    // via the IStancePose interface so swapping the visual backend doesn't touch this resolver.
    IStancePose pose;
    Player3DController playerCtrl;   // optional — resolves the MoveGait source

    // An AI unit's own status host, so its leg cadence answers to ITS Slow/Haste. Optional: a unit with no status
    // host simply never slows or hastes, and reads the shared baseline unchanged.
    StatusController status;

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
    bool overrideFit;                // this play was asked to fit its window, whatever the stance's own speed source

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
        status = GetComponent<StatusController>();
    }

    /// <summary>This unit's live leg cadence — ONE rule for every character.
    ///
    /// <para>The player's controller already folds baseline x live Slow/Haste x gear tier each frame. An AI unit used
    /// to carry a value FROZEN at spawn instead, so a hasted townsperson or shadow kept walking at the old cadence
    /// while it visibly sped up. It now folds the same three terms from the same shared baseline, reading its OWN
    /// Slow/Haste and the gear it is actually playing.</para></summary>
    float GaitFor(StanceId id)
    {
        if (playerCtrl != null) return playerCtrl.GaitSpeedMult;
        var spec = CharacterSpec.Active;
        // Run legs churn at the run gear so the slower travel doesn't foot-skate — the same tier rule the player uses.
        float gearAnimTier = id == StanceId.Run ? spec.runGearMult : 1f;
        return spec.GaitMult(status != null ? status.MoveMult : 1f, gearAnimTier);
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
    /// <param name="fitToWindow">Force the clip to fit <paramref name="seconds"/> even when the stance's own speed
    /// source is not FitWindow. A repeating skill needs this: its release animation must play through once per beat at
    /// whatever the beat's length is, and the stance may well be authored to a gait instead.</param>
    public float PlayActionFor(StanceId id, float seconds = 0f, float durationScale = 1f, bool fitToWindow = false)
    {
        var b = StanceCatalog.Get(id);
        float natural = (pose != null && b != null) ? pose.NaturalDuration(b.action, b.clip) : 0f;
        if (natural <= 0.05f) natural = 0.6f;   // clip missing/absent → a sane default beat

        bool fit = fitToWindow || b == null || b.speedSource == StanceSpeedSource.FitWindow;
        float dur;
        if (!fit)
            // Intrinsic-speed stance (MoveGait / AttackSpeed / CastSpeed / Baked): the clip plays at its OWN resolved
            // speed, so the lock must equal its real play time = natural / that speed. durationScale (an attack-speed
            // hint for FitWindow) does NOT apply — the speed source already owns the timing. This is what stops a
            // MoveGait release stance from over-rooting: the lock now ends exactly when the animation does.
            dur = natural / ResolveSpeedMult(b, 0f);
        else
            // FitWindow release stance: the caller feeds the window (seconds, or the natural length), scaled by
            // durationScale; the clip is fit to it, so play time == the window.
            dur = (seconds > 0f ? seconds : natural) * Mathf.Max(0.01f, durationScale);

        overrideId = id; overrideWindow = dur; overrideUntil = Time.time + dur; overrideFit = fitToWindow;
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

    /// <summary>The gait/stat-resolved PLAYBACK multiplier for a stance on THIS unit — the exact value ApplyStance feeds
    /// the pose. The jump brain reads it to shrink its rooted crouch/land timers by the same factor the clip is sped up,
    /// so the root and the visible wind-up/tail can't drift (single owner of the multiplier). Respects the stance's
    /// speedSource: a MoveGait Jump scales with GaitSpeedMult, a Baked one stays at 1×.</summary>
    public float ResolvedSpeedMultOf(StanceId id)
    {
        var b = StanceCatalog.Get(id);
        return b != null && pose != null ? ResolveSpeedMult(b, 0f) : 1f;
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
            if (ob != null) { driving = true; currentResolved = overrideId; ApplyStance(ob, overrideWindow, forceFit: overrideFit); return; }
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
    void ApplyStance(StanceBinding b, float window, bool release = false, bool forceFit = false)
    {
        // Jump + Fall drive the clip in phases: `window` carries the freeze point (seconds), `release` plays the
        // landing tail. Fall (ledge walk-off) reuses the Jump binding's airborneFreeze — the brain feeds it here.
        if (b.id == StanceId.Jump || b.id == StanceId.Fall) { pose.PlayClipPhase(b.action, b.clip, window, release, ResolveSpeedMult(b, window)); return; }

        pose.Play(b.action, b.clip);
        pose.SetActionSpeed(b.action, ResolveSpeedMult(b, window, forceFit));
    }

    // The live playback-speed multiplier for a stance, resolved from its speedSource. SINGLE OWNER used by BOTH the
    // per-frame drive (ApplyStance) and the aftercast-lock duration (PlayActionFor), so the visible animation length
    // and the lock length can never drift apart — whatever the speed source. `window` is only read for FitWindow.
    float ResolveSpeedMult(StanceBinding b, float window, bool forceFit = false)
    {
        float m = b.speedMult;
        // forceFit REPLACES the authored source rather than stacking on it: a caller that says "this play must last
        // exactly this long" owns the timing outright, and multiplying a gait on top would break the very fit it asked
        // for. Without it, behaviour is identical to before.
        if (forceFit || b.speedSource == StanceSpeedSource.FitWindow)
        {
            float n = pose.NaturalDuration(b.action, b.clip);
            if (window > 0.01f && n > 0.01f) m *= n / window;
        }
        else if (b.speedSource == StanceSpeedSource.MoveGait) m *= GaitFor(b.id);
        return Mathf.Max(0.01f, m);
    }
}
