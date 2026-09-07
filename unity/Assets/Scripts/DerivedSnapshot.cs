/// <summary>
/// An immutable readout of every derived stat the Character/Inventory windows show, computed
/// from a given equipment loadout. The LIVE panel reads <see cref="Character.CurrentDerived"/>;
/// the hover "equip this → ATK 220→248" preview reads <see cref="Character.DeriveWith"/> on a
/// HYPOTHETICAL set and diffs the two. Both go through the same Character formulas, so the
/// preview can never drift from real combat numbers.
/// </summary>
public struct DerivedSnapshot
{
    public int atkMin, atkMax;
    public int matkMin, matkMax;
    public int def, mdef;
    public int hit, flee;
    public int maxHP, maxMP;
    public int moveSpeed;
    public float critChance;   // 0..1
    public float critMult;     // e.g. 1.4 = 140%
    public float aspdMult;     // >1 = faster
    public float castTimeMult; // the one cast-speed value: 1 = full wind-up, 0 = instant cast. LOWER is faster.
    public int hardDefPct, hardMdefPct;   // flat % of incoming damage ignored, on top of the DEF subtraction
    public float guardCutPct;  // 0..1 incoming-damage cut while Guard is up (a state; 0 at rest)
    public float lifeStealPct; // fraction of damage dealt returned as HP
    public float reflectPct;   // permanent thorns + any timed reflect window
    public float manaLeechPct; // fraction of damage dealt returned as MP (a state; 0 at rest)
    public int barrierHP;      // remaining Shield pool (a state; 0 at rest)
    public float basicAttackRange;   // how far a basic attack reaches, in world units — gear alone on the baseline
}
