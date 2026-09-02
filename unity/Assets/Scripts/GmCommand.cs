using System;
using UnityEngine;

/// <summary>
/// One GM (game-master) command: its primary name, alternate names, a usage/summary line for
/// <c>/help</c>, and the handler that runs it. Registered in <see cref="GmCommands"/> — the single
/// dispatcher every chat "/…" line routes through, so a new command is one entry, never a new call site.
/// </summary>
public class GmCommand
{
    public readonly string name;        // primary name, lower-case, no leading slash
    public readonly string[] aliases;   // extra names that map to the same command
    public readonly string usage;       // e.g. "gold <amount>" — shown by /help
    public readonly string summary;     // one-liner for /help
    public readonly Action<GmContext> run;

    /// <summary>May an ordinary player run this? <b>Default NO</b>, and that default is the point: every
    /// command here changes something — a level, a bag, the creatures standing in the world — so a new one
    /// arrives locked, and the person adding it has to say out loud that it is harmless before anybody but a
    /// GM can type it. A permission that has to be remembered is one that eventually is not.</summary>
    public readonly bool anyoneMayRun;

    /// <summary>Changes something that everybody else in the world can see, so it belongs to the machine that
    /// owns the world. A player's game does not carry these out; it asks the server to (see
    /// <see cref="GmCommands.Execute"/>).</summary>
    public readonly bool changesTheWorld;

    public GmCommand(string name, string usage, string summary, Action<GmContext> run, params string[] aliases)
        : this(name, usage, summary, run, false, false, aliases) { }

    public GmCommand(string name, string usage, string summary, Action<GmContext> run,
                     bool anyoneMayRun, bool changesTheWorld, params string[] aliases)
    {
        this.name = name;
        this.usage = usage;
        this.summary = summary;
        this.run = run;
        this.anyoneMayRun = anyoneMayRun;
        this.changesTheWorld = changesTheWorld;
        this.aliases = aliases ?? Array.Empty<string>();
    }
}

/// <summary>
/// Everything a GM command handler needs: the parsed argument tokens plus a reply channel back to the
/// chat log. Resolved lazily (commands run on a key press, never per-frame) so a Find here is fine.
/// </summary>
public class GmContext
{
    public string[] args;               // tokens AFTER the command name
    public Action<string> reply;        // print a line back into the chat log

    public void Reply(string msg) => reply?.Invoke(msg);

    public int ArgCount => args != null ? args.Length : 0;
    public string Arg(int i) => (args != null && i >= 0 && i < args.Length) ? args[i] : null;

    /// <summary>All args from <paramref name="from"/> onward re-joined with spaces (for free-text tails).</summary>
    public string Rest(int from) => (args != null && from < args.Length) ? string.Join(" ", args, from, args.Length - from) : "";

    public bool TryInt(int i, out int v) { v = 0; var s = Arg(i); return s != null && int.TryParse(s, out v); }
    public bool TryFloat(int i, out float v)
        => float.TryParse(Arg(i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

    /// <summary>WHO ASKED, when that is somebody other than the person at this keyboard. Set on a server,
    /// where a command arrives from one of many connections and "the player" is not a question with a single
    /// answer — the same reason <see cref="PlayerUnits"/> exists. Left empty on a player's own game, where the
    /// only person who can type a command is the one already standing there.</summary>
    public Character askerCharacter;
    public Transform askerBody;

    public Character Ch => askerCharacter != null ? askerCharacter : Character.Instance;

    /// <summary>The NAME of the map the person who asked is standing in — what <see cref="Map"/> looks up, kept
    /// beside it so a message about the map and the map itself can never name two different ones.</summary>
    public string MapName => Ch != null ? Ch.Map : MapManager.CurrentMap;

    /// <summary>THE MAP THE PERSON WHO ASKED IS STANDING IN. On a server the request arrives from one of many
    /// connections and the machine's own idea of "the current map" is meaningless — it names whichever map it
    /// loaded last, which is somebody else's. Anything a command builds belongs here.
    ///
    /// <para>INVALID when this machine is not holding that map — a player may walk into a map the server never
    /// opened. Anything that builds must ask before it builds; see <c>GmCommands.TryAskersMap</c>.</para></summary>
    public UnityEngine.SceneManagement.Scene Map
        => UnityEngine.SceneManagement.SceneManager.GetSceneByName(MapName);

    public Transform Player
    {
        get
        {
            if (askerBody != null) return askerBody;
            var p = UnityEngine.Object.FindAnyObjectByType<Player3DController>();
            return p != null ? p.transform : null;
        }
    }
}
