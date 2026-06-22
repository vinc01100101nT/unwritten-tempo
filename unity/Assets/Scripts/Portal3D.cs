using UnityEngine;

/// <summary>
/// A doorway between 3D maps — the 2.5D analogue of <see cref="Portal"/> (which is 2D-only:
/// Collider2D + PlayerController2D). When the billboard player (<see cref="Player3DController"/>,
/// a CharacterController) walks into this trigger collider, it asks <see cref="MapManager"/> to
/// swap to <c>targetScene</c> — loaded additively under the persistent <c>Systems</c> scene, with
/// the old map unloaded. The arriving <see cref="PersistentPlayer3D"/> reads <c>spawnId</c> and
/// places the player at the matching <see cref="SpawnPoint"/>.
///
/// Both maps must be listed in File ▸ Build Settings (the region bakers do this for you).
/// </summary>
[RequireComponent(typeof(Collider))]
public class Portal3D : MonoBehaviour
{
    [Tooltip("Name of the scene to load when the player enters (must be in Build Settings).")]
    public string targetScene;

    [Tooltip("Id of the SpawnPoint in the target scene to arrive at (leave empty for that map's default).")]
    public string spawnId;

    /// <summary>Live portals in the loaded map(s). The <see cref="Minimap"/> reads this to draw portal
    /// blips with zero per-frame allocation (same pattern as <c>MonsterAI3D.Active</c>).</summary>
    public static readonly System.Collections.Generic.List<Portal3D> Active = new System.Collections.Generic.List<Portal3D>();

    // Guards against the trigger firing again on the frames between entering and the async map load
    // actually swapping scenes (the CharacterController can report several OnTriggerEnter ticks).
    bool fired;

    void OnEnable()  { Active.Add(this); }
    void OnDisable() { Active.Remove(this); }

    // When the component is first added, make its collider a trigger.
    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (fired || string.IsNullOrEmpty(targetScene)) return;
        if (other.GetComponentInParent<Player3DController>() == null) return;   // only the player triggers it
        fired = true;
        MapManager.Travel(targetScene, spawnId);   // additive swap; Systems (HUD/player) stays loaded
    }

    // Draw a translucent box in the Scene view so you can place it (the trigger is invisible in-game;
    // the bakers add a separate visible "warp pad" child).
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.45f);
        var col = GetComponent<Collider>();
        Vector3 size = col != null ? col.bounds.size : Vector3.one;
        Gizmos.DrawCube(transform.position, size);
    }
}
