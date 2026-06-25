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
    public float eDashSpeedMult = 2f;
    public float eDashDuration = 0.50f;
    public float eDashCooldown = 0.7f;

    // ---- runtime state -------------------------------------------------------------------------------
    bool jumping; float jumpStart; Vector3 jumpDir;

    /// <summary>True while a hop arc is in progress (takeoff → land). Read by FX like wet-ground footstep
    /// ripples so they pause mid-air and splash on landing, and by the baked visual to play its Jump clip.</summary>
    public bool IsAirborne => jumping;
    float jumpBaseY;       // ground Y captured at takeoff — the arc returns here, then gravity falls to land
    bool eDashing; float eDashStart; Vector3 eDashDir; float eDashReadyAt;

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
            eDashDir = AbilityDir(); eDashing = true; eDashStart = Time.time;
            SetDashInvuln(true);   // i-frames for the whole dash burst — roll THROUGH incoming hits
            StepEDash(); transform.localScale = baseScale; return true;
        }
        if (KeyJump())
        {
            // Direction is steered LIVE in StepJump (re-reads WASD each frame), so we only latch the takeoff
            // height/time here. Fully free-directional in the air; no key held = static up-and-down hop.
            jumping = true; jumpStart = Time.time; jumpBaseY = transform.position.y;
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
        float air = JumpAirTime;
        float elapsed = Time.time - jumpStart;

        // FULLY free-directional, even mid-air: re-read WASD EVERY frame so releasing or switching keys steers
        // the hop live (release W mid-air -> stops forward; tap A mid-air -> moves and faces left). No key held
        // -> jumpDir zero -> no horizontal travel, the body just rises/falls in place.
        Vector3 wasd = ReadWasd();
        jumpDir = wasd.sqrMagnitude > 1e-4f ? wasd.normalized : Vector3.zero;
        float fwd = jumpDistance * UnitScale.ForPlayer / air;
        Vector3 delta = jumpDir * fwd * Time.deltaTime;

        if (elapsed < air)
        {
            // Phase 1 — scripted arc: the vertical lift is driven DIRECTLY (no gravity) as a symmetric parabola
            // back to takeoff height. The engine's cast shadow tracks the lifted body for a natural jump shadow.
            float u = Mathf.Clamp01(elapsed / air);
            float h = jumpHeight * UnitScale.ForPlayer * 4f * u * (1f - u);   // 0 -> peak (u=0.5) -> 0
            delta.y = (jumpBaseY + h) - transform.position.y;
            controller.Move(delta);
        }
        else
        {
            // Phase 2 — the arc is done but we may still be high in the air (hopped off a tall tree/ledge).
            // Keep FALLING under gravity, holding the last jump frame, until we actually touch the ground —
            // so the walk/idle pose can't play mid-fall. Horizontal steering stays live.
            delta.y = -gravity * Time.deltaTime;
            controller.Move(delta);
            if (controller.isGrounded) { jumping = false; OnJumpLand(); }   // landed -> hand control back to normal movement
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

    void StepEDash()
    {
        float spd = eDashSpeedMult * CurrentRunSpeed;   // roll = 2× the player's CURRENT run speed (scales with size + buffs)
        controller.Move((eDashDir * spd + Vector3.down * gravity) * Time.deltaTime);
        worldFacing = eDashDir;
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

    static bool KeyJump()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.Space);
    }

    static bool KeyDash()
    {
        // Don't dash while a modifier is held (Alt = OS/Unity menu accelerators, Ctrl/Shift for combos).
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.E);
    }
}
