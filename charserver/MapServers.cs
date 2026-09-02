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
        int Port,
        int PlayerPort,
        string[] Maps,
        string Build,
        DateTimeOffset At);

    readonly ConcurrentDictionary<string, Registration> byId = new();

    // Which map lives where. Rebuilt whenever a registration changes rather than searched each time — this is
    // asked on every entry into the world, and the answer only moves when a map server comes or goes.
    volatile Dictionary<string, Registration> byMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What this pair of servers must agree on before either trusts the other with a character.
    ///
    /// <para>It is NOT a security measure — that link is between two machines you run, and the shared secret
    /// below is what keeps strangers off it. It catches a HALF-FINISHED DEPLOY: a map server built against a
    /// different character than this one understands would read the pushed bytes into the wrong fields and
    /// store somebody's life slightly wrong, silently. Refusing to start is the cheap failure.</para></summary>
    public const string CharacterBuild = "unwritten-character-v9";

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

    public Registration Register(string id, string address, int port, int playerPort, string[] maps, string build)
    {
        var reg = new Registration(id, address, port, playerPort, maps ?? [], build, DateTimeOffset.UtcNow);
        byId[id] = reg;
        Rebuild();
        return reg;
    }

    public bool Forget(string id)
    {
        bool gone = byId.TryRemove(id, out _);
        if (gone) Rebuild();
        return gone;
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
            Console.WriteLine($"[char] map server '{id}' has said nothing for {TrustedFor.TotalMinutes:0} minutes — forgotten.");
        }
        Rebuild();
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
    /// so there is no second description of a character to keep in step with the first.</para></summary>
    public async Task<bool> AnnounceAsync(Registration to, Ticket ticket, string characterName,
                                          JsonElement progress, bool isGm, CancellationToken ct = default)
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
        });

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"http://{to.Address}:{to.Port}/arriving")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-server-secret", config.ServerSecret);

        try
        {
            var reply = await wire.SendAsync(request, ct);
            return reply.IsSuccessStatusCode;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[char] could not announce to map server {to.Id} — {e.Message}");
            return false;
        }
    }
}
