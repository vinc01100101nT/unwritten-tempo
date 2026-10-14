/// <summary>
/// An immutable readout of every derived stat the Character/Inventory windows show, computed
/// from a given equipment loadout. The LIVE panel reads <see cref="Character.CurrentDerived"/>;
/// the hover "equip this → ATK 220→248" preview reads <see cref="Character.DeriveWith"/> on a
/// HYPOTHETICAL set and diffs the two. Both go through the same Character formulas, so the
/// preview can never drift from real combat numbers.
/// </summary>
public struct DerivedSnapshot
{
    /// <summary>A sheet nobody has filled in yet: every number zero, except the three multipliers, which are 1 — so a
    /// swing or a cast read off it before the server's first word is neither instant nor infinite.</summary>
    public static readonly DerivedSnapshot Neutral = new DerivedSnapshot { critMult = 1f, aspdMult = 1f, castTimeMult = 1f };

    /// <summary>Field-by-field, so a compare allocates nothing (a struct's default Equals can box and reflect).</summary>
    public static bool Same(in DerivedSnapshot a, in DerivedSnapshot b) =>
        a.atkMin == b.atkMin && a.atkMax == b.atkMax && a.matkMin == b.matkMin && a.matkMax == b.matkMax &&
        a.meleeMin == b.meleeMin && a.meleeMax == b.meleeMax && a.rangedMin == b.rangedMin && a.rangedMax == b.rangedMax &&
        a.def == b.def && a.mdef == b.mdef && a.hit == b.hit && a.flee == b.flee &&
        a.maxHP == b.maxHP && a.maxMP == b.maxMP && a.moveSpeed == b.moveSpeed &&
        a.barrierHP == b.barrierHP &&
        UnityEngine.Mathf.Approximately(a.critChance, b.critChance) && UnityEngine.Mathf.Approximately(a.critMult, b.critMult) &&
        UnityEngine.Mathf.Approximately(a.aspdMult, b.aspdMult) && UnityEngine.Mathf.Approximately(a.castTimeMult, b.castTimeMult) &&
        UnityEngine.Mathf.Approximately(a.swingsPerSecond, b.swingsPerSecond) &&
        UnityEngine.Mathf.Approximately(a.guardCutPct, b.guardCutPct) &&
        UnityEngine.Mathf.Approximately(a.lifeStealPct, b.lifeStealPct) && UnityEngine.Mathf.Approximately(a.reflectPct, b.reflectPct) &&
        UnityEngine.Mathf.Approximately(a.manaLeechPct, b.manaLeechPct) &&
        UnityEngine.Mathf.Approximately(a.basicAttackRange, b.basicAttackRange);

    public int atkMin, atkMax;         // the physical attack of the weapon in hand — one of the two below
    public int meleeMin, meleeMax;     // what any non-ranged weapon, or bare hands, swings with
    public int rangedMin, rangedMax;   // what a ranged weapon swings with
    public int matkMin, matkMax;
    public int def, mdef;
    public int hit, flee;
    public int maxHP, maxMP;
    public int moveSpeed;
    public float critChance;   // 0..1
    public float critMult;     // e.g. 1.4 = 140%
    public float aspdMult;     // >1 = faster
    public float swingsPerSecond;   // basic attacks a second with the weapon in hand — its own pace and aspdMult together
    public float castTimeMult; // the one cast-speed value: 1 = full wind-up, 0 = instant cast. LOWER is faster.
    public float guardCutPct;  // 0..1 incoming-damage cut while Guard is up (a state; 0 at rest)
    public float lifeStealPct; // fraction of damage dealt returned as HP
    public float reflectPct;   // worn words + any timed reflect window
    public float manaLeechPct; // fraction of damage dealt returned as MP (a state; 0 at rest)
    public int barrierHP;      // remaining Shield pool (a state; 0 at rest)
    public float basicAttackRange;   // how far a basic attack reaches, in world units — gear alone on the baseline
}
