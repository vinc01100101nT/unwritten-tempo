using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drop-in "make this map feel alive" director. Place ONE in a scene (Tools ▸ unwritten ▸ World ▸ Add Ambient
/// Townsfolk) and at Play it spawns wandering LIVE-3D townsfolk drawn from the rigged Quaternius models — the same
/// cast the player is built from, so there's zero new art. Each gets a <see cref="TownNPC"/> brain (stroll near
/// where it spawned + occasional barks) on the shared <see cref="PathAgent3D"/> mover, and animates through the SAME
/// machinery the player uses: a <see cref="Character3DPose"/> sink driven by a <see cref="StanceController"/> choke
/// point, fed Walk/Idle by <see cref="TownNPC3DVisual"/>. (Mobs stay billboards; townsfolk + player are live-3D.)
///
/// Townsfolk are NOT independently tuned: their walk speed, gait cadence, and height are READ off the live player at
/// spawn (single source of truth), so a townsperson strolls and stands exactly like the player walking.
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
        var spec = CharacterSpec.Active;
        if (spec.civilianModels == null || spec.civilianModels.Count == 0)
        {
            Debug.LogWarning("[unwritten] CharacterSpec has no civilian models — no townsfolk spawned.");
            return;
        }
        if (spec.animatorController == null)
            Debug.LogWarning("[unwritten] CharacterSpec has no animatorController — townsfolk will T-pose.");

        var pc = FindAnyObjectByType<Player3DController>();
        Transform player = pc != null ? pc.transform : null;

        for (int i = 0; i < count; i++)
            SpawnOne(player, spec);
    }

    void SpawnOne(Transform player, CharacterSpec spec)
    {
        var model = spec.RandomCivilian();
        if (model == null) return;

        Vector2 r = Random.insideUnitCircle * areaRadius;
        Vector3 p = GroundSnap(transform.position + new Vector3(r.x, 0f, r.y));

        // The MOVER must live on an UNSCALED root: a CharacterController silently refuses to translate when its
        // transform is scaled down to character size (~0.1) — MEASURED, its move came out 0 at scale 0.098 but works
        // at scale 1. So (same split the player uses) the unscaled root carries the controller + brain, and the
        // scaled visual model rides it as a child.
        var root = new GameObject("Townsfolk_" + model.name);
        root.transform.position = p;
        root.hideFlags = HideFlags.DontSaveInEditor;   // runtime spawn — never bake a stray copy into the scene

        // The scaled model child + its animator + the pose sink + the stance choke point — built by the SHARED
        // character-body recipe, the same one the 3D shadow mobs use, so there is one definition of "a character
        // body" rather than one per unit type. Leg cadence is no longer handed in: the stance choke point folds it
        // live from the shared baseline and this unit's own Slow/Haste.
        Character3DBody.Build(root, model, spec.animatorController, spec.bodyHeight);

        // Shared mover on the UNSCALED root (auto-adds a CharacterController), then the ONE character rig — the same
        // capsule and routing footprint the player and the shadow mobs get. This used to be a copy of the formula.
        var agent = root.AddComponent<PathAgent3D>();
        Character3DBody.ApplyRig(root, agent);

        // 3D animation driver + wander brain, both on the root (added last so they resolve their siblings).
        root.AddComponent<TownNPC3DVisual>();
        var npc = root.AddComponent<TownNPC>();
        npc.Init(p, wanderRadius, spec.WalkGearSpeed, barks, player);
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
