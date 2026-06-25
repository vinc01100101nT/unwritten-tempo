using UnityEngine;

/// <summary>
/// Plays one of the baked 8-direction characters AS the real player, without modifying the load-bearing
/// <see cref="Player3DController"/>. It rides on a <see cref="BakedBillboard8"/> instance: each frame it snaps
/// to the player's position, hides the player's own sprite, and drives the billboard from the player's
/// <see cref="Player3DController.WorldFacing"/> + actual movement speed (Idle/Walk/Run from real motion).
///
/// <para>On top of locomotion it routes the game's one-shot states to the baked actions: <b>Die</b> (dead),
/// <b>Hurt</b> (took damage), <b>Attack</b> (melee swing), <b>Jump</b> (the hop), <b>Dash</b> (charge dash),
/// and <b>PickUp</b> (collected loot). Overrides win over locomotion and are NOT cancelled by moving (we feed
/// facing without velocity while one is active), so a jump/attack-on-the-run still plays.</para>
///
/// Wire it with <c>Tools ▸ unwritten ▸ Player ▸ Use Baked Character</c>. Reversible — remove this object (or run
/// the revert tool) and the original player sprite comes back.
/// </summary>
[RequireComponent(typeof(BakedBillboard8))]
public class BakedPlayerVisual : MonoBehaviour
{
    [Tooltip("The real player to ride. Auto-found at Start if left empty.")]
    public Player3DController player;
    [Tooltip("Speed (units/sec) above which the player counts as walking.")]
    public float walkThreshold = 0.12f;
    [Tooltip("Seconds the Hurt action plays after taking a hit.")]
    public float hurtDuration = 0.4f;
    [Tooltip("Seconds the PickUp action plays when collecting loot.")]
    public float pickUpDuration = 0.6f;
    [Tooltip("Free-fall pose: where in the Jump clip the looped falling frames begin (0 = whole clip, 0.5 = its " +
             "2nd/descent half, ~0.9 ≈ hold the last frame). No dedicated Fall art exists, so we reuse Jump.")]
    [Range(0f, 1f)] public float fallPosePhase = 0.5f;
    [Tooltip("While DEAD, the grounding offset for the (lying) corpse sprite. It is CLAMPED so it can only ever " +
             "SETTLE the corpse onto the floor, never RAISE it above where the player stood — set it BELOW the " +
             "standing feet-offset to sink the lying body if its head pokes through the ground; a value at/above " +
             "the standing offset leaves the corpse exactly on the ground (no visible lift).")]
    public float deadFeetOffset = 0.17f;
    [Tooltip("Seconds to ease the corpse to its dead grounding, so it settles rather than popping.")]
    public float deadSettleTime = 0.25f;

    BakedBillboard8 baked;
    Player3DAttacker attacker;     // for the Swinging (attack) state
    SpriteRenderer hiddenPlayerSprite;
    CharacterController cc;        // used to drop from capsule-center to the feet/ground point
    Character subscribed;          // the Character we hooked OnDamaged on (to unhook the SAME one)
    Vector3 lastPlayerPos;
    bool wasDead;
    float groundedFeetOffset;      // standing grounding captured at death, restored on revive
    float deadTargetOffset;        // clamped dead grounding (never above standing) we ease toward
    bool wasRolling;               // rising-edge guard so the dash→Roll time-fit + log fire once per dash
    float hurtUntil, pickUpUntil;  // momentary one-shot windows (event-driven, not state-driven)

    void Awake() => baked = GetComponent<BakedBillboard8>();

    void Start()
    {
        if (player == null) player = FindFirstObjectByType<Player3DController>();
        if (player == null) { Debug.LogWarning("[unwritten] BakedPlayerVisual found no Player3DController."); return; }

        cc = player.GetComponent<CharacterController>();
        attacker = player.GetComponent<Player3DAttacker>();
        hiddenPlayerSprite = player.GetComponent<SpriteRenderer>();
        if (hiddenPlayerSprite != null) hiddenPlayerSprite.enabled = false;   // hide the original avatar

        // Hook the momentary triggers we can't poll: damage (→ Hurt) and loot collection (→ PickUp).
        subscribed = Character.Instance;
        if (subscribed != null) subscribed.OnDamaged += OnDamaged;
        WorldPickup.OnCollected += OnPickUp;

        lastPlayerPos = player.transform.position;
        transform.position = GroundedPos(lastPlayerPos);

        ApplyClimbLimit();
    }

    // Block elevated surfaces that are ≥50% of the VISIBLE character height: cap the CharacterController's
    // auto-climb (stepOffset) at just UNDER half the baked sprite's world height. Small curbs / tree-roots
    // (< 50%) still step up; anything taller becomes a wall you must jump to clear. Measured off the real
    // sprite so it tracks whatever was baked/scaled, and written onto the player's serialized capsule field
    // so it survives any later ApplyCapsule() call.
    void ApplyClimbLimit()
    {
        if (cc == null || baked == null) return;
        float h = baked.VisualWorldHeight;
        if (h <= 0.01f) return;
        float climb = h * 0.5f - 0.02f;                       // just under 50% so a half-height ledge blocks
        float safeMax = cc.height + 2f * cc.radius - 0.02f;   // Unity requires stepOffset < height + 2·radius
        climb = Mathf.Clamp(climb, 0.02f, safeMax);
        player.capsuleStepOffset = climb;                     // persists through ApplyCapsule()
        player.ApplyCapsule();
        Debug.Log($"[unwritten] Player auto-climb (stepOffset) set to {climb:0.00}u = 50% of the {h:0.00}u " +
                  "sprite; taller elevated surfaces now block (jump to clear).");
    }

    void OnDamaged(int dmg)
    {
        // Don't flinch on the killing blow — Die owns that frame.
        if (Character.Instance == null || !Character.Instance.IsDead) hurtUntil = Time.time + hurtDuration;
    }

    void OnPickUp() => pickUpUntil = Time.time + pickUpDuration;

    // The player's transform sits at the CharacterController CENTER, which rests ~half a capsule above the
    // floor (bottom = center.y - height/2). The baked billboard's feetOffset grounds the feet only when the
    // object origin is ON the ground (as in the sandbox), so drop to the capsule's bottom — otherwise the
    // whole avatar floats by that fixed standoff no matter how you tune feetOffset / worldScale.
    Vector3 GroundedPos(Vector3 p)
    {
        if (cc != null) p.y += cc.center.y - cc.height * 0.5f;
        return p;
    }

    void LateUpdate()
    {
        if (player == null || baked == null) return;

        Vector3 pos = player.transform.position;
        transform.position = GroundedPos(pos);          // ride the player, dropped to its feet

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        float speed = (pos - lastPlayerPos).magnitude / dt;
        lastPlayerPos = pos;

        // Death overrides everything (the controller hides its own dead sprite, which we've disabled anyway).
        bool dead = Character.Instance != null && Character.Instance.IsDead;
        if (dead)
        {
            // On the alive→dead edge, capture the standing grounding and pick the dead grounding. CLAMP it so the
            // corpse can only settle DOWN onto the floor, never pop UP above where the player stood (the old code
            // snapped it up by ~0.35u, which read as the corpse "elevating"/floating). Then EASE to it so the body
            // settles to the ground instead of teleporting.
            if (!wasDead)
            {
                groundedFeetOffset = baked.FeetOffset;
                deadTargetOffset = Mathf.Min(deadFeetOffset, groundedFeetOffset);
            }
            float ease = deadSettleTime > 0.001f ? Time.deltaTime / deadSettleTime : 1f;
            baked.SetFeetOffset(Mathf.MoveTowards(baked.FeetOffset, deadTargetOffset, Mathf.Abs(groundedFeetOffset - deadTargetOffset) * ease + 0.0001f));
            baked.Play(CharacterAction.Die);   // plays once and holds its final (lying) frame — see BakedBillboard8.HoldsLastFrame
            wasDead = true;
            return;
        }
        if (wasDead) { baked.SetFeetOffset(groundedFeetOffset); baked.StopOverride(); wasDead = false; }   // revived → stand back up on the normal feet

        Vector3 dir = player.WorldFacing; dir.y = 0f;

        // Free-fall (walked/ran off higher ground): reuse the Jump clip's descent half as the falling pose,
        // looped while airborne. Mutually exclusive with the scripted hop (IsAirborne) below — the controller
        // clears IsFalling during the hop/dash/swing frames. Beats locomotion so you don't "run" through the air.
        if (player.IsFalling && baked.HasAction(CharacterAction.Jump))
        {
            wasRolling = false;
            baked.PlayFall(CharacterAction.Jump, fallPosePhase);
            if (dir.sqrMagnitude > 1e-4f) baked.SetFacing(dir);
            return;
        }

        // One-shot / action overrides, highest priority first. While one is active we feed only FACING (not
        // velocity) so movement can't cancel it — a swing/jump/dash on the run keeps playing its action.
        // Hurt is the EXCEPTION: it only flinches when fully idle (not moving/jumping/attacking/etc.), so a hit
        // never interrupts an action or a run.
        CharacterAction ov;
        bool hasOverride = true;
        if (attacker != null && attacker.Swinging)          ov = CharacterAction.Attack;
        else if (player.IsAirborne)                         ov = CharacterAction.Jump;
        else if (player.Dashing || player.EDashing)         ov = baked.HasAction(CharacterAction.Roll) ? CharacterAction.Roll : CharacterAction.Dash;
        else if (Time.time < pickUpUntil)                   ov = CharacterAction.PickUp;
        else if (Time.time < hurtUntil && speed < walkThreshold) ov = CharacterAction.Hurt;
        else { ov = CharacterAction.Idle; hasOverride = false; }

        if (hasOverride)
        {
            // Speed the Attack clip so the WHOLE animation plays exactly within the swing window (which scales with
            // attack-speed), and damage lands as it finishes. speed = naturalClipLength / swingTime.
            if (ov == CharacterAction.Attack && attacker != null)
            {
                float swing = attacker.SwingTime;
                float natural = baked.NaturalDuration(CharacterAction.Attack);
                if (swing > 0.01f && natural > 0.01f)
                    baked.SetActionSpeed(CharacterAction.Attack, natural / swing);
            }

            // Dash → Roll: compress the Roll clip to play in full within the (short) dash window so it reads as a
            // dodge-roll, not a 2-frame flash. Done once on the rising edge.
            if (ov == CharacterAction.Roll)
            {
                if (!wasRolling)
                {
                    wasRolling = true;
                    float dashLen = player.EDashing ? player.eDashDuration : 0.4f;
                    float natural = baked.NaturalDuration(CharacterAction.Roll);
                    if (dashLen > 0.01f && natural > 0.01f) baked.SetActionSpeed(CharacterAction.Roll, natural / dashLen);
                }
            }
            else wasRolling = false;

            baked.Play(ov);                                              // missing action falls back to Idle internally
            if (dir.sqrMagnitude > 1e-4f) baked.SetFacing(dir);          // keep aiming without clearing the override
            return;
        }

        // Locomotion — facing from the player's INTENT (stable even when blocked). Normal movement RUNS;
        // holding Shift (player.WalkingSlow) downshifts to WALK (+ slower speed, set in the controller). We
        // force the locomotion action rather than letting speed thresholds pick it, so a "run" reads as Run
        // even at the modest player move speed. Falls back to Walk if no Run strip was baked.
        if (speed >= walkThreshold && dir.sqrMagnitude > 1e-4f)
        {
            CharacterAction loco = player.WalkingSlow
                ? CharacterAction.Walk
                : (baked.HasAction(CharacterAction.Run) ? CharacterAction.Run : CharacterAction.Walk);
            baked.Play(loco);
            baked.SetFacing(dir.normalized);
        }
        else
        {
            baked.StopOverride();
            baked.SetVelocity(Vector3.zero);
            if (dir.sqrMagnitude > 1e-4f) baked.SetFacing(dir);
        }
    }

    void OnDestroy()
    {
        if (subscribed != null) subscribed.OnDamaged -= OnDamaged;
        WorldPickup.OnCollected -= OnPickUp;
        if (hiddenPlayerSprite != null) hiddenPlayerSprite.enabled = true;   // restore the original avatar
    }
}
