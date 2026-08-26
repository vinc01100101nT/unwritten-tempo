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

    /// <summary>The body spawned for each connected player, found in Resources by this name.</summary>
    public const string PlayerPrefabName = "NetPlayer";

    /// <summary>The shell spawned for each mob the server owns, found in Resources by this name.</summary>
    public const string MobPrefabName = "NetMob";

    /// <summary>True once this game is part of a networked world — either running it or connected to one.
    /// FALSE in plain single-player, which is what keeps the offline game behaving exactly as it always has:
    /// with no server in the picture, this machine is the only authority there is and everything that used
    /// to run locally still does.</summary>
    public static bool IsConnectedToAServer =>
        NetworkServer.active || (NetworkClient.active && NetworkClient.isConnected);

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
        spawned.networkAddress = Environment.GetEnvironmentVariable(HostEnvVar) ?? DefaultHost;

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

        // The server hands out bodies itself, when a client says who it is (see OnJoinRequest). Mirror's
        // automatic version would spawn one before we know which character to build.
        spawned.autoCreatePlayer = false;

        if (GameServer.IsServer)
        {
            spawned.StartServer();
            Debug.Log("[server] listening for players on port " + kcp.port);
        }
        else
        {
            Debug.Log("[client] dialling " + spawned.networkAddress + ":" + kcp.port);
            spawned.StartClient();
        }
    }

    static ushort PortFromEnvironment()
    {
        string raw = Environment.GetEnvironmentVariable(PortEnvVar);
        return ushort.TryParse(raw, out ushort p) && p != 0 ? p : DefaultPort;
    }

    /// <summary>What a client says on arrival: who it is playing. The server cannot know this by itself — the
    /// account service told the CLIENT which character it chose — so the client states it and the server spawns
    /// a body to match. Deliberately just identity: nothing here is trusted for anything that affects gameplay.</summary>
    public struct JoinRequest : NetworkMessage
    {
        public string characterName;
        public string modelName;
    }

    /// <summary>A player saying "I am hitting that". Deliberately carries WHO and WHAT KIND of blow — never
    /// how much it hurt. The server owns the numbers; a client that could name its own damage could name any
    /// number it liked.</summary>
    public struct HitRequest : NetworkMessage
    {
        public uint targetNetId;
        public bool isMagic;
        public Element element;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        NetworkServer.RegisterHandler<JoinRequest>(OnJoinRequest);
        NetworkServer.RegisterHandler<HitRequest>(OnHitRequest);
    }

    /// <summary>Send a hit to the server instead of applying it here. Returns TRUE when it was sent — which
    /// means the caller must NOT apply anything itself. Returns FALSE offline, or on the server, where the
    /// caller is the authority and should carry on as normal.
    ///
    /// <para>Lives on the net layer rather than in the combat door so that <see cref="CombatAuthority"/> keeps
    /// knowing nothing about wires: it asks one question and gets a yes or no.</para></summary>
    public static bool SendHitToServer(IDamageable target, in DamageInfo info)
    {
        if (NetworkServer.active) return false;                 // I AM the authority — apply it for real
        if (!NetworkClient.active || !NetworkClient.isConnected) return false;   // offline — unchanged game

        // Only something the server also knows about can be hit. A purely local object (a training dummy a
        // client made for itself) has no shared identity, so there is nothing to name across the wire.
        //
        // The shell lookup comes FIRST and is a dictionary hit, because the overwhelmingly common case — a
        // player swinging at a mob — resolves there. Walking up the object's parents looking for a network
        // identity is a component search on the per-hit path, which PERFORMANCE.md bans, so it is only reached
        // for the rare thing that is networked without being a mob.
        var comp = target as Component;
        var shell = comp != null ? NetMob.ShellForView(comp.gameObject) : null;
        var identity = shell != null ? shell.netIdentity
                                     : (comp != null ? comp.GetComponentInParent<NetworkIdentity>() : null);
        if (identity == null) return false;

        NetworkClient.Send(new HitRequest
        {
            targetNetId = identity.netId,
            isMagic = info.isMagic,
            element = info.element
        });
        return true;
    }

    // Said ONCE per connection, not once per swing: a player attacking holds the button down, and a line per
    // blow would bury the log it is trying to warn from.
    static readonly System.Collections.Generic.HashSet<int> warnedNoFighter = new System.Collections.Generic.HashSet<int>();

    static void WarnNoFighter(int connectionId)
    {
        if (!warnedNoFighter.Add(connectionId)) return;
        Debug.LogWarning("[server] connection " + connectionId + " swung, but this server has no fighter for "
                         + "them yet, so the blow was refused. Player stats are still tied to one global "
                         + "character; each connection needs its own before the server can resolve combat.");
    }

    // The server's side: resolve the hit for real, on the one copy of the world that counts.
    void OnHitRequest(NetworkConnectionToClient conn, HitRequest msg)
    {
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

        // The SERVER rolls, using the same shared functions the offline game uses — the formulas are not
        // duplicated for the networked case, they are simply run somewhere trustworthy.
        bool crit = swinger.RollBasicCrit(victim as ICombatUnit);
        int damage = swinger.RollBasicDamage(crit);

        CombatAuthority.Apply(victim, new DamageInfo(damage, msg.element, msg.isMagic, crit,
                                                    attackerHit: swinger.BasicHit, attacker: swinger));
    }

    // A body per connection, built from the identity that connection announced.
    void OnJoinRequest(NetworkConnectionToClient conn, JoinRequest msg)
    {
        if (conn.identity != null) return;   // already has a body; ignore a repeat

        var go = Instantiate(playerPrefab);
        var np = go.GetComponent<NetPlayer>();
        if (np != null)
        {
            np.playerName = string.IsNullOrWhiteSpace(msg.characterName) ? "Player" : msg.characterName;
            np.modelName = msg.modelName;
        }
        NetworkServer.AddPlayerForConnection(conn, go);

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

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        Debug.Log("[server] player left — connection " + conn.connectionId
                  + ", " + (NetworkServer.connections.Count - 1) + " remaining");
        base.OnServerDisconnect(conn);
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Debug.Log("[client] connected to the server");

        // Announce who is playing, so the server can give this connection the right body. Sent from here
        // rather than at sign-in because a connection is the thing that needs a body, and a connection is
        // what has just come into existence.
        NetworkClient.Send(new JoinRequest
        {
            characterName = AccountSession.CharacterName ?? "Player",
            modelName = AccountSession.ModelName ?? ""
        });
    }

    public override void OnClientDisconnect()
    {
        Debug.Log("[client] disconnected from the server");
        base.OnClientDisconnect();
    }
}
