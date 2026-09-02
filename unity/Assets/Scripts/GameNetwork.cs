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

    const string DefaultHost = "localhost";
    const ushort DefaultPort = 7777;

    /// <summary>How far away a player is still told about a thing, in world units. See where it is used for why 60.</summary>
    const int VisibleRange = 60;

    /// <summary>The body spawned for each connected player, found in Resources by this name.</summary>
    public const string PlayerPrefabName = "NetPlayer";

    /// <summary>The shell spawned for each mob the server owns, found in Resources by this name.</summary>
    public const string MobPrefabName = "NetMob";

    /// <summary>The shell spawned for each drop the server owns, found in Resources by this name.</summary>
    public const string LootPrefabName = "NetLoot";

    // DELIBERATELY ABSENT: a public "am I joined right now" answer. It had no readers, and the one use anybody
    // would reach for it for — gating behaviour — is the timing accident this layer already outlawed: dialling
    // takes a moment, and a question asked inside that moment gets the wrong answer and acts on it. The question
    // to ask is what job this copy was GIVEN (GameServer.IsServer), which is settled at start-up.

    /// <summary>Raised the moment this copy is joined and the world may exist. <see cref="GameRoot"/> waits for it
    /// before bringing a map up: signing in is no longer enough to be somewhere.</summary>
    public static event Action OnJoinedWorld;

    /// <summary>Raised when the wire is gone for good — every retry spent. Nobody may be in the world after this,
    /// so <see cref="GameRoot"/> tears it down and puts the sign-in screen back.</summary>
    public static event Action OnLostWorld;

    // How hard we try before giving up on a world. Small enough that nobody is left staring at a veil, long enough
    // to ride out the brief drop that a wireless connection produces several times an hour.
    const int JoinAttempts = 3;
    const float SecondsBetweenAttempts = 2f;

    int attemptsSpent;
    bool goneForGood;   // stop retrying once we have already bounced; a late failure must not restart the loop

    static GameObject mobPrefab;

    /// <summary>Server-only: announce a mob that has just come into existence, so every connected player
    /// receives it. Called from wherever a creature is born; does nothing off a server, so the offline game
    /// is untouched.</summary>
    public static void PublishMob(GameObject mob, string defId)
    {
        if (mob == null || !NetworkServer.active) return;

        if (mobPrefab == null) mobPrefab = Resources.Load<GameObject>(MobPrefabName);
        if (mobPrefab == null)
        {
            Debug.LogError("[net] no '" + MobPrefabName + "' prefab in Resources — mobs will exist on the "
                           + "server but no player will see them.");
            return;
        }

        var shell = Instantiate(mobPrefab);
        shell.GetComponent<NetMob>()?.BindOnServer(mob, defId);
        NetworkServer.Spawn(shell);
    }

    static GameObject lootPrefab;

    /// <summary>Server-only: announce a drop that has just been made, so every connected player receives it and
    /// builds its own body for it. TRUE when the announcement was made — which is also the answer to "should I
    /// skip building one here", because a server has no screen and the body belongs on the machines watching.
    /// FALSE off a server, so a game with no world but its own drops exactly as it always did.</summary>
    public static bool PublishLoot(Item item, int gold, Vector3 start, Vector3 landing, string map)
    {
        if (!NetworkServer.active) return false;

        if (lootPrefab == null) lootPrefab = Resources.Load<GameObject>(LootPrefabName);
        if (lootPrefab == null)
        {
            Debug.LogError("[net] no '" + LootPrefabName + "' prefab in Resources — loot will exist on the "
                           + "server but no player will see it.");
            return false;
        }

        var shell = Instantiate(lootPrefab);
        shell.GetComponent<NetLoot>()?.BindOnServer(item, gold, start, landing, map);
        NetworkServer.Spawn(shell);
        return true;
    }

    /// <summary>ASK for a drop this machine is only watching. TRUE when the question was sent and the answer is
    /// the server's to give — the caller must then do NOTHING, because paying out here as well would hand the
    /// same coin to two people. FALSE for a drop no server has heard of, which is this machine's own to give.</summary>
    public static bool SendPickupToServer(WorldPickup pickup)
    {
        if (pickup == null) return false;

        var shell = NetLoot.ShellForView(pickup.gameObject);
        if (shell == null) return false;                       // an offline drop — nobody else can be holding it

        var me = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<NetPlayer>() : null;
        if (me == null) return false;                          // watching a drop with no body of our own to ask with

        me.AskForLoot(shell, pickup);
        return true;
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

        spawned.networkAddress = ServerAddress.HasTypedHost
            ? ServerAddress.TypedHost
            : Environment.GetEnvironmentVariable(HostEnvVar) ?? DefaultHost;

        // The body every connected player is seen as. Loaded by NAME from Resources so both halves resolve
        // the same asset with no scene wiring — the same way the skill and monster catalogs already load.
        spawned.playerPrefab = Resources.Load<GameObject>(PlayerPrefabName);
        if (spawned.playerPrefab == null)
            Debug.LogError("[net] no '" + PlayerPrefabName + "' prefab in Resources — players will connect "
                           + "but nobody will have a body.");

        // Anything the server can spawn must be known to the clients too, or an arriving mob is an object
        // nobody can build. The player body is registered automatically; everything else is listed here.
        mobPrefab = Resources.Load<GameObject>(MobPrefabName);
        if (mobPrefab != null) spawned.spawnPrefabs.Add(mobPrefab);
        else Debug.LogError("[net] no '" + MobPrefabName + "' prefab in Resources — no player will see mobs.");

        lootPrefab = Resources.Load<GameObject>(LootPrefabName);
        if (lootPrefab != null) spawned.spawnPrefabs.Add(lootPrefab);
        else Debug.LogError("[net] no '" + LootPrefabName + "' prefab in Resources — no player will see loot.");

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
                Debug.LogError(Shout("[server] port " + kcp.port + " is already taken — another server is running. "
                                     + "This one is quitting rather than sitting here listening to nothing. Stop the "
                                     + "other one first, or give this one a different GAME_SERVER_PORT."));
                Stop();
                return;
            }

            spawned.StartServer();
            Debug.Log("[server] listening for players on port " + kcp.port);
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
    /// how a single machine is run for testing, with no character server at all.</para></summary>
    static void AskWhereToEnter(kcp2k.KcpTransport kcp)
    {
        string characterId = AccountSession.CharacterId;
        if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(AccountSession.CharacterServerUrl))
        {
            Debug.Log("[client] dialling " + spawned.networkAddress + ":" + kcp.port
                      + " (no character server to ask)");
            spawned.StartClient();
            return;
        }

        AccountClient.Send("POST", "/characters/" + characterId + "/enter", "{}", true, (ok, body, error) =>
        {
            if (spawned == null) return;

            if (!ok)
            {
                Debug.LogError("[client] the character server would not let us in — " + error + " " + body);
                Stop();
                OnLostWorld?.Invoke();
                return;
            }

            var where = JsonUtility.FromJson<EntryReply>(body);
            if (where == null || string.IsNullOrEmpty(where.ticket))
            {
                Debug.LogError("[client] the character server let us in but said nowhere to go: " + body);
                Stop();
                OnLostWorld?.Invoke();
                return;
            }

            AccountSession.EntryTicket = where.ticket;
            if (!string.IsNullOrEmpty(where.host)) spawned.networkAddress = where.host;
            // AND THE PORT IT NAMED. A build pointed at one machine by its environment must still go wherever
            // the character server sends it, or splitting the world across machines would need a new build for
            // every player.
            if (where.port > 0 && where.port <= ushort.MaxValue) kcp.port = (ushort)where.port;

            Debug.Log("[client] dialling " + spawned.networkAddress + ":" + kcp.port + " with a ticket");
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

    static ushort PortFromEnvironment()
    {
        string raw = Environment.GetEnvironmentVariable(PortEnvVar);
        return ushort.TryParse(raw, out ushort p) && p != 0 ? p : DefaultPort;
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
    static string Shout(string line) => "\u001b[31m" + line + "\u001b[0m";

    /// <summary>Give up and go. A dedicated server has no window to close, and stopping the editor is a
    /// different action from quitting a built game — so the one place that has to end the process says both.</summary>
    static void Stop()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(1);
#endif
    }

    /// <summary>What a client says on arrival: who it is playing. The server cannot know this by itself — the
    /// account service told the CLIENT which character it chose — so the client states it and the server spawns
    /// a body to match. Deliberately just identity: nothing here is trusted for anything that affects gameplay.</summary>
    public struct JoinRequest : NetworkMessage
    {
        public string characterName;
        public string modelName;

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

    /// <summary>What the server DECIDED one blow was, sent out to everybody watching. Carries the resolved swing —
    /// how hard, which element, whether it crit, whether it whiffed — and NOT the victim's remaining health, which
    /// travels the usual way.
    ///
    /// <para><b>Why this has to exist.</b> Once a client forwards its hits, the client itself stops applying them,
    /// so without this nothing on any screen reacts: no damage number, no flash, no stagger. The server decides the
    /// blow but has no screen of its own, so if it does not say what happened, the hit is invisible everywhere.</para></summary>
    public struct HitResult : NetworkMessage
    {
        public uint targetNetId;

        /// <summary>WHO swung it. Without this a landed blow arrives on every machine with no attacker attached, and
        /// everything a hit teaches its OWN attacker goes dead: the run of consecutive hits never advances, the
        /// on-damage-dealt passives never fire, lifesteal never pays out. Each machine compares it with its own
        /// player and credits the person at THIS keyboard only when the blow was theirs — so one swing teaches one
        /// attacker, not everybody watching.</summary>
        public uint attackerNetId;

        public int amount;
        public Element element;
        public bool isMagic;
        public bool isCrit;
        public bool missed;
        public int attackerHit;

        /// <summary>An extra beat of a flurry, landing past the moment the creature died. Carried because it changes
        /// what the receiving machine SHOWS — the numbers keep climbing to the end of the count instead of stopping
        /// dead mid-combo — and that is the whole visible difference between the two.</summary>
        public bool overkill;

        /// <summary>This strike belongs to a run that shows a RISING YELLOW TOTAL, and the receiving machine should
        /// fold it into one.
        ///
        /// <para><b>Why it has to travel.</b> The total used to be added up from what a local hit returned, which is
        /// nothing now — a player's machine decides no damage, so every number reaches it separately and the run that
        /// sent them has long since finished. The only thing that still knows whether a total was owed is the
        /// authored skill, and the server is the one holding it.</para>
        ///
        /// <para><b>The server's reading, never a client's claim.</b> It is worked out from the same authored data
        /// that decided it offline, so a machine cannot ask for a number it was not owed. It moves nothing about the
        /// damage either way — this is one number on a screen.</para></summary>
        public bool showsTotal;
    }

    /// <summary>What the server decided a blow ON A PERSON was. The mirror of <see cref="HitResult"/>, and it has to
    /// be its own message because a person and a creature are two different things on the wire: a creature is one
    /// shell everybody builds a copy of, while a person is somebody's real character on one machine and a body being
    /// watched on every other. Both halves are covered — see <see cref="NetPlayer.ShowBlow"/>.</summary>
    public struct PlayerHitResult : NetworkMessage
    {
        public uint victimNetId;
        public int amount;
        public Element element;
        public bool isMagic;
        public bool isCrit;
        public bool missed;
        public int attackerHit;

        /// <summary>WHAT THE BLOW RAN INTO on the way to their health — how much a barrier ate, how much a brace held
        /// back, whether a parry or an off-hand block erased it outright.
        ///
        /// <para><b>Why the number alone was not enough.</b> <see cref="amount"/> is what SURVIVED, and every one of
        /// these can take a blow down to nothing — so a fully braced, soaked, parried or blocked hit arrived as a
        /// plain zero, which draws nothing and looks exactly like being missed. A brace held a blow and the person
        /// wearing it saw no sign that anything had happened at all. This is the same kind of fact as
        /// <see cref="missed"/>, which has always travelled for exactly this reason.</para></summary>
        public UnitMitigation.Outcome mitigated;
    }

    /// <summary>What a kill was worth, told to the person who earned it. Progress is written by a player's own
    /// machine, so the server announcing the reward is the only way it can reach the character it belongs to.</summary>
    public struct XpEarned : NetworkMessage
    {
        public int amount;
    }

    /// <summary>A player's SKILL strike, NAMED rather than measured — which skill, which of its strikes, and at what.
    ///
    /// <para><b>The server works the damage out itself.</b> It holds the same authored skill (the catalog loads from
    /// the same data on both sides) and the same character, so being told the id is enough: it looks the strike up
    /// and rolls it off the stats it holds. Nothing about how hard it hit crosses the wire, which is the whole
    /// point — a number a client states is a number a client can inflate.</para>
    ///
    /// <para><b>The pile is the one exception, and it is a narrow one.</b> Three of the game's skills let a strike
    /// answer to a pile the cast banked part-way through its own run, and that reading exists only inside the run.
    /// It travels here, and the server ignores it outright unless the strike it names actually reads a pile — so for
    /// every other skill the client says nothing that affects the number at all.</para></summary>
    public struct SkillStrikeRequest : NetworkMessage
    {
        public uint targetNetId;
        public string skillId;

        /// <summary>WHICH strike, as the two numbers it takes to name one: a skill is a list of beats, and each beat
        /// carries its own list of things it does. Both are needed — "the third effect" means nothing without
        /// saying third effect of which beat.</summary>
        public int tickIndex;
        public int effectIndex;

        public int stackCount;
        public float stackCarried;
        public bool overkill;
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

    /// <summary>A strike whose damage the client resolved and the server CANNOT re-derive, because no authored skill
    /// stands behind it — a weapon's own flurry or cleave, split off a swing by the modifiers on that weapon.
    ///
    /// <para><b>Still trusted, and now the only thing that is.</b> It used to carry every skill in the game; a skill
    /// names itself instead (see <see cref="SkillStrikeRequest"/>). What is left is the handful of strikes that are
    /// properties of a weapon rather than of a skill, and moving those needs the weapon's modifiers resolved on the
    /// server — a smaller job than the one just done, and the next one.</para></summary>
    ///
    /// <para><b>Why this one carries numbers when an ordinary swing does not.</b> A swing is the same act every
    /// time, so the server can roll it from the fighter it already holds and needs to be told nothing but the
    /// target. A skill is whatever it was authored to be — its own damage, its own element, its own count of hits,
    /// its own rules about landing past a death — and none of that lives on the server today. So the strike travels
    /// as the client resolved it, and the server checks what it CAN check: that this is a real target, that it is
    /// near enough to be plausible, and that nobody is sending thousands a second.</para>
    ///
    /// <para><b>This is the trusted half of the layer, and it is trusted on purpose rather than by accident.</b>
    /// Refusing it instead — which is what happened until now — means skills simply do not work in a shared world.
    /// The end state is the server running the skill itself; that needs the whole skill system to exist there, and
    /// it is its own piece of work. Until then a skill's damage is the client's word.</para></summary>
    public struct ResolvedStrikeRequest : NetworkMessage
    {
        public uint targetNetId;
        public int amount;
        public Element element;
        public bool isMagic;
        public bool isCrit;
        public bool overkill;
    }

    /// <summary>A player asking to come back after a death. Carries nothing — the server knows who is asking and
    /// whether they are actually dead, and those are the only two facts that matter.</summary>
    public struct ReviveRequest : NetworkMessage { }

    /// <summary>A player saying they healed themselves — a potion, a bandage, anything that puts health back.
    ///
    /// <para><b>Trusted, and deliberately so for now.</b> Health belongs to the server, so a heal that happened only
    /// on somebody's own machine would be undone by the server's next word about them, and a potion would appear to
    /// do nothing. The honest fix is for the thing that heals to happen ON the server — the same fix skills need,
    /// and the same one the migration plan already has on its list. Until then this crosses as a claim, which is no
    /// more trusted than a skill's own damage already is.</para></summary>
    public struct HealClaim : NetworkMessage
    {
        public int amount;
    }

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

    public override void OnStartServer()
    {
        base.OnStartServer();
        NetworkServer.RegisterHandler<JoinRequest>(OnJoinRequest);
        NetworkServer.RegisterHandler<HitRequest>(OnHitRequest);
        NetworkServer.RegisterHandler<HealClaim>(OnHealClaim);
        NetworkServer.RegisterHandler<ReviveRequest>(OnReviveRequest);
        NetworkServer.RegisterHandler<SkillStrikeRequest>(OnSkillStrikeRequest);
        NetworkServer.RegisterHandler<SkillCastRequest>(OnSkillCastRequest);
        NetworkServer.RegisterHandler<ItemUsedRequest>(OnItemUsedRequest);
        NetworkServer.RegisterHandler<ResolvedStrikeRequest>(OnResolvedStrikeRequest);
        NetworkServer.RegisterHandler<GmCommandRequest>(OnGmCommandRequest);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        NetworkClient.RegisterHandler<HitResult>(OnHitResult);
        NetworkClient.RegisterHandler<PlayerHitResult>(OnPlayerHitResult);
        NetworkClient.RegisterHandler<XpEarned>(OnXpEarned);
        NetworkClient.RegisterHandler<SkillCastPlayed>(OnSkillCastPlayed);
        NetworkClient.RegisterHandler<GmCommandReply>(OnGmCommandReply);
    }

    // A blow on a PERSON, arriving on every machine that can see them. Each one shows it in the way it honestly
    // can — see NetPlayer.ShowBlow.
    static void OnPlayerHitResult(PlayerHitResult msg)
    {
        if (!NetworkClient.spawned.TryGetValue(msg.victimNetId, out var identity) || identity == null) return;
        var shell = identity.GetComponent<NetPlayer>();
        if (shell == null) return;

        shell.ShowBlow(new DamageInfo(msg.amount, msg.element, msg.isMagic, msg.isCrit,
                                      attackerHit: msg.attackerHit, missed: msg.missed),
                       msg.mitigated);
    }

    static void OnXpEarned(XpEarned msg)
    {
        if (Character.Instance == null || msg.amount <= 0) return;
        Character.Instance.AddXP(msg.amount);
    }

    void OnHealClaim(NetworkConnectionToClient conn, HealClaim msg)
    {
        // THE SAME FLOOD GUARD EVERY OTHER CLAIM GOES THROUGH. Left off, this was the one message a machine could
        // send as fast as the wire allows: each one legally tops a character back up, so nothing the server decided
        // could ever bring them down. The guard is not a heal RATE — it sits far above any real one — it is the
        // ceiling on how many claims a second one connection may make at all.
        if (HealingTooFast(conn.connectionId)) return;
        if (msg.amount <= 0) return;
        var fighter = FighterFor(conn);
        if (fighter != null) fighter.Heal(msg.amount);
    }

    void OnReviveRequest(NetworkConnectionToClient conn, ReviveRequest msg)
    {
        // Only a dead person can be brought back, so a live one asking is refused rather than handed a free heal.
        var fighter = FighterFor(conn) as Character;

        // IT SAYS WHY IT REFUSED. This used to do nothing at all when it would not grant a revive, which from
        // the player's seat is a Respawn button that does not work and a death screen that never goes away, with
        // nothing anywhere saying so. A refusal that cannot be explained is the same as a crash you cannot see.
        if (fighter == null)
        {
            Debug.LogWarning("[server] somebody asked to be revived but this server holds no fighter for them.");
            return;
        }
        if (!fighter.IsDead)
        {
            Debug.LogWarning("[server] " + (conn.identity != null ? conn.identity.name : "somebody")
                             + " asked to be revived but this server has them alive on "
                             + fighter.CurrentHP + " health — nothing to do.");
            return;
        }
        Debug.Log("[server] reviving a player who was dead.");

        fighter.Revive();

        // AND COMING BACK MOVES THEM. A person stands up at a spawn point, which is nowhere near where they fell —
        // a jump the movement check would otherwise read as the largest lie it has ever seen, on every single
        // death. Forgiven from HERE because this is the server granting it: nothing a client says can reach it.
        var shell = conn.identity != null ? conn.identity.GetComponent<NetPlayer>() : null;
        if (shell != null) shell.ForgiveNextMoveAfterServerMove();
    }

    /// <summary>The body belonging to this character, on a server. Lets anything holding a CHARACTER reach the
    /// connection that owns it — a GM command acting on somebody's bag has to be able to tell them about it.
    /// Null off a server, and null for a character nobody is connected as.</summary>
    public static NetPlayer ShellFor(Character who)
    {
        if (who == null || !NetworkServer.active) return null;
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn == null || conn.identity == null) continue;
            var shell = conn.identity.GetComponent<NetPlayer>();
            if (shell != null && ReferenceEquals(shell.ServerCombatUnit, who)) return shell;
        }
        return null;
    }

    static ICombatUnit FighterFor(NetworkConnectionToClient conn)
    {
        var shell = conn != null && conn.identity != null ? conn.identity.GetComponent<NetPlayer>() : null;
        return shell != null ? shell.ServerCombatUnit : null;
    }

    /// <summary>Announce a blow the server decided landed on a person, to everybody who can see them. Called by the
    /// damage door rather than by whatever swung, so a creature's bite, a poison tick and anything written later all
    /// reach the screens through one line.</summary>
    public static void AnnounceBlowOnPlayer(Character victim, in DamageInfo info, int dealt)
    {
        if (!NetworkServer.active || victim == null) return;

        var identity = IdentityOfFighter(victim);
        if (identity == null) return;

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
            mitigated = victim.TakeMitigation()
        });
    }

    /// <summary>Announce a blow the server decided landed on a CREATURE, to everybody watching that creature. The
    /// exact mirror of <see cref="AnnounceBlowOnPlayer"/>, and it exists for the same reason: the server has no
    /// screen, so a blow it keeps to itself is a blow that appears on nobody's.
    ///
    /// <para><b>The gap this closes.</b> A joined player sends the WHOLE cast across and then resolves none of it,
    /// so every strike of a skill is landed by the server on its own initiative, with no request from anybody to
    /// answer. The handlers below each announce the blow they were ASKED for — there was nothing announcing the
    /// blows nobody asked for. The creature's health still reached every screen, because that is a synced number
    /// that is written straight in without drawing anything, which is exactly why the bar moved and no digits
    /// ever did.</para>
    ///
    /// <para><b>What it deliberately does nothing for.</b> Off a server. A victim that is a person — that is the
    /// line above, and announcing here as well would draw their number twice. And a creature no server published,
    /// which has no audience to announce to.</para></summary>
    /// <param name="showsTotal">Whether this strike belongs to a run that draws ONE rising yellow total rather
    /// than a number per beat. Worked out by the caller from the authored skill, never from a client's claim.</param>
    public static void AnnounceBlowOnMob(ICombatUnit victim, ICombatUnit attacker, in DamageInfo info, bool showsTotal)
    {
        if (!NetworkServer.active || victim == null) return;

        var body = victim.Body;
        var shell = body != null ? NetMob.ShellForServerMob(body.gameObject) : null;
        if (shell == null) return;

        var attackerIdentity = IdentityOfFighter(attacker as Character);

        NetworkServer.SendToReadyObservers(shell.netIdentity, new HitResult
        {
            targetNetId = shell.netId,
            attackerNetId = attackerIdentity != null ? attackerIdentity.netId : 0u,
            // THE BLOW AS IT WENT IN, not the number that came out of it. Every watching machine runs what arrives
            // through the same door the server did — armour, element chart and all — so handing over the finished
            // figure would have the victim's armour taken out of it a second time.
            amount = info.amount,
            element = info.element,
            isMagic = info.isMagic,
            isCrit = info.isCrit,
            missed = info.missed,
            attackerHit = info.attackerHit,
            overkill = info.overkill,
            showsTotal = showsTotal
        });
    }

    /// <summary>Tell the person a reward belongs to that they earned it. Silent off a server, and silent for a
    /// character that is not somebody's — so the single-machine game is untouched.</summary>
    public static void TellOwnerTheyEarned(Character who, int xp)
    {
        if (!NetworkServer.active || who == null || xp <= 0) return;

        var identity = IdentityOfFighter(who);
        if (identity == null || identity.connectionToClient == null) return;
        identity.connectionToClient.Send(new XpEarned { amount = xp });
    }

    /// <summary>Name an authored skill's strike to the server so IT can roll the damage. Returns TRUE when it was
    /// sent, which means the caller must not land anything itself.
    ///
    /// <para>Returns FALSE — leaving the caller to carry on as it always did — on the server, and for a strike whose
    /// skill cannot be named. The second case is the honest fallback: a strike that no authored skill claims cannot
    /// be re-rolled at the far end, so it goes down the older path that carries its own number.</para></summary>
    public static bool SendSkillStrikeToServer(SkillEffect e, IDamageable target, in StackVars vars, bool overkill)
    {
        // THE SERVER HAS NOBODY TO ASK, and that is not a fault worth a word. It lands the strike itself and
        // announces it (see AnnounceBlowOnMob), which is the ordinary case for every cast a joined player sends
        // across whole. Answered before the warning below, because that warning blamed "no shell for that body"
        // on a machine where the real reason was simply being the authority — a wrong reason printed once per
        // skill, in the one log somebody debugging this would read.
        if (NetworkServer.active) return false;

        var identity = SharedIdentityOf(target);
        if (identity == null)
        {
            // SAYS WHY IT DID NOTHING. A strike that is neither sent nor applied is silent — no error, no number,
            // nothing on screen — and silence has no stack trace. Whichever of the two reasons it is, it is said
            // once per skill rather than per strike, so a flurry cannot bury it.
            WhyNoStrike(PassiveEvents.CurrentSkillId, "the target is not a creature this server owns "
                        + "(no shell for that body), so there is nobody to ask");
            return false;
        }

        // WHICH skill is landing right now — already tracked for the passives that answer "this skill killed it",
        // so naming a strike costs no new bookkeeping anywhere.
        var def = SkillCatalog.Get(PassiveEvents.CurrentSkillId);
        if (!FindStrike(def, e, out int tick, out int index))
        {
            WhyNoStrike(PassiveEvents.CurrentSkillId, def == null
                ? "no skill is named as the cause right now, so the strike cannot say which one it belongs to"
                : "that damage effect is not one of '" + def.id + "'s own authored effects");
            return false;
        }

        NetworkClient.Send(new SkillStrikeRequest
        {
            targetNetId = identity.netId,
            skillId = def.id,
            tickIndex = tick,
            effectIndex = index,
            stackCount = vars.Count,
            stackCarried = vars.Carried,
            overkill = overkill
        });
        return true;
    }

    // Once per skill, not once per strike — a flurry raises dozens and a line each would bury the log.
    static readonly System.Collections.Generic.HashSet<string> saidWhyNoStrike = new System.Collections.Generic.HashSet<string>();

    static void WhyNoStrike(string skillId, string why)
    {
        string key = (skillId ?? "") + "|" + why;
        if (!saidWhyNoStrike.Add(key)) return;
        Debug.LogWarning("[client] a skill strike from '" + (string.IsNullOrEmpty(skillId) ? "(unnamed)" : skillId)
                         + "' was neither sent to the server nor shown: " + why + ".");
    }

    // WHERE this strike sits inside its skill. By reference, so it names the exact authored effect rather than one
    // that merely looks the same. Runs once per strike — never per frame — so a short walk is the right shape.
    static bool FindStrike(SkillDef def, SkillEffect e, out int tickIndex, out int effectIndex)
    {
        tickIndex = -1; effectIndex = -1;
        if (def == null || def.bindings == null || e == null) return false;

        for (int t = 0; t < def.bindings.Count; t++)
        {
            var effects = def.bindings[t] != null ? def.bindings[t].effects : null;
            if (effects == null) continue;
            int i = effects.IndexOf(e);
            if (i < 0) continue;
            tickIndex = t; effectIndex = i;
            return true;
        }
        return false;
    }

    /// <summary>Tell the server about health this player put back. Returns TRUE when it was sent — the caller must
    /// then NOT apply it here, for the same reason a swing is not applied here.</summary>
    public static bool SendHealToServer(int amount)
    {
        if (!WatchingOnly() || amount <= 0) return false;
        NetworkClient.Send(new HealClaim { amount = amount });
        return true;
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

    // The shell a server-held fighter belongs to. The fighter is built as a child of that shell (see PlayerFighter),
    // so this is a walk up the parents rather than a search — the same reason the creature side uses a table.
    static NetworkIdentity IdentityOfFighter(Character fighter)
    {
        var body = fighter != null ? fighter.transform : null;
        return body != null ? body.GetComponentInParent<NetworkIdentity>() : null;
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

    // A watching machine's half: show the blow the server just decided, using the SAME sink the offline game uses.
    // The presentation is not written a second time for the networked case — the resolved blow is simply handed to
    // the door every hit already went through.
    /// <summary>How many blows this machine has been told about, and how many it could not find a victim for.
    /// A probe: "the number did not show" has two very different causes — the announcement never arrived, or it
    /// arrived and was dropped — and nothing else on this machine can tell them apart.</summary>
    public static int HitResultsHeard;
    public static int HitResultsWithNoVictim;

    static void OnHitResult(HitResult msg)
    {
        HitResultsHeard++;
        if (!NetworkClient.spawned.TryGetValue(msg.targetNetId, out var identity) || identity == null) return;

        var shell = identity.GetComponent<NetMob>();
        var victim = shell != null ? shell.WatchedTarget : identity.GetComponent<IDamageable>();
        if (victim == null) { HitResultsWithNoVictim++; return; }

        int dealt = CombatAuthority.ApplyFromServer(victim, new DamageInfo(msg.amount, msg.element, msg.isMagic,
                                                              msg.isCrit,
                                                              attackerHit: msg.attackerHit, overkill: msg.overkill,
                                                              missed: msg.missed,
                                                              attacker: AttackerIfItWasMe(msg.attackerNetId)));

        // THE RISING YELLOW TOTAL, built from the numbers that actually arrived. Keyed by WHO struck — the shared
        // name every machine agrees on, not "was it me" — so two people flurrying one creature keep two climbing
        // numbers instead of pooling into a third that belongs to neither. The body it hangs over is the one this
        // machine can see, which is the watched copy the number was just drawn on.
        if (msg.showsTotal && dealt > 0)
        {
            var body = victim as ICombatUnit;
            if (body != null) RepeatsDamageTotal.ReportArrived(body.Body, dealt, msg.attackerNetId);
        }
    }

    /// <summary>The local fighter, but ONLY when the blow the server just announced was this keyboard's. Everyone
    /// receives every blow, and a hit teaches its attacker things — a run of consecutive hits, the on-damage-dealt
    /// passives, a lifesteal trickle — so handing my own character to somebody ELSE's swing would credit me for
    /// every blow anybody landed anywhere in sight. Null for a stranger's swing, which is the honest answer: this
    /// machine holds no copy of their fighter and has nothing of theirs to teach.</summary>
    static ICombatUnit AttackerIfItWasMe(uint attackerNetId)
    {
        if (attackerNetId == 0) return null;
        var me = NetworkClient.localPlayer;
        return me != null && me.netId == attackerNetId ? Character.Instance : null;
    }

    /// <summary>Send a hit to the server instead of applying it here. Returns TRUE when it was sent — which
    /// means the caller must NOT apply anything itself. Returns FALSE offline, or on the server, where the
    /// caller is the authority and should carry on as normal.
    ///
    /// <para>Lives on the net layer rather than in the combat door so that <see cref="CombatAuthority"/> keeps
    /// knowing nothing about wires: it asks one question and gets a yes or no.</para></summary>
    public static bool SendHitToServer(IDamageable target, in DamageInfo info)
    {
        var identity = SharedIdentityOf(target);
        if (identity == null) return false;

        // A SWING AND A SKILL ASK DIFFERENT QUESTIONS, and the game already tells them apart everywhere else by the
        // accuracy the attacker rolled: a real swing rolled one (zero or more), a skill strike says −1 for "always
        // lands". No new flag, no second way to ask.
        //
        // A swing says only WHAT it is hitting, because the server holds everything else about it. An authored SKILL
        // says only which skill and which of its strikes, for the same reason — and never reaches this line, because
        // it is sent from where the skill is known (SendSkillStrikeToServer). What is left here is an always-lands
        // strike with no skill behind it: a weapon's own flurry or cleave. Its damage is still the client's word.
        if (info.attackerHit < 0)
        {
            NetworkClient.Send(new ResolvedStrikeRequest
            {
                targetNetId = identity.netId,
                amount = info.amount,
                element = info.element,
                isMagic = info.isMagic,
                isCrit = info.isCrit,
                overkill = info.overkill
            });
            return true;
        }

        NetworkClient.Send(new HitRequest { targetNetId = identity.netId });
        return true;
    }

    /// <summary>The shared name for something both halves know about, or null when this machine is the authority,
    /// is offline, or the thing is purely local (a dummy a client made for itself, which no server has heard of).
    ///
    /// <para>ONE lookup, and it is a dictionary probe — every blow and every damage tick asks this, and PERFORMANCE.md
    /// bans a component search on that path. Anything networked registers itself in that table; there is deliberately
    /// no second, slower way to be found, because a fallback search runs on every MISS, which is exactly the common
    /// case of a creature hitting a player.</para></summary>
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

    static void WhyRefused(string skillId, string why)
    {
        string key = (skillId ?? "") + "|" + why;
        if (!saidWhyRefused.Add(key)) return;
        Debug.LogWarning("[server] refused a skill strike from '" + (string.IsNullOrEmpty(skillId) ? "(unnamed)" : skillId)
                         + "': " + why + ". Nothing was damaged and no number was shown.");
    }

    static void WarnNoFighter(int connectionId)
    {
        if (!warnedNoFighter.Add(connectionId)) return;
        Debug.LogWarning("[server] connection " + connectionId + " swung, but this server has no fighter for "
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

    // How many SKILL STRIKES a connection may send in one second. Its own budget rather than a share of the blow
    // ceiling above, for exactly the reason the heal ceiling has one: a skill legitimately sends a request PER HIT
    // PER CREATURE, so one flurry across a crowd is five hits times however many it caught — twenty-five of them
    // before anybody has done anything unusual. Sharing the twenty-a-second blow budget meant those extra strikes
    // were dropped in silence, mid-combo, with nothing on screen to say a hit had gone missing.
    //
    // The number is MY CALL and it is deliberately far above any real cast: nothing here is a rate a player can
    // reach, it is only the ceiling that stops one machine asking the server to resolve thousands a second.
    const int MaxSkillStrikesPerSecond = 200;

    // How many HEALS a connection may claim in one second. Its own ceiling rather than a share of the one above,
    // because the two are different actions at different rates: a flurry skill legitimately sends a request per
    // hit, and making a potion eat into that budget would silently drop real strikes mid-combo. Like the blow
    // ceiling, this sits far above anything a person can actually do.
    const int MaxHealsPerSecond = 10;

    class ClaimLedger { public float windowEnds; public int countedThisWindow; }

    static readonly System.Collections.Generic.Dictionary<int, ClaimLedger> swingLedgers =
        new System.Collections.Generic.Dictionary<int, ClaimLedger>();
    static readonly System.Collections.Generic.Dictionary<int, ClaimLedger> healLedgers =
        new System.Collections.Generic.Dictionary<int, ClaimLedger>();
    static readonly System.Collections.Generic.Dictionary<int, ClaimLedger> skillLedgers =
        new System.Collections.Generic.Dictionary<int, ClaimLedger>();

    static bool SwingingTooFast(int connectionId) => TooMany(swingLedgers, connectionId, MaxHitsPerSecond);
    static bool HealingTooFast(int connectionId) => TooMany(healLedgers, connectionId, MaxHealsPerSecond);
    static bool CastingTooFast(int connectionId) => TooMany(skillLedgers, connectionId, MaxSkillStrikesPerSecond);

    // One counter, used by every ledger — so a new kind of claim gets a flood guard by adding a ceiling and a
    // dictionary, not by writing the counting again and getting it subtly different.
    static bool TooMany(System.Collections.Generic.Dictionary<int, ClaimLedger> ledgers, int connectionId, int ceiling)
    {
        if (!ledgers.TryGetValue(connectionId, out var ledger))
            ledgers[connectionId] = ledger = new ClaimLedger();

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

    // The server's side: resolve the hit for real, on the one copy of the world that counts.
    void OnHitRequest(NetworkConnectionToClient conn, HitRequest msg)
    {
        // NOTHING a client says is taken on trust — not what it is hitting, not how often, not from how far. A
        // request names a target and nothing else; every number and every permission is decided here.
        if (SwingingTooFast(conn.connectionId)) return;

        if (!NetworkServer.spawned.TryGetValue(msg.targetNetId, out var identity) || identity == null) return;

        var shell = identity.GetComponent<NetMob>();
        var victim = shell != null && shell.ServerMob != null
            ? shell.ServerMob.GetComponent<IDamageable>()
            : identity.GetComponent<IDamageable>();
        if (victim == null) return;

        var attacker = conn.identity != null ? conn.identity.GetComponent<NetPlayer>() : null;
        var swinger = attacker != null ? attacker.ServerCombatUnit : null;
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

        // Reach is checked AFTER the fighter is found, because it is the fighter that has a reach at all.
        if (!WithinReach(swinger, victim)) return;

        // THE SERVER RUNS THE SWING ITSELF — through the ONE implementation of what a swing is, the same one a
        // skill strike authored as a basic attack goes through. It used to roll its own crit and damage inline
        // here, which was a second copy of the swing: it silently skipped everything else a swing does, so a run
        // of hits was never counted, a boost charge was never spent, the impact cue never played, and — the reason
        // this was found — the two moments a swing raises, "about to swing" and "a swing landed", fired on NOBODY.
        // Every passive keyed to either of them was dead in a shared world.
        //
        // It announces the intent here because the machine that swung can no longer do it: its own announcement
        // fires against a character it does not decide for, so nothing comes of it.
        BasicSwing.Land(swinger, victim as ICombatUnit, ScaledNumber.Of(1f), StackVars.None,
                        outer: 1f, overkill: false, announceIntent: true, out var resolved);

        // …and then TELL EVERYONE. The server has no screen, so a blow it keeps to itself is a blow nobody sees.
        NetworkServer.SendToReadyObservers(identity, new HitResult
        {
            targetNetId = msg.targetNetId,
            attackerNetId = conn.identity != null ? conn.identity.netId : 0u,
            amount = resolved.amount,
            element = resolved.element,
            isMagic = resolved.isMagic,
            isCrit = resolved.isCrit,
            missed = resolved.missed,
            attackerHit = resolved.attackerHit,
            overkill = false
        });
    }

    // How far a skill may reach before the server calls it impossible. Deliberately generous: skills range from a
    // touch to a bolt across a clearing and the server does not yet know which is which, so this is only there to
    // stop a blow aimed at something on the far side of the map. It is NOT a skill's range — the skill's own range
    // is checked on the machine that ran it, and moving that check here is part of resolving skills server-side.
    const float SkillReachCeiling = 30f;

    /// <summary>THE SERVER ROLLS THE SKILL. It is told which skill and which of that skill's strikes just landed on
    /// what — and works out how hard from the authored data and the character it holds, exactly as the player's own
    /// machine would have.</summary>
    void OnSkillStrikeRequest(NetworkConnectionToClient conn, SkillStrikeRequest msg)
    {
        // EVERY REFUSAL BELOW SAYS SO. A strike that is dropped here is silent from the player's seat — the skill
        // plays, nothing takes damage, no number appears, and no log anywhere mentions it. Silence has no stack
        // trace, so each way out names itself, once per reason per skill.
        if (CastingTooFast(conn.connectionId)) { WhyRefused(msg.skillId, "asked for faster than the ceiling allows"); return; }

        var def = SkillCatalog.Get(msg.skillId);
        if (def == null || def.bindings == null)
        {
            WhyRefused(msg.skillId, "this server has no such skill in its catalog");
            return;
        }
        if (msg.tickIndex < 0 || msg.tickIndex >= def.bindings.Count)
        {
            WhyRefused(msg.skillId, "tick " + msg.tickIndex + " is outside that skill's " + def.bindings.Count + " tick(s)");
            return;
        }

        var effects = def.bindings[msg.tickIndex] != null ? def.bindings[msg.tickIndex].effects : null;
        if (effects == null || msg.effectIndex < 0 || msg.effectIndex >= effects.Count)
        {
            WhyRefused(msg.skillId, "effect " + msg.effectIndex + " is outside that tick's effects");
            return;
        }

        var effect = effects[msg.effectIndex];
        // ONLY A STRIKE may be asked for here. Naming any other kind of effect would have the server land a
        // damage roll for something authored to heal or to buff.
        if (effect == null || effect.kind != SkillEffectKind.Damage)
        {
            WhyRefused(msg.skillId, "that effect is not a Damage effect");
            return;
        }

        if (!NetworkServer.spawned.TryGetValue(msg.targetNetId, out var identity) || identity == null)
        {
            WhyRefused(msg.skillId, "the target it named is not anything this server is holding");
            return;
        }
        var shell = identity.GetComponent<NetMob>();
        var victim = (shell != null && shell.ServerMob != null
            ? shell.ServerMob.GetComponent<IDamageable>()
            : identity.GetComponent<IDamageable>()) as ICombatUnit;
        if (victim == null) { WhyRefused(msg.skillId, "the target it named cannot be hurt"); return; }

        var caster = FighterFor(conn) as Character;
        if (caster == null) { WarnNoFighter(conn.connectionId); return; }
        if (!Owns(caster, msg.skillId)) { WarnNotTheirSkill(conn.connectionId, msg.skillId); return; }

        var casterBody = caster.Body;
        var victimBody = victim.Body;
        if (casterBody == null || victimBody == null)
        {
            WhyRefused(msg.skillId, "one of the two bodies does not exist on this server");
            return;
        }
        if ((victimBody.position - casterBody.position).sqrMagnitude > SkillReachCeiling * SkillReachCeiling)
        {
            WhyRefused(msg.skillId, "the target is further than the ceiling of " + SkillReachCeiling + " units away");
            return;
        }

        // THE PILE IS IGNORED UNLESS THIS STRIKE ACTUALLY READS ONE. The reading is the single thing a client still
        // states that can move a number, so it is accepted only where the authored data says it means something —
        // which is three skills in the whole game. Everywhere else the client's figure is dropped on the floor.
        var vars = effect.damageMult.useStacks
            ? new StackVars(Mathf.Max(0, msg.stackCount), msg.stackCarried)
            : StackVars.None;

        SkillEffectApplier.Strike(effect, victim, caster, vars, msg.overkill, out _, out var resolved);

        // ANNOUNCED EXACTLY AS IT WAS RESOLVED, never rebuilt from the authored effect. Every watching machine runs
        // the blow through the same door the server did — armour, element chart and all — so what travels is the
        // blow as it went IN, not the number that came out of it. Describing it from the effect instead got three
        // things wrong at once: it stated the finished number, so armour was taken off twice and every number on
        // screen read low; and for a strike that swings the weapon it named the skill's element and could never
        // report a crit or a whiff, because those belong to the swing rather than to the skill that asked for it.
        NetworkServer.SendToReadyObservers(identity, new HitResult
        {
            targetNetId = msg.targetNetId,
            attackerNetId = conn.identity != null ? conn.identity.netId : 0u,
            amount = resolved.amount,
            element = resolved.element,
            isMagic = resolved.isMagic,
            isCrit = resolved.isCrit,
            missed = resolved.missed,
            attackerHit = resolved.attackerHit,
            overkill = msg.overkill,
            // WHETHER A TOTAL WAS OWED, read off the authored skill on the machine that holds it. The two things
            // that draw one have always been these: a run that asked to show ONE number across all its repeats, and
            // a strike that lands more than once. Read here rather than sent, so it stays the skill's decision.
            showsTotal = SkillEffectApplier.ShowsRunTotal(def, effect, caster, vars)
        });
    }

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
    void OnSkillCastRequest(NetworkConnectionToClient conn, SkillCastRequest msg)
    {
        if (CastingTooFast(conn.connectionId)) return;

        var def = SkillCatalog.Get(msg.skillId);
        if (def == null || def.IsPassive) return;   // a passive has no pressed form: it fires from its own moments, here

        var caster = FighterFor(conn) as Character;
        if (caster == null) { WarnNoFighter(conn.connectionId); return; }
        if (!Owns(caster, msg.skillId)) { WarnNotTheirSkill(conn.connectionId, msg.skillId); return; }

        var runner = PlayerFighter.RunnerFor(caster, msg.skillId);
        if (runner == null) return;

        // WHERE THEY AIMED, put back into this machine's copy of their map.
        Vector3 point = MapManager.InWorld(msg.aimPoint, caster.Map);

        var casterBody = caster.Body;
        if (casterBody == null) return;
        if ((point - casterBody.position).sqrMagnitude > SkillReachCeiling * SkillReachCeiling) return;

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

        // The cast itself announces each of its plays as they start (see AnnounceCastPlayed) — including its
        // repeats, so a shower's later falls reach every screen too, rather than only its first.
        runner.CastFromRequest(aimed, point);
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

        // WHAT IS ACTUALLY THERE. The message says where in the bag, and the item is read off the bag the SERVER
        // holds — so a request to drink something the person does not own resolves to nothing, and there is no
        // item name on the wire for anybody to substitute.
        if (msg.bagIndex < 0 || msg.bagIndex >= caster.Bag.Count) return;
        var entryInBag = caster.Bag[msg.bagIndex];
        var item = entryInBag != null ? entryInBag.def : null;
        if (item == null || !item.IsUsable) return;
        // AND STILL THE ONE THEY MEANT. Without this, a second click landing inside the first one's round trip
        // drinks whatever moved into that position.
        if (!string.IsNullOrEmpty(msg.expectedItemId) && item.id != msg.expectedItemId) return;

        // THE ITEM NAMES THE SKILL, and the server reads that off the item it holds — never off the message.
        var runner = PlayerFighter.RunnerFor(caster, item.useSkillId);
        var entry = runner != null && runner.def != null ? runner.def.ItemUseEntry : null;
        if (entry == null) return;

        runner.FireAsPassive(entry, null);

        // AND IT IS SPENT HERE, where it was drunk. The bag belongs to the server now, so a stack reduced only on
        // the drinker's own machine would be put straight back the next time the server said what they were
        // carrying — a potion that heals you and is never used up.
        if (--entryInBag.count <= 0) caster.Bag.RemoveAt(msg.bagIndex);

        var shell = conn.identity != null ? conn.identity.GetComponent<NetPlayer>() : null;
        shell?.PushBagToOwner();
        // A drunk potion is a change like any other. Without this it was written down only if something ELSE
        // happened to dirty the character, so a server restart handed the potions back.
        shell?.SaveSoon();
    }

    /// <summary>Say that one play of a cast is starting NOW, to every machine that can see the caster — the caster's
    /// own included. It carries the seed, which is the point of it: where a scattered shot lands is where its damage
    /// is measured from, so the picture and the decision must be rolled from the same number.
    ///
    /// <para>Sent per PLAY rather than per cast, because a repeating skill is several plays and each one scatters
    /// again. Silent off a server, so the single-machine game is untouched.</para></summary>
    public static void AnnounceCastPlayed(Character caster, SkillDef def, Transform aimedAt, Vector3 aimPoint, int seed)
    {
        if (!NetworkServer.active || caster == null || def == null) return;
        var identity = IdentityOfFighter(caster);
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

    // What the cast was aimed at, as the shared thing that speaks for it — asked on the SERVER, where a creature is
    // the real one and its shell is the parent that carries the identity.
    static uint IdentityOfServerSide(Transform aimed)
    {
        if (aimed == null) return 0u;
        var identity = aimed.GetComponentInParent<NetworkIdentity>();
        return identity != null ? identity.netId : 0u;
    }

    // Somebody else's skill, arriving as a thing to LOOK at. Nothing here decides anything: the effect plays on
    // their body and every consequence of it reaches this machine through the doors that already announce one.
    static void OnSkillCastPlayed(SkillCastPlayed msg)
    {
        if (!NetworkClient.spawned.TryGetValue(msg.casterNetId, out var identity) || identity == null) return;
        var shell = identity.GetComponent<NetPlayer>();
        if (shell == null) return;

        Transform aimed = null;
        if (msg.targetNetId != 0u && NetworkClient.spawned.TryGetValue(msg.targetNetId, out var at) && at != null)
            aimed = at.transform;

        shell.ShowCast(SkillCatalog.Get(msg.skillId), aimed, msg.aimPoint, msg.seed);
    }

    // SAID OUT LOUD, once per connection, for the same reason a missing fighter is: the refusal itself is silent —
    // the strike simply does not land — and the honest cause is usually not cheating at all but a character whose
    // progress never arrived, leaving the server holding somebody with no weapon and therefore no skills.
    static readonly System.Collections.Generic.HashSet<string> warnedNotTheirSkill = new System.Collections.Generic.HashSet<string>();

    static void WarnNotTheirSkill(int connectionId, string skillId)
    {
        // KEYED BY THE SKILL AS WELL AS THE PERSON. Said once per CONNECTION, the first refusal silenced every
        // other skill that person cast for the rest of their session — so a log with one line in it looked like
        // one broken skill when it was all of them.
        if (!warnedNotTheirSkill.Add(connectionId + "|" + skillId)) return;
        Debug.LogWarning("[server] connection " + connectionId + " cast '" + skillId + "', which is neither bound on "
                         + "the hotbar this server holds for them nor on their equipped weapon, so the strike was "
                         + "refused. Either their character's progress never loaded (they are standing here at "
                         + "starting numbers, holding nothing), or the skill was never theirs.");
    }

    // IS THIS SKILL EVEN THEIRS? Without asking, the server would roll ANY skill in the catalog for anybody who
    // named one — the whole game's damage available to a machine that never earned it.
    //
    // THE QUESTION IS "DOES THIS CHARACTER HAVE IT TO HAND", AND IT HAS TWO ANSWERS, because a skill reaches a
    // character two different ways:
    //   - it is BOUND — sitting in one of the numbered slots or the ultimate slot. This is how an active is cast.
    //   - it is ON THE EQUIPPED WEAPON — how a passive arrives, since a passive takes no slot at all.
    // Either one is enough. Both are read off the character the SERVER holds, restored from saved progress the
    // account service handed it, so neither is a client's word about itself.
    //
    // ASKING ONLY THE WEAPON WAS WRONG AND BROKE THE GAME (2026-08-30). A character can carry bound skills with
    // nothing equipped — a real one did, with three on the bar and an empty gear list — so every skill they cast
    // was refused and nothing took damage. The lesson is bigger than the line: this check must only enforce a rule
    // the GAME already enforces. Inventing a stricter one here does not harden the server, it breaks play.
    //
    // Only OWNERSHIP is settled here. Whether the skill was off cooldown and whether its cost was paid are still
    // decided on the player's own machine, because neither a cooldown nor a mana bill exists on the server yet.
    static bool Owns(Character caster, string skillId)
    {
        if (caster == null || string.IsNullOrEmpty(skillId)) return false;

        if (caster.Hotbar != null && (caster.Hotbar.IsBound(skillId) || caster.Hotbar.Ult == skillId)) return true;

        var weapon = caster.GetEquippedInstance(EquipSlotType.Weapon);
        var kit = weapon != null ? weapon.skillIds : null;
        if (kit == null) return false;
        for (int i = 0; i < kit.Count; i++) if (kit[i] == skillId) return true;
        return false;
    }

    void OnResolvedStrikeRequest(NetworkConnectionToClient conn, ResolvedStrikeRequest msg)
    {
        if (SwingingTooFast(conn.connectionId)) return;
        if (!NetworkServer.spawned.TryGetValue(msg.targetNetId, out var identity) || identity == null) return;

        var shell = identity.GetComponent<NetMob>();
        var victim = shell != null && shell.ServerMob != null
            ? shell.ServerMob.GetComponent<IDamageable>()
            : identity.GetComponent<IDamageable>();
        if (victim == null) return;

        var caster = FighterFor(conn);
        if (caster == null) { WarnNoFighter(conn.connectionId); return; }

        var casterBody = caster.Body;
        var victimUnit = victim as ICombatUnit;
        var victimBody = victimUnit != null ? victimUnit.Body : null;
        if (casterBody == null || victimBody == null) return;
        if ((victimBody.position - casterBody.position).sqrMagnitude > SkillReachCeiling * SkillReachCeiling) return;

        // A skill always lands — that is what the −1 accuracy means everywhere else in the game — so no roll
        // happens here. The strike is applied exactly as it was resolved, through the same door as everything else.
        var resolved = new DamageInfo(msg.amount, msg.element, msg.isMagic, msg.isCrit,
                                      attackerHit: -1, overkill: msg.overkill, attacker: caster);
        CombatAuthority.Apply(victim, resolved);

        NetworkServer.SendToReadyObservers(identity, new HitResult
        {
            targetNetId = msg.targetNetId,
            attackerNetId = conn.identity != null ? conn.identity.netId : 0u,
            amount = resolved.amount,
            element = resolved.element,
            isMagic = resolved.isMagic,
            isCrit = resolved.isCrit,
            missed = false,
            attackerHit = -1,
            overkill = msg.overkill
        });
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

        var asker = conn != null && conn.identity != null ? conn.identity.GetComponent<NetPlayer>() : null;
        if (asker == null) return;   // no body here yet; there is nobody for a command to act as

        // 1. IS THIS PERSON TRUSTED. Read off what the account service told this server at the door — never off
        //    anything the request said about itself, exactly as a player's name and body already are. A game that
        //    has been tampered with can send this message; it cannot make this answer true.
        if (!asker.ServerIsGm) { Answer(GmCommands.NotAGm); return; }

        // 2. IS THIS EVEN OURS TO RUN. Only the commands that change the shared world belong here. Anything else
        //    arriving is either an old game or a poked one, and running it would act on whichever character this
        //    server happened to touch last.
        if (!GmCommands.ChangesTheWorld(msg.line)) { Answer("That command runs in your own game, not here."); return; }

        // 3. AND AS WHOM. A server holds as many people as have signed in, so the command acts on the person who
        //    asked — their character, standing where they are standing — never on "the player", which is a
        //    question with no single answer here.
        GmCommands.ExecuteAsAsker(msg.line, asker.ServerCombatUnit as Character, asker.transform, Answer);

        Debug.Log("[server] " + asker.playerName + " ran '" + msg.line + "'");
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
            Debug.LogWarning("[server] refused a connection nobody announced — no good ticket.");
            conn.Disconnect();
            return;
        }

        // AND IT MUST BE THE CHARACTER THEY WERE ANNOUNCED WITH. The name and the body on their message are
        // only what to draw; anything that decides something is read off what the character server sent.
        if (PlayerFighter.AlreadyPlaying(announced.characterId))
        {
            Debug.LogWarning("[server] refused a second connection for a character already being played.");
            conn.Disconnect();
            return;
        }

        var go = Instantiate(playerPrefab);
        var np = go.GetComponent<NetPlayer>();
        if (np != null)
        {
            np.playerName = string.IsNullOrWhiteSpace(announced.characterName) ? "Player" : announced.characterName;
            np.modelName = msg.modelName;
        }
        NetworkServer.AddPlayerForConnection(conn, go);

        // AND A CHARACTER TO GO WITH THE BODY — the one that was PUSHED here, already in hand. Nothing is
        // fetched: this server never asks the character store for anything, which is the whole point of the
        // character server announcing somebody before sending them.
        PlayerFighter.BuildFrom(np, announced);

        Debug.Log("[server] " + (np != null ? np.playerName : "someone") + " entered the world — "
                  + NetworkServer.connections.Count + " player(s) here");
    }

    // ---- what each side reports -------------------------------------------------------------------------
    // A server has no screen, so a connection arriving is only visible if it says so. These are the lines that
    // turn "it should be working" into evidence.

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);
        Debug.Log("[server] player connected — connection " + conn.connectionId
                  + ", " + NetworkServer.connections.Count + " now on this world");
    }

    /// <summary>EVERYBODY GETS WRITTEN DOWN WHEN THIS SERVER STOPS. Without it a clean shutdown threw away
    /// whatever had happened since each player's last beat — up to fifteen seconds each, for everybody at once,
    /// on the one event an operator triggers deliberately.</summary>
    public override void OnStopServer()
    {
        foreach (var conn in NetworkServer.connections.Values)
        {
            var shell = conn != null && conn.identity != null ? conn.identity.GetComponent<NetPlayer>() : null;
            shell?.SaveNow(null);
        }
        Debug.Log("[server] stopping — asked for a write for every player still here.");
        base.OnStopServer();
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // COUNTED AFTER, not guessed at. Taking one off the current count assumed this connection was still in
        // it, and by this point it sometimes is not — so the number went NEGATIVE as people left.
        Debug.Log("[server] player left — connection " + conn.connectionId);

        // Everything remembered ABOUT a connection goes when the connection does. A long-lived server hands out
        // connection numbers for months; anything kept per connection and never dropped grows forever.
        warnedNoFighter.Remove(conn.connectionId);
        swingLedgers.Remove(conn.connectionId);
        healLedgers.Remove(conn.connectionId);

        // WRITTEN DOWN BEFORE THEY ARE LET GO. This is the moment everything that happened to them has to
        // survive — and the order matters: a character released while its last write is still in flight is how
        // the same items end up in two places, which is the oldest duplication trick there is. The write is
        // asked for first, and the character is dismissed only once it has landed.
        var leaving = conn.identity != null ? conn.identity.GetComponent<NetPlayer>() : null;
        if (leaving != null)
        {
            // THE NAME AND THE ID, TAKEN NOW. Everything below this runs after Mirror has destroyed the body, so
            // reading anything off it inside the answer would be reading a destroyed object — which is exactly
            // what happened: the character was never released, and its owner was refused as "already playing"
            // for the life of the server. Copied out here, while it still exists.
            string whoLeft = leaving.playerName;
            string theirCharacter = leaving.ServerCharacterId;

            // AND THE NAME IS FREED IMMEDIATELY, not when the write lands. Holding it until then means a person
            // whose save is slow — or whose character server is down — cannot come back at all, and the write
            // has nothing to do with whether somebody may play again.
            PlayerFighter.Dismiss(leaving);

            leaving.SaveNow(ok =>
            {
                if (!ok) Debug.LogError("[server] " + whoLeft + " left and their character could NOT be "
                                        + "written down. Their last few seconds are lost.");
                // Said again for the case the line above cannot cover: if anything else released the body
                // first, the name still has to come free.
                PlayerFighter.ReleaseCharacter(theirCharacter);
            });
        }

        base.OnServerDisconnect(conn);
        Debug.Log("[server] " + NetworkServer.connections.Count + " player(s) still here");
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Debug.Log("[client] connected to the server");

        // A fresh start — but only once this connection has actually been let INTO the world, which is what the
        // join below asks for. Reset merely on the wire coming up, a REFUSED join looked like a success: the
        // count went back to zero, the refusal arrived, and the retry began again from one, forever. MEASURED: a
        // player who had already been in the world once printed "retrying 1 of 3" more than thirty times and
        // never gave up, leaving them under a veil with no way out.
        //
        // It is cleared instead when the server confirms them (see OnJoinedWorld's caller).
        ReconnectNotice.Hide();
        ReconnectNotice.Hide();

        // Announce who is playing, so the server can give this connection the right body. Sent from here
        // rather than at sign-in because a connection is the thing that needs a body, and a connection is
        // what has just come into existence.
        NetworkClient.Send(new JoinRequest
        {
            characterName = AccountSession.CharacterName ?? "Player",
            modelName = AccountSession.ModelName ?? "",
            characterId = AccountSession.CharacterId ?? "",
            ticket = AccountSession.EntryTicket ?? ""
        });

        // ACTUALLY IN, so the retry count starts fresh. Cleared here rather than when the wire came up, because
        // the wire coming up is also what happens a moment before being refused.
        attemptsSpent = 0;

        // The world may exist now, and not one moment before this.
        OnJoinedWorld?.Invoke();
    }

    public override void OnClientDisconnect()
    {
        Debug.Log("[client] disconnected from the server");
        base.OnClientDisconnect();

        // Mirror reports a REFUSED first attempt and a DROPPED live connection through this same door, which is
        // exactly right for us: "we are not in a world" is one condition however we arrived at it, and it gets one
        // answer — try again, then give up.
        if (GameServer.IsServer || goneForGood) return;

        attemptsSpent++;
        if (attemptsSpent < JoinAttempts)
        {
            Debug.Log("[client] lost the world — retrying (" + attemptsSpent + " of " + JoinAttempts + ")");
            ReconnectNotice.Show();
            StartCoroutine(DialAgainShortly());
            return;
        }

        goneForGood = true;
        ReconnectNotice.Hide();
        Debug.LogWarning("[client] gave up after " + JoinAttempts + " attempts — leaving the world");
        OnLostWorld?.Invoke();
    }

    System.Collections.IEnumerator DialAgainShortly()
    {
        yield return new WaitForSeconds(SecondsBetweenAttempts);
        if (goneForGood) yield break;

        // A FRESH TICKET, not the spent one. A ticket opens the door exactly once — that is the whole point of
        // it — so dialling again with the same one is refused every single time, and the retry could never have
        // succeeded. Asking the character server again also re-announces this person to the map server, which
        // is what actually has to happen for them to be let back in.
        AskWhereToEnter(Transport.active as kcp2k.KcpTransport);
    }

    /// <summary>Put this copy back to knowing nothing, so a later sign-in dials a genuinely fresh connection
    /// instead of finding a half-dead one still sitting there. Called by <see cref="GameRoot"/> as it tears the
    /// world down.</summary>
    public static void Forget()
    {
        if (spawned == null) return;
        if (NetworkClient.active) spawned.StopClient();
        Destroy(spawned.gameObject);
        spawned = null;
        mobPrefab = null;
    }
}
