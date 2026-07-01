using System;

/// <summary>
/// The Ring of Progression — the player's life's-work VESSEL. It carries its OWN level + XP
/// (separate from the character's), and OWNS the two systems that hang off ring progress:
///   • <see cref="Passives"/> — the Trio-Ring passive draft (the % multiplier layer), and
///   • <see cref="Skills"/>   — the class-agnostic active-skill ownership.
///
/// Design (see the "Ring of Progression" design doc): the ring levels in PARALLEL with the
/// character (the same kills feed both — see <see cref="Character.AddXP"/>). Every RING level-up
/// banks one passive draft point (the grant moved OFF character level). Active-skill picks are
/// earned per 10 points ALLOCATED into the ring (see <see cref="SkillRoster.EarnedPicks"/>).
///
/// A plain C# class (not a MonoBehaviour): <see cref="Character"/> owns one as <c>Character.Ring</c>.
/// Its effect on the character is GATED by whether a ring item is equipped (<see cref="Character.RingEquipped"/>):
/// unequip = "diminished, not erased" — the passive % stops applying and actives can't fire, but the
/// walk + level/XP are kept.
/// </summary>
public sealed class RingProgress
{
    /// <summary>The Trio-Ring passive draft state (ranks + banked points). Lives on the ring.</summary>
    public PassiveRanks Passives { get; } = new PassiveRanks();

    /// <summary>Which shared active skills the ring has unlocked. Lives on the ring.</summary>
    public SkillRoster Skills { get; } = new SkillRoster();

    /// <summary>The ring's own level (starts at 1). Independent of <see cref="Character.Level"/>.</summary>
    public int Level { get; private set; } = 1;

    /// <summary>Ring XP banked toward the next ring level.</summary>
    public int XP { get; private set; }

    /// <summary>XP needed to reach the next ring level — a slightly slower curve than the character's,
    /// so the ring is a deliberate, felt second track rather than a mirror of the level bar.</summary>
    public int XPToNext => 20 + Level * 12;

    /// <summary>Fired on any XP or level change — drives the K-panel ring readout (level + XP bar).</summary>
    public event Action OnChanged;

    /// <summary>Fired when the ring reaches a new level (passes the new level) — drives the ring level toast.</summary>
    public event Action<int> OnLeveledUp;

    /// <summary>Feed the ring <paramref name="amount"/> XP; each level gained banks one passive draft point.</summary>
    public void AddXP(int amount)
    {
        if (amount <= 0) return;
        XP += amount;
        while (XP >= XPToNext)
        {
            XP -= XPToNext;
            Level++;
            Passives.AddPoint();          // a ring level = one new Trio-Ring point to spend
            OnLeveledUp?.Invoke(Level);
        }
        OnChanged?.Invoke();
    }

    /// <summary>Dev/testing: set the ring's level directly (DevSettings "Ring level" knob). Each level gained
    /// banks one passive draft point, exactly like a normal level-up, so the ring track can be exercised
    /// without grinding XP. Lowering only sets the level (banked points are left alone).</summary>
    public void SetLevel(int newLevel)
    {
        newLevel = Math.Max(1, newLevel);
        while (Level < newLevel) { Level++; Passives.AddPoint(); OnLeveledUp?.Invoke(Level); }
        Level = newLevel;
        XP = 0;
        OnChanged?.Invoke();
    }

    /// <summary>Total points ALLOCATED into the ring so far — the gate for active-skill picks
    /// (<see cref="SkillRoster.EarnedPicks"/>) and the "N/10 to next skill" readout.</summary>
    public int PointsAllocated => Passives.TotalRanks();

    /// <summary>Points the player still has banked but not yet spent on the ring.</summary>
    public int BankedPoints => Passives.BankedPoints;

    // ---- save / load ----------------------------------------------------------
    public void Capture(out int level, out int xp) { level = Level; xp = XP; }

    /// <summary>Restore level + XP from a save. The passive ranks/banked + owned skills are restored
    /// separately (by <see cref="Character.RestoreSave"/>, via <see cref="Passives"/>/<see cref="Skills"/>).</summary>
    public void Restore(int level, int xp)
    {
        Level = Math.Max(1, level);
        XP = Math.Max(0, xp);
        OnChanged?.Invoke();
    }
}
