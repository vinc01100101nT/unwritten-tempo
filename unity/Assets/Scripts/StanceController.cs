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
    int actionBeat;
    // The action and beat that were ON SCREEN last frame. A new beat of the same action starts its clip over from the
    // first frame; anything else (a different action, or no action last frame) enters it the ordinary way.
    bool shownAction;
    StanceId shownActionId;
    int shownActionBeat;

    // Self-expiring action OVERRIDE — a fire-and-forget one-shot (a skill release) that outranks even the brain's
    // per-frame action, so it can't be stomped by locomotion mid-play. Set once via PlayActionFor; auto-clears when
    // Time.time passes overrideUntil. overrideWindow is the FitWindow length its clip is fit to (plays through once).
    StanceId overrideId;
    float overrideUntil;
    float overrideWindow;
    bool overrideFit;                // this play was asked to fit its window, whatever the stance's own speed source
    // Where in the clip this override must START, as a 0→1 fraction; negative = at the beginning. Set when a beat
    // that began before we heard about it is joined part-way through, and spent on the first frame it is applied.
    float overrideSeekTo = -1f;
    // The override that was ON SCREEN last frame. A new play of the same one starts its clip over from the first
    // frame — the state is already current, so entering it the ordinary way does nothing, and back-to-back beats of
    // a clip that does not loop left the body holding its last frame for as long as the beats kept coming.
    bool shownOverride;
    StanceId shownOverrideId;

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
    }

    // How fast this body is travelling over the ground right now, handed over every frame by the shared brain.
    float groundSpeed;

    /// <summary>How fast this body is travelling over the ground, in units per second — set every frame by
    /// <see cref="CharacterBrain.Apply"/>, the one place every kind of body is driven from.</summary>
    public void SetGroundSpeed(float unitsPerSecond) { groundSpeed = Mathf.Max(0f, unitsPerSecond); }

    /// <summary>THE LEGS OF A WALK, RUN OR SPRINT ARE LOCKED TO THE GROUND: the clip plays at the body's real ground
    /// speed divided by how fast that clip walks on this body, so a planted foot stays still at any speed — bare feet,
    /// shoes, a buff or a slow. Only a live-3D body whose clip has been measured can answer; anything else (a flat
    /// billboard, a clip never measured) plays at its own speed.</summary>
    bool TryGroundLocked(StanceBinding b, out float playback)
    {
        playback = 1f;
        if (!(pose is Character3DPose body) || !body.TryClipSpeed(b, out float clipSpeed)) return false;
        playback = StrideMath.LockedPlayback(groundSpeed, clipSpeed);
        return true;
    }

    /// <summary>Claim this as the PLAYER's controller: register it as <see cref="Player"/> (so cast/combat systems
    /// can push held stances onto it) and switch its fallback to hold the authored Idle stance instead of self-drive.</summary>
    public void BecomePlayer() { Player = this; selfDrive = false; }

    /// <summary>The visual this controller drives — the same answer <see cref="Character3DVisual.Pose"/> and
    /// <see cref="Agent3DVisual.Pose"/> give, under the same name. Reach for it to ask a body something about
    /// itself that only the visual knows, such as which way it has actually turned: on a live-3D character the
    /// model yaws and the root it hangs under never does.</summary>
    public IStancePose Pose => pose;

    void OnDestroy() { if (Player == this) Player = null; }

    // ---- action-stance layer (transient) ----

    /// <summary>Drive an action stance THIS frame (overrides any held stance). <paramref name="window"/> is the
    /// runtime duration a FitWindow stance fits its clip to (e.g. attacker.SwingTime, dash length); ignored for other
    /// sources. Call every frame the action holds; <see cref="ClearAction"/> when it ends. <paramref name="beat"/>
    /// names which beat of this action it is: a new number while the same action is showing starts its clip over.</summary>
    public void DriveStance(StanceId id, float window = 0f, bool release = false, int beat = 0)
    {
        hasAction = true; actionId = id; actionWindow = window; actionRelease = release; actionBeat = beat;
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
    /// whatever the beat's length is, and the stance may well be authored to its own speed instead.</param>
    /// <param name="startedSecondsAgo">How long ago this action actually BEGAN. 0 for anything happening now. A
    /// positive value joins the action part-way through — the clip enters at that fraction and holds for what is
    /// left of it, rather than replaying from the start. This is how a body watched from another machine picks up
    /// an action that was already running when we started watching; an action already over plays nothing.</param>
    public float PlayActionFor(StanceId id, float seconds = 0f, float durationScale = 1f, bool fitToWindow = false,
                               float startedSecondsAgo = 0f)
    {
        float played = ActionDuration(id, seconds, durationScale, fitToWindow);

        float left = played - Mathf.Max(0f, startedSecondsAgo);
        if (left <= 0f) return 0f;

        overrideId = id; overrideWindow = played; overrideUntil = Time.time + left; overrideFit = fitToWindow;
        // The clip is still fit to its WHOLE length, so it plays at the right speed; only the entry point moves.
        overrideSeekTo = startedSecondsAgo > 0f ? Mathf.Clamp01(startedSecondsAgo / played)
                       : shownOverride && shownOverrideId == id ? 0f : -1f;

        // SAID OUT LOUD, so a skill's release swing happens on every screen and not only the caster's. It travels as
        // a MOMENT — what, and when it began — because that is the shape it is: it plays once and it is over.
        //
        // Only from the body at THIS keyboard, and only for a beat starting NOW. A watched body reaches this same
        // line when it is told about somebody else's beat, and reporting from there would bounce that beat straight
        // back out again.
        //
        // WHAT IS SENT WITH IT IS A WINDOW THE PLAY MUST FIT INSIDE, and only GAMEPLAY owns such a window — a repeat
        // beat, a swing cadence, a dash length. A caller that named one is passed on; a caller that named none is
        // asking for the animation's own length, which belongs to the ART, and every machine has its own copy of the
        // art and must read it there. Sending a length in that case is what made a skill's ending run visibly shorter
        // for everybody else than for the person casting it: the watching side takes the SHORTER of the two answers,
        // so the caster's number could only ever cut its own play short, never lengthen it, and any disagreement at
        // all between the two machines came out as a clipped animation on every screen but the caster's.
        float windowToTell = (fitToWindow || seconds > 0f) ? played : 0f;
        if (Player == this && startedSecondsAgo <= 0f) GameNetwork.ReportActionBegan(id, windowToTell);
        return played;
    }

    /// <summary>How long <paramref name="id"/> WOULD play for, without playing it — the same number
    /// <see cref="PlayActionFor"/> roots the caster for. Split out so anything that must last "exactly as long as the
    /// animation" reads the clip rather than a seconds field typed next to it: a hurt reaction and the movement stop
    /// that goes with it have to be the same length, and two hand-typed numbers is how they drift apart.</summary>
    // The slowest pace a held action is timed at — see ActionDuration.
    const float MinPaceForAHold = 0.25f;

    public float ActionDuration(StanceId id, float seconds = 0f, float durationScale = 1f, bool fitToWindow = false)
    {
        var b = StanceCatalog.Get(id);
        float natural = (pose != null && b != null) ? pose.NaturalDuration(b.action, b.clip) : 0f;
        if (natural <= 0.05f) natural = 0.6f;   // clip missing/absent → a sane default beat

        bool fit = fitToWindow || b == null || b.speedSource == StanceSpeedSource.FitWindow;
        float dur;
        if (!fit)
            // Intrinsic-speed stance (Baked, or a walk cycle): the clip plays at its OWN resolved speed, so the lock
            // must equal its real play time = natural / that speed. durationScale (an attack-speed hint for FitWindow)
            // does NOT apply — the speed source already owns the timing, so the lock ends exactly when the animation does.
            // NEVER LONGER THAN FOUR TIMES ITS OWN LENGTH: this number roots the caster, and a pace read near 0 once
            // turned a 1.2 s release into a two-minute freeze. A pace that slow is a fault, not a speed anybody chose.
            dur = natural / Mathf.Max(MinPaceForAHold, ResolveSpeedMult(b, 0f));   // server-math: how long this machine's own body plays an animation — the movement exception, no game number
        else
            // FitWindow release stance: the caller feeds the window (seconds, or the natural length), scaled by
            // durationScale; the clip is fit to it, so play time == the window.
            dur = (seconds > 0f ? seconds : natural) * Mathf.Max(0.01f, durationScale);

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

    /// <summary>The resolved PLAYBACK multiplier for a stance on THIS unit — the exact value ApplyStance feeds
    /// the pose. The jump brain reads it to shrink its rooted crouch/land timers by the same factor the clip is sped up,
    /// so the root and the visible wind-up/tail can't drift (single owner of the multiplier). Respects the stance's
    /// speedSource: a Baked Jump plays at its own speedMult, a FitWindow one fits its window.</summary>
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

    // WHAT WAS LAST SAID TO THE MACHINES WATCHING THIS BODY. Kept so the report below is a change gate: nothing goes
    // out on a frame where nothing moved, which is almost every frame.
    bool saidHasPose;
    StanceId saidPose;
    float saidPoseWindow;
    bool saidPoseRelease;
    int saidPoseBeat;
    bool saidHasHeld;
    StanceId saidHeld;
    float saidHeldWindow;

    // Below this, a window has not really changed — a float that wobbles in its last digits must not spend a packet.
    const float WindowEpsilon = 0.001f;

    /// <summary>Tell the world what this body settled on, whenever it settles on something new.
    ///
    /// <para><b>Why it is here and nowhere else.</b> This is the one place every pose in the game passes through, so
    /// a pose reported from here is a pose that cannot be forgotten — and a pose added to the game later travels
    /// without anybody wiring it up. Reporting from each of the places that DECIDE a pose instead would be one wiring
    /// job per pose, and the one somebody forgets is the one nobody ever sees.</para>
    ///
    /// <para><b>What is deliberately NOT reported, and why.</b> A travel gear, because every machine already works
    /// walking out from the speed and the gear it is told; and a weapon swing, because the server announces that
    /// itself from the weapon IT holds. Saying either again would make two owners of one answer — the swing would
    /// replay from the top on every screen, and the walk would fight the speed. Which is which is the stance
    /// vocabulary's own question to answer, not a list kept here.</para>
    ///
    /// <para>Only the body at THIS keyboard reports; a watched copy of somebody else is showing what it was told and
    /// has nothing of its own to say.</para></summary>
    void ReportToWatchers(bool hasPose, StanceId poseId, float window, bool release, int beat,
                          bool hasHeld, StanceId heldId, float heldWindow)
    {
        if (hasPose && !StanceCatalog.WatchersMustBeTold(poseId)) hasPose = false;

        if (hasPose != saidHasPose || (hasPose && (poseId != saidPose || release != saidPoseRelease || beat != saidPoseBeat
                                                   || Mathf.Abs(window - saidPoseWindow) > WindowEpsilon)))
        {
            saidHasPose = hasPose; saidPose = poseId; saidPoseWindow = window; saidPoseRelease = release; saidPoseBeat = beat;
            GameNetwork.ReportDrivenStance(hasPose, poseId, window, release, beat);
        }

        if (hasHeld != saidHasHeld || (hasHeld && (heldId != saidHeld
                                                   || Mathf.Abs(heldWindow - saidHeldWindow) > WindowEpsilon)))
        {
            saidHasHeld = hasHeld; saidHeld = heldId; saidHeldWindow = heldWindow;
            GameNetwork.ReportHeldStance(hasHeld, heldId, heldWindow);
        }
    }

    /// <summary>FORGET WHAT HAS ALREADY BEEN SAID, so the very next frame says all of it again.
    ///
    /// <para>Called when this body arrives in a world — a first sign-in, or walking through a door into a zone
    /// another server holds. The new world starts out believing this body is doing nothing, and the change gate
    /// above would agree with it and stay silent: a person who walked through that door holding a pose would keep
    /// it on their own screen and hold none on anybody else's, until the next time they happened to change.</para></summary>
    public void ForgetWhatWasSaid()
    {
        saidHasPose = false; saidPose = default; saidPoseWindow = 0f; saidPoseRelease = false; saidPoseBeat = 0;
        saidHasHeld = false; saidHeld = default; saidHeldWindow = 0f;
    }

    /// <summary>The held stance currently winning, by priority alone — the answer this body is REPORTING, which is
    /// not quite the answer it PLAYS. Whether a body has the art for a stance is a question about that machine's own
    /// copy of the character, so each one asks it of itself; what travels is which stance was pushed.
    ///
    /// <para>Walks the one or two stances a body holds at once — an index loop over a short list, no allocation.</para></summary>
    bool HeldWinner(out StanceId id, out float window)
    {
        id = default; window = 0f;
        int bestPri = int.MinValue;
        bool found = false;
        for (int i = 0; i < active.Count; i++)
        {
            var b = StanceCatalog.Get(active[i].id);
            if (b == null) continue;
            if (b.priority >= bestPri) { bestPri = b.priority; id = active[i].id; window = active[i].window; found = true; }
        }
        return found;
    }

    void LateUpdate()
    {
        if (pose == null) return;

        // SAID BEFORE IT IS DRAWN, and from one place rather than from each of the branches below — every one of
        // which returns, so a report written inside them would be three reports and one of them would rot.
        if (Player == this)
        {
            bool hasHeld = HeldWinner(out var heldId, out float heldWindow);
            ReportToWatchers(hasAction, actionId, actionWindow, actionRelease, actionBeat, hasHeld, heldId, heldWindow);
        }

        // Cleared here and set again only by the action branch below, so every other branch counts as "no action
        // on screen" and the next action enters the ordinary way.
        bool actionWasShown = shownAction;
        shownAction = false;
        shownOverride = false;

        // 0) One-shot action override (skill release) — beats even the brain's per-frame action until it expires.
        if (Time.time < overrideUntil)
        {
            var ob = StanceCatalog.Get(overrideId);
            if (ob != null)
            {
                driving = true; currentResolved = overrideId;
                shownOverride = true; shownOverrideId = overrideId;
                float seek = overrideSeekTo;
                // Spent here, not in the setter: it must be applied on the first frame this override actually
                // renders, and cleared so the following frames do not keep snapping back to the entry point.
                overrideSeekTo = -1f;
                ApplyStance(ob, overrideWindow, forceFit: overrideFit, seekTo: seek);
                return;
            }
        }

        // 1) Action stance beats everything.
        if (hasAction)
        {
            var b = StanceCatalog.Get(actionId);
            if (b != null)
            {
                bool newBeat = actionWasShown && shownActionId == actionId && shownActionBeat != actionBeat;
                shownAction = true; shownActionId = actionId; shownActionBeat = actionBeat;
                driving = true; currentResolved = actionId;
                ApplyStance(b, actionWindow, actionRelease, seekTo: newBeat ? 0f : -1f);
                return;
            }
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
            bool fresh = currentResolved != best.id;
            driving = true; currentResolved = best.id;
            ApplyStance(best, bestWindow, restart: fresh);
            return;
        }

        // 3) Nothing active — hand back to the fallback.
        if (selfDrive)
        {
            pose.SetShownStance(StanceId.Walk);   // the visual's own Idle/Walk/Run: the carry arms, never the whole-body stand
            if (driving) { pose.ResumeSelfDrive(); driving = false; }   // ONCE — ResumeSelfDrive re-bases movement sampling
        }
        else
        {
            var idle = StanceCatalog.Get(StanceId.Idle);
            if (idle != null) ApplyStance(idle, 0f); else { pose.SetShownStance(StanceId.Idle); pose.Play(CharacterAction.Idle, ""); }
            currentResolved = StanceId.Idle;
            driving = false;
        }
    }

    // Play a resolved binding on the billboard: its action, its playback mode, and its live-modulated speed.
    void ApplyStance(StanceBinding b, float window, bool release = false, bool forceFit = false, float seekTo = -1f,
                     bool restart = false)
    {
        pose.SetShownStance(b.id);
        // Jump + Fall drive the clip in phases: `window` carries the freeze point (seconds), `release` plays the
        // landing tail. Fall (ledge walk-off) reuses the Jump binding's airborneFreeze — the brain feeds it here.
        if (b.id == StanceId.Jump || b.id == StanceId.Fall) { pose.PlayClipPhase(b.action, b.clip, window, release, ResolveSpeedMult(b, window)); return; }
        // A HELD POSE THAT STOPS PART-WAY: the clip reaches holdAt, then stays on that frame for as long as the stance
        // is held — the same phase drive the jump's airborne freeze uses. Fit Window = the body glides into that frame
        // across the whole window (a bow drawn over the cast); otherwise the clip plays up to it at its own speed.
        if (b.holdAt > 0f)
        {
            float glide = b.speedSource == StanceSpeedSource.FitWindow && window > 0.01f ? window : 0f;
            pose.PlayClipPhase(b.action, b.clip, b.holdAt, false, ResolveSpeedMult(b, window), restart, glide);
            return;
        }

        if (seekTo >= 0f) pose.PlayFrom(b.action, b.clip, seekTo);
        else pose.Play(b.action, b.clip);
        pose.SetActionSpeed(b.action, ResolveSpeedMult(b, window, forceFit));
    }

    // The live playback-speed multiplier for a stance, resolved from its speedSource. SINGLE OWNER used by BOTH the
    // per-frame drive (ApplyStance) and the aftercast-lock duration (PlayActionFor), so the visible animation length
    // and the lock length can never drift apart — whatever the speed source. `window` is only read for FitWindow.
    float ResolveSpeedMult(StanceBinding b, float window, bool forceFit = false)
    {
        float m = b.speedMult;
        // forceFit REPLACES the authored source rather than stacking on it: a caller that says "this play must last
        // exactly this long" owns the timing outright, and multiplying another speed on top would break the very fit it asked
        // for. Without it, behaviour is identical to before.
        bool walkCycle = StanceCatalog.IsWalkCycle(b.id);
        if (forceFit || (!walkCycle && b.speedSource == StanceSpeedSource.FitWindow))
        {
            float n = pose.NaturalDuration(b.action, b.clip);
            if (window > 0.01f && n > 0.01f) m *= n / window;
        }
        // WALK, RUN AND SPRINT — AND NOTHING ELSE — follow the ground: locked to the body's real ground speed when the
        // clip's stride was measured (every body is measured, and an unmeasured human borrows the average stride), else
        // their own speed. Their saved speedSource is never read, so a movement buff or a root can never change how
        // long any other animation takes.
        else if (walkCycle && TryGroundLocked(b, out float locked)) m *= locked;
        return Mathf.Max(0.01f, m);
    }
}
