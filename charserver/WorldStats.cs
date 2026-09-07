using System.Text.Json;
using Npgsql;

namespace CharServer;

/// <summary>
/// The one PUBLIC read of what the world looks like right now — how many people are playing, which maps are
/// standing and on which process, and who has earned the most. The player wiki draws its status page from this
/// and nothing else does.
///
/// <para><b>No token and no secret, on purpose</b>, because a wiki page is opened by anybody. That is why it
/// answers with counts and public names ONLY: never an account, never an address a player could be reached at,
/// never anybody's position. Everything else this server knows stays behind sign-in or the shared server
/// secret, exactly as before.</para>
///
/// <para><b>Worked out on a beat, not per reader.</b> One answer is built at most every
/// <see cref="FreshFor"/> and handed to everyone who asks inside that window, so a page open on a thousand
/// screens costs this server the same as one open on a single screen: one database read per half minute. The
/// list of processes needs no database at all — the map servers already say what they are holding.</para>
/// </summary>
public sealed class WorldStats(Db db, MapServers maps, Config config)
{
    /// <summary>How long one built answer is handed out for. Matched to the beat a map server re-announces
    /// itself on (30 seconds), because a shorter window cannot produce a newer number — it would only ask the
    /// database more often for the same answer.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(30);

    readonly SemaphoreSlim oneBuilderAtATime = new(1, 1);
    string? built;
    DateTimeOffset builtAt;

    public void MapOnto(WebApplication app)
    {
        app.MapGet("/stats", async (HttpContext http) =>
        {
            var body = await CurrentAsync(http.RequestAborted);

            // ONLY THIS ROUTE IS OPENED TO A BROWSER PAGE. The wiki is served from its own address, so a browser
            // treats it as a different site and refuses to hand it this reply unless the reply says otherwise —
            // silently, with nothing in the console. Named here rather than switched on for the whole server so
            // that opening the status page cannot also open the doors a player's token is meant to guard.
            var asking = http.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(asking) && config.WikiOrigins.Contains(asking))
                http.Response.Headers.AccessControlAllowOrigin = asking;

            return Results.Content(body, "application/json; charset=utf-8");
        });
    }

    async Task<string> CurrentAsync(CancellationToken ct)
    {
        if (built is not null && DateTimeOffset.UtcNow - builtAt < FreshFor) return built;

        await oneBuilderAtATime.WaitAsync(ct);
        try
        {
            // ASKED AGAIN INSIDE THE GATE. Several requests can find the answer stale at the same moment and
            // queue here; without this, each would then build its own, which is the stampede the cache exists
            // to prevent — worst at exactly the busy moment it matters most.
            if (built is not null && DateTimeOffset.UtcNow - builtAt < FreshFor) return built;

            built = await BuildAsync(ct);
            builtAt = DateTimeOffset.UtcNow;
            return built;
        }
        finally
        {
            oneBuilderAtATime.Release();
        }
    }

    async Task<string> BuildAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var alive = maps.Alive;

        var servers = new List<object>(alive.Count);
        int playersOnline = 0;
        foreach (var reg in alive.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            playersOnline += reg.Players;
            servers.Add(new
            {
                id = reg.Id,
                maps = reg.Maps,
                players = reg.Players,
                creatures = reg.Creatures,
                lastHeardSecondsAgo = (int)Math.Max(0, (now - reg.At).TotalSeconds),
            });
        }

        var answer = new
        {
            measuredAt = now.ToString("o"),
            playersOnline,
            mapsOnline = alive.Sum(r => r.Maps.Length),
            servers,
            topPlayers = await TopPlayersAsync(ct),
        };
        return JsonSerializer.Serialize(answer);
    }

    /// <summary>How many names the board shows. A board is read at a glance, and a longer one is a longer
    /// database read for rows nobody scrolls to.</summary>
    const int BoardSize = 20;

    async Task<List<object>> TopPlayersAsync(CancellationToken ct)
    {
        // WHAT IS READ, AND WHAT IS DELIBERATELY NOT. A name and a level are what a player already sees above
        // somebody's head in the world; the account behind them is not, and never leaves this server.
        //
        // A character saved before the game recorded a lifetime figure has none, and 0 is a real answer, so a
        // missing one sorts BELOW every real one rather than counting as zero. The figure is never worked out
        // from the level here — the game owns that arithmetic, and a second copy of it in this server is a
        // second thing to keep in step (migration 005 filled every existing row in one pass).
        const string Sql = """
            select name,
                   case when jsonb_typeof(progress->'level') = 'number'
                        then (progress->>'level')::bigint else 0 end as level,
                   case when jsonb_typeof(progress->'lifetimeXp') = 'number'
                        then (progress->>'lifetimeXp')::bigint else null end as lifetime
              from character
             where deleted_at is null
               and progress ? 'level'
             order by lifetime desc nulls last, level desc, name asc
             limit $1
            """;

        await using var conn = await db.ConnectAsync(ct);
        await using var cmd = new NpgsqlCommand(Sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = BoardSize });

        var board = new List<object>(BoardSize);
        await using var rows = await cmd.ExecuteReaderAsync(ct);
        int rank = 0;
        while (await rows.ReadAsync(ct))
        {
            rank++;
            board.Add(new
            {
                rank,
                name = rows.GetString(0),
                level = rows.GetInt64(1),
                lifetimeXp = rows.IsDBNull(2) ? (long?)null : rows.GetInt64(2),
            });
        }
        return board;
    }
}
