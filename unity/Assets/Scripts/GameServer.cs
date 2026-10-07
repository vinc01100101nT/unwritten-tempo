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

    /// <summary>A part that only draws or sounds — the sky, the weather, the music, the camera — asks this as the first
    /// line it runs. On the server it is switched off and removed on the spot, before it has done anything, and the
    /// answer is true; everywhere else the answer is false and it carries on. The server sends data and nothing else.</summary>
    public static bool RemovesPresentation(UnityEngine.Behaviour part)
    {
        if (!IsServer || part == null) return false;
        part.enabled = false;
        UnityEngine.Object.Destroy(part);
        return true;
    }

    /// <summary>THE TRIPWIRE every drawing door asks: TRUE on the server, where nothing may be drawn or sounded, and
    /// the door must do nothing. The server is never meant to reach one — every event is announced and drawn by the
    /// screens — so the first time each door is reached here it writes an error to the server's log, naming the door
    /// and the path that reached it. Each door says so once, so a door reached every frame cannot flood the log.</summary>
    public static bool CaughtDrawing(string door)
    {
        if (!IsServer) return false;
        if (reportedDoors.Add(door))
            GameLog.Error(LogCat.Server, "the server reached the drawing door '" + door + "', which it must never do — "
                                         + "announce the event and let the screens draw it. Reached by:\n" + Environment.StackTrace);
        return true;
    }

    static readonly System.Collections.Generic.HashSet<string> reportedDoors = new System.Collections.Generic.HashSet<string>();

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
