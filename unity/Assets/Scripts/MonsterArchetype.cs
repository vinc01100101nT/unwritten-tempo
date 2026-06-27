using UnityEngine;

/// <summary>
/// Plain config the mob builder stamps onto the shared billboard-mob template — its own sprite sheet,
/// tint, size, stats and behavior style. This is a runtime value type (not an asset): it's the DTO the
/// editor builders pass into <c>Build25DSpike.BuildMonster</c>. The authoring/table layer is
/// <see cref="MonsterDef"/> (a ScriptableObject) which converts to one of these via
/// <see cref="MonsterDef.ToArchetype"/>; the legacy seed list lives in <c>MobRoster</c> (Editor).
///
/// The two behavior axes (kiting via <c>preferredRange</c>, fleeing via <c>fleeHpFraction</c>) are read by
/// <see cref="MonsterAI3D"/>; everything else is HP / damage / senses already on Health + MonsterAI3D.
/// Nothing here runs per-frame, so adding variety costs zero frame budget — mobs differ in data, not code.
/// </summary>
public struct MonsterArchetype
{
    public string name;
    public string sheet;        // folder under Assets/Art/NinjaAdventure/Actor/Monster (e.g. "Slime")
    public int    weight;       // relative spawn frequency in a mixed field
    public Color  tint;         // SpriteRenderer tint (white = none)
    public float  scale;        // size multiplier on the base billboard scale
    public Color  flash;        // hit-flash color

    // Survivability. `defense`/`magicDefense` are SOFT (flat) DEF; `hardDefPercent`/`hardMagicDefPercent`
    // are pre-renewal HARD DEF (% reduction, applied first — a crit ignores both). `luk` = crit shield.
    public int maxHealth, defense, magicDefense, flee;
    public int hardDefPercent, hardMagicDefPercent, luk;

    // Offense.
    public MonsterAI3D.AttackType attackType;
    public int   attack, hit, xpReward;
    public float damageSpread, touchRange, touchInterval;

    // Senses / movement.
    public float moveSpeed, aggroRadius, leashRadius, stopDistance, wanderRadius;

    // Behavior style (see MonsterAI3D): 0 preferredRange = melee; fleeHpFraction 0 = never flee.
    public float preferredRange, fleeHpFraction;

    // Element & status (RO). `element` = this mob's ARMOR property (what the player's attacks are checked
    // against — Fire vs Earth = Weak!, etc.); `attackElement` = its OUTGOING property. On-hit ailment is
    // inflicted on the player when a hit lands (chance 0 = this mob inflicts no status).
    public Element element, attackElement;
    public Ailment onHitAilment;
    public float onHitAilmentChance, onHitAilmentSeconds, onHitAilmentMagnitude;

    // Loot (Zeny drop on death). goldDropChance defaults to 0.85 in LootDropper if both are 0.
    public int goldMin, goldMax;

    // Item drops: parallel arrays — name must match a Resources/Items/*.asset displayName (without .asset).
    // Each entry is rolled independently on death. Leave null/empty for no item drops.
    public string[] itemDropNames;
    public float[]  itemDropChances;
}
