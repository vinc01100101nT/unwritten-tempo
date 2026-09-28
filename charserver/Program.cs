using System.Text.Json;
using CharServer;
using Npgsql;

// The character server. It owns what a character IS — the list, the creation, the deletion, the save — and it
// is the only thing that reads or writes those rows. The map server never touches the database; it asks here.
//
// It does NOT sign anybody in. That belongs to the login service, which issues a token this server verifies
// with the same secret. Two programs able to mint identity is the thing that arrangement avoids.

var config = Config.FromEnvironment();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://{config.BindHost}:{config.Port}");

// NOBODY IS TOLD HOW THIS SERVER IS BUILT. Left as the scaffolding made it, an unhandled fault answers the
// caller with the exception and its stack — which anyone can reach, without a token, by sending a malformed
// body. The setting is still honoured if it is deliberately set; what changes is that the developer-facing
// behaviour is no longer what you get by accident.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")))
    builder.Environment.EnvironmentName = Environments.Production;

// A save larger than the ceiling is refused by the SERVER before the body is ever read, rather than after it
// has been parsed — measuring it afterwards means having already spent the memory the ceiling exists to cap.
// TWO CEILINGS, AND THE ORDER MATTERS. The one that should normally do the refusing is the one in the code,
// because it can say WHY — "that save is too large" reaches the player, where the wire's refusal is an empty
// reply the game can only report as "the request failed". So the wire is set well above the save ceiling and
// is purely the backstop against a body nobody should be sending at all; anything merely over the documented
// limit sails past it and is turned away, in words, by the route.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = config.MaxProgressBytes * 4L);

var app = builder.Build();

Db db;
Auth auth;
try
{
    db = Db.Open(config.DatabaseUrl);
    auth = new Auth(config.JwtSecret);
}
catch (Exception e)
{
    // INSIDE THE GUARD, because opening the pool reads the address apart and an address it cannot read throws
    // right here — before the check below that exists to say so kindly. That put the raw fault on the screen
    // in place of the one sentence naming the setting to look at.
    Console.Error.WriteLine($"[char] cannot use DATABASE_URL — {e.Message}");
    return 1;
}

// The roster the login service already reads, read from the same file rather than copied. One owner.
var rosterPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                              "accounts", "data", "character-models.json");
var roster = Roster.Load(Environment.GetEnvironmentVariable("CHARACTER_MODELS_PATH")
                         ?? Path.GetFullPath(rosterPath));

// IS SOMEBODY ALREADY THERE. Asked BEFORE listening, because the alternative is what it does without this: the
// wire layer throws, and the failure reaches whoever ran it as a page of stack trace with the one useful fact —
// "address already in use" — buried in the middle of it. The other two servers here both refuse in one line and
// quit; this one now does the same, so all three fail the same way.
{
    var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, config.Port);
    try
    {
        probe.Start();
        probe.Stop();
    }
    catch (System.Net.Sockets.SocketException)
    {
        Console.Error.WriteLine($"[char] port {config.Port} is already taken — another character server is "
                                + "running. This one is quitting rather than sitting here listening to nothing. "
                                + "Stop the other one first, or give this one a different CHAR_SERVER_PORT.");
        Console.Error.WriteLine($"[char] check with:  lsof -nP -iTCP:{config.Port} -sTCP:LISTEN");
        return 1;
    }
}

// IS THE DATABASE ACTUALLY THERE. Asked before the port is opened, because the failure it prevents is silent:
// a server that is listening but cannot read a character looks completely healthy from outside and fails on
// the first player instead of at start-up, where somebody is watching.
try
{
    await db.CheckReachableAsync();
}
catch (Exception e)
{
    Console.Error.WriteLine($"[char] cannot reach the database — {e.Message}");
    Console.Error.WriteLine("[char] quitting rather than listening to nothing. Check DATABASE_URL.");
    return 1;
}

var maps = new MapServers(config);
var gameVersion = new GameVersion(config.PatchDir);
new Characters(db, auth, roster, maps, gameVersion).MapOnto(app);

// The one public read: what the world looks like right now, for the player wiki's status page. No token, no
// secret, counts and public names only — see WorldStats.
new WorldStats(db, maps, config).MapOnto(app);

// ---- the map servers' own door ----------------------------------------------------------------------------
// Reached with the shared SERVER secret, never with a player's token. A person signing in must not be able to
// register a map server, and a map server does not sign in.

app.MapPost("/map-servers", async (HttpContext http, MapServerRegistration? reg) =>
{
    if (!maps.IsAServer(http.Request)) return Results.Json(new { error = "not a server" }, statusCode: 401);
    if (reg is null || string.IsNullOrWhiteSpace(reg.id) || string.IsNullOrWhiteSpace(reg.address))
        return Results.Json(new { error = "send { id, address, port, maps, build }" }, statusCode: 400);

    // BOTH HALVES MUST HAVE BEEN BUILT FROM THE SAME UNDERSTANDING OF A CHARACTER. A map server built against a
    // different one would read the pushed character into the wrong fields and store somebody's life slightly
    // wrong, silently, which is the most expensive kind of wrong. Refusing to start is the cheap failure.
    if (reg.build != MapServers.CharacterBuild)
        return Results.Json(new
        {
            error = "that map server was built against a different character than this one understands",
            expected = MapServers.CharacterBuild,
            got = reg.build ?? "",
        }, statusCode: 409);

    var registered = maps.Register(reg.id, reg.address, reg.port, reg.playerPort, reg.maps ?? [], reg.build,
                                   reg.players, reg.creatures, reg.startingMap);
    Console.WriteLine($"[char] map server '{registered.Id}' at {registered.Address}:{registered.Port} " +
                      $"holds {registered.Maps.Length} map(s): {string.Join(", ", registered.Maps)} " +
                      $"[{registered.Players} player(s), {registered.Creatures} creature(s)]");
    return Results.Ok(new { ok = true, holding = registered.Maps.Length });
});

app.MapDelete("/map-servers/{id}", (HttpContext http, string id) =>
{
    if (!maps.IsAServer(http.Request)) return Results.Json(new { error = "not a server" }, statusCode: 401);
    bool gone = maps.Forget(id);
    if (gone) Console.WriteLine($"[char] map server '{id}' has gone.");
    return gone ? Results.NoContent() : Results.NotFound();
});

// WHERE A MAP SERVER WRITES A CHARACTER DOWN. It has no database of its own and never will — this is the
// only way what happened in the world becomes permanent. Reached with the SERVER secret and scoped by the
// character id alone, because a map server is not a person and has no account to be scoped to.
app.MapPut("/server/characters/{id}/progress", async (HttpContext http, string id, JsonElement body) =>
{
    if (!maps.IsAServer(http.Request)) return Results.Json(new { error = "not a server" }, statusCode: 401);
    if (!Guid.TryParseExact(id, "D", out var characterId))
        return Results.Json(new { error = "invalid character id" }, statusCode: 400);
    if (!body.TryGetProperty("progress", out var progress))
        return Results.Json(new { error = "send { progress: … }" }, statusCode: 400);

    var raw = progress.GetRawText();
    if (raw.Length > config.MaxProgressBytes)
        return Results.Json(new { error = "that save is too large" }, statusCode: 413);

    var verdict = Progress.Check(progress);
    if (!verdict.Ok) return Results.Json(new { error = verdict.Reason }, statusCode: 400);

    // The same rule a player's own save follows: a save that names no body keeps the look already stored, so a
    // build that does not carry appearance cannot wipe it.
    bool keepsLook = progress.TryGetProperty("modelName", out var m)
                     && m.ValueKind == JsonValueKind.String && (m.GetString()?.Length ?? 0) > 0;

    await using var conn = await db.ConnectAsync();
    await using var cmd = new Npgsql.NpgsqlCommand(keepsLook
        ? "update character set progress = $2, updated_at = now() where id = $1 and deleted_at is null returning id"
        : @"update character
               set progress = $2::jsonb
                            || jsonb_build_object('modelName',  progress -> 'modelName',
                                                  'dyeSources', progress -> 'dyeSources',
                                                  'dyeTargets', progress -> 'dyeTargets'),
                   updated_at = now()
             where id = $1 and deleted_at is null returning id", conn);
    cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = characterId });
    cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = raw, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb });

    var wrote = await cmd.ExecuteScalarAsync();
    return wrote is null
        ? Results.Json(new { error = "no such character" }, statusCode: 404)
        : Results.Ok(new { ok = true });
});

// WHERE A PLAYER GOES WHEN THEY WALK THROUGH A DOOR INTO A ZONE ANOTHER PROCESS HOLDS. Asked by the map server
// they are LEAVING, never by the player: only that server knows whether it has finished writing them down, and
// only it can put them back where they were if this fails. Reached with the SERVER secret.
//
// The character is read from the store rather than travelling with the request, and that is safe only because
// the process asking has already written them down and waited for it to land — which is the step before this one.
app.MapPost("/server/characters/{id}/move", async (HttpContext http, string id, MoveBody? body) =>
{
    if (!maps.IsAServer(http.Request)) return Results.Json(new { error = "not a server" }, statusCode: 401);
    if (!Guid.TryParseExact(id, "D", out var characterId))
        return Results.Json(new { error = "invalid character id" }, statusCode: 400);
    if (body is null || string.IsNullOrWhiteSpace(body.map))
        return Results.Json(new { error = "send { map: … }" }, statusCode: 400);

    await using var conn = await db.ConnectAsync();
    await using var read = new NpgsqlCommand(
        "select id, name, progress from character where id = $1 and deleted_at is null", conn);
    read.Parameters.Add(new NpgsqlParameter { Value = characterId });

    string name;
    JsonDocument progress;
    await using (var rows = await read.ExecuteReaderAsync())
    {
        if (!await rows.ReadAsync()) return Results.Json(new { error = "no such character" }, statusCode: 404);
        name = rows.GetString(1);
        progress = JsonDocument.Parse(rows.GetString(2));
    }

    using (progress)
    {
        // SOMEBODY MUST BE HOLDING IT BEFORE ANYBODY IS SENT TO IT. Nobody is given a list of zones at start-up,
        // so a zone nobody is standing in is held by nobody — the ordinary state, not a fault. This puts it on
        // the quietest process and waits for it to actually stand up.
        var to = (await maps.EnsureHeldAsync(body.map, http.RequestAborted)).Server;
        if (to is null)
            return Results.Json(new { error = "the world could not bring that map up just now" }, statusCode: 503);

        var ticket = maps.MintTicket(body.accountId ?? "", characterId.ToString());
        bool taken = await maps.AnnounceAsync(to, ticket, name, progress.RootElement, body.isGm,
                                              http.RequestAborted);
        if (!taken)
            return Results.Json(new { error = "the world would not take them just now" }, statusCode: 503);

        Console.WriteLine($"[char] moving '{name}' to '{body.map}' on map server '{to.Id}'.");
        return Results.Ok(new { host = to.Address, port = to.PlayerPort, ticket = ticket.Value, map = body.map });
    }
});

// DELIBERATELY NO "spend this ticket" DOOR. A ticket is handed to the map server in the same breath as the
// character it belongs to, so that server already holds it and checks it against its own list — one place, no
// round trip while somebody waits at the door. A second door here would be a second answer to one question, and
// the two would eventually disagree about whether a ticket had been used.

// Something to ask that costs nothing and needs no token, so whoever runs this can tell it is alive without
// having to sign in first.
// IT ASKS THE DATABASE. Answering from memory made this say "ok" for as long as the process was alive, whether
// or not a character could still be read — which is the same "listening but cannot serve anybody" failure the
// start-up check above exists to prevent, moved to after start-up where nobody is watching.
app.MapGet("/health", async () =>
{
    try
    {
        await db.CheckReachableAsync();
        return Results.Ok(new { ok = true, models = roster.Models.Count });
    }
    catch (Exception e)
    {
        return Results.Json(new { ok = false, error = e.Message }, statusCode: 503);
    }
});

Console.WriteLine($"[char] listening on {config.Port}, {roster.Models.Count} character models, database reachable.");
Console.WriteLine(gameVersion.IsOn
    ? $"[char] game version check ON — players need version '{gameVersion.Current ?? "(none published yet)"}' from {config.PatchDir}"
    : "[char] game version check off — PATCH_DIR is not set, so no copy is turned away for being out of date.");
app.Run();
return 0;

// `players` and `creatures` are how busy that process says it is — the two numbers a zone is placed by. An older
// map server that does not send them registers as idle, which is honest: it has not told us otherwise.
record MapServerRegistration(string? id, string? address, int port, int playerPort, string[]? maps, string? build,
                             int players = 0, int creatures = 0, string? startingMap = null);
record SpendBody(string? ticket);
record MoveBody(string? map, string? accountId, bool isGm);
