using System.Text.Json;
using Npgsql;

namespace CharServer;

/// <summary>
/// WORLD EVENTS — a timed change a GM makes to the whole world at once, such as an experience rate. This server holds
/// the one true list (the <c>world_event</c> table) and every map server copies it: each is PUSHED the list the moment
/// it changes, and each also asks for it on its own beat, so one that missed a push, restarted, or started later
/// still converges within one beat.
///
/// <para><b>Every copy carries the list's REVISION</b>, raised in the same transaction as every start and stop. A push
/// and a poll answer can cross on their way to a map server; it keeps the newer and ignores the older, so an event that
/// was replaced or stopped is never brought back by a copy read before the change.</para>
///
/// <para>Only map servers come through these doors, behind the shared server secret. A player asks their map server
/// (the <c>/exprate</c> command, GM only), and that server asks here.</para>
/// </summary>
public sealed class WorldEvents(Db db, MapServers maps)
{
    /// <summary>The kinds there are. A kind this list does not name is refused at the door.</summary>
    public static readonly string[] Kinds = { "experience_rate" };

    /// <summary>How long one read of the list is handed to every map server that asks — the beat many map servers
    /// poll on costs one database read per window, however many of them there are. A start or a stop clears it.</summary>
    static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(5);

    readonly SemaphoreSlim oneReaderAtATime = new(1, 1);
    string? cached;
    DateTimeOffset cachedAt;

    public sealed record StartBody(string? kind, double multiplier, long seconds, string? startedBy);
    public sealed record StopBody(string? kind, string? stoppedBy);

    public void MapOnto(WebApplication app)
    {
        app.MapGet("/server/events", async (HttpContext http) =>
        {
            if (!maps.IsAServer(http.Request)) return Results.Json(new { error = "not a server" }, statusCode: 401);
            try { return Results.Content(await CurrentAsync(http.RequestAborted), "application/json; charset=utf-8"); }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[char] could not read the world events — {e.Message}");
                return Results.Json(new { error = "the world events could not be read just now" }, statusCode: 503);
            }
        });

        app.MapPost("/server/events", async (HttpContext http, StartBody? body) =>
        {
            if (!maps.IsAServer(http.Request)) return Results.Json(new { error = "not a server" }, statusCode: 401);
            if (body is null || !Kinds.Contains(body.kind) || string.IsNullOrWhiteSpace(body.startedBy))
                return Results.Json(new { error = "send { kind, multiplier, seconds, startedBy }" }, statusCode: 400);
            if (!(body.multiplier > 0) || double.IsInfinity(body.multiplier))
                return Results.Json(new { error = "the multiplier must be a number above 0" }, statusCode: 400);
            if (body.seconds < 1 || body.seconds > int.MaxValue)
                return Results.Json(new { error = "the duration must be at least one second" }, statusCode: 400);

            string list;
            await using (var conn = await db.ConnectAsync(http.RequestAborted))
            await using (var tx = await conn.BeginTransactionAsync(http.RequestAborted))
            {
                await BumpRevisionAsync(conn, tx, http.RequestAborted);
                // A REPLACEMENT SETS EVERYTHING AFRESH, the start time included: the start time is how a map server
                // tells a new event from the one it already holds, and keeping the old one would make the new
                // multiplier arrive as a silent refresh of the old event.
                await using (var cmd = new NpgsqlCommand(
                    @"insert into world_event (kind, multiplier, started_at, ends_at, started_by)
                      values ($1, $2, now(), now() + make_interval(secs => $3), $4)
                      on conflict (kind) do update set multiplier = excluded.multiplier, started_at = excluded.started_at,
                                                       ends_at = excluded.ends_at, started_by = excluded.started_by",
                    conn, tx))
                {
                    cmd.Parameters.Add(new NpgsqlParameter { Value = body.kind });
                    cmd.Parameters.Add(new NpgsqlParameter { Value = body.multiplier });
                    cmd.Parameters.Add(new NpgsqlParameter { Value = (double)body.seconds });
                    cmd.Parameters.Add(new NpgsqlParameter { Value = body.startedBy!.Trim() });
                    await cmd.ExecuteNonQueryAsync(http.RequestAborted);
                }
                list = await ReadAsync(conn, tx, http.RequestAborted);
                await tx.CommitAsync(http.RequestAborted);
            }
            Console.WriteLine($"[char] {body.startedBy} started {body.kind} ×{body.multiplier} for {body.seconds}s.");
            return await Changed(list);
        });

        app.MapPost("/server/events/stop", async (HttpContext http, StopBody? body) =>
        {
            if (!maps.IsAServer(http.Request)) return Results.Json(new { error = "not a server" }, statusCode: 401);
            if (body is null || !Kinds.Contains(body.kind) || string.IsNullOrWhiteSpace(body.stoppedBy))
                return Results.Json(new { error = "send { kind, stoppedBy }" }, statusCode: 400);

            string list;
            int stopped;
            await using (var conn = await db.ConnectAsync(http.RequestAborted))
            await using (var tx = await conn.BeginTransactionAsync(http.RequestAborted))
            {
                await using (var cmd = new NpgsqlCommand("delete from world_event where kind = $1 and ends_at > now()", conn, tx))
                {
                    cmd.Parameters.Add(new NpgsqlParameter { Value = body.kind });
                    stopped = await cmd.ExecuteNonQueryAsync(http.RequestAborted);
                }
                if (stopped > 0) await BumpRevisionAsync(conn, tx, http.RequestAborted);
                list = await ReadAsync(conn, tx, http.RequestAborted);
                await tx.CommitAsync(http.RequestAborted);
            }
            if (stopped == 0) return Results.Json(new { error = "no such event is running" }, statusCode: 404);
            Console.WriteLine($"[char] {body.stoppedBy} stopped {body.kind}.");
            return await Changed(list);
        });
    }

    // A change: the cached copy is stale, every map server still believed to be there is pushed the new list, and the
    // asker is answered with it too — so the GM's own map server applies it without waiting for its push.
    async Task<IResult> Changed(string list)
    {
        cached = null;
        await maps.PushEventsAsync(list);
        return Results.Content(list, "application/json; charset=utf-8");
    }

    async Task<string> CurrentAsync(CancellationToken ct)
    {
        if (cached is not null && DateTimeOffset.UtcNow - cachedAt < FreshFor) return cached;
        await oneReaderAtATime.WaitAsync(ct);
        try
        {
            if (cached is not null && DateTimeOffset.UtcNow - cachedAt < FreshFor) return cached;
            await using var conn = await db.ConnectAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var list = await ReadAsync(conn, tx, ct);
            await tx.CommitAsync(ct);
            cached = list;
            cachedAt = DateTimeOffset.UtcNow;
            return list;
        }
        finally { oneReaderAtATime.Release(); }
    }

    static async Task BumpRevisionAsync(NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        // The row lock this takes is also what puts two GMs' starts in one order: the second waits for the first to
        // commit, so the higher revision is always the later change.
        await using var cmd = new NpgsqlCommand("update world_event_revision set revision = revision + 1", conn, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>The list as every map server reads it: <c>{ revision, events: [ { kind, multiplier, startedAtMs,
    /// secondsLeft } ] }</c>. Time left, not an end time — each map server counts it down on its own clock, so the
    /// machines' clocks never have to agree. Expired rows are deleted first, so nothing grows.</summary>
    static async Task<string> ReadAsync(NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        await using (var sweep = new NpgsqlCommand("delete from world_event where ends_at <= now()", conn, tx))
            await sweep.ExecuteNonQueryAsync(ct);

        // THE REVISION BEFORE THE ROWS. A start committed between the two reads can then only make the rows NEWER than
        // the revision says, never older — and a map server that takes newer rows under an older number is simply
        // brought level by the push carrying the newer number, with nothing announced twice.
        long revision;
        await using (var rev = new NpgsqlCommand("select revision from world_event_revision", conn, tx))
            revision = (long)(await rev.ExecuteScalarAsync(ct) ?? 0L);

        var events = new List<object>();
        await using (var cmd = new NpgsqlCommand(
            @"select kind, multiplier, (extract(epoch from started_at) * 1000)::bigint,
                     extract(epoch from ends_at - now())::double precision
                from world_event", conn, tx))
        await using (var rows = await cmd.ExecuteReaderAsync(ct))
            while (await rows.ReadAsync(ct))
                events.Add(new
                {
                    kind = rows.GetString(0),
                    multiplier = rows.GetDouble(1),
                    startedAtMs = rows.GetInt64(2),
                    secondsLeft = rows.GetDouble(3),
                });

        return JsonSerializer.Serialize(new { known = true, revision, events });
    }
}
