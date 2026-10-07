using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// WORLD EVENTS on this map server — a timed change a GM makes to the whole world at once, such as an experience rate.
/// The character server holds the one true list; this is this map server's copy of it, kept two ways: the list is
/// PUSHED here the moment it changes (<see cref="MapServerLink"/>'s <c>/events</c> door), and asked for on a beat
/// (<see cref="Poll"/>), so a push that went missing is made up within one beat.
///
/// <para><b>Every change is said to every player on this map server</b>, as a line in their chat: an event starting, an
/// event ending, and — to somebody signing in while events run — which are running now. Every sentence is finished
/// here; a player's machine only shows it.</para>
///
/// <para><b>A copy older than the one already applied is ignored</b> (its revision is lower): a push and a poll answer
/// can cross on the way, and the older one would otherwise bring back an event that was replaced or stopped.</para>
///
/// Server only. Nothing here runs on a player's machine.
/// </summary>
public static class WorldEvents
{
    public const string ExperienceRate = "experience_rate";

    /// <summary>One event this map server is carrying out. Its end is on THIS machine's clock, worked out from the time
    /// the character server said was left — so the two machines' clocks never have to agree.</summary>
    sealed class Running
    {
        public double multiplier;
        public long startedAtMs;
        public DateTime endsAtUtc;
    }

    static readonly Dictionary<string, Running> running = new Dictionary<string, Running>(StringComparer.Ordinal);

    // The last event of each kind this server ENDED, by its start time — so a copy of the list read a moment before it
    // ended, arriving a moment after, cannot start it a second time. One entry per kind, so it cannot grow.
    static readonly Dictionary<string, long> lastEnded = new Dictionary<string, long>(StringComparer.Ordinal);

    static long appliedRevision;
    static readonly List<string> scratchKinds = new List<string>(4);

    // ---- the wire ------------------------------------------------------------------------------------------------

    [Serializable] public class Wire
    {
        public bool known;
        public long revision;
        public Event[] events;
    }

    [Serializable] public class Event
    {
        public string kind;
        public double multiplier;
        public long startedAtMs;
        public double secondsLeft;
    }

    // ---- what the rest of the game reads -------------------------------------------------------------------------

    /// <summary>What a kill worth <paramref name="experience"/> pays while the events now running last.</summary>
    public static int KillExperience(int experience)
        => running.TryGetValue(ExperienceRate, out var e) ? WorldEventMath.Multiplied(experience, e.multiplier) : experience;

    /// <summary>The one line somebody signing in is told — every event running now and how long each has left — or null
    /// when none is.</summary>
    public static string RunningNowLine()
    {
        if (running.Count == 0) return null;
        var sb = new StringBuilder("Events running now: ");
        bool first = true;
        foreach (var kv in running)
        {
            if (!first) sb.Append("; ");
            first = false;
            sb.Append(Describe(kv.Key, kv.Value.multiplier)).Append(" (")
              .Append(WorldEventMath.DurationText(WorldClock.SecondsUntil(kv.Value.endsAtUtc))).Append(" left)");
        }
        return sb.Append('.').ToString();
    }

    // ---- keeping the copy ------------------------------------------------------------------------------------------

    /// <summary>Take a copy of the list. <paramref name="justChanged"/> is true when it arrived because the list
    /// CHANGED (a push, or the answer to this server's own start or stop) — then a new event is announced as started.
    /// A new event found on the beat is one this server learned of late, so it is announced as running, with its time
    /// left, rather than as though it had just begun.</summary>
    public static void Apply(Wire list, bool justChanged)
    {
        if (list == null || !list.known || list.revision < appliedRevision) return;
        appliedRevision = list.revision;

        var incoming = list.events ?? Array.Empty<Event>();
        foreach (var e in incoming)
        {
            if (e == null || string.IsNullOrEmpty(e.kind) || !(e.multiplier > 0)) continue;
            var endsAt = WorldClock.UtcNow.AddSeconds(Math.Max(0, e.secondsLeft));

            if (running.TryGetValue(e.kind, out var held) && held.startedAtMs == e.startedAtMs)
            {
                held.multiplier = e.multiplier;
                held.endsAtUtc = endsAt;
                continue;
            }
            if (lastEnded.TryGetValue(e.kind, out var ended) && ended == e.startedAtMs) continue;

            // A NEW EVENT OF A KIND ALREADY RUNNING REPLACES IT — the old one is said to have ended first.
            if (held != null) End(e.kind, held);
            running[e.kind] = new Running { multiplier = e.multiplier, startedAtMs = e.startedAtMs, endsAtUtc = endsAt };
            Say(justChanged
                ? "Event started — " + Describe(e.kind, e.multiplier) + " for " + WorldEventMath.DurationText(e.secondsLeft) + "."
                : "Event running — " + Describe(e.kind, e.multiplier) + ", " + WorldEventMath.DurationText(e.secondsLeft) + " left.");
        }

        // ANYTHING HELD THAT THE LIST NO LONGER NAMES has been stopped, or ran out on the character server's clock first.
        scratchKinds.Clear();
        foreach (var kv in running)
        {
            bool named = false;
            foreach (var e in incoming)
                if (e != null && e.kind == kv.Key) { named = true; break; }
            if (!named) scratchKinds.Add(kv.Key);
        }
        foreach (var kind in scratchKinds) End(kind, running[kind]);
        scratchKinds.Clear();
    }

    /// <summary>End every event whose time is up on this machine's clock. Called every frame by the map server; it
    /// costs one comparison per running event, and nothing at all when none is.</summary>
    public static void Tick()
    {
        if (running.Count == 0) return;
        var now = WorldClock.UtcNow;
        scratchKinds.Clear();
        foreach (var kv in running)
            if (now >= kv.Value.endsAtUtc) scratchKinds.Add(kv.Key);
        foreach (var kind in scratchKinds) End(kind, running[kind]);
        scratchKinds.Clear();
    }

    static void End(string kind, Running which)
    {
        running.Remove(kind);
        lastEnded[kind] = which.startedAtMs;
        Say("Event ended — " + Describe(kind, which.multiplier) + ".");
    }

    static void Say(string line)
    {
        GameLog.Info(LogCat.Server, "world event: " + line);
        GameNetwork.TellEveryoneOnThisServer(line);
    }

    /// <summary>What an event IS, in a player's words: "experience ×1.5".</summary>
    static string Describe(string kind, double multiplier)
        => (kind == ExperienceRate ? "experience" : kind) + " " + WorldEventMath.MultiplierText(multiplier);

    // ---- asking for the list on a beat -------------------------------------------------------------------------------

    /// <summary>How often this map server asks for the list on its own — the most a missed push can go unnoticed.</summary>
    const float SecondsBetweenPolls = 30f;

    static float nextPoll;
    static bool polling;

    /// <summary>Ask the character server for the list, on the beat. Its own beat, NOT the registration's: a map server
    /// holds its registration back while a save is stuck, and the events must not wait on that.</summary>
    public static void Poll()
    {
        if (polling || Time.realtimeSinceStartup < nextPoll) return;
        nextPoll = Time.realtimeSinceStartup + SecondsBetweenPolls;
        polling = true;
        ServerHttp.Send("GET", MapServerLink.CharacterServerUrl + "/server/events", null, MapServerLink.ServerSecret,
            (ok, reply, error, _) =>
            {
                polling = false;
                // A FAILED ASK CHANGES NOTHING. Taking it as an empty list would end every event here and start them
                // all again on the next beat.
                if (!ok)
                {
                    GameLog.Warn(LogCat.Map, "could not read the world events — " + error + ". Keeping the ones held.");
                    return;
                }
                Apply(Parse(reply), justChanged: false);
            });
    }

    public static Wire Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonUtility.FromJson<Wire>(json); }
        catch (Exception e)
        {
            GameLog.Warn(LogCat.Map, "the world events could not be read — " + e.Message);
            return null;
        }
    }
}
