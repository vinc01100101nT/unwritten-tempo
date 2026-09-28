namespace CharServer;

/// <summary>
/// THE one check that a player's copy of the game is the version this server was published with. An outdated copy
/// disagrees with the server about what the world is, and a player who skipped an update would otherwise get in
/// and find it broken in ways nobody could explain.
///
/// <para><b>The current version has one owner: the patch folder's <c>version.txt</c></b>, written by
/// <c>tools/publish-update.py</c> in the same step that stamps the version into the game files the launcher hands
/// out. This server READS it, never keeps its own copy — a second number typed here would be the thing that drifts.
/// The file is re-read whenever it changes on disk, so publishing an update takes effect without a restart.</para>
///
/// <para><b>OFF when <c>PATCH_DIR</c> is not set</b>, which is every machine that does not hand out updates — this
/// desk, the editor, the end-to-end tests. There is nothing to be out of date against there.</para>
///
/// <para>Compatibility, not security. A player can type any version they like; what this stops is an honest
/// player on an old copy walking into a world that no longer matches it.</para>
/// </summary>
public sealed class GameVersion(string? patchDir)
{
    /// <summary>What a player's copy sends, on every request to this server.</summary>
    public const string Header = "X-Game-Version";

    public const string OutdatedMessage = "Your game is out of date. Close it and run the launcher to update.";

    readonly string? versionFile = string.IsNullOrWhiteSpace(patchDir) ? null : Path.Combine(patchDir, "version.txt");
    readonly object gate = new();
    DateTime readAt;
    string? current;

    public bool IsOn => versionFile != null;

    /// <summary>The version players must have, or null when none has been published yet.</summary>
    public string? Current
    {
        get
        {
            if (versionFile == null) return null;
            lock (gate)
            {
                var stamp = File.Exists(versionFile) ? File.GetLastWriteTimeUtc(versionFile) : DateTime.MinValue;
                if (stamp != readAt)
                {
                    readAt = stamp;
                    current = stamp == DateTime.MinValue ? null : File.ReadAllText(versionFile).Trim();
                    if (string.IsNullOrEmpty(current)) current = null;
                }
                return current;
            }
        }
    }

    /// <summary>Refuse an outdated copy before anything else is looked at, so the player is told to update rather
    /// than being told something about their sign-in that updating would have fixed.</summary>
    public async ValueTask<object?> RequireCurrent(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var wanted = Current;
        if (wanted != null && ctx.HttpContext.Request.Headers[Header].ToString() != wanted)
            return Results.Json(new { error = OutdatedMessage }, statusCode: StatusCodes.Status426UpgradeRequired);
        return await next(ctx);
    }
}
