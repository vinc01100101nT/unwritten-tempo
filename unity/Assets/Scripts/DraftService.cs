using System;
using System.Collections.Generic;

/// <summary>
/// Pure, Unity-free logic for the level-up draft (doc §4). Given the player's
/// <see cref="PassiveRanks"/>, it produces the cards to show; the UI just renders them
/// and calls <c>passives.Pick(stat)</c> on the chosen one. No MonoBehaviour, no scene
/// access — so it can be exercised from an editor self-test or unit tests.
/// </summary>
public static class DraftService
{
    /// <summary>One card in a draft.</summary>
    public struct Option
    {
        public StatId stat;
        public bool isWildcard;   // the across-the-ring gamble
        public bool isLeader;     // the center "stack this" card
    }

    /// <summary>A draft to present. <see cref="isOpening"/> means the level-1 free start:
    /// all stats, no wildcard. Otherwise <see cref="options"/> is [left, leader, right, wildcard].</summary>
    public struct Draft
    {
        public bool isOpening;
        public List<Option> options;
    }

    /// <summary>Build the draft the player should see right now.</summary>
    /// <param name="passives">The current rank state (its <c>Leader</c> drives the Trio).</param>
    /// <param name="rng">Only for a caller that owns the walk (the editor's simulated walk): rolls a fresh wildcard
    /// first. Everyone else passes nothing and the draft shows the wildcard the server rolled and sent.</param>
    public static Draft BuildDraft(PassiveRanks passives, Random rng = null)
    {
        if (rng != null) passives.RollWildcard(rng);

        // Level-1 opening: a free choice of any stat, no wildcard. Whatever they pick
        // becomes the first leader.
        if (!passives.HasLeader)
        {
            var all = new List<Option>(SkillRing.Count);
            foreach (var stat in SkillRing.Ring)
                all.Add(new Option { stat = stat });
            return new Draft { isOpening = true, options = all };
        }

        var leader = passives.Leader;
        var left = SkillRing.LeftOf(leader);
        var right = SkillRing.RightOf(leader);

        var opts = new List<Option>(4)
        {
            new Option { stat = left },
            new Option { stat = leader, isLeader = true },
            new Option { stat = right },
        };
        if (passives.HasWildcard) opts.Add(new Option { stat = passives.Wildcard, isWildcard = true });
        return new Draft { isOpening = false, options = opts };
    }

    /// <summary>SERVER: roll the wildcard — uniform over the 7 stats outside the Trio that still take a rank (a node
    /// at <see cref="SkillRing.NodeCap"/> is left out). At most 3 nodes can be full, so the pool is never empty.</summary>
    public static StatId RollWildcard(StatId leader, PassiveRanks ranks, Random rng)
    {
        rng = rng ?? shared;
        var left = SkillRing.LeftOf(leader);
        var right = SkillRing.RightOf(leader);
        pool.Clear();
        foreach (var stat in SkillRing.Ring)
            if (stat != leader && stat != left && stat != right && ranks.RanksIn(stat) < SkillRing.NodeCap)
                pool.Add(stat);
        return pool.Count > 0 ? pool[rng.Next(pool.Count)] : leader;
    }

    // Reused by every wildcard roll — rolled on the server only, one pick at a time.
    static readonly List<StatId> pool = new List<StatId>(SkillRing.Count);

    static readonly Random shared = new Random();
}
