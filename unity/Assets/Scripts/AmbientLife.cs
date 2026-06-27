using UnityEngine;

/// <summary>
/// Drop-in "make this map feel alive" director. Place ONE in a scene (Tools ▸ unwritten ▸ World ▸ Add Ambient
/// Townsfolk) and at Play it spawns wandering billboard townsfolk drawn from the <see cref="CharacterRoster"/>
/// — the very same baked characters the player is built from, so there's zero new art. Each gets a
/// <see cref="TownNPC"/> brain (stroll near where it spawned + occasional barks) on the shared
/// <see cref="PathAgent3D"/> mover, so they route around props and animate Walk/Idle for free.
///
/// This is intentionally self-contained (no recipe wiring required): its own transform + <see cref="areaRadius"/>
/// define where folk appear and roam, and it ground-snaps each spawn with a single downward raycast. The
/// per-NPC perf story (simulate-near / freeze-far, pooled popup barks) lives on <see cref="TownNPC"/>.
///
/// Spawning happens ONCE at Start — there's no Instantiate churn afterward (townsfolk persist; the LOD just
/// freezes their brains when you're far), so this respects the no-allocation-in-hot-paths rule.
/// </summary>
[DisallowMultipleComponent]
public class AmbientLife : MonoBehaviour
{
    [Header("Cast")]
    [Tooltip("Baked characters to draw townsfolk from (Assets/Art/Characters/CharacterRoster.asset). " +
             "Assigned for you by the editor tool.")]
    public CharacterRoster roster;

    [Header("Population")]
    [Min(0)] public int count = 12;
    [Tooltip("Townsfolk spawn within this radius of THIS object and wander near where they land.")]
    public float areaRadius = 25f;
    [Tooltip("How far each townsperson roams from its own spawn point.")]
    public float wanderRadius = 6f;
    [Tooltip("Per-NPC stroll speed is rolled in this range (units/sec). Slower than mobs — they're not in a hurry.")]
    public Vector2 moveSpeedRange = new Vector2(1.1f, 1.7f);

    [Header("Barks")]
    [Tooltip("Reactive canned chatter when the player passes (NpcBarks.Town pool).")]
    public bool barks = true;

    [Header("Grounding")]
    [Tooltip("Layer(s) treated as the ground for snapping spawns down. Set to the Ground layer by the tool.")]
    public LayerMask groundMask;

    void Start()
    {
        if (roster == null || roster.characters == null || roster.characters.Count == 0)
        {
            Debug.LogWarning("[unwritten] AmbientLife on '" + name + "' has no roster/characters — no townsfolk " +
                             "spawned. Assign a CharacterRoster (or re-run the Add Ambient Townsfolk tool).");
            return;
        }

        var pc = FindFirstObjectByType<Player3DController>();
        Transform player = pc != null ? pc.transform : null;
        string[] pool = barks ? NpcBarks.Town : null;

        for (int i = 0; i < count; i++)
            SpawnOne(player, pool);
    }

    void SpawnOne(Transform player, string[] pool)
    {
        var entry = roster.characters[Random.Range(0, roster.characters.Count)];
        if (entry == null || entry.prefab == null) return;

        Vector2 r = Random.insideUnitCircle * areaRadius;
        Vector3 p = GroundSnap(transform.position + new Vector3(r.x, 0f, r.y));

        var go = Instantiate(entry.prefab, p, Quaternion.identity);
        go.name = "Townsfolk_" + entry.name;

        // Mirror the mob movement stack (Build25DSpike.BuildMonster): PathAgent3D auto-adds a CharacterController,
        // which we size to the baked character (its root localScale is the worldScale, so these are LOCAL units —
        // height 1 ≈ 1.7u tall, capsule foot at 0 sits on the spawn point).
        var agent = go.AddComponent<PathAgent3D>();
        agent.agentRadius = 0.3f;
        var cc = go.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.radius = 0.25f;
            cc.height = 1.0f;
            cc.center = new Vector3(0f, 0.5f, 0f);
            cc.stepOffset = 0.3f;
            cc.slopeLimit = 45f;
        }

        var npc = go.AddComponent<TownNPC>();
        npc.Init(p, wanderRadius, Random.Range(moveSpeedRange.x, moveSpeedRange.y), pool, barks, player, groundMask);
    }

    // Drop a candidate point onto the ground with one downward ray (Ground layer only, so it lands on the floor,
    // not on a roof/prop). Falls back to this director's own height if nothing is hit.
    Vector3 GroundSnap(Vector3 p)
    {
        int mask = groundMask.value != 0 ? groundMask.value : Physics.DefaultRaycastLayers;
        Vector3 from = p + Vector3.up * 30f;
        if (Physics.Raycast(from, Vector3.down, out var hit, 80f, mask, QueryTriggerInteraction.Ignore))
            p.y = hit.point.y;
        else
            p.y = transform.position.y;
        return p;
        // TEST COMMENT, WILL DELETE LATER
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.9f, 0.5f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, areaRadius);
    }
}
