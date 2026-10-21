using System;
using Mirror;
using UnityEngine;

/// <summary>
/// The ONE place the game joins the wire. A server starts listening here; a client dials the server here.
/// Both sides are the same component asking <see cref="GameServer.IsServer"/> which job it has, so the
/// address, the port and the transport are configured once and cannot drift between the two halves.
///
/// <para><b>Self-bootstrapping</b>, like <c>SaveManager</c> and <c>SignInScreen</c>: no scene wiring, so a
/// scene can never be saved with a half-configured net object baked into it.</para>
///
/// <para><b>Where it is pointed comes from the environment</b>, never from a rebuild — the same rule
/// <see cref="AccountSession.ServerUrl"/> already follows. A build made once can be aimed at localhost, at a
/// machine on the LAN, or at a hosted server, by setting <c>GAME_SERVER_HOST</c> / <c>GAME_SERVER_PORT</c>.</para>
///
/// <para><b>No bodies yet.</b> <see cref="NetworkManager.autoCreatePlayer"/> is off: this layer only
/// establishes that a connection exists. Spawning a body for each connection is the next step and belongs
/// with the shared character rig, not here.</para>
/// </summary>
public class GameNetwork : NetworkManager
{
    /// <summary>Where a CLIENT dials. Ignored on a server, which listens on all its addresses.</summary>
    public const string HostEnvVar = "GAME_SERVER_HOST";

    /// <summary>The port both sides use. One value, read the same way on both, so they cannot disagree.</summary>
    public const string PortEnvVar = "GAME_SERVER_PORT";

    /// <summary>How far away a player is still told about a thing, in world units. See where it is used for why 60.</summary>
    const int VisibleRange = 60;

    /// <summary>The body spawned for each connected player, found in Resources by this name.</summary>
    public const string PlayerPrefabName = "NetPlayer";

    /// <summary>The shell spawned for each mob the server owns, found in Resources by this name.</summary>
    public const string MobPrefabName = "NetMob";

    /// <summary>The shell spawned for each townsperson the server owns, found in Resources by this name.</summary>
    public const string NpcPrefabName = "NetNpc";

    /// <summary>The shell spawned for each thing lying on the ground, found in Resources by this name.</summary>
    public const string DropPrefabName = "NetGroundDrop";

    // DELIBERATELY ABSENT: a public "am I joined right now" answer. It had no readers, and the one use anybody
    // would reach for it for — gating behaviour — is the timing accident this layer already outlawed: dialling
    // takes a moment, and a question asked inside that moment gets the wrong answer and acts on it. The question
    // to ask is what job this copy was GIVEN (GameServer.IsServer), which is settled at start-up.

    /// <summary>Raised the moment this copy is joined and the world may exist. <see cref="GameRoot"/> waits for it
    /// before bringing a map up: signing in is no longer enough to be somewhere.</summary>
    public static event Action OnJoinedWorld;

    /// <summary>Raised when the wire is gone for good — every retry spent, a refusal, or this character signed in
    /// on another device. Nobody may be in the world after this, so <see cref="GameRoot"/> tears it down and puts the
    /// sign-in screen back, showing the reason it carries — the whole of it, in words a player can act on.</summary>
    public static event Action<string> OnLostWorld;

    /// <summary>What a player is told when every attempt to reach or stay in the world has failed.</summary>
    const string CouldNotReachWorld = "Could not reach the game world after 3 tries. The game server may be down or "
                                      + "restarting — try again in a moment.";

    /// <summary>What a player is told when their character was signed in on another device.</summary>
    const string SignedInElsewhereText = "Your character was signed in on another device, so this one was disconnected.";

    // SET BY THE SERVER'S WORD that this character was signed in on another device, just before it hangs up. The
    // hang-up that follows is then the end, with this as its reason: no retry, which would only take the character
    // back from the other device.
    string kickedBecause;

    // How hard we try before giving up on a world. Small enough that nobody is left staring at a veil, long enough
    // to ride out the brief drop that a wireless connection produces several times an hour.
    const int JoinAttempts = 3;
    const float SecondsBetweenAttempts = 2f;

    int attemptsSpent;
    bool goneForGood;   // stop retrying once we have already bounced; a late failure must not restart the loop

    // HANGING UP ON PURPOSE, to dial somewhere else. Walking through a door to a zone another process holds means
    // letting go of this world deliberately — and Mirror reports that hang-up through the very same door as a
    // connection that DROPPED, so without this the ordinary "you lost the world" answer runs on a move that is
    // going perfectly well. That answer asks the character server to let this player in again two seconds later,
    // and asking evicts them from wherever they now are — which drops them, which asks again, forever.
    bool hangingUpToMove;

    // The retry already waiting to fire, so a deliberate dial can call it off. Left running, it re-enters the
    // world on top of a move that has already succeeded.
    Coroutine pendingRetry;

    static GameObject mobPrefab;
    static GameObject npcPrefab;
    static GameObject dropPrefab;

    /// <summary>Server-only: announce a mob that has just come into existence, so every connected player
    /// receives it. Called from wherever a creature is born; does nothing off a server, so the offline game
    /// is untouched.</summary>
    public static void PublishMob(GameObject mob, string defId)
    {
        if (mob == null || !NetworkServer.active) return;
        // Every creature the world gains is announced here, so this is where it gets its spawn_id.
        SpawnIds.MobAppeared(mob.GetComponent<Health>(), defId);   // perf-ok: once per creature announced

        if (mobPrefab == null) mobPrefab = Resources.Load<GameObject>(MobPrefabName);
        if (mobPrefab == null)
        {
            GameLog.Error(LogCat.Net, "no '" + MobPrefabName + "' prefab in Resources — mobs will exist on the "
                           + "server but no player will see them.");
            return;
        }

        var shell = Instantiate(mobPrefab);
        shell.GetComponent<NetMob>()?.BindOnServer(mob, defId);
        NetworkServer.Spawn(shell);
    }

    /// <summary>Server-only: announce a townsperson who has just come into existence, so every player near enough
    /// receives them. Called from wherever a town is populated; does nothing off a server.</summary>
    public static void PublishNpc(GameObject npc, string modelName)
    {
        if (npc == null || !NetworkServer.active) return;
        SpawnIds.NpcAppeared(npc, modelName);

        if (npcPrefab == null) npcPrefab = Resources.Load<GameObject>(NpcPrefabName);
        if (npcPrefab == null)
        {
            GameLog.Error(LogCat.Net, "no '" + NpcPrefabName + "' prefab in Resources — townsfolk will exist on the "
                           + "server but no player will see them.");
            return;
        }

        var shell = Instantiate(npcPrefab);
        shell.GetComponent<NetNpc>()?.BindOnServer(npc, modelName);
        NetworkServer.Spawn(shell);
    }

    /// <summary>Server-only: announce a thing somebody has just put on the ground, so every player near enough
    /// can read it. Does nothing off a server, where the drop this machine made IS the only one there is.</summary>
    public static void PublishDrop(GroundDrop drop)
    {
        if (drop == null || !NetworkServer.active) return;

        if (dropPrefab == null) dropPrefab = Resources.Load<GameObject>(DropPrefabName);
        if (dropPrefab == null)
        {
            GameLog.Error(LogCat.Net, "no '" + DropPrefabName + "' prefab in Resources — dropped items will exist "
                           + "on the server but nobody will see them lying there.");
            return;
        }

        var shell = Instantiate(dropPrefab);
        shell.GetComponent<NetGroundDrop>()?.BindOnServer(drop);
        NetworkServer.Spawn(shell);
        // NAMED ONLY NOW. Its name on the wire is handed out by the spawn above, and picking a thing up is a
        // request that names it — so the drop could not carry its own name until this moment.
        var identity = shell.GetComponent<NetworkIdentity>();
        if (identity != null) drop.netId = identity.netId;
    }

    /// <summary>Server-only: say that a townsperson just spoke, and what they said, so the same words appear over
    /// their head on every screen that can see them at the same moment. Silent off a server.</summary>
    public static void AnnounceNpcSaid(GameObject serverNpc, string line)
    {
        if (!NetworkServer.active || serverNpc == null) return;
        NetNpc.ShellForServerNpc(serverNpc)?.Said(line);
    }

    /// <summary>Server-only: a player has started, or finished, talking to a townsperson. The townsperson stops
    /// where they are and turns to face whoever is speaking — decided here so that everybody watching sees it, not
    /// only the person holding the conversation.</summary>
    public static void SetNpcTalkedTo(uint npcNetId, Transform speaker)
    {
        if (!NetworkServer.active) return;
        if (!NetworkServer.spawned.TryGetValue(npcNetId, out var identity) || identity == null) return;
        identity.GetComponent<NetNpc>()?.ServerTalkTo(speaker);
    }

    /// <summary>Ask the server to accept <paramref name="questId"/> from the townsperson whose body is
    /// <paramref name="npcBody"/>. The server decides; this only asks.</summary>
    public static void TellServerAcceptQuest(GameObject npcBody, string questId)
    {
        var shell = NetNpc.ShellForView(npcBody);
        if (shell == null) return;
        MyPlayer?.AskToAcceptQuest(questId, shell.netId);
    }

    /// <summary>Ask the server to start, or stop, a conversation with the townsperson whose body is
    /// <paramref name="npcBody"/>. Called on the machine of the person pressing F; silent with no world, where the
    /// townsperson is this machine's own and stops on its own.</summary>
    public static void TellServerTalkingTo(GameObject npcBody, bool talking)
    {
        var shell = NetNpc.ShellForView(npcBody);
        if (shell == null) return;
        var me = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<NetPlayer>() : null;
        if (me != null) me.AskToTalkTo(shell.netId, talking);
    }

    static GameNetwork spawned;

    /// <summary>Bring the net layer up. Called once, by <see cref="GameRoot"/>, at the moment the world starts
    /// existing — which on a server is boot, and on a client is entering the world after sign-in. One caller
    /// means the two sides can never be started by different rules.</summary>
    public static void Begin()
    {
        if (spawned != null) return;

        var go = new GameObject("GameNetwork") { hideFlags = HideFlags.DontSaveInEditor };

        // ORDER MATTERS, and getting it wrong fails quietly. Adding the manager runs its own start-up THAT
        // INSTANT, and the first thing it looks for is a transport; finding none it logs one line and gives
        // up half-built — singleton claimed, everything after it skipped. So the transport goes on FIRST and
        // the manager finds it already sitting there.
        var kcp = go.AddComponent<kcp2k.KcpTransport>();
        kcp.port = PortFromEnvironment();

        spawned = go.AddComponent<GameNetwork>();
        spawned.transport = kcp;
        Transport.active = kcp;

        // A client is told where to dial by the character server; this is only where it starts.
        spawned.networkAddress = ServerAddress.Setting(HostEnvVar) ?? "";

        // The body every connected player is seen as. Loaded by NAME from Resources so both halves resolve
        // the same asset with no scene wiring — the same way the skill and monster catalogs already load.
        spawned.playerPrefab = Resources.Load<GameObject>(PlayerPrefabName);
        if (spawned.playerPrefab == null)
            GameLog.Error(LogCat.Net, "no '" + PlayerPrefabName + "' prefab in Resources — players will connect "
                           + "but nobody will have a body.");

        // Anything the server can spawn must be known to the clients too, or an arriving mob is an object
        // nobody can build. The player body is registered automatically; everything else is listed here.
        mobPrefab = Resources.Load<GameObject>(MobPrefabName);
        if (mobPrefab != null) spawned.spawnPrefabs.Add(mobPrefab);
        else GameLog.Error(LogCat.Net, "no '" + MobPrefabName + "' prefab in Resources — no player will see mobs.");

        npcPrefab = Resources.Load<GameObject>(NpcPrefabName);
        if (npcPrefab != null) spawned.spawnPrefabs.Add(npcPrefab);
        else GameLog.Error(LogCat.Net, "no '" + NpcPrefabName + "' prefab in Resources — no player will see townsfolk.");

        dropPrefab = Resources.Load<GameObject>(DropPrefabName);
        if (dropPrefab != null) spawned.spawnPrefabs.Add(dropPrefab);
        else GameLog.Error(LogCat.Net, "no '" + DropPrefabName + "' prefab in Resources — no player will see what "
                           + "anybody drops.");

        // The server hands out bodies itself, when a client says who it is (see OnJoinRequest). Mirror's
        // automatic version would spawn one before we know which character to build.
        spawned.autoCreatePlayer = false;

        if (GameServer.IsServer)
        {
            // HOW OFTEN THE WIRE IS PUMPED, matched to how often this server runs a frame. The pump happens once
            // per frame, so anything above the frame rate is a promise the loop cannot keep — the extra passes
            // could only ever walk the connection list and find nothing new to say.
            //
            // SERVER ONLY, and the branch is the point. Mirror reads this one number for both directions, so
            // setting it outside here slowed down every PLAYER's outgoing messages too — including the position
            // reports the movement check measures, which would have added a fraction of a second to every one of
            // them for no reason at all.
            //
            // It is also NOT what decides what a moving thing costs: a creature and a person each write their
            // position ten times a second, and Mirror sends a value only when it actually changed, so the traffic
            // was already paced by the writers. This only stops the empty passes between them.
            spawned.sendRate = GameSystems.ServerFps;

            // ONLY TELL A PLAYER ABOUT WHAT IS NEAR THEM. Without this the server sends every creature's position to
            // every person ten times a second regardless of where they are, so a busy world costs each player the
            // whole world's traffic — the cost grows with the square of how much is in it. Mirror does the work;
            // this only says how far "near" is.
            //
            // The distance is comfortably beyond anything the game itself already treats as mattering: creatures
            // stop thinking at 28 units from a player, so at 60 a thing is always known about long before it does
            // anything, and nothing appears out of thin air in front of anybody.
            // THE MAP IS ASKED FIRST, THEN THE DISTANCE. A plain distance check compares every thing in the
            // world against every person in it, and a server holds every map at once — so it was measuring the
            // gap between a person in the town and a creature in a field, forever, to reach an answer the
            // parking gap had already decided. Asking which map each is in settles those in one comparison and
            // only then does the arithmetic, for the pairs where it can change the answer.
            //
            // It is also the shape the world has to be in before maps can ever live in separate processes: a
            // watcher list built per map is one that does not silently depend on the other maps being here.
            var interest = go.AddComponent<SceneDistanceInterestManagement>();
            interest.visRange = VisibleRange;

            if (kcp.port == 0)
            {
                GameLog.Error(LogCat.Server, Shout(PortEnvVar + " is not set in settings.env, so this server has no "
                                     + "port for players to connect on. It is quitting."));
                Stop();
                return;
            }

            // IS ANYBODY ALREADY THERE. Asked BEFORE listening, because the failure it prevents is silent: the
            // wire layer throws when the port is taken, so the "listening" line below never runs and this copy
            // goes on standing in the process list having bound nothing at all. It looks alive, it answers
            // nobody, and every player keeps talking to the older copy that really does hold the port — which
            // is indistinguishable, from a player's seat, from the new code simply not working.
            // THE CHARACTER SERVER IS TOLD WHAT THIS MACHINE HOLDS, and this machine starts listening for the
            // people it sends. Without it nobody can enter the world at all: a player is handed an address by
            // the character server, and it has no address to hand out until this has said so.
            MapServerLink.Begin();

            if (!PortIsFree(kcp.port, kcp.DualMode))
            {
                GameLog.Error(LogCat.Server, Shout("port " + kcp.port + " is already taken — another server is running. "
                                     + "This one is quitting rather than sitting here listening to nothing. Stop the "
                                     + "other one first, or give this one a different GAME_SERVER_PORT."));
                Stop();
                return;
            }

            spawned.StartServer();
            GameLog.ApplyStartSetting();
            ReachableAddresses.ReportAndWatch(kcp.port, MapServerLink.PlayerAddress);
            // Said here, on the server, because the server's log is the one a play-test actually reads.
            GameLog.AnnounceToTesters();
        }
        else
        {
            // Said BEFORE dialling, on purpose. Everything that must not build its own copy of the world asks this,
            // and some of them ask while the handshake is still in flight — so the answer has to be true from the
            // moment the decision was made, not from the moment the wire came up.
            // ASK THE CHARACTER SERVER WHERE TO GO. A player no longer dials an address somebody typed into a
            // build — they say which character they are entering as, and are told which machine is holding the
            // map they were last in, along with a one-time ticket to knock with. Their whole character has
            // already been sent there by the time the answer comes back.
            AskWhereToEnter(kcp);
        }
    }

    /// <summary>Ask the character server to let this person into the world, then dial wherever it says.
    ///
    /// <para>Falls back to the address in the environment when there is no character server to ask — which is
    /// how a single machine is run for testing, with no character server at all.</para>
    ///
    /// <para><b>Failing to get in never ends the process.</b> All three failures below hand the player back to
    /// the sign-in screen with a line saying the world was lost — the same answer as running out of retries, and
    /// the only one they can act on. Closing the application is out of all proportion to one request that did
    /// not come back, and it is the single thing done to a player that they cannot undo.</para></summary>
    static void AskWhereToEnter(kcp2k.KcpTransport kcp)
    {
        string characterId = AccountSession.CharacterId;
        if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(AccountSession.CharacterServerUrl))
        {
            // NOBODY TO ASK MEANS NOBODY CAN ENTER, and dialling anyway is worse than saying so. A map server
            // refuses every connection it was not told to expect — there is no branch that lets a ticketless one
            // through — so this used to dial, be refused, and retry forever under a veil, with the only clue a
            // single line in the log. MEASURED on a real run: a player who launched the game in a way that did
            // not carry CHAR_SERVER_URL printed "retrying 1 of 3" more than forty times and never got in.
            //
            // Every way of arriving here is the same way: something started the game WITHOUT a terminal, so none
            // of the settings a shell would have carried reached it — `open` on the built game, a player
            // double-clicking one, or the EDITOR started from the Dock. A build carries the address in its own
            // settings file and the editor reads settings.env, so reaching this now means that file is empty too,
            // not merely that nobody typed anything.
            GameLog.Error(LogCat.Client, "this copy has no character server to ask, so nobody can let it into the "
                           + "world — CHAR_SERVER_URL is not set"
                           + (string.IsNullOrEmpty(characterId) ? ", and no character has been chosen" : "")
                           + ". Set CHAR_SERVER_URL in settings.env at the top of the project (the editor reads it "
                           + "there, and tools/build-game.sh writes it into every build), or in the terminal you "
                           + "start from. See docs/2-systems/networking/starting-the-network.md.");
            OnLostWorld?.Invoke("This copy of the game does not know where the character server is (CHAR_SERVER_URL "
                                + "is not set), so it cannot enter the world.");
            return;
        }

        AccountClient.Send("POST", "/characters/" + characterId + "/enter", "{}", true, (ok, body, error) =>
        {
            if (spawned == null) return;

            if (!ok)
            {
                GameLog.Error(LogCat.Client, "the character server would not let us in — " + error + " " + body);
                OnLostWorld?.Invoke(string.IsNullOrEmpty(error) ? "The character server would not let you into the world."
                                                                : char.ToUpperInvariant(error[0]) + error.Substring(1));
                return;
            }

            var where = JsonUtility.FromJson<EntryReply>(body);
            if (where == null || string.IsNullOrEmpty(where.ticket))
            {
                GameLog.Error(LogCat.Client, "the character server let us in but said nowhere to go: " + body);
                OnLostWorld?.Invoke("The character server let you in but did not say which world to join.");
                return;
            }

            AccountSession.EntryTicket = where.ticket;

            // AND WHICH MAP THEY ARE BEING SENT TO. The character server has just read this person's save, decided
            // which map they logged out in, and brought that map up on the process below — so it is the ONE thing
            // that already knows, and the boot gate loads what it says instead of the authored starting map.
            // Written unconditionally, empty included, so a second sign-in in the same session can never boot the
            // first one's map.
            AccountSession.EntryMap = where.map;
            if (!string.IsNullOrEmpty(where.host)) spawned.networkAddress = where.host;
            // AND THE PORT IT NAMED. A build pointed at one machine by its environment must still go wherever
            // the character server sends it, or splitting the world across machines would need a new build for
            // every player.
            if (where.port > 0 && where.port <= ushort.MaxValue) kcp.port = (ushort)where.port;

            GameLog.Info(LogCat.Client, "dialling " + spawned.networkAddress + ":" + kcp.port + " with a ticket");
            spawned.StartClient();
        });
    }

    [Serializable]
    class EntryReply
    {
        public string host;
        public int port;
        public string ticket;
        public string map;
    }

    /// <summary>The port PLAYERS connect on. Said here because it is decided here, and the character server has
    /// to be able to tell somebody where to knock.</summary>
    public static ushort PlayerPort => PortFromEnvironment();

    /// <summary>GAME_SERVER_PORT from settings.env, or 0 when it is not set — no port is written here to fall back
    /// on. A server refuses to start without it; a client is told its port by the character server.</summary>
    static ushort PortFromEnvironment()
    {
        string raw = ServerAddress.Setting(PortEnvVar);
        return ushort.TryParse(raw, out ushort p) ? p : (ushort)0;
    }

    /// <summary>Can a server actually take this port? Answered by BINDING it exactly the way the wire layer is
    /// about to — same address family, same dual-stack setting — and letting it go again. A cheaper test that
    /// merely looked for a listener would answer a different question and could still be wrong.</summary>
    static bool PortIsFree(ushort port, bool dualMode)
    {
        System.Net.Sockets.Socket probe = null;
        try
        {
            if (dualMode)
            {
                probe = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetworkV6,
                                                      System.Net.Sockets.SocketType.Dgram,
                                                      System.Net.Sockets.ProtocolType.Udp) { DualMode = true };
                probe.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.IPv6Any, port));
            }
            else
            {
                probe = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,
                                                      System.Net.Sockets.SocketType.Dgram,
                                                      System.Net.Sockets.ProtocolType.Udp);
                probe.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, port));
            }
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;   // taken — the one answer this is here to give
        }
        finally
        {
            probe?.Close();   // released immediately, so the real listener can take it a moment later
        }
    }

    /// <summary>Paint a line red so it can be FOUND. A server writes its log straight to a terminal, where the
    /// only thing that stands out is colour — the editor's own markup means nothing there, and an error sits in
    /// the stream looking exactly like the hundred ordinary lines around it. Same escape codes the project's own
    /// shell scripts already use for the same purpose, so there is one idea of what "red" means here.
    ///
    /// <para>Reserved for a line the reader MUST not miss. Colouring anything else would spend the one thing that
    /// makes this work — that almost nothing else is red.</para></summary>
    internal static string Shout(string line) => "\u001b[31m" + line + "\u001b[0m";

    /// <summary>Give up and go, reporting a failure. Ending the process is the same act however it was asked
    /// for, so it is written once in <see cref="GameExit"/> and this only says WHY it is ending.</summary>
    static void Stop() => GameExit.Quit(1);

    /// <summary>What a client says on arrival: who it is playing. The server cannot know this by itself — the
    /// account service told the CLIENT which character it chose — so the client states it and the server spawns
    /// a body to match. Deliberately just identity: nothing here is trusted for anything that affects gameplay.</summary>
    /// <summary>What time it is, said by the machine running the world, at the moment it hands somebody a body.
    ///
    /// <para>The world's sun and every timer that has to outlive something are anchored to this one clock, so a
    /// player's machine must be told it rather than reading its own. It is sent once per world joined: a moment
    /// is all that is needed, because the receiver counts forward from it against its own steady timer.</para></summary>
    public struct WorldTime : NetworkMessage
    {
        public double epochSeconds;
    }

    /// <summary>How far a GM's <c>/time</c> has moved the time of day on this server — sent to everybody when it
    /// moves, and to each person as they arrive. See <see cref="TimeOfDay.ShiftSeconds"/>.</summary>
    public struct TimeOfDayShift : NetworkMessage
    {
        public double seconds;
    }

    /// <summary>The weather a GM's <c>/weather</c> is holding on this server — sent to everybody when it changes,
    /// and to each person as they arrive. See <see cref="WeatherDirector.ForcedByServer"/>.</summary>
    public struct WeatherHeld : NetworkMessage
    {
        /// <summary>The preset, or -1 when this server holds none and the calendar decides.</summary>
        public int preset;
        /// <summary>When the server began holding it (world-clock epoch seconds), what it held before (-1 = the
        /// calendar's weather) and since when — so standing water is the same for everyone, whenever they arrived.</summary>
        public double since;
        public int before;
        public double beforeSince;
    }

    public struct JoinRequest : NetworkMessage
    {
        public string characterName;

        /// <summary>WHICH character, and the PROOF that it is theirs. The server reads their real stats from the
        /// account service with this rather than being told them — a client that states its own numbers is a client
        /// that can state any numbers. The proof is the one the account service issued at sign-in, so the service
        /// only ever answers for characters on that account: claiming somebody else's name gets nothing.</summary>
        public string characterId;

        /// <summary>THE RECEIPT, not the character. The character server has already handed this person's whole
        /// character to this map server and given them this one-time secret to knock with — so nothing about who
        /// they are travels on this message, and a connection that cannot produce one was never announced.</summary>
        public string ticket;
    }

    /// <summary>A player saying "I am hitting that", and NOTHING else. One field, because every other thing a
    /// blow has is something the server already knows and a client could lie about.
    ///
    /// <para><b>Why it carries no element and no physical/magic flag.</b> It used to, and the server used them
    /// verbatim. Element is a straight multiplier against a creature's armour and the two defences are separate
    /// numbers, so those two fields were a damage dial with the client's hand on it: pick the armour's weakness,
    /// pick whichever defence is lower, every swing. Both are properties of the ATTACKER's weapon, which the
    /// server is already holding — so it reads them off the fighter instead of being told.</para></summary>
    public struct HitRequest : NetworkMessage
    {
        public uint targetNetId;
    }

    /// <summary>"I am attacking this target and am not in reach yet" — which target, nothing else. Sent by a person's
    /// own machine at most once a swing interval, and only while a skill that answers the start of a basic attack is
    /// ready (Crossed Branches' lunge). The server judges everything else (<see cref="OnAttackIntentRequest"/>).</summary>
    public struct AttackIntentRequest : NetworkMessage
    {
        public uint targetNetId;
    }

    /// <summary>What the server DREW for one blow on a creature, sent to everybody watching it — the number, its
    /// colour and opacity, whether it staggers — and NOT the creature's remaining health, which travels the usual way.
    ///
    /// <para><b>Why it carries the finished blow.</b> A player's machine works nothing out. Handed the blow as it went
    /// in, it would take armour and the element chart off itself using its OWN copy of the creature — which is not the
    /// creature the server hit, so it could draw a number the health bar never lost.</para></summary>
    public struct HitResult : NetworkMessage
    {
        public uint targetNetId;

        /// <summary>WHO swung it, as the shared name every machine agrees on. It keys the rising yellow total, so two
        /// people flurrying one creature keep two numbers. It teaches nobody anything: whatever a hit gives its
        /// attacker — a run of hits, a passive, lifesteal — was already given, on the server.</summary>
        public uint attackerNetId;

        /// <summary>THE BLOW EXACTLY AS THE SERVER DREW IT, every number final — see <see cref="ShownBlow"/>. The
        /// watching machine copies it onto the screen and works nothing out: not armour, not the element chart, not
        /// a parry, not a barrier.</summary>
        public int number;
        public float elementModifier;
        public bool crit;
        public bool magic;
        public bool missed;
        public bool flash;

        /// <summary>The RISING YELLOW TOTAL this strike brings its striker's run over this creature to, worked out on the
        /// server (<see cref="StrikeTotals"/>) — 0 when the strike belongs to no run that shows one. The watching machine
        /// puts this number up as it is and adds nothing: whether a total is owed is the authored skill's answer, and
        /// the sum is the server's.</summary>
        public int total;

        /// <summary>Damage that bypassed defence — a poison tick, a reflected blow — and what it was, so the number is
        /// coloured by its cause (<see cref="TrueDamageCauses.Colour"/>). <see cref="TrueDamageCause.None"/> for a blow.</summary>
        public TrueDamageCause tickCause;
    }

    /// <summary>What the server decided a blow ON A PERSON was. The mirror of <see cref="HitResult"/>, and it has to
    /// be its own message because a person and a creature are two different things on the wire: a creature is one
    /// shell everybody builds a copy of, while a person is somebody's real character on one machine and a body being
    /// watched on every other. Both halves are covered — see <see cref="NetPlayer.ShowBlow"/>.</summary>
    public struct PlayerHitResult : NetworkMessage
    {
        public uint victimNetId;

        /// <summary>WHO struck it, as the shared name every machine agrees on — a creature or another person alike.
        /// It keys the rising yellow total, so two creatures flurrying one person keep two climbing numbers instead
        /// of pooling into a third that belongs to neither. 0 when the striker is not a shared body. It teaches
        /// nobody anything: whatever the blow gave its attacker was already given, on the server.</summary>
        public uint attackerNetId;

        /// <summary>The RISING YELLOW TOTAL this blow brings its striker's run over this person to, worked out on the
        /// server (<see cref="StrikeTotals"/>) — 0 when the blow belongs to no run that shows one. The exact twin of
        /// the field on a creature's blow, so the same flurry draws the same total whichever side of it you stand on.</summary>
        public int total;

        /// <summary>The blow passed through them while they were dodging (a dash's moment of being untouchable), so their
        /// screen says "Dodge" rather than nothing.</summary>
        public bool dodged;
        public int amount;
        public Element element;
        public bool isMagic;
        public bool isCrit;
        public bool missed;
        public int attackerHit;

        /// <summary>WHAT THE BLOW RAN INTO on the way to their health — how much a barrier ate, how much a brace held
        /// back, whether a parry or a block erased it outright.
        ///
        /// <para><b>Why the number alone was not enough.</b> <see cref="amount"/> is what SURVIVED, and every one of
        /// these can take a blow down to nothing — so a fully braced, soaked, parried or blocked hit arrived as a
        /// plain zero, which draws nothing and looks exactly like being missed. A brace held a blow and the person
        /// wearing it saw no sign that anything had happened at all. This is the same kind of fact as
        /// <see cref="missed"/>, which has always travelled for exactly this reason.</para></summary>
        public UnitMitigation.Outcome mitigated;

        /// <summary>A damage-over-time tick — poison, bleed, burn, a brace's bill — rather than a hit. A tick staggers
        /// nobody, and one arriving after the person is dead is not shown.</summary>
        public bool tick;

        /// <summary>What this tick IS — a poison, a brace's bill, damage bounced back — so every screen colours it from its
        /// own table (<see cref="TrueDamageCauses.Colour"/>). Read only when <see cref="tick"/> is set: without it every
        /// tick on a person was drawn in plain hit red (play-test, 2026-09-14).</summary>
        public TrueDamageCause tickCause;
    }

    /// <summary>EVERY BLOW ONE SKILL TICK LANDED, in one message (<see cref="BlowBatch"/>). Each hit is exactly the
    /// message it would have travelled as on its own, plus WHEN to draw it (in pieces of the multi-hit cascade, 0 =
    /// now) and what the combat log writes for it (−1 = nothing: a later piece of a blow already written). The
    /// watching machine draws them as told and adds nothing up.</summary>
    public struct HitBatch : NetworkMessage
    {
        public HitResult[] mobHits;
        public byte[] mobDelays;
        public int[] mobLog;
        public PlayerHitResult[] playerHits;
        public byte[] playerDelays;
        public int[] playerLog;
    }

    /// <summary>What the server HEALED a unit for, so the green digits appear over it. Health is the server's, and so is
    /// every gain — a potion, a heal skill, regeneration, lifesteal — so this is the only road the digits have to a
    /// screen. A person's gains are told to that person alone, the audience they have always had; a creature's reach
    /// everybody watching it.</summary>
    public struct HPAddedShown : NetworkMessage
    {
        public uint netId;
        public int amount;
    }

    /// <summary>A cooldown the server just started on one of this person's skills, and the moment on the world's clock
    /// it ends. A player's machine starts no cooldown of its own; this is how its hotbar learns that one is running.</summary>
    public struct CooldownStarted : NetworkMessage
    {
        public string skillId;
        public double endsAtEpochSeconds;
    }

    /// <summary>The server has finished with a press of this skill — it cast it or refused it. Sent for every press,
    /// after any <see cref="CooldownStarted"/> it caused, so the pressing machine knows when it may send the next.</summary>
    public struct CastAnswered : NetworkMessage
    {
        public string skillId;
        // REFUSED: the skill did not run there, so the picture this machine started of it must stop — a run that holds
        // its caster still and lasts while a pile only the server spends would otherwise never end.
        public bool refused;
    }

    /// <summary>What a kill was worth, told to the person who earned it. Progress is written by a player's own
    /// machine, so the server announcing the reward is the only way it can reach the character it belongs to.</summary>
    public struct XpEarned : NetworkMessage
    {
        public int amount;

        // THE FINISHED ANSWER, as the server's copy of the character now stands. The owner's machine copies these
        // onto the screen; it used to run the level-up loop itself from the two amounts above, which is the
        // player's machine deciding a level, a stat point and a full heal. The two "gained" counts say how many
        // times to play the level-up moment — a count to replay, never a number to work out.
        public int level, statPoints, levelsGained;
        public double xp;   // to two decimals, like every track's experience
        public long lifetimeXp;
        public int ringLevel, ringBankedPoints, ringLevelsGained;
        public double ringXp;
    }

    /// <summary>A KILL'S PLACE IN ITS KILLER'S RUN, told to the killer alone: the run's count, the moment it ends, what
    /// the kill paid and how much of that the run added, and the experience and zeny per hour as the server now works
    /// them out (<see cref="KillComboMath"/>). Sent for every kill that counts, and again when a turned-back character
    /// pays its rescue zeny. Every number is final; the player's machine copies it onto the screen and into the
    /// combat log, and only compares the end moment with the clock to stop drawing the count.</summary>
    public struct KillComboShown : NetworkMessage
    {
        /// <summary>The creature that died, for the combat log's name — 0 for a rescue payment.</summary>
        public uint victimNetId;

        /// <summary>True for the kill itself; false for the rescue zeny a turned-back character paid afterwards.</summary>
        public bool kill;

        public int combo;
        public int bonusPercent;
        public double endsAtEpochSeconds;
        public float windowSeconds;
        public int experience, experienceFromCombo;
        public int zeny, zenyFromCombo;
        public long experiencePerHour, zenyPerHour;
    }

    /// <summary>A PERSON CASTING A SKILL — the whole of it, named rather than resolved. Which skill, what they
    /// aimed at, and where. Nothing else: no damage, no duration, no charge count, no number of any kind.
    ///
    /// <para><b>This replaces asking for one strike at a time.</b> A skill used to be run on the caster's own
    /// machine, which sent the server only the damage half of it — so a brace held nothing, a buff protected
    /// nobody, and a charge was spent on a copy of the character that nobody else could see. The server now runs
    /// the authored skill itself, from its first beat to its last, against the character it holds for that person:
    /// the same component, the same effects, the same measured timings, drawing nothing.</para>
    ///
    /// <para>The aim is stated IN THE CASTER'S OWN MAP, because a server sets each map it holds down somewhere of
    /// its own and the two are not the same coordinates.</para></summary>
    public struct SkillCastRequest : NetworkMessage
    {
        public string skillId;

        /// <summary>What they picked, if the skill asked them to pick something. 0 when it did not.</summary>
        public uint targetNetId;

        /// <summary>Where they aimed, in their own map's coordinates.</summary>
        public Vector3 aimPoint;
    }

    /// <summary>A skill going off, as everybody else sees it — sent to every machine watching the caster so the
    /// effect, the pose and the sound play there too. It carries no outcome: what the skill DID arrives through
    /// the doors that already announce a blow, a heal and a death.</summary>
    public struct SkillCastPlayed : NetworkMessage
    {
        public uint casterNetId;
        public string skillId;
        public uint targetNetId;
        public Vector3 aimPoint;

        /// <summary>The number every scattered shot of this play is rolled from — picked here, so a shower falls in
        /// the same places on every screen as it fell in on the server that decided who it hit.</summary>
        public int seed;
    }

    /// <summary>A CAST WINDING UP — sent the moment somebody starts a spell with a cast time, to every machine
    /// that can see them, so their cast bar and the skill's name appear over their head there as they do over
    /// their own. Nothing about a wind-up crossed before this: a watcher saw somebody stand still and then an
    /// explosion, with no way to tell a long spell from a pause.
    ///
    /// <para>It names the SKILL rather than its name and colour, because a watcher already holds the authored
    /// skill and can read both off it — the same reason a cast going off names one.</para></summary>
    public struct CastWindupStarted : NetworkMessage
    {
        public uint casterNetId;
        public string skillId;

        /// <summary>When it began, on the clock both machines share — so a watcher whose message took a moment to
        /// arrive starts the bar part-filled rather than restarting it, and every screen empties at once.</summary>
        public double startedAt;

        /// <summary>How long the wind-up runs, as the server measured it against the character IT holds. A caster
        /// with more dexterity winds up faster, and that is the server's number, not the asker's.</summary>
        public float seconds;

        /// <summary>Where the cast is aimed, in the caster's own map — so the ring that opens on the ground under
        /// the wind-up appears in the same place on every screen as it does on the caster's. Without it a watcher
        /// saw the bar and the pose but no ring, which is most of what tells you a spell is about to land there.</summary>
        public Vector3 aimPoint;
    }

    /// <summary>A CUE GOING OFF ON A UNIT — the one message every visual reaction in the game travels on.
    ///
    /// <para><b>Why one message for all of them.</b> A hit spark, a death poof, a poison glow, a stun flash, a heal
    /// sparkle and a sprint puff are the same KIND of thing: a picture played on a body because something happened
    /// to it. They all already go through one door in the game (<see cref="VfxCue"/>), and this is that door's other
    /// half. Written once, so a cue added later crosses the wire without anybody wiring it up.</para>
    ///
    /// <para>It carries no outcome and no number. Whatever the event DID reached this machine through the doors that
    /// announce a blow, a heal or a death; this is only what it looked like.</para></summary>
    /// <summary>ONE LEG OF SOMETHING IN THE AIR — an arrow, a thrown blade's jump, its flight home — said by the
    /// machine that owns the world (<see cref="Flights"/>) and drawn by every machine that can see the thrower. It
    /// carries no damage: the blow is announced by the unit it lands on, at the moment it lands. It carries the exact
    /// moments the leg leaves and arrives on the shared clock, so the drawn flight reaches its target at the instant
    /// the number appears.</summary>
    public struct FlightLeg : NetworkMessage
    {
        /// <summary>Who threw it — the shared name every machine knows that unit by.</summary>
        public uint throwerNetId;

        /// <summary>Which of that thrower's flights this leg belongs to, so its next leg moves the same drawn blade.</summary>
        public ushort blade;

        /// <summary>Where this leg goes, so the drawn flight homes onto a body that is still moving. The thrower's own
        /// name when the leg is the flight home. 0 when the target is not a shared body.</summary>
        public uint toNetId;

        /// <summary>Where the target stood, in the thrower's map — what a machine that has not been told about the
        /// target draws toward.</summary>
        public Vector3 toPoint;

        /// <summary>When the leg leaves and when it arrives, on the clock every machine shares.</summary>
        public double departs, arrives;

        /// <summary>WHAT WAS THROWN — the kind of weapon it left, and for a skill's bounce the skill and which of its tick
        /// bindings it is (-1 for a plain throw). Each watching machine picks what to draw from these, out of its own
        /// copy of the skill (<see cref="Flights.LookOf"/>).</summary>
        public WeaponType thrown;
        public string skillId;
        public short binding;

        /// <summary>The thrower's map, so a watcher puts the flight in the right world.</summary>
        public string map;
    }

    public struct CuePlayed : NetworkMessage
    {
        /// <summary>Whose body it plays on — the shared name every machine knows that unit by.</summary>
        public uint onNetId;

        /// <summary>The cue's second reference point, when it has one. 0 when it does not.</summary>
        public uint targetNetId;

        public VfxCueId cue;

        /// <summary>0 = a one-shot puff, over the moment it plays. Anything else NAMES a lasting aura, so the
        /// message that ends it can say which one — two people poisoned at once is the ordinary case, and each
        /// one's glow has to come off its own body.</summary>
        public int auraKey;
    }

    /// <summary>A LASTING CUE THAT IS OVER — the ailment expired, the wind-up ended, the body went away. Sent from
    /// the one place an aura stops, so all three reasons take it off every screen the same way.</summary>
    public struct CueStopped : NetworkMessage
    {
        public uint onNetId;
        public int auraKey;
    }

    /// <summary>A WIND-UP THAT IS OVER — finished, or broken by a hit, a silence or a death. Sent from the one
    /// place a cast ends, so all three reasons clear the bar the same way and at the same moment the caster's own
    /// does. A watcher also puts a bar away on its own when nothing refreshes it, so a lost message costs a frame
    /// rather than leaving a bar hanging over somebody forever.</summary>
    public struct CastWindupEnded : NetworkMessage
    {
        public uint casterNetId;
    }

    /// <summary>A PERSON USING AN ITEM — a potion drunk, a food eaten. It names the ITEM, never the skill: the item
    /// is what says which skill runs, so a machine that named the skill itself could run any skill in the game by
    /// calling it a potion.
    ///
    /// <para>What using an item does is a skill like any other, so it is resolved where every other skill now is.
    /// Whether they actually HAD one is still their own machine's word, because a bag lives there and nowhere
    /// else — that is the inventory's migration, not this one.</para></summary>
    public struct ItemUsedRequest : NetworkMessage
    {
        /// <summary>WHERE IN THE BAG, not which item — the server reads what is actually at that position in the
        /// bag IT holds, so a machine cannot ask to drink a potion it does not have.</summary>
        public int bagIndex;

        /// <summary>What the asker believed was at that position. The server drops the request if the bag has
        /// moved under it — two quick clicks are enough to shift every position by one, and without this the
        /// second one would drink whatever slid into the place the first one emptied.</summary>
        public string expectedItemId;
    }

    /// <summary>A player asking to come back after a death. Carries nothing — the server knows who is asking and
    /// whether they are actually dead, and those are the only two facts that matter.</summary>
    public struct ReviveRequest : NetworkMessage { }

    /// <summary>A command a player typed that only the machine owning the world can carry out — summoning a
    /// creature, killing one, standing a dummy up. The whole typed line crosses rather than a parsed command,
    /// so the server reads it with the SAME parser the game does and the two cannot come to disagree about what
    /// a line meant.
    ///
    /// <para>Nothing here is trusted. The line names no target, no amount that is believed, and above all it
    /// carries no claim about who is asking — the server already knows which connection this arrived on, and
    /// looks up whether THAT person is a GM from what the account service told it at the door.</para></summary>
    public struct GmCommandRequest : NetworkMessage
    {
        public string line;
    }

    /// <summary>What the server has to say about it, on its way back into the asker's own chat log — the reply
    /// the command would have printed had it run at their keyboard. Sent only to the connection that asked.</summary>
    public struct GmCommandReply : NetworkMessage
    {
        public string text;
    }

    /// <summary>A player's machine asking what the game says about one creature, by its id — sent when the pointer
    /// rests on a creature's name in the map window. It carries the id and nothing else.</summary>
    public struct MobInfoRequest : NetworkMessage
    {
        public string id;
    }

    /// <summary>The server's finished text about that creature (<see cref="MobInfo.Text"/>), with the id it answers,
    /// sent only to the connection that asked.</summary>
    public struct MobInfoReply : NetworkMessage
    {
        public string id;
        public string text;
    }

    /// <summary>A player's machine asking how many of each creature are alive in one map, by its scene name — sent while
    /// the map window's zone view shows that map. It carries the map and nothing else.</summary>
    public struct MapCountsRequest : NetworkMessage
    {
        public string map;
    }

    /// <summary>The server's counts for that map (<see cref="MapCreatureCounts.Count"/>): each creature id there, how
    /// many are alive and how many the map keeps (0 when it keeps no number of its own), side by side. <c>held</c> is
    /// false when the server holds no such map. Sent only to the asker.</summary>
    public struct MapCountsReply : NetworkMessage
    {
        public string map;
        public bool held;
        public string[] ids;
        public int[] counts;
        public int[] kept;
    }

    /// <summary>A LINE SOMEBODY TYPED AND SAID OUT LOUD. It carries the words and nothing else: not who said them
    /// and not where they were standing, because the server already knows which connection this arrived on and
    /// where it holds that person. A machine that named its own speaker could put words in anybody's mouth.</summary>
    public struct ChatSaid : NetworkMessage
    {
        public string text;
    }

    /// <summary>A line as everybody HEARS it — sent to every machine that can see the speaker, the speaker's own
    /// included, so one line said is one row in one chat log on every screen rather than each machine drawing its
    /// own idea of what was said.
    ///
    /// <para>It carries WHO as well as WHAT, twice over and for two different reasons: the name is what the log row
    /// reads, and the shell's number is which body the white bubble hangs over.</para></summary>
    public struct ChatHeard : NetworkMessage
    {
        public uint speakerNetId;
        public string speaker;
        public string text;

        /// <summary>TRUE when the words are a skill NAME the speaker called out rather than something they typed.
        /// The line is drawn identically either way — same row, same white bubble over the same head — and only
        /// tinted apart, so a call reads as a call at a glance.</summary>
        public bool isSkillCall;
    }

    /// <summary>A PRIVATE MESSAGE somebody typed, for one person by name. Like <see cref="ChatSaid"/> it never names
    /// its sender: the server takes that from the connection it arrived on.</summary>
    public struct PmSend : NetworkMessage
    {
        public string toName;
        public string text;
    }

    /// <summary>A private message as its two ends see it, sent only to them. <see cref="outgoing"/> is the sender's
    /// own copy, sent once it was actually delivered; <see cref="name"/> is then who it went to, otherwise who it
    /// came from — spelled as that character spells it.</summary>
    public struct PmHeard : NetworkMessage
    {
        public string name;
        public string text;
        public bool outgoing;
    }

    /// <summary>A line THE GAME says to a player in their chat — a world event starting or ending, the events running
    /// when they sign in. The sentence is finished on the server; the player's machine only shows it.</summary>
    public struct SystemNotice : NetworkMessage
    {
        public string text;
    }

    // WHO IS TOLD ABOUT WHAT. Without this, everything that happens is sent to every person connected, however far
    // away and whatever map they are standing in — so the traffic climbed with the SQUARE of how many people were
    // here, and a person in a dungeon paid for every cast in every town.
    //
    // Distance is the measure because a server holds several maps at once by setting each one down 2000 apart from
    // the others: any range comfortably inside that gap separates the maps, with no second rule to keep in step.
    //
    // WHAT THIS RANGE DOES AND DOES NOT DO, measured rather than assumed: the maps themselves are small — 60x60 up
    // to 120x64 — so the component's own default of 500 separates MAPS and nothing else. Everyone standing in the
    // same map still hears each other, exactly as they did before, which is why nothing about what a player can see
    // changes today. Culling INSIDE a map needs a range near what the camera actually shows (it pulls back 7 at the
    // very most), and that is a decision about draw distance — people appearing and vanishing as you walk — so it is
    // deliberately not made here.
    //
    // Attached from here rather than left to a scene, because a world this is missing from is not visibly broken —
    // it just quietly sends everything to everybody, which is exactly the kind of fault nobody notices until there
    // are enough people for it to matter.
    void EnsureInterestManagement()
    {
        if (GetComponent<InterestManagementBase>() != null) return;
        gameObject.AddComponent<DistanceInterestManagement>();
    }

    public override void OnStartServer()
    {
        EnsureInterestManagement();
        base.OnStartServer();
        // NOTHING CARRIED OVER FROM A PREVIOUS WORLD. These outlive the server that filled them — a second Play in
        // the same editor session, or a server stopped and started — and a swing left in flight from the last one
        // names a connection number this one will hand out again to somebody else.
        pendingBlows.Clear();
        swingDueAt.Clear();
        attackStartDueAt.Clear();
        NetworkServer.RegisterHandler<JoinRequest>(OnJoinRequest);
        NetworkServer.RegisterHandler<HitRequest>(OnHitRequest);
        NetworkServer.RegisterHandler<AttackIntentRequest>(OnAttackIntentRequest);
        NetworkServer.RegisterHandler<ReviveRequest>(OnReviveRequest);
        NetworkServer.RegisterHandler<SkillCastRequest>(OnSkillCastRequest);
        NetworkServer.RegisterHandler<ItemUsedRequest>(OnItemUsedRequest);
        NetworkServer.RegisterHandler<GmCommandRequest>(OnGmCommandRequest);
        NetworkServer.RegisterHandler<MobInfoRequest>(OnMobInfoRequest);
        NetworkServer.RegisterHandler<MapCountsRequest>(OnMapCountsRequest);
        NetworkServer.RegisterHandler<ChatSaid>(OnChatSaid);
        NetworkServer.RegisterHandler<PmSend>(OnPmSend);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        NetworkClient.RegisterHandler<HitResult>(OnHitResult);
        NetworkClient.RegisterHandler<PlayerHitResult>(OnPlayerHitResult);
        NetworkClient.RegisterHandler<HitBatch>(OnHitBatch);
        NetworkClient.RegisterHandler<HPAddedShown>(OnHPAddedShown);
        NetworkClient.RegisterHandler<CooldownStarted>(OnCooldownStarted);
        NetworkClient.RegisterHandler<CastAnswered>(OnCastAnswered);
        NetworkClient.RegisterHandler<XpEarned>(OnXpEarned);
        // A new connection is a new run and a new meter on the server, so nothing the last one sent is kept.
        KillComboReadout.Forget();
        NetworkClient.RegisterHandler<KillComboShown>(OnKillComboShown);
        NetworkClient.RegisterHandler<SkillCastPlayed>(OnSkillCastPlayed);
        NetworkClient.RegisterHandler<GmCommandReply>(OnGmCommandReply);
        // A new connection may be to a server holding different numbers, so nothing an earlier one sent is kept.
        MobInfo.ForgetSent();
        NetworkClient.RegisterHandler<MobInfoReply>(OnMobInfoReply);
        MapCreatureCounts.ForgetSent();
        NetworkClient.RegisterHandler<MapCountsReply>(OnMapCountsReply);
        NetworkClient.RegisterHandler<WorldTime>(OnWorldTime);
        NetworkClient.RegisterHandler<TimeOfDayShift>(OnTimeOfDayShift);
        NetworkClient.RegisterHandler<WeatherHeld>(OnWeatherHeld);
        NetworkClient.RegisterHandler<ChatHeard>(OnChatHeard);
        NetworkClient.RegisterHandler<PmHeard>(OnPmHeard);
        NetworkClient.RegisterHandler<SystemNotice>(OnSystemNotice);
        NetworkClient.RegisterHandler<SignedInElsewhere>(OnSignedInElsewhere);
        NetworkClient.RegisterHandler<CastWindupStarted>(OnCastWindupStarted);
        NetworkClient.RegisterHandler<CastWindupEnded>(OnCastWindupEnded);
        NetworkClient.RegisterHandler<FlightLeg>(OnFlightLeg);
        NetworkClient.RegisterHandler<CuePlayed>(OnCuePlayed);
        NetworkClient.RegisterHandler<CueStopped>(OnCueStopped);    }

    // THE WORLD'S CLOCK ARRIVING. Until this lands the sun has nothing to draw itself from, which is why it is
    // sent with the body rather than waited for by anything.
    static void OnWorldTime(WorldTime msg) => WorldClock.TakeFromServer(msg.epochSeconds);

    static void OnTimeOfDayShift(TimeOfDayShift msg) => TimeOfDay.TakeShift(msg.seconds);
    static void OnWeatherHeld(WeatherHeld msg)
        => WeatherDirector.TakeForced(msg.preset < 0 ? null : (WeatherDirector.Preset?)msg.preset, msg.since,
                                      msg.before < 0 ? null : (WeatherDirector.Preset?)msg.before, msg.beforeSince);

    static WeatherHeld WeatherHeldNow()
        => new WeatherHeld
        {
            preset = WeatherDirector.ForcedByServer.HasValue ? (int)WeatherDirector.ForcedByServer.Value : -1,
            since = WeatherDirector.ForcedSince,
            before = WeatherDirector.ForcedBefore.HasValue ? (int)WeatherDirector.ForcedBefore.Value : -1,
            beforeSince = WeatherDirector.ForcedBeforeSince,
        };

    /// <summary>Server-only: tell everybody on this server the time-of-day shift it now holds.</summary>
    public static void TellEveryoneTheTimeOfDay()
    {
        if (!NetworkServer.active) return;
        NetworkServer.SendToAll(new TimeOfDayShift { seconds = TimeOfDay.ShiftSeconds });
    }

    /// <summary>Server-only: tell everybody on this server the weather it is now holding.</summary>
    public static void TellEveryoneTheWeather()
    {
        if (!NetworkServer.active) return;
        NetworkServer.SendToAll(WeatherHeldNow());
    }

    // A blow on a PERSON, arriving on every machine that can see them. Each one shows it in the way it honestly
    // can — see NetPlayer.ShowBlow.
    static void OnPlayerHitResult(PlayerHitResult msg) => DrawPlayerHit(msg, msg.amount);

    // A skill tick's blows, all at once: each drawn now or held for its place in the cascade.
    static void OnHitBatch(HitBatch msg)
    {
        if (msg.mobHits != null)
            for (int i = 0; i < msg.mobHits.Length; i++)
            {
                int delay = msg.mobDelays != null && i < msg.mobDelays.Length ? msg.mobDelays[i] : 0;
                int log = msg.mobLog != null && i < msg.mobLog.Length ? msg.mobLog[i] : msg.mobHits[i].number;
                if (delay <= 0) DrawMobHit(msg.mobHits[i], log); else BlowBatch.QueueMob(msg.mobHits[i], delay, log);
            }
        if (msg.playerHits != null)
            for (int i = 0; i < msg.playerHits.Length; i++)
            {
                int delay = msg.playerDelays != null && i < msg.playerDelays.Length ? msg.playerDelays[i] : 0;
                int log = msg.playerLog != null && i < msg.playerLog.Length ? msg.playerLog[i] : msg.playerHits[i].amount;
                if (delay <= 0) DrawPlayerHit(msg.playerHits[i], log); else BlowBatch.QueuePlayer(msg.playerHits[i], delay, log);
            }
    }

    /// <summary>Draw one blow on a person exactly as the server sent it. <paramref name="logNumber"/> is what the combat
    /// log writes for it, or −1 for a piece of a blow the log already wrote.</summary>
    public static void DrawPlayerHit(PlayerHitResult msg, int logNumber)
    {
        if (!NetworkClient.spawned.TryGetValue(msg.victimNetId, out var identity) || identity == null) return;
        var shell = identity.GetComponent<NetPlayer>();
        if (shell == null) return;

        // A CREATURE THAT JUST STRUCK AT THIS MACHINE'S PLAYER, hit or miss, is drawn through scenery for a while.
        if (identity.isLocalPlayer)
        {
            var striker = NetMob.ShellForNetId(msg.attackerNetId);
            if (striker != null) striker.TradedABlowWithMe();
            // AND THE COMBAT LOG'S LINE for it, the server's own numbers stored as they are.
            if (logNumber >= 0)
                ChatController.CombatTaken(striker != null ? striker.ShownName : null, logNumber, msg.missed, msg.dodged,
                                           msg.isCrit, msg.tick ? msg.tickCause : TrueDamageCause.None,
                                           msg.mitigated.parried, msg.mitigated.blocked);
        }

        shell.ShowBlow(new DamageInfo(msg.amount, msg.element, msg.isMagic, msg.isCrit,
                                      attackerHit: msg.attackerHit, missed: msg.missed),
                       msg.mitigated, msg.tick, msg.tickCause, msg.total, msg.attackerNetId, msg.dodged);
    }

    // Green digits the server said it healed, over the body this machine draws for that unit.
    static void OnHPAddedShown(HPAddedShown msg)
    {
        if (!NetworkClient.spawned.TryGetValue(msg.netId, out var identity) || identity == null) return;
        Transform body = null;
        var shell = identity.GetComponent<NetMob>();
        if (shell != null)
        {
            var creature = shell.WatchedTarget as Health;
            body = creature != null ? creature.transform : null;
        }
        else
        {
            var person = identity.GetComponent<NetPlayer>();
            body = person != null ? person.DrawnBody : null;
        }
        if (body != null) CombatAuthority.ShowHPAdded(body, msg.amount);
    }

    // A cooldown the server started: this machine's runner for that skill is set to end exactly when the server's does.
    static void OnCooldownStarted(CooldownStarted msg)
    {
        var me = Character.Instance;
        // The SHARED cooldown travels in the same message under its own name: it belongs to the character, not to
        // any one skill.
        if (me != null && msg.skillId == SharedCooldown.UltimateKey)
        {
            me.Skills.ResumeSharedUltimate(WorldClock.FromEpochSeconds(msg.endsAtEpochSeconds));
            return;
        }
        var runner = me != null ? me.RunnerById(msg.skillId) : null;
        if (runner != null) runner.ResumeCooldown(WorldClock.FromEpochSeconds(msg.endsAtEpochSeconds));
    }

    // The server is done with a press: the skill may be pressed again (its cooldown, if any, has already arrived).
    static void OnCastAnswered(CastAnswered msg)
    {
        var me = Character.Instance;
        var runner = me != null ? me.RunnerById(msg.skillId) : null;
        if (runner == null) return;
        runner.ServerAnsweredPress();
        if (msg.refused)
        {
            runner.ServerRefusedPress();
            AutoCombatClient.ServerRefused(msg.skillId);
        }
    }

    static void OnXpEarned(XpEarned msg)
    {
        // A machine that IS the server already ran the level-up on this very character; only a machine whose
        // world is elsewhere has anything to take.
        if (Character.Instance == null || !TheWorldIsElsewhere) return;
        Character.Instance.TakeXpFromServer(msg.level, msg.xp, msg.lifetimeXp, msg.statPoints, msg.levelsGained,
                                            msg.ringLevel, msg.ringXp, msg.ringBankedPoints, msg.ringLevelsGained);
    }

    // A kill's place in this player's run, as the server worked it out: the combo readout copies it, and the combat log
    // writes what the kill paid.
    static void OnKillComboShown(KillComboShown msg)
    {
        KillComboReadout.Take(msg.combo, msg.bonusPercent, msg.endsAtEpochSeconds, msg.windowSeconds,
                              msg.experiencePerHour, msg.zenyPerHour);
        if (msg.kill)
        {
            var shell = NetMob.ShellForNetId(msg.victimNetId);
            ChatController.CombatDefeated(shell != null ? shell.ShownName : null);
            if (msg.experience > 0) ChatController.CombatExperience(msg.experience, msg.experienceFromCombo);
        }
        if (msg.zeny > 0) ChatController.CombatZeny(msg.zeny, msg.zenyFromCombo);
    }

    void OnReviveRequest(NetworkConnectionToClient conn, ReviveRequest msg)
    {
        // Only a dead person can be brought back, so a live one asking is refused rather than handed a free heal.
        var fighter = FighterFor(conn) as Character;

        // IT SAYS WHY IT REFUSED. This used to do nothing at all when it would not grant a revive, which from
        // the player's seat is a Respawn button that does not work and a death screen that never goes away, with
        // nothing anywhere saying so. A refusal that cannot be explained is the same as a crash you cannot see.
        // AND IT TELLS THEIR MACHINE, which raised the loading screen on the press and would otherwise keep it up.
        if (fighter == null)
        {
            GameLog.Warn(LogCat.Server, "somebody asked to be revived but this server holds no fighter for them.");
            RefuseTripOf(conn, "Nobody here to return.");
            return;
        }
        if (!fighter.IsDead)
        {
            GameLog.Warn(LogCat.Server, "" + (conn.identity != null ? conn.identity.name : "somebody")
                             + " asked to be revived but this server has them alive on "
                             + fighter.CurrentHP + " health — nothing to do.");
            RefuseTripOf(conn, "You are already standing.");
            return;
        }
        GameLog.Info(LogCat.Server, "returning " + NameOf(fighter) + " (" + CharacterIdOf(fighter) + "), who was dead, "
                         + "to their save point.");
        RespawnOnServer(fighter);
    }

    static void RefuseTripOf(NetworkConnectionToClient conn, string why)
    {
        var shell = conn.identity != null ? conn.identity.GetComponent<NetPlayer>() : null;
        if (shell != null) shell.RefuseTrip(why);
    }

    /// <summary>Server-only: stand <paramref name="who"/> back up where they fell, at full HP and MP — the GM's
    /// <c>/revive</c> and <c>/heal</c>. A player's own "Return to last saved point" is <see cref="RespawnOnServer"/>.</summary>
    public static void ReviveOnServer(Character who)
    {
        if (!NetworkServer.active || who == null) return;
        who.Revive();
    }

    /// <summary>Server-only: stand <paramref name="who"/> back up at their save point, on a tenth of their health with
    /// their mana as it was. The one way a player comes back by their own choice — the death window's "Return to
    /// last saved point" asks for this.</summary>
    public static void RespawnOnServer(Character who)
    {
        if (!NetworkServer.active || who == null) return;
        who.ReviveAtSavePoint();

        var shell = ShellFor(who);
        if (shell == null) return;
        // COMING BACK THIS WAY IS AN UNLOCK CONDITION — the tutorial that teaches healing plays from here. Written down
        // on the next save, which a move to a map another server holds makes before they leave.
        if (who.Quests.Unlock(QuestUnlock.ReturnToSavePoint, who.CurrentHP, who.MaxHP)) shell.QuestsMoved();
        // AND COMING BACK MOVES THEM, nowhere near where they fell. The trip holds them where they are sent and
        // believes no report until one lands there (see NetPlayer.ExpectArrival), so the jump is never read as a lie.
        shell.SendToSavePoint();
    }

    /// <summary>The body belonging to this character, on a server. Lets anything holding a CHARACTER reach the
    /// connection that owns it — a GM command acting on somebody's bag has to be able to tell them about it.
    /// Null off a server, and null for a character nobody is connected as.</summary>
    /// <summary>Cut off whoever is currently playing this character here, because somebody else has just been
    /// announced as arriving with it. TRUE when somebody was actually dropped.
    ///
    /// <para>Two different things need this and both are the same act. A person signing in a second time TAKES
    /// OVER, so the one already playing has to go. And a person walking through a door into a zone this process
    /// holds is announced while their old body is still standing somewhere — if that body is here, it is the
    /// same character twice and the older one is the one that is finished with.</para></summary>
    /// <summary>Write this character down, wait for the write to land, and only then let go of the connection
    /// playing them. <paramref name="done"/> is called either way, and is what tells the character server it may
    /// now read the row.
    ///
    /// <para><b>The order is the whole point.</b> Reading the row before this person has been written down hands
    /// the machine taking over whoever they were on the last routine save — up to fifteen seconds ago — and
    /// everything since is simply never recorded. It is the same order a door already uses: write, wait, then
    /// hand over.</para></summary>
    public static void SaveAndDrop(string characterId, System.Action done)
    {
        if (string.IsNullOrEmpty(characterId) || !NetworkServer.active) { done?.Invoke(); return; }

        NetPlayer shell = null;
        NetworkConnectionToClient holding = null;
        foreach (var conn in NetworkServer.connections.Values)
        {
            var s = conn != null && conn.identity != null ? conn.identity.GetComponent<NetPlayer>() : null;
            if (s != null && s.ServerCharacterId == characterId) { shell = s; holding = conn; break; }
        }
        // NOT PLAYING HERE — but a save of theirs may still be waiting: they logged out while the character server
        // could not be reached. It is sent, and landed, before this is answered, so whoever takes over reads it.
        // Most servers asked have nothing waiting either, and answer at once.
        if (shell == null)
        {
            SaveOutbox.Flush(characterId, ok =>
            {
                if (!ok) GameLog.Warn(LogCat.Server, "a waiting save for character " + characterId + " could not be "
                                          + "sent before letting go — whoever takes over starts from the stored copy.");
                done?.Invoke();
            });
            return;
        }

        // TOLD WHY, while the write is in the air, which is long enough for the message to go out before the hang-up.
        TellSignedInElsewhere(holding, shell);

        // NOTHING MORE FROM THEM. Said before the write, so what is written is the last thing they actually did
        // rather than something accepted while the write was in the air.
        shell.MarkLeaving();

        GameLog.Info(LogCat.Server, shell.playerName + " is being taken over — writing them down before letting go.");
        shell.SaveNow(ok =>
        {
            if (!ok) GameLog.Warn(LogCat.Server, "could not write that character down before letting go of them — "
                                       + "whoever takes over will start from their last ordinary save.");
            if (holding != null) holding.Disconnect();
            done?.Invoke();
        });
    }

    /// <summary>SERVER ONLY: is the connection playing this character one that has already walked into a door and
    /// is waiting to be told where to go?
    ///
    /// <para>This exists for the map a player walks into that THIS SAME server already holds. The character server
    /// announces them here before it answers the process they are standing on, and the ordinary answer to an
    /// announcement is to drop whoever is playing that character — which here is the mover themselves, on the one
    /// connection their new address and ticket still have to travel down. Cutting it kills the move at its first
    /// step and leaves the player behind a curtain nothing can lower.</para></summary>
    public static bool WhoIsPlayingIsAlreadyMoving(string characterId)
    {
        if (string.IsNullOrEmpty(characterId) || !NetworkServer.active) return false;
        foreach (var conn in NetworkServer.connections.Values)
        {
            var shell = conn != null && conn.identity != null ? conn.identity.GetComponent<NetPlayer>() : null;
            if (shell != null && shell.ServerCharacterId == characterId) return shell.Leaving;
        }
        return false;
    }

    public static bool DropWhoeverIsPlaying(string characterId)
    {
        if (string.IsNullOrEmpty(characterId) || !NetworkServer.active) return false;

        NetworkConnectionToClient found = null;
        foreach (var conn in NetworkServer.connections.Values)
        {
            var shell = conn != null && conn.identity != null ? conn.identity.GetComponent<NetPlayer>() : null;
            if (shell != null && shell.ServerCharacterId == characterId) { found = conn; break; }
        }
        if (found == null) return false;

        // SAID BEFORE THEY ARE DROPPED. Leaving is what stops the disconnect below writing this copy down over
        // the one the other process is already holding, and it stops anything else this connection sends being
        // acted on in the moment it has left.
        var going = found.identity != null ? found.identity.GetComponent<NetPlayer>() : null;
        bool told = TellSignedInElsewhere(found, going);
        going?.MarkLeaving();

        GameLog.Info(LogCat.Server, "dropping the connection already playing that character — somebody has just "
                         + "been announced with it, so this one is the older of the two.");
        if (told && spawned != null) spawned.StartCoroutine(HangUpShortly(found));
        else found.Disconnect();
        return true;
    }

    public static NetPlayer ShellFor(Character who)
    {
        if (who == null || !NetworkServer.active) return null;
        foreach (var conn in NetworkServer.connections.Values)
        {
            var shell = ShellFor(conn);
            if (shell != null && ReferenceEquals(shell.ServerCombatUnit, who)) return shell;
        }
        return null;
    }

    // WHO IS PLAYING ON A CONNECTION, remembered. The answer cannot change while somebody is here, yet it used to be
    // searched for on their object again on every single thing they asked for — every cast, every item, every swing.
    // Filled the first time it is asked and dropped when the connection goes, the same as everything else kept per
    // connection here. A body that has been destroyed reads as missing and is looked up again, so a person who is
    // given a fresh one is never answered with the old.
    static readonly System.Collections.Generic.Dictionary<int, NetPlayer> shellByConnection =
        new System.Collections.Generic.Dictionary<int, NetPlayer>();

    static NetPlayer ShellFor(NetworkConnectionToClient conn)
    {
        if (conn == null) return null;
        if (shellByConnection.TryGetValue(conn.connectionId, out var known) && known != null && known) return known;

        var shell = conn.identity != null ? conn.identity.GetComponent<NetPlayer>() : null;
        if (shell != null) shellByConnection[conn.connectionId] = shell;
        return shell;
    }

    static ICombatUnit FighterFor(NetworkConnectionToClient conn)
    {
        var shell = ShellFor(conn);
        // NOBODY WHO IS LEAVING ACTS. From the moment a move starts, this person's state on this process is
        // finished and about to be thrown away — so a swing, a heal, a pick-up or a spent point accepted after it
        // happens to a copy nobody will ever read again, and the player simply loses it. Refused HERE, at the one
        // place every player-driven request resolves who is asking, rather than at each of them in turn.
        if (shell == null || shell.Leaving) return null;
        return shell.ServerCombatUnit;
    }

    /// <summary>SERVER ONLY: say what a unit was just healed for. A person hears their own gains and nobody else does —
    /// the audience the green digits have always had; a creature's gains reach everybody watching it.</summary>
    public static void AnnounceHPAdded(ICombatUnit unit, int amount)
    {
        if (!NetworkServer.active || unit == null || amount <= 0) return;

        if (unit is Character person)
        {
            var identity = IdentityOfFighter(person);
            var conn = identity != null ? identity.connectionToClient : null;
            if (conn != null) conn.Send(new HPAddedShown { netId = identity.netId, amount = amount });
            return;
        }

        var body = unit.Body;
        var shell = body != null ? NetMob.ShellForServerMob(body.gameObject) : null;
        if (shell != null)
            NetworkServer.SendToReadyObservers(shell.netIdentity, new HPAddedShown { netId = shell.netId, amount = amount });
    }

    /// <summary>SERVER ONLY: tell a person the server just started a cooldown on one of their skills, and when it ends.
    /// Silent for a character nobody is playing.</summary>
    public static void TellOwnerCooldownStarted(ICombatUnit who, string skillId, System.DateTime endsAtUtc)
    {
        if (!NetworkServer.active || PlayerUnits.Gone(who) || string.IsNullOrEmpty(skillId)) return;
        // A creature's shell belongs to no connection, so nobody is told — its cooldowns are its own business.
        var identity = IdentityOfCaster(who);
        var conn = identity != null ? identity.connectionToClient : null;
        if (conn == null) return;
        conn.Send(new CooldownStarted { skillId = skillId, endsAtEpochSeconds = WorldClock.ToEpochSeconds(endsAtUtc) });
    }

    /// <summary>Announce a blow the server decided landed on a person, to everybody who can see them. Called by the
    /// damage door rather than by whatever swung, so a creature's bite, a poison tick and anything written later all
    /// reach the screens through one line.</summary>
    public static void AnnounceBlowOnPlayer(Character victim, in DamageInfo info, int dealt, bool tick = false,
                                            TrueDamageCause tickCause = TrueDamageCause.None)
    {
        if (!NetworkServer.active || victim == null) return;

        var identity = IdentityOfFighter(victim);
        if (identity == null) return;
        uint striker = SharedNameOfStriker(info.attacker);

        // A MULTI-HIT STRIKE IS SHOWN AS ITS HITS: one blow, cut into equal pieces here, each with its own place in
        // the cascade and its own rising total — every number final before it leaves.
        int pieces = BlowBatch.Open && BlowBatch.Split > 1 && !tick && !info.missed && dealt > 0 ? BlowBatch.Split : 1;
        if (BlowBatch.Open)
        {
            var mitigatedOnce = victim.TakeMitigation();
            bool dodgedOnce = dealt <= 0 && CombatAuthority.WhyNoDamage == NoDamageReason.Dodged;
            for (int k = 0; k < pieces; k++)
            {
                int piece = BlowBatch.Piece(dealt, pieces, k);
                BlowBatch.AddPlayer(identity, new PlayerHitResult
                {
                    victimNetId = identity.netId,
                    amount = piece,
                    element = info.element,
                    isMagic = info.isMagic,
                    isCrit = info.isCrit,
                    missed = info.missed,
                    attackerHit = info.attackerHit,
                    mitigated = k == 0 ? mitigatedOnce : default,
                    tick = tick,
                    tickCause = tickCause,
                    attackerNetId = striker,
                    total = SkillEffectApplier.StrikeShowsTotal && !tick && !info.missed && piece > 0
                        ? StrikeTotals.Add(identity.netId, striker, piece) : 0,
                    dodged = dodgedOnce,
                }, k, k == 0 ? dealt : -1);
            }
            return;
        }

        NetworkServer.SendToReadyObservers(identity, new PlayerHitResult
        {
            victimNetId = identity.netId,
            amount = dealt,
            element = info.element,
            isMagic = info.isMagic,
            isCrit = info.isCrit,
            missed = info.missed,
            attackerHit = info.attackerHit,
            // TAKEN AND CLEARED, right after the blow was applied and before anything else can be hit. The victim
            // recorded this on its way through its own intake, which is the only place that knows a barrier ate part
            // of it or a brace held all of it.
            mitigated = victim.TakeMitigation(),
            tick = tick,
            tickCause = tickCause,
            // WHO STRUCK, through the one helper that answers for a creature and a person alike — the striker here is
            // usually a creature, which the person-only lookup the creature's announce uses cannot name.
            attackerNetId = striker,
            // Whether a total is owed is the authored skill's answer, held by the strike landing right now; the sum is
            // the server's. A damage-over-time tick never folds in: it is a different thing happening at the same time.
            total = SkillEffectApplier.StrikeShowsTotal && !tick && !info.missed && dealt > 0
                ? StrikeTotals.Add(identity.netId, striker, dealt) : 0,
            dodged = dealt <= 0 && CombatAuthority.WhyNoDamage == NoDamageReason.Dodged,
        });
    }

    // THE SHARED NAME FOR WHOEVER STRUCK, creature or person. Only the rising yellow total reads it, to keep two
    // strikers' numbers apart; 0 means nobody in particular, which is what a blow with no unit behind it is.
    static uint SharedNameOfStriker(ICombatUnit attacker)
    {
        if (PlayerUnits.Gone(attacker)) return 0u;
        var identity = SharedIdentityOnServer(attacker.Body);
        return identity != null ? identity.netId : 0u;
    }

    /// <summary>SERVER ONLY: say that a creature just BEGAN an action — a swing, and later anything else its body
    /// does that takes time. Silent off a server, and silent for a creature no shell speaks for.
    ///
    /// <para>Sent from the MOMENT it happens rather than noticed on the push beat, which runs ten times a second:
    /// an action noticed there would be up to 100 ms late and a short one would be missed outright.</para>
    ///
    /// <para><paramref name="beatSeconds"/> is the window the action has to finish inside — a number this machine
    /// already holds. It is never an animation clip's length: a headless server has no business reading art.</para></summary>
    public static void AnnounceActionOnMob(GameObject serverMob, StanceId action, float beatSeconds, Transform aimedAt = null,
                                           float connectsAfter = 0f)
    {
        if (!NetworkServer.active || serverMob == null) return;
        var shell = NetMob.ShellForServerMob(serverMob);
        if (shell == null) return;
        // WHO IT WAS AIMED AT, by the shared name every machine knows them by — so a screen can draw the swing toward
        // them. 0 = nobody in particular (a skill's release), or somebody no screen can see.
        var aimed = aimedAt != null ? SharedIdentityOnServer(aimedAt) : null;
        shell.BeganAction(action, beatSeconds, aimed != null ? aimed.netId : 0u, connectsAfter);
    }

    /// <summary>CLIENT: the body this machine draws for the unit the server calls <paramref name="netId"/>, or null when
    /// it is not here.</summary>
    public static Transform DrawnBodyOf(uint netId)
        => netId != 0u && NetworkClient.spawned.TryGetValue(netId, out var identity) ? DrawnBodyOf(identity) : null;

    /// <summary>SERVER ONLY: a caster began an action — a skill's release. A PERSON's own machine already tells the world
    /// every pose it plays, so this speaks only for a creature, through the same road its swings take.</summary>
    public static void AnnounceActionOnCaster(ICombatUnit caster, StanceId action, float beatSeconds)
    {
        if (caster is Health creature && creature != null) AnnounceActionOnMob(creature.gameObject, action, beatSeconds);
    }

    /// <summary>SERVER ONLY: a caster started or stopped HOLDING a pose — the casting pose for a wind-up. Speaks only
    /// for a creature, for the same reason as <see cref="AnnounceActionOnCaster"/>.</summary>
    public static void AnnounceHeldOnCaster(ICombatUnit caster, bool has, StanceId pose, float window)
    {
        if (!NetworkServer.active || !(caster is Health creature) || creature == null) return;
        var shell = NetMob.ShellForServerMob(creature.gameObject);
        if (shell != null) shell.HoldPose(has, pose, window);
    }

    /// <summary>SERVER ONLY: tell everybody watching a creature what it just drew for a blow. Called by the creature
    /// itself at the moment it draws one (<c>Health.Show</c>), so every blow — a swing, a strike, an execute, a whiff,
    /// a flurry beat past death — is announced by one line nobody has to remember to add. The mirror of
    /// <see cref="AnnounceBlowOnPlayer"/>.
    ///
    /// <para><b>What it deliberately does nothing for.</b> Off a server. And a creature no server published, which has
    /// no audience to announce to.</para></summary>
    public static void AnnounceBlowOnMob(Health victim, ICombatUnit attacker, in ShownBlow blow)
    {
        if (!NetworkServer.active || victim == null) return;

        var shell = NetMob.ShellForServerMob(victim.gameObject);
        if (shell == null) return;

        var attackerIdentity = IdentityOfFighter(attacker as Character);
        uint striker = attackerIdentity != null ? attackerIdentity.netId : 0u;

        // A MULTI-HIT STRIKE IS SHOWN AS ITS HITS — see the person's twin above.
        if (BlowBatch.Open)
        {
            bool runs = blow.cause == TrueDamageCause.None && !blow.missed && blow.number > 0;
            int pieces = runs && BlowBatch.Split > 1 ? BlowBatch.Split : 1;
            for (int k = 0; k < pieces; k++)
            {
                int piece = BlowBatch.Piece(blow.number, pieces, k);
                BlowBatch.AddMob(shell.netIdentity, new HitResult
                {
                    targetNetId = shell.netId,
                    attackerNetId = striker,
                    number = piece,
                    elementModifier = blow.elementModifier,
                    crit = blow.crit,
                    magic = blow.magic,
                    missed = blow.missed,
                    flash = blow.flash,
                    tickCause = blow.cause,
                    total = SkillEffectApplier.StrikeShowsTotal && runs && piece > 0
                        ? StrikeTotals.Add(shell.netId, striker, piece) : 0,
                }, k, k == 0 ? blow.number : -1);
            }
            return;
        }

        NetworkServer.SendToReadyObservers(shell.netIdentity, new HitResult
        {
            targetNetId = shell.netId,
            attackerNetId = striker,
            // THE BLOW AS IT CAME OUT, every number final: armour, the element chart, a parry and a barrier have all
            // already been taken off here, and nothing on the watching machine takes anything off again.
            number = blow.number,
            elementModifier = blow.elementModifier,
            crit = blow.crit,
            magic = blow.magic,
            missed = blow.missed,
            flash = blow.flash,
            tickCause = blow.cause,
            // Whether a total is owed is the authored skill's answer, held by the strike being landed right now; the sum
            // is the server's. Damage that bypassed defence is never part of a run.
            total = SkillEffectApplier.StrikeShowsTotal && blow.cause == TrueDamageCause.None && !blow.missed && blow.number > 0
                ? StrikeTotals.Add(shell.netId, striker, blow.number) : 0,
        });
    }

    /// <summary>Tell the person a reward belongs to that they earned it — and what their character now IS, so
    /// their machine copies the answer rather than working it out. Called AFTER the server has paid the
    /// character (<see cref="Character.AddXP(int, out int)"/>), with the level counts that call returned. Silent
    /// off a server, and silent for a character that is not somebody's. The sheet those numbers produce (max
    /// HP among them) follows on the very next beat through the same door a GM's change uses.</summary>
    public static void TellOwnerTheyEarned(Character who, int xp, int levelsGained, int ringLevelsGained)
    {
        if (xp <= 0) return;
        TellOwnerTheirProgress(who, xp, levelsGained, ringLevelsGained);
    }

    /// <summary>SERVER ONLY: tell <paramref name="who"/>'s own machine their level, experience, ring and points as they
    /// stand now, with nothing earned — after a GM dialled a level straight in (<c>/ringlvl</c>), up or down.</summary>
    public static void TellOwnerTheirProgress(Character who) => TellOwnerTheirProgress(who, 0, 0, 0);

    static void TellOwnerTheirProgress(Character who, int xp, int levelsGained, int ringLevelsGained)
    {
        if (!NetworkServer.active || who == null) return;

        var identity = IdentityOfFighter(who);
        if (identity == null || identity.connectionToClient == null) return;
        identity.connectionToClient.Send(new XpEarned
        {
            amount = Mathf.Max(0, xp),
            level = who.Level,
            xp = who.XP,
            lifetimeXp = who.LifetimeXp,
            statPoints = who.StatPoints,
            levelsGained = Mathf.Max(0, levelsGained),
            ringLevel = who.Ring.Level,
            ringXp = who.Ring.XP,
            ringBankedPoints = who.Ring.Passives.BankedPoints,
            ringLevelsGained = Mathf.Max(0, ringLevelsGained),
        });
        identity.GetComponent<NetPlayer>()?.PushStatsToOwner();
    }

    /// <summary>SERVER ONLY: tell <paramref name="who"/>'s own machine where their run of kills stands after a payment —
    /// a kill of <paramref name="victim"/> (<paramref name="kill"/>) or a turned-back character's rescue zeny — what that
    /// payment was and how much of it the run added, and their experience and zeny per hour. Called AFTER the payment is
    /// made and put into the meter (<see cref="KillCombo"/>). Silent off a server, and for a character that is not
    /// somebody's.</summary>
    public static void TellOwnerTheirKillCombo(Character who, Health victim, bool kill, int experience,
                                               int experienceFromCombo, int zeny, int zenyFromCombo)
    {
        if (!NetworkServer.active || who == null) return;

        var identity = IdentityOfFighter(who);
        if (identity == null || identity.connectionToClient == null) return;
        var shell = kill && victim != null ? NetMob.ShellForServerMob(victim.gameObject) : null;
        SendKillCombo(identity, who.KillCombo, shell != null ? shell.netId : 0u, kill, experience, experienceFromCombo,
                      zeny, zenyFromCombo);
    }

    /// <summary>SERVER ONLY: if <paramref name="who"/>'s experience or zeny per hour has moved since it was last sent —
    /// it falls minute by minute once they stop killing — send the rates as they now stand, with no payment in it.
    /// Asked once a minute per person (<see cref="NetPlayer"/>); sends nothing once the rates rest at their last value,
    /// which for somebody who stopped is zero, ten minutes on.</summary>
    public static void TellOwnerTheirRatesIfMoved(Character who)
    {
        if (!NetworkServer.active || who == null) return;
        var run = who.KillCombo;
        double now = KillCombo.Now;
        if (run.ExperiencePerHour(now) == run.SentExperiencePerHour && run.ZenyPerHour(now) == run.SentZenyPerHour) return;
        var identity = IdentityOfFighter(who);
        if (identity == null || identity.connectionToClient == null) return;
        SendKillCombo(identity, run, 0u, false, 0, 0, 0, 0);
    }

    static void SendKillCombo(NetworkIdentity identity, KillCombo run, uint victimNetId, bool kill, int experience,
                              int experienceFromCombo, int zeny, int zenyFromCombo)
    {
        double now = KillCombo.Now;
        long experiencePerHour = run.ExperiencePerHour(now), zenyPerHour = run.ZenyPerHour(now);
        run.SentExperiencePerHour = experiencePerHour;
        run.SentZenyPerHour = zenyPerHour;
        identity.connectionToClient.Send(new KillComboShown
        {
            victimNetId = victimNetId,
            kill = kill,
            combo = run.Count,
            bonusPercent = KillComboMath.BonusPercent(run.Count),
            // The run's end, measured on the server's steady clock and turned into the world's time as it is sent.
            endsAtEpochSeconds = WorldClock.EpochSecondsNow + run.SecondsLeft(now),
            windowSeconds = (float)KillComboMath.WindowSeconds,
            experience = Mathf.Max(0, experience),
            experienceFromCombo = Mathf.Max(0, experienceFromCombo),
            zeny = Mathf.Max(0, zeny),
            zenyFromCombo = Mathf.Max(0, zenyFromCombo),
            experiencePerHour = experiencePerHour,
            zenyPerHour = zenyPerHour,
        });
    }

    /// <summary>Server-only: hand <paramref name="who"/>'s own machine the bag as it now stands, and write the
    /// change down. TRUE when it was sent — which is also the answer to "was this a server's doing", so a caller
    /// paying somebody knows whether the window still has to be told directly (see <see cref="Rewards.PayLoot"/>).
    /// FALSE on a machine with no world, where the only bag there is is the one already on screen.</summary>
    public static bool TellOwnerTheirBagChanged(Character who)
    {
        if (!NetworkServer.active || who == null) return false;

        var identity = IdentityOfFighter(who);
        var shell = identity != null ? identity.GetComponent<NetPlayer>() : null;
        if (shell == null) return false;

        shell.PushBagToOwner();
        shell.SaveSoon();
        return true;
    }

    /// <summary>What to call <paramref name="who"/> in a line of log. On a server that is the name the person
    /// signed in with, which is the only way to tell several people apart; on a machine with one person at it, it
    /// is whoever is signed in here.</summary>
    public static string NameOf(Character who)
    {
        if (who == null) return "someone";

        var identity = NetworkServer.active ? IdentityOfFighter(who) : null;
        var shell = identity != null ? identity.GetComponent<NetPlayer>() : null;
        if (shell != null && !string.IsNullOrWhiteSpace(shell.playerName)) return shell.playerName;

        return string.IsNullOrWhiteSpace(AccountSession.CharacterName) ? "someone" : AccountSession.CharacterName;
    }

    /// <summary>The player id of the character <paramref name="who"/> — the id their row has in the database. Server
    /// only; for a log line, so an unknown one reads as "no player id" rather than an empty pair of brackets.</summary>
    public static string CharacterIdOf(Character who)
    {
        var identity = who != null && NetworkServer.active ? IdentityOfFighter(who) : null;
        var shell = identity != null ? identity.GetComponent<NetPlayer>() : null;
        return shell != null && !string.IsNullOrEmpty(shell.ServerCharacterId) ? shell.ServerCharacterId : "no player id";
    }

    /// <summary>Ask the server to bring this player back. Returns TRUE when it was sent.</summary>
    public static bool SendReviveToServer()
    {
        if (!WatchingOnly()) return false;
        NetworkClient.Send(new ReviveRequest());
        return true;
    }

    /// <summary>TRUE when this machine does not decide damage — every blow it makes is sent, and the number comes
    /// back. Read by presentation that used to add up what a local hit RETURNED: those returns are 0 now, and a
    /// running total built from them would show a confident, enormous zero.</summary>
    public static bool DamageDecidedElsewhere => WatchingOnly();

    // TRUE on a machine that is watching a world somebody else owns — a player's. False on the server, which owns
    // it, so the server's own fighters act for real rather than sending themselves messages.
    static bool WatchingOnly() => !NetworkServer.active && NetworkClient.active && NetworkClient.isConnected;

    // The shell a server-held fighter belongs to — bound onto the fighter when PlayerFighter builds it, because this is
    // asked for every blow and every heal. A fighter built some other way falls back to walking up its parents.
    // The shared identity that speaks for whoever is casting: a person's shell, or a creature's.
    static NetworkIdentity IdentityOfCaster(ICombatUnit caster)
    {
        if (caster is Character person) return IdentityOfFighter(person);
        if (caster is Health creature && creature != null)
        {
            var mobShell = NetMob.ShellForServerMob(creature.gameObject);
            return mobShell != null ? mobShell.netIdentity : null;
        }
        return null;
    }

    /// <summary>Server-only: the shell of the player playing <paramref name="fighter"/>, found through the fighter's own
    /// identity — one lookup, not a walk over every connection, because a kill asks it.</summary>
    public static NetPlayer ShellOfFighter(Character fighter)
    {
        var identity = IdentityOfFighter(fighter);
        return identity != null ? identity.GetComponent<NetPlayer>() : null;   // perf-ok: on a quest kill, never per frame
    }

    static NetworkIdentity IdentityOfFighter(Character fighter)
    {
        if (fighter == null) return null;
        if (fighter.ServerIdentity != null) return fighter.ServerIdentity;
        return fighter.transform.GetComponentInParent<NetworkIdentity>();
    }

    /// <summary>Server-only: mark a character as worth writing down soon — what every other change to them does,
    /// and what practice must do too or a crash costs everything learned since the last kill's loot. Silent with
    /// no world and for anybody nobody is playing.</summary>
    public static void SaveSoonFor(Character who)
    {
        if (!NetworkServer.active || who == null) return;
        var identity = IdentityOfFighter(who);
        var shell = identity != null ? identity.GetComponent<NetPlayer>() : null;
        if (shell != null) shell.SaveSoon();
    }

    /// <summary>Server-only: tell whoever is playing <paramref name="who"/> that something just arrived in their
    /// bag. True when it was sent — false when there is no world, or nobody is playing that character here, which
    /// is the caller's signal to say it on this machine instead.</summary>
    public static bool TellOwnerTheyGot(Character who, string itemId, int count, EquipmentInstance copy, Rarity rarity)
    {
        if (!NetworkServer.active || who == null || string.IsNullOrEmpty(itemId)) return false;
        var identity = IdentityOfFighter(who);
        var shell = identity != null ? identity.GetComponent<NetPlayer>() : null;
        if (shell == null || identity.connectionToClient == null) return false;
        shell.TellOwnerTheyGot(itemId, count, copy, rarity);
        return true;
    }

    /// <summary>MOVING A PERSON is the server's decision and their own machine's job. Walking and running are still
    /// theirs to do — so a skill that hauls them somewhere (a dash, a blink, a shove) is decided here, against the
    /// character this server holds, and then TOLD to the machine that owns the legs.
    ///
    /// <para>Returns TRUE when it was sent, which means the caller must not try to move anything itself. FALSE for a
    /// character that is not somebody's — the single-machine case, where the mover is right there.</para></summary>
    public static bool TellOwnerToMove(Character who, NetPlayer.Motion motion)
    {
        if (!NetworkServer.active || who == null) return false;
        var identity = IdentityOfFighter(who);
        var shell = identity != null ? identity.GetComponent<NetPlayer>() : null;
        if (shell == null || identity.connectionToClient == null) return false;

        // WHERE, IN THEIR OWN MAP. A server parks each map it holds somewhere of its own; a destination sent raw
        // would put them somewhere else entirely.
        motion.destination = MapManager.InMap(motion.destination, who.Map);
        motion.faceAt = MapManager.InMap(motion.faceAt, who.Map);
        shell.MoveOnOwnersMachine(identity.connectionToClient, motion);
        return true;
    }

    /// <summary>SERVER ONLY: tell the machine that owns <paramref name="who"/>'s legs that a taunt has locked them onto
    /// <paramref name="taunter"/>. False when there is no such machine to tell (no world, or nobody playing them).</summary>
    public static bool TellOwnerTaunted(Character who, Health taunter)
    {
        if (!NetworkServer.active || who == null || taunter == null) return false;
        var identity = IdentityOfFighter(who);
        var shell = identity != null ? identity.GetComponent<NetPlayer>() : null;
        if (shell == null || identity.connectionToClient == null) return false;
        var taunterIdentity = IdentityOfCaster(taunter);
        if (taunterIdentity == null) return false;
        shell.TauntOnOwnersMachine(identity.connectionToClient, taunterIdentity.netId);
        return true;
    }

    // A watching machine's half: draw the blow exactly as the server drew it, through the SAME function the server's
    // own intake draws with — so the picture is written once, and nothing about the blow is worked out here.
    /// <summary>How many blows this machine has been told about, and how many it could not find a victim for.
    /// A probe: "the number did not show" has two very different causes — the announcement never arrived, or it
    /// arrived and was dropped — and nothing else on this machine can tell them apart.</summary>
    public static int HitResultsHeard;
    public static int HitResultsWithNoVictim;

    static void OnHitResult(HitResult msg) => DrawMobHit(msg, msg.number);

    /// <summary>Draw one blow on a creature exactly as the server sent it. <paramref name="logNumber"/> is what the
    /// combat log writes for it, or −1 for a piece of a blow the log already wrote.</summary>
    public static void DrawMobHit(HitResult msg, int logNumber)
    {
        HitResultsHeard++;
        if (!NetworkClient.spawned.TryGetValue(msg.targetNetId, out var identity) || identity == null) return;

        var shell = identity.GetComponent<NetMob>();
        bool mine = shell != null && NetworkClient.localPlayer != null && NetworkClient.localPlayer.netId == msg.attackerNetId;
        // A CREATURE THIS MACHINE'S PLAYER JUST HIT — a miss does not count — is drawn through scenery for a while.
        if (mine && !msg.missed) shell.TradedABlowWithMe();
        // AND THE COMBAT LOG'S LINE for it — the server's own numbers, stored as they are.
        if (mine && logNumber >= 0) ChatController.CombatDealt(shell.ShownName, logNumber, msg.missed, msg.crit, msg.tickCause);
        var victim = (shell != null ? shell.WatchedTarget : identity.GetComponent<IDamageable>()) as Health;
        if (victim == null) { HitResultsWithNoVictim++; return; }

        victim.DrawShownBlow(new ShownBlow(msg.number, msg.elementModifier, msg.crit, msg.magic, msg.flash, msg.missed,
                                           msg.tickCause));

        // THE HURT STAGGER, fed the server's final number. It is the one thing a blow still starts on this machine,
        // because a stagger is a body stopping — movement — and each machine drives the bodies it draws.
        if (msg.flash && !msg.missed && msg.number > 0)
        {
            var status = victim.StatusHost;
            if (status != null) status.TookHit(msg.number);
        }

        // THE RISING YELLOW TOTAL, exactly as the server summed it. Keyed by WHO struck — the shared name every machine
        // agrees on — so two people flurrying one creature keep two climbing numbers instead of pooling into a third
        // that belongs to neither. It hangs over the watched copy the number was just drawn on.
        if (msg.total > 0) RepeatsDamageTotal.ReportArrived(victim.transform, msg.total, msg.attackerNetId);
    }

    /// <summary>The shared name for something both halves know about, or null when this machine is the authority,
    /// is offline, or the thing is purely local (a dummy a client made for itself, which no server has heard of).
    ///
    /// <para>ONE lookup, and it is a dictionary probe — every blow and every damage tick asks this, and PERFORMANCE.md
    /// bans a component search on that path. Anything networked registers itself in that table; there is deliberately
    /// no second, slower way to be found, because a fallback search runs on every MISS, which is exactly the common
    /// case of a creature hitting a player.</para></summary>
    /// <summary>A PLAYER'S MACHINE: say that a swing has just begun on <paramref name="target"/>. Returns whether the
    /// server was actually asked — false means there is nobody to ask (this machine is the authority, or the target is
    /// something no server has heard of), and the caller falls back to whatever it did before there was a wire.
    ///
    /// <para>Asked at the START of the swing rather than when the animation reaches the target, because the server
    /// decides when the blow lands and announces the swing to every watcher from the moment it accepts it. The
    /// message carries only what it is aimed at: the server holds the weapon, the numbers and the cadence.</para></summary>
    public static bool AskToSwingAt(IDamageable target)
    {
        var identity = SharedIdentityOf(target);
        if (identity == null) return false;
        NetworkClient.Send(new HitRequest { targetNetId = identity.netId });
        return true;
    }

    /// <summary>Tell the server this person is attacking <paramref name="target"/> while still out of reach. False when
    /// there is no server to tell.</summary>
    public static bool TellAttackIntent(IDamageable target)
    {
        var identity = SharedIdentityOf(target);
        if (identity == null) return false;
        NetworkClient.Send(new AttackIntentRequest { targetNetId = identity.netId });
        return true;
    }

    static NetworkIdentity SharedIdentityOf(IDamageable target)
    {
        if (NetworkServer.active) return null;                  // I AM the authority — apply it for real
        if (!NetworkClient.active || !NetworkClient.isConnected) return null;   // offline — unchanged game

        var comp = target as Component;
        if (comp == null) return null;
        var shell = NetMob.ShellForView(comp.gameObject);
        return shell != null ? shell.netIdentity : null;
    }

    // Said ONCE per connection, not once per swing: a player attacking holds the button down, and a line per
    // blow would bury the log it is trying to warn from.
    static readonly System.Collections.Generic.HashSet<int> warnedNoFighter = new System.Collections.Generic.HashSet<int>();

    // Once per reason per skill, so a flurry cannot bury it and a genuine repeat still shows.
    static readonly System.Collections.Generic.HashSet<string> saidWhyRefused = new System.Collections.Generic.HashSet<string>();

    /// <summary>The same thing for the door one step earlier — the CAST itself, before any strike exists.
    ///
    /// <para>Every way out of that door used to be a bare return, so a skill the server would not run was silent on
    /// every machine at once: nothing on the player's screen, nothing in any log, and the skill's own cooldown and
    /// mana already spent on their side. "It does nothing" was the only report a play-test could make, and it named
    /// none of the six different reasons it could have been. A WARNING rather than a category line, because the
    /// category is off by default and a refusal nobody can see is exactly the thing this ends.</para></summary>
    static void WhyCastRefused(string skillId, string why)
    {
        if (!SaidOnce("cast|" + skillId + "|" + why)) return;
        GameLog.Warn(LogCat.Server, "would not cast '" + Named(skillId) + "': " + why
                         + ". The skill did not run here, so nothing it does happened to anybody.");
    }

    // ONCE PER REASON PER SKILL. Said every time, a refused cast in a fight would fill the log with the same line
    // sixty times a second and bury everything else in it.
    static bool SaidOnce(string key) => saidWhyRefused.Add(key ?? "");

    static string Named(string skillId) => string.IsNullOrEmpty(skillId) ? "(unnamed)" : skillId;

    static void WarnNoFighter(int connectionId)
    {
        if (!warnedNoFighter.Add(connectionId)) return;
        GameLog.Warn(LogCat.Server, "connection " + connectionId + " swung, but this server has no fighter for "
                         + "them yet, so the blow was refused. Player stats are still tied to one global "
                         + "character; each connection needs its own before the server can resolve combat.");
    }

    // How many blows a single connection may have looked at in one second. This is a FLOOD GUARD on the wire, not
    // the rate a character actually swings at — it sits far above any real attack speed, so it can never slow a
    // legitimate fast build down, and it stops one machine from asking the server to resolve thousands of blows a
    // second. The real cadence belongs to the fighter, and lands with per-connection fighters.
    const int MaxHitsPerSecond = 20;

    // How much further than its true reach a blow may land before the server calls it impossible. A player sees
    // themselves where they were a moment ago, so a swing that looked in range on their screen can arrive at a
    // server that has since moved the target — refusing those would make a fair hit fail at a bad moment.
    const float ReachForgiveness = 1.5f;

    // How many CASTS AND ITEM USES a connection may send in one second — each one asks the server to run a whole
    // skill. The number is deliberately far above any real rate: it is only the ceiling that stops one machine
    // asking the server to run thousands a second.
    const int MaxSkillRequestsPerSecond = 200;

    class ClaimLedger { public float windowEnds; public int countedThisWindow; }

    static readonly System.Collections.Generic.Dictionary<int, ClaimLedger> swingLedgers =
        new System.Collections.Generic.Dictionary<int, ClaimLedger>();
    static readonly System.Collections.Generic.Dictionary<int, ClaimLedger> skillLedgers =
        new System.Collections.Generic.Dictionary<int, ClaimLedger>();

    // When each connection's next blow may land, by this machine's clock. Separate from the flood ledger above and
    // asking a different question: that one is "is this machine shouting", this one is "does this fighter's attack
    // speed allow a blow yet".
    static readonly System.Collections.Generic.Dictionary<int, float> swingDueAt =
        new System.Collections.Generic.Dictionary<int, float>();

    // How much of a beat a blow may arrive early and still be believed. DELIBERATELY LOOSE, and it is my call:
    // the swinger's own machine still times the animation, so an honest blow arrives a trip's jitter away from
    // when this machine expected it, and refusing an honest player is worse than missing a cheat. At half a beat
    // it still cuts the fastest a machine can possibly swing down to twice its real rate, from the twenty a
    // second the flood ceiling allows. Tighten it once the swing's timing lives here rather than on the client.
    const float SwingEarliness = 0.5f;

    // When each connection may next raise "about to swing" (OnBasicAttackStart). ONE gate for both roads that raise it —
    // an accepted swing and an attack intent — so a lunge fired while walking in is not fired again by the swing it
    // led to: once per swing interval, whichever road comes first.
    static readonly System.Collections.Generic.Dictionary<int, float> attackStartDueAt =
        new System.Collections.Generic.Dictionary<int, float>();

    static bool AttackStartIsDue(int connectionId, float swingTime)
    {
        float now = Time.time;
        if (attackStartDueAt.TryGetValue(connectionId, out float due) && now < due) return false;
        attackStartDueAt[connectionId] = now + swingTime * SwingEarliness;   // server-math: only the server's message handlers reach this
        return true;
    }

    // A person attacking a target still out of their reach: raise "about to swing" at it, so a lunge that answers that
    // moment can close the gap. Judged like a swing request — an enemy, in their map, the swinger able to act — but
    // nothing is struck here. The lunge's own range and its charges are asked by PassiveEvents, as for any moment.
    void OnAttackIntentRequest(NetworkConnectionToClient conn, AttackIntentRequest msg)
    {
        if (SwingingTooFast(conn.connectionId)) return;
        if (!NetworkServer.spawned.TryGetValue(msg.targetNetId, out var identity) || identity == null) return;
        var victim = VictimBehind(identity) as ICombatUnit;
        if (victim == null || victim.IsDead) return;
        var attacker = ShellFor(conn);
        var swinger = attacker != null && !attacker.Leaving && !attacker.AwayOnTeleport ? attacker.ServerCombatUnit : null;
        if (swinger == null || swinger.IsDead || victim.Map != swinger.Map) return;
        if (!Sides.Hostile(swinger, victim)) return;
        var fighter = swinger as Character;
        float swingTime = fighter != null ? fighter.SwingTime : 0f;
        if (swingTime <= 0f || !AttackStartIsDue(conn.connectionId, swingTime)) return;
        PassiveEvents.Fire(swinger, SkillTrigger.OnBasicAttackStart, null, victim.Body);
    }

    static bool SwingIsDue(int connectionId, float swingTime)
    {
        float now = Time.time;
        if (swingDueAt.TryGetValue(connectionId, out float due) && now < due)
        {
            // SAID, not dropped in silence: a blow that vanishes with nothing in the log looks exactly like a bug
            // in the swing itself, and the whole point of this guard is that it can be wrong about honest players.
            GameLog.Warn(LogCat.Server, "a blow from connection " + connectionId + " arrived "
                             + (due - now).ToString("0.00") + "s before that fighter's next swing was due — dropped.");
            return false;
        }

        swingDueAt[connectionId] = now + swingTime * SwingEarliness;
        return true;
    }
    static bool SwingingTooFast(int connectionId) => TooMany(swingLedgers, connectionId, MaxHitsPerSecond);
    static bool CastingTooFast(int connectionId) => TooMany(skillLedgers, connectionId, MaxSkillRequestsPerSecond);

    // One counter, used by every ledger — so a new kind of claim gets a flood guard by adding a ceiling and a
    // dictionary, not by writing the counting again and getting it subtly different.
    static bool TooMany<TKey>(System.Collections.Generic.Dictionary<TKey, ClaimLedger> ledgers, TKey key, int ceiling)
    {
        if (!ledgers.TryGetValue(key, out var ledger))
            ledgers[key] = ledger = new ClaimLedger();

        if (Time.time >= ledger.windowEnds) { ledger.windowEnds = Time.time + 1f; ledger.countedThisWindow = 0; }
        ledger.countedThisWindow++;
        return ledger.countedThisWindow > ceiling;
    }

    // Could this attacker actually touch that victim? Measured with the SHARED contact rule every other part of the
    // game measures with — a unit's full-size reach blended against how big the two bodies are — so a swing the
    // server allows is a swing the offline game would also have allowed.
    static bool WithinReach(ICombatUnit swinger, IDamageable victim)
    {
        var target = victim as ICombatUnit;
        if (swinger == null || swinger.Body == null || target == null || target.Body == null) return false;

        float reach = swinger.MeleeReachFullSize
                      * UnitScale.MeleeBlend(swinger.MeleeScale, target.MeleeScale)
                      + ReachForgiveness;
        return (target.Body.position - swinger.Body.position).sqrMagnitude <= reach * reach;
    }

    // A swing that has been accepted and is on its way to landing, held until its contact moment comes round.
    // A STRUCT in a plain list, walked once a frame: a swing is accepted about once a second per player, and a
    // coroutine each would be an object and a timer per blow, forever (PERFORMANCE.md).
    struct PendingBlow
    {
        public int connectionId;
        public uint targetNetId;
        public float landsAt;
        public float power;     // share of a full swing: 1 for the first hand, less for a pair weapon's other hand
        public EquipmentInstance weapon;   // the very copy in the hand when the swing began — another at the contact drops it
    }

    static readonly System.Collections.Generic.List<PendingBlow> pendingBlows =
        new System.Collections.Generic.List<PendingBlow>();

    /// <summary>The server's clock for swings in flight. Walked backwards so a landing can be removed in place
    /// without disturbing the ones still waiting.</summary>
    public override void Update()
    {
        base.Update();
        // The pieces of a multi-hit cascade, drawn as their turns come — a player's machine only.
        if (NetworkClient.active) BlowBatch.TickPlayback();
        if (!NetworkServer.active) return;
        // Everything in the air — arrows, thrown blades, bouncing skills — on the same server frame.
        Flights.Tick();
        // Strikes riding a dash, landing as the dash arrives.
        DashLandings.Tick();
        if (pendingBlows.Count == 0) return;

        // IN THE ORDER THEY CONNECT: a pair weapon's two hits can both fall due in one frame at a high attack speed,
        // and the first hand's must land first — it is the one that can kill, spend the charge, start the run.
        float now = Time.time;
        while (true)
        {
            int due = -1;
            for (int i = 0; i < pendingBlows.Count; i++)
                if (now >= pendingBlows[i].landsAt && (due < 0 || pendingBlows[i].landsAt < pendingBlows[due].landsAt)) due = i;
            if (due < 0) break;
            var p = pendingBlows[due];
            int last = pendingBlows.Count - 1;
            pendingBlows[due] = pendingBlows[last];
            pendingBlows.RemoveAt(last);
            if (NetworkServer.connections.TryGetValue(p.connectionId, out var conn) && conn != null)
                LandTheBlow(conn, p.targetNetId, p.power, p.weapon);
        }
    }

    // The server's side: ACCEPT a swing. It does not land here — see LandTheBlow, which this schedules.
    void OnHitRequest(NetworkConnectionToClient conn, HitRequest msg)
    {
        // NOTHING a client says is taken on trust — not what it is hitting, not how often, not from how far. A
        // request names a target and nothing else; every number and every permission is decided here.
        if (SwingingTooFast(conn.connectionId)) return;

        if (!NetworkServer.spawned.TryGetValue(msg.targetNetId, out var identity) || identity == null) return;
        if (VictimBehind(identity) == null) return;

        var attacker = ShellFor(conn);
        // Same refusal as FighterFor makes: somebody on their way out of this world does not act in it.
        if (attacker != null && attacker.AwayOnTeleport) return;   // away on a teleport: nothing of theirs runs
        var swinger = attacker != null && !attacker.Leaving ? attacker.ServerCombatUnit : null;
        if (swinger == null)
        {
            // NOT YET BUILT, and it must never fail quietly. The server has no fighter for this connection,
            // because a player's stats still live on a single global character wired to one on-screen avatar
            // — true of a client, false of a server holding several people at once. Until each connection has
            // its own, the server cannot roll anybody's blow and refuses rather than guessing whose stats to
            // use. See the migration plan: "per-connection combat units".
            WarnNoFighter(conn.connectionId);
            return;
        }

        // ONLY AN ENEMY. A creature fighting beside you, or one that fights nobody, is not somebody you may strike —
        // and that is this machine's answer, not the cursor's that asked.
        if (!Sides.Hostile(swinger, VictimBehind(identity) as ICombatUnit)) return;

        // A SWING NEEDS THE WEAPON IN HAND. Their machine waits for it already; one that did not gets the weapon taken
        // out instead of a blow, and asks again once it is in hand. Judged before the beat, so a refused ask costs none.
        var drawHost = swinger.StatusHost;
        if (drawHost != null && !drawHost.Draw.IsDrawn)
        {
            drawHost.RequestDraw();
            if (GameLog.On(LogCat.Hit)) GameLog.Info(LogCat.Hit, "no swing for connection " + conn.connectionId
                                                     + ": the weapon is not in hand (" + drawHost.Draw.Phase + ") — taking it out");
            return;
        }

        // THE BEAT IS THIS MACHINE'S, not the swinger's. Until now the only thing standing between a client and
        // twenty blows a second was the flood ceiling, which is set far above any real attack speed on purpose —
        // so a machine that sent a request the moment it was allowed to, instead of when its own animation reached
        // the target, simply swung faster. The real cadence was always meant to live here and was waiting on the
        // server holding a fighter per connection, which it now does.
        var fighter = swinger as Character;
        float swingTime = fighter != null ? fighter.SwingTime : 0f;
        if (swingTime > 0f && !SwingIsDue(conn.connectionId, swingTime)) return;

        var wornCopy = fighter != null ? fighter.GetEquippedInstance(EquipSlotType.Weapon) : null;
        var worn = wornCopy != null ? wornCopy.def : null;
        var weapon = worn != null ? worn.weaponType : WeaponType.None;

        // AND THE SWING IS SAID OUT LOUD. Nothing on the wire has ever said that a person's body did anything, so
        // until now everybody else watched their damage numbers climb over a character standing perfectly still.
        // The pose is chosen from the weapon THIS machine holds for them, never from what their machine claims.
        bool ranged = fighter != null && fighter.SwingsRanged;
        if (attacker != null) attacker.BeganAction(WeaponStances.AttackFor(weapon, ranged), swingTime);

        // ABOUT TO SWING — RAISED HERE, WHERE THE SWING ACTUALLY BEGINS. It used to be raised down at the contact
        // moment instead, alongside "a swing landed", which put two moments that mean different things at the same
        // instant and left the earlier one meaning nothing. The swing starts here and connects a fraction of a
        // second later, and those are the two instants the two moments belong to.
        //
        // ONCE PER ACCEPTED SWING, on both roads out of this method — the melee blow scheduled below and a bow's,
        // which lands straight away — so neither can raise it twice and neither can skip it.
        var announceTo = VictimBehind(identity);
        // Not again if an attack intent already raised it this swing interval (AttackStartIsDue).
        if (swingTime <= 0f || AttackStartIsDue(conn.connectionId, swingTime))
            PassiveEvents.Fire(swinger, SkillTrigger.OnBasicAttackStart, null,
                               announceTo is ICombatUnit hit ? hit.Body : identity.transform);

        // WHEN IT LANDS IS THIS MACHINE'S TOO. A swing was asked for at the moment it began, and it connects part
        // way through — the same fraction the swinger's own animation reaches, because both read the one number in
        // the shared character spec. Until this, the swinger's machine sent the request AT its contact frame, which
        // handed it the say over when inside its own swing a blow landed.
        //
        // A RANGED ATTACK FLIES — an arrow, or a thrown shuriken that bounces and comes back — and lands when it
        // arrives, in the ONE flight store every unit's ranged attack goes through (Flights). Its reach and its map are
        // judged HERE, at the throw: once something is in the air it homes onto its target, so a check left to the
        // landing would be the only thing standing between a forged request and a blade thrown across the world.
        if (ranged)
        {
            var shotAt = announceTo as ICombatUnit;
            if (shotAt == null || shotAt.Map != swinger.Map || !WithinReach(swinger, announceTo)) return;
            Flights.ThrowBasic(swinger, shotAt, conn.connectionId);
            return;
        }

        if (swingTime > 0f)
        {
            // A PAIR WEAPON LANDS TWICE — the other hand's hit later in the same swing, at its own share of the power,
            // each a whole hit of its own when it lands. Both moments are fractions of the swing, so attack speed
            // brings both sooner.
            var spec = CharacterSpec.Active;
            int contacts = SwingContacts.Of(weapon, spec.hitTiming, spec.offHandHitTiming, spec.offHandPower,
                                            out float firstAt, out float secondAt, out float secondPower);
            pendingBlows.Add(new PendingBlow
            {
                connectionId = conn.connectionId,
                targetNetId = msg.targetNetId,
                landsAt = Time.time + swingTime * firstAt,
                power = 1f,
                weapon = wornCopy
            });
            if (contacts == 2)
                pendingBlows.Add(new PendingBlow
                {
                    connectionId = conn.connectionId,
                    targetNetId = msg.targetNetId,
                    landsAt = Time.time + swingTime * secondAt,
                    power = secondPower,
                    weapon = wornCopy
                });
            return;
        }

        LandTheBlow(conn, msg.targetNetId, 1f, wornCopy);
    }

    /// <summary>A bow or a firearm — the two weapons whose blow travels rather than connecting. Read from the same
    /// weapon type the swinger's own machine reads, so the two can never disagree about which is which.</summary>

    // Whatever actually takes the damage behind a shell: a creature's real body on this machine, or the thing
    // itself. One reading of it, because both the accepting half and the landing half need the same answer.
    static IDamageable VictimBehind(NetworkIdentity identity)
    {
        if (identity == null) return null;
        var shell = identity.GetComponent<NetMob>();
        return shell != null && shell.ServerMob != null
            ? shell.ServerMob.GetComponent<IDamageable>()
            : identity.GetComponent<IDamageable>();
    }

    // The contact moment itself, on the server's clock. EVERYTHING here is re-asked rather than remembered from
    // when the swing was accepted: a creature can die, be recycled or walk out of reach during a swing, and a
    // fighter can be gone entirely if their player left mid-blow.
    void LandTheBlow(NetworkConnectionToClient conn, uint targetNetId, float power, EquipmentInstance weaponAtStart)
    {
        if (!NetworkServer.spawned.TryGetValue(targetNetId, out var identity) || identity == null) return;

        var victim = VictimBehind(identity);
        if (victim == null) return;
        // A TARGET ALREADY DEAD IS NOT HIT AGAIN. A pair weapon's first hand can kill, and its second hit is then
        // due on a body that is already down; a creature's own swing has always refused this, now a person's does too.
        if (victim is ICombatUnit fallen && fallen.IsDead) return;

        var attacker = ShellFor(conn);
        // Same refusal as FighterFor makes: somebody on their way out of this world does not act in it.
        var swinger = attacker != null && !attacker.Leaving ? attacker.ServerCombatUnit : null;
        if (swinger == null) return;
        // Asked again at the contact: the victim may have stopped being an enemy during the swing.
        if (!Sides.Hostile(swinger, victim as ICombatUnit)) return;

        // AND THE DEAD DO NOT SWING. A blow is scheduled when the swing begins and lands a fraction of a second
        // later, so a person killed inside their own wind-up was still landing it — and nothing here refused a
        // request from somebody this machine already knew was dead, which left that entirely to the honesty of
        // their own machine. The skill door has always refused a dead caster; this is the same refusal, on the
        // one moment every basic attack passes through.
        if (swinger.IsDead) return;

        // THE SWING BELONGS TO THE WEAPON THAT STARTED IT. One swapped mid-swing drops what is still on its way, so a
        // pair weapon's second hit can never land from a sword drawn in between.
        var holder = swinger as Character;
        if (holder != null && holder.GetEquippedInstance(EquipSlotType.Weapon) != weaponAtStart) return;

        // Reach is judged HERE, at the contact moment, exactly as it was when the swinger's own machine chose that
        // moment — so a target that walked out during the swing is missed the same way it always was.
        if (!WithinReach(swinger, victim)) return;

        // THE SERVER RUNS THE SWING ITSELF — through the ONE implementation of what a swing is, the same one a
        // skill strike authored as a basic attack goes through. It used to roll its own crit and damage inline
        // here, which was a second copy of the swing: it silently skipped everything else a swing does, so a run
        // of hits was never counted, a boost charge was never spent, the impact cue never played, and — the reason
        // this was found — the two moments a swing raises, "about to swing" and "a swing landed", fired on NOBODY.
        // Every passive keyed to either of them was dead in a shared world.
        //
        // THE INTENT IS NOT ANNOUNCED HERE. It belongs to the moment the swing STARTED, which is where this machine
        // now raises it (see OnHitRequest) — raising it again at the contact moment would be the same swing
        // announcing itself twice, and would put "about to swing" and "a swing landed" at one instant.
        //
        // EVERYBODY WATCHING IS TOLD by the victim itself, at the moment it draws the blow (Health.Show for a creature,
        // the damage door for a person) — so there is no second announcement to send from here.
        BasicSwing.Land(swinger, victim as ICombatUnit, ScaledNumber.Of(power), StackVars.None,
                        outer: 1f, overkill: false, announceIntent: false, out _);
    }

    // How far a skill may reach before the server calls it impossible. Deliberately generous: skills range from a
    // touch to a bolt across a clearing and the server does not yet know which is which, so this is only there to
    // stop a blow aimed at something on the far side of the map. It is NOT a skill's range — the skill's own range
    // is checked on the machine that ran it, and moving that check here is part of resolving skills server-side.
    const float SkillReachCeiling = 30f;

    // HOW MUCH FURTHER THAN ITS RANGE A CAST MAY BE AND STILL BE BELIEVED. This machine is told where a person is
    // ten times a second, so its copy of both bodies can be a tenth of a second old — at a run that is a little
    // over half a unit of drift between them, before any hitch on either side. Two units is several times that and
    // still fifteen times tighter than the ceiling above, so an honest cast fired right on the line is never
    // refused and a claim from across the clearing still is. Forgiving on purpose, exactly as the movement check is.
    const float SkillRangeSlack = 2f;

    /// <summary>TRUE when the world this machine is playing in belongs to somebody else — a joined client. Nothing
    /// here decides anything then: not a blow, not a buff, not a charge.
    ///
    /// <para>Asked as well as, not instead of, <see cref="Character.IsWatchedCopy"/>: that latch is set the first
    /// time the shell speaks, and a skill cast in the frames before it would otherwise have been resolved twice —
    /// once here and once, properly, on the server.</para></summary>
    public static bool TheWorldIsElsewhere =>
        !NetworkServer.active && NetworkClient.active && NetworkClient.isConnected;

    /// <summary>Send a whole cast to the server. Returns TRUE when it went, which means the caller must not resolve
    /// a single thing itself — the picture of the cast is all this machine is entitled to.
    ///
    /// <para>FALSE on the machine that owns the world (it simply casts), and false before anyone has joined.</para></summary>
    public static bool SendCastToServer(SkillDef def, Transform aimedAt, Vector3 aimPoint)
    {
        if (def == null) return false;
        if (NetworkServer.active) return false;
        if (!NetworkClient.active || !NetworkClient.isConnected) return false;

        NetworkClient.Send(new SkillCastRequest
        {
            skillId = def.id,
            targetNetId = IdentityOfAimed(aimedAt),
            // WHERE THEY AIMED, INSIDE THEIR OWN MAP. A server parks each map it holds somewhere of its own, so a
            // raw world position taken here would land somewhere else entirely over there.
            aimPoint = MapManager.InMap(aimPoint, MapManager.CurrentMap)
        });
        return true;
    }

    // What the caster picked, as the shared thing that speaks for it. A creature answers through its shell, a
    // person through theirs; anything else was never shared and is named as nothing.
    static uint IdentityOfAimed(Transform aimed)
    {
        if (aimed == null) return 0u;
        var shell = NetMob.ShellForView(aimed.gameObject);
        if (shell != null) return shell.netId;
        var person = aimed.GetComponentInParent<NetPlayer>();
        return person != null ? person.netId : 0u;
    }

    // How far a cast may be aimed from the caster before the server calls it impossible — the same generous ceiling
    // a named strike already answers to, read from the same constant so the two cannot drift apart.
    // EVERY PRESS IS ANSWERED, whichever way it went, so the pressing machine — which starts no cooldown of its own —
    // knows when it may send the next one.
    void OnSkillCastRequest(NetworkConnectionToClient conn, SkillCastRequest msg)
    {
        bool cast = RunCastRequest(conn, msg);
        conn.Send(new CastAnswered { skillId = msg.skillId, refused = !cast });
    }

    bool RunCastRequest(NetworkConnectionToClient conn, SkillCastRequest msg)
    {
        // EVERY REFUSAL BELOW SAYS SO, exactly as the strike door above already does. These were six bare returns,
        // and a cast stopped by any of them was invisible everywhere: the player's own machine had already spent the
        // mana and started the cooldown, their screen showed the skill doing nothing, and no log anywhere said which
        // of the six it was. Silence has no stack trace.
        if (CastingTooFast(conn.connectionId))
        { WhyCastRefused(msg.skillId, "asked for faster than the ceiling allows"); return false; }

        var def = SkillCatalog.Get(msg.skillId);
        if (def == null) { WhyCastRefused(msg.skillId, "this server has no such skill in its catalog"); return false; }
        // a passive has no pressed form: it fires from its own moments, here
        if (def.IsPassive) { WhyCastRefused(msg.skillId, "it is a passive — it has no pressed form"); return false; }

        var caster = FighterFor(conn) as Character;
        if (caster == null) { WarnNoFighter(conn.connectionId); return false; }
        var castShell = ShellFor(conn);
        if (castShell != null && castShell.AwayOnTeleport) { WhyCastRefused(msg.skillId, "they are away on a teleport"); return false; }
        // A WEAPON SKILL NEEDS THE WEAPON IN HAND — the same rule a swing obeys, the same answer: take it out.
        var drawHost = caster.StatusHost;
        if (def.weaponType != WeaponType.None && drawHost != null && !drawHost.Draw.IsDrawn)
        {
            drawHost.RequestDraw();
            WhyCastRefused(msg.skillId, "the weapon is not in hand (" + drawHost.Draw.Phase + ") — taking it out");
            return false;
        }
        if (!Owns(caster, msg.skillId))
        {
            WarnNotTheirSkill(conn.connectionId, msg.skillId, WhyNotTheirs(caster, msg.skillId));
            // AND THE PLAYER IS TOLD, in their own chat, because from their side a refused press looks exactly like
            // a broken key: their screen animated, and nothing happened.
            ShellFor(conn)?.TellOwner(caster.WhyNotUsable(msg.skillId)
                                      ?? "That skill is not on the bar this server holds for you.");
            return false;
        }

        var runner = PlayerFighter.RunnerFor(caster, msg.skillId);
        if (runner == null)
        { WhyCastRefused(msg.skillId, "this server built them no runner for it — it is not among the skills they can use"); return false; }

        // WHERE THEY AIMED, put back into this machine's copy of their map.
        Vector3 point = MapManager.InWorld(msg.aimPoint, caster.Map);

        var casterBody = caster.Body;
        if (casterBody == null)
        { WhyCastRefused(msg.skillId, "this server has no body bound for that character"); return false; }
        if ((point - casterBody.position).sqrMagnitude > SkillReachCeiling * SkillReachCeiling)
        {
            WhyCastRefused(msg.skillId, "they aimed " + Mathf.RoundToInt((point - casterBody.position).magnitude)
                             + " units away, past the ceiling of " + SkillReachCeiling
                             + " — usually the map this server has them in disagreeing with the one they are in");
            return false;
        }

        Transform aimed = null;
        if (msg.targetNetId != 0u && NetworkServer.spawned.TryGetValue(msg.targetNetId, out var identity) && identity != null)
        {
            var shell = identity.GetComponent<NetMob>();
            if (shell != null && shell.ServerMob != null) aimed = shell.ServerMob.transform;
            else
            {
                var person = identity.GetComponent<NetPlayer>();
                var unit = person != null ? person.ServerCombatUnit : null;
                aimed = unit != null ? unit.Body : null;
            }
        }

        // AND NOW THE SKILL'S OWN RANGE, which nothing on this machine used to ask. The ceiling above is one
        // generous number for every skill in the game, so a cast authored to reach eight units was accepted from
        // twenty-nine — the real range was checked only on the machine that asked, which is the machine with the
        // most reason to lie about it.
        //
        // MEASURED AGAINST WHAT THE CAST NAMED: the picked unit when there is one, otherwise the aimed spot, which
        // the aiming reticle already keeps inside the same range.
        if (def.castRange > 0f)
        {
            Vector3 at = aimed != null ? aimed.position : point;
            Vector3 gap = at - casterBody.position; gap.y = 0f;
            float allowed = def.castRange + SkillRangeSlack;
            if (gap.sqrMagnitude > allowed * allowed)
            {
                WhyCastRefused(msg.skillId, "they cast it from " + gap.magnitude.ToString("0.#")
                                 + " units away and it reaches " + def.castRange);
                return false;
            }
        }

        // The cast itself announces each of its plays as they start (see AnnounceCastPlayed) — including its
        // repeats, so a shower's later falls reach every screen too, rather than only its first.
        //
        // AND WHEN THE RUNNER ITSELF SAYS NO, IT SAYS WHY. Its own gates — silenced, stunned, on cooldown, no mana,
        // a pile it spends that is not there — were only ever written to a log category that is off by default, on
        // the one machine nobody is watching. The reason comes back here and is said out loud with the rest.
        if (!runner.CastFromRequest(aimed, point, out string why)) { WhyCastRefused(msg.skillId, why); return false; }
        return true;
    }

    /// <summary>Tell the server an item was used, so the skill it names is run there like every other skill.
    /// Returns TRUE when it went, which means the caller must not run anything itself.</summary>
    /// <summary>The body the server spawned for the person at THIS keyboard, or null when there is no world —
    /// which is the same question as "is anything I do to my bag the server's to decide". One place asks it, so
    /// nothing has to work out on its own what being joined means.</summary>
    public static NetPlayer MyPlayer
        => NetworkClient.active && NetworkClient.localPlayer != null
            ? NetworkClient.localPlayer.GetComponent<NetPlayer>()
            : null;

    public static bool SendItemUsedToServer(int bagIndex, string expectedItemId)
    {
        if (bagIndex < 0) return false;
        if (NetworkServer.active) return false;
        if (!NetworkClient.active || !NetworkClient.isConnected) return false;
        NetworkClient.Send(new ItemUsedRequest { bagIndex = bagIndex, expectedItemId = expectedItemId });
        return true;
    }

    void OnItemUsedRequest(NetworkConnectionToClient conn, ItemUsedRequest msg)
    {
        // The same ceiling a cast answers to: an item's effect IS a cast, and a machine sending them as fast as the
        // wire allows is the thing this guards against, whatever it calls them.
        if (CastingTooFast(conn.connectionId)) return;

        var caster = FighterFor(conn) as Character;
        if (caster == null) { WarnNoFighter(conn.connectionId); return; }

        // WHAT IS ACTUALLY THERE, AND STILL THE ONE THEY MEANT. The message says where in the bag, and the item is
        // read off the bag the SERVER holds — so a request to drink something the person does not own resolves to
        // nothing. The same check every other bag request answers to.
        var shell = ShellFor(conn);
        // ON THEIR WAY OUT, nothing they send is acted on — the save handing them over has already been taken.
        if (shell != null && shell.Leaving) return;
        if (!BagStack.StillTheSame(caster, msg.bagIndex, msg.expectedItemId,
                                   shell != null ? shell.playerName : "connection " + conn.connectionId)) return;
        UseItemOnServer(caster, shell, msg.bagIndex);
    }

    /// <summary>SERVER: use the item at <paramref name="bagIndex"/> of <paramref name="caster"/>'s bag — run its skill
    /// and spend one. The one body behind a press of an item (above) and auto-combat's drink (NetPlayer), so a potion
    /// drunk either way costs and does exactly the same. The caller has already checked the index holds what it meant.
    /// TRUE when the item was used and spent.</summary>
    public static bool UseItemOnServer(Character caster, NetPlayer shell, int bagIndex)
    {
        if (caster == null || bagIndex < 0 || bagIndex >= caster.Bag.Count) return false;
        var entryInBag = caster.Bag[bagIndex];
        var item = entryInBag != null ? entryInBag.def : null;
        if (item == null || !item.IsUsable) return false;

        // THE ITEM NAMES THE SKILL, and the server reads that off the item it holds — never off the message.
        var runner = PlayerFighter.RunnerFor(caster, item.useSkillId);
        var entry = runner != null && runner.def != null ? runner.def.ItemUseEntry : null;
        if (entry == null) return false;

        // AN EFFECT THAT HAS NOWHERE TO HAPPEN IS REFUSED BEFORE IT COSTS ANYTHING — a Fly Wing on ground the map has
        // no record of stays in the bag, and the player is told why. A landing spot found here is the one the effect
        // uses: it is rolled once.
        string cannot = SkillEffectApplier.WhyItCannotLand(runner.def, caster);
        if (cannot != null) { shell?.TellOwner(cannot); return false; }

        // AN EFFECT THAT DID NOT RUN COSTS NOTHING. The drinker died between the press and its arrival, or the
        // skill has nothing to play: the potion stays in the bag rather than vanishing for no effect.
        if (!runner.FireAsPassive(entry, null)) { SkillEffectApplier.ForgetLanding(caster); return false; }

        // AND IT IS SPENT HERE, where it was drunk. The bag belongs to the server now, so a stack reduced only on
        // the drinker's own machine would be put straight back the next time the server said what they were
        // carrying — a potion that heals you and is never used up.
        // THE SAME STACK, found again: the skill that just ran can have changed the bag (rarely — but an index held
        // across it would then spend somebody else's stack).
        int at = caster.Bag.IndexOf(entryInBag);
        if (at >= 0 && --entryInBag.count <= 0) caster.Bag.RemoveAt(at);
        if (GameLog.On(LogCat.Server))
            GameLog.Info(LogCat.Server, string.Concat(SpawnIds.MapOf(caster), " ", SpawnIds.Label(caster), " used ",
                                                      item.displayName, CombatAuthority.HealthLeft(caster), " — ",
                                                      Mathf.Max(0, entryInBag.count).ToString(), " left"));

        shell?.PushBagToOwner();
        // A drunk potion is a change like any other. Without this it was written down only if something ELSE
        // happened to dirty the character, so a server restart handed the potions back.
        shell?.SaveSoon();
        return true;
    }

    /// <summary>Say that one play of a cast is starting NOW, to every machine that can see the caster — the caster's
    /// own included. It carries the seed, which is the point of it: where a scattered shot lands is where its damage
    /// is measured from, so the picture and the decision must be rolled from the same number.
    ///
    /// <para>Sent per PLAY rather than per cast, because a repeating skill is several plays and each one scatters
    /// again. Silent off a server, so the single-machine game is untouched.</para></summary>
    public static void AnnounceCastPlayed(ICombatUnit caster, SkillDef def, Transform aimedAt, Vector3 aimPoint, int seed)
    {
        if (!NetworkServer.active || PlayerUnits.Gone(caster) || def == null) return;
        var identity = IdentityOfCaster(caster);
        if (identity == null) return;

        NetworkServer.SendToReadyObservers(identity, new SkillCastPlayed
        {
            casterNetId = identity.netId,
            skillId = def.id,
            targetNetId = IdentityOfServerSide(aimedAt),
            aimPoint = MapManager.InMap(aimPoint, caster.Map),
            seed = seed
        });
    }

    /// <summary>Call out a skill as it fires, to everybody who can see the caster AND the caster themselves — in the
    /// words <see cref="SkillShout"/> decides here, so every screen shows the same call. <paramref name="efficient"/>
    /// is this cast's efficiency, rolled once at its release. Silent off a server, for anybody gone, and when the
    /// shout rule says nothing.</summary>
    public static void AnnounceSkillCall(ICombatUnit caster, SkillDef def, bool efficient)
    {
        if (!NetworkServer.active || PlayerUnits.Gone(caster) || def == null) return;
        var identity = IdentityOfCaster(caster);
        if (identity == null) return;
        // A PERSON is named beside their call; a creature's call is only a bubble over its head, so it carries none.
        var shell = identity.GetComponent<NetPlayer>();
        if (shell == null && identity.GetComponent<NetMob>() == null) return;

        // A person or a shadow may make the full style call; an ordinary creature carries no style of its own.
        bool mayCallStyle = shell != null || (caster is Health beast && beast.kind == CreatureKind.Shadow);
        string text = SkillShout.Words(def, efficient, mayCallStyle);
        if (text == null) return;

        NetworkServer.SendToReadyObservers(identity, new ChatHeard
        {
            speakerNetId = identity.netId,
            speaker = shell != null ? shell.playerName : "",
            text = text,
            isSkillCall = true,
        }, includeOwner: true);
    }

    /// <summary>Say that a cast is WINDING UP, to everybody who can see the caster — so the bar and the skill's
    /// name appear over their head there too. Not to the caster's own machine, which draws its bar from the
    /// wind-up it is running itself. Silent off a server, and silent for a skill with no wind-up to show.</summary>
    public static void AnnounceCastWindup(ICombatUnit caster, SkillDef def, float seconds, Vector3 aimPoint)
    {
        if (!NetworkServer.active || PlayerUnits.Gone(caster) || def == null || seconds <= 0f) return;
        var identity = IdentityOfCaster(caster);
        if (identity == null) return;

        NetworkServer.SendToReadyObservers(identity, new CastWindupStarted
        {
            casterNetId = identity.netId,
            skillId = def.id,
            startedAt = NetworkTime.time,
            seconds = seconds,
            aimPoint = MapManager.InMap(aimPoint, caster.Map),
        }, includeOwner: false);
    }

    /// <summary>Say that a wind-up is over — finished or broken. The pair of the one above, sent from the same
    /// side to the same people, so a bar cannot be left standing on somebody else's screen.</summary>
    public static void AnnounceCastWindupEnded(ICombatUnit caster)
    {
        if (!NetworkServer.active || PlayerUnits.Gone(caster)) return;
        var identity = IdentityOfCaster(caster);
        if (identity == null) return;

        NetworkServer.SendToReadyObservers(identity, new CastWindupEnded
        {
            casterNetId = identity.netId,
        }, includeOwner: false);
    }

    // Somebody else starting to cast, arriving as a thing to LOOK at. Their shell holds the wind-up and draws the
    // bar over their body each frame; nothing here decides anything.
    static void OnCastWindupStarted(CastWindupStarted msg)
    {
        if (!NetworkClient.spawned.TryGetValue(msg.casterNetId, out var identity) || identity == null) return;
        var def = SkillCatalog.Get(msg.skillId);
        var shell = identity.GetComponent<NetPlayer>();
        if (shell != null) { shell.ShowCastWindup(def, msg.startedAt, msg.seconds, msg.aimPoint); return; }
        var creature = identity.GetComponent<NetMob>();
        if (creature != null) creature.ShowCastWindup(def, msg.startedAt, msg.seconds, msg.aimPoint);
    }

    static void OnCastWindupEnded(CastWindupEnded msg)
    {
        if (!NetworkClient.spawned.TryGetValue(msg.casterNetId, out var identity) || identity == null) return;
        var shell = identity.GetComponent<NetPlayer>();
        if (shell != null) { shell.StopCastWindup(); return; }
        var creature = identity.GetComponent<NetMob>();
        if (creature != null) creature.StopCastWindup();
    }

    // ---- cues: every visual reaction in the game, on one road -------------------------------------------

    /// <summary>Say that a cue just played on a unit, to every machine watching it. Returns the shared name of the
    /// unit it was announced on, or 0 when nothing was said — off a server, or for a body no client has ever been
    /// told about. <see cref="VfxCue.Begin"/> keeps that name so the aura's ENDING can be sent to the same people.
    ///
    /// <para>Called from the one door every cue in the game passes through, so nothing has to remember to do this
    /// and nothing can be added later that forgets to.</para></summary>
    public static uint AnnounceCue(Transform actor, Transform target, VfxCueId cue, int auraKey)
    {
        if (!NetworkServer.active || actor == null) return 0u;
        var identity = SharedIdentityOnServer(actor);
        if (identity == null) return 0u;

        var aimed = target != null && target != actor ? SharedIdentityOnServer(target) : null;

        NetworkServer.SendToReadyObservers(identity, new CuePlayed
        {
            onNetId = identity.netId,
            targetNetId = aimed != null ? aimed.netId : 0u,
            cue = cue,
            auraKey = auraKey,
        });
        return identity.netId;
    }

    /// <summary>SERVER: say one leg of a flight to everybody who can see <paramref name="thrower"/> — the one announce
    /// for every arrow, blade and bounce in the game, so a flight cannot look one way when a person throws it and
    /// another when a creature does. <paramref name="to"/> is the thrower itself for the flight home.</summary>
    public static void AnnounceFlightLeg(Transform thrower, ushort blade, Transform to, Vector3 toPointInMap,
                                         double departs, double arrives, WeaponType thrown, string skillId, int binding,
                                         string map)
    {
        if (!NetworkServer.active || thrower == null) return;
        var identity = SharedIdentityOnServer(thrower);
        if (identity == null) return;

        var at = to == thrower ? identity : to != null ? SharedIdentityOnServer(to) : null;
        NetworkServer.SendToReadyObservers(identity, new FlightLeg
        {
            throwerNetId = identity.netId,
            blade = blade,
            toNetId = at != null ? at.netId : 0u,
            toPoint = toPointInMap,
            departs = departs,
            arrives = arrives,
            thrown = thrown,
            skillId = skillId ?? "",
            binding = (short)binding,
            map = map ?? "",
        });
    }

    /// <summary>SERVER: is the person on this connection on their way out of this world — a door to a map another
    /// process holds, mid-save? Anything that lands for them now happens to a copy about to be thrown away.</summary>
    public static bool IsLeaving(int connectionId)
    {
        if (!NetworkServer.connections.TryGetValue(connectionId, out var conn) || conn == null) return true;
        var shell = ShellFor(conn);
        return shell == null || shell.Leaving;
    }

    /// <summary>Say that a lasting cue is over, to the same people who were told it began.</summary>
    public static void AnnounceCueStopped(uint onNetId, int auraKey)
    {
        if (!NetworkServer.active || onNetId == 0u || auraKey == 0) return;
        if (!NetworkServer.spawned.TryGetValue(onNetId, out var identity) || identity == null) return;

        NetworkServer.SendToReadyObservers(identity, new CueStopped { onNetId = onNetId, auraKey = auraKey });
    }

    /// <summary>THE SHARED THING THAT SPEAKS FOR A BODY, asked on the SERVER. A creature answers through the shell
    /// filed under it — a creature is NOT a child of its shell, so walking up the parents finds nothing — and a
    /// person answers through the shell their fighter's body hangs on.
    ///
    /// <para>perf-ok: the creature lookup is one dictionary read, and the person's identity sits on the very object
    /// asked about (their fighter's body IS the shell) or one parent above it (their status host). Neither searches
    /// the scene and neither allocates, which is what the per-hit path bans.</para></summary>
    static NetworkIdentity SharedIdentityOnServer(Transform body)
    {
        if (body == null) return null;
        var shell = NetMob.ShellForServerMob(body.gameObject);
        if (shell != null) return shell.netIdentity;
        return body.GetComponentInParent<NetworkIdentity>();
    }

    /// <summary>THE BODY THIS MACHINE ACTUALLY DRAWS for a unit — the copy built for a creature, the copy built for
    /// another person, and your own real avatar for yourself. Every announced picture is played on the answer to
    /// this, so one question is asked of every kind of unit instead of one per kind.</summary>
    static Transform DrawnBodyOf(NetworkIdentity identity)
    {
        if (identity == null) return null;
        var mob = identity.GetComponent<NetMob>();
        if (mob != null) return mob.DrawnBody;
        var person = identity.GetComponent<NetPlayer>();
        return person != null ? person.DrawnBody : null;
    }

    // Somebody else's cue — or your own, coming back from the machine that decided it. Nothing here decides
    // anything: it is a picture played on a body, and every consequence of the event behind it arrived separately.
    static void OnCuePlayed(CuePlayed msg)
    {
        if (!NetworkClient.spawned.TryGetValue(msg.onNetId, out var identity) || identity == null) return;
        Transform on = DrawnBodyOf(identity);
        if (on == null) return;

        Transform at = null;
        if (msg.targetNetId != 0u && NetworkClient.spawned.TryGetValue(msg.targetNetId, out var other) && other != null)
            at = DrawnBodyOf(other);

        VfxCue.PlayFromServer(msg.cue, on, at, msg.auraKey, msg.onNetId);
        // No word over a person away on a teleport, exactly as no effect is drawn on them.
        if (!TeleportLook.Covers(on)) CueWords.Show(msg.cue, on, identity.TryGetComponent<NetMob>(out _));
    }

    static void OnCueStopped(CueStopped msg) => VfxCue.StopFromServer(msg.onNetId, msg.auraKey);

    // ONE LEG OF A FLIGHT. Drawn from wherever that flight's picture is now (the thrower's hand for its first leg) to
    // whatever the leg goes to, arriving at the moment the server said — on every machine that can see the thrower,
    // the thrower's own included. Nothing is decided here.
    static void OnFlightLeg(FlightLeg msg)
    {
        if (!NetworkClient.spawned.TryGetValue(msg.throwerNetId, out var identity) || identity == null) return;
        Transform thrower = DrawnBodyOf(identity);
        if (thrower == null) return;

        Transform at = null;
        if (msg.toNetId == msg.throwerNetId) at = thrower;
        else if (msg.toNetId != 0u && NetworkClient.spawned.TryGetValue(msg.toNetId, out var other) && other != null)
            at = DrawnBodyOf(other);

        var look = Flights.LookOf(msg.thrown, msg.skillId, msg.binding, out var effectId);
        FlightPictures.Leg(msg.throwerNetId, msg.blade, thrower, at, MapManager.InWorld(msg.toPoint, msg.map),
                           msg.departs, msg.arrives, look, effectId, at == thrower);
    }

    // ---- stances: what a person's body is doing, said by the machine that owns their legs ----------------

    /// <summary>Tell the world this body has begun a ONE-SHOT action pose — a skill's release swing. It plays once
    /// and is over, so it travels as a moment: what, and when it began.</summary>
    public static void ReportActionBegan(StanceId id, float seconds)
    {
        var me = MyPlayer;
        if (me != null) me.CmdActionBegan(id, seconds);
    }

    /// <summary>Tell the world which pose this body is HOLDING while it does something that takes time — a jump, a
    /// dodge, a flinch, a death. Sent on the change, and "nothing" is a change like any other.</summary>
    public static void ReportDrivenStance(bool has, StanceId id, float window, bool release, int beat)
    {
        var me = MyPlayer;
        if (me != null) me.CmdDrivenStance(has, id, window, release, beat);
    }

    /// <summary>Tell the world which standing pose is underneath all that — the guard, the cast, the seat.</summary>
    public static void ReportHeldStance(bool has, StanceId id, float window)
    {
        var me = MyPlayer;
        if (me != null) me.CmdHeldStance(has, id, window);
    }

    /// <summary>Tell the world this body has begun a scripted hop — how tall and how long. Said once, at the launch,
    /// because the lift is a curve every machine can draw for itself rather than a stream of heights.</summary>
    public static void ReportHopBegan(float peak, float seconds)
    {
        var me = MyPlayer;
        if (me != null) me.CmdHopBegan(peak, seconds);
    }

    /// <summary>…and that it is over — at the touchdown, which is the real end, rather than when the arithmetic says
    /// it should have been. A hop cut short by a ceiling ends on every screen when it ends here.</summary>
    public static void ReportHopEnded()
    {
        var me = MyPlayer;
        if (me != null) me.CmdHopEnded();
    }

    /// <summary>Tell the world this body has begun a scripted move — a glide or a blink (<see cref="MoveSaid"/>). Said
    /// once, at the start, because the move is over long before enough position reports could show it.</summary>
    public static void ReportMoveBegan(MoveSaid said)
    {
        var me = MyPlayer;
        if (me != null) me.CmdMoveBegan(said);
    }

    /// <summary>SERVER ONLY: a creature began a scripted move — the mirror of <see cref="ReportMoveBegan"/>, said by the
    /// machine that moves a creature's legs. Silent off a server, and for a body no shell speaks for.</summary>
    public static void AnnounceMoveOnMob(GameObject serverMob, MoveSaid said)
    {
        if (!NetworkServer.active || serverMob == null) return;
        var shell = NetMob.ShellForServerMob(serverMob);
        if (shell != null) shell.BeganMove(said);
    }

    /// <summary>Tell the world this body has changed into or out of the sprint gear. Which gear somebody is in
    /// cannot be read off their speed alone — a hasted run and a sprint are both simply fast — so it is said.</summary>
    public static void ReportSprinting(bool on)
    {
        var me = MyPlayer;
        if (me != null) me.CmdSprinting(on);
    }

    /// <summary>Tell the server this body started or stopped walking (Shift). Walking puts the weapon away.</summary>
    public static void ReportWalking(bool on)
    {
        var me = MyPlayer;
        if (me != null) me.CmdWalking(on);
    }

    /// <summary>Ask the server to take this person's weapon out, or put it away. It decides; this only asks.</summary>
    public static void AskWeapon(bool draw)
    {
        var me = MyPlayer;
        if (me != null) me.CmdWeapon(draw);
    }

    // What the cast was aimed at, as the shared thing that speaks for it — asked on the SERVER, where a creature is
    // the real one and its shell is the parent that carries the identity.
    static uint IdentityOfServerSide(Transform aimed)
    {
        // A CREATURE IS NAMED BY ITS SHELL — the one object every machine shares. The server's own creature carries no
        // identity of its own, so asking it alone named nobody, and every screen played the skill's target effects on
        // the caster instead.
        var identity = SharedIdentityOnServer(aimed);
        return identity != null ? identity.netId : 0u;
    }

    // Somebody else's skill, arriving as a thing to LOOK at. Nothing here decides anything: the effect plays on
    // their body and every consequence of it reaches this machine through the doors that already announce one.
    static void OnSkillCastPlayed(SkillCastPlayed msg)
    {
        if (!NetworkClient.spawned.TryGetValue(msg.casterNetId, out var identity) || identity == null) return;

        Transform aimed = null;
        if (msg.targetNetId != 0u && NetworkClient.spawned.TryGetValue(msg.targetNetId, out var at) && at != null)
        {
            // A PERSON is aimed at on the body this machine draws for them — their own avatar for yourself, the copy
            // for anybody else — never at their shell. Only a creature's skill ever aims at a person.
            var drawn = DrawnBodyOf(at);
            aimed = drawn != null ? drawn : at.transform;
        }

        var def = SkillCatalog.Get(msg.skillId);
        var shell = identity.GetComponent<NetPlayer>();
        if (shell != null) { shell.ShowCast(def, aimed, msg.aimPoint, msg.seed); return; }
        var creature = identity.GetComponent<NetMob>();
        if (creature != null) creature.ShowCast(def, aimed, msg.aimPoint, msg.seed);
    }

    // SAID OUT LOUD, once per connection, for the same reason a missing fighter is: the refusal itself is silent —
    // the strike simply does not land — and the honest cause is usually not cheating at all but a character whose
    // progress never arrived, leaving the server holding somebody with no weapon and therefore no skills.
    static readonly System.Collections.Generic.HashSet<string> warnedNotTheirSkill = new System.Collections.Generic.HashSet<string>();

    static void WarnNotTheirSkill(int connectionId, string skillId, string exactly)
    {
        // KEYED BY THE SKILL AS WELL AS THE PERSON. Said once per CONNECTION, the first refusal silenced every
        // other skill that person cast for the rest of their session — so a log with one line in it looked like
        // one broken skill when it was all of them.
        if (!warnedNotTheirSkill.Add(connectionId + "|" + skillId)) return;
        GameLog.Warn(LogCat.Server, "connection " + connectionId + " cast '" + skillId + "', which is not on the "
                         + "hotbar this server holds for them, or is not learned, or belongs to a weapon they are not "
                         + "holding, so the strike was refused. Either their character's progress never loaded (they "
                         + "are standing here at starting numbers, holding nothing), or the skill was never theirs. "
                         + "Exactly: " + exactly + ".");
    }

    // IS THIS SKILL EVEN THEIRS? Without asking, the server would roll ANY skill in the catalog for anybody who
    // named one — the whole game's damage available to a machine that never earned it.
    //
    // THE QUESTION IS "DOES THIS CHARACTER HAVE IT TO HAND", AND IT HAS TWO ANSWERS, because a skill reaches a
    // character two different ways:
    //   - it is BOUND — sitting in one of the numbered slots or the ultimate slot. This is how an active is cast.
    //   - it is LEARNED, and the weapon it belongs to is in their hand (Character.MayUse).
    // Both are read off the character the SERVER holds, restored from saved progress the character server handed
    // it, so neither is a client's word about itself.
    //
    // ASKING ONLY THE WEAPON WAS WRONG AND BROKE THE GAME (2026-08-30). A character can carry bound skills with
    // nothing equipped — a real one did, with three on the bar and an empty gear list — so every skill they cast
    // was refused and nothing took damage. The lesson is bigger than the line: this check must only enforce a rule
    // the GAME already enforces. Inventing a stricter one here does not harden the server, it breaks play.
    //
    // Only OWNERSHIP is settled HERE — but that is not the same as saying it is all the server settles. The cast
    // it lets through runs on the server's own copy of the skill, and that copy pays for itself: it refuses a skill
    // still on cooldown, refuses one the person cannot afford, and refuses one whose required state is missing or
    // whose caster is silenced or stunned — all before a single effect lands. This note used to say the opposite,
    // that a cooldown and a mana bill existed only on the player's machine, which was false and is the sort of thing
    // somebody later trusts a client over.
    static bool Owns(Character caster, string skillId)
    {
        if (caster == null || string.IsNullOrEmpty(skillId)) return false;

        // ON THEIR BAR, LEARNED, AND THE RIGHT WEAPON IN HAND — all three, on the server, whatever a window said.
        bool onTheirBar = caster.Hotbar != null && caster.Hotbar.IsBound(skillId);
        // AN AUTO-COMBAT SLOT IS A BAR TOO — the four skills auto-combat presses, held to the same learned-and-in-hand
        // test below. Read off the settings this server holds, never off the press.
        if (!onTheirBar) onTheirBar = InAutoSlots(caster.AutoCombat, skillId);
        if (!onTheirBar) return false;
        return caster.MayUse(skillId);
    }

    // Which of Owns' three questions said no, in words — for the log line above only.
    static string WhyNotTheirs(Character caster, string skillId)
    {
        if (caster == null) return "there is no character";
        bool onBar = caster.Hotbar != null && caster.Hotbar.IsBound(skillId);
        bool inAuto = InAutoSlots(caster.AutoCombat, skillId);
        string auto = caster.AutoCombat != null && caster.AutoCombat.slots != null
            ? string.Join(",", caster.AutoCombat.slots) : "(none)";
        return "on the hotbar " + onBar + ", in the auto-skill slots " + inAuto + " (they hold: " + auto + "), "
               + (caster.WhyNotUsable(skillId) ?? "learned and its weapon in hand");
    }

    static bool InAutoSlots(AutoCombatSettings s, string skillId)
    {
        if (s == null || s.slots == null) return false;
        for (int i = 0; i < s.slots.Length; i++) if (s.slots[i] == skillId) return true;
        return false;
    }

    // ---- commands a player asks the world to carry out ---------------------------------------------------

    /// <summary>Hand a typed command to the machine that owns the world. Returns FALSE when there is no world to
    /// hand it to, so the chat can say so plainly instead of falling silent — a command that appears to work and
    /// does nothing is how an afternoon disappears.</summary>
    public static bool SendCommandToServer(string line, Action<string> reply)
    {
        if (!NetworkClient.isConnected || string.IsNullOrWhiteSpace(line)) return false;

        // WHERE THE ANSWER GOES. The reply arrives as its own message some milliseconds later, long after the
        // chat box has forgotten what was typed, so the channel back to this log is held until it comes.
        pendingCommandReply = reply;
        NetworkClient.Send(new GmCommandRequest { line = line });
        return true;
    }

    // The chat log that asked the last question, kept only until the answer arrives. One at a time is enough:
    // a person types one command and waits to read what happened before typing the next.
    static Action<string> pendingCommandReply;

    static void OnGmCommandReply(GmCommandReply msg)
    {
        if (string.IsNullOrEmpty(msg.text)) return;
        pendingCommandReply?.Invoke(msg.text);
    }

    /// <summary>PLAYER'S MACHINE: ask the server what it says about creature <paramref name="id"/>. False when
    /// there is no server to ask.</summary>
    public static bool AskMobInfo(string id)
    {
        if (!NetworkClient.isConnected || string.IsNullOrEmpty(id)) return false;
        NetworkClient.Send(new MobInfoRequest { id = id });
        return true;
    }

    static void OnMobInfoReply(MobInfoReply msg) => MobInfo.TakeSent(msg.id, msg.text);

    // Questions asked of the server — a typed command, a creature's details — counted per connection together, so
    // neither door can be used to flood the other's work.
    const int MaxQuestionsPerSecond = 10;
    static readonly System.Collections.Generic.Dictionary<int, ClaimLedger> questionLedgers =
        new System.Collections.Generic.Dictionary<int, ClaimLedger>();
    static bool AskingTooFast(int connectionId) => TooMany(questionLedgers, connectionId, MaxQuestionsPerSecond);

    // Asked BY a player, answered HERE with the finished text. Only an exact creature id is looked up, and the
    // text is built once per creature (MobInfo), so an answer costs one dictionary read.
    void OnMobInfoRequest(NetworkConnectionToClient conn, MobInfoRequest msg)
    {
        if (conn == null || AskingTooFast(conn.connectionId)) return;
        if (ShellFor(conn) == null) return;
        if (string.IsNullOrEmpty(msg.id) || msg.id.Length > 64) return;
        var d = MonsterCatalog.ById(msg.id);
        conn.Send(new MobInfoReply { id = msg.id, text = d != null ? MobInfo.Text(d) : "No creature called '" + msg.id + "'." });
    }

    /// <summary>The map window asking how many of each creature live in a map; false when there is no server to ask.</summary>
    public static bool AskMapCreatureCounts(string map)
    {
        if (!NetworkClient.isConnected || string.IsNullOrEmpty(map)) return false;
        NetworkClient.Send(new MapCountsRequest { map = map });
        return true;
    }

    static void OnMapCountsReply(MapCountsReply msg) => MapCreatureCounts.TakeSent(msg.map, msg.held, msg.ids, msg.counts, msg.kept);

    static readonly System.Collections.Generic.List<string> countIds = new System.Collections.Generic.List<string>(32);
    static readonly System.Collections.Generic.List<int> countNumbers = new System.Collections.Generic.List<int>(32);
    static readonly System.Collections.Generic.List<int> keptNumbers = new System.Collections.Generic.List<int>(32);

    // Asked BY a player, answered HERE: one walk over the creatures filed under that map. Counted with the other
    // questions a connection asks, so it cannot be used to flood the server.
    void OnMapCountsRequest(NetworkConnectionToClient conn, MapCountsRequest msg)
    {
        if (conn == null || AskingTooFast(conn.connectionId)) return;
        if (ShellFor(conn) == null) return;
        if (string.IsNullOrEmpty(msg.map) || msg.map.Length > 64) return;
        bool held = MapCreatureCounts.Count(msg.map, countIds, countNumbers, keptNumbers);
        conn.Send(new MapCountsReply { map = msg.map, held = held, ids = countIds.ToArray(), counts = countNumbers.ToArray(),
                                       kept = keptNumbers.ToArray() });
    }

    // ---- SAYING SOMETHING OUT LOUD ----------------------------------------------------------------------
    //
    // A said line takes exactly the shape a cast already takes, and deliberately so: the speaker ASKS, the server
    // DECIDES who is near enough to hear, and what comes back is one message to every one of them — the speaker
    // included. Nothing is drawn at the keyboard that typed it, so there is no second copy of a chat log to drift.

    /// <summary>The longest line the server will pass on. Anything past this is cut rather than refused, so a
    /// paste that ran away still says its first sentence instead of vanishing with no explanation.</summary>
    public const int MostCharactersInALine = 200;

    /// <summary>How many lines one connection may say in a second. Far above anybody typing and well below what a
    /// machine could shout, the same shape every other ceiling on this layer has.</summary>
    const int MaxLinesSaidPerSecond = 4;

    static readonly System.Collections.Generic.Dictionary<int, ClaimLedger> chatLedgers =
        new System.Collections.Generic.Dictionary<int, ClaimLedger>();

    static bool SayingTooFast(int connectionId) => TooMany(chatLedgers, connectionId, MaxLinesSaidPerSecond);

    // A typed line is made fit to pass on — for every kind of chat — by ChatLinks.Resolve: its item references become
    // links, every other control character a space, and it is cut at MostCharactersInALine as shown.

    /// <summary>Say a line to the world. Returns TRUE when it went, which means the caller must NOT show it
    /// itself — it comes back from the server a moment later, as everybody else receives it.
    ///
    /// <para>FALSE with no world, where there is nobody to ask and the chat log is the only place the line can
    /// go.</para></summary>
    public static bool SendSayToServer(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!NetworkClient.active || !NetworkClient.isConnected) return false;
        // AND NOT BEFORE THERE IS A BODY TO HAVE SAID IT. The server names the speaker by the body it holds for
        // this connection, so a line arriving in the second or two before one exists has nobody to attribute it to
        // and is dropped there in silence. Answering false puts it in this machine's own log instead — the same
        // answer having no world at all gives, rather than a line that simply vanishes.
        if (NetworkClient.localPlayer == null) return false;
        NetworkClient.Send(new ChatSaid { text = text });
        return true;
    }

    // Said BY a player, passed on HERE. Nothing about the speaker is taken from the message: who said it is the
    // connection it arrived on, and who hears it is whoever can already see that person's body.
    void OnChatSaid(NetworkConnectionToClient conn, ChatSaid msg)
    {
        if (SayingTooFast(conn.connectionId)) return;

        var speaker = ShellFor(conn);
        if (speaker == null) return;   // no body here yet, so there is nobody to have said it and nobody to hang a bubble on
        var identity = speaker.netIdentity;
        if (identity == null) return;

        // Every item the line names becomes a link to the speaker's own copy of it (ChatLinks), and nothing else in it
        // may be anything but plain text.
        var text = ChatLinks.Resolve(msg.text, speaker.ServerCombatUnit as Character, MostCharactersInALine);
        if (text.Length == 0) return;

        // A COMMAND IS NOT A SAID LINE and must never become one. Commands have their own door, which asks whether
        // the person is a GM; letting one through here would print it to the room and, worse, teach anybody reading
        // the log to type it.
        if (GmCommands.IsCommand(text)) return;

        // WHAT A TESTER SAYS OUT LOUD is often the most useful line of a play-test — "where do I go?", "this is
        // stuck" — so a said line is written down. Only the room's chat: a private message is never logged.
        if (GameLog.On(LogCat.Server))
        {
            var who = speaker.ServerCombatUnit as Character;
            GameLog.Info(LogCat.Server, who != null
                ? string.Concat(SpawnIds.MapOf(who), " ", SpawnIds.Label(who), " said: ", ChatLinks.Plain(text))
                : string.Concat(speaker.playerName, " said: ", ChatLinks.Plain(text)));
        }

        NetworkServer.SendToReadyObservers(identity, new ChatHeard
        {
            speakerNetId = identity.netId,
            speaker = speaker.playerName,
            text = text,
            isSkillCall = false,
        });
    }

    // A line arriving to be READ. Your own comes back here too and is the copy that gets shown, which is why the
    // box that typed it showed nothing: one line said is one row, decided in one place.
    static void OnChatHeard(ChatHeard msg)
    {
        if (string.IsNullOrEmpty(msg.text)) return;

        if (NetworkClient.localPlayer != null && NetworkClient.localPlayer.netId == msg.speakerNetId)
        {
            if (msg.isSkillCall) ChatController.SkillCall(msg.text);
            else ChatController.Said(msg.text);
            return;
        }

        if (!NetworkClient.spawned.TryGetValue(msg.speakerNetId, out var identity) || identity == null) return;
        var shell = identity.GetComponent<NetPlayer>();
        if (shell == null)
        {
            // A CREATURE calling out a skill: the bubble over its head, and nothing in the log.
            var creature = identity.GetComponent<NetMob>();
            if (creature != null && msg.isSkillCall) creature.ShowCreatureCall(msg.text);
            return;
        }
        // Somebody else's skill call, which this person may have chosen not to see.
        if (msg.isSkillCall && GameSettings.HideOtherPlayersSkillCalls) return;
        ChatController.Heard(msg.speaker, msg.text, shell.SpeechPose, msg.isSkillCall);
    }

    // ---- A PRIVATE MESSAGE ------------------------------------------------------------------------------
    //
    // The person it is for may be on any map server, so it never goes straight there: this server hands it to the
    // character server, which knows which server it last gave that character to, and that server hands it to them.
    // The sender's own copy comes back only once it has actually been delivered — the same rule a said line keeps.

    /// <summary>How many private messages one connection may send in a second.</summary>
    const int MaxPmsSentPerSecond = 2;
    /// <summary>How many private messages one character may RECEIVE in a second, from everybody together — so nobody
    /// can be buried, however many accounts are sending.</summary>
    const int MaxPmsReceivedPerSecond = 5;
    /// <summary>Longer than any name the character server accepts; anything past it is not sent on at all.</summary>
    const int LongestNameAsked = 32;

    static readonly System.Collections.Generic.Dictionary<int, ClaimLedger> pmSentLedgers =
        new System.Collections.Generic.Dictionary<int, ClaimLedger>();
    // Keyed by character id, filled only for somebody playing here and dropped when they leave.
    static readonly System.Collections.Generic.Dictionary<string, ClaimLedger> pmReceivedLedgers =
        new System.Collections.Generic.Dictionary<string, ClaimLedger>();

    /// <summary>Send a private message to <paramref name="toName"/>. FALSE with no world to send it through.</summary>
    public static bool SendPmToServer(string toName, string text)
    {
        if (string.IsNullOrWhiteSpace(toName) || string.IsNullOrWhiteSpace(text)) return false;
        if (!NetworkClient.active || !NetworkClient.isConnected || NetworkClient.localPlayer == null) return false;
        NetworkClient.Send(new PmSend { toName = toName.Trim(), text = text });
        return true;
    }

    [System.Serializable]
    class PmAsk { public string toName; public string fromName; public string text; }

    [System.Serializable]
    class PmAnswer { public string name; }

    void OnPmSend(NetworkConnectionToClient conn, PmSend msg)
    {
        var sender = ShellFor(conn);
        if (sender == null) return;
        if (sender.Leaving) { sender.TellOwner("Could not deliver that message."); return; }
        if (TooMany(pmSentLedgers, conn.connectionId, MaxPmsSentPerSecond))
        {
            sender.TellOwner("You are sending messages too fast.");
            return;
        }

        var toName = msg.toName != null ? msg.toName.Trim() : "";
        var text = ChatLinks.Resolve(msg.text, sender.ServerCombatUnit as Character, MostCharactersInALine);
        if (toName.Length == 0 || text.Length == 0) return;
        if (toName.Length > LongestNameAsked) { sender.TellOwner(toName.Substring(0, LongestNameAsked) + " is not online."); return; }
        if (string.Equals(toName, sender.playerName, System.StringComparison.OrdinalIgnoreCase))
        {
            sender.TellOwner("You cannot send a message to yourself.");
            return;
        }

        string fromId = sender.ServerCharacterId;
        var body = JsonUtility.ToJson(new PmAsk { toName = toName, fromName = sender.playerName, text = text });
        ServerHttp.Send("POST", MapServerLink.CharacterServerUrl + "/server/pm", body, MapServerLink.ServerSecret,
                        (ok, reply, error, status) =>
        {
            // STILL THE SAME PERSON, STILL HERE. The answer can take seconds; by then this connection may have gone,
            // or be playing somebody else, and their copy must not land on whoever has it now.
            if (!NetworkServer.connections.TryGetValue(conn.connectionId, out var still) || still != conn) return;
            var now = ShellFor(conn);
            if (now == null || now.ServerCharacterId != fromId) return;

            var answer = !string.IsNullOrEmpty(reply) ? JsonUtility.FromJson<PmAnswer>(reply) : null;
            string name = answer != null && !string.IsNullOrEmpty(answer.name) ? answer.name : toName;
            switch (status)
            {
                case 200: conn.Send(new PmHeard { name = name, text = text, outgoing = true }); break;
                case 404: now.TellOwner(name + " is not online."); break;
                case 409: now.TellOwner(name + " is changing maps — try again in a moment."); break;
                case 429: now.TellOwner(name + " is receiving too many messages right now."); break;
                default:
                    GameLog.Warn(LogCat.Server, "a private message from " + now.playerName + " could not be passed on — "
                                     + status + " " + error);
                    now.TellOwner("Could not deliver that message.");
                    break;
            }
        });
    }

    /// <summary>SERVER ONLY: hand a private message to the character it is for, if they are here. Returns what to
    /// tell the character server: 200 delivered (or held for somebody announced and still on their way in, given
    /// to them as they arrive), 409 when they are leaving this server for another, 429 when they are receiving too
    /// many, 404 when they are not here at all.</summary>
    public static int DeliverPm(string toCharacterId, string fromName, string text)
    {
        // Made fit by the sender's server, links and all; cleaned again here, keeping those links.
        var pm = new PmHeard { name = fromName, text = ChatLinks.CleanRelayed(text, MostCharactersInALine), outgoing = false };
        var shell = PlayerFighter.PlayedBy(toCharacterId);
        if (shell != null && !shell.Leaving && shell.connectionToClient != null)
        {
            if (TooMany(pmReceivedLedgers, toCharacterId, MaxPmsReceivedPerSecond)) return 429;
            shell.connectionToClient.Send(pm);
            return 200;
        }
        int held = MapServerLink.HoldPmForArrival(toCharacterId, pm);
        if (held != 0) return held;
        return shell != null ? 409 : 404;
    }

    static void OnPmHeard(PmHeard msg)
    {
        if (string.IsNullOrEmpty(msg.text) || string.IsNullOrEmpty(msg.name)) return;
        ChatController.Pm(msg.name, msg.text, msg.outgoing);
    }

    static void OnSystemNotice(SystemNotice msg) => ChatController.GameSaid(msg.text);

    /// <summary>The server is about to hang up on this machine because its character was signed in on another
    /// device.</summary>
    public struct SignedInElsewhere : NetworkMessage { }

    static void OnSignedInElsewhere(SignedInElsewhere _)
    {
        if (spawned != null) spawned.kickedBecause = SignedInElsewhereText;
    }

    /// <summary>SERVER ONLY: tell the machine on <paramref name="conn"/> that its character was signed in on another
    /// device — unless it is <see cref="NetPlayer.Leaving"/> already, which is a player walking through a door, whose
    /// own hang-up is a move and not a loss. Asked BEFORE the shell is marked leaving. True when they were told, so
    /// the caller gives the message time to reach them before hanging up.</summary>
    static bool TellSignedInElsewhere(NetworkConnectionToClient conn, NetPlayer shell)
    {
        if (conn == null || shell == null || shell.Leaving) return false;
        conn.Send(new SignedInElsewhere());
        return true;
    }

    // A message waits to be sent with the rest of the frame's, and hanging up first throws it away; this long is
    // several frames, which a player waiting to be dropped never notices.
    const float SecondsToSayBeforeHangingUp = 0.25f;

    static System.Collections.IEnumerator HangUpShortly(NetworkConnectionToClient conn)
    {
        yield return new WaitForSecondsRealtime(SecondsToSayBeforeHangingUp);
        if (conn != null && NetworkServer.connections.ContainsKey(conn.connectionId)) conn.Disconnect();
    }

    /// <summary>Server-only: one finished line in the chat of every player on this map server. ONLY PEOPLE ALREADY IN —
    /// a connection still at the door has no body yet, and is told what is running when it is let in.</summary>
    public static void TellEveryoneOnThisServer(string line)
    {
        if (!NetworkServer.active || string.IsNullOrEmpty(line)) return;
        var notice = new SystemNotice { text = line };
        foreach (var conn in NetworkServer.connections.Values)
            if (conn != null && conn.identity != null) conn.Send(notice);
    }

    // Asked BY a player, carried out HERE. Three things are checked before anything happens, in this order,
    // because each one is cheaper than the next and each one is a way the request can be a lie.
    void OnGmCommandRequest(NetworkConnectionToClient conn, GmCommandRequest msg)
    {
        // Summoning loads prefabs, so its answer arrives long after this method has returned — by which time the
        // person who asked may have walked out of the world. Checked against the live roll of connections rather
        // than a null test, because a dropped connection object is not null, it is simply no longer anybody's.
        void Answer(string text)
        {
            if (conn == null || string.IsNullOrEmpty(text)) return;
            if (!NetworkServer.connections.ContainsKey(conn.connectionId)) return;
            conn.Send(new GmCommandReply { text = text });
        }

        if (conn == null || AskingTooFast(conn.connectionId)) return;
        var asker = ShellFor(conn);
        if (asker == null) return;   // no body here yet; there is nobody for a command to act as

        // 1. IS THIS PERSON TRUSTED. Read off what the account service told this server at the door — never off
        //    anything the request said about itself, exactly as a player's name and body already are. A game that
        //    has been tampered with can send this message; it cannot make this answer true. The only commands a
        //    non-GM gets past this are the ones marked for anybody, which only read.
        if (!asker.ServerIsGm && !GmCommands.AnyoneMayRun(msg.line)) { Answer(GmCommands.NotAGm); return; }

        // 2. IS THIS EVEN OURS TO RUN. Only the commands that change the shared world belong here. Anything else
        //    arriving is either an old game or a poked one, and running it would act on whichever character this
        //    server happened to touch last.
        if (!GmCommands.ChangesTheWorld(msg.line)) { Answer("That command runs in your own game, not here."); return; }

        // 3. AND AS WHOM. A server holds as many people as have signed in, so the command acts on the person who
        //    asked — their character, standing where they are standing — never on "the player", which is a
        //    question with no single answer here.
        GmCommands.ExecuteAsAsker(msg.line, asker.ServerCombatUnit as Character, asker.transform, Answer);

        GameLog.Info(LogCat.Server, "" + asker.playerName + " ran '" + msg.line + "'");
    }

    // A body per connection, built from the identity that connection announced.
    void OnJoinRequest(NetworkConnectionToClient conn, JoinRequest msg)
    {
        if (conn.identity != null) return;   // already has a body; ignore a repeat

        // WERE WE TOLD TO EXPECT THEM. The character server announces a person and hands over their whole
        // character BEFORE telling them where to knock, so a connection that cannot produce the ticket it was
        // given was never announced and is nobody. This is the door, and it is the reason nothing about who a
        // person is travels on their own message any more.
        var announced = MapServerLink.TakeExpected(msg.ticket);
        if (announced == null)
        {
            GameLog.Warn(LogCat.Server, "refused a connection nobody announced — no good ticket.");
            conn.Disconnect();
            return;
        }

        // AND WHOEVER WAS PLAYING THAT CHARACTER HERE IS FINISHED, NOW. A ticket is minted once, by the character
        // server, in the same breath as it decides this arrival is the live copy — so somebody holding a good one
        // always outranks whoever is already sitting on that character here.
        //
        // This is where the person walking through a door into a map this same server holds is let go: they were
        // deliberately spared when they were announced, because that announcement had to leave their old
        // connection alive long enough to send them this address and this ticket.
        if (PlayerFighter.AlreadyPlaying(announced.characterId))
        {
            GameLog.Info(LogCat.Server, "letting go of the connection already playing that character — the one at "
                             + "the door has a ticket for it.");
            DropWhoeverIsPlaying(announced.characterId);
            PlayerFighter.ReleaseCharacter(announced.characterId);
        }

        var go = Instantiate(playerPrefab);
        var np = go.GetComponent<NetPlayer>();
        if (np != null)
        {
            np.playerName = string.IsNullOrWhiteSpace(announced.characterName) ? "Player" : announced.characterName;
            np.DressOnServer(announced.progress);
        }
        NetworkServer.AddPlayerForConnection(conn, go);

        // WHAT TIME IT IS HERE, sent with the body. This machine's clock is the world's, and everything the
        // arriving player is about to draw or count from it — the sun, a cooldown still running, a creature due
        // back — is measured against it. Sent after the body because a connection with no body is nobody.
        conn.Send(new WorldTime { epochSeconds = WorldClock.EpochSecondsNow });
        // …and whatever a GM has done to this server's sky, so somebody arriving after a /time or a /weather sees
        // the same sky as everybody already here. Sent even when nothing was done: a person coming from another
        // server would otherwise keep that server's sky.
        conn.Send(new TimeOfDayShift { seconds = TimeOfDay.ShiftSeconds });
        conn.Send(WeatherHeldNow());

        // AND A CHARACTER TO GO WITH THE BODY — the one that was PUSHED here, already in hand. Nothing is
        // fetched: this server never asks the character store for anything, which is the whole point of the
        // character server announcing somebody before sending them.
        // THE HAND-OVER NUMBER FIRST: building them can already ask for a save (a new character's kit, a quest given on
        // arrival), and a save carrying 0 would be taken unchecked.
        if (np != null) np.ServerHandover = announced.handover;
        PlayerFighter.BuildFrom(np, announced);

        // AND ANY PRIVATE MESSAGE THAT REACHED THIS SERVER WHILE THEY WERE ON THEIR WAY IN — held on their
        // announcement (see MapServerLink.HoldPmForArrival), now theirs.
        if (announced.pendingPms != null)
            foreach (var pm in announced.pendingPms) conn.Send(pm);

        // AND, TO SOMEBODY ENTERING THE WORLD, WHICH WORLD EVENTS ARE RUNNING. Only on entering — walking in from
        // another map server is not news, and saying it at every door would bury it.
        if (announced.signingIn)
        {
            var running = WorldEvents.RunningNowLine();
            if (running != null) conn.Send(new SystemNotice { text = running });
        }

        GameLog.Info(LogCat.Server, "" + (np != null ? np.playerName : "someone")
                  + " (" + (np != null && !string.IsNullOrEmpty(np.ServerCharacterId) ? np.ServerCharacterId : "no player id")
                  + ") entered the world — " + NetworkServer.connections.Count + " player(s) here");

        // SAID AGAIN, HERE, EVERY TIME SOMEBODY ARRIVES. Saying it only at start-up was not enough and the reason
        // is worth keeping: a tester reads the END of a log, and a start-up line sits at the beginning of it
        // forever — hours and tens of thousands of routine lines away by the time anybody spawns. It also does not
        // survive a cleared console. An arrival is the one moment every tester passes through, and it lands at the
        // tail of the log exactly when they start reading.
        GameLog.AnnounceToTesters();
    }

    // ---- what each side reports -------------------------------------------------------------------------
    // A server has no screen, so a connection arriving is only visible if it says so. These are the lines that
    // turn "it should be working" into evidence.

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);
        GameLog.Info(LogCat.Server, "player connected — connection " + conn.connectionId
                  + ", " + NetworkServer.connections.Count + " now on this world");
    }

    /// <summary>EVERYBODY GETS WRITTEN DOWN WHEN THIS SERVER STOPS. Without it a clean shutdown threw away
    /// whatever had happened since each player's last beat — up to fifteen seconds each, for everybody at once,
    /// on the one event an operator triggers deliberately.</summary>
    public override void OnStopServer()
    {
        foreach (var conn in NetworkServer.connections.Values)
            ShellFor(conn)?.SaveNow(null);
        // AND KEPT ON DISK until they land: the process may end before the writes above come back, and whatever
        // has not landed by then is sent by the next start instead of being lost.
        SaveOutbox.WriteAllToDisk();
        GameLog.Info(LogCat.Server, "stopping — asked for a write for every player still here.");
        ReachableAddresses.Stop();
        shellByConnection.Clear();   // nothing about the world that just ended survives into the next one
        base.OnStopServer();
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // COUNTED AFTER, not guessed at. Taking one off the current count assumed this connection was still in
        // it, and by this point it sometimes is not — so the number went NEGATIVE as people left.
        var leftShell = conn.identity != null ? conn.identity.GetComponent<NetPlayer>() : null;
        GameLog.Info(LogCat.Server, "player left — connection " + conn.connectionId
                  + (leftShell != null
                      ? " — " + leftShell.playerName + " ("
                        + (string.IsNullOrEmpty(leftShell.ServerCharacterId) ? "no player id" : leftShell.ServerCharacterId) + ")"
                      : ""));

        // Everything remembered ABOUT a connection goes when the connection does. A long-lived server hands out
        // connection numbers for months; anything kept per connection and never dropped grows forever.
        warnedNoFighter.Remove(conn.connectionId);
        shellByConnection.Remove(conn.connectionId);
        swingLedgers.Remove(conn.connectionId);
        skillLedgers.Remove(conn.connectionId);
        chatLedgers.Remove(conn.connectionId);
        questionLedgers.Remove(conn.connectionId);
        pmSentLedgers.Remove(conn.connectionId);
        if (leftShell != null && !string.IsNullOrEmpty(leftShell.ServerCharacterId))
            pmReceivedLedgers.Remove(leftShell.ServerCharacterId);
        swingDueAt.Remove(conn.connectionId);
        attackStartDueAt.Remove(conn.connectionId);
        // AND ANY SWING STILL IN FLIGHT. A blow scheduled a moment before somebody left would otherwise come round
        // and look for a fighter that has been dismissed — harmless, but it would sit in the list until it did.
        for (int i = pendingBlows.Count - 1; i >= 0; i--)
            if (pendingBlows[i].connectionId == conn.connectionId) pendingBlows.RemoveAt(i);
        // And everything they threw that is still in the air.
        Flights.DropThrower(conn.connectionId);

        // WRITTEN DOWN BEFORE THEY ARE LET GO. This is the moment everything that happened to them has to
        // survive — and the order matters: a character released while its last write is still in flight is how
        // the same items end up in two places, which is the oldest duplication trick there is. The write is
        // asked for first, and the character is dismissed only once it has landed.
        // ASKED FOR DIRECTLY, not through the remembered answer: this runs AFTER that memory has been dropped a few
        // lines up, and going through it here would quietly write the entry back for a connection that is leaving —
        // one stranded entry per departure, on a server that runs for months.
        var leaving = leftShell;
        if (leaving != null)
        {
            // THE NAME AND THE ID, TAKEN NOW. Everything below this runs after Mirror has destroyed the body, so
            // reading anything off it inside the answer would be reading a destroyed object — which is exactly
            // what happened: the character was never released, and its owner was refused as "already playing"
            // for the life of the server. Copied out here, while it still exists.
            string whoLeft = leaving.playerName;
            string theirCharacter = leaving.ServerCharacterId;

            // BUT NOT IF THEY ARE ALREADY SOMEBODY ELSE'S. A person walking through a door, or one whose account
            // has just been signed in again somewhere else, is dropped here AFTER another process has been handed
            // their character — so writing this copy down now puts a state that is already finished with on top
            // of the one that is live, and the player loses whatever they have done since. The write that mattered
            // has already happened, before the move began, and was waited for.
            if (leaving.Leaving)
            {
                GameLog.Info(LogCat.Server, whoLeft + " left because they are being played elsewhere now — not "
                                 + "writing this copy down over theirs.");
                // A DOOR, NOT A GOODBYE: their visit's tally waits for them to arrive in the next map.
                PlaySession.WentThroughADoor(theirCharacter);
                PlayerFighter.Dismiss(leaving);
                // ONLY IF THE NAME IS STILL THIS SHELL'S. Somebody who walked into a map this same server holds is
                // already playing again on a NEWER connection by the time this one is torn down, and freeing the
                // name here would say nobody is playing that character while somebody plainly is.
                PlayerFighter.ReleaseCharacterIfHeldBy(theirCharacter, leaving);
                base.OnServerDisconnect(conn);
                return;
            }

            // THEY STOPPED PLAYING: the one line that sums up their whole visit.
            PlaySession.Stopped(theirCharacter);

            // THE WRITE IS ASKED FOR FIRST, AND THIS ORDER IS THE WHOLE POINT. Letting go of the fighter unbinds
            // the character from this shell and destroys it — so asking for the write afterwards found nothing to
            // write, gave up without so much as sending a request, and reported failure. MEASURED 2026-09-03: every
            // single person who left lost whatever had changed since their last periodic write, and the log said so
            // every time. The comment here used to claim this order already; the code did the opposite.
            //
            // Asking first costs nothing, because the whole of what gets written is copied out of the character
            // SYNCHRONOUSLY inside this call, before the request is even sent. So the body may be torn down on the
            // very next line without touching what is already on its way.
            leaving.SaveNow(ok =>
            {
                if (!ok) GameLog.Warn(LogCat.Server, "" + whoLeft + " left and their character could not be "
                                        + "written down yet — kept, and tried again until it lands.");
                // Said again for the case the line above cannot cover: if anything else released the body
                // first, the name still has to come free — but only while it is still THIS shell's, because a
                // slow write can land long after the character has been handed to a newer connection.
                PlayerFighter.ReleaseCharacterIfHeldBy(theirCharacter, leaving);
            });

            // AND THE NAME IS FREED IMMEDIATELY, not when the write lands — this runs now, not in the answer above.
            // Holding it until the write returns means a person whose save is slow, or whose character server is
            // down, cannot come back at all, and the write has nothing to do with whether somebody may play again.
            PlayerFighter.Dismiss(leaving);
        }

        base.OnServerDisconnect(conn);
        GameLog.Info(LogCat.Server, "" + NetworkServer.connections.Count + " player(s) still here");
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        GameLog.Info(LogCat.Client, "connected to the server");

        // NOTHING IS RESET HERE, and the world is NOT woken here. The wire coming up is also exactly what
        // happens a moment before a join is refused, so treating it as success is what made a refused player
        // retry forever from one: the count went back to zero, the refusal arrived, and the loop began again.
        // MEASURED on a real run: "retrying 1 of 3" printed more than forty times, and the world came up on
        // screen for somebody the server had already turned away. Both now wait for ConfirmedInTheWorld.

        // Announce who is playing, so the server can give this connection the right body. Sent from here
        // rather than at sign-in because a connection is the thing that needs a body, and a connection is
        // what has just come into existence.
        NetworkClient.Send(new JoinRequest
        {
            characterName = AccountSession.CharacterName ?? "Player",
            characterId = AccountSession.CharacterId ?? "",
            ticket = AccountSession.EntryTicket ?? ""
        });
    }

    /// <summary>THE MOMENT THIS PLAYER IS REALLY IN: the server granted them a body, which is the only thing it
    /// does that a refused connection never gets. Called by that body as it starts on the machine that owns it.
    ///
    /// <para>This is where the retry count starts fresh and where the world is allowed to come up — never on the
    /// wire coming up, which happens to the refused too.</para></summary>
    public static void ConfirmedInTheWorld()
    {
        if (spawned == null) return;
        spawned.attemptsSpent = 0;
        // AND THE HANG-UP IS OVER. Left standing — because a hang-up did not reach the handler for once — it would
        // swallow the next REAL drop and leave somebody staring at a world that is not there.
        spawned.hangingUpToMove = false;
        ReconnectNotice.Hide();
        LoadingScreen.Reached(LoadingScreen.Stage.Connected);
        OnJoinedWorld?.Invoke();

        // ARRIVED SOMEWHERE ELSE. The map is loaded only now, with a body already granted on the far side: if the
        // dial had failed, the player would still be standing in the world they left, which is somewhere they can
        // be put back. Loading first and then failing to dial leaves them in a map connected to nobody.
        if (string.IsNullOrEmpty(movingToMap)) return;
        string map = movingToMap;
        movingToMap = null;

        // AND THE CURTAIN COMES DOWN WHEN THE MAP IS ACTUALLY THERE, not when it was asked for. Travel only
        // STARTS a load; without waiting for it to land the screen stayed up on "Arriving…" forever, because
        // nothing else on this path ever lowered it. Signed up for one arrival and taken straight off again, so
        // an ordinary door later cannot lower a screen that is not up.
        string spawn = movingToSpawn;
        movingToSpawn = null;

        // AN EXACT SPOT RATHER THAN A DOORWAY — a save point. Carried the way a signing-in player's last position is,
        // so the arrival below places them there and then forgets it.
        if (movingToSpot)
        {
            movingToSpot = false;
            SceneTravel.ArriveAtSpot(map, movingToSpotAt);
            spawn = null;
        }

        // ALREADY THERE? Then put them at the doorway they arrived by and lower it now. The map can finish
        // loading before this runs, and waiting for an arrival that has already happened never ends.
        var already = UnityEngine.SceneManagement.SceneManager.GetSceneByName(map);
        if (already.IsValid() && already.isLoaded)
        {
            SceneTravel.Target = spawn;
            if (PersistentPlayer3D.Instance != null) PersistentPlayer3D.Instance.ReturnToSavedPlace();
            LoadingScreen.Hide();
            return;
        }

        LoadingScreen.HideWhenMapArrives();
        MapManager.Travel(map, spawn);
    }

    // The map this player is moving to on ANOTHER server, remembered across the hang-up and the dial because the
    // connection that knew about it is the one being replaced.
    static string movingToMap;

    // WHICH DOORWAY THEY CAME OUT OF. Arriving with no spawn point left them standing exactly where they were,
    // which is INSIDE the door they just walked into — so it fired again the moment they landed, and again after
    // that. The curtain never came down because the move never stopped starting over.
    static string movingToSpawn;

    // OR THE EXACT SPOT THEY ARRIVE AT, inside the map — a save point — which has no doorway to name.
    static bool movingToSpot;
    static Vector3 movingToSpotAt;

    /// <summary>Leave this world for another one, with the address and the one-time ticket the character server
    /// has already handed the process we are leaving.
    ///
    /// <para><b>Dial first, load the map second.</b> The two failures are not alike: arriving before the map is
    /// up is untidy and fixes itself the moment the map lands, while loading the map and then failing to dial
    /// leaves somebody standing in a world with nobody in it. So the risky step is taken while there is still
    /// somewhere to be put back to.</para>
    ///
    /// <para>It does not walk the ordinary reconnect path: that one waits two seconds before it even asks, counts
    /// against the three tries that end in being signed out, and is written for a connection that DROPPED. This
    /// hang-up is deliberate.</para></summary>
    public static void MoveToAnotherServer(string host, int port, string ticket, string map, string spawnId,
                                           bool toSpot, Vector3 spot)
    {
        if (spawned == null || string.IsNullOrEmpty(ticket)) return;

        LoadingScreen.Reached(LoadingScreen.Stage.Saved);
        movingToMap = map;
        movingToSpawn = spawnId;
        movingToSpot = toSpot;
        movingToSpotAt = spot;

        AccountSession.EntryTicket = ticket;

        // SAID BEFORE THE HANG-UP, NOT AFTER. Letting go of this world fires the same handler as a connection
        // that dropped, and that handler starts a two-second retry — which asks the character server to let this
        // player in all over again, on top of a move that is already working. Asking evicts them from the world
        // they have just arrived in, and being evicted asks again: MEASURED on a real run, thirty-two rounds of
        // dropping and reconnecting over sixty-five seconds after one step through a door.
        if (NetworkClient.active) { spawned.hangingUpToMove = true; spawned.StopClient(); }

        // AND ANY RETRY ALREADY WAITING IS CALLED OFF. One left over from an earlier drop would fire in the
        // middle of this move and start the same loop from the other end.
        if (spawned.pendingRetry != null) { spawned.StopCoroutine(spawned.pendingRetry); spawned.pendingRetry = null; }

        if (!string.IsNullOrEmpty(host)) spawned.networkAddress = host;
        if (port > 0 && port <= ushort.MaxValue && Transport.active is kcp2k.KcpTransport kcp)
            kcp.port = (ushort)port;

        spawned.attemptsSpent = 0;
        spawned.goneForGood = false;
        GameLog.Info(LogCat.Client, "moving to " + spawned.networkAddress + ":" + port + " for '" + map + "'");
        spawned.StartClient();
    }

    public override void OnClientDisconnect()
    {
        GameLog.Info(LogCat.Client, "disconnected from the server");
        base.OnClientDisconnect();

        // Mirror reports a REFUSED first attempt and a DROPPED live connection through this same door, which is
        // exactly right for us: "we are not in a world" is one condition however we arrived at it, and it gets one
        // answer — try again, then give up.
        if (GameServer.IsServer) return;
        if (kickedBecause != null)
        {
            string why = kickedBecause;
            kickedBecause = null;
            goneForGood = true;
            if (pendingRetry != null) { StopCoroutine(pendingRetry); pendingRetry = null; }
            ReconnectNotice.Hide();
            OnLostWorld?.Invoke(why);
            return;
        }
        if (goneForGood) return;

        // WE HUNG UP ON PURPOSE. Nothing was lost, so nothing is counted, no notice goes on screen, and above all
        // no retry is scheduled — the dial to the other world is already on its way from the line that set this.
        if (hangingUpToMove) { hangingUpToMove = false; return; }

        attemptsSpent++;
        if (attemptsSpent < JoinAttempts)
        {
            GameLog.Info(LogCat.Client, "lost the world — retrying (" + attemptsSpent + " of " + JoinAttempts + ")");
            ReconnectNotice.Show();
            pendingRetry = StartCoroutine(DialAgainShortly());
            return;
        }

        goneForGood = true;
        ReconnectNotice.Hide();
        GameLog.Warn(LogCat.Client, "gave up after " + JoinAttempts + " attempts — leaving the world");
        OnLostWorld?.Invoke(CouldNotReachWorld);
    }

    /// <summary>THE PLAYER IS CLOSING THE GAME, and that is the third and last way a world is left on purpose.
    /// Mirror hangs up the client from here, which fires the very same handler a dropped connection does — so
    /// without this the last thing a quitting client did was count an attempt, build a fresh full-screen canvas
    /// for "Reconnecting…" and start a coroutine to dial again in two seconds, all while the engine was already
    /// tearing itself down. The other two deliberate exits already say so before hanging up:
    /// <see cref="MoveToAnotherServer"/> sets its hang-up flag and <see cref="Forget"/> sets this same one.</summary>
    public override void OnApplicationQuit()
    {
        goneForGood = true;
        if (pendingRetry != null) { StopCoroutine(pendingRetry); pendingRetry = null; }
        ReconnectNotice.Hide();
        base.OnApplicationQuit();
    }

    System.Collections.IEnumerator DialAgainShortly()
    {
        yield return new WaitForSeconds(SecondsBetweenAttempts);
        pendingRetry = null;
        if (goneForGood) yield break;

        // A FRESH TICKET, not the spent one. A ticket opens the door exactly once — that is the whole point of
        // it — so dialling again with the same one is refused every single time, and the retry could never have
        // succeeded. Asking the character server again also re-announces this person to the map server, which
        // is what actually has to happen for them to be let back in.
        AskWhereToEnter(Transport.active as kcp2k.KcpTransport);
    }

    /// <summary>Put this copy back to knowing nothing, so a later sign-in dials a genuinely fresh connection
    /// instead of finding a half-dead one still sitting there. Called by <see cref="GameRoot"/> as it tears the
    /// world down — for a player who chose to go as much as for one whose connection died.</summary>
    public static void Forget()
    {
        if (spawned != null)
        {
            // SAID BEFORE THE HANG-UP, and this is the whole reason the line is here. Stopping the client fires
            // the very same handler a dropped connection does, and that handler counts an attempt, puts
            // "Reconnecting…" on screen and schedules another dial. Nothing is being reconnected: this call's
            // entire job is to stop being in a world. It is the same trap the move-to-another-server path
            // already guards, marked here instead of at each caller because EVERY caller of this is leaving.
            spawned.goneForGood = true;

            if (NetworkClient.active) spawned.StopClient();
            Destroy(spawned.gameObject);
            spawned = null;
            mobPrefab = null;
            npcPrefab = null;
        }

        // AND THE NOTICE COMES DOWN, whether this call put it up or found it already showing — a player can
        // choose to leave in the middle of a reconnect. It is its own don't-destroy canvas at the very top of
        // the draw order, so one left showing outlives the scene reload and sits over the sign-in screen with
        // nothing alive to take it down.
        ReconnectNotice.Hide();
    }
}
