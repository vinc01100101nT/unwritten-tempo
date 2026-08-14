using System.Collections;
using UnityEngine;

/// <summary>
/// Brings a billboard mob back after it dies, so the world doesn't empty out. Mobs are built procedurally
/// by an editor tool (no runtime prefab), so instead of destroying + re-instantiating we RECYCLE the same
/// object: <see cref="Health.Die"/> hands off here once <see cref="DeathDissolve"/> finishes its poof/fade,
/// we hide it for <see cref="respawnDelay"/>, then revive it at a fresh spot near its original home.
///
/// Self-contained: it records its home + the ground height offset on Awake (raycasting the <c>Ground</c>
/// layer), and re-enables everything <see cref="DeathDissolve"/> switched off.
/// </summary>
[RequireComponent(typeof(Health))]
public class MobRespawn : MonoBehaviour
{
    [Tooltip("Seconds after the corpse fades before the mob returns.")]
    public float respawnDelay = 1f;   // near-instant respawn — keeps the field full for auto-combat
    [Tooltip("Respawn within this radius of the original spawn home.")]
    public float homeRadius = 4f;
    [Tooltip("Never respawn closer than this to the player (no spawning on their head). Per-instance ON PURPOSE — the " +
             "field spawner has its own, because the map recipe authors clearance per spawn area (MapRecipe." +
             "clearFromSpawn): a tight courtyard and an open field legitimately want different distances.")]
    public float playerClearRadius = 7f;

    Vector3 home;
    int groundMask;
    float footOffset = 0.3f;   // mob feet sit this far above the ground (measured on Awake)

    void Awake()
    {
        home = transform.position;
        groundMask = LayerMask.GetMask("Ground");
        if (groundMask != 0 &&
            Physics.Raycast(home + Vector3.up * 30f, Vector3.down, out var hit, 100f, groundMask, QueryTriggerInteraction.Ignore))
            footOffset = home.y - hit.point.y;
    }

    /// <summary>Called by <see cref="Health.Die"/> after the death FX completes (instead of Destroy).</summary>
    public void OnFadedOut()
    {
        var sr = GetComponent<SpriteRenderer>(); if (sr != null) sr.enabled = false;   // hide while waiting
        // A live-3D shadow renders a BODY, not that sprite — hide it too, or its corpse stands around fully lit
        // for the whole respawn wait.
        var shadow = GetComponent<MobShadow3DVisual>(); if (shadow != null) shadow.SetBodyVisible(false);
        StartCoroutine(RespawnAfter());
    }

    IEnumerator RespawnAfter()
    {
        yield return new WaitForSeconds(respawnDelay);
        Respawn();
    }

    void Respawn() => Reactivate(gameObject, PickSpot());

    /// <summary>Bring a dead/hidden mob back to a live state at <paramref name="pos"/>: restore its death
    /// visuals, refill HP, and re-enable movement/AI/colliders. THE single source of truth for "revive a mob
    /// object" — used by this in-place recycle AND by the pooled <see cref="FieldMobSpawner"/> when it reuses
    /// an object, so the tricky re-enable order + stepOffset clamp can't drift between the two paths.</summary>
    public static void Reactivate(GameObject mob, Vector3 pos)
    {
        mob.transform.position = pos;

        var dd = mob.GetComponent<DeathDissolve>(); if (dd != null) dd.ResetAfterRespawn();   // restore lit material + alpha

        // A respawn is a FRESH creature, not the same one resuming its career: drop back to the authored level so a
        // recycled object can't keep the levels it earned last life. Without this, farming one spot ratchets the zone
        // permanently upward (pooled objects accumulate levels they never lose). Runs BEFORE Revive so the refill
        // clamps to the reset level's max HP, not the higher one it died with.
        var block = mob.GetComponent<UnitStatBlock>(); if (block != null) block.ResetToSpawnLevel();
        var brain = mob.GetComponent<MonsterAI3D>(); if (brain != null) brain.ResetCombatXp();   // and its banked XP
        var health = mob.GetComponent<Health>(); if (health != null) health.Revive();          // full HP again

        // Re-enable everything DeathDissolve disabled when it became "remains".
        // PRESENTATION is owned by the unit, not assumed here. A billboard mob gets its sprite back; a live-3D
        // shadow restores its own BODY and must keep the sprite off — forcing sr.enabled = true on one is what made
        // respawned shadows come back as sprites.
        var shadow = mob.GetComponent<MobShadow3DVisual>();
        if (shadow != null) shadow.OnRevived();
        else { var sr = mob.GetComponent<SpriteRenderer>(); if (sr != null) sr.enabled = true; }
        Enable<MonsterAI3D>(mob); Enable<BillboardAnimator3D>(mob); Enable<AttackLunge>(mob);
        // Enable the controller BEFORE its mover so PathAgent3D never runs a frame against a disabled cc.
        // Clamp stepOffset FIRST: BillboardAnimator3D scales mobs by UnitScale.ForMob, and once a mob is small
        // enough the authored 0.3 step exceeds the capsule's climbable height (see ClampStepOffset for the exact
        // bound). Unity then rejects the enable and logs "Step Offset must be ≤ …" every respawn (floods during
        // auto-combat) — and a controller left disabled also spams "Move called on inactive controller". Fitting
        // the step to the live scale stops both.
        var cc = mob.GetComponent<CharacterController>();
        if (cc != null) { ClampStepOffset(cc); cc.enabled = true; }
        var agent = mob.GetComponent<PathAgent3D>(); if (agent != null) agent.enabled = true;
        foreach (var col in mob.GetComponentsInChildren<Collider>(true)) col.enabled = true;

        // LAST: hand the mover + animator back to the freeze switch. Everything above turns a mob's parts on wholesale,
        // which silently un-freezes a unit revived FAR from the player — and the switch only acts when its near/far
        // answer changes, so "still far" would early-out and leave it running forever. Re-asserting here keeps the
        // switch's belief and the components in step. (Same class of bug as the sprite/3D-body restore right above.)
        var lod = mob.GetComponent<SimulationLod>(); if (lod != null) lod.Resync();
    }

    // A ground point within homeRadius of home, kept clear of the player; falls back to home.
    Vector3 PickSpot()
    {
        var player = FindAnyObjectByType<Player3DController>();
        Vector3 p = player != null ? player.transform.position : home;

        for (int i = 0; i < 16; i++)
        {
            Vector2 r = Random.insideUnitCircle * homeRadius;
            Vector3 probe = new Vector3(home.x + r.x, home.y + 30f, home.z + r.y);
            if (groundMask != 0 &&
                Physics.Raycast(probe, Vector3.down, out var hit, 120f, groundMask, QueryTriggerInteraction.Ignore))
            {
                float dx = hit.point.x - p.x, dz = hit.point.z - p.z;
                if (Mathf.Sqrt(dx * dx + dz * dz) < playerClearRadius) continue;   // too close to the player
                return hit.point + Vector3.up * footOffset;
            }
        }
        return home;
    }

    static void Enable<T>(GameObject go) where T : MonoBehaviour { var c = go.GetComponent<T>(); if (c != null) c.enabled = true; }

    // Unity rejects a CharacterController enable when stepOffset exceeds the capsule's climbable height, and our
    // billboard mobs shrink at runtime (UnitScale.ForMob), so the authored 0.3 step overshoots once they're small.
    // The editor warning reads "≤ scaledHeight + scaledRadius·2", but the ACTUALLY-enforced bound is tighter:
    // PhysX treats CharacterController.height as the FULL capsule height (the two end-caps ARE the radius), so the
    // real allowance is the CYLINDER height (height − 2·radius, floored at 0) plus 2·radius, all scaled. Using the
    // full height over-counted the allowance by 2·scaledRadius — so mid-size mobs slipped past the old clamp and
    // Unity still threw (and a controller left disabled then spams "Move called on inactive controller"). We clamp
    // DOWN to the cylinder allowance with a margin that also absorbs the idle-breathe squash (which lowers scale.y
    // a few % after respawn) and float slop. Never raise — only shrink to keep the enable valid at any mob size.
    static void ClampStepOffset(CharacterController cc)
    {
        Vector3 s = cc.transform.lossyScale;
        float scaleY = Mathf.Abs(s.y);
        float scaleR = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        float cylH    = Mathf.Max(cc.height - 2f * cc.radius, 0f) * scaleY;   // PhysX cylinder portion
        float scaledR = cc.radius * scaleR;
        float maxStep = (cylH + 2f * scaledR) * 0.9f;                          // 0.9 = breathing + float headroom
        if (cc.stepOffset > maxStep) cc.stepOffset = Mathf.Max(0f, maxStep);
    }
}
