using System.Collections.Generic;

/// <summary>
/// WHICH CREATURES A SUMMONER CALLS, and how many of each — worked out from the roster every time, never typed onto a
/// creature, so adding or re-levelling a creature re-pairs everything with nobody remembering to. Plain lists in, plain
/// lists out, so the rule can be checked by calling it. Design: docs/3-world/mob-roster.md.
///
/// <para><b>An elite</b> calls the three normals nearest its level at or below it, in the shares
/// <see cref="CreatureKinds.EliteSummonShares"/> (five of the nearest, three of the next, two of the one after).</para>
///
/// <para><b>A shadow</b> calls one elite: the shadows ranked from the highest level down pair with the elites ranked the
/// same way, so the level-160 shadow calls the level-99 elite and each step down the shadows is a step down the elites.</para>
/// </summary>
public static class SummonPicks
{
    /// <summary>One kind of creature a summoner calls, and how many of it it keeps out.</summary>
    public struct Pick
    {
        public MonsterDef def;
        public int count;
        public Pick(MonsterDef def, int count) { this.def = def; this.count = count; }
    }

    // Reused: the sorted candidates one question works through. Asked once per summoner life, never per frame.
    static readonly List<MonsterDef> scratch = new List<MonsterDef>(32);
    static readonly List<MonsterDef> scratch2 = new List<MonsterDef>(32);

    /// <summary>Fill <paramref name="into"/> with what <paramref name="summoner"/> calls. Empty when its kind does not
    /// summon, or when nothing on the roster answers.</summary>
    public static void For(MonsterDef summoner, IReadOnlyList<MonsterDef> roster, List<Pick> into)
    {
        into.Clear();
        if (summoner == null || roster == null) return;
        if (summoner.kind == CreatureKind.Elite) ForElite(summoner.level, roster, into);
        else if (summoner.kind == CreatureKind.Shadow) ForShadow(summoner, roster, into);
    }

    /// <summary>The normals an elite of <paramref name="eliteLevel"/> calls.</summary>
    public static void ForElite(int eliteLevel, IReadOnlyList<MonsterDef> roster, List<Pick> into)
    {
        into.Clear();
        scratch.Clear();
        for (int i = 0; i < roster.Count; i++)
        {
            var m = roster[i];
            if (m != null && m.kind == CreatureKind.Normal && m.level <= eliteLevel) scratch.Add(m);
        }
        scratch.Sort(HighestLevelFirst);

        var shares = CreatureKinds.EliteSummonShares;
        int n = System.Math.Min(shares.Length, scratch.Count);
        for (int i = 0; i < n; i++) into.Add(new Pick(scratch[i], shares[i]));
        // FEWER THAN THREE NORMALS AT OR BELOW IT: the shares that found nobody go to the last one found, so an elite
        // near the bottom of the ladder still keeps its full number out.
        for (int i = n; i < shares.Length && n > 0; i++)
        {
            var last = into[n - 1];
            into[n - 1] = new Pick(last.def, last.count + shares[i]);
        }
        scratch.Clear();
    }

    /// <summary>The elite a shadow calls, <see cref="CreatureKinds.MaxSummons"/> of it.</summary>
    public static void ForShadow(MonsterDef shadow, IReadOnlyList<MonsterDef> roster, List<Pick> into)
    {
        into.Clear();
        scratch.Clear();
        scratch2.Clear();
        for (int i = 0; i < roster.Count; i++)
        {
            var m = roster[i];
            if (m == null) continue;
            if (m.kind == CreatureKind.Shadow) scratch.Add(m);
            else if (m.kind == CreatureKind.Elite) scratch2.Add(m);
        }
        scratch.Sort(HighestLevelFirst);
        scratch2.Sort(HighestLevelFirst);
        int rank = scratch.IndexOf(shadow);
        if (rank >= 0 && rank < scratch2.Count)
            into.Add(new Pick(scratch2[rank], CreatureKinds.MaxSummons(CreatureKind.Shadow)));
        scratch.Clear();
        scratch2.Clear();
    }

    // Highest level first; the same level is settled by id so every machine and every run agrees.
    static int HighestLevelFirst(MonsterDef a, MonsterDef b)
    {
        int byLevel = b.level.CompareTo(a.level);
        return byLevel != 0 ? byLevel : string.CompareOrdinal(a.id, b.id);
    }
}
