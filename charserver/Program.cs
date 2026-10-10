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
// The host's own start-up lines are held back: "Now listening on: http://0.0.0.0:8081" is an address nobody can
// open. Addresses (below, once started) prints the ones that answered instead. Its other three lines go with it.
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);

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
        Console.Error.WriteLine($"[char] check with:  tools/platform.sh port-info {config.Port}");
        // 2, not 1: starting again cannot fix a port somebody else holds, so whatever restarts this server
        // (charserver/run.sh, or the service on a rented machine) stops instead of trying forever.
        return 2;
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

// THE CLOCK FOR THE FIRST MOMENTS AFTER A START, started now rather than on the first request — see StartupGrace.
StartupGrace.Begin();

var maps = new MapServers(config);
var gameVersion = new GameVersion(config.PatchDir);
new Characters(db, auth, roster, maps, gameVersion).MapOnto(app);

// The one public read: what the world looks like right now, for the website's Server Status page. No token, no
// secret, counts and public names only — see WorldStats.
new WorldStats(db, maps, config).MapOnto(app);

// Timed changes a GM makes to the whole world at once — see WorldEvents. Map servers only, behind the server secret.
new WorldEvents(db, maps).MapOnto(app);

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

    var registered = maps.Register(reg.id, reg.address, reg.arrivalsAddress, reg.port, reg.playerPort,
                                   reg.maps ?? [], reg.build, reg.players, reg.creatures, reg.startingMap);
    // Answered at once; the check-in is said after its address has been tried, which can take a second.
    _ = maps.SayCheckIn(registered);
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

    // WHICH HAND-OVER THIS SAVE BELONGS TO. 0 (or absent) is a map server built before hand-overs were numbered,
    // and is not checked; anything else must be the live one, or the save is refused (see Handover).
    int handover = body.TryGetProperty("handover", out var h) && h.ValueKind == JsonValueKind.Number
                   && h.TryGetInt32(out var hv) ? hv : 0;

    // THE QUEST ROWS THAT CHANGED, riding beside the progress so the two land together or not at all (see
    // 009_character_quest.sql). Absent or empty is the ordinary case: nothing about any quest moved.
    var questCheck = QuestRows.Check(body, handover);
    if (!questCheck.Ok) return Results.Json(new { error = questCheck.Reason }, statusCode: 400);

    await using var conn = await db.ConnectAsync();
    await using var tx = await conn.BeginTransactionAsync();
    await using var cmd = new Npgsql.NpgsqlCommand(keepsLook
        ? @"update character set progress = $2, updated_at = now()
             where id = $1 and deleted_at is null and ($3 = 0 or handover = $3) returning id"
        : @"update character
               set progress = $2::jsonb
                            || jsonb_build_object('modelName',  progress -> 'modelName',
                                                  'dyeSources', progress -> 'dyeSources',
                                                  'dyeTargets', progress -> 'dyeTargets')
                            -- THE HAIR, only where one is stored: a key the stored save never had must not be
                            -- written in as a null over the empty one the map server sent.
                            || jsonb_strip_nulls(jsonb_build_object('hairStyle',  progress -> 'hairStyle',
                                                                    'hairColour', progress -> 'hairColour')),
                   updated_at = now()
             where id = $1 and deleted_at is null and ($3 = 0 or handover = $3) returning id", conn, tx);
    cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = characterId });
    cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = raw, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb });
    cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = handover });

    if (await cmd.ExecuteScalarAsync() is not null)
    {
        // Only once the progress itself was taken: the same character, the same hand-over, the same transaction.
        if (questCheck.Rows is { } questRows) await QuestRows.WriteAsync(conn, tx, characterId, questRows);
        await tx.CommitAsync();
        return Results.Ok(new { ok = true });
    }
    await tx.RollbackAsync();
    if (handover == 0) return Results.Json(new { error = "no such character" }, statusCode: 404);

    // NOT WRITTEN, AND A NUMBER WAS GIVEN: either the character is gone, or this save is from an older hand-over.
    await using var live = new Npgsql.NpgsqlCommand(
        "select handover from character where id = $1 and deleted_at is null", conn);
    live.Parameters.Add(new Npgsql.NpgsqlParameter { Value = characterId });
    if (await live.ExecuteScalarAsync() is not int current)
        return Results.Json(new { error = "no such character" }, statusCode: 404);
    Console.WriteLine($"[char] refused a save for character {characterId} from hand-over {handover} — "
                      + $"they are on hand-over {current} now, so it would have rolled them back.");
    return Results.Json(new { error = "that save is from an older hand-over", live = current }, statusCode: 409);
});

// WHERE A MAP SERVER WRITES A CHARACTER'S HOTBAR DOWN — only when a slot changes, so the progress save above no
// longer rewrites the bar every time it runs. Guarded exactly as that door is: the SERVER secret, and the same
// hand-over rule, so a map server the character has already left cannot put an old bar back.
const int MaxHotbarSlots = 64;
const int MaxHotbarSlotLength = 160;
app.MapPut("/server/characters/{id}/hotbar", async (HttpContext http, string id, JsonElement body) =>
{
    if (!maps.IsAServer(http.Request)) return Results.Json(new { error = "not a server" }, statusCode: 401);
    if (!Guid.TryParseExact(id, "D", out var characterId))
        return Results.Json(new { error = "invalid character id" }, statusCode: 400);
    if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("slots", out var slots)
        || slots.ValueKind != JsonValueKind.Array)
        return Results.Json(new { error = "send { slots: [ … ] }" }, statusCode: 400);
    if (slots.GetArrayLength() > MaxHotbarSlots)
        return Results.Json(new { error = $"a hotbar has at most {MaxHotbarSlots} slots" }, statusCode: 400);
    foreach (var slot in slots.EnumerateArray())
        if (slot.ValueKind != JsonValueKind.String || (slot.GetString()?.Length ?? 0) > MaxHotbarSlotLength)
            return Results.Json(new { error = $"every slot is a string of at most {MaxHotbarSlotLength} characters" },
                                statusCode: 400);

    // The same numbering the progress door reads: 0 (or absent) is not checked, anything else must be the live one.
    int handover = body.TryGetProperty("handover", out var h) && h.ValueKind == JsonValueKind.Number
                   && h.TryGetInt32(out var hv) ? hv : 0;

    await using var conn = await db.ConnectAsync();
    // Written only when the character exists, is not deleted, and (when a number was given) is on that hand-over —
    // the select finds no row otherwise, so nothing is inserted and nothing is updated.
    await using var cmd = new NpgsqlCommand(
        @"insert into character_hotbar (character_id, slots, updated_at)
          select id, $2, now() from character
           where id = $1 and deleted_at is null and ($3 = 0 or handover = $3)
          on conflict (character_id) do update set slots = excluded.slots, updated_at = now()
          returning character_id", conn);
    cmd.Parameters.Add(new NpgsqlParameter { Value = characterId });
    cmd.Parameters.Add(new NpgsqlParameter { Value = slots.GetRawText(), NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb });
    cmd.Parameters.Add(new NpgsqlParameter { Value = handover });

    if (await cmd.ExecuteScalarAsync() is not null) return Results.Ok(new { ok = true });
    if (handover == 0) return Results.Json(new { error = "no such character" }, statusCode: 404);

    // NOT WRITTEN, AND A NUMBER WAS GIVEN: either the character is gone, or this bar is from an older hand-over.
    await using var live = new NpgsqlCommand(
        "select handover from character where id = $1 and deleted_at is null", conn);
    live.Parameters.Add(new NpgsqlParameter { Value = characterId });
    if (await live.ExecuteScalarAsync() is not int current)
        return Results.Json(new { error = "no such character" }, statusCode: 404);
    Console.WriteLine($"[char] refused a hotbar for character {characterId} from hand-over {handover} — "
                      + $"they are on hand-over {current} now, so it would have rolled them back.");
    return Results.Json(new { error = "that hotbar is from an older hand-over", live = current }, statusCode: 409);
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

    // SOMEBODY MUST BE HOLDING IT BEFORE ANYBODY IS SENT TO IT. Nobody is given a list of zones at start-up, so a
    // zone nobody is standing in is held by nobody — the ordinary state, not a fault. This puts it on the quietest
    // process and waits for it to actually stand up. Asked first: if it cannot come up, nothing below has happened and
    // the server they are leaving can still write them down.
    var to = (await maps.EnsureHeldAsync(body.map, http.RequestAborted)).Server;
    if (to is null)
        return Results.Json(new { error = "the world could not bring that map up just now" }, statusCode: 503);

    await using var conn = await db.ConnectAsync();
    // A NEW HAND-OVER, BEFORE THE READ: the server they are leaving can no longer write them down (see Handover), so
    // nothing it sends after this lands between the read and the announcement.
    if (await Handover.NextAsync(conn, characterId) is not int handover)
        return Results.Json(new { error = "no such character" }, statusCode: 404);

    // The hotbar rides in the same read: its own row, and none at all for a character that never had one written.
    // The quest rows come as one sub-selected JSON array (see QuestRows).
    await using var read = new NpgsqlCommand(
        @"select c.id, c.name, c.progress, h.slots, " + QuestRows.ReadAsJson + @"
            from character c left join character_hotbar h on h.character_id = c.id
           where c.id = $1 and c.deleted_at is null", conn);
    read.Parameters.Add(new NpgsqlParameter { Value = characterId });

    string name;
    JsonDocument progress;
    JsonDocument? hotbar;
    JsonDocument quests;
    await using (var rows = await read.ExecuteReaderAsync())
    {
        if (!await rows.ReadAsync()) return Results.Json(new { error = "no such character" }, statusCode: 404);
        name = rows.GetString(1);
        progress = JsonDocument.Parse(rows.GetString(2));
        hotbar = rows.IsDBNull(3) ? null : JsonDocument.Parse(rows.GetString(3));
        quests = JsonDocument.Parse(rows.GetString(4));
    }

    using (progress)
    using (hotbar)
    using (quests)
    {
        var ticket = maps.MintTicket(body.accountId ?? "", characterId.ToString());
        bool taken = await maps.AnnounceAsync(to, ticket, name, progress.RootElement, hotbar?.RootElement,
                                              quests.RootElement, body.isGm, handover, signingIn: false, http.RequestAborted);
        if (!taken)
            return Results.Json(new { error = "the world would not take them just now" }, statusCode: 503);

        Console.WriteLine($"[char] moving '{name}' to '{body.map}' on map server '{to.Id}'.");
        return Results.Ok(new { host = to.Address, port = to.PlayerPort, ticket = ticket.Value, map = body.map });
    }
});

// A PRIVATE MESSAGE, passed from the map server its sender is on to the one holding whoever it is for. The sender's
// name comes from that map server, which read it off the connection — never from anything a player typed — so this
// door is a server's, behind the server secret, like every other here. Nothing is stored: an offline name is told so.
app.MapPost("/server/pm", async (HttpContext http, PmBody? body) =>
{
    if (!maps.IsAServer(http.Request)) return Results.Json(new { error = "not a server" }, statusCode: 401);
    if (body is null || string.IsNullOrWhiteSpace(body.toName) || string.IsNullOrWhiteSpace(body.fromName)
        || string.IsNullOrWhiteSpace(body.text))
        return Results.Json(new { error = "send { toName, fromName, text }" }, statusCode: 400);
    // A name nobody could have is somebody who is not online, said the same way as any other.
    if (!Characters.IsAName(body.toName)) return Results.Json(new { name = body.toName }, statusCode: 404);

    var result = await maps.DeliverPmAsync(body.toName!, body.fromName!, body.text!, http.RequestAborted);
    return Results.Json(new { name = result.Name ?? body.toName }, statusCode: result.Status);
});

// A GM COMMAND OR A STATUS READ FROM A TERMINAL (tools/gm.py), passed to the map server holding the map it names. The
// server secret is the whole of the trust: whoever holds it can already do anything a map server can. A map nobody is
// holding is said so and NOT brought up — a command for an empty map has nobody to show it to.
async Task<IResult> ForTerminal(HttpContext http, TerminalBody? body, string path)
{
    if (!maps.IsAServer(http.Request)) return Results.Json(new { error = "not a server" }, statusCode: 401);
    if (body is null || string.IsNullOrWhiteSpace(body.map))
        return Results.Json(new { error = "send { map, line, as }" }, statusCode: 400);
    if (path == "gm" && string.IsNullOrWhiteSpace(body.line))
        return Results.Json(new { error = "send { map, line, as }" }, statusCode: 400);

    var server = maps.Holding(body.map);
    if (server is null)
        return Results.Json(new { error = $"no map server is holding {body.map} right now — nobody is in it" },
                            statusCode: 404);

    string characterId = "";
    if (!string.IsNullOrWhiteSpace(body.@as))
    {
        characterId = maps.CharacterIdFor(body.@as) ?? "";
        if (characterId.Length == 0)
            return Results.Json(new { error = $"nobody called {body.@as} is in the world" }, statusCode: 404);
    }

    string map = MapServers.SpelledAsHeld(server, body.map);
    Console.WriteLine($"[char] the terminal asked map server '{server.Id}' ({map}): {(path == "gm" ? body.line : "status")}"
                      + (characterId.Length > 0 ? $" as {body.@as}" : ""));
    var answer = await maps.AskForTerminalAsync(server, path, new { map, line = body.line ?? "", characterId },
                                                http.RequestAborted);
    return Results.Content(answer.Body, "application/json", statusCode: answer.Status);
}

app.MapPost("/server/gm", (HttpContext http, TerminalBody? body) => ForTerminal(http, body, "gm"));
app.MapPost("/server/status", (HttpContext http, TerminalBody? body) => ForTerminal(http, body, "status"));

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

Console.WriteLine($"[char] {roster.Models.Count} character models, database reachable.");
// SAID ONCE THE PORT IS REALLY OPEN, and only for the addresses that answered — see Addresses. The address players'
// copies dial is the same choice tools/build-game.sh makes when it writes it into a build.
var givenSetting = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PUBLIC_CHAR_SERVER_URL"))
    ? "PUBLIC_CHAR_SERVER_URL"
    : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CHAR_SERVER_URL")) ? "CHAR_SERVER_URL" : null;
app.Lifetime.ApplicationStarted.Register(() => _ = Addresses.ReportAndWatch(
    config.Port, givenSetting, givenSetting is null ? null : Environment.GetEnvironmentVariable(givenSetting)));
Console.WriteLine(gameVersion.IsOn
    ? $"[char] game version check ON — players need version '{gameVersion.Current ?? "(none published yet)"}' from {config.PatchDir}"
    : "[char] game version check off — PATCH_DIR is not set, so no copy is turned away for being out of date.");
app.Run();
return 0;

// `players` and `creatures` are how busy that process says it is — the two numbers a zone is placed by. An older
// map server that does not send them registers as idle, which is honest: it has not told us otherwise.
record MapServerRegistration(string? id, string? address, int port, int playerPort, string[]? maps, string? build,
                             int players = 0, int creatures = 0, string? startingMap = null,
                             string? arrivalsAddress = null);
record SpendBody(string? ticket);
record MoveBody(string? map, string? accountId, bool isGm);
record PmBody(string? toName, string? fromName, string? text);
record TerminalBody(string? map, string? line, string? @as);
