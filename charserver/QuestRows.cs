using System.Text.Json;
using Npgsql;

namespace CharServer;

/// <summary>
/// A CHARACTER'S QUEST ROWS — the changed ones a map server sends beside a progress save, and every one of them read
/// back when the character is handed to a map server (see 009_character_quest.sql).
///
/// <para><b>Why they ride the progress save.</b> A quest step is judged on the bag the map server saw. Written through
/// a door of their own, the bag and the quest could land apart — a crash between the two would leave a step finished
/// on a bag that was never stored, or the reverse. Written in the progress door's own transaction, after the progress
/// itself was taken under the same hand-over check, they land together or not at all.</para>
/// </summary>
public static class QuestRows
{
    /// <summary>The most rows one save may carry. A save carries only the rows that changed since the last one landed,
    /// so ordinary play sends a handful; this bounds a malformed or hostile body, not a busy player.</summary>
    public const int MaxRowsPerSave = 4096;
    public const int MaxIdLength = 120;
    /// <summary>The most a step can count to — far above any count a quest asks for; it bounds a malformed body.</summary>
    public const int MaxProgress = 1_000_000;

    public sealed record Row(string QuestId, string StepId, bool Tracked, bool Finished, int Progress);

    public readonly record struct Verdict(bool Ok, string Reason, List<Row>? Rows);

    /// <summary>Read and check the optional <c>quests</c> array of a progress save. Null rows when there is none.</summary>
    public static Verdict Check(JsonElement body, int handover)
    {
        if (!body.TryGetProperty("quests", out var quests) || quests.ValueKind == JsonValueKind.Null)
            return new Verdict(true, "", null);
        if (quests.ValueKind != JsonValueKind.Array) return new Verdict(false, "quests must be an array", null);
        int count = quests.GetArrayLength();
        if (count == 0) return new Verdict(true, "", null);
        if (count > MaxRowsPerSave) return new Verdict(false, $"a save carries at most {MaxRowsPerSave} quest rows", null);
        // NO UNNUMBERED QUEST ROWS. A save numbered 0 is let through unchecked only for map servers built before
        // hand-overs were numbered — and no such server can send quests, so a 0 here is a bug, never an old server.
        if (handover == 0) return new Verdict(false, "quest rows need a hand-over number", null);

        var rows = new List<Row>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int tracked = 0;
        foreach (var q in quests.EnumerateArray())
        {
            if (q.ValueKind != JsonValueKind.Object) return new Verdict(false, "every quest row is an object", null);
            string? id = Str(q, "q"), step = Str(q, "s");
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength || step is null || step.Length > MaxIdLength)
                return new Verdict(false, $"every quest row has an id and a step of at most {MaxIdLength} characters", null);
            if (!seen.Add(id)) return new Verdict(false, $"quest '{id}' appears twice in one save", null);
            bool t = Bool(q, "t"), f = Bool(q, "f");
            int p = q.TryGetProperty("p", out var pv) && pv.ValueKind == JsonValueKind.Number && pv.TryGetInt32(out var pi) ? pi : 0;
            if (p < 0 || p > MaxProgress) return new Verdict(false, $"a quest's progress is between 0 and {MaxProgress}", null);
            if (t) tracked++;
            rows.Add(new Row(id, step, t, f, p));
        }
        if (tracked > 1) return new Verdict(false, "at most one quest is tracked", null);
        return new Verdict(true, "", rows);
    }

    /// <summary>Write the rows inside the progress door's transaction. A finished quest stays finished whatever a later
    /// row says, and choosing a quest to follow stops following every other one.</summary>
    public static async Task WriteAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid characterId, List<Row> rows)
    {
        var json = JsonSerializer.Serialize(rows.Select(r => new { q = r.QuestId, s = r.StepId, t = r.Tracked, f = r.Finished, p = r.Progress }));
        await using (var upsert = new NpgsqlCommand(
            @"insert into character_quest (character_id, quest_id, step_id, tracked, progress, finished_at, updated_at)
              select $1, x.q, x.s, x.t, coalesce(x.p, 0), case when x.f then now() end, now()
                from jsonb_to_recordset($2) as x(q text, s text, t boolean, f boolean, p integer)
              on conflict (character_id, quest_id) do update
                 set step_id     = excluded.step_id,
                     tracked     = excluded.tracked,
                     progress    = excluded.progress,
                     finished_at = coalesce(character_quest.finished_at, excluded.finished_at),
                     updated_at  = now()", conn, tx))
        {
            upsert.Parameters.Add(new NpgsqlParameter { Value = characterId });
            upsert.Parameters.Add(new NpgsqlParameter { Value = json, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb });
            await upsert.ExecuteNonQueryAsync();
        }

        var followed = rows.FirstOrDefault(r => r.Tracked);
        if (followed is null) return;
        await using var clear = new NpgsqlCommand(
            @"update character_quest set tracked = false, updated_at = now()
               where character_id = $1 and tracked and quest_id <> $2", conn, tx);
        clear.Parameters.Add(new NpgsqlParameter { Value = characterId });
        clear.Parameters.Add(new NpgsqlParameter { Value = followed.QuestId });
        await clear.ExecuteNonQueryAsync();
    }

    /// <summary>The SQL that reads every row of the character named by <c>c.id</c> as one JSON array — open quests with
    /// their step, finished ones too, because a finished quest must never be given again. Used as a sub-select, never
    /// a join, so the progress document is read once however many quests there are.</summary>
    public const string ReadAsJson =
        @"(select coalesce(json_agg(json_build_object('q', quest_id, 's', step_id, 't', tracked, 'p', progress,
                                                      'f', finished_at is not null)), '[]'::json)::text
             from character_quest where character_id = c.id)";

    static string? Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static bool Bool(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
