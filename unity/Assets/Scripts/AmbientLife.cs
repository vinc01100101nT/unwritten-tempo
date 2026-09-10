using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drop-in "make this map feel alive" director. Place ONE in a scene (Tools ▸ unwritten ▸ World ▸ Add Ambient
/// Townsfolk) and the machine that OWNS the world spawns wandering LIVE-3D townsfolk drawn from the rigged
/// Quaternius models — the same cast the player is built from, so there's zero new art. Each gets a
/// <see cref="TownNPC"/> brain (stroll near where it spawned + occasional barks) on the shared
/// <see cref="PathAgent3D"/> mover, and animates through the SAME machinery the player uses: a
/// <see cref="Character3DPose"/> sink driven by a <see cref="StanceController"/> choke point, fed Walk/Idle by
/// <see cref="TownNPC3DVisual"/>. (Mobs stay billboards; townsfolk + player are live-3D.)
///
/// <para>ONE TOWN, SO ONE SET OF PEOPLE, AND ONLY THE SERVER MAKES THEM. Every machine used to run this and
/// populate its own town, so two players standing in the same square saw different people, in different places,
/// saying different things — and pointing at one of them meant nothing to anybody else. Each townsperson is now
/// announced over the wire (see <see cref="NetNpc"/>) and every player within range receives that same person.
/// The same rule the creature spawner already follows.</para>
///
/// Townsfolk are NOT independently tuned: the cast, the animator, their height, their capsule and their walk speed
/// all come from the shared CharacterSpec (single source of truth), so a townsperson strolls and stands exactly
/// like the player walking. This object decides only WHERE and HOW MANY.
///
/// This is intentionally self-contained (no recipe wiring required): its own transform + <see cref="areaRadius"/>
/// define where folk appear and roam, and it ground-snaps each spawn with a single downward raycast. The
/// per-NPC perf story (simulate-near / freeze-far, pooled popup barks) lives on <see cref="TownNPC"/>.
///
/// Spawning happens ONCE at Start — there's no Instantiate churn afterward (townsfolk persist; the LOD just
/// freezes their brains/animators when you're far), so this respects the no-allocation-in-hot-paths rule.
/// </summary>
[DisallowMultipleComponent]
public class AmbientLife : MonoBehaviour
{
    // The cast, the animator controller, the body height, the capsule and the walk speed all come from the shared
    // CharacterSpec — there is no per-director copy to assign or to drift. This object decides only WHERE and HOW MANY.

    [Header("Population")]
    [Min(0)] public int count = 12;
    [Tooltip("Townsfolk spawn within this radius of THIS object and wander near where they land.")]
    public float areaRadius = 25f;
    [Tooltip("How far each townsperson roams from its own spawn point.")]
    public float wanderRadius = 6f;

    [Header("Barks")]
    [Tooltip("Reactive canned chatter when the player passes — lines vary with time of day (NpcBarks.PickTownLine).")]
    public bool barks = true;

    [Header("Grounding")]
    [Tooltip("Layer(s) treated as the ground for snapping spawns down. Set to the Ground layer by the tool.")]
    public LayerMask groundMask;

    void Start()
    {
        // ONLY THE MACHINE THAT OWNS THE WORLD POPULATES IT. A player receives the people who are already there
        // (see NetNpc); one that made its own would give every player a private town at private positions — which
        // is what this game did before it had a server, and is the thing that makes a shared world impossible.
        if (!GameServer.IsServer)
        {
            enabled = false;
            return;
        }

        var spec = CharacterSpec.Active;
        if (spec.civilianModels == null || spec.civilianModels.Count == 0)
        {
            GameLog.Warn(LogCat.Unwritten, "CharacterSpec has no civilian models — no townsfolk spawned.");
            return;
        }
        if (spec.animatorController == null)
            GameLog.Warn(LogCat.Unwritten, "CharacterSpec has no animatorController — townsfolk will T-pose.");

        for (int i = 0; i < count; i++)
            SpawnOne(spec);
    }

    // One townsperson: pick which body they wear, pick where they stand, build them, and announce them so every
    // player near enough receives the same person.
    //
    // NO MODEL IS FETCHED HERE. The name is the whole of what this machine needs — it has no screen, and a rigged
    // model assembled for nobody is the cost the live-3D creatures already stopped paying. Each watching machine
    // fetches its own copy from the name that crosses the wire.
    void SpawnOne(CharacterSpec spec)
    {
        var modelName = spec.RandomCivilian();
        if (string.IsNullOrWhiteSpace(modelName)) return;

        Vector2 r = Random.insideUnitCircle * areaRadius;
        Vector3 p = GroundSnap(transform.position + new Vector3(r.x, 0f, r.y));

        // The ONE recipe for a townsperson, shared with the machines that only watch one.
        var root = Townsperson.Build(modelName, p, gameObject.scene, null);
        root.GetComponent<TownNPC>().Init(p, wanderRadius, barks);

        GameNetwork.PublishNpc(root, modelName);
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
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.9f, 0.5f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, areaRadius);
    }
}
