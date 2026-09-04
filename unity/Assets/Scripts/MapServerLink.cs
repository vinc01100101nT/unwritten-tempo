using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// The map server's end of its link with the character server.
///
/// <para><b>The order this exists to reverse.</b> A map server used to FETCH a character over the network with
/// the player's own sign-in proof — so the thing running the world talked to the character store, and a
/// client's token was the key that opened it. Now the character server PUSHES: it tells this server that a
/// person is arriving and hands over their whole character in the same breath, and only then is that person
/// told where to knock. A connection that turns up unannounced is refused before it is anybody.</para>
///
/// <para><b>Two things it does, both server-only.</b> It says what maps it holds, so the character server knows
/// where to send people; and it listens for arrivals. Neither exists on a machine somebody plays on.</para>
/// </summary>
public class MapServerLink : MonoBehaviour
{
    /// <summary>Where the character server lives, from the environment like every other address here.</summary>
    public static string CharacterServerUrl =>
        Environment.GetEnvironmentVariable("CHAR_SERVER_URL") ?? "http://localhost:8081";

    /// <summary>What this server proves it is a server with. NOT a player's token and never interchangeable
    /// with one. The character server refuses everything on this road without it.</summary>
    public static string ServerSecret =>
        Environment.GetEnvironmentVariable("SERVER_SECRET") ?? "";

    /// <summary>The port this server listens on for the character server — NOT the port players connect to.
    /// Two different conversations, so two different doors.</summary>
    public static int ArrivalsPort
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("MAP_SERVER_ARRIVALS_PORT");
            return int.TryParse(raw, out var n) ? n : 7788;
        }
    }

    /// <summary>WHAT THIS SERVER UNDERSTANDS A CHARACTER TO BE. The character server refuses to hand players to
    /// a map server that does not say exactly this.
    ///
    /// <para>It is NOT a security measure — that road is between two machines you run, and the shared secret is
    /// what keeps strangers off it. It catches a HALF-FINISHED DEPLOY: a map server built against a different
    /// character would read the pushed fields into the wrong places and store somebody's life slightly wrong,
    /// silently, which is the most expensive kind of wrong. Refusing to start is the cheap failure.</para>
    ///
    /// <para>Change this whenever the save's shape changes, and change it on both sides in the same edit.</para></summary>
    public const string CharacterBuild = "unwritten-character-v9";

    /// <summary>Who this server is, to the character server. Stable for the life of the process.</summary>
    public static string ServerId { get; private set; } = "";

    static MapServerLink spawned;

    /// <summary>Someone the character server has told us to expect, with the character it sent. Held until they
    /// actually connect, or until they have taken too long about it.</summary>
    public sealed class Expected
    {
        public string ticket;
        public string accountId;
        public string characterId;
        public string characterName;
        public bool isGm;
        public SaveData progress;
        public float expiresAt;
    }

    static readonly Dictionary<string, Expected> expected = new();

    // Long enough for a client to be told an address and connect to it; short enough that a person who never
    // arrives is not remembered all day.
    const float ExpectedFor = 60f;

    /// <summary>Server-only: is this ticket somebody we were told to expect? Spends it — a ticket opens the
    /// door exactly once, which is what stops a captured one being used again.</summary>
    public static Expected TakeExpected(string ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return null;
        if (!expected.TryGetValue(ticket, out var who)) return null;
        expected.Remove(ticket);
        return Time.time <= who.expiresAt ? who : null;
    }

    /// <summary>Server-only: stand this link up. Does nothing anywhere else.</summary>
    public static void Begin()
    {
        if (spawned != null || !GameServer.IsServer) return;

        if (string.IsNullOrEmpty(ServerSecret))
        {
            GameLog.Error(LogCat.Map, "SERVER_SECRET is not set, so the character server will refuse this map server "
                           + "and nobody will be able to enter the world. Set the same value on both.");
            return;
        }

        ServerId = SystemInfo.deviceName + "-" + Guid.NewGuid().ToString("N")[..8];

        var go = new GameObject("MapServerLink") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);
        spawned = go.AddComponent<MapServerLink>();
        spawned.StartListening();
    }

    // ---- listening for arrivals -------------------------------------------------------------------------
    //
    // On its own thread, because the thing that answers a network request must not be the thing running the
    // world. What arrives is put on a queue and read on the ordinary frame, which is the only place Unity's
    // own objects may be touched at all.

    HttpListener listener;
    Thread listening;
    readonly Queue<Expected> arrived = new();
    volatile bool stopping;

    void StartListening()
    {
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://+:{ArrivalsPort}/");
            listener.Start();
        }
        catch (Exception e)
        {
            // A narrower address, for a machine that will not hand out the wildcard one (a Mac asks for
            // permission it cannot ask for without a window).
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{ArrivalsPort}/");
                listener.Start();
            }
            catch (Exception inner)
            {
                GameLog.Error(LogCat.Map, "could not listen for arrivals on " + ArrivalsPort + " — " + e.Message
                               + " / " + inner.Message + ". Nobody will be able to enter the world.");
                return;
            }
        }

        listening = new Thread(Listen) { IsBackground = true };
        listening.Start();

        GameLog.Info(LogCat.Map, "listening for arrivals on " + ArrivalsPort + " as '" + ServerId + "'");

        // SAID AGAIN EVERY TIME THE MAPS CHANGE, not once at the start. This stands up before the world does, so
        // the first thing it had to say was that it held NOTHING — and the character server believed it, because
        // that is what it was told. MEASURED on a real start-up: registered with 0 maps, then loaded two.
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => MapsChanged();
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded += _ => MapsChanged();
        RegisterWithCharacterServer();
    }

    void Listen()
    {
        while (!stopping)
        {
            HttpListenerContext ctx;
            try { ctx = listener.GetContext(); }
            catch { return; }   // stopped, or the listener has gone

            try { Answer(ctx); }
            catch (Exception e) { GameLog.Warn(LogCat.Map, "an arrival went wrong — " + e.Message); }
        }
    }

    void Answer(HttpListenerContext ctx)
    {
        // NOT A DOOR ANY PLAYER MAY WALK THROUGH, and asked BEFORE a stranger's body is read — reading first
        // meant anybody who could reach this port could make this server swallow as much as they cared to send.
        // Compared in fixed time so the comparison itself cannot be used to learn the secret one letter at a time.
        if (!SameSecret(ctx.Request.Headers["x-server-secret"])) { Reply(ctx, 401, "{\"error\":\"no\"}"); return; }

        string body;
        using (var reader = new System.IO.StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = reader.ReadToEnd();
        if (ctx.Request.Url == null || !ctx.Request.Url.AbsolutePath.EndsWith("/arriving")) { Reply(ctx, 404, "{}"); return; }

        var msg = JsonUtility.FromJson<Arrival>(body);
        if (msg == null || string.IsNullOrEmpty(msg.ticket)) { Reply(ctx, 400, "{\"error\":\"no ticket\"}"); return; }
        if (msg.build != CharacterBuild)
        {
            Reply(ctx, 409, "{\"error\":\"built against a different character\"}");
            return;
        }

        lock (arrived)
        {
            arrived.Enqueue(new Expected
            {
                ticket = msg.ticket,
                accountId = msg.accountId,
                characterId = msg.characterId,
                characterName = msg.characterName,
                isGm = msg.isGm,
                progress = msg.progress,
            });
        }
        Reply(ctx, 200, "{\"ok\":true}");
    }

    static bool SameSecret(string given)
    {
        if (string.IsNullOrEmpty(given) || string.IsNullOrEmpty(ServerSecret)) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(ServerSecret));
    }

    static void Reply(HttpListenerContext ctx, int code, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    void Update()
    {
        Heartbeat();

        // Taken off the queue on the ordinary frame — the clock below is Unity's, and it may only be read here.
        lock (arrived)
        {
            while (arrived.Count > 0)
            {
                var who = arrived.Dequeue();
                who.expiresAt = Time.time + ExpectedFor;
                expected[who.ticket] = who;
                GameLog.Info(LogCat.Map, "expecting " + who.characterName + " — their character arrived ahead of them.");
            }
        }

        // Anyone who was announced and never turned up. Left alone this grows for the life of the server.
        if (expected.Count == 0) return;
        List<string> stale = null;
        foreach (var kv in expected)
            if (Time.time > kv.Value.expiresAt) (stale ??= new List<string>()).Add(kv.Key);
        if (stale != null) foreach (var t in stale) expected.Remove(t);
    }

    // ---- saying what we hold ----------------------------------------------------------------------------

    // Maps arrive one after another as the world comes up, and each would otherwise be its own trip. Told once,
    // a moment after the last of them settles.
    bool announcementPending;

    void MapsChanged()
    {
        if (announcementPending) return;
        announcementPending = true;
        Invoke(nameof(AnnounceWhatWeHold), 0.5f);
    }

    void AnnounceWhatWeHold()
    {
        announcementPending = false;
        RegisterWithCharacterServer();
    }

    // SAID AGAIN, ON A BEAT, FOR AS LONG AS THIS SERVER IS ALIVE. The character server stops believing a map
    // server that has gone quiet, because one that crashed cannot tell anybody — and a registration nobody
    // refreshes would be exactly as convincing as a live one right up until players were sent to a machine that
    // is not there. Comfortably more often than it stops being believed.
    const float SecondsBetweenHeartbeats = 30f;

    float nextHeartbeat;

    void Heartbeat()
    {
        if (Time.time < nextHeartbeat) return;
        nextHeartbeat = Time.time + SecondsBetweenHeartbeats;
        RegisterWithCharacterServer();
    }

    void RegisterWithCharacterServer()
    {
        var maps = new List<string>();
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (s.isLoaded && s.name != MapManager.SystemsScene) maps.Add(s.name);
        }

        var body = JsonUtility.ToJson(new Registration
        {
            id = ServerId,
            address = Environment.GetEnvironmentVariable("MAP_SERVER_ADDRESS") ?? "127.0.0.1",
            port = ArrivalsPort,
            // AND THE DOOR PLAYERS USE, which is not this one. The character server hands this second number to
            // a player and the first to itself; one number for both would send somebody to knock at the door
            // meant for the character server, where nothing is listening for them.
            playerPort = GameNetwork.PlayerPort,
            maps = maps.ToArray(),
            build = CharacterBuild,
        });

        ServerHttp.Send("POST", CharacterServerUrl + "/map-servers", body, ServerSecret, (ok, reply, error) =>
        {
            if (ok) GameLog.Info(LogCat.Map, "the character server knows this map server holds " + maps.Count + " map(s).");
            else GameLog.Error(LogCat.Map, "the character server would not take this map server — " + error
                                + " " + reply + ". Nobody will be able to enter the world.");
        });
    }

    bool saidGoodbye;

    void OnDestroy()
    {
        stopping = true;
        try { listener?.Stop(); } catch { }

        // ONCE. Quitting calls this itself AND Unity calls it again on teardown, so without the latch the
        // character server was told twice that this map server had gone.
        if (saidGoodbye || string.IsNullOrEmpty(ServerId)) return;
        saidGoodbye = true;
        ServerHttp.Send("DELETE", CharacterServerUrl + "/map-servers/" + ServerId, null, ServerSecret, null);
    }

    void OnApplicationQuit() => OnDestroy();

    // ---- what travels on the wire, as the two sides both spell it ---------------------------------------

    [Serializable]
    class Registration
    {
        public string id;
        public string address;
        public int port;
        public int playerPort;
        public string[] maps;
        public string build;
    }

    [Serializable]
    class Arrival
    {
        public string build;
        public string ticket;
        public string accountId;
        public string characterId;
        public string characterName;
        public bool isGm;
        public SaveData progress;
    }
}
