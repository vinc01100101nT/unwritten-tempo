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
    /// <summary>The most pieces one tutorial run may list — far above any tutorial's; it bounds a malformed body.</summary>
    public const int MaxPieces = 64;
    /// <summary>The most free folds or free purchases one piece may carry — far above any tutorial's.</summary>
    public const int MaxFree = 1000;
    public const int MaxCopyIdLength = 64;

    /// <summary>One quest row. <c>Pieces</c> is the run's piece list as JSON (see 011_character_quest_runs.sql).</summary>
    public sealed record Row(string QuestId, string StepId, bool Tracked, bool Finished, int Progress, bool Replaying,
                             string Resume, string Pieces);

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
            bool t = Bool(q, "t"), f = Bool(q, "f"), r = Bool(q, "r");
            int p = q.TryGetProperty("p", out var pv) && pv.ValueKind == JsonValueKind.Number && pv.TryGetInt32(out var pi) ? pi : 0;
            if (p < 0 || p > MaxProgress) return new Verdict(false, $"a quest's progress is between 0 and {MaxProgress}", null);
            // ONLY A FINISHED QUEST IS REPLAYED — a replay never makes a quest unfinished, so its reward is never owed again.
            if (r && !f) return new Verdict(false, $"quest '{id}' is replaying but not finished", null);
            string resume = Str(q, "u") ?? "";
            if (resume.Length > MaxIdLength) return new Verdict(false, $"a quest to resume is at most {MaxIdLength} characters", null);
            var pieces = Pieces(q, out string? why);
            if (pieces is null) return new Verdict(false, $"quest '{id}': {why}", null);
            if (t) tracked++;
            rows.Add(new Row(id, step, t, f, p, r, resume, pieces));
        }
        if (tracked > 1) return new Verdict(false, "at most one quest is tracked", null);
        return new Verdict(true, "", rows);
    }

    // The run's piece list, checked field by field and written back out in a fixed shape — never the body's own text, so
    // nothing the body adds beside the known fields reaches the table. Null with the reason when it is malformed.
    static string? Pieces(JsonElement q, out string? why)
    {
        why = null;
        if (!q.TryGetProperty("a", out var a) || a.ValueKind == JsonValueKind.Null) return "[]";
        if (a.ValueKind != JsonValueKind.Array) { why = "its pieces must be an array"; return null; }
        if (a.GetArrayLength() > MaxPieces) { why = $"a run lists at most {MaxPieces} pieces"; return null; }
        var clean = new List<object>(a.GetArrayLength());
        foreach (var e in a.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) { why = "every piece is an object"; return null; }
            string? item = Str(e, "i"), copy = Str(e, "c") ?? "";
            if (string.IsNullOrEmpty(item) || item.Length > MaxIdLength || copy.Length > MaxCopyIdLength)
            { why = $"every piece names an item, and a copy of at most {MaxCopyIdLength} characters"; return null; }
            int g = Int(e, "g"), n = Int(e, "n"), b = Int(e, "b");
            if (g < 0 || g > 1 || n < 0 || n > MaxFree || b < 0 || b > MaxFree)
            { why = $"a piece's free forges are 0 or 1, and its free folds and purchases at most {MaxFree}"; return null; }
            clean.Add(new { i = item, c = copy, g, n, b });
        }
        return JsonSerializer.Serialize(clean);
    }

    /// <summary>Write the rows inside the progress door's transaction. A finished quest stays finished whatever a later
    /// row says, and choosing a quest to follow stops following every other one.</summary>
    public static async Task WriteAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid characterId, List<Row> rows)
    {
        var json = JsonSerializer.Serialize(rows.Select(r => new
        {
            q = r.QuestId, s = r.StepId, t = r.Tracked, f = r.Finished, p = r.Progress, r = r.Replaying, u = r.Resume,
            a = JsonSerializer.Deserialize<JsonElement>(r.Pieces),
        }));
        // A REPLAY STANDS ONLY ON A FINISHED ROW: `replaying` is written only beside a finished quest, so it can never
        // make one unfinished.
        await using (var upsert = new NpgsqlCommand(
            @"insert into character_quest (character_id, quest_id, step_id, tracked, progress, finished_at, replaying,
                                           resume_quest, pieces, updated_at)
              select $1, x.q, x.s, x.t, coalesce(x.p, 0), case when x.f then now() end, coalesce(x.r, false) and x.f,
                     coalesce(x.u, ''), coalesce(x.a, '[]'::jsonb), now()
                from jsonb_to_recordset($2) as x(q text, s text, t boolean, f boolean, p integer, r boolean, u text, a jsonb)
              on conflict (character_id, quest_id) do update
                 set step_id      = excluded.step_id,
                     tracked      = excluded.tracked,
                     progress     = excluded.progress,
                     finished_at  = coalesce(character_quest.finished_at, excluded.finished_at),
                     replaying    = excluded.replaying,
                     resume_quest = excluded.resume_quest,
                     pieces       = excluded.pieces,
                     updated_at   = now()", conn, tx))
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
                                                      'f', finished_at is not null, 'r', replaying,
                                                      'u', resume_quest, 'a', pieces)), '[]'::json)::text
             from character_quest where character_id = c.id)";

    static string? Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static int Int(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    static bool Bool(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
