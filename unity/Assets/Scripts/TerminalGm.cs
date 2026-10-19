using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// THE TERMINAL'S DOOR INTO THE GM COMMANDS, on a map server. <c>tools/gm.py</c> asks the character server, which
/// hands the request here with the server secret (see <see cref="MapServerLink"/>); this runs it through the same
/// command list the in-game chat uses (<see cref="GmCommands.ExecuteFromTerminal"/>) and answers with what the
/// command said, or answers a status read with what this server holds. Called on the game loop only.
/// </summary>
public static class TerminalGm
{
    [Serializable]
    public class Ask
    {
        public string map;
        public string line;
        /// <summary>The <c>--as</c> character, found by the character server from the name typed. Empty = nobody.</summary>
        public string characterId;
    }

    [Serializable]
    class AnswerBody
    {
        public string[] lines;
        /// <summary>No line came back in time — the command may or may not have run.</summary>
        public bool unsure;
    }

    // A command that answers later (a summon loading creatures, a world event asking every map server) is waited for
    // up to FirstLineWait; once it has said something, the answer goes after QuietFor with nothing more. AllWait is the
    // ceiling for a command that keeps talking. The character server waits longer than AllWait, so it always hears.
    const float FirstLineWait = 8f;
    const float QuietFor = 1f;
    const float AllWait = 12f;

    class Pending
    {
        public HttpListenerContext asker;
        public readonly List<string> lines = new List<string>();
        public float startedAt;
        public float lastLineAt = -1f;
        public bool answered;
    }

    static readonly List<Pending> pending = new List<Pending>();

    // The chat's colour tags, and the zero-width space the chat puts after a '<' in typed text so it cannot become a tag.
    static readonly Regex Tags = new Regex("<[^>]*>|​", RegexOptions.Compiled);

    public static void Run(Ask ask, HttpListenerContext asker)
    {
        var p = new Pending { asker = asker, startedAt = Time.realtimeSinceStartup };
        pending.Add(p);
        void Reply(string text) => Heard(p, text);

        if (ask == null || string.IsNullOrEmpty(ask.map) || string.IsNullOrEmpty(ask.line))
        { Reply("Send a map and a command."); return; }
        if (!MapServerLink.Holds(ask.map)) { Reply($"This map server is not holding {ask.map}."); return; }

        Character ch = null;
        Transform body = null;
        string asName = null;
        if (!string.IsNullOrEmpty(ask.characterId))
        {
            var shell = PlayerFighter.PlayedBy(ask.characterId);
            if (shell == null) { Reply("That character is not on the map server holding " + ask.map + "."); return; }
            asName = shell.playerName;
            // NOBODY WHO IS LEAVING. Anything done to a body after its move began happens to a copy nobody reads again.
            if (shell.Leaving) { Reply($"{asName} is changing maps — try again in a moment."); return; }
            ch = shell.ServerCombatUnit as Character;
            if (ch == null || ch.Map != ask.map) { Reply($"{asName} is not in {ask.map}."); return; }
            body = shell.transform;
        }

        GameLog.Info(LogCat.Server, "the terminal ran '" + ask.line + "' in " + ask.map
                                    + (asName != null ? " as " + asName : ""));
        GmCommands.ExecuteFromTerminal(ask.line, ask.map, ch, body, Reply);
    }

    /// <summary>What this server holds for a map, as finished lines: its maps, the time of day, the weather, and who
    /// is in that map.</summary>
    public static void Status(Ask ask, HttpListenerContext asker)
    {
        var p = new Pending { asker = asker, startedAt = Time.realtimeSinceStartup };
        pending.Add(p);
        if (ask == null || string.IsNullOrEmpty(ask.map)) { Heard(p, "Send a map."); return; }
        if (!MapServerLink.Holds(ask.map)) { Heard(p, $"This map server is not holding {ask.map}."); return; }

        Heard(p, $"{ask.map} is on map server {MapServerLink.ServerId}, which holds: {MapServerLink.HeldMapsText()}. "
                 + "Weather and time are held once for all of them.");

        var tod = TimeOfDay.Instance;
        if (tod == null) Heard(p, "Time of day: this server has no clock.");
        else
        {
            string clock = RealCalendar.ClockDigits(tod.MinuteOfDay, out bool pm);
            Heard(p, $"Time of day: {clock} {(pm ? "PM" : "AM")} GMT+8, "
                     + $"{RealCalendar.DateText(RealCalendar.DateOf(tod.DayIndex))} ({(tod.IsDay ? "day" : "night")}, "
                     + $"{tod.time01:0.000} — 0 is sunrise, 0.25 noon, 0.5 sunset, 0.75 midnight).");
        }

        var wd = WeatherDirector.Instance;
        if (wd == null) Heard(p, "Weather: this server has no weather.");
        else if (WeatherDirector.ForcedByServer.HasValue)
            Heard(p, $"Weather: {wd.CurrentName} — held by a GM for "
                     + $"{(WorldClock.EpochSecondsNow - WeatherDirector.ForcedSince) / 60.0:0} min, until this server restarts.");
        else Heard(p, $"Weather: {wd.CurrentName} — the calendar's for today.");

        var units = new List<ICombatUnit>();
        PlayerUnits.CollectIn(ask.map, units);
        var names = new List<string>();
        foreach (var u in units)
        {
            var shell = GameNetwork.ShellFor(u as Character);
            if (shell != null) names.Add(shell.playerName);
        }
        Heard(p, names.Count == 0 ? $"Players in {ask.map}: nobody."
                                  : $"Players in {ask.map} ({names.Count}): {string.Join(", ", names)}.");
    }

    static void Heard(Pending p, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        string plain = Tags.Replace(text, "");
        // Every line goes in the log, a late one too — the log is where "did it run?" is answered after the fact.
        GameLog.Info(LogCat.Server, "the terminal was answered: " + plain);
        if (p.answered) return;
        p.lines.Add(plain);
        p.lastLineAt = Time.realtimeSinceStartup;
    }

    /// <summary>Send every answer whose command has finished talking. Called once a frame; nothing to do costs one
    /// count check.</summary>
    public static void Tick()
    {
        if (pending.Count == 0) return;
        float now = Time.realtimeSinceStartup;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var p = pending[i];
            bool quiet = p.lines.Count > 0 && now - p.lastLineAt >= QuietFor;
            bool tooLong = p.lines.Count == 0 ? now - p.startedAt >= FirstLineWait : now - p.startedAt >= AllWait;
            if (!quiet && !tooLong) continue;

            p.answered = true;
            pending.RemoveAt(i);
            var body = JsonUtility.ToJson(new AnswerBody { lines = p.lines.ToArray(), unsure = p.lines.Count == 0 });
            try { MapServerLink.Reply(p.asker, 200, body); }
            catch (Exception e) { GameLog.Warn(LogCat.Map, "could not answer the terminal — " + e.Message); }
        }
    }
}
