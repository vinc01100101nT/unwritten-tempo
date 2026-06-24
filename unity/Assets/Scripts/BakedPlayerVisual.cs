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

    BakedBillboard8 baked;
    Player3DAttacker attacker;     // for the Swinging (attack) state
    SpriteRenderer hiddenPlayerSprite;
    CharacterController cc;        // used to drop from capsule-center to the feet/ground point
    Character subscribed;          // the Character we hooked OnDamaged on (to unhook the SAME one)
    Vector3 lastPlayerPos;
    bool wasDead;
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
        if (dead) { baked.Play(CharacterAction.Die); wasDead = true; return; }
        if (wasDead) { baked.StopOverride(); wasDead = false; }

        Vector3 dir = player.WorldFacing; dir.y = 0f;

        // One-shot / action overrides, highest priority first. While one is active we feed only FACING (not
        // velocity) so movement can't cancel it — a swing/jump/dash on the run keeps playing its action.
        // Hurt is the EXCEPTION: it only flinches when fully idle (not moving/jumping/attacking/etc.), so a hit
        // never interrupts an action or a run.
        CharacterAction ov;
        bool hasOverride = true;
        if (attacker != null && attacker.Swinging)          ov = CharacterAction.Attack;
        else if (player.IsAirborne)                         ov = CharacterAction.Jump;
        else if (player.Dashing)                            ov = CharacterAction.Dash;
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

            baked.Play(ov);                                              // missing action falls back to Idle internally
            if (dir.sqrMagnitude > 1e-4f) baked.SetFacing(dir);          // keep aiming without clearing the override
            return;
        }

        // Locomotion — facing from the player's INTENT (stable even when blocked); Idle/Walk/Run from ACTUAL speed.
        baked.StopOverride();
        if (speed >= walkThreshold && dir.sqrMagnitude > 1e-4f)
            baked.SetVelocity(dir.normalized * speed);
        else { baked.SetVelocity(Vector3.zero); if (dir.sqrMagnitude > 1e-4f) baked.SetFacing(dir); }
    }

    void OnDestroy()
    {
        if (subscribed != null) subscribed.OnDamaged -= OnDamaged;
        WorldPickup.OnCollected -= OnPickUp;
        if (hiddenPlayerSprite != null) hiddenPlayerSprite.enabled = true;   // restore the original avatar
    }
}
