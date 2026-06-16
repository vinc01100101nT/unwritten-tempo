using UnityEngine;

/// <summary>
/// A named arrival point. A <see cref="Portal"/>'s <c>spawnId</c> matches a
/// SpawnPoint's <c>id</c> in the destination scene — that's where the player
/// appears after travelling. Place it just OUTSIDE the return portal's trigger
/// so you don't immediately bounce back.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    [Tooltip("Unique id within this scene, matched by a Portal's Spawn Id.")]
    public string id = "Entrance";

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, 0.4f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.6f);
    }
}
