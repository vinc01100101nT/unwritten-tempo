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
    [Tooltip("Never respawn closer than this to the player (no spawning on their head).")]
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
        StartCoroutine(RespawnAfter());
    }

    IEnumerator RespawnAfter()
    {
        yield return new WaitForSeconds(respawnDelay);
        Respawn();
    }

    void Respawn()
    {
        transform.position = PickSpot();

        var dd = GetComponent<DeathDissolve>(); if (dd != null) dd.ResetAfterRespawn();   // restore lit material + alpha
        GetComponent<Health>().Revive();                                                   // full HP again

        // Re-enable everything DeathDissolve disabled when it became "remains".
        var sr = GetComponent<SpriteRenderer>(); if (sr != null) sr.enabled = true;
        Enable<MonsterAI3D>(); Enable<BillboardAnimator3D>(); Enable<AttackLunge>();
        var agent = GetComponent<PathAgent3D>(); if (agent != null) agent.enabled = true;
        var cc = GetComponent<CharacterController>(); if (cc != null) cc.enabled = true;
        foreach (var col in GetComponentsInChildren<Collider>(true)) col.enabled = true;
    }

    // A ground point within homeRadius of home, kept clear of the player; falls back to home.
    Vector3 PickSpot()
    {
        var player = FindFirstObjectByType<Player3DController>();
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

    void Enable<T>() where T : MonoBehaviour { var c = GetComponent<T>(); if (c != null) c.enabled = true; }
}
