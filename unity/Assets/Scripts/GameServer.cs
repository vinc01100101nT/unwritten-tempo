using System;

/// <summary>
/// Is this copy of the game running as the authoritative game server, rather than as somebody's client?
/// THE single answer to that question — everything that must behave differently on a server asks here, so the
/// test is written once and cannot drift between the places that need it.
///
/// <para><b>Two ways it turns on.</b> A Dedicated Server build says so by itself (Unity defines
/// <c>UNITY_SERVER</c> for that build target). Anything else — a normal build, or the Editor — is a server only
/// when the <c>GAME_SERVER</c> environment variable is set, which is how a server is run locally for testing
/// without producing a separate build first. Same shape as <see cref="AccountSession.ServerUrl"/> reading
/// <c>ACCOUNTS_URL</c>: a build is pointed at a job by its environment, never by a rebuild.</para>
///
/// <para>Read ONCE at startup and cached. The answer cannot change while the process is alive, and a property
/// that re-read the environment on every call would be a per-frame cost for a value that is a constant.</para>
/// </summary>
public static class GameServer
{
    /// <summary>True when this process owns the world instead of watching one.</summary>
    public static readonly bool IsServer = Resolve();

    static bool Resolve()
    {
#if UNITY_SERVER
        return true;
#else
        // Present and not explicitly switched off. Any other value counts as "on" so that the usual shell
        // habits (=1, =true, =yes) all work without a list of accepted spellings.
        string v = Environment.GetEnvironmentVariable("GAME_SERVER");
        return !string.IsNullOrEmpty(v) && v != "0" && !v.Equals("false", StringComparison.OrdinalIgnoreCase);
#endif
    }
}
