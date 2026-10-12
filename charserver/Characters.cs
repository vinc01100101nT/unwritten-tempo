using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace CharServer;

/// <summary>
/// Everything a character IS, and the only code that reads or writes one. The map server never touches this
/// database; it asks here. That is the single rule this whole server exists to make true.
/// </summary>
public sealed partial class Characters(Db db, Auth auth, Roster roster, MapServers maps, GameVersion version)
{
    // Names players read out loud and type at each other: letters, digits and underscore only, so a name
    // cannot hide look-alike whitespace or wear punctuation that breaks a chat command.
    [GeneratedRegex(@"^[A-Za-z0-9_]{3,16}$")] private static partial Regex NamePattern();
    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$")] private static partial Regex HexColour();

    /// <summary>Could this be a character's name at all — the one rule, read by every door that is handed a name.</summary>
    internal static bool IsAName(string? name) => name is not null && NamePattern().IsMatch(name);

    const string Columns = "id, name, progress, created_at, updated_at";

    /// <summary>How many recoloured colours a character may carry. One number, so the route that creates a
    /// character and the route that recolours one cannot come to disagree about it.</summary>
    const int MaxColours = 32;

    // Surrounding space is trimmed before the shape is judged, so a colour pasted with a stray space is the
    // same colour rather than a refusal.
    static bool IsColour(string? c) => c is not null && HexColour().IsMatch(c.Trim());

    public void MapOnto(WebApplication app)
    {
        // Every route a PLAYER'S COPY calls is in this group, and every path into the world starts here — so an
        // outdated copy is turned away at the character list, before it can choose anyone.
        var chars = app.MapGroup("").AddEndpointFilter(version.RequireCurrent).AddEndpointFilter(RequireSignedIn);

        chars.MapGet("/character-models", () => Results.Ok(new { models = roster.Models }));
        // CAST, and it matters. ListAsync's only argument is the request itself, which makes it look exactly
        // like the low-level shape that returns nothing — so it would have been bound as one and its answer
        // thrown away, leaving every character list an empty reply with no error anywhere. The others take a
        // further argument and cannot be mistaken for it.
        chars.MapGet("/characters", (Delegate)ListAsync);
        chars.MapPost("/characters", CreateAsync);
        chars.MapPatch("/characters/{id}/appearance", AppearanceAsync);
        chars.MapDelete("/characters/{id}", DeleteAsync);
        chars.MapGet("/characters/{id}/progress", ReadProgressAsync);
        // NO WRITE FOR A PLAYER. A character's progress is written by the map server running the world that
        // character is in, through the server-secret door, and by nothing else. A player's own machine never
        // learns what really happened in a fight, so anything it sent would be older than the truth and would
        // land on top of it. Their look is different and still has its own door above: it is not progress, and
        // it is chosen on a screen the server never sees.

        // THE DOOR INTO THE WORLD. A player asks to enter with a character; this hands their whole character to
        // the map server that holds the map they are entering, and gives them back only an address and a
        // one-time ticket. They never carry their character; they carry a receipt for it.
        chars.MapPost("/characters/{id}/enter", EnterAsync);
    }

    // ---- who is asking ----------------------------------------------------------------------------------
    //
    // Checked ONCE here for every route below rather than at the top of each, because a route that forgot it
    // would be a route anybody could call, and nothing about it would look wrong.

    const string AccountKey = "accountId";

    async ValueTask<object?> RequireSignedIn(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var accountId = auth.AccountIdFrom(ctx.HttpContext.Request.Headers.Authorization);
        // TURNED INTO THE THING IT WILL BE USED AS, HERE, ONCE. Every route needs it as an id and each used to
        // convert it itself — six chances to be handed a token whose account is not an id at all, and the
        // conversion does not return a failure, it throws. That reached the caller as a server fault carrying a
        // stack trace, where the honest answer is the same "sign in first" as any other unusable token.
        if (accountId is null || !Guid.TryParse(accountId, out var account))
            return Results.Json(new { error = "sign in first" }, statusCode: 401);
        ctx.HttpContext.Items[AccountKey] = account;
        return await next(ctx);
    }

    static Guid AccountOf(HttpContext http) => (Guid)http.Items[AccountKey]!;

    // THE PLAIN DASHED FORM ONLY, because that is the one shape the login service accepts and the two must not
    // disagree about what counts as an id. The permissive parse also takes braces, brackets and no dashes at
    // all, so the same character had several spellings here and exactly one there.
    static bool IsId(string raw, out Guid id) => Guid.TryParseExact(raw, "D", out id);

    // ---- the routes -------------------------------------------------------------------------------------

    async Task<IResult> ListAsync(HttpContext http)
    {
        await using var conn = await db.ConnectAsync();
        await using var cmd = new NpgsqlCommand(
            $"select {Columns} from character where account_id = $1 and deleted_at is null order by created_at /* index: character_by_account */", conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = AccountOf(http) });

        var list = new List<object>();
        await using var rows = await cmd.ExecuteReaderAsync();
        while (await rows.ReadAsync()) list.Add(Summarise(rows));
        return Results.Ok(new { characters = list });
    }

    async Task<IResult> CreateAsync(HttpContext http, CreateBody? body)
    {
        if (body is null) return Bad("send a character");
        var name = body.name?.Trim() ?? "";
        if (!IsAName(name)) return Bad("3-16 letters, digits or underscore");

        var sources = body.dyeSources ?? [];
        var targets = body.dyeTargets ?? [];
        if (sources.Length != targets.Length) return Bad("dyeSources and dyeTargets must have the same number of colours");
        if (sources.Length > MaxColours || targets.Length > MaxColours) return Bad("too many colours");
        foreach (var c in sources.Concat(targets))
            if (!IsColour(c)) return Bad("colours must look like #RRGGBB");
        sources = [.. sources.Select(c => c.Trim())];
        targets = [.. targets.Select(c => c.Trim())];

        var model = roster.Resolve(body.modelName);
        if (model is null) return Bad($"no character model called '{body.modelName}'");

        // THE HAIR, always explicit for a new character: a hairstyle that body can wear (or "none"), and its colour.
        // Empty is kept for characters made before hairstyles, so a new one may never be created that way.
        var hairStyle = body.hairStyle?.Trim() ?? "";
        if (hairStyle.Length == 0) return Bad("choose a hairstyle");
        if (!roster.AllowsHair(model, hairStyle)) return Bad($"'{model}' cannot wear the hairstyle '{hairStyle}'");
        var hairColour = body.hairColour?.Trim() ?? "";
        if (hairStyle == Roster.NoHair) hairColour = "";
        else if (!IsColour(hairColour)) return Bad("the hair colour must look like #RRGGBB");

        // The six stats chosen on the creation hexagon. Only their SHAPE is checked here — six whole numbers, 1 to 9 —
        // so nothing absurd is stored. Whether they are a spread the hexagon can make is the game's own rule
        // (UnitStatMath.IsCreationSpread), applied by the map server on the character's first entry; it is not
        // written twice.
        var primary = body.primary ?? [];
        if (primary.Length != 6 || primary.Any(v => v < 1 || v > 9)) return Bad("send six stats, each 1 to 9");

        // The look and the stats go straight into the save document — the character has no other home for them.
        // It carries no version, which is exactly what marks this character as never played.
        var born = JsonSerializer.Serialize(new
        {
            modelName = model, dyeSources = sources, dyeTargets = targets, hairStyle, hairColour, primary,
        });

        try
        {
            await using var conn = await db.ConnectAsync();
            await using var cmd = new NpgsqlCommand(
                $"insert into character (account_id, name, progress) values ($1, $2, $3) returning {Columns}", conn);
            cmd.Parameters.Add(new NpgsqlParameter { Value = AccountOf(http) });
            cmd.Parameters.Add(new NpgsqlParameter { Value = name });
            cmd.Parameters.Add(new NpgsqlParameter { Value = born, NpgsqlDbType = NpgsqlDbType.Jsonb });

            await using var rows = await cmd.ExecuteReaderAsync();
            if (!await rows.ReadAsync()) return Results.Json(new { error = "could not create" }, statusCode: 500);
            return Results.Json(Summarise(rows), statusCode: 201);
        }
        catch (Exception e) when (Db.IsUniqueViolation(e))
        {
            return Results.Json(new { error = "that name is taken" }, statusCode: 409);
        }
    }

    // Appearance has its own route because it changes at a completely different moment than progress does —
    // the instant a player recolours, rather than on the next autosave. It writes into the same document.
    async Task<IResult> AppearanceAsync(HttpContext http, string id, AppearanceBody? body)
    {
        if (!IsId(id, out var characterId)) return Bad("invalid character id");
        if (body is null) return Bad("send an appearance");

        bool sendsColours = body.dyeSources is not null || body.dyeTargets is not null;
        if (sendsColours && (body.dyeSources is null || body.dyeTargets is null))
            return Bad("send both dyeSources and dyeTargets, or neither");
        if (sendsColours && body.dyeSources!.Length != body.dyeTargets!.Length)
            return Bad("dyeSources and dyeTargets must have the same number of colours");
        // THE SAME CEILING CREATION HAS. Without it this route took as many colours as somebody cared to send,
        // and they land in the save document permanently — every later save that omits the look copies them
        // straight back in, and every character list then carries them.
        if (sendsColours && (body.dyeSources!.Length > MaxColours || body.dyeTargets!.Length > MaxColours))
            return Bad("too many colours");
        if (sendsColours)
            foreach (var c in body.dyeSources!.Concat(body.dyeTargets!))
                if (!IsColour(c)) return Bad("colours must look like #RRGGBB");

        string? model = null;
        if (body.modelName is not null)
        {
            model = roster.Resolve(body.modelName);
            if (model is null) return Bad($"no character model called '{body.modelName}'");
        }

        // Merged into the save document, so whatever the caller left out keeps the value it already had.
        var patch = new Dictionary<string, object>();
        if (model is not null) patch["modelName"] = model;
        if (sendsColours)
        {
            patch["dyeSources"] = body.dyeSources!.Select(c => c.Trim()).ToArray();
            patch["dyeTargets"] = body.dyeTargets!.Select(c => c.Trim()).ToArray();
        }

        await using var conn = await db.ConnectAsync();
        await using var cmd = new NpgsqlCommand(
            $@"update character set progress = progress || $3::jsonb, updated_at = now()
                where id = $1 and account_id = $2 and deleted_at is null returning {Columns}", conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = characterId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = AccountOf(http) });
        cmd.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(patch), NpgsqlDbType = NpgsqlDbType.Jsonb });

        await using var rows = await cmd.ExecuteReaderAsync();
        if (!await rows.ReadAsync()) return NoSuchCharacter();
        return Results.Ok(Summarise(rows));
    }

    async Task<IResult> DeleteAsync(HttpContext http, string id)
    {
        if (!IsId(id, out var characterId)) return Bad("invalid character id");

        // Soft delete: the row stays, so a mistaken deletion is recoverable and anything that later references
        // this character still has something to point at. The partial unique index treats a deleted row as
        // absent, so the name comes free.
        await using var conn = await db.ConnectAsync();
        await using var cmd = new NpgsqlCommand(
            @"update character set deleted_at = now(), updated_at = now()
               where id = $1 and account_id = $2 and deleted_at is null returning id", conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = characterId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = AccountOf(http) });

        var gone = await cmd.ExecuteScalarAsync();
        return gone is null ? NoSuchCharacter() : Results.NoContent();
    }

    async Task<IResult> ReadProgressAsync(HttpContext http, string id)
    {
        if (!IsId(id, out var characterId)) return Bad("invalid character id");

        // The GM mark rides along here, joined off the owning account, because both readers already ask for
        // exactly this — the player's own game, to decide which commands it will even attempt, and the map
        // server, to decide which it will actually carry out. Answering it in a second place as well would be
        // two owners for one fact.
        await using var conn = await db.ConnectAsync();
        // AND ANYBODY ALREADY PLAYING THIS CHARACTER LETS GO OF THEM FIRST — writing them down and waiting for
        // that write, BEFORE the row below is read. Read first and whoever is arriving picks up the last ordinary
        // save, up to fifteen seconds old, and everything the machine being taken over did since is lost. This is
        // the same order a door already uses: write, wait, then hand over.
        await maps.LetGoOfEverywhereAsync(characterId.ToString(), http.RequestAborted);

        await using var cmd = new NpgsqlCommand(
            @"select c.id, c.name, c.progress, c.created_at, c.updated_at, a.is_gm
                from character c join account a on a.id = c.account_id
               where c.id = $1 and c.account_id = $2 and c.deleted_at is null", conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = characterId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = AccountOf(http) });

        await using var rows = await cmd.ExecuteReaderAsync();
        if (!await rows.ReadAsync()) return NoSuchCharacter();

        using var progress = JsonDocument.Parse(rows.GetString(2));
        var look = Progress.AppearanceOf(progress.RootElement);
        return Results.Ok(new
        {
            id = rows.GetGuid(0),
            name = rows.GetString(1),
            modelName = look.ModelName,
            dyeSources = look.DyeSources,
            dyeTargets = look.DyeTargets,
            hairStyle = look.HairStyle,
            hairColour = look.HairColour,
            level = Progress.LevelOf(progress.RootElement),
            createdAt = rows.GetDateTime(3),
            updatedAt = rows.GetDateTime(4),
            progress = progress.RootElement.Clone(),
            isGm = rows.GetBoolean(5),
        });
    }

    // ---- entering the world -----------------------------------------------------------------------------

    async Task<IResult> EnterAsync(HttpContext http, string id, EnterBody? body)
    {
        if (!IsId(id, out var characterId)) return Bad("invalid character id");

        // NOBODY IN WHILE THIS SERVER IS STILL LEARNING WHERE EVERYONE IS — see StartupGrace.
        int wait = StartupGrace.SecondsLeft;
        if (wait > 0)
            return Results.Json(new { error = $"the world is starting up — try again in {wait} seconds" },
                                statusCode: 503);

        await using var conn = await db.ConnectAsync();

        // IS THIS THE SIGN-IN THAT IS STILL THE LIVE ONE. Signing in a second time takes over: the login service
        // writes a fresh marker on the account, and the pass this request is carrying holds the marker from the
        // sign-in that made it. An older one belongs to a machine that has since been taken over, and letting it
        // into the world is what puts one account into two places — which is where the same item ends up saved
        // twice, because two world servers write that character down on their own beats.
        //
        // Asked HERE and nowhere else: this is the one door into the world, so it is the one moment it decides
        // anything. Every other request stays as cheap as it was.
        //
        // NOTE: this reads a column on a table the LOGIN service owns. It is a read, at one moment, of the one
        // fact only that service can know — but it is a line across the ownership split, and if that split is
        // ever enforced this becomes a question asked of that service instead.
        var carrying = auth.SessionIdFrom(http.Request.Headers.Authorization);
        await using (var session = new NpgsqlCommand(
            "select session_id from account where id = $1", conn))
        {
            session.Parameters.Add(new NpgsqlParameter { Value = AccountOf(http) });
            var current = await session.ExecuteScalarAsync();
            // A null on the account means nobody has signed in since sign-ins started being marked, so there is
            // nothing to be older than and nothing to refuse.
            if (current is Guid live && !string.Equals(carrying, live.ToString(), StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { error = "this account has been signed in somewhere else" }, statusCode: 409);
        }

        // AND ANYBODY ALREADY PLAYING THIS CHARACTER LETS GO OF THEM FIRST — writing them down and waiting for
        // that write, BEFORE the row below is read. Read first and whoever is arriving picks up the last ordinary
        // save, up to fifteen seconds old, and everything the machine being taken over did since is lost. This is
        // the same order a door already uses: write, wait, then hand over.
        await maps.LetGoOfEverywhereAsync(characterId.ToString(), http.RequestAborted);

        // A NEW HAND-OVER, BEFORE THE READ: from here, only saves carrying this number are taken (see Handover). Raised
        // after the read, a late save from a server that did not let go in time landed between the two — the progress
        // healed on the next save, but quest rows written as changes would have stayed from the old server's timeline.
        // Only for this account's own character: anybody else's number is not this request's to move.
        int handover;
        await using (var own = new NpgsqlCommand(
            @"update character set handover = handover + 1
               where id = $1 and account_id = $2 and deleted_at is null returning handover", conn))
        {
            own.Parameters.Add(new NpgsqlParameter { Value = characterId });
            own.Parameters.Add(new NpgsqlParameter { Value = AccountOf(http) });
            if (await own.ExecuteScalarAsync() is not int raised) return NoSuchCharacter();
            handover = raised;
        }

        // The character's own rows are read in the same query (see CharacterRows) — the hotbar and the auto-combat
        // settings, each null for a character that never had it written. The quest rows come as one sub-selected JSON
        // array (see QuestRows), never a join, so the progress is read once.
        await using var cmd = new NpgsqlCommand(
            @"select c.id, c.name, c.progress, c.created_at, c.updated_at, a.is_gm, " + QuestRows.ReadAsJson
            + CharacterRows.ReadAsColumns + @"
                from character c join account a on a.id = c.account_id
               where c.id = $1 and c.account_id = $2 and c.deleted_at is null", conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = characterId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = AccountOf(http) });

        await using var rows = await cmd.ExecuteReaderAsync();
        if (!await rows.ReadAsync()) return NoSuchCharacter();

        using var progress = JsonDocument.Parse(rows.GetString(2));
        using var quests = JsonDocument.Parse(rows.GetString(6));
        var ownRows = CharacterRows.Read(rows, 7);
        try
        {
        string name = rows.GetString(1);
        bool isGm = rows.GetBoolean(5);
        var sent = progress.RootElement;

        // WHERE THIS PERSON IS GOING. Their save says which map they logged out in; a caller may name one
        // instead, which is what walking through a door into another map server's territory will use.
        var map = body?.map;
        bool fromSave = false;
        if (string.IsNullOrWhiteSpace(map)
            && progress.RootElement.TryGetProperty("sceneName", out var scene)
            && scene.ValueKind == JsonValueKind.String)
        {
            map = scene.GetString();
            fromSave = true;
        }

        // A CHARACTER NOBODY HAS PLAYED YET names no map, so they begin at the starting map the map servers name,
        // brought up below like any other. Sent to a process with no map named, they were filed under whatever
        // map that process happened to be holding, while their own machine loaded the starting map.
        if (string.IsNullOrWhiteSpace(map))
        {
            var starting = maps.QuietestAlive()?.StartingMap;
            if (!string.IsNullOrEmpty(starting)) map = starting;
            else Console.Error.WriteLine($"[char] no map server says where a new character begins, so '{name}' is sent "
                                        + "to the quietest one as it is — rebuild the map server.");
        }

        // WHICH PROCESS HOLDS THAT MAP — and if nobody does, one is asked to bring it up and we wait for it.
        // No process is given a list of zones when it starts, so "nobody is holding it" is the ordinary state of
        // every map nobody is standing in, not an error. It only becomes one when there is no process at all, or
        // the map never comes up.
        MapServers.Registration? to;
        if (map is not null)
        {
            var held = await maps.EnsureHeldAsync(map, http.RequestAborted);

            // A SAVED MAP THE GAME NO LONGER HAS. The map server said its build has no map by that name and named
            // its starting map instead, so that is where they go — and their saved map and position are forgotten
            // in the row, because the game reads the row again once they arrive and would otherwise walk them
            // straight back to the map that is gone. With no saved map they stand where anyone new arrives.
            if (held.NoSuchMapInstead is { } instead && fromSave && instead != map)
            {
                await rows.DisposeAsync();
                await using var forget = new NpgsqlCommand(
                    @"update character set progress = progress - 'sceneName' - 'posX' - 'posY' - 'posZ',
                                           updated_at = now()
                       where id = $1 and deleted_at is null returning progress::text", conn);
                forget.Parameters.Add(new NpgsqlParameter { Value = characterId });
                if (await forget.ExecuteScalarAsync() is not string forgottenText) return NoSuchCharacter();
                using (var forgotten = JsonDocument.Parse(forgottenText)) sent = forgotten.RootElement.Clone();

                Console.WriteLine($"[char] '{name}' was saved in '{map}', which is not in the game any more — "
                                  + $"sending them to '{instead}' and forgetting where they stood.");
                map = instead;
                held = await maps.EnsureHeldAsync(map, http.RequestAborted);
            }

            to = held.Server;
            if (to is null)
                return Results.Json(new { error = "the world could not bring that map up just now" }, statusCode: 503);
        }
        else
        {
            // A character nobody has played yet names no map, so there is nothing to place. They go to the
            // quietest process, which brings up whatever it starts with.
            to = maps.QuietestAlive();
            if (to is null)
                return Results.Json(new { error = "no map server is running" }, statusCode: 503);
        }


        var ticket = maps.MintTicket(AccountOf(http).ToString(), characterId.ToString());

        await rows.DisposeAsync();

        // ANNOUNCED FIRST, AND ONLY THEN IS THE PLAYER TOLD WHERE TO GO. If the map server will not take them,
        // they are told that here rather than being sent to knock on a door that is going to refuse them.
        // THE MAP IS NAMED IN THE ANNOUNCEMENT: the one decided above, or — for a character nobody has played — the
        // starting map of the process they go to, which is what that process brings up for them.
        bool taken = await maps.AnnounceAsync(to, ticket, name, map ?? to.StartingMap ?? "", sent,
                                              CharacterRows.ForAnnouncement(ownRows), quests.RootElement, isGm, handover,
                                              signingIn: true, http.RequestAborted);
        if (!taken)
            return Results.Json(new { error = "the world would not take you just now" }, statusCode: 503);

                // THE PORT PLAYERS USE, not the one this server pushed their character to. Those are two different
        // doors on the same machine, and handing over the wrong one sends somebody to knock where nothing is
        // listening for them.
        return Results.Ok(new { host = to.Address, port = to.PlayerPort, ticket = ticket.Value, map });
        }
        finally { CharacterRows.Dispose(ownRows); }
    }

    public sealed record EnterBody(string? map);

    // ---- shared shapes ----------------------------------------------------------------------------------

    // What a character-select screen needs. The level is READ from the save document rather than stored beside
    // it — one owner for the number, so the list can never disagree with the character it describes.
    static object Summarise(NpgsqlDataReader rows)
    {
        using var progress = JsonDocument.Parse(rows.GetString(2));
        var look = Progress.AppearanceOf(progress.RootElement);
        return new
        {
            id = rows.GetGuid(0),
            name = rows.GetString(1),
            modelName = look.ModelName,
            dyeSources = look.DyeSources,
            dyeTargets = look.DyeTargets,
            hairStyle = look.HairStyle,
            hairColour = look.HairColour,
            level = Progress.LevelOf(progress.RootElement),
            createdAt = rows.GetDateTime(3),
            updatedAt = rows.GetDateTime(4),
        };
    }

    static IResult Bad(string why) => Results.Json(new { error = why }, statusCode: 400);
    static IResult NoSuchCharacter() => Results.Json(new { error = "no such character" }, statusCode: 404);

    public sealed record CreateBody(string? name, string? modelName, string[]? dyeSources, string[]? dyeTargets,
                                    string? hairStyle, string? hairColour, int[]? primary);
    public sealed record AppearanceBody(string? modelName, string[]? dyeSources, string[]? dyeTargets);
}
