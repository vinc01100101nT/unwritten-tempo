using System.Text.Json;
using Npgsql;

namespace CharServer;

/// <summary>
/// THE ROWS A CHARACTER KEEPS BESIDE ITS PROGRESS — the hotbar (008_character_hotbar.sql), the auto-combat settings
/// (013_character_auto_combat.sql) and the cape emblem (014_character_emblem.sql). Each is written only when it changes, through a server-to-server door of its own, so
/// the fifteen-second progress save never rewrites it; each is read in the same query that reads the character when it
/// is handed to a map server, and travels beside the progress in the announcement.
///
/// <para><b>One table, not one copy per row.</b> The hotbar's door, read and announcement used to be written out by hand
/// in four places; a second row would have been a second copy of all four, and a fix to one would have missed the other.
/// A new row is one entry in <see cref="All"/> plus its migration — its door, its read and its place in the announcement
/// come from here.</para>
/// </summary>
public static class CharacterRows
{
    /// <summary>One row: the door it is written through (<c>PUT /server/characters/{id}/{Door}</c>), its table and
    /// column, the key it travels under in the door's body and in the announcement, and the check its value must
    /// pass — null when it passes, otherwise the sentence the door answers with.</summary>
    public sealed record Row(string Door, string Table, string Column, string BodyKey, string AnnounceKey,
                             Func<JsonElement, string?> Check);

    // A slot holds a skill id, "item:<id>" or "gear:<copy id>" — never long. The bounds stop a malformed or hostile
    // body, not a real one.
    const int MaxHotbarSlots = 64;
    const int MaxSlotLength = 160;
    const int AutoSkillSlots = 4;
    const int MaxHuntMode = 2;
    // An emblem is a 24 x 24 PNG the map server re-encoded itself: 127 bytes for a simple picture, 2,396 for random
    // noise (measured 2026-10-16). The bound stops a malformed or hostile body.
    const int MaxEmblemPngBytes = 4096;
    const int EmblemSide = 24;

    public static readonly Row[] All =
    {
        new("hotbar", "character_hotbar", "slots", "slots", "hotbar", CheckHotbar),
        new("auto-combat", "character_auto_combat", "settings", "settings", "autoCombat", CheckAutoCombat),
        new("emblem", "character_emblem", "emblem", "emblem", "emblem", CheckEmblem),
    };

    /// <summary>Every row as one more column of a select over <c>character c</c>, in <see cref="All"/>'s order — a
    /// scalar sub-select each, null for a character that never had that row written. Starts with a comma.</summary>
    public static readonly string ReadAsColumns = string.Concat(Array.ConvertAll(All, r =>
        $", (select x.{r.Column}::text from {r.Table} x where x.character_id = c.id)"));

    /// <summary>Read the columns <see cref="ReadAsColumns"/> added, starting at <paramref name="first"/>: one parsed
    /// document per row, null where the character has none. The caller disposes them (<see cref="Dispose"/>).</summary>
    public static JsonDocument?[] Read(NpgsqlDataReader rows, int first)
    {
        var docs = new JsonDocument?[All.Length];
        for (int i = 0; i < All.Length; i++)
            docs[i] = rows.IsDBNull(first + i) ? null : JsonDocument.Parse(rows.GetString(first + i));
        return docs;
    }

    public static void Dispose(JsonDocument?[]? docs)
    {
        if (docs == null) return;
        foreach (var d in docs) d?.Dispose();
    }

    /// <summary>The announcement's view of what <see cref="Read"/> returned: each row's key and its value, null where
    /// the character has none.</summary>
    public static Dictionary<string, JsonElement?> ForAnnouncement(JsonDocument?[]? docs)
    {
        var map = new Dictionary<string, JsonElement?>(All.Length);
        for (int i = 0; i < All.Length; i++)
            map[All[i].AnnounceKey] = docs != null && docs[i] != null ? docs[i]!.RootElement : null;
        return map;
    }

    /// <summary>Every row's door. Guarded exactly as the progress door is: the SERVER secret, and the same hand-over
    /// rule, so a map server the character has already left cannot put an old row back.</summary>
    public static void MapDoors(WebApplication app, MapServers maps, Db db)
    {
        foreach (var row in All)
        {
            var r = row;
            string upsert = $@"insert into {r.Table} (character_id, {r.Column}, updated_at)
                                select id, $2, now() from character
                                 where id = $1 and deleted_at is null and ($3 = 0 or handover = $3)
                                on conflict (character_id) do update set {r.Column} = excluded.{r.Column}, updated_at = now()
                                returning character_id";

            app.MapPut($"/server/characters/{{id}}/{r.Door}", async (HttpContext http, string id, JsonElement body) =>
            {
                if (!maps.IsCurrentServer(http.Request)) return maps.RefuseCaller(http.Request);
                if (!Guid.TryParseExact(id, "D", out var characterId))
                    return Results.Json(new { error = "invalid character id" }, statusCode: 400);
                if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(r.BodyKey, out var value))
                    return Results.Json(new { error = $"send {{ {r.BodyKey}: … }}" }, statusCode: 400);
                if (r.Check(value) is { } wrong) return Results.Json(new { error = wrong }, statusCode: 400);

                // The same numbering the progress door reads: 0 (or absent) is not checked, anything else must be the
                // live one.
                int handover = body.TryGetProperty("handover", out var h) && h.ValueKind == JsonValueKind.Number
                               && h.TryGetInt32(out var hv) ? hv : 0;

                await using var conn = await db.ConnectAsync();
                // Written only when the character exists, is not deleted, and (when a number was given) is on that
                // hand-over — the select finds no row otherwise, so nothing is inserted and nothing is updated.
                await using var cmd = new NpgsqlCommand(upsert, conn);
                cmd.Parameters.Add(new NpgsqlParameter { Value = characterId });
                cmd.Parameters.Add(new NpgsqlParameter { Value = value.GetRawText(), NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb });
                cmd.Parameters.Add(new NpgsqlParameter { Value = handover });

                if (await cmd.ExecuteScalarAsync() is not null) return Results.Ok(new { ok = true });
                if (handover == 0) return Results.Json(new { error = "no such character" }, statusCode: 404);

                // NOT WRITTEN, AND A NUMBER WAS GIVEN: either the character is gone, or this row is from an older
                // hand-over.
                await using var live = new NpgsqlCommand(
                    "select handover from character where id = $1 and deleted_at is null", conn);
                live.Parameters.Add(new NpgsqlParameter { Value = characterId });
                if (await live.ExecuteScalarAsync() is not int current)
                    return Results.Json(new { error = "no such character" }, statusCode: 404);
                Console.WriteLine($"[char] refused a {r.Door} row for character {characterId} from hand-over {handover} — "
                                  + $"they are on hand-over {current} now, so it would have rolled them back.");
                return Results.Json(new { error = $"that {r.Door} row is from an older hand-over", live = current },
                                    statusCode: 409);
            });
        }
    }

    // ---- the checks ---------------------------------------------------------------------------------------------

    static string? CheckHotbar(JsonElement slots)
    {
        if (slots.ValueKind != JsonValueKind.Array) return "send { slots: [ … ] }";
        if (slots.GetArrayLength() > MaxHotbarSlots) return $"a hotbar has at most {MaxHotbarSlots} slots";
        foreach (var slot in slots.EnumerateArray())
            if (slot.ValueKind != JsonValueKind.String || (slot.GetString()?.Length ?? 0) > MaxSlotLength)
                return $"every slot is a string of at most {MaxSlotLength} characters";
        return null;
    }

    // The map server has already clamped every number and pruned every slot before it sends; this only makes sure
    // nothing malformed is ever stored, whoever sent it.
    static string? CheckAutoCombat(JsonElement s)
    {
        if (s.ValueKind != JsonValueKind.Object) return "send { settings: { … } }";
        if (!s.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int version) || version != 1)
            return "settings must say v: 1";
        if (!s.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array
            || slots.GetArrayLength() != AutoSkillSlots)
            return $"settings carry exactly {AutoSkillSlots} auto-skill slots";
        foreach (var slot in slots.EnumerateArray())
            if (slot.ValueKind != JsonValueKind.String || (slot.GetString()?.Length ?? 0) > MaxSlotLength)
                return $"every auto-skill slot is a string of at most {MaxSlotLength} characters";
        foreach (var name in new[] { "lootRateAtMostPercent", "hpPotionBelowPercent", "mpPotionBelowPercent" })
            if (!s.TryGetProperty(name, out var n) || n.ValueKind != JsonValueKind.Number
                || !n.TryGetDouble(out double d) || d < 0 || d > 100)
                return $"{name} is a number from 0 to 100";
        foreach (var name in new[] { "lootGears", "lootConsumables", "lootMaterials", "lootAll" })
            if (!s.TryGetProperty(name, out var b) || (b.ValueKind != JsonValueKind.True && b.ValueKind != JsonValueKind.False))
                return $"{name} is true or false";
        if (!s.TryGetProperty("huntMode", out var m) || m.ValueKind != JsonValueKind.Number || !m.TryGetInt32(out int mode) || mode < 0 || mode > MaxHuntMode)
            return $"huntMode is a whole number from 0 to {MaxHuntMode}";
        return null;
    }

    // The map server checked and re-encoded the picture; this only makes sure what is stored is a 24 x 24 PNG of a sane
    // size (or "" for none), with cape colours of the packed shape, whoever sent it. A check of shape, not a second decoder.
    static string? CheckEmblem(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return "send { emblem: { … } }";
        if (!e.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int version) || version != 1)
            return "emblem must say v: 1";
        if (!e.TryGetProperty("png", out var p) || p.ValueKind != JsonValueKind.String) return "emblem.png is a string";
        // The cape colours, packed by the map server: "" or "RRGGBB,RRGGBB,RRGGBB,0|1".
        if (e.TryGetProperty("colours", out var colours))
        {
            if (colours.ValueKind != JsonValueKind.String) return "emblem.colours is a string";
            string packed = colours.GetString() ?? "";
            if (packed.Length != 0 && !System.Text.RegularExpressions.Regex.IsMatch(packed, "^([0-9A-Fa-f]{6},){3}[01]$"))
                return "emblem.colours is three RRGGBB colours and 0 or 1, separated by commas";
        }
        string text = p.GetString() ?? "";
        if (text.Length == 0) return null;
        byte[] png;
        try { png = Convert.FromBase64String(text); }
        catch (FormatException) { return "emblem.png is base64"; }
        if (png.Length > MaxEmblemPngBytes) return $"an emblem is at most {MaxEmblemPngBytes} bytes";
        // The PNG signature, then the IHDR chunk: width and height are the big-endian words at bytes 16 and 20.
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (png.Length < 24) return "emblem.png is not a PNG";
        for (int i = 0; i < 8; i++) if (png[i] != signature[i]) return "emblem.png is not a PNG";
        int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        if (width != EmblemSide || height != EmblemSide) return $"an emblem is {EmblemSide} x {EmblemSide} pixels";
        return null;
    }
}
