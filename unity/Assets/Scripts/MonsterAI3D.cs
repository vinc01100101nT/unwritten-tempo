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

    [Header("Combat")]
    public int attack = 3;
    public int xpReward = 5;
    public float touchRange = 0.9f;
    public float touchInterval = 1f;

    PathAgent3D agent;
    Transform player;
    Vector3 home;
    float nextWander;
    bool chasing;
    float nextTouch;

    void Awake()
    {
        agent = GetComponent<PathAgent3D>();
        home = transform.position;
    }

    void Start()
    {
        agent.moveSpeed = moveSpeed;

        var pc = FindFirstObjectByType<Player3DController>();
        if (pc != null) player = pc.transform;

        var health = GetComponent<Health>();
        if (health != null)
            health.onDeath.AddListener(() => { if (Character.Instance != null) Character.Instance.AddXP(xpReward); });

        nextWander = Time.time + Random.value;
    }

    void Update()
    {
        Vector3 pos = transform.position;
        float toPlayer = player != null ? PlanarDist(pos, player.position) : Mathf.Infinity;

        bool was = chasing;
        if (chasing && toPlayer > leashRadius) chasing = false;
        else if (!chasing && toPlayer <= aggroRadius) chasing = true;

        // Touch damage (Phase F parity). No-op in the spike: Character.Instance is null without Systems.
        if (player != null && toPlayer <= touchRange && Time.time >= nextTouch && Character.Instance != null)
        {
            int dealt = Character.Instance.TakeDamage(attack);
            DamagePopup.Spawn(player.position, dealt, new Color(1f, 0.35f, 0.35f));
            nextTouch = Time.time + touchInterval;
        }

        if (chasing)
        {
            if (toPlayer <= stopDistance) agent.Halt();
            else agent.MoveToward(player.position);
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

    static float PlanarDist(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, aggroRadius);
        Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, leashRadius);
    }
}
