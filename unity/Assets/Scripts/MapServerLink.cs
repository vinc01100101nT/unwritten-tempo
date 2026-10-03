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

    /// <summary>WHERE THE CHARACTER SERVER DELIVERS CHARACTERS TO THIS SERVER — a different address from the one
    /// players are given (<c>MAP_SERVER_ADDRESS</c>). A delivery carries a whole character, an entry ticket and the
    /// shared server secret as plain text, so it must never cross the open internet: on a rented machine this is a
    /// private network address, or 127.0.0.1 when the character server runs on the same machine. Empty means the
    /// player address is used for both, which is right only on a desk or a home network.</summary>
    public static string ArrivalsAddress =>
        Environment.GetEnvironmentVariable("MAP_SERVER_ARRIVALS_ADDRESS") ?? "";

    /// <summary>WHAT THIS SERVER UNDERSTANDS A CHARACTER TO BE. The character server refuses to hand players to
    /// a map server that does not say exactly this.
    ///
    /// <para>It is NOT a security measure — that road is between two machines you run, and the shared secret is
    /// what keeps strangers off it. It catches a HALF-FINISHED DEPLOY: a map server built against a different
    /// character would read the pushed fields into the wrong places and store somebody's life slightly wrong,
    /// silently, which is the most expensive kind of wrong. Refusing to start is the cheap failure.</para>
    ///
    /// <para>Change this whenever the save's shape changes, and change it on both sides in the same edit.</para></summary>
    public const string CharacterBuild = "unwritten-character-v17";

    /// <summary>Who this server is, to the character server. Stable for the life of the process.</summary>
    public static string ServerId { get; private set; } = "";

    static MapServerLink spawned;

    // EVERY MAP THIS BUILD CAN LOAD, and where somebody goes when they ask for one it cannot. Filled once, on the
    // frame this link starts, because both are read on the listening thread, where Unity may not be asked anything.
    static HashSet<string> mapsInBuild = new HashSet<string>(StringComparer.Ordinal);
    static string startingMap = "";

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
        public int handover;
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

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings; i++)
            names.Add(System.IO.Path.GetFileNameWithoutExtension(
                UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i)));
        mapsInBuild = names;
        // SAVES A PREVIOUS RUN COULD NOT SEND go back in the queue before anything else, so they are sent before
        // this server tells the character server it exists.
        SaveOutbox.LoadFromDisk();
        var root = FindFirstObjectByType<GameRoot>();   // perf-ok: once, as the server starts
        startingMap = root != null ? root.firstMap : "";

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
    // WHAT ARRIVED, AND WHO IS STILL WAITING TO BE TOLD IT WAS TAKEN. Both travel together because the answer
    // is not sent from the listening thread — see the end of Answer below.
    readonly Queue<(Expected who, HttpListenerContext asker)> arrived = new();
    volatile bool stopping;

    void StartListening()
    {
        // ONLY THE DELIVERY ADDRESS, when one is set, so nothing outside that network can even reach this door.
        if (!string.IsNullOrEmpty(ArrivalsAddress))
        {
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://{ArrivalsAddress}:{ArrivalsPort}/");
                listener.Start();
            }
            catch (Exception e)
            {
                listener = null;
                GameLog.Warn(LogCat.Map, "could not listen for arrivals only on " + ArrivalsAddress + " — " + e.Message
                                 + ". Listening on every address instead; block port " + ArrivalsPort
                                 + " from outside with the firewall.");
            }
        }

        if (listener == null && !ListenOnEveryAddress()) return;

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

    /// <summary>The arrival door on every address — or, on a machine that will not hand out the wildcard one, on
    /// 127.0.0.1. False (and said) when neither could be opened.</summary>
    bool ListenOnEveryAddress()
    {
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://+:{ArrivalsPort}/");
            listener.Start();
            return true;
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
                return true;
            }
            catch (Exception inner)
            {
                GameLog.Error(LogCat.Map, "could not listen for arrivals on " + ArrivalsPort + " — " + e.Message
                               + " / " + inner.Message + ". Nobody will be able to enter the world.");
                return false;
            }
        }
    }

    void Listen()
    {
        while (!stopping)
        {
            HttpListenerContext ctx;
            try { ctx = listener.GetContext(); }
            catch (Exception e)
            {
                // Only a stop ends this loop. Anything else used to end it too, silently — and from then on
                // nobody could arrive at this map server until it was restarted, with no line saying why.
                if (stopping || listener == null || !listener.IsListening) return;
                GameLog.Warn(LogCat.Map, "the arrival door hiccupped and stays open — " + e.Message);
                // A door that throws on every call would otherwise spin flat out and flood the log.
                System.Threading.Thread.Sleep(1000);
                continue;
            }

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
        if (ctx.Request.Url == null) { Reply(ctx, 404, "{}"); return; }

        // BRING A MAP UP. The character server decides which process hosts a zone and says so here; the process
        // itself never chooses, because only the character server can see how loaded the others are. Answered at
        // once — the caller waits for the map to be STANDING through the arrival below, not through this.
        if (ctx.Request.Url.AbsolutePath.EndsWith("/host"))
        {
            var ask = JsonUtility.FromJson<HostRequest>(body);
            if (ask == null || string.IsNullOrEmpty(ask.map)) { Reply(ctx, 400, "{\"error\":\"no map\"}"); return; }
            // A MAP THIS BUILD DOES NOT HAVE is said at once, with where to send them instead. Taking it would
            // start a load that can never finish, and the character server would wait out its whole patience for
            // a map that is never coming — which is what a character saved in a deleted map met on every sign-in.
            if (!mapsInBuild.Contains(ask.map))
            {
                GameLog.Warn(LogCat.Map, "asked to bring up '" + ask.map + "', which is not in this build — "
                                 + "answered with '" + startingMap + "' instead.");
                Reply(ctx, 404, JsonUtility.ToJson(new NoSuchMap { error = "no such map", instead = startingMap }));
                return;
            }
            lock (toHost) toHost.Enqueue(ask.map);
            Reply(ctx, 200, "{\"ok\":true}");
            return;
        }

        // SOMEBODY IS BEING TAKEN OVER. Written down FIRST, and this is not answered until that write has
        // landed — the character server reads the row the moment it hears back, and answering early would hand
        // the new machine whoever this person was up to fifteen seconds ago, losing everything since.
        if (ctx.Request.Url.AbsolutePath.EndsWith("/evict"))
        {
            var who = JsonUtility.FromJson<EvictRequest>(body);
            if (who == null || string.IsNullOrEmpty(who.characterId)) { Reply(ctx, 400, "{\"error\":\"no character\"}"); return; }
            lock (toEvict) toEvict.Enqueue((who.characterId, ctx));
            return;
        }

        if (!ctx.Request.Url.AbsolutePath.EndsWith("/arriving")) { Reply(ctx, 404, "{}"); return; }

        var msg = JsonUtility.FromJson<Arrival>(body);
        if (msg == null || string.IsNullOrEmpty(msg.ticket)) { Reply(ctx, 400, "{\"error\":\"no ticket\"}"); return; }
        if (msg.build != CharacterBuild)
        {
            Reply(ctx, 409, "{\"error\":\"built against a different character\"}");
            return;
        }

        lock (arrived)
        {
            arrived.Enqueue((new Expected
            {
                ticket = msg.ticket,
                accountId = msg.accountId,
                characterId = msg.characterId,
                characterName = msg.characterName,
                isGm = msg.isGm,
                progress = msg.progress,
                handover = msg.handover,
            }, ctx));
        }

        // AND NOT ANSWERED HERE. The character server reads a yes as "they may knock now" and hands the player an
        // address in the same breath — but nobody can be let in until the frame below has filed this person, and a
        // player who beat that frame was refused at the door as somebody nobody had announced, and had to dial
        // again. Answered from Update instead, once the ticket genuinely opens the door. This thread does not wait
        // for that: it goes straight back to listening, so a second arrival is never stuck behind the first.
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
        SaveOutbox.Tick();
        if (registrationOwed && !SaveOutbox.HasFailed) RegisterWithCharacterServer();   // perf-ok: once, when the last waiting save lands
        Heartbeat();
        BringUpWhatWasAskedFor();
        LetGoOfWhoWasAskedFor();

        // Taken off the queue on the ordinary frame — the clock below is Unity's, and it may only be read here.
        lock (arrived)
        {
            while (arrived.Count > 0)
            {
                var (who, asker) = arrived.Dequeue();
                who.expiresAt = Time.time + ExpectedFor;
                expected[who.ticket] = who;
                GameLog.Info(LogCat.Map, "expecting " + who.characterName + " — their character arrived ahead of them.");

                // AND ANYBODY STILL PLAYING THAT CHARACTER HERE IS FINISHED. By now they have almost always been
                // written down and let go already, because the character server asks every server to do that
                // before it reads the row — this is the backstop for the one case that misses: somebody walking
                // through a door back into a zone THIS server holds, where nothing was asked of anybody. Their
                // write has already happened, before the door was answered, so nothing is lost by dropping them.
                // THE ONE EXCEPTION IS THE PERSON DOING THE MOVING. When the map they walked into is one THIS
                // server already holds, the announcement is about them — and cutting them here would cut the
                // connection their new address and ticket still have to be sent down. They keep their body and
                // their registration until they turn up at the door with the ticket, which drops them there.
                if (GameNetwork.WhoIsPlayingIsAlreadyMoving(who.characterId)) { /* left alone on purpose */ }
                else
                {
                    GameNetwork.DropWhoeverIsPlaying(who.characterId);
                    PlayerFighter.ReleaseCharacter(who.characterId);
                }

                // NOW they may be told, and not one line sooner: the ticket above is what the door checks, so the
                // yes going out here is true the instant it is read. If the character server has already given up
                // waiting (it allows five seconds) this throws, and this person is simply never sent — the entry
                // above is swept a minute later like anybody else who was announced and never came.
                try { Reply(asker, 200, "{\"ok\":true}"); }
                catch (Exception e)
                {
                    GameLog.Warn(LogCat.Map, "could not tell the character server that " + who.characterName
                                   + " was taken — " + e.Message + ". They will not be sent here.");
                }
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

    // Maps the character server has asked for, taken off the listening thread and acted on in the game loop —
    // nothing about a scene may be touched from a thread that is not the one running the game.
    readonly Queue<string> toHost = new Queue<string>();

    void BringUpWhatWasAskedFor()
    {
        while (true)
        {
            string map;
            lock (toHost)
            {
                if (toHost.Count == 0) return;
                map = toHost.Dequeue();
            }
            MapHosting.Want(map);
        }
    }

    [Serializable]
    class HostRequest
    {
        public string map;
    }

    [Serializable]
    class NoSuchMap
    {
        public string error;
        public string instead;
    }

    [Serializable]
    class EvictRequest
    {
        public string characterId;
    }

    // People the character server has asked us to let go of, taken off the listening thread and dealt with in the
    // game loop — a body and a save may only be touched from the thread running the game.
    readonly Queue<(string characterId, HttpListenerContext asker)> toEvict = new Queue<(string, HttpListenerContext)>();

    void LetGoOfWhoWasAskedFor()
    {
        while (true)
        {
            string characterId; HttpListenerContext asker;
            lock (toEvict)
            {
                if (toEvict.Count == 0) return;
                (characterId, asker) = toEvict.Dequeue();
            }
            GameNetwork.SaveAndDrop(characterId, () =>
            {
                try { Reply(asker, 200, "{\"ok\":true}"); }
                catch (Exception e)
                {
                    GameLog.Warn(LogCat.Map, "could not confirm letting go of a character — " + e.Message);
                }
            });
        }
    }

    void Heartbeat()
    {
        if (Time.time < nextHeartbeat) return;
        nextHeartbeat = Time.time + SecondsBetweenHeartbeats;
        RegisterWithCharacterServer();
    }

    // Set when a registration was held back because a save was still waiting; sent the moment none is.
    bool registrationOwed;

    void RegisterWithCharacterServer()
    {
        // NOT WHILE A SAVE IS STILL WAITING. The character server places people only on servers it knows, so a
        // server that has not registered yet cannot be handed anybody — and nobody can be read from an older copy
        // than the one this server is still trying to send. Said once, then sent as soon as the saves land.
        if (SaveOutbox.HasFailed)
        {
            if (!registrationOwed)
                GameLog.Warn(LogCat.Map, "not telling the character server about this map server until "
                                 + SaveOutbox.FailedCount + " waiting save(s) have landed.");
            registrationOwed = true;
            return;
        }
        registrationOwed = false;

        var maps = new List<string>();   // perf-ok: a 30-second beat or a map change, never every frame
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (s.isLoaded && s.name != MapManager.SystemsScene) maps.Add(s.name);
        }

        var body = JsonUtility.ToJson(new Registration
        {
            id = ServerId,
            address = Environment.GetEnvironmentVariable("MAP_SERVER_ADDRESS") ?? "127.0.0.1",
            arrivalsAddress = ArrivalsAddress,
            port = ArrivalsPort,
            // AND THE DOOR PLAYERS USE, which is not this one. The character server hands this second number to
            // a player and the first to itself; one number for both would send somebody to knock at the door
            // meant for the character server, where nothing is listening for them.
            playerPort = GameNetwork.PlayerPort,
            maps = maps.ToArray(),   // perf-ok: same beat as above
            build = CharacterBuild,
            startingMap = startingMap,
            // HOW BUSY THIS ONE IS, so the character server can put a zone on the quietest process instead of
            // guessing. People first, because a person costs far more than a creature and is the thing a zone
            // placed here would have to share a frame with.
            players = Mirror.NetworkServer.connections != null ? Mirror.NetworkServer.connections.Count : 0,
            creatures = MonsterAI3D.Active != null ? MonsterAI3D.Active.Count : 0,
        });

        ServerHttp.Send("POST", CharacterServerUrl + "/map-servers", body, ServerSecret, (ok, reply, error, _) =>
        {
            // SAID WHEN IT CHANGES, NOT ON EVERY BEAT. This registration repeats on a timer, and writing the same
            // "holds 1 map(s)" each time filled an idle server's log with a line every half minute — which made a quiet
            // log mean nothing. It is written when the count differs from the last one said, and again after a failure.
            if (ok)
            {
                if (maps.Count == lastSaidMapCount) return;
                lastSaidMapCount = maps.Count;
                GameLog.Info(LogCat.Map, "the character server knows this map server holds " + maps.Count + " map(s).");
            }
            else
            {
                lastSaidMapCount = -1;
                GameLog.Error(LogCat.Map, "the character server would not take this map server — " + error
                                  + " " + reply + ". Nobody will be able to enter the world.");
            }
        });
    }

    // The map count last written by the registration above; -1 until the first success and after any failure.
    int lastSaidMapCount = -1;

    bool saidGoodbye;

    void OnDestroy()
    {
        stopping = true;

        // ANYBODY STILL WAITING TO BE TOLD IS TOLD NO. Their arrival was never filed and this server is going
        // away, so leaving the request hanging would only make the character server sit out its own five seconds
        // before deciding the same thing, with a player watching a loading screen for all of it.
        lock (arrived)
        {
            while (arrived.Count > 0)
            {
                var (_, asker) = arrived.Dequeue();
                try { Reply(asker, 503, "{\"error\":\"this map server is going away\"}"); } catch { }
            }
        }

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
        // Where the character server delivers characters to; empty means the address above.
        public string arrivalsAddress;
        public int port;
        public int playerPort;
        public string[] maps;
        public string build;

        // Where a character nobody has played yet begins — the character server has no map list of its own, so
        // this is how it learns which map to bring up for them.
        public string startingMap;

        // How busy this process is right now — the two numbers the character server weighs when it decides
        // which process a zone nobody is holding should be brought up on.
        public int players;
        public int creatures;
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
        // Which hand-over this is — every save of this character from here carries it back.
        public int handover;
    }
}
