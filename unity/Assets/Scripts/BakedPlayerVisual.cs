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
///
/// <para>This is the player's animation BRAIN, but not its hands: it computes WHICH action/pose to show and routes
/// every drive through a <see cref="StanceController"/> (the single choke point that actually touches
/// <see cref="BakedBillboard8.Play"/>), so nothing else can fight it for the billboard. Runs at execution order −100
/// so its intent is fed before the controller resolves (−50) and the sprite renders (0).</para>
/// </summary>
[RequireComponent(typeof(BakedBillboard8))]
[DefaultExecutionOrder(-100)]
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
    [Tooltip("While DEAD, the grounding offset for the (lying) corpse sprite. It is CLAMPED so it can only ever " +
             "SETTLE the corpse onto the floor, never RAISE it above where the player stood — set it BELOW the " +
             "standing feet-offset to sink the lying body if its head pokes through the ground; a value at/above " +
             "the standing offset leaves the corpse exactly on the ground (no visible lift).")]
    public float deadFeetOffset = 0.17f;
    [Tooltip("Seconds to ease the corpse to its dead grounding, so it settles rather than popping.")]
    public float deadSettleTime = 0.25f;

    BakedBillboard8 baked;
    StanceController stance;       // the single choke point we route every Play/pose through (auto-added)
    Player3DAttacker attacker;     // for the Swinging (attack) state
    SpriteRenderer hiddenPlayerSprite;
    CharacterController cc;        // used to drop from capsule-center to the feet/ground point
    int groundMask;               // Ground layer only — the single grounding truth shared with TownNPC
    Character subscribed;          // the Character we hooked OnDamaged on (to unhook the SAME one)
    Vector3 lastPlayerPos;
    bool wasDead;
    float groundedFeetOffset;      // standing grounding captured at death, restored on revive
    float deadTargetOffset;        // clamped dead grounding (never above standing) we ease toward
    float hurtUntil, pickUpUntil;  // momentary one-shot windows (event-driven, not state-driven)

    void Awake()
    {
        baked = GetComponent<BakedBillboard8>();
        stance = GetComponent<StanceController>();
        if (stance == null) stance = gameObject.AddComponent<StanceController>();
        stance.BecomePlayer();   // register as StanceController.Player + hold Idle (never self-sample movement)
    }

    void Start()
    {
        if (player == null) player = FindFirstObjectByType<Player3DController>();
        if (player == null) { Debug.LogWarning("[unwritten] BakedPlayerVisual found no Player3DController."); return; }

        cc = player.GetComponent<CharacterController>();
        int gl = LayerMask.NameToLayer("Ground");
        groundMask = gl >= 0 ? (1 << gl) : 0;   // 0 → no Ground layer; GroundedPos falls back to the capsule drop
        attacker = player.GetComponent<Player3DAttacker>();
        stance.SetGaitSource(player);   // MoveGait stances resolve against the real controller (on the player, not this visual)
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

    // Ground the avatar on the SAME truth the townsfolk use ([[TownNPC]]): raycast the Ground layer under the
    // player and seat the visual's origin ON that surface, which is exactly the basis BakedBillboard8.feetOffset
    // was tuned for — so the player's feet land where the NPCs' feet land. Jumps/falls are preserved without a
    // pop: we lift the visual by how far the player's root is ABOVE its grounded resting height, so at rest the
    // lift is 0 (feet on ground) and mid-hop it rises by the real hop height. Falls back to the old capsule
    // drop only if there's no Ground hit (off-map) or no Ground layer exists.
    Vector3 GroundedPos(Vector3 p)
    {
        if (cc == null) return p;
        if (GroundSnap.TryGroundY(p, groundMask, out float groundY))   // Ground-layer foot-seat (shared truth)
        {
            float lossy = player != null ? player.transform.lossyScale.y : 1f;
            // Where the root sits when the capsule rests on this ground (bottom = center.y - height/2, scaled, + skin).
            float restingRootY = groundY + cc.skinWidth - (cc.center.y - cc.height * 0.5f) * lossy;
            float above = Mathf.Max(0f, p.y - restingRootY);   // 0 when grounded; >0 mid-jump / when falling
            p.y = groundY + above;
            return p;
        }
        p.y += cc.center.y - cc.height * 0.5f;   // no ground found: legacy capsule-center drop
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
            stance.DriveStance(StanceId.Die);   // its action (Die) plays once and holds its final (lying) frame — see BakedBillboard8.HoldsLastFrame
            wasDead = true;
            return;
        }
        if (wasDead) { baked.SetFeetOffset(groundedFeetOffset); stance.ClearAction(); wasDead = false; }   // revived → stand back up on the normal feet

        // Decide the action stance via the SHARED brain — the SAME logic the 3D visual (Character3DVisual) uses, so
        // "swinging → Attack", "airborne → Jump", "hit → Hurt" lives in ONE place and can't drift between the two
        // presentations. Feed it this billboard's clip capabilities (Walk↔Run choice, Fall-reuses-Jump gate); the
        // controller fits FitWindow stances (Attack/Roll) to the runtime window it returns.
        var d = PlayerStanceBrain.Decide(player, attacker, speed, walkThreshold, hurtUntil, pickUpUntil,
                                         baked.HasAction(CharacterAction.Run), baked.HasAction(CharacterAction.Jump));
        if (d.hasAction) stance.DriveStance(d.id, d.window);
        else stance.ClearAction();
        if (d.applyFacing) baked.SetFacing(d.facing);
    }

    void OnDestroy()
    {
        if (subscribed != null) subscribed.OnDamaged -= OnDamaged;
        WorldPickup.OnCollected -= OnPickUp;
        if (hiddenPlayerSprite != null) hiddenPlayerSprite.enabled = true;   // restore the original avatar
    }
}
