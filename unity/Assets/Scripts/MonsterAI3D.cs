using UnityEngine;

/// <summary>
/// 3D port of <see cref="MonsterAI"/> — pure brain for a billboard mob. Wanders near its spawn,
/// CHASES the <see cref="Player3DController"/> once within <see cref="aggroRadius"/>, and gives up
/// past <see cref="leashRadius"/>. Movement is delegated to a <see cref="PathAgent3D"/> sibling
/// (routes around props via the grid A*); <see cref="BillboardAnimator3D"/> animates from that
/// motion. Reuses <see cref="Health"/> for HP/death and the existing <see cref="Character"/> for
/// touch-damage + XP (no-ops in the standalone spike, which has no Character yet).
/// </summary>
[RequireComponent(typeof(PathAgent3D))]
public class MonsterAI3D : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 2.5f;

    [Header("Senses (world units)")]
    public float aggroRadius = 6f;
    public float leashRadius = 10f;
    [Tooltip("Hold this far from the player. Must be < touchRange so it can actually land hits.")]
    public float stopDistance = 0.7f;

    [Header("Wander")]
    public float wanderRadius = 3f;
    public float wanderPauseMin = 1f;
    public float wanderPauseMax = 3f;

    public enum AttackType { Physical, Magic }

    [Header("Combat")]
    [Tooltip("Physical hits the player's Defense; Magic hits MagicDefense (and ignores Evasion).")]
    public AttackType attackType = AttackType.Physical;
    public int attack = 3;
    [Tooltip("Outgoing damage swings down to (1−spread)×attack each hit, RO-style (0 = always full attack).")]
    [Range(0f, 1f)] public float damageSpread = 0.25f;
    [Tooltip("HIT (RO accuracy). Rolled against the player's FLEE on each bite; lose and the attack MISSes (white).")]
    public int hit = 8;
    public int xpReward = 5;
    public float touchRange = 0.9f;
    public float touchInterval = 1f;

    [Header("Behavior style")]
    [Tooltip("0 = melee (close in and bite). >0 = HOLD this far from the player and attack from range " +
             "(a caster/archer kites). For ranged mobs set touchRange ≥ this so they can land hits while holding.")]
    public float preferredRange = 0f;
    [Tooltip("0 = never flee. Otherwise, once HP drops below this fraction of max while aggroed, the mob turns " +
             "and runs from the player — a skittish 'wounded animal' that breaks off when hurt.")]
    [Range(0f, 1f)] public float fleeHpFraction = 0f;

    PathAgent3D agent;
    AttackLunge lunge;
    Transform player;
    Vector3 home;
    float nextWander;
    bool chasing;
    float nextTouch;

    /// <summary>This mob's <see cref="Health"/>, cached once. The cursor pick and auto-combat target
    /// scans read this instead of calling <c>GetComponent&lt;Health&gt;()</c> per mob, every frame.</summary>
    public Health Health { get; private set; }

    /// <summary>Live registry of active mobs, so cursor target-magnetism can scan candidates without a
    /// per-frame <c>FindObjectsByType</c>. Maintained on enable/disable (so it respects MobRespawn pooling).</summary>
    public static readonly System.Collections.Generic.List<MonsterAI3D> Active = new System.Collections.Generic.List<MonsterAI3D>();

    void Awake()
    {
        agent = GetComponent<PathAgent3D>();
        lunge = GetComponent<AttackLunge>();
        Health = GetComponent<Health>();
        home = transform.position;
    }

    void OnEnable()  { if (!Active.Contains(this)) Active.Add(this); }
    void OnDisable() => Active.Remove(this);

    void Start()
    {
        agent.moveSpeed = moveSpeed;

        var pc = FindFirstObjectByType<Player3DController>();
        if (pc != null) player = pc.transform;

        if (Health != null)
            Health.onDeath.AddListener(() => { if (Character.Instance != null) Character.Instance.AddXP(xpReward); });

        nextWander = Time.time + Random.value;
    }

    void Update()
    {
        Vector3 pos = transform.position;
        // A dead player is no target: leash off and resume wandering until they respawn.
        bool playerAlive = player != null && Character.Instance != null && !Character.Instance.IsDead;
        // Compare SQUARED distances against squared radii — avoids a sqrt per check, per mob, per frame.
        float toPlayerSqr = playerAlive ? PlanarSqr(pos, player.position) : Mathf.Infinity;

        bool was = chasing;
        if (chasing && toPlayerSqr > leashRadius * leashRadius) chasing = false;
        else if (!chasing && toPlayerSqr <= aggroRadius * aggroRadius) chasing = true;

        // Touch damage (Phase F parity). No-op in the spike: Character.Instance is null without Systems.
        // Use TRUE 3D distance (not planar) so a mob that has fallen off a cliff/edge below the terrain —
        // same XZ as the player but far below — can't keep biting an invisible, "there are no mobs" hit.
        float touch3DSqr = playerAlive ? (player.position - pos).sqrMagnitude : Mathf.Infinity;
        if (player != null && touch3DSqr <= touchRange * touchRange && Time.time >= nextTouch && Character.Instance != null)
        {
            // Magic mobs hit MagicDefense (and can't be dodged); physical mobs hit Defense/Evasion.
            int atk = Mathf.RoundToInt(attack * (1f - Random.Range(0f, damageSpread)));   // RO-style spread
            int dealt = attackType == AttackType.Magic
                ? Character.Instance.TakeMagicDamage(atk)          // magic ignores HIT/FLEE (MATK vs MDEF)
                : Character.Instance.TakeDamage(atk, hit);          // physical: our HIT rolled vs player FLEE
            if (dealt > 0)
                DamagePopup.Spawn(player.position, dealt,
                    attackType == AttackType.Magic ? new Color(0.7f, 0.5f, 1f) : new Color(1f, 0.35f, 0.35f));
            nextTouch = Time.time + touchInterval;
            if (lunge != null) lunge.Punch();   // squash-stretch "chomp" tell
        }

        if (chasing)
        {
            Pursue(pos, toPlayerSqr);
        }
        else
        {
            if (was) { agent.Halt(); nextWander = Time.time; }
            if (agent.Arrived && Time.time >= nextWander)
            {
                Vector2 r = Random.insideUnitCircle * wanderRadius;
                agent.MoveTo(home + new Vector3(r.x, 0f, r.y));
                nextWander = Time.time + Random.Range(wanderPauseMin, wanderPauseMax);
            }
        }
    }

    // The chase brain, split by behavior style. All branches reuse the per-frame distance already computed
    // in Update — no extra Find/GetComponent/alloc (PERFORMANCE.md: nothing new in the per-mob hot path).
    void Pursue(Vector3 pos, float toPlayerSqr)
    {
        // Skittish: wounded enough → break off and run from the player.
        if (fleeHpFraction > 0f && Health != null && Health.Current <= Health.maxHealth * fleeHpFraction)
        {
            agent.MoveToward(AwayPoint(pos));
            return;
        }

        // Ranged/caster: hold the pocket at preferredRange — kite back if too close, close in if too far,
        // otherwise stand and let the touch-attack above fire (touchRange is set ≥ preferredRange).
        if (preferredRange > 0f)
        {
            float near = preferredRange * 0.8f;
            if (toPlayerSqr < near * near)            agent.MoveToward(AwayPoint(pos));
            else if (toPlayerSqr > preferredRange * preferredRange) agent.MoveToward(player.position);
            else                                       agent.Halt();
            return;
        }

        // Melee: close to stopDistance and bite (the original behavior).
        if (toPlayerSqr <= stopDistance * stopDistance) agent.Halt();
        else agent.MoveToward(player.position);
    }

    // A point ~2 units from the player, directly opposite this mob — the target for kiting / fleeing.
    // Planar; locals only (no allocation).
    Vector3 AwayPoint(Vector3 pos)
    {
        Vector3 away = pos - player.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = transform.forward;   // on top of the player → pick any direction
        away.Normalize();
        return pos + away * 2f;
    }

    static float PlanarSqr(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return (a - b).sqrMagnitude;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, aggroRadius);
        Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, leashRadius);
    }
}
