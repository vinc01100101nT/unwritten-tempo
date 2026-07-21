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
    [Header("Cast (rigged 3D models)")]
    [Tooltip("Rigged Quaternius character models to draw townsfolk from (Assets/1_Authored/Art/Characters/Models). " +
             "Assigned for you by the editor tool (civilians only — soldiers/knights become combat NPCs later).")]
    public List<GameObject> models = new List<GameObject>();
    [Tooltip("The shared Humanoid animator controller the player uses (HumanoidPlayerController). Every model's own " +
             "embedded avatar retargets it, so one controller animates the whole cast. Assigned by the editor tool.")]
    public RuntimeAnimatorController sharedController;

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

    // Last-resort height if there's no live 3D player to measure at spawn (shouldn't happen in Play). Mirrors the
    // player build's Character3DRigBuilder.TargetHeight; the live read below is the real source of truth.
    const float FallbackHeight = 0.327f;

    void Start()
    {
        if (models == null || models.Count == 0)
        {
            Debug.LogWarning("[unwritten] AmbientLife on '" + name + "' has no models — no townsfolk spawned. " +
                             "Assign rigged character models (or re-run the Add Ambient Townsfolk tool).");
            return;
        }
        if (sharedController == null)
            Debug.LogWarning("[unwritten] AmbientLife on '" + name + "' has no sharedController — townsfolk will " +
                             "T-pose. Assign HumanoidPlayerController (or re-run the Add Ambient Townsfolk tool).");

        var pc = FindFirstObjectByType<Player3DController>();
        Transform player = pc != null ? pc.transform : null;

        // Read the player's own values ONCE — townsfolk copy them (single source of truth), no per-NPC tuning:
        //   walk speed = the player's WALK travel speed (moveSpeed × scale × walkSpeedMult × gaitSpeed),
        //   gait       = the player's leg-cadence multiplier (GaitSpeedMult baseline = gaitSpeed),
        //   height     = the player's actual rendered world height (its Character3DPose is the only 3D char yet).
        float walkSpeed = 0.6f, gait = 1.5f, targetHeight = FallbackHeight;
        if (pc != null)
        {
            walkSpeed = pc.moveSpeed * UnitScale.ForPlayer * pc.walkSpeedMult * pc.gaitSpeed;
            gait = pc.gaitSpeed;
        }
        var playerPose = FindFirstObjectByType<Character3DPose>();   // before any NPC spawns, this is the player's
        if (playerPose != null && playerPose.VisualWorldHeight > 1e-3f) targetHeight = playerPose.VisualWorldHeight;

        for (int i = 0; i < count; i++)
            SpawnOne(player, walkSpeed, gait, targetHeight);
    }

    void SpawnOne(Transform player, float walkSpeed, float gait, float targetHeight)
    {
        var model = models[Random.Range(0, models.Count)];
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

        // Scaled visual model as a child, feet at the root pivot.
        var vis = Instantiate(model);
        vis.hideFlags = HideFlags.DontSaveInEditor;
        vis.transform.SetParent(root.transform, false);
        float h0 = MeasureHeight(vis);   // native render height (child sits at parent scale 1 here)
        vis.transform.localScale = Vector3.one * (h0 > 1e-3f ? targetHeight / h0 : 1f);
        vis.transform.localPosition = Vector3.zero;

        // Swap the model's own animator onto the shared Humanoid controller (its embedded avatar retargets it) — the
        // same swap PlayerModelSwap does for the player — plus the pose sink. Both live on the (scaled) model child.
        var anim = vis.GetComponent<Animator>() ?? vis.AddComponent<Animator>();
        if (sharedController != null) anim.runtimeAnimatorController = sharedController;
        anim.applyRootMotion = false;
        if (vis.GetComponent<Character3DPose>() == null) vis.AddComponent<Character3DPose>();

        // Stance choke point on the ROOT (it resolves the pose sink from the child). selfDrive OFF so a still NPC
        // holds the authored Idle stance (like the player); gait cadence is the player's own so the Walk clip paces
        // the player's walk speed (no foot-slide).
        var stance = root.AddComponent<StanceController>();
        stance.selfDrive = false;
        stance.externalGaitMult = gait;

        // Shared mover on the UNSCALED root (auto-adds a CharacterController). Dims are WORLD units now.
        var agent = root.AddComponent<PathAgent3D>();
        agent.agentRadius = Mathf.Max(0.05f, targetHeight * 0.3f);
        var cc = root.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.height = targetHeight * 0.9f;
            cc.center = new Vector3(0f, targetHeight * 0.45f, 0f);
            cc.radius = targetHeight * 0.18f;
            cc.stepOffset = targetHeight * 0.25f;
            cc.slopeLimit = 45f;
        }

        // 3D animation driver + wander brain, both on the root (added last so they resolve their siblings).
        root.AddComponent<TownNPC3DVisual>();
        var npc = root.AddComponent<TownNPC>();
        npc.Init(p, wanderRadius, walkSpeed, barks, player);
    }

    // World-space rendered height (union of all renderer bounds) at the object's CURRENT scale. Called right after
    // Instantiate (scale 1), so it returns the model's native height — the basis for the fit-to-world scale.
    static float MeasureHeight(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return 0f;
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b.size.y;
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
