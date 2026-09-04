using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// Every category a log line can belong to. A tester switches these on and off to decide what the log tells
/// them, so each one names ONE kind of thing that happens — never an umbrella.
///
/// <para>The first block already existed as hand-typed prefixes on 89 scattered calls. Those names are kept
/// exactly as they were, so anything anybody already greps for still matches. The second block is new, and
/// covers the moments a play-test needs to see and could not before.</para>
/// </summary>
public enum LogCat
{
    /// <summary>Start-up and general engine-side notes.</summary>
    Unwritten,

    /// <summary>The machine that owns the world, talking about itself.</summary>
    Server,

    /// <summary>Messages crossing between machines.</summary>
    Net,

    /// <summary>A player's own machine.</summary>
    Client,

    /// <summary>Maps loading, handing over, being held.</summary>
    Map,

    /// <summary>Something being created in the world.</summary>
    Spawn,

    /// <summary>The routine head-count. THE loudest line there is — one play-test's log held 1205 of these
    /// against 53 lines that said anything, which is what buried the findings.</summary>
    Census,

    /// <summary>A blow landing, or being refused, and why.</summary>
    Hit,

    /// <summary>A passive skill firing.</summary>
    Passive,

    /// <summary>A buff arriving, stacking, or running out.</summary>
    Buff,

    /// <summary>A skill cast being accepted or refused.</summary>
    Cast,

    /// <summary>A typed command being run.</summary>
    Command,
}

/// <summary>
/// THE one place a log line goes through — the choke point that makes the log filterable instead of a wall.
///
/// <para><b>Why this exists.</b> A play-test drowned: the routine head-count printed twice a second and buried
/// every line that mattered. There was no way to turn it down, because there was no central place to turn it
/// down AT — only 89 hand-written calls, each with a hand-typed prefix that 19 of them forgot. A tester now
/// says which categories it wants and reads only those.</para>
///
/// <para><b>AN OFF CATEGORY MUST COST NOTHING, AND THAT IS THE CALLER'S JOB TOO.</b> The check here is one bit
/// test against an integer. But a caller that writes an interpolated string has already paid for that string
/// before this function is entered, and these sit in the damage and passive paths, which fire per hit. So
/// every caller that BUILDS a string must sit behind <see cref="On"/>:</para>
/// <code>
/// if (GameLog.On(LogCat.Hit)) GameLog.Info(LogCat.Hit, string.Concat(name, " hit for ", dmg.ToString()));
/// </code>
/// <para>A bare literal with nothing to build needs no guard. The automatic hot-path scan
/// (<c>tools/check-perf-hotpaths.py</c>) reads per-hit paths, so an unguarded interpolation there is caught.</para>
///
/// <para><b>A warning or an error ALWAYS prints, whatever the switches say.</b> Categories decide how much
/// ROUTINE detail is wanted; they never hide a problem. Silencing a real fault to tidy a log is the exact
/// failure this was built to stop — a play-test once reported a clean console while a red error sat at the
/// top of it.</para>
/// </summary>
public static class GameLog
{
    // Everything that printed before categories existed still prints, so day one behaves exactly as before.
    // The new event categories start off: they cost nothing at all until a tester asks for them.
    const int LegacyOn = (1 << (int)LogCat.Unwritten) | (1 << (int)LogCat.Server) | (1 << (int)LogCat.Net)
                       | (1 << (int)LogCat.Client)    | (1 << (int)LogCat.Map)    | (1 << (int)LogCat.Spawn)
                       | (1 << (int)LogCat.Census);

    static int enabled = LegacyOn;

    /// <summary>Is this category being written right now? <b>Guard every built string with this</b> — an off
    /// category must cost one bit test, not a string build.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool On(LogCat cat) => (enabled & (1 << (int)cat)) != 0;

    /// <summary>Switch one category on or off. Returns what it is now, so a caller can say so out loud.</summary>
    public static bool Set(LogCat cat, bool on)
    {
        if (on) enabled |= 1 << (int)cat;
        else    enabled &= ~(1 << (int)cat);
        return On(cat);
    }

    /// <summary>Switch every category at once — "show me everything", or "quiet down".</summary>
    public static void SetAll(bool on) => enabled = on ? ~0 : 0;

    /// <summary>Put the switches back where they start: everything that printed before categories existed, and
    /// none of the new event detail.</summary>
    public static void Reset() => enabled = LegacyOn;

    /// <summary>Every category, in order — the fixed list the command reads and writes. A static array, so
    /// asking for it never allocates (unlike <c>Enum.GetValues</c>, which builds a fresh one each call).</summary>
    public static readonly LogCat[] AllCategories =
    {
        LogCat.Unwritten, LogCat.Server, LogCat.Net, LogCat.Client, LogCat.Map, LogCat.Spawn, LogCat.Census,
        LogCat.Hit, LogCat.Passive, LogCat.Buff, LogCat.Cast, LogCat.Command,
    };

    /// <summary>The lower-case word in square brackets at the head of the line. A tester filters the file on
    /// this, so it is the category's own name and nothing else. Returns a constant, so it never allocates.</summary>
    public static string Tag(LogCat cat)
    {
        switch (cat)
        {
            case LogCat.Unwritten: return "unwritten";
            case LogCat.Server:    return "server";
            case LogCat.Net:       return "net";
            case LogCat.Client:    return "client";
            case LogCat.Map:       return "map";
            case LogCat.Spawn:     return "spawn";
            case LogCat.Census:    return "census";
            case LogCat.Hit:       return "hit";
            case LogCat.Passive:   return "passive";
            case LogCat.Buff:      return "buff";
            case LogCat.Cast:      return "cast";
            case LogCat.Command:   return "command";
            default:               return "log";
        }
    }

    /// <summary>THE LOG TELLING YOU WHAT IT IS NOT TELLING YOU. Printed once when the world comes up, and it leads
    /// with what is switched OFF, because that is the half somebody can act on.
    ///
    /// <para><b>Why it is a log line and not a note in a document.</b> A tester always reads the log — that is the
    /// whole job — so this is the one place a message is certain to be seen. Instructions in a document depend on
    /// being read, and a document went unread three times in one day: a tester was told twice not to write files
    /// into the repository and did it anyway, and had two numbers in front of it that answered its own question
    /// and never compared them.</para>
    ///
    /// <para>It is BUILT FROM <see cref="AllCategories"/>, so a category added later shows up here on its own. A
    /// hand-written list would be one more thing that quietly stops being true.</para></summary>
    public static void AnnounceToTesters()
    {
        var sb = new System.Text.StringBuilder();
        int off = 0;
        for (int i = 0; i < AllCategories.Length; i++)
        {
            if (On(AllCategories[i])) continue;
            if (off++ > 0) sb.Append(", ");
            sb.Append(Tag(AllCategories[i]));
        }

        if (off == 0)
        {
            Debug.Log("[" + Tag(LogCat.Unwritten) + "] every kind of log line is switched on. Quieten one with "
                      + "'/log <name> off' — '/log' on its own lists them.");
            return;
        }

        Debug.Log("[" + Tag(LogCat.Unwritten) + "] THIS LOG IS NOT TELLING YOU ABOUT: " + sb
                  + ".  Switch one on with '/log <name> on', or all of them with '/log all on'. "
                  + "'/log' on its own shows every category and its state. Warnings and errors always print.");
    }

    /// <summary>Every category and whether it is on right now — what a tester reads to know what it is missing.
    /// Builds a string, so it belongs on a command and never in a frame.</summary>
    public static string Describe()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < AllCategories.Length; i++)
        {
            var c = AllCategories[i];
            if (sb.Length > 0) sb.Append("  ");
            sb.Append(On(c) ? "on " : "off ").Append(Tag(c));
        }
        return sb.ToString();
    }

    /// <summary>Turn a tester's word back into a category. False when nothing matches, so a typo is refused
    /// rather than silently switching the wrong thing.</summary>
    public static bool TryParse(string word, out LogCat cat)
    {
        cat = default;
        if (string.IsNullOrWhiteSpace(word)) return false;
        string w = word.Trim();
        for (int i = 0; i < AllCategories.Length; i++)
            if (string.Equals(Tag(AllCategories[i]), w, System.StringComparison.OrdinalIgnoreCase))
            {
                cat = AllCategories[i];
                return true;
            }
        return false;
    }

    // Every one of these takes the optional object the line is ABOUT. Unity makes a line with one attached
    // clickable — it selects that object — which is how somebody finds the misconfigured thing a warning names.
    // Losing it would quietly make those warnings harder to act on than they were before.

    /// <summary>A routine line. Dropped when its category is off — this is the one that gets filtered.</summary>
    public static void Info(LogCat cat, string message, Object context = null)
    {
        if (!On(cat)) return;
        Debug.Log("[" + Tag(cat) + "] " + message, context);
    }

    /// <summary>Something is wrong but the game carries on. ALWAYS printed — a switch never hides it.</summary>
    public static void Warn(LogCat cat, string message, Object context = null)
        => Debug.LogWarning("[" + Tag(cat) + "] " + message, context);

    /// <summary>Something is broken. ALWAYS printed — a switch never hides it.</summary>
    public static void Error(LogCat cat, string message, Object context = null)
        => Debug.LogError("[" + Tag(cat) + "] " + message, context);
}
