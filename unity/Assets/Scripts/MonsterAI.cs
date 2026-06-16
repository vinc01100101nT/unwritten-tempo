using UnityEngine;

/// <summary>
/// Minimal top-down enemy brain. It wanders near its spawn point, then CHASES the
/// player once they come within <see cref="aggroRadius"/>, and gives up (returns
/// to wandering) once they get past <see cref="leashRadius"/>.
///
/// Movement is delegated to a <see cref="PathAgent"/> sibling, so the monster now routes
/// AROUND walls and crates to reach the player instead of grinding into them. This class
/// is pure brain: senses + decisions; the agent does the walking (and the existing
/// <see cref="CharacterAnimator2D"/> animates it from that motion). A PathAgent is
/// required and auto-added if missing, so older monster prefabs gain routing without a rebuild.
///
/// The player is found by its <see cref="PlayerController2D"/>, so no tags needed.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PathAgent))]
public class MonsterAI : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 2.5f;

    [Header("Senses (world units)")]
    [Tooltip("Start chasing when the player is within this distance.")]
    public float aggroRadius = 6f;
    [Tooltip("Give up the chase once the player is farther than this.")]
    public float leashRadius = 10f;
    [Tooltip("Stop this close to the player so it doesn't jitter on top of them.")]
    public float stopDistance = 0.6f;

    [Header("Wander (when not chasing)")]
    public float wanderRadius = 3f;
    public float wanderPauseMin = 1f;
    public float wanderPauseMax = 3f;

    [Header("Combat")]
    [Tooltip("Attack power dealt to the player on contact.")]
    public int attack = 3;
    [Tooltip("XP awarded to the player when this dies.")]
    public int xpReward = 5;
    [Tooltip("How close (world units) it must be to hit the player.")]
    public float touchRange = 0.9f;
    [Tooltip("Seconds between contact hits.")]
    public float touchInterval = 1f;

    // Monsters share a physics layer that ignores ITSELF, so a pack passes THROUGH each other
    // (no hard pile-ups / jitter) while still colliding with walls and the player. The layer
    // index is used directly, so there's no manual layer naming to do.
    const int CrowdLayer = 8;
    static bool crowdLayerReady;

    Rigidbody2D rb;
    PathAgent agent;
    Transform player;
    Vector2 home;
    Vector2 wanderTarget;
    float nextWanderTime;
    bool chasing;
    float nextTouch;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        agent = GetComponent<PathAgent>();
        if (agent == null) { agent = gameObject.AddComponent<PathAgent>(); agent.agentRadius = 0.35f; }

        // Pass through other monsters (still collide with walls + the player).
        gameObject.layer = CrowdLayer;
        if (!crowdLayerReady) { Physics2D.IgnoreLayerCollision(CrowdLayer, CrowdLayer, true); crowdLayerReady = true; }

        home = rb.position;
        wanderTarget = home;
    }

    void Start()
    {
        agent.moveSpeed = moveSpeed;   // the routing mover walks at our speed
        agent.avoidStrength = 0.9f;    // gentle spread; pass-through (Awake) prevents hard pile-ups
        agent.avoidRadius = 0.8f;      // ~ a monster's body, so they spread only when actually close

        var pc = FindFirstObjectByType<PlayerController2D>();
        if (pc != null)
        {
            player = pc.transform;
            // Don't shove the player around: stop this monster's body from colliding with the
            // player's. Mobs still surround and deal (distance-based) touch damage — they just
            // can't physically push the player.
            var myCol = GetComponent<Collider2D>();
            var playerCol = pc.GetComponent<Collider2D>();
            if (myCol != null && playerCol != null) Physics2D.IgnoreCollision(myCol, playerCol, true);
        }
        agent.avoidIgnore = player;   // dodge other monsters, but NOT our target — so we can still reach the player

        // Award XP to the character when this monster dies.
        var health = GetComponent<Health>();
        if (health != null)
            health.onDeath.AddListener(() => { if (Character.Instance != null) Character.Instance.AddXP(xpReward); });

        // Stagger the first wander pick so a freshly-spawned pack doesn't all route on one frame.
        nextWanderTime = Time.time + Random.value;
    }

    void FixedUpdate()
    {
        Vector2 pos = rb.position;
        float toPlayer = player != null ? Vector2.Distance(pos, player.position) : Mathf.Infinity;

        // Aggro / leash transitions.
        bool wasChasing = chasing;
        if (chasing && toPlayer > leashRadius) chasing = false;
        else if (!chasing && toPlayer <= aggroRadius) chasing = true;

        // Bump the player on contact (Phase F: monsters fight back).
        if (player != null && toPlayer <= touchRange && Time.time >= nextTouch && Character.Instance != null)
        {
            int dealt = Character.Instance.TakeDamage(attack);
            DamagePopup.Spawn(player.position, dealt, new Color(1f, 0.35f, 0.35f));  // red = player took a hit
            nextTouch = Time.time + touchInterval;
        }

        if (chasing)
        {
            // Hold at stopDistance so we don't pile onto the player; otherwise close in. Spreading
            // around the player now comes from pass-through + avoidance, not a special target point.
            if (toPlayer <= stopDistance) agent.Halt();
            else agent.MoveToward(player.position);
        }
        else
        {
            if (wasChasing) { agent.Halt(); nextWanderTime = Time.time; }   // just leashed off → resume wandering now

            // Idle until the pause elapses, then pick a fresh point near home and route there.
            if (agent.Arrived && Time.time >= nextWanderTime)
            {
                wanderTarget = home + Random.insideUnitCircle * wanderRadius;
                agent.MoveTo(wanderTarget);
                nextWanderTime = Time.time + Random.Range(wanderPauseMin, wanderPauseMax);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, aggroRadius);
        Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, leashRadius);
    }
}
