using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SERVER: what each person did while they played on this server, added up as it happens and written as ONE line
/// the moment they stop — how long they played, the levels, kills, experience and gold, how often they died, how
/// many things fell for them and what became of them, how many quests they finished and which maps they walked.
///
/// <para><b>Why it exists.</b> A play-test is read back from the log, and the log is a list of moments. The question
/// asked afterwards is about the whole visit — "did they ever pick anything up?" — and answering it meant adding up
/// dozens of lines by hand. On 2026-10-07 a tester killed 23 creatures, had 18 things fall for them and picked up
/// none, and nothing in the log said so in one place.</para>
///
/// <para><b>One visit is one tally, across doors.</b> Walking into another map this server holds drops the old
/// connection and opens a new one; the tally is kept by character id, so it carries on. Only a real leave writes it
/// and lets it go. Somebody who walked to a map ANOTHER server holds never comes back here, so a tally left waiting
/// longer than <see cref="ForgetAwayAfterSeconds"/> is dropped unwritten — without that, every such move would leave
/// an entry behind on a server that runs for months.</para>
///
/// <para>Nothing here is saved and nothing is sent: it is a reading aid for the log and nothing else.</para>
/// </summary>
public static class PlaySession
{
    sealed class Tally
    {
        public string name;
        public float startedAt;
        public int startLevel, level;
        public int kills, experience, deaths;
        public long gold;
        public int fellForThem, pickedUp, vanished, questsFinished;
        public readonly List<string> maps = new List<string>(4);
        // -1 while they are playing here; otherwise when they walked through a door to somewhere.
        public float awaySince = -1f;
    }

    static readonly Dictionary<string, Tally> byCharacter = new Dictionary<string, Tally>();
    static readonly List<string> forgetScratch = new List<string>(4);

    /// <summary>How long a tally waits for somebody who walked through a door to arrive back on this server.</summary>
    public const float ForgetAwayAfterSeconds = 600f;

    static bool Watching => GameServer.IsServer;

    static Tally Of(Character c)
    {
        if (c == null || c.IsWatchedCopy) return null;
        string id = GameNetwork.CharacterIdOf(c);
        return byCharacter.TryGetValue(id, out var t) ? t : null;
    }

    /// <summary>They are now playing here — arrived in the world, or walked into another map this server holds.</summary>
    public static void Arrived(Character c, string characterId, string name)
    {
        string map = c != null ? c.Map : null;
        if (!Watching || c == null || string.IsNullOrEmpty(characterId)) return;
        if (!byCharacter.TryGetValue(characterId, out var t))
        {
            t = new Tally { name = name, startedAt = Time.unscaledTime, startLevel = c.Level };
            byCharacter[characterId] = t;
        }
        t.awaySince = -1f;
        t.level = c.Level;
        if (!string.IsNullOrEmpty(map) && !t.maps.Contains(map)) t.maps.Add(map);
    }

    /// <summary>They walked into a map they are now being played in — the same visit goes on.</summary>
    public static void EnteredMap(Character c, string map)
    {
        var t = Of(c);
        if (t != null && !string.IsNullOrEmpty(map) && !t.maps.Contains(map)) t.maps.Add(map);
    }

    public static void Killed(Character c, int experiencePaid)
    {
        var t = Of(c);
        if (t == null) return;
        t.kills++;
        t.experience += experiencePaid;
        t.level = c.Level;
    }

    public static void ReachedLevel(Character c, int level) { var t = Of(c); if (t != null) t.level = level; }
    public static void EarnedGold(Character c, long gold) { var t = Of(c); if (t != null) t.gold += gold; }
    public static void Died(Character c) { var t = Of(c); if (t != null) t.deaths++; }
    public static void ThingsFell(Character c, int piles) { var t = Of(c); if (t != null) t.fellForThem += piles; }
    public static void PickedUp(Character c) { var t = Of(c); if (t != null) t.pickedUp++; }
    public static void QuestFinished(Character c) { var t = Of(c); if (t != null) t.questsFinished++; }

    /// <summary>Something that fell for <paramref name="characterId"/> lay untaken until it vanished.</summary>
    public static void Vanished(string characterId)
    {
        if (!Watching || string.IsNullOrEmpty(characterId)) return;
        if (byCharacter.TryGetValue(characterId, out var t)) t.vanished++;
    }

    /// <summary>They left through a door — to a map here or on another server. The tally waits for them.</summary>
    public static void WentThroughADoor(string characterId)
    {
        if (!Watching || string.IsNullOrEmpty(characterId)) return;
        if (byCharacter.TryGetValue(characterId, out var t)) t.awaySince = Time.unscaledTime;
    }

    /// <summary>They stopped playing: the visit's one line is written, and the tally let go.</summary>
    public static void Stopped(string characterId)
    {
        if (!Watching || string.IsNullOrEmpty(characterId)) return;
        ForgetTheLongGone();
        if (!byCharacter.TryGetValue(characterId, out var t)) return;
        byCharacter.Remove(characterId);
        if (!GameLog.On(LogCat.Server)) return;

        int seconds = Mathf.Max(0, Mathf.RoundToInt(Time.unscaledTime - t.startedAt));
        var sb = new System.Text.StringBuilder(256);
        sb.Append(t.name).Append(" (").Append(characterId).Append(") stopped playing after ")
          .Append(seconds / 60).Append("m ").Append(seconds % 60).Append("s — level ")
          .Append(t.startLevel).Append(" → ").Append(t.level)
          .Append("; ").Append(t.kills).Append(" kill(s) for ").Append(t.experience).Append(" experience and ")
          .Append(t.gold).Append(" gold; died ").Append(t.deaths).Append(" time(s); ")
          .Append(t.fellForThem).Append(" thing(s) fell for them, ").Append(t.vanished).Append(" vanished untaken; picked up ")
          .Append(t.pickedUp).Append("; finished ").Append(t.questsFinished).Append(" quest(s); maps: ")
          .Append(string.Join(", ", t.maps));
        GameLog.Info(LogCat.Server, sb.ToString());
    }

    static void ForgetTheLongGone()
    {
        float now = Time.unscaledTime;
        forgetScratch.Clear();
        foreach (var kv in byCharacter)
        {
            // STILL PLAYING HERE is never forgotten. The old connection's goodbye can land after the new one's hello,
            // which marks a person away who is in fact standing in a map this server holds.
            if (kv.Value.awaySince < 0f || now - kv.Value.awaySince <= ForgetAwayAfterSeconds) continue;
            if (PlayerFighter.PlayedBy(kv.Key) != null) continue;
            forgetScratch.Add(kv.Key);
        }
        for (int i = 0; i < forgetScratch.Count; i++) byCharacter.Remove(forgetScratch[i]);
    }
}
