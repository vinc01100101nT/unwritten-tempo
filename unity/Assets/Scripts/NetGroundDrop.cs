using Mirror;
using UnityEngine;

/// <summary>
/// One thing lying on the ground, as every player sees it. The SERVER owns the real drop — which particular
/// sword it is, with its folds, its grade and its cards — and this is the thin thing that crosses the wire: the
/// item's id, how many, its grade, its folds, its rarity, which map it lies in and where inside that map. Every watching
/// machine builds its own <see cref="GroundDrop"/> from those, exactly the way <see cref="NetMob"/> builds a
/// body for a creature and <see cref="NetNpc"/> builds one for a townsperson.
///
/// <para><b>The rolled copy never travels.</b> A watcher is told the NAME to write on the floor and the COLOUR
/// to write it in, and nothing else. It has no business holding the item itself: it does not decide who picks it
/// up, and a machine handed a real copy of a piece of equipment is a machine that could put one in a bag nobody
/// gave it. Picking up names this drop and asks; the server answers out of the copy it kept.</para>
///
/// <para><b>Nothing here is written down.</b> A drop is not saved, so it lives exactly as long as the process
/// holding its map — see <see cref="GroundDrop"/>.</para>
/// </summary>
public class NetGroundDrop : NetworkBehaviour
{
    [SyncVar] string itemId = "";
    [SyncVar] int count = 1;
    [SyncVar] int qualityIndex;
    [SyncVar] int upgrade;

    /// <summary>How rare it is, which colours its name — worked out on the server, where the copy and its trademark
    /// are (<see cref="Rarities.Of"/>).</summary>
    [SyncVar] int rarityIndex;

    /// <summary>Which map it lies in. A server holds several at once and parks them apart, so without this every
    /// machine would draw every drop in the world into whichever map its player happened to be standing in.</summary>
    [SyncVar] string mapName = "";

    /// <summary>Where it lies INSIDE its map, not where any one machine happens to keep that map — a watcher
    /// adds back its own answer for where the map is (see <see cref="MapManager.InWorld"/>).</summary>
    [SyncVar] Vector3 inMap;

    /// <summary>SERVER ONLY: the real drop, with the rolled copy. Null on every watching machine.</summary>
    public GroundDrop ServerDrop { get; private set; }

    /// <summary>Server-only: hand this shell the drop it speaks for. Called before the shell is announced, so
    /// everything a watcher needs arrives with it in one message.</summary>
    public void BindOnServer(GroundDrop drop)
    {
        if (drop == null) return;
        ServerDrop = drop;
        itemId = drop.def != null ? drop.def.id : "";
        count = drop.count;
        qualityIndex = (int)drop.quality;
        upgrade = drop.upgrade;
        rarityIndex = (int)drop.rarity;
        mapName = drop.mapName;
        inMap = MapManager.InMap(drop.transform.position, drop.mapName);

        // AND THE SHELL ITSELF LIES THERE TOO. Where the object IS decides who the server tells about it; a
        // shell left at the origin would be announced to whoever stood near the origin and to nobody else.
        transform.position = drop.transform.position;
        var scene = drop.gameObject.scene;
        if (scene.IsValid()) MapManager.PutInMap(gameObject, scene);
    }

    // ---- everybody else's half: show it -------------------------------------------------------------------

    GroundDrop view;
    int seenMapVersion = -1;

    // Stop asking. An id this build has no item for is never going to turn into one, and the question below runs
    // every frame until it is answered — so it is asked once, said out loud once, and then dropped.
    bool givenUp;

    public override void OnStartClient()
    {
        // ON A MACHINE THAT IS ALSO THE SERVER there is already a real drop standing there, and building a
        // second one would put two sets of words on one spot and give the reach two things to find.
        if (isServer) { view = ServerDrop; return; }
        TryBuildView();
    }

    public override void OnStopClient()
    {
        if (isServer) return;
        if (view != null) view.Remove();
        view = null;
    }

    void OnDestroy()
    {
        // The server's own copy goes with the shell that announced it — claimed, or the map being torn down.
        if (ServerDrop != null) { ServerDrop.Remove(); ServerDrop = null; }
    }

    void TryBuildView()
    {
        if (view != null || givenUp || string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(mapName)) return;
        var def = ItemCatalog.Resolve(itemId);
        if (def == null)
        {
            givenUp = true;
            GameLog.Warn(LogCat.Net, "somebody dropped '" + itemId + "', which this build has no item for — "
                             + "nothing will be shown lying there.");
            return;
        }

        view = GroundDrop.Place(MapManager.InWorld(inMap, mapName), def, count, null,
                                (Quality)qualityIndex, upgrade, mapName, netId, (Rarity)rarityIndex);
        if (view == null) return;
        view.ShowOnScreen();
        // DECIDED IN THE SAME BREATH AS IT IS BUILT, not on the next frame. Waiting left a drop in somebody
        // else's map lit up and reachable for a frame, in a place this machine parks that map and the player
        // is not.
        seenMapVersion = MapManager.MapVersion;
        view.SetReadable(mapName == MapManager.CurrentMap);
    }

    void Update()
    {
        // THE SERVER'S HALF: something with an end that nobody took goes away, and every watcher's copy with it.
        if (isServer)
        {
            if (ServerDrop != null && ServerDrop.Expired)
            {
                LogVanished(ServerDrop);
                NetworkServer.Destroy(gameObject);
            }
            return;
        }
        if (!isClient) return;

        // WHAT ARRIVED LATE. The item's id and its map are values of their own on the wire and can land a frame
        // or two after the object itself, exactly as a creature's kind does — so this keeps asking until there
        // is something to build, rather than deciding once on an empty name and never drawing anything.
        if (view == null) { TryBuildView(); return; }

        // AM I EVEN IN THE PLACE THIS THING IS LYING? Re-decided only when this machine changes map, which is
        // when a person walks through a door — never once per frame in the settled case.
        if (seenMapVersion == MapManager.MapVersion) return;
        seenMapVersion = MapManager.MapVersion;

        bool inMyMap = mapName == MapManager.CurrentMap;
        view.SetReadable(inMyMap);
        // A map is set down in a different place on each machine, and where it is set down can change as maps
        // come and go — so where the words belong is worked out again whenever that happens.
        if (inMyMap) view.transform.position = MapManager.InWorld(inMap, mapName);
    }

    // "lst_fild01 Rawhide ×2 vanished untaken after 60s — it fell for patches #201": what a player LEFT on the ground,
    // which is as telling in a play-test as what they took. Only a kill's drops have an end, so only they say this.
    static void LogVanished(GroundDrop drop)
    {
        PlaySession.Vanished(drop.reservedForId);
        if (!GameLog.On(LogCat.Server)) return;
        int lay = Mathf.RoundToInt(Time.time - drop.placedAt);
        string forWhom = !string.IsNullOrEmpty(drop.reservedForName) ? drop.reservedForName : drop.reservedForId;
        GameLog.Info(LogCat.Server, string.Concat(drop.mapName, " ", drop.Text, " vanished untaken after ", lay.ToString(),
                                                  "s", string.IsNullOrEmpty(forWhom) ? "" : " — it fell for " + forWhom));
    }
}
