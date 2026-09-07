using UnityEngine;

/// <summary>
/// A doorway between 3D maps. When the billboard player (<see cref="Player3DController"/>,
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

        // THE CURTAIN GOES UP THE MOMENT THE DOOR IS TOUCHED, before anything is asked. The player has already
        // committed by walking into it, and the world behind them must stop being something they can act on —
        // whether the map on the other side is held by this same server or by another one entirely.
        LoadingScreen.Show("Stepping through…");

        // AND THE SERVER DECIDES, not this machine. The map on the other side may be held by a different
        // process, and only the server can find out which, bring it up, or refuse — so this asks instead of
        // travelling. When the map turns out to be right here, the answer comes straight back and the door is
        // exactly as instant as it has always been.
        var me = GameNetwork.MyPlayer;
        if (me != null) { me.CmdWalkThroughPortal(targetScene, spawnId); return; }

        // No world to ask. There is no offline game, so this is a broken state rather than a case to serve —
        // said out loud instead of silently doing nothing at a door the player is standing in.
        LoadingScreen.Hide();
        GameLog.Warn(LogCat.Client, "walked into a door with no world to ask — staying put.");
    }

    // WALKING BACK OUT MAKES THE DOOR USABLE AGAIN. The latch above only exists to stop the same step through a
    // door being counted several times while the answer is still coming back. When the answer is "no" the player
    // is left standing exactly where they were — inside this trigger — and without this the door they are stood
    // in is dead for the rest of the session and there is nothing on screen to say why. Clearing it here and not
    // on the refusal is deliberate: standing in a door that just refused them must not ask again every frame.
    void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<Player3DController>() == null) return;
        fired = false;
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
