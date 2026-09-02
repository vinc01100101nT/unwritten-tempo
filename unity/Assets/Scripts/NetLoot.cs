using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One drop, as every player sees it. The SERVER owns the drop — what it is, where it lies, and who is allowed to
/// take it — and this object is the thin thing that crosses the wire: which item (or how much Zeny), where it
/// burst from and came to rest, and which map it lies in. Every client builds its own body from that, exactly the
/// way <see cref="NetMob"/> builds a body for a creature.
///
/// <para><b>A drop is competed for, and a creature is not.</b> So the body a watching machine draws gives nothing
/// away by itself: it ASKS (see <see cref="GameNetwork.SendPickupToServer"/>), the server decides, and this shell
/// is destroyed for everybody the moment one person gets it. Two people clicking the same coin means one of them
/// is quietly told no.</para>
///
/// <para><b>Why this lives in this file.</b> It is the wire half of the thing above it, and the file it would
/// otherwise want — one of its own, named after it — is the file a prefab needs. There is no loot prefab yet; the
/// shell is built at runtime on both sides instead (see <see cref="GameNetwork.PublishLoot"/>).</para>
/// </summary>
public class NetLoot : NetworkBehaviour
{
    /// <summary>Every drop shell alive on this machine, found by the BODY it drew — so a click on that body can
    /// name the drop to the server. A dictionary for the same reason the creature side keeps one: it is asked
    /// from a click path, not walked.</summary>
    static readonly Dictionary<GameObject, NetLoot> byView = new Dictionary<GameObject, NetLoot>();

    /// <summary>The shell that speaks for <paramref name="viewObject"/>, or null if that drop is not one this
    /// machine received from a server — an offline game's own drop, which no server has heard of.</summary>
    public static NetLoot ShellForView(GameObject viewObject)
    {
        for (var t = viewObject != null ? viewObject.transform : null; t != null; t = t.parent)
            if (byView.TryGetValue(t.gameObject, out var shell) && shell != null) return shell;
        return null;
    }

    /// <summary>Which item this is, by catalog id. Empty for Zeny. Set once by the server before it spawns.</summary>
    [SyncVar] string itemId = "";

    /// <summary>How much Zeny this is. 0 for an item drop.</summary>
    [SyncVar] int gold;

    /// <summary>WHICH MAP this drop lies in, by scene name — the same value, for the same reason, that a creature
    /// carries: the world is several maps at once sharing one set of coordinates, so without it a coin dropped in
    /// the field would be drawn on a town floor a metre from somebody standing in the town.</summary>
    [SyncVar] string mapName = "";

    [SyncVar] Vector3 netStart;
    [SyncVar] Vector3 netLanding;

    /// <summary>What this drop is worth. Read on the SERVER, when it decides who gets it.</summary>
    public string ItemId => itemId;
    public int Gold => gold;

    // SERVER ONLY. A drop pays out once however many people click it in the same breath.
    bool claimed;

    /// <summary>Server-only: take this drop, once. FALSE means somebody else already has it.</summary>
    public bool ClaimOnServer()
    {
        if (claimed) return false;
        claimed = true;
        return true;
    }

    /// <summary>Server-only: hand this shell the drop it speaks for.</summary>
    public void BindOnServer(Item item, int goldAmount, Vector3 start, Vector3 landing, string map)
    {
        itemId = item != null ? item.id : "";
        gold = goldAmount;
        mapName = map ?? "";

        // WHERE IT IS INSIDE ITS MAP, not where this machine happens to keep that map — see MapManager's parking.
        netStart = MapManager.InMap(start, mapName);
        netLanding = MapManager.InMap(landing, mapName);

        // AND THE SHELL ITSELF LIES THERE TOO. The position this object CARRIES and the position of the object are
        // two different numbers, and it is the second one the server measures to decide who is told about it — the
        // lesson the creature side already paid for (see NetMob.StandWhereTheMobStands).
        transform.position = landing;

        // AND IN THE MAP THE DROP FELL IN. Same reason as the creature side: watcher lists are built map first,
        // so a shell sitting in the map the server loaded last is one nobody standing over the drop is told about.
        MapManager.PutInMap(gameObject, SceneManager.GetSceneByName(mapName));
    }

    // ---- everybody else's half: show it ------------------------------------------------------------------

    GameObject view;

    // The map this machine was standing in when we last decided whether to draw this drop. An INTEGER, not the
    // name: it only ever moves when a person walks through a door.
    int seenMapVersion = -1;
    bool inMyMap;

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Out of the map this machine is standing in, before a door can destroy it — a shell belongs to the world.
        MapManager.KeepAcrossMaps(gameObject);
    }

    void Update()
    {
        // Nothing for the owning half to push: a drop does not move, and a server has no screen to draw it on.
        if (isServer || !isClient) return;

        // AM I EVEN IN THE PLACE THIS DROP IS? Re-decided when this machine changes map, and while the drop has
        // not said which map it is in yet — a value that can land a frame or two after the drop itself.
        bool mapKnown = !string.IsNullOrEmpty(mapName);
        if (seenMapVersion != MapManager.MapVersion || !mapKnown)
        {
            seenMapVersion = mapKnown ? MapManager.MapVersion : -1;
            inMyMap = mapKnown && mapName == MapManager.CurrentMap;
            if (!inMyMap) DropTheView();
        }

        if (!inMyMap || view != null) return;
        BuildTheView();
    }

    void BuildTheView()
    {
        var body = LootDropper.BuildBody(ItemCatalog.Resolve(itemId), gold, LootDropper.WatchedCoinSprite(),
                                         MapManager.InWorld(netStart, mapName),
                                         MapManager.InWorld(netLanding, mapName));
        if (body == null) return;

        view = body.gameObject;
        byView[view] = this;

        // The body belongs to the WORLD, not to the map this machine happens to be standing in when it arrives.
        MapManager.KeepAcrossMaps(view);
    }

    /// <summary>Let this drop's body go. The ONE description of "stop drawing this", because there are two reasons
    /// to — the shell going away, and this machine walking into another map.</summary>
    void DropTheView()
    {
        if (view == null) return;
        byView.Remove(view);   // before the object goes, or the table keeps a dead key forever
        Destroy(view);
        view = null;
    }

    public override void OnStopClient()
    {
        DropTheView();
        base.OnStopClient();
    }
}
