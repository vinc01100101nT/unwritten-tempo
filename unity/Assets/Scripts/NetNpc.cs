using Mirror;
using UnityEngine;

/// <summary>
/// One townsperson, as every player sees them. The SERVER owns the real person — their wander brain, their
/// routing, the moment they speak — and this object is the thin thing that crosses the wire: which body they
/// wear, which map they are in, where they are, which way they are facing, how fast they are walking, and the
/// last thing they said. Every client builds its own copy of the body from that, exactly the way
/// <see cref="NetMob"/> builds a body for a creature and <see cref="NetPlayer"/> builds one for a person.
///
/// <para><b>Why not send the body itself.</b> A townsperson's body is a rigged model fetched on demand out of a
/// shared cast. Sending the NAME and letting each machine fetch its own copy means the wire carries a few dozen
/// bytes per person instead of a model, and it reuses the build path the game already has (see
/// <see cref="Townsperson"/>) rather than inventing a second one.</para>
///
/// <para><b>The client's copy does not think.</b> Its wander brain is told to stop deciding and its mover is
/// switched off on arrival — those are the server's job now. A client that kept its own brain would have a
/// townsperson who disagreed with everyone else's about where they stood and what they said, which is exactly
/// the disagreement this whole layer exists to prevent.</para>
/// </summary>
public class NetNpc : NetworkBehaviour
{
    /// <summary>Every townsperson shell alive on this machine, found by the BODY it drew. A dictionary rather than
    /// a list because pressing F to talk asks it, and the shells of a whole town would be a walk.</summary>
    static readonly System.Collections.Generic.Dictionary<GameObject, NetNpc> byView =
        new System.Collections.Generic.Dictionary<GameObject, NetNpc>();

    /// <summary>The shell that speaks for <paramref name="viewObject"/>, or null if that object is not a
    /// townsperson this machine received from a server.</summary>
    public static NetNpc ShellForView(GameObject viewObject)
    {
        for (var t = viewObject != null ? viewObject.transform : null; t != null; t = t.parent)
            if (byView.TryGetValue(t.gameObject, out var shell) && shell != null) return shell;
        return null;
    }

    /// <summary>Every shell on the SERVER, found by the REAL person it speaks for — the mirror of
    /// <see cref="byView"/>, which answers the same question on a watching machine. The server has to name the
    /// person who just spoke in order to announce what they said.</summary>
    static readonly System.Collections.Generic.Dictionary<GameObject, NetNpc> byServerNpc =
        new System.Collections.Generic.Dictionary<GameObject, NetNpc>();

    /// <summary>SERVER ONLY: the shell that speaks for the real person <paramref name="npcObject"/> belongs to, or
    /// null when that object is not a townsperson this server published.</summary>
    public static NetNpc ShellForServerNpc(GameObject npcObject)
    {
        for (var t = npcObject != null ? npcObject.transform : null; t != null; t = t.parent)
            if (byServerNpc.TryGetValue(t.gameObject, out var shell) && shell != null) return shell;
        return null;
    }

    /// <summary>WHICH BODY this person wears, by the shared cast's name for it. Chosen once by the server before
    /// anybody is told this person exists.
    ///
    /// <para>On the wire because it is this person's IDENTITY, not a picture each machine may invent. Every
    /// machine used to draw its own at random out of the same pool, so the same townsperson stood in front of two
    /// people wearing two different faces.</para></summary>
    [SyncVar] public string modelName = "";

    /// <summary>WHAT THIS PERSON WORKS AT, as the server says — a smith, a loom, a tannery, or nothing. It has to
    /// cross, because the body a player presses F on is a copy this machine built, and a copy that did not know
    /// would open a conversation with a smith. Written when the shell is bound and whenever the role changes.</summary>
    [SyncVar(hook = nameof(OnStationChanged))] public int stationKind;

    void OnStationChanged(int _, int now)
    {
        var t = view != null ? view.GetComponent<TownNPC>() : null;
        if (t != null) t.station = (CraftStation)now;
    }

    /// <summary>WHICH MAP this person lives in, by scene name. The world is several maps at once and they share one
    /// set of coordinates, so somebody in a town and somebody in a field can be a metre apart by the numbers —
    /// without this a watcher would draw the other map's townsfolk standing on nothing in front of them.</summary>
    [SyncVar] string mapName = "";

    [SyncVar] Vector3 netPosition;
    [SyncVar] float netYaw;

    /// <summary>This person's level as the server holds it. Written once, when the shell is bound.</summary>
    [SyncVar] int netLevel;

    /// <summary>How fast this person is actually walking on the ground, in units per second — 0 while they stand.
    /// TOLD rather than left for each watcher to work out, because a watcher can only measure the catch-up glide
    /// toward the positions it received, not the walk.</summary>
    [SyncVar] float netSpeed;

    // ---- what this person just SAID ----------------------------------------------------------------------
    // HELD AS A VALUE rather than shouted once, the same shape a creature's action beat uses: the line, and the
    // moment on the clock both machines share that it was said at. A shout reaches whoever was already watching
    // and nobody else, and people walk into earshot constantly.

    [SyncVar] string netSaid = "";
    [SyncVar] double netSaidAt;

    /// <summary>How long a bubble stays up. Also how old a line may be and still be worth showing to somebody who
    /// has only just walked into range — past this it is something that already finished happening.</summary>
    const float BubbleSeconds = 2.6f;

    /// <summary>The real person. Only ever set on the SERVER — this is the one that actually thinks and walks;
    /// everything below just copies their state onto the wire.</summary>
    public GameObject ServerNpc { get; private set; }

    TownNPC serverBrain;
    PathAgent3D serverAgent;

    // The client's own copy of the body: seen, never simulated.
    GameObject view;
    // A body is on its way. NOT "one was asked for once" — a body can be destroyed under us, and a latch that only
    // ever went true meant such a person could never be drawn again for as long as they lived.
    bool viewLoading;

    // The last line this machine actually put in a bubble, by the moment it was said. The line is a VALUE re-read
    // every frame rather than an event that fires once, so without this it would pop again on every frame it held.
    double shownSaidAt = double.NegativeInfinity;

    /// <summary>THE BODY THIS MACHINE DRAWS FOR THIS PERSON — or null before it is built. The same question a
    /// creature's shell and a person's shell both answer, so anything that plays a picture on a unit asks one
    /// question rather than one per kind of unit.</summary>
    public Transform DrawnBody => view != null ? view.transform : null;

    // Ten times a second, the same cadence a player's position and a creature's use.
    const float PushInterval = 0.1f;

    float nextPush;

    // The synced facing, held as a rotation. Rebuilt only when the number actually changes — the follow below runs
    // every frame on every townsperson on screen, and building a rotation from an angle there would be that cost
    // times a whole town, forever, for a value that changes ten times a second at most.
    Quaternion facing = Quaternion.identity;
    float facingBuiltFrom = float.NaN;

    /// <summary>Server-only: hand this shell the person it speaks for.</summary>
    public void BindOnServer(GameObject npc, string wearing)
    {
        ServerNpc = npc;
        // FILED UNDER THE REAL PERSON, so a line they speak can find the shell that has to announce it. The
        // watching side files its own body the same way when it builds one (see AdoptView).
        if (npc != null) byServerNpc[npc] = this;
        modelName = wearing;
        mapName = npc != null ? npc.gameObject.scene.name : "";
        serverBrain = npc != null ? npc.GetComponent<TownNPC>() : null;
        stationKind = serverBrain != null ? (int)serverBrain.station : 0;
        netLevel = serverBrain != null ? serverBrain.Level : 1;
        serverAgent = npc != null ? npc.GetComponent<PathAgent3D>() : null;
        if (npc != null)
        {
            // WHERE THEY ARE INSIDE THEIR MAP, not where this machine happens to keep that map — see MapManager's
            // parking. A watcher adds back its own answer for where the map is, which for a player is nowhere.
            netPosition = MapManager.InMap(npc.transform.position, mapName);
            netYaw = npc.transform.eulerAngles.y;
            // AND THE SHELL ITSELF STANDS THERE TOO — see StandWhereTheNpcStands.
            transform.position = npc.transform.position;
            // AND IN THIS PERSON'S OWN MAP, not in whichever one the server happened to load last. The watcher
            // lists are built map first, so a shell filed under the wrong map is one nobody in the right map is
            // ever told about.
            MapManager.PutInMap(gameObject, npc.gameObject.scene);
        }
    }

    /// <summary>Put this shell where the person it speaks for actually is.
    ///
    /// <para>The position on the wire is a value this object CARRIES; the position of the OBJECT is what the server
    /// measures to decide who is told about it. Left at the world origin, "is this person near enough to hear
    /// about" would really be asking "is this player near the origin" — one answer for a whole town at once.</para></summary>
    void StandWhereTheNpcStands() => transform.position = ServerNpc.transform.position;

    void Update()
    {
        if (isServer) { PushFromTheRealNpc(); return; }
        if (isClient) FollowTheServer();
    }

    // ---- the server's half: publish what the real person is doing ----------------------------------------

    void PushFromTheRealNpc()
    {
        // GONE. A town's people are built once and live for as long as the map does, so this is the map itself
        // being let go — and the shell goes with it rather than pushing a frozen position forever.
        if (ServerNpc == null || !ServerNpc.activeInHierarchy)
        {
            NetworkServer.Destroy(gameObject);
            return;
        }

        if (Time.time < nextPush) return;
        nextPush = Time.time + PushInterval;

        netPosition = MapManager.InMap(ServerNpc.transform.position, mapName);
        netYaw = ServerNpc.transform.eulerAngles.y;
        StandWhereTheNpcStands();

        // WHAT GEAR THEY ARE IN, as a speed. Read off the mover's own live figure so a watcher picks the right
        // gait AND churns the legs at the right cadence. A SyncVar written the value it already holds sends nothing.
        netSpeed = serverAgent != null && serverAgent.Moving ? serverAgent.moveSpeed : 0f;
    }

    /// <summary>SERVER ONLY: say that this person just spoke, and what they said. Called at the moment it happens.
    /// The line is the SERVER's choice, so everybody standing there hears the same words at the same time —
    /// before this, every machine picked its own and no two townspeople agreed with themselves.</summary>
    public void Said(string line)
    {
        if (!isServer || string.IsNullOrEmpty(line)) return;
        netSaid = line;
        // Stamped LAST, and stamped HERE rather than by whoever asked: the moment is what every watcher measures
        // the line against, so it is the server's clock or it is worth nothing.
        netSaidAt = NetworkTime.time;
    }

    /// <summary>SERVER ONLY: this person is being talked to, or has been left alone. Stops them where they are and
    /// turns them to whoever is speaking, so every player watching sees them stop and turn — not just the one
    /// holding the conversation.</summary>
    public void ServerTalkTo(Transform speaker)
    {
        if (!isServer || serverBrain == null) return;
        if (speaker != null) serverBrain.BeginTalk(speaker);
        else serverBrain.EndTalk();
    }

    // ---- everybody else's half: show it ------------------------------------------------------------------

    // The copy's animation driver, cached at build time: this is read every frame on every townsperson on screen,
    // and a per-frame component lookup at that rate is exactly what PERFORMANCE.md bans.
    Agent3DVisual viewVisual;

    readonly LevelCapAura levelAura = new LevelCapAura();

    // The map this machine was standing in when we last decided whether to draw this person. An INTEGER, not the
    // name: this runs on every townsperson in the world every frame, and PERFORMANCE.md bans a string compare at
    // that rate. It only ever moves when somebody walks through a door.
    int seenMapVersion = -1;
    bool inMyMap;

    void FollowTheServer()
    {
        // AM I EVEN IN THE PLACE THIS PERSON IS? Re-decided when this machine changes map, and while this person
        // has not said which map they are in yet — never once per frame in the settled case. The second half is not
        // belt and braces: a map arrives as its own value on the wire and can land a frame or two after the person
        // does, and deciding once on an empty name would show somebody from another town for the rest of their life.
        bool mapKnown = !string.IsNullOrEmpty(mapName);
        if (seenMapVersion != MapManager.MapVersion || !mapKnown)
        {
            seenMapVersion = mapKnown ? MapManager.MapVersion : -1;
            inMyMap = mapKnown && mapName == MapManager.CurrentMap;
            // GONE THE MOMENT YOU LEAVE, not left standing in the map you walked into.
            if (!inMyMap && view != null) DropTheView();
        }

        if (!inMyMap) return;

        if (view == null) { EnsureView(); return; }

        // The SHARED way a watched body catches up — the same one a watched person and a watched creature use, so
        // the three can never drift into gliding differently.
        if (facingBuiltFrom != netYaw) { facing = Quaternion.Euler(0f, netYaw, 0f); facingBuiltFrom = netYaw; }
        WatchedBody.Follow(view.transform, MapManager.InWorld(netPosition, mapName), facing);

        // WHAT THE BODY IS DOING, handed over rather than guessed at. Its mover and its brain are switched off on
        // this machine, so the two things its animation used to read from both answer "nothing" forever.
        if (viewVisual != null) viewVisual.TellTravelSpeed(netSpeed);

        // THE SMOKE AT THE LEVEL CAP, from the level the server sent — the same one every kind of unit wears.
        levelAura.Show(netLevel, view.transform);

        ShowWhatTheySaid();
    }

    // The white bubble over their head, for anything the server said they said. Nothing is built or allocated
    // unless a NEW line arrived, and a line older than the bubble lasts is not shown at all — somebody who has just
    // walked into earshot is not owed a remark that finished before they got there.
    void ShowWhatTheySaid()
    {
        if (netSaidAt <= shownSaidAt || string.IsNullOrEmpty(netSaid)) return;
        shownSaidAt = netSaidAt;
        if (NetworkTime.time - netSaidAt > BubbleSeconds) return;
        var pose = viewVisual != null ? viewVisual.Pose : null;
        if (pose != null) SpeechBubble.ShowOverHead(pose, netSaid, BubbleSeconds);
    }

    void EnsureView()
    {
        if (viewLoading || view != null || string.IsNullOrEmpty(modelName)) return;
        viewLoading = true;

        string wanted = modelName;
        CharacterModels.Load(wanted, model =>
        {
            // NOBODY LEFT TO SPEAK FOR THEM. A shell can be taken away while its body is still being fetched —
            // an ordinary event, because somebody who walks out of range is dismissed and taken back again.
            if (this == null) return;
            viewLoading = false;
            if (model == null)
            {
                GameLog.Warn(LogCat.Net, "no character model called '" + wanted
                                 + "' — a townsperson will be invisible.");
                return;
            }
            AdoptView(Townsperson.Build(wanted, MapManager.InWorld(netPosition, mapName),
                                        MapManager.CurrentScene, model));
        });
    }

    // Everything true of a body this shell now speaks for. ONE place, so nothing can quietly skip a step.
    void AdoptView(GameObject go)
    {
        if (go == null) return;

        // Placed AND turned before anybody sees it: a body that appeared facing due north and then swung round to
        // its real heading is the same wrong-first-frame the watched players used to have.
        go.transform.rotation = Quaternion.Euler(0f, netYaw, 0f);

        // THIS COPY DOES NOT THINK. What remains is a body that walks where the server says it walks — but it does
        // know what the person it copies works at, or pressing F on a smith would open a conversation.
        var brain = go.GetComponent<TownNPC>();
        if (brain != null)
        {
            brain.BecomeAWatchedCopy();
            brain.station = (CraftStation)stationKind;
        }

        view = go;
        // Cached once; never looked up per frame.
        viewVisual = go.GetComponent<Agent3DVisual>();
        // So pressing F next to this body finds the person the server owns.
        byView[go] = this;

        // The body belongs to the WORLD, not to the map this machine happens to be standing in when it arrives.
        // Travelling unloads the map you left, and everything built into it goes with it.
        MapManager.KeepAcrossMaps(go);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Out of the map this machine is standing in, before a portal can catch it. Mirror drops an arriving object
        // into whichever map is current, and travelling unloads that map.
        MapManager.KeepAcrossMaps(gameObject);
    }

    /// <summary>Stop drawing this person. Two reasons to — the shell going away, and this machine walking into
    /// another map — so one description of it.
    ///
    /// <para>The body is DESTROYED rather than banked, the same treatment a watched player's body gets. A town's
    /// people are built once and stay for as long as you are in their town, so there is no churn for a pool to
    /// absorb — unlike a field of creatures dying and respawning every few seconds.</para></summary>
    void DropTheView()
    {
        if (view == null) return;
        levelAura.Stop();
        byView.Remove(view);
        Destroy(view);
        view = null;
        viewVisual = null;
        viewLoading = false;
        shownSaidAt = double.NegativeInfinity;
    }

    public override void OnStopClient()
    {
        DropTheView();
        base.OnStopClient();
    }

    /// <summary>The shell is going away, so the person it spoke for goes out of the table with it — otherwise a dead
    /// key sits there forever, exactly as it would on the watching side.</summary>
    public override void OnStopServer()
    {
        if (ServerNpc != null && byServerNpc.TryGetValue(ServerNpc, out var held) && held == this)
            byServerNpc.Remove(ServerNpc);
        base.OnStopServer();
    }
}
