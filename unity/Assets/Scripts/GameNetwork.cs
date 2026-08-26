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

    static GameNetwork spawned;

    /// <summary>Bring the net layer up. Called once, by <see cref="GameRoot"/>, at the moment the world starts
    /// existing — which on a server is boot, and on a client is entering the world after sign-in. One caller
    /// means the two sides can never be started by different rules.</summary>
    public static void Begin()
    {
        if (spawned != null) return;

        var go = new GameObject("GameNetwork") { hideFlags = HideFlags.DontSaveInEditor };
        spawned = go.AddComponent<GameNetwork>();

        var kcp = go.AddComponent<kcp2k.KcpTransport>();
        kcp.port = PortFromEnvironment();

        spawned.transport = kcp;
        Transport.active = kcp;
        spawned.networkAddress = Environment.GetEnvironmentVariable(HostEnvVar) ?? DefaultHost;

        // The body every connected player is seen as. Loaded by NAME from Resources so both halves resolve
        // the same asset with no scene wiring — the same way the skill and monster catalogs already load.
        spawned.playerPrefab = Resources.Load<GameObject>(PlayerPrefabName);
        if (spawned.playerPrefab == null)
            Debug.LogError("[net] no '" + PlayerPrefabName + "' prefab in Resources — players will connect "
                           + "but nobody will have a body.");

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

    public override void OnStartServer()
    {
        base.OnStartServer();
        NetworkServer.RegisterHandler<JoinRequest>(OnJoinRequest);
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
