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
    /// <param name="rng">Optional RNG for the wildcard; defaults to a shared instance.
    /// (Wildcard is live-random in the MVP — seeded determinism is out of scope, doc §10.)</param>
    public static Draft BuildDraft(PassiveRanks passives, Random rng = null)
    {
        rng = rng ?? shared;

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
            new Option { stat = PickWildcard(leader, left, right, rng), isWildcard = true },
        };
        return new Draft { isOpening = false, options = opts };
    }

    // Uniform over the 7 stats outside the Trio (doc §11 default — distance-weighting
    // is a possible later tuning dial).
    static StatId PickWildcard(StatId leader, StatId left, StatId right, Random rng)
    {
        var pool = new List<StatId>(SkillRing.Count - 3);
        foreach (var stat in SkillRing.Ring)
            if (stat != leader && stat != left && stat != right)
                pool.Add(stat);
        return pool[rng.Next(pool.Count)];
    }

    static readonly Random shared = new Random();
}
