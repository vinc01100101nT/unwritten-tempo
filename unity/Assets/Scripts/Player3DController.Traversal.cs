using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Player traversal abilities — Space = a REAL, free-directional jump arc (steered live by WASD, even
/// mid-air); E = a facing-direction dash burst on a cooldown. These are model-agnostic movement moves: the
/// VISUAL is driven by whatever animation path is active (the baked billboard reads <see cref="IsAirborne"/>
/// / <see cref="EDashing"/> to play its Jump / Roll clips). Split out of <see cref="Player3DController"/> so
/// the movement logic lives apart from the per-model animation code.
///
/// <para>Space jumps a real vertical arc (the engine's cast shadow stays on the ground); air time is derived
/// from the peak height + gravity (t = 2·√(2h/g)) so it stays realistic. E dashes in the facing direction with
/// i-frames for the whole burst, then goes on cooldown.</para>
/// </summary>
public partial class Player3DController
{
    [Header("Jump (Space) — real arc")]
    [Tooltip("World units the hop travels horizontally in the current move/facing direction.")]
    public float jumpDistance = 2.4f;
    [Tooltip("Peak jump height in world units. A REAL vertical arc — the body rises (the engine's cast shadow " +
             "stays on the ground). Air time is derived from this + gravity (t = 2·√(2h/g)). Scales with UnitScale.ForPlayer.")]
    public float jumpHeight = 0.8f;

    [Header("Dash (E) — facing-direction burst")]
    [Tooltip("Roll speed as a multiple of the player's CURRENT run speed (CurrentRunSpeed). 2 = roll twice as fast " +
             "as you're currently running, so it always tracks move-speed buffs/slows.")]
    public float eDashSpeedMult = 0.85f;
    public float eDashDuration = 1f;
    public float eDashCooldown = 2f;

    // ---- runtime state -------------------------------------------------------------------------------
    bool jumping; float jumpStart; Vector3 jumpDir;

    // The jump plays in three phases keyed to the Jump clip's authored timestamps (Stance Composer → the Jump
    // binding's crouchEnd / airborneFreeze, the single owner). Crouch + Land ROOT the player (gravity only, WASD
    // ignored); Air is the free-directional arc. The clip freezes at airborneFreeze all through Air, then plays its
    // tail during Land.
    enum JumpPhase { None, Crouch, Air, Land }
    JumpPhase jumpPhase;
    float jumpPhaseStart;                    // Time.time the current phase began
    bool jumpRollQueued;                     // E pressed mid-air → roll-cancel the landing (if the dash is off cooldown)
    float jumpCrouchEnd, jumpFreeze, jumpTailDur;   // cached from the Jump binding (+ its clip length) at takeoff

    /// <summary>True while a jump is in progress (crouch → air → landing recovery). Read by FX like wet-ground
    /// footstep ripples so they pause mid-air and splash on landing, and by the visual to play its Jump clip.</summary>
    public bool IsAirborne => jumping;
    /// <summary>Seconds into the Jump clip where the airborne pose freezes (the Jump binding owns it). The stance
    /// brain feeds this to the pose so the clip holds that frame for the whole fall.</summary>
    public float JumpClipFreeze => jumpFreeze;
    /// <summary>True only during the landing-recovery phase — tells the pose to release the frozen clip and play its
    /// landing tail once.</summary>
    public bool JumpClipReleasing => jumpPhase == JumpPhase.Land;
    float jumpBaseY;       // ground Y captured at takeoff — the arc returns here, then gravity falls to land
    bool eDashing; float eDashStart; Vector3 eDashDir; float eDashReadyAt;
    // Time.time at which the roll becomes ROOTED (no move) for the rest of the dash — derived from the Roll binding's
    // rootFromFrame at takeoff. Equals dash end (never roots) when no root frame is authored.
    float eDashRootAt;

    /// <summary>True during the E-key dash burst. Exposed so the baked visual can play Roll for it
    /// (the charge gap-closer uses <see cref="Dashing"/>; this is the free traversal dash).</summary>
    public bool EDashing => eDashing;

    // ---- traversal abilities (called early in Update; returns true when it owns the frame) ----
    bool TickTraversalAbilities()
    {
        if (eDashing) { StepEDash(); transform.localScale = baseScale; return true; }
        if (jumping)  { StepJump();  transform.localScale = baseScale; return true; }

        // Same input guards as ReadInput so a panel/skill-aim click doesn't also hop/dash.
        if (DevSettings.Blocking || GroundTargeter.Active || UnitTargeter.Active) return false;

        if (KeyDash() && Time.time >= eDashReadyAt)
        {
            BeginEDash(AbilityDir());
            StepEDash(); transform.localScale = baseScale; return true;
        }
        if (KeyJump())
        {
            // Latch the Jump clip's timestamps ONCE (cold path, per jump): crouchEnd + airborneFreeze from the Jump
            // binding, plus the clip length for the landing-tail root. The binding is the single owner.
            var jb = StanceCatalog.Get(StanceId.Jump);
            // The × mult speeds up the CLIP playback (Character3DPose.PlayClipPhase), so the rooted CROUCH and LAND
            // phases must shrink by the same factor to stay glued to the visible wind-up/tail — otherwise the sped-up
            // clip finishes early and the player stands frozen while the phase timer runs out. Freeze stays in NATURAL
            // seconds: the pose normalizes it (freezeN), which is mult-independent, and Air is physics-driven — so gait
            // scales the wind-up + landing while the airborne hold stays neutral. Read the SAME resolved mult the pose
            // uses (gait-inclusive when the Jump stance is MoveGait), so root and clip can't drift.
            float jumpMult = StanceController.Player != null
                ? StanceController.Player.ResolvedSpeedMultOf(StanceId.Jump)
                : (jb != null ? Mathf.Max(0.01f, jb.speedMult) : 1f);
            jumpCrouchEnd = (jb != null ? Mathf.Max(0f, jb.crouchEnd) : 0f) / jumpMult;
            jumpFreeze    = jb != null ? Mathf.Max(0f, jb.airborneFreeze) : 0f;
            float clipLen = StanceController.Player != null ? StanceController.Player.NaturalDurationOf(StanceId.Jump) : 0f;
            jumpTailDur   = Mathf.Max(0f, clipLen - jumpFreeze) / jumpMult;

            // Begin in the CROUCH phase (rooted). StepJump launches into the arc once crouchEnd elapses. Direction is
            // steered LIVE in StepJump (re-reads WASD each air frame), so nothing to latch here.
            jumping = true; jumpPhase = JumpPhase.Crouch; jumpPhaseStart = Time.time; jumpRollQueued = false;
            StepJump(); transform.localScale = baseScale; return true;
        }
        return false;
    }

    // Current WASD direction, or the last move/aim facing when standing still.
    Vector3 AbilityDir()
    {
        Vector3 wasd = ReadWasd();
        if (wasd.sqrMagnitude > 1e-4f) return wasd.normalized;
        Vector3 f = worldFacing; f.y = 0f;
        return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
    }

    // Realistic air time for the current peak height under the player's gravity: t = 2·√(2h/g).
    float JumpAirTime => 2f * Mathf.Sqrt(2f * Mathf.Max(0.01f, jumpHeight * UnitScale.ForPlayer) / Mathf.Max(0.1f, gravity));

    void StepJump()
    {
        float now = Time.time;

        // ---- CROUCH: rooted on the ground (gravity only, WASD ignored) while the clip plays its take-off wind-up. ----
        if (jumpPhase == JumpPhase.Crouch)
        {
            controller.Move(Vector3.down * gravity * Time.deltaTime);
            if (now - jumpPhaseStart >= jumpCrouchEnd)
            {
                jumpPhase = JumpPhase.Air; jumpPhaseStart = now;
                jumpStart = now; jumpBaseY = transform.position.y;   // launch: latch the arc's takeoff time + height
            }
            return;
        }

        // ---- LAND: rooted (gravity only) while the clip plays its landing tail; ends after the tail's length. ----
        if (jumpPhase == JumpPhase.Land)
        {
            controller.Move(Vector3.down * gravity * Time.deltaTime);
            if (now - jumpPhaseStart >= jumpTailDur) { jumping = false; jumpPhase = JumpPhase.None; }
            return;
        }

        // ---- AIR: the free-directional arc (steered live by WASD every frame). ----
        // Buffer an E press so touchdown can roll-cancel the landing. We latch INTENT only (regardless of cooldown);
        // whether it becomes a roll is decided against the cooldown at the moment we'd start it (touchdown).
        if (KeyDash()) jumpRollQueued = true;

        float air = JumpAirTime;
        float elapsed = now - jumpStart;

        // FULLY free-directional, even mid-air: re-read WASD EVERY frame so releasing or switching keys steers
        // the hop live (release W mid-air -> stops forward; tap A mid-air -> moves and faces left). No key held
        // -> jumpDir zero -> no horizontal travel, the body just rises/falls in place.
        Vector3 wasd = ReadWasd();
        jumpDir = wasd.sqrMagnitude > 1e-4f ? wasd.normalized : Vector3.zero;
        float fwd = jumpDistance * UnitScale.ForPlayer / air;
        Vector3 delta = jumpDir * fwd * Time.deltaTime;

        if (elapsed < air)
        {
            // Scripted arc: the vertical lift is driven DIRECTLY (no gravity) as a symmetric parabola back to takeoff
            // height. The engine's cast shadow tracks the lifted body for a natural jump shadow.
            float u = Mathf.Clamp01(elapsed / air);
            float h = jumpHeight * UnitScale.ForPlayer * 4f * u * (1f - u);   // 0 -> peak (u=0.5) -> 0
            delta.y = (jumpBaseY + h) - transform.position.y;
            controller.Move(delta);
        }
        else
        {
            // The arc is done but we may still be high in the air (hopped off a tall tree/ledge). Keep FALLING under
            // gravity until we actually touch the ground, then enter the rooted landing recovery.
            delta.y = -gravity * Time.deltaTime;
            controller.Move(delta);
            if (controller.isGrounded)
            {
                OnJumpLand();   // impact splash regardless of what follows

                // Roll-cancel: an E buffered mid-air replaces the landing recovery with the actual Roll — but only if
                // the dash is off cooldown. Exactly one of {roll, land} runs; they never overlap. End the jump and hand
                // to the E-dash (its own stance/physics/i-frames take over next frame via TickTraversalAbilities).
                if (jumpRollQueued && Time.time >= eDashReadyAt)
                {
                    jumping = false; jumpPhase = JumpPhase.None; jumpRollQueued = false;
                    BeginEDash(AbilityDir());   // i-frames for the roll burst, same as a grounded dash
                }
                else { jumpPhase = JumpPhase.Land; jumpPhaseStart = now; }
            }
        }

        if (jumpDir.sqrMagnitude > 1e-4f) worldFacing = jumpDir;   // re-face toward live input; no input keeps facing
    }

    // On touchdown: if we landed in a visible rain puddle, splash. Same puddle test as the walking water step,
    // so the landing sound only fires where you'd see (and hear) water — dry-ground landings stay silent.
    void OnJumpLand()
    {
        if (waterJumpSfx != null && !waterJumpSfx.Empty
            && WetGround.Instance != null && WetGround.Instance.IsPuddleAt(transform.position))
            Sfx.Play(waterJumpSfx);
    }

    // Start a roll/dash in `dir`: latch the burst state, i-frames, and WHEN it becomes rooted. The single owner of the
    // dash-start sequence (both the E-press and the mid-air roll-cancel route through here), so the root latch can't be
    // forgotten at one site. The Roll binding's rootFromFrame → a normalized point in the roll clip → dash time (the
    // clip is FitWindow-fit to eDashDuration, so a clip fraction is that fraction of the dash). 0/absent → never roots.
    /// <summary>Skill-driven INSTANT teleport (Blink Step): relocate the player straight to <paramref name="dest"/>
    /// — no roll, no travel. Cancels any in-progress dash, then uses the CharacterController-safe disable/move/enable
    /// so the capsule doesn't fight the reposition. The destination is already range-clamped by the ground reticle.</summary>
    public void Teleport(Vector3 dest)
    {
        if (eDashing) { eDashing = false; SetDashInvuln(false); }
        if (controller != null) controller.enabled = false;
        transform.position = dest;
        if (controller != null) controller.enabled = true;
    }

    void BeginEDash(Vector3 dir)
    {
        eDashDir = dir; eDashing = true; eDashStart = Time.time;
        SetDashInvuln(true);   // i-frames for the whole dash burst — roll THROUGH incoming hits

        var rb = StanceCatalog.Get(StanceId.Roll);
        int frame = rb != null ? rb.rootFromFrame : 0;
        int total = StanceController.Player != null ? StanceController.Player.NaturalFrameCountOf(StanceId.Roll) : 0;
        eDashRootAt = (frame > 0 && total > 0)
            ? eDashStart + Mathf.Clamp01((float)frame / total) * eDashDuration
            : eDashStart + eDashDuration;   // no root frame authored → move for the whole dash (rooting off)
    }

    void StepEDash()
    {
        // Root phase: from the authored frame to the clip's end the player can't move (recovery). Gravity only.
        if (Time.time >= eDashRootAt)
            controller.Move(Vector3.down * gravity * Time.deltaTime);
        else
        {
            float spd = eDashSpeedMult * CurrentRunSpeed;   // roll = 2× the player's CURRENT run speed (scales with size + buffs)
            controller.Move((eDashDir * spd + Vector3.down * gravity) * Time.deltaTime);
            worldFacing = eDashDir;
        }
        if (Time.time - eDashStart >= eDashDuration)
        {
            eDashing = false; eDashReadyAt = Time.time + eDashCooldown;
            SetDashInvuln(false);   // dash over → vulnerable again
        }
    }

    /// <summary>Tie the player's i-frames to the dash burst. The dash is the dodge: while it runs the player is
    /// <see cref="Character.Invulnerable"/>, so incoming hits whiff with a "Dodge" cue. Cleared whenever the dash
    /// ends — including a death-cancel (see the dead branch in the main controller).</summary>
    void SetDashInvuln(bool on) { if (Character.Instance != null) Character.Instance.Invulnerable = on; }

    static bool KeyJump() => Hotkeys.Down(KeyCode.Space);

    static bool KeyDash()
    {
        // Don't dash while a modifier is held (Alt = OS/Unity menu accelerators, Ctrl/Shift for combos).
        if (Hotkeys.Held(KeyCode.LeftAlt) || Hotkeys.Held(KeyCode.RightAlt)) return false;
        return Hotkeys.Down(KeyCode.E);
    }
}
