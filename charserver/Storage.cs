using System.Text.Json;
using Npgsql;

namespace CharServer;

/// <summary>
/// AN ACCOUNT'S STORAGE — what it keeps with the Keeper, shared by all its characters (see 012_account_storage.sql).
///
/// <para><b>Why a move rides the progress save.</b> Storing takes a thing out of a bag; taking puts one in. Written
/// through a door of their own, the bag and the storage could land apart — a crash between the two would leave the item
/// in both places, or in neither. Applied inside the progress door's transaction, after the progress itself was taken
/// under the same hand-over check, they land together or not at all.</para>
///
/// <para>The account is read from the character row, never from the body: a map server names a character, and the
/// character says whose storage it may touch.</para>
/// </summary>
public static class Storage
{
    public const int MaxIdLength = 120;
    public const int MaxCopyIdLength = 64;
    public const int MaxTextLength = 2000;
    /// <summary>The most of one thing a single move may carry — far above any stack; it bounds a malformed body.</summary>
    public const int MaxCount = 1_000_000;
    /// <summary>The widest a storage limit sent by a map server may be — it bounds a malformed body.</summary>
    public const int MaxLimit = 100_000;

    public sealed record Move(Guid MoveId, bool Store, string ItemId, int Count, int Refine, string Cards, string Roll,
                              string CopyId, int Limit);

    public readonly record struct Verdict(bool Ok, string Reason, Move? Move);

    /// <summary>Read and check the optional <c>storage</c> object of a progress save. No move when it is absent, or
    /// carries no move id — the map server always writes the object, empty when nothing is moving.</summary>
    public static Verdict Check(JsonElement body)
    {
        if (!body.TryGetProperty("storage", out var s) || s.ValueKind == JsonValueKind.Null) return new Verdict(true, "", null);
        if (s.ValueKind != JsonValueKind.Object) return new Verdict(false, "storage must be an object", null);
        string moveId = Str(s, "moveId") ?? "";
        if (moveId.Length == 0) return new Verdict(true, "", null);
        if (!Guid.TryParseExact(moveId, "D", out var id)) return new Verdict(false, "a storage move id is a uuid", null);
        string kind = Str(s, "kind") ?? "";
        if (kind != "store" && kind != "take") return new Verdict(false, "a storage move is 'store' or 'take'", null);
        string item = Str(s, "itemId") ?? "", cards = Str(s, "cards") ?? "", roll = Str(s, "roll") ?? "", copy = Str(s, "copyId") ?? "";
        if (item.Length == 0 || item.Length > MaxIdLength) return new Verdict(false, $"a storage move names an item of at most {MaxIdLength} characters", null);
        if (copy.Length > MaxCopyIdLength) return new Verdict(false, $"a copy id is at most {MaxCopyIdLength} characters", null);
        if (cards.Length > MaxTextLength || roll.Length > MaxTextLength) return new Verdict(false, "a stored piece's text is too long", null);
        int count = Int(s, "count"), refine = Int(s, "refine"), limit = Int(s, "limit");
        if (count < 1 || count > MaxCount) return new Verdict(false, $"a storage move carries between 1 and {MaxCount}", null);
        if (copy.Length > 0 && count != 1) return new Verdict(false, "a piece of gear is moved one at a time", null);
        if (limit < 1 || limit > MaxLimit) return new Verdict(false, $"a storage limit is between 1 and {MaxLimit}", null);
        if (refine < 0) return new Verdict(false, "a stored piece's folds are never below nothing", null);
        return new Verdict(true, "", new Move(id, kind == "store", item, count, refine, cards, roll, copy, limit));
    }

    /// <summary>Apply <paramref name="move"/> inside the progress door's transaction, after the progress was written.
    /// Null when it was applied (or had already been — the same move id twice applies once); otherwise the reason it was
    /// refused, and the caller rolls the whole save back.</summary>
    public static async Task<string?> ApplyAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid characterId, Move move,
                                                 JsonElement progress)
    {
        Guid account;
        Guid? lastMove;
        await using (var who = new NpgsqlCommand(
            "select account_id, storage_move from character where id = $1 and deleted_at is null", conn, tx))
        {
            who.Parameters.Add(new NpgsqlParameter { Value = characterId });
            await using var r = await who.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return "no such character";
            account = r.GetGuid(0);
            lastMove = r.IsDBNull(1) ? null : r.GetGuid(1);
        }

        // ONE MOVE AT A TIME PER ACCOUNT: the head row is made if missing, then locked until this transaction ends.
        await using (var make = new NpgsqlCommand(
            "insert into account_storage_head (account_id) values ($1) on conflict (account_id) do nothing", conn, tx))
        {
            make.Parameters.Add(new NpgsqlParameter { Value = account });
            await make.ExecuteNonQueryAsync();
        }
        await using (var head = new NpgsqlCommand(
            "select revision from account_storage_head where account_id = $1 for update", conn, tx))
        {
            head.Parameters.Add(new NpgsqlParameter { Value = account });
            await head.ExecuteScalarAsync();
        }

        // ALREADY APPLIED: this save is a resend of one that carried the same move. The progress it carries already
        // reflects it, so nothing more happens here.
        if (lastMove == move.MoveId) return null;

        string? why = move.Store ? await StoreAsync(conn, tx, account, move, progress)
                                 : await TakeAsync(conn, tx, account, move, progress);
        if (why != null) return why;

        await using (var bump = new NpgsqlCommand(
            "update account_storage_head set revision = revision + 1 where account_id = $1", conn, tx))
        {
            bump.Parameters.Add(new NpgsqlParameter { Value = account });
            await bump.ExecuteNonQueryAsync();
        }
        await using (var mark = new NpgsqlCommand("update character set storage_move = $1 where id = $2", conn, tx))
        {
            mark.Parameters.Add(new NpgsqlParameter { Value = move.MoveId });
            mark.Parameters.Add(new NpgsqlParameter { Value = characterId });
            await mark.ExecuteNonQueryAsync();
        }
        return null;
    }

    static async Task<string?> StoreAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid account, Move move, JsonElement progress)
    {
        bool gear = move.CopyId.Length > 0;
        if (gear)
        {
            // THE PIECE LEFT THE BAG IN THIS VERY SAVE, so it must not still be in it, nor worn — and nobody may have it
            // stored already.
            if (Holds(progress, "bagCopyIds", move.CopyId) || Holds(progress, "equipCopyIds", move.CopyId))
                return "that piece is still carried";
            await using var already = new NpgsqlCommand("select 1 from account_storage where copy_id = $1", conn, tx);
            already.Parameters.Add(new NpgsqlParameter { Value = move.CopyId });
            if (await already.ExecuteScalarAsync() is not null) return "that piece is already stored";
        }

        bool newRow = true;
        if (!gear)
        {
            await using var stack = new NpgsqlCommand(
                "select 1 from account_storage where account_id = $1 and item_id = $2 and copy_id = ''", conn, tx);
            stack.Parameters.Add(new NpgsqlParameter { Value = account });
            stack.Parameters.Add(new NpgsqlParameter { Value = move.ItemId });
            newRow = await stack.ExecuteScalarAsync() is null;
        }
        if (newRow)
        {
            await using var count = new NpgsqlCommand("select count(*) from account_storage where account_id = $1", conn, tx);
            count.Parameters.Add(new NpgsqlParameter { Value = account });
            long rows = (long)(await count.ExecuteScalarAsync() ?? 0L);
            if (rows >= move.Limit) return "storage is full";
        }

        await using var put = new NpgsqlCommand(gear
            ? @"insert into account_storage (account_id, item_id, count, refine, cards, roll, copy_id)
                values ($1, $2, 1, $3, $4, $5, $6)"
            : @"insert into account_storage (account_id, item_id, count) values ($1, $2, $3)
                on conflict (account_id, item_id) where copy_id = '' do update set count = account_storage.count + excluded.count",
            conn, tx);
        put.Parameters.Add(new NpgsqlParameter { Value = account });
        put.Parameters.Add(new NpgsqlParameter { Value = move.ItemId });
        if (gear)
        {
            put.Parameters.Add(new NpgsqlParameter { Value = move.Refine });
            put.Parameters.Add(new NpgsqlParameter { Value = move.Cards });
            put.Parameters.Add(new NpgsqlParameter { Value = move.Roll });
            put.Parameters.Add(new NpgsqlParameter { Value = move.CopyId });
        }
        else put.Parameters.Add(new NpgsqlParameter { Value = move.Count });
        await put.ExecuteNonQueryAsync();
        return null;
    }

    static async Task<string?> TakeAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid account, Move move, JsonElement progress)
    {
        bool gear = move.CopyId.Length > 0;
        // THE PIECE ARRIVED IN THE BAG IN THIS VERY SAVE, so the bag that is being written must carry it.
        if (gear && !Holds(progress, "bagCopyIds", move.CopyId)) return "that piece is not in the bag being written";

        // THE WHOLE ROW GOES when everything in it is taken — a piece of gear always, a stack when the count taken is
        // exactly what it holds. A row is never written with nothing in it: the table refuses a count of 0.
        await using (var all = new NpgsqlCommand(gear
            ? "delete from account_storage where account_id = $1 and item_id = $2 and copy_id = $3 returning 0"
            : "delete from account_storage where account_id = $1 and item_id = $2 and copy_id = '' and count = $3 returning 0",
            conn, tx))
        {
            all.Parameters.Add(new NpgsqlParameter { Value = account });
            all.Parameters.Add(new NpgsqlParameter { Value = move.ItemId });
            if (gear) all.Parameters.Add(new NpgsqlParameter { Value = move.CopyId });
            else all.Parameters.Add(new NpgsqlParameter { Value = move.Count });
            if (await all.ExecuteScalarAsync() is not null) return null;
        }
        if (gear) return "that is no longer in storage";

        // SOME OF A STACK: only from one that holds more than is being taken.
        await using var some = new NpgsqlCommand(
            @"update account_storage set count = count - $3
               where account_id = $1 and item_id = $2 and copy_id = '' and count > $3 returning count", conn, tx);
        some.Parameters.Add(new NpgsqlParameter { Value = account });
        some.Parameters.Add(new NpgsqlParameter { Value = move.ItemId });
        some.Parameters.Add(new NpgsqlParameter { Value = move.Count });
        return await some.ExecuteScalarAsync() is null ? "that is no longer in storage" : null;
    }

    /// <summary>The read door: everything the character's ACCOUNT keeps, oldest first, with the revision the list is at.
    /// Reached with the SERVER secret, scoped by the character id alone like every map server door.</summary>
    public static void MapOnto(WebApplication app, Db db, MapServers maps)
    {
        app.MapGet("/server/characters/{id}/storage", async (HttpContext http, string id) =>
        {
            if (!maps.IsAServer(http.Request)) return Results.Json(new { error = "not a server" }, statusCode: 401);
            if (!Guid.TryParseExact(id, "D", out var characterId))
                return Results.Json(new { error = "invalid character id" }, statusCode: 400);

            await using var conn = await db.ConnectAsync();
            await using var read = new NpgsqlCommand(
                @"select coalesce((select h.revision from account_storage_head h where h.account_id = c.account_id), 0),
                         (select coalesce(json_agg(json_build_object('itemId', s.item_id, 'count', s.count, 'refine', s.refine,
                                                                     'cards', s.cards, 'roll', s.roll, 'copyId', s.copy_id)
                                                   order by s.seq /* index: account_storage_by_account */), '[]'::json)::text
                            from account_storage s where s.account_id = c.account_id)
                    from character c where c.id = $1 and c.deleted_at is null", conn);
            read.Parameters.Add(new NpgsqlParameter { Value = characterId });
            await using var r = await read.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return Results.Json(new { error = "no such character" }, statusCode: 404);
            long revision = r.GetInt64(0);
            using var rows = JsonDocument.Parse(r.GetString(1));
            return Results.Json(new { revision, rows = rows.RootElement.Clone() });
        });
    }

    // Is `value` one of the strings in the document's array `name`?
    static bool Holds(JsonElement doc, string name, string value)
    {
        if (doc.ValueKind != JsonValueKind.Object || !doc.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var e in arr.EnumerateArray())
            if (e.ValueKind == JsonValueKind.String && e.GetString() == value) return true;
        return false;
    }

    static string? Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static int Int(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
}
