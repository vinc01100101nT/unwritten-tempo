using System.Collections.Concurrent;
using System.Text.Json;

namespace CharServer;

/// <summary>
/// Which map server holds which maps, and the handing of a player from here to there.
///
/// <para><b>Why this exists at all.</b> Until now a map server FETCHED a character over the network using the
/// player's own sign-in proof — so the thing running the world talked to the character store, and a client's
/// token was the key that opened it. The order is now the other way round, which is the arrangement Ragnarok
/// has used since 2002: the character server PUSHES the character to the map server, having first told it that
/// this person is arriving. A map server therefore never asks the store for anything, and a client that turns
/// up unannounced is refused before it is anybody.</para>
///
/// <para><b>Held in memory on purpose.</b> A registration is true only while both processes are alive; writing
/// it down would mean remembering map servers that stopped existing, and a stale row here sends players at a
/// machine that is not there.</para>
/// </summary>
public sealed class MapServers(Config config)
{
    /// <summary><paramref name="Port"/> is the map server's door for THIS server — where a character is pushed.
    /// <paramref name="PlayerPort"/> is where PLAYERS connect. Two different conversations on two different
    /// doors, and telling a player the wrong one sends them to knock where nobody is listening for them.</summary>
    public sealed record Registration(
        string Id,
        string Address,
        string ArrivalsAddress,
        int Port,
        int PlayerPort,
        string[] Maps,
        string Build,
        DateTimeOffset At,
        int Players,
        int Creatures,
        string StartingMap)
    {
        /// <summary>How busy this process is, as one number to compare processes by. A person weighs far more
        /// than a creature because a person is played at: they move every frame, they fight, they are written
        /// down, and everything they do is answered. The exact weight is a judgement, not a measurement, and it
        /// only has to rank processes correctly — it is never shown to anybody or used as a budget.</summary>
        public int Load => Players * 50 + Creatures;

        /// <summary>Where THIS server delivers characters, asks a map up, or asks for one to be let go — the map
        /// server's private address when it gave one, never sent to a player.</summary>
        public string ArrivalsUrl => $"http://{ArrivalsAddress}:{Port}";
    }

    readonly ConcurrentDictionary<string, Registration> byId = new();

    /// <summary>Which map server each character was LAST handed to, by character id. Written when an
    /// announcement is accepted, so letting a character go asks that one server instead of every server there
    /// is. An entry is dropped the moment its server says it no longer holds them, or when that server is
    /// forgotten, so it never names a machine that has gone.</summary>
    readonly ConcurrentDictionary<string, string> heldBy = new();

    // THE SAME PEOPLE, BY NAME — how a private message finds who it is for without asking the database. Lower-cased
    // name → the character last handed over under it, written and dropped beside heldBy (Hold / Unhold) and never
    // anywhere else, so the two cannot disagree about who is out there. idToNameKey is the way back, so dropping a
    // character finds its name without a search. A name reused by a newer character simply points at the newer one,
    // and the older character letting go can no longer take it with it (Unhold removes only its own pairing).
    readonly ConcurrentDictionary<string, (string id, string name)> heldNames = new();
    readonly ConcurrentDictionary<string, string> idToNameKey = new();

    void Hold(string characterId, string characterName, string serverId)
    {
        heldBy[characterId] = serverId;
        if (string.IsNullOrEmpty(characterName)) return;
        var key = characterName.ToLowerInvariant();
        heldNames[key] = (characterId, characterName);
        idToNameKey[characterId] = key;
    }

    void Unhold(string characterId)
    {
        heldBy.TryRemove(characterId, out _);
        if (idToNameKey.TryRemove(characterId, out var key)
            && heldNames.TryGetValue(key, out var held) && held.id == characterId)
            heldNames.TryRemove(new KeyValuePair<string, (string id, string name)>(key, held));
    }

    // Which map lives where. Rebuilt whenever a registration changes rather than searched each time — this is
    // asked on every entry into the world, and the answer only moves when a map server comes or goes.
    volatile Dictionary<string, Registration> byMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What this pair of servers must agree on before either trusts the other with a character.
    ///
    /// <para>It is NOT a security measure — that link is between two machines you run, and the shared secret
    /// below is what keeps strangers off it. It catches a HALF-FINISHED DEPLOY: a map server built against a
    /// different character than this one understands would read the pushed bytes into the wrong fields and
    /// store somebody's life slightly wrong, silently. Refusing to start is the cheap failure.</para></summary>
    public const string CharacterBuild = "unwritten-character-v21";

    public IReadOnlyCollection<Registration> All => byId.Values.ToArray();

    /// <summary>Does this caller hold the shared server secret. Nothing here is reachable with a PLAYER's
    /// token: a person signing in must never be able to register a map server or ask for somebody's character.</summary>
    public bool IsAServer(HttpRequest request)
    {
        var given = request.Headers["x-server-secret"].ToString();
        return !string.IsNullOrEmpty(given)
               && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                      System.Text.Encoding.UTF8.GetBytes(given),
                      System.Text.Encoding.UTF8.GetBytes(config.ServerSecret));
    }

    public Registration Register(string id, string address, string? arrivalsAddress, int port, int playerPort,
                                 string[] maps, string build, int players, int creatures, string? startingMap)
    {
        // A map server that names no private delivery address is delivered to at the address players are given.
        string arrivals = string.IsNullOrWhiteSpace(arrivalsAddress) ? address : arrivalsAddress.Trim();
        var reg = new Registration(id, address, arrivals, port, playerPort, maps ?? [], build, DateTimeOffset.UtcNow,
                                   players, creatures, startingMap?.Trim() ?? "");
        byId[id] = reg;
        Rebuild();
        return reg;
    }

    public bool Forget(string id)
    {
        bool gone = byId.TryRemove(id, out _);
        lastSaid.TryRemove(id, out _);
        if (gone) Rebuild();
        return gone;
    }

    // WHAT WAS LAST SAID ABOUT EACH MAP SERVER'S CHECK-IN. A map server checks in every 30 seconds; writing the same
    // line each time filled an idle log with nothing new. Said again only when it differs. Removed with the
    // registration, on both of the paths that forget one.
    readonly ConcurrentDictionary<string, string> lastSaid = new();

    /// <summary>Say a check-in — only after the address characters are delivered to has been tried, because that
    /// address is where this server sends every player's character, and printing it untried made a dead address
    /// look like a working one. Said only when what is true about it changed.</summary>
    public async Task SayCheckIn(Registration reg)
    {
        var holds = $"holds {reg.Maps.Length} map(s): {string.Join(", ", reg.Maps)} "
                    + $"[{reg.Players} player(s), {reg.Creatures} creature(s)]";
        var answers = await Addresses.Answers(reg.ArrivalsAddress, reg.Port);
        var line = answers
            ? $"[char] map server '{reg.Id}' answers at {reg.ArrivalsAddress}:{reg.Port} and {holds}"
            : $"[char] map server '{reg.Id}' does not answer at {reg.ArrivalsAddress}:{reg.Port} — "
              + $"nobody can be handed to it. It {holds}";

        // Forgotten while it was being tried: saying it now would bring back a line for a server that is gone.
        if (!byId.ContainsKey(reg.Id)) return;
        if (lastSaid.TryGetValue(reg.Id, out var last) && last == line) return;
        lastSaid[reg.Id] = line;
        if (answers) Console.WriteLine(line);
        else Addresses.WriteRed(line);
    }

    void Rebuild()
    {
        var next = new Dictionary<string, Registration>(StringComparer.OrdinalIgnoreCase);
        // Newest registration wins a contested map, so a map server restarting under a new id takes its maps
        // back rather than leaving them pointed at the copy that has gone.
        foreach (var reg in byId.Values.OrderBy(r => r.At))
            foreach (var map in reg.Maps)
                next[map] = reg;
        byMap = next;
    }

    /// <summary>How long a registration is believed without being said again. A map server re-announces itself
    /// whenever its maps change and on a beat; one that has said nothing for this long has gone without being
    /// able to say so — a crash, a pulled cable — and must stop being handed players, or everybody entering is
    /// sent to knock at a machine that is not there.</summary>
    static readonly TimeSpan TrustedFor = TimeSpan.FromMinutes(2);

    /// <summary>Which map server holds this map, or null when nobody has claimed it or the one that did has
    /// gone quiet.</summary>
    public Registration? Holding(string map)
    {
        ForgetTheSilent();
        return map is not null && byMap.TryGetValue(map, out var reg) ? reg : null;
    }

    /// <summary>Every map server still believed to be there.</summary>
    public IReadOnlyCollection<Registration> Alive
    {
        get { ForgetTheSilent(); return byId.Values.ToArray(); }
    }

    void ForgetTheSilent()
    {
        var cutoff = DateTimeOffset.UtcNow - TrustedFor;
        List<string>? gone = null;
        foreach (var kv in byId)
            if (kv.Value.At < cutoff) (gone ??= new List<string>()).Add(kv.Key);
        if (gone is null) return;
        foreach (var id in gone)
        {
            byId.TryRemove(id, out _);
            lastSaid.TryRemove(id, out _);
            foreach (var held in heldBy)
                if (held.Value == id) Unhold(held.Key);
            Console.WriteLine($"[char] map server '{id}' has said nothing for {TrustedFor.TotalMinutes:0} minutes — forgotten.");
        }
        Rebuild();
    }

    /// <summary>Ask the map server holding this character to let go of them, writing them down first, and wait
    /// for the answer. TRUE when it actually had them.
    ///
    /// <para>The server that took them last is written down (<see cref="heldBy"/>), so ordinarily ONE server is
    /// asked. Every server is asked only when none is written down, or the one written down has since been
    /// forgotten — the cost of that fan-out is players × map servers, so it is the exception and not the rule.
    /// The one that has them writes them down and waits for that write to land before answering, which is the
    /// entire reason this exists: reading their row before that hands whoever is taking over a copy up to fifteen
    /// seconds old, and everything since is never recorded anywhere.</para></summary>
    public async Task<bool> LetGoOfEverywhereAsync(string characterId, CancellationToken ct = default)
    {
        if (heldBy.TryGetValue(characterId, out var holderId))
        {
            ForgetTheSilent();
            if (byId.TryGetValue(holderId, out var holder))
            {
                bool had = await AskToLetGoAsync(holder, characterId, ct);
                if (!had) Unhold(characterId);
                return had;
            }
            Unhold(characterId);
        }

        var servers = Alive;
        if (servers.Count == 0) return false;

        var asks = new List<Task<bool>>(servers.Count);
        foreach (var reg in servers) asks.Add(AskToLetGoAsync(reg, characterId, ct));
        var answers = await Task.WhenAll(asks);

        foreach (var had in answers) if (had) return true;
        return false;
    }

    async Task<bool> AskToLetGoAsync(Registration to, string characterId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{to.ArrivalsUrl}/evict")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { characterId }),
                                        System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-server-secret", config.ServerSecret);

        try
        {
            var reply = await wire.SendAsync(request, ct);
            return reply.IsSuccessStatusCode;
        }
        catch (Exception e)
        {
            // A server that cannot answer is one this person is not going to keep playing on either way, so this
            // is said and stepped past rather than stopping somebody from signing in.
            Console.Error.WriteLine($"[char] map server {to.Id} did not answer about letting go of a character — {e.Message}");
            return false;
        }
    }

    /// <summary>Which process a map nobody is holding should be brought up on: the quietest one that is still
    /// answering. Every process can host any zone — none is given a list when it starts — so this is the only
    /// thing that decides, and it is the only thing that can, because it is the only one that sees them all.</summary>
    public Registration? QuietestAlive()
    {
        Registration? best = null;
        foreach (var reg in Alive)
            if (best is null || reg.Load < best.Load) best = reg;
        return best;
    }

    /// <summary>What a map server said to "bring this map up". <c>Taken</c> means it has STARTED loading, not that
    /// the map is standing — what proves that is the map appearing in its next registration. <c>NoSuchMap</c> means
    /// its build has no map by that name, and <c>Instead</c> names its starting map.</summary>
    public enum HostAnswer { Taken, Failed, NoSuchMap }

    /// <summary>Who is holding a map, or — when no build has it — the map to send somebody to instead.</summary>
    public sealed record Held(Registration? Server, string? NoSuchMapInstead);

    sealed record NoSuchMapReply(string? error, string? instead);

    /// <summary>Tell a map server to bring a map up.</summary>
    public async Task<(HostAnswer Answer, string? Instead)> AskToHostAsync(Registration to, string map,
                                                                          CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{to.ArrivalsUrl}/host")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { map }),
                                        System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-server-secret", config.ServerSecret);

        try
        {
            var reply = await wire.SendAsync(request, ct);
            if (reply.IsSuccessStatusCode) return (HostAnswer.Taken, null);
            if (reply.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var said = await reply.Content.ReadFromJsonAsync<NoSuchMapReply>(ct);
                if (!string.IsNullOrWhiteSpace(said?.instead)) return (HostAnswer.NoSuchMap, said.instead);
            }
            return (HostAnswer.Failed, null);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[char] could not ask map server {to.Id} to host '{map}' — {e.Message}");
            return (HostAnswer.Failed, null);
        }
    }

    /// <summary>Make sure SOMETHING is holding this map, bringing it up on the quietest process if nobody is.
    /// Returns the server holding it; no server when there is nobody to ask or it never came up; and, when the
    /// build has no map by that name, the starting map the map server named instead.
    ///
    /// <para>It waits for the map to actually appear in a registration rather than trusting the request, because
    /// a map that has been asked for is not a map a player can be pushed into yet.</para></summary>
    public async Task<Held> EnsureHeldAsync(string map, CancellationToken ct = default)
    {
        var already = Holding(map);
        if (already is not null) return new Held(already, null);

        var host = QuietestAlive();
        if (host is null) return new Held(null, null);

        Console.WriteLine($"[char] nobody holds '{map}' — asking '{host.Id}' to bring it up (load {host.Load}).");
        var (answer, instead) = await AskToHostAsync(host, map, ct);
        if (answer == HostAnswer.NoSuchMap) return new Held(null, instead);
        if (answer != HostAnswer.Taken) return new Held(null, null);

        // A map has to load its ground and spawn its creatures before anybody can be put into it, and how long
        // that takes depends on what is in it. Asked for repeatedly rather than waited out in one go, so a map
        // that comes up quickly costs a player almost nothing.
        for (int i = 0; i < 40 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(250, ct);
            var now = Holding(map);
            if (now is not null) return new Held(now, null);
        }

        Console.Error.WriteLine($"[char] '{map}' was asked for but never came up on '{host.Id}'.");
        return new Held(null, null);
    }

    // ---- the ticket ------------------------------------------------------------------------------------
    //
    // A one-time, short-lived, account-and-character-bound secret. It is what the map server matches an
    // arriving connection against, and it is the reason an unannounced connection can be refused: the map
    // server was told about this person before they knocked.

    public sealed record Ticket(string Value, string AccountId, string CharacterId, DateTimeOffset Expires);

    readonly ConcurrentDictionary<string, Ticket> tickets = new();

    // Long enough to reconnect on a slow link, short enough that a stolen one is worthless by the time it is
    // stolen. A player uses it within a second of being handed it.
    static readonly TimeSpan TicketLife = TimeSpan.FromSeconds(30);

    public Ticket MintTicket(string accountId, string characterId)
    {
        SweepExpired();
        var value = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                           .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var t = new Ticket(value, accountId, characterId, DateTimeOffset.UtcNow + TicketLife);
        tickets[value] = t;
        return t;
    }

    void SweepExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in tickets)
            if (kv.Value.Expires <= now) tickets.TryRemove(kv.Key, out _);
    }

    // ---- pushing the character across ---------------------------------------------------------------------

    static readonly HttpClient wire = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>Tell a map server that this person is arriving, and hand over their whole character with the
    /// announcement. TRUE when the map server took it.
    ///
    /// <para>The character travels as the document it is stored as — the same shape the game reads and writes,
    /// so there is no second description of a character to keep in step with the first.</para>
    ///
    /// <para>The hotbar travels beside it, not inside it: it has its own row, written only when a slot changes.
    /// <paramref name="hotbar"/> is that stored array, or null when the character has never had one written.</para></summary>
    public async Task<bool> AnnounceAsync(Registration to, Ticket ticket, string characterName,
                                          JsonElement progress, JsonElement? hotbar, JsonElement quests, bool isGm,
                                          int handover, bool signingIn,
                                          CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            build = CharacterBuild,
            ticket = ticket.Value,
            accountId = ticket.AccountId,
            characterId = ticket.CharacterId,
            characterName,
            isGm,
            progress,
            hotbar,
            // Every quest row the character has, open and finished (see QuestRows) — a finished quest is never given again.
            quests,
            // Every save of this character from that server carries this back — see Handover.
            handover,
            // ENTERING THE WORLD rather than walking from one map server to another — the moment the map server tells
            // them which world events are running. Told on every map change, it would repeat at every door.
            signingIn,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{to.ArrivalsUrl}/arriving")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-server-secret", config.ServerSecret);

        try
        {
            var reply = await wire.SendAsync(request, ct);
            if (reply.IsSuccessStatusCode) Hold(ticket.CharacterId, characterName, to.Id);
            return reply.IsSuccessStatusCode;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[char] could not announce to map server {to.Id} — {e.Message}");
            return false;
        }
    }

    /// <summary>What became of a private message: the status the holding map server gave (200 delivered, 404 not
    /// playing there, 409 changing maps, 429 receiving too many), 404 when nobody by that name was handed over, 503
    /// when the server could not be reached. <see cref="Name"/> is the name as the character spells it.</summary>
    public sealed record PmResult(int Status, string? Name);

    /// <summary>Hand a private message to the one map server last given the character with this name. ONE server is
    /// asked, found through <see cref="heldNames"/> and <see cref="heldBy"/>; nobody is searched for. A "not here"
    /// answer leaves heldBy alone: it is owned by announcements, and somebody between two servers is still on their way
    /// to the one written down.</summary>
    public async Task<PmResult> DeliverPmAsync(string toName, string fromName, string text,
                                               CancellationToken ct = default)
    {
        if (!heldNames.TryGetValue(toName.ToLowerInvariant(), out var who)) return new(404, null);
        if (!heldBy.TryGetValue(who.id, out var serverId) || !byId.TryGetValue(serverId, out var server))
            return new(404, who.name);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{server.ArrivalsUrl}/pm")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { toCharacterId = who.id, fromName, text }),
                                        System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-server-secret", config.ServerSecret);

        try
        {
            var reply = await wire.SendAsync(request, ct);
            int status = (int)reply.StatusCode;
            return status is 200 or 404 or 409 or 429 ? new(status, who.name) : new(503, who.name);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[char] map server {server.Id} did not answer about a private message — {e.Message}");
            return new(503, who.name);
        }
    }

    /// <summary>Hand the world events list to EVERY map server still believed to be there, all at once — the one
    /// question every server has to hear. One that misses it catches up on its own poll (see WorldEvents), so a
    /// failure here is logged and nothing more.</summary>
    public async Task PushEventsAsync(string list)
    {
        var sends = new List<Task>();
        foreach (var server in Alive) sends.Add(PushEventsTo(server, list));
        await Task.WhenAll(sends);
    }

    async Task PushEventsTo(Registration server, string list)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{server.ArrivalsUrl}/events")
        {
            Content = new StringContent(list, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-server-secret", config.ServerSecret);
        try
        {
            var reply = await wire.SendAsync(request);
            if (!reply.IsSuccessStatusCode)
                Console.Error.WriteLine($"[char] map server {server.Id} refused the world events ({(int)reply.StatusCode}) — it will catch up on its own poll.");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[char] map server {server.Id} did not take the world events — {e.Message}. It will catch up on its own poll.");
        }
    }
}
