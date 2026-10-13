using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HOW A KILL'S POINTS ARE COUNTED — plain numbers, so the rule can be checked by calling it. Each person who fought a
/// creature collects points out of 100: 60 by their share of the health players took off it, 15 by their share of the
/// time it spent after them, 15 by their share of the health they restored to OTHER players that it had taken, 5 for
/// the first to cost it health and 5 for the last. The most points owns the loot; experience is split by points.
/// Design: docs/2-systems/loot-owner-pointing.md.
///
/// <para>Every part is a SHARE of a fixed number of points, never a raw amount, so nothing a group does to itself can
/// make more points than the fight had: a part nobody earned (nobody healed) simply hands out nothing,
/// and what each person is paid is their points over all points handed out.</para>
/// </summary>
public static class KillPointsMath
{
    public const float DealtPoints = 60f, HeldPoints = 15f, HealedPoints = 15f, FirstHitPoints = 5f, LastHitPoints = 5f;

    /// <summary>One person's points for one creature.</summary>
    public static float Points(float dealt, float allDealt, float held, float allHeld, float healed, float allHealed,
                               bool firstHit, bool lastHit)
    {
        float points = 0f;
        if (allDealt > 0f) points += DealtPoints * Mathf.Clamp01(dealt / allDealt);
        if (allHeld > 0f) points += HeldPoints * Mathf.Clamp01(held / allHeld);
        if (allHealed > 0f) points += HealedPoints * Mathf.Clamp01(healed / allHealed);
        if (firstHit) points += FirstHitPoints;
        if (lastHit) points += LastHitPoints;
        return points;
    }

    /// <summary>The part of a kill worth <paramref name="worth"/> that <paramref name="points"/> out of
    /// <paramref name="allPoints"/> earns, rounded down — whoever has the most is handed what rounding left over.</summary>
    public static int Share(int worth, float points, float allPoints)
    {
        if (worth <= 0 || points <= 0f || allPoints <= 0f) return 0;
        return (int)System.Math.Min(worth, System.Math.Floor((double)worth * points / allPoints));
    }

    /// <summary>What everyone's recorded damage is multiplied by when a creature missing
    /// <paramref name="missingBefore"/> health gets some back and is left missing <paramref name="missingAfter"/> —
    /// so damage it has healed off is nobody's claim on it any more.</summary>
    public static float KeptAfterRegain(int missingBefore, int missingAfter)
    {
        if (missingBefore <= 0 || missingAfter <= 0) return 0f;
        return Mathf.Clamp01((float)missingAfter / missingBefore);
    }
}

/// <summary>
/// WHO FOUGHT ONE CREATURE, FOR ONE LIFE OF IT — kept on the server by the creature itself (<see cref="Health.Ledger"/>),
/// written by the door every blow passes through and read once, when it dies (<see cref="Rewards"/>). Emptied when the
/// creature dies, comes back to life, or gets back to full health.
///
/// <para>One entry per person, found by walking the list: an ordinary creature has one or two, and a crowded elite a
/// few hundred, which is still cheaper per blow than hashing. The list is made once per body and reused for every
/// life of it. A person who reconnects is a new object, so an entry also keeps their character id and is handed to
/// whoever returns with it.</para>
/// </summary>
public sealed class KillLedger
{
    struct Entry
    {
        public Character who;
        public string id;
        public float dealt, held, healed;
    }

    const string NoId = "no player id";

    readonly List<Entry> entries = new List<Entry>(2);
    int first = -1;
    // Whose entry the last moment of its attention went to: it is after the same person frame after frame, so this is
    // nearly always the answer and the walk is skipped.
    int heldAt = -1;

    public bool IsEmpty => entries.Count == 0;

    public void Clear()
    {
        entries.Clear();
        first = -1;
        heldAt = -1;
    }

    /// <summary>The person a unit's deeds count for: themselves, or whoever a creature is fighting for.</summary>
    public static Character PersonBehind(ICombatUnit unit)
    {
        if (PlayerUnits.Gone(unit)) return null;
        if (unit is Character person) return person;
        if (unit is Health creature && !PlayerUnits.Gone(creature.owner)) return creature.owner as Character;
        return null;
    }

    /// <summary><paramref name="by"/> just took <paramref name="removed"/> health off <paramref name="victim"/> — the
    /// health that actually came off, never the size of the blow, so an overkill claims no more than was left.</summary>
    public static void NoteDealt(Health victim, ICombatUnit by, int removed)
    {
        if (removed <= 0 || victim == null) return;
        var person = PersonBehind(by);
        if (person == null) return;
        var ledger = victim.LedgerForWriting();
        int i = ledger.IndexOf(person);
        var e = ledger.entries[i];
        e.dealt += removed;
        ledger.entries[i] = e;
        if (ledger.first < 0) ledger.first = i;
    }

    /// <summary>The creature spent <paramref name="seconds"/> after <paramref name="prey"/>. Counted from the moment
    /// it gives chase, with no exception — before anybody has hurt it, and whether or not it ever lands a blow, so
    /// nobody is credited less for being hard to hurt or hard to catch.
    /// Called from the creature's own tick: a comparison and an addition, no walk while it stays on one person.</summary>
    public void NoteHeld(ICombatUnit prey, float seconds)
    {
        if (seconds <= 0f) return;
        var person = PersonBehind(prey);
        if (person == null) return;
        if (heldAt < 0 || heldAt >= entries.Count || !ReferenceEquals(entries[heldAt].who, person)) heldAt = IndexOf(person);
        var e = entries[heldAt];
        e.held += seconds;
        entries[heldAt] = e;
    }

    /// <summary><paramref name="healer"/> restored <paramref name="restored"/> health this creature had taken from
    /// somebody else.</summary>
    public void NoteHealed(Character healer, int restored)
    {
        if (restored <= 0 || healer == null) return;
        int i = IndexOf(healer);
        var e = entries[i];
        e.healed += restored;
        entries[i] = e;
    }

    /// <summary>The creature got health back: every DAMAGE claim on it shrinks by the same share, and at full health
    /// nobody has one — nor a first hit, nor healing against it. The time it has spent after people stays: it is
    /// still chasing them.</summary>
    public void Regained(int missingBefore, int missingAfter)
    {
        float keep = KillPointsMath.KeptAfterRegain(missingBefore, missingAfter);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            e.dealt *= keep;
            if (keep <= 0f) e.healed = 0f;
            entries[i] = e;
        }
        if (keep <= 0f) first = -1;
    }

    /// <summary>It gave up on everybody. With no damage claim on it there was no fight, so the time it spent chasing
    /// is forgotten; with one, the fight is still open and everything stays.</summary>
    public void StoppedChasing()
    {
        if (first < 0) Clear();
    }

    int IndexOf(Character person)
    {
        for (int i = 0; i < entries.Count; i++)
            if (ReferenceEquals(entries[i].who, person)) return i;

        // Somebody who came back: the same character on a new object takes their old entry.
        string id = GameNetwork.CharacterIdOf(person);
        if (id != NoId)
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].id == id && PlayerUnits.Gone(entries[i].who))
                {
                    var back = entries[i];
                    back.who = person;
                    entries[i] = back;
                    return i;
                }
        entries.Add(new Entry { who = person, id = id });
        return entries.Count - 1;
    }

    // One death's sums, worked out once per question; a death asks a handful of times and never on a frame.
    static readonly List<float> points = new List<float>(8);

    /// <summary>Everyone's points at this moment, index for index with the entries. Somebody gone, or not on
    /// <paramref name="map"/>, scores nothing — their deeds still count in what the others' shares are measured
    /// against, so leaving never makes those who stayed look as if they did more.</summary>
    float Score(Character lastHit, string map)
    {
        float allDealt = 0f, allHeld = 0f, allHealed = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            allDealt += entries[i].dealt;
            allHeld += entries[i].held;
            allHealed += entries[i].healed;
        }
        points.Clear();
        float all = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            float p = 0f;
            if (!PlayerUnits.Gone(e.who) && e.who.Map == map)
                p = KillPointsMath.Points(e.dealt, allDealt, e.held, allHeld, e.healed, allHealed,
                                          i == first, ReferenceEquals(e.who, lastHit));
            points.Add(p);
            all += p;
        }
        return all;
    }

    int TopIndex()
    {
        int top = -1;
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] <= 0f) continue;
            if (top < 0 || points[i] > points[top]
                || (points[i] == points[top] && entries[i].dealt > entries[top].dealt)) top = i;
        }
        return top;
    }

    /// <summary>Who has the most points — the one the loot is kept for. Null when nobody here has any.</summary>
    public Character Top(Character lastHit, string map)
    {
        Score(lastHit, map);
        int top = TopIndex();
        points.Clear();
        return top >= 0 ? entries[top].who : null;
    }

    /// <summary>Split a kill worth <paramref name="worth"/> among everyone with points, by points. The parts add up to
    /// exactly <paramref name="worth"/>: what rounding leaves over goes to whoever has the most. A person whose part
    /// rounds to nothing is still listed, with 0 — they fought, so the kill counts for them.</summary>
    public void Split(int worth, Character lastHit, string map, List<Character> who, List<int> parts)
    {
        who.Clear();
        parts.Clear();
        float all = Score(lastHit, map);
        int top = TopIndex();
        if (top < 0 || all <= 0f) { points.Clear(); return; }
        int handed = 0, topAt = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            if (points[i] <= 0f) continue;
            int part = KillPointsMath.Share(worth, points[i], all);
            if (i == top) topAt = who.Count;
            who.Add(entries[i].who);
            parts.Add(part);
            handed += part;
        }
        if (topAt >= 0 && handed < worth) parts[topAt] += worth - handed;
        points.Clear();
    }
}

/// <summary>
/// WHICH CREATURES HAVE TAKEN HEALTH FROM ONE PERSON THAT NOBODY HAS RESTORED YET — what a heal from somebody else is
/// credited against (<see cref="KillLedger.NoteHealed"/>). A handful of entries in a fixed array, so a blow landing
/// allocates nothing; the oldest gives way when it is full. What is owed never exceeds the health the person is
/// actually missing, so health a potion or their own regeneration already gave back is not credited to a healer.
/// </summary>
public sealed class HurtBy
{
    struct Entry
    {
        public Health creature;
        public int life;
        public int owed;
    }

    const int Most = 8;
    readonly Entry[] entries = new Entry[Most];
    int count;

    public void Clear()
    {
        for (int i = 0; i < count; i++) entries[i] = default;
        count = 0;
    }

    /// <summary><paramref name="creature"/> just took <paramref name="taken"/> health; the person is now missing
    /// <paramref name="missingNow"/> in all.</summary>
    public void Note(Health creature, int taken, int missingNow)
    {
        if (creature == null || taken <= 0) return;
        if (missingNow <= 0) { Clear(); return; }
        int at = -1;
        for (int i = 0; i < count; i++)
            if (ReferenceEquals(entries[i].creature, creature) && entries[i].life == creature.Life) { at = i; break; }
        if (at < 0)
        {
            if (count == Most) RemoveAt(0);
            at = count++;
            entries[at] = new Entry { creature = creature, life = creature.Life };
        }
        entries[at].owed += taken;
        Trim(missingNow);
    }

    /// <summary>Somebody else restored <paramref name="restored"/> of this person's health: credit them, on each
    /// creature that took it, newest hurt first.</summary>
    public void Credit(Character healer, int restored, int missingBefore)
    {
        if (restored <= 0 || healer == null) return;
        Trim(missingBefore);
        for (int i = count - 1; i >= 0 && restored > 0; i--)
        {
            var e = entries[i];
            // A creature that died, or came back as a new life, is owed nothing by anyone.
            if (e.creature == null || e.creature.IsDead || e.creature.Life != e.life) { RemoveAt(i); continue; }
            int part = Mathf.Min(restored, e.owed);
            if (part <= 0) continue;
            var ledger = e.creature.Ledger;
            if (ledger != null) ledger.NoteHealed(healer, part);
            entries[i].owed -= part;
            restored -= part;
            if (entries[i].owed <= 0) RemoveAt(i);
        }
    }

    // What is owed in all can never be more than the health actually missing: the oldest hurts are forgiven first.
    void Trim(int missing)
    {
        int owed = 0;
        for (int i = 0; i < count; i++) owed += entries[i].owed;
        for (int i = 0; i < count && owed > missing; )
        {
            int cut = Mathf.Min(entries[i].owed, owed - missing);
            entries[i].owed -= cut;
            owed -= cut;
            if (entries[i].owed <= 0) RemoveAt(i); else i++;
        }
    }

    void RemoveAt(int at)
    {
        for (int i = at; i < count - 1; i++) entries[i] = entries[i + 1];
        entries[--count] = default;
    }
}
