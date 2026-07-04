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

    public GmCommand(string name, string usage, string summary, Action<GmContext> run, params string[] aliases)
    {
        this.name = name;
        this.usage = usage;
        this.summary = summary;
        this.run = run;
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

    public Character Ch => Character.Instance;

    public Transform Player
    {
        get { var p = UnityEngine.Object.FindFirstObjectByType<Player3DController>(); return p != null ? p.transform : null; }
    }
}
