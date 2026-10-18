using System;
using System.Collections.Generic;

/// <summary>
/// The player's passive skill state — where on the ring they've walked. A plain C#
/// class (not a MonoBehaviour): <see cref="Character"/> owns one instance as
/// <c>Character.Passives</c>, the effective-stat getters fold its <see cref="Percent"/>
/// in, and the ring column of the Character window draws it.
/// SERVER-OWNED: the server spends points (<see cref="Pick"/>) and saves the walk; the player's machine asks for a
/// pick and draws what the server sends back (<see cref="TakeFromServer"/>).
///
/// Model (doc §4): every level-up <see cref="AddPoint"/>s a bankable skill point.
/// Spending one = <see cref="Pick"/>: it always does the same two things —
/// <c>ranks[chosen]++; Leader = chosen;</c>. The "stack / slide / recenter" steering
/// is emergent: picking the current leader leaves it put, picking a neighbor slides the
/// window, picking the wildcard recenters — all just "leader becomes whatever you chose."
/// </summary>
public class PassiveRanks
{
    readonly Dictionary<StatId, int> ranks = new Dictionary<StatId, int>();

    /// <summary>The current center of the Trio window — the last stat picked.
    /// <see cref="HasLeader"/> is false until the level-1 opening pick chooses a start.</summary>
    public StatId Leader { get; private set; }
    public bool HasLeader { get; private set; }

    /// <summary>Unspent skill points. Each level-up banks one; each <see cref="Pick"/> spends one.
    /// Starts at 1 — the level-1 "free start" pick (all stats, no wildcard — doc §4).</summary>
    public int BankedPoints { get; private set; } = 1;

    /// <summary>Fired whenever points or ranks change — drives both the K-panel badge and
    /// (via Character) a stat recompute.</summary>
    public event Action OnChanged;

    /// <summary>Goes up by one on every change to the walk, the wildcard included — so the server can tell whether
    /// there is anything new to send without reading the whole walk every beat.</summary>
    public int Version { get; private set; }

    public int RanksIn(StatId stat) => ranks.TryGetValue(stat, out var r) ? r : 0;

    /// <summary>Read-only view of every invested rank — used by the save system to snapshot the walk.</summary>
    public IReadOnlyDictionary<StatId, int> RankMap => ranks;

    /// <summary>Restore the whole passive state from a save (parallel arrays, since JsonUtility can't
    /// serialize a Dictionary). Replaces any current ranks/leader/banked total and fires
    /// <see cref="OnChanged"/> so the stat layer + K-panel refresh.</summary>
    public void LoadState(StatId[] stats, int[] values, StatId leader, bool hasLeader, int banked)
    {
        ranks.Clear();
        if (stats != null && values != null)
            for (int i = 0; i < stats.Length && i < values.Length; i++)
                ranks[stats[i]] = values[i];
        Leader = leader;
        HasLeader = hasLeader;
        BankedPoints = Math.Max(0, banked);
        Version++;
        OnChanged?.Invoke();
    }

    /// <summary>The passive multiplier for a stat (0.32 == +32%), straight off the soft-cap curve.</summary>
    public float Percent(StatId stat) => SkillRing.Percent(stat, RanksIn(stat));

    /// <summary>Bank a skill point (called once per level gained).</summary>
    public void AddPoint()
    {
        BankedPoints++;
        Version++;
        OnChanged?.Invoke();
    }

    /// <summary>Debug/respec: force the unspent banked total (clamped at 0). Used when the character
    /// level is tuned in the inspector, so the bankable points match what a real level-up walk grants.
    /// Fires <see cref="OnChanged"/> so the K-panel badge refreshes.</summary>
    public void SetBankedPoints(int points)
    {
        BankedPoints = Math.Max(0, points);
        Version++;
        OnChanged?.Invoke();
    }

    /// <summary>The draft's across-the-ring card for the next point. ROLLED ON THE SERVER ONLY
    /// (<see cref="RollWildcard"/>) and sent to the player's machine, which draws it — so the card on screen is the
    /// one the server will accept. <see cref="HasWildcard"/> is false before the first pick, when there is none.</summary>
    public StatId Wildcard { get; private set; }
    public bool HasWildcard { get; private set; }

    /// <summary>SERVER: roll a fresh wildcard for the current leader (none before the opening pick).</summary>
    public void RollWildcard(Random rng)
    {
        HasWildcard = HasLeader;
        if (HasLeader) Wildcard = DraftService.RollWildcard(Leader, this, rng);
        Version++;
    }

    /// <summary>SERVER: may <paramref name="stat"/> be picked right now — a point to spend, and the stat is one of the
    /// cards the draft is showing (any stat for the opening pick; else the leader, a neighbour, or the wildcard), and
    /// the stat is not already at <see cref="SkillRing.NodeCap"/> ranks.</summary>
    public bool MayPick(StatId stat)
    {
        if (BankedPoints <= 0) return false;
        // a node stops at its cap
        if (RanksIn(stat) >= SkillRing.NodeCap) return false;
        if (!HasLeader) return true;
        return stat == Leader || stat == SkillRing.LeftOf(Leader) || stat == SkillRing.RightOf(Leader)
               || (HasWildcard && stat == Wildcard);
    }

    /// <summary>PLAYER'S MACHINE: take the whole walk as the server now has it, wildcard included, with what each
    /// stat is worth now and after one more point (whole percent, indexed by <see cref="StatId"/>).</summary>
    public void TakeFromServer(StatId[] stats, int[] values, StatId leader, bool hasLeader, int banked,
                               StatId wildcard, bool hasWildcard, int[] percentNow, int[] percentNext)
    {
        Wildcard = wildcard;
        HasWildcard = hasWildcard;
        for (int i = 0; i < toldPercentNow.Length; i++)
        {
            toldPercentNow[i] = percentNow != null && i < percentNow.Length ? percentNow[i] : 0;
            toldPercentNext[i] = percentNext != null && i < percentNext.Length ? percentNext[i] : 0;
        }
        LoadState(stats, values, leader, hasLeader, banked);
    }

    // The server's whole-percent figures for the window, as last sent. Zero until the first walk arrives.
    readonly int[] toldPercentNow = new int[Enum.GetValues(typeof(StatId)).Length];
    readonly int[] toldPercentNext = new int[Enum.GetValues(typeof(StatId)).Length];

    /// <summary>PLAYER'S MACHINE: what <paramref name="stat"/> is worth now, in whole percent, as the server said.</summary>
    public int ShownPercent(StatId stat) => toldPercentNow[(int)stat];

    /// <summary>PLAYER'S MACHINE: what one more point would make <paramref name="stat"/>, as the server said.</summary>
    public int ShownNextPercent(StatId stat) => toldPercentNext[(int)stat];

    /// <summary>Spend one banked point on <paramref name="stat"/>: +1 rank and recenter the
    /// leader on it. No-op (and returns false) if there are no points to spend.</summary>
    public bool Pick(StatId stat)
    {
        if (BankedPoints <= 0) return false;
        BankedPoints--;
        ranks[stat] = RanksIn(stat) + 1;
        Leader = stat;
        HasLeader = true;
        Version++;
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>Total ranks invested across all stats — the length of the walk so far.</summary>
    public int TotalRanks()
    {
        int total = 0;
        foreach (var r in ranks.Values) total += r;
        return total;
    }
}
