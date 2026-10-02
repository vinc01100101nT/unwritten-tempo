using Npgsql;

namespace CharServer;

/// <summary>
/// WHICH HAND-OVER OF A CHARACTER IS THE LIVE ONE. Every time a character is sent to a map server — entering the
/// world, or a door into a zone another map server holds — its number goes up by one, and that number travels with
/// the character. The map server sends it back with every save, and a save from an older hand-over is refused.
///
/// <para><b>Why.</b> A save replaces the whole stored character. A map server that lost touch with this one, kept a
/// player's save, and reconnected after that player had already moved on would otherwise write an older copy over
/// the newer one — rolling the player back. Refused, the late save is lost but never doubled.</para>
/// </summary>
public static class Handover
{
    /// <summary>Add one to this character's hand-over number and return it. Null when the character does not
    /// exist (or was deleted).</summary>
    public static async Task<int?> NextAsync(NpgsqlConnection conn, Guid characterId)
    {
        await using var cmd = new NpgsqlCommand(
            "update character set handover = handover + 1 where id = $1 and deleted_at is null returning handover",
            conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = characterId });
        return await cmd.ExecuteScalarAsync() is int n ? n : null;
    }
}

/// <summary>
/// THE FIRST MOMENTS AFTER THIS SERVER STARTS, when nobody is let into the world. Which map server holds whom lives
/// only in memory here, so right after a start this server knows no map servers at all — and a map server that
/// kept saves it could not send during an outage does not register until those saves have landed. Letting somebody
/// in during that time would read their character before their newest save arrived. Every map server reports at
/// least every 30 seconds, so 40 covers one full report with room to spare.
/// </summary>
public static class StartupGrace
{
    static readonly DateTime startedAt = DateTime.UtcNow;

    public const int Seconds = 40;

    /// <summary>Seconds still to wait before anybody may enter, or 0 once the grace is over.</summary>
    public static int SecondsLeft =>
        Math.Max(0, Seconds - (int)(DateTime.UtcNow - startedAt).TotalSeconds);

    public static void Begin() => _ = startedAt;
}
