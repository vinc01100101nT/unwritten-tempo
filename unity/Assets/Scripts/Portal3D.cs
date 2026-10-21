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

    // Each visible child's own turn as placed, kept so facing the centre never piles one turn on another.
    Quaternion[] placedTurns;

    // The trigger whose footprint is the circle, held once.
    BoxCollider footprint;

    void OnEnable()
    {
        if (footprint == null) footprint = GetComponent<BoxCollider>();
        Active.Add(this);
        FaceMapCentre();
    }

    /// <summary>THE DOOR'S PICTURE FACES THE MIDDLE OF ITS MAP: every visible child turns about the upright axis only
    /// (it stays standing) so its front points at the centre of the map's ground, as the minimap list records it.
    /// Once per door each time its map loads; nothing per frame, nothing saved. A map with no recorded ground, or a
    /// door standing on the centre, keeps the turn it was placed with.</summary>
    void FaceMapCentre()
    {
        if (!Application.isPlaying) return;
        var catalog = MinimapCatalog.Instance;
        var entry = catalog != null ? catalog.Find(gameObject.scene.name) : null;
        if (entry == null) return;
        float half = entry.worldSize * 0.5f;
        var toCentre = new Vector3(entry.worldMin.x + half - transform.position.x, 0f,
                                   entry.worldMin.y + half - transform.position.z);
        if (toCentre.sqrMagnitude < 0.0001f) return;
        var facing = Quaternion.LookRotation(toCentre, Vector3.up);
        int n = transform.childCount;
        if (placedTurns == null || placedTurns.Length != n)
        {
            placedTurns = new Quaternion[n];
            for (int i = 0; i < n; i++) placedTurns[i] = transform.GetChild(i).localRotation;
        }
        for (int i = 0; i < n; i++) transform.GetChild(i).rotation = facing * placedTurns[i];
    }
    void OnDisable() { Active.Remove(this); }

    // When the component is first added, make its collider a trigger.
    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    // FEET ON THE CIRCLE, NOT A BODY IN THE AIR. The trigger is the circle's footprint and only a sliver tall, and it
    // is asked while the walker stays in it: a door is taken the moment they stand on the ground inside the circle —
    // landing on it from a jump counts, flying over it does not. Runs only while somebody is inside the trigger, and
    // the two cheap checks come before the component lookup.
    void OnTriggerStay(Collider other)
    {
        if (fired || string.IsNullOrEmpty(targetScene)) return;
        if (!(other is CharacterController walker) || !walker.isGrounded) return;
        if (!FeetInsideCircle(other.transform.position)) return;
        if (other.GetComponentInParent<Player3DController>() == null) return;   // only the player triggers it
        fired = true;

        // NOT WHILE AUTO-COMBAT IS ON — a hunt that chased something through a door would carry on in a map nobody
        // chose. Said once; the latch above keeps it from being said again until they step out of the door.
        if (AutoCombatClient.On)
        {
            OverheadNotice.Warn("Switch auto-combat off to use a door.");
            return;
        }

        // THE CURTAIN GOES UP THE MOMENT THE DOOR IS TOUCHED, before anything is asked. The player has already
        // committed by walking into it, and the world behind them must stop being something they can act on —
        // whether the map on the other side is held by this same server or by another one entirely. A teleport: the
        // effect starts and the player vanishes on this frame, the screen fades to black, and only once it is black
        // is the server asked — the map they are leaving is never seen going.
        WalkThrough(targetScene, spawnId);
    }

    /// <summary>THE ONE WAY THROUGH A DOOR — a portal walked into, and a GM's <c>/tp</c>, which the server hands to
    /// this same call: the effect and the vanish now, the fade to black, and the server asked once it is black.</summary>
    public static void WalkThrough(string map, string arriveAtSpawnId)
    {
        TeleportLook.CoverWithCurtain(() => AskToWalkThrough(map, arriveAtSpawnId));
    }

    static void AskToWalkThrough(string map, string arriveAtSpawnId)
    {
        // AND THE SERVER DECIDES, not this machine. The map on the other side may be held by a different
        // process, and only the server can find out which, bring it up, or refuse — so this asks instead of
        // travelling. When the map turns out to be right here, the answer comes straight back and the door is
        // exactly as instant as it has always been.
        var me = GameNetwork.MyPlayer;
        if (me != null) { me.CmdWalkThroughPortal(map, arriveAtSpawnId); return; }

        // No world to ask. There is no offline game, so this is a broken state rather than a case to serve —
        // said out loud instead of silently doing nothing at a door the player is standing in.
        LoadingScreen.Hide();
        GameLog.Warn(LogCat.Client, "walked into a door with no world to ask — staying put.");
    }

    // Within the circle the trigger's square footprint draws, measured flat.
    bool FeetInsideCircle(Vector3 feet)
    {
        float radius = footprint != null ? 0.5f * Mathf.Min(footprint.size.x, footprint.size.z) : 0.5f;
        float dx = feet.x - transform.position.x, dz = feet.z - transform.position.z;
        return dx * dx + dz * dz <= radius * radius;
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
