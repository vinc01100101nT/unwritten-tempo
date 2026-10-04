using UnityEngine;

/// <summary>
/// Plain config the mob builder stamps onto the shared billboard-mob template — its own sprite sheet,
/// tint, size, stats and behavior style. This is a runtime value type (not an asset): it's the DTO the
/// editor builders pass into <c>RigBuilder25D.BuildMonster</c>. The authoring/table layer is
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
    public string artFolder;    // Unity's id for the art folder (see MonsterDef.artFolder); empty = the caller brings the picture
    public int    weight;       // relative spawn frequency in a mixed field
    public Color  tint;         // SpriteRenderer tint (white = none)
    public CreatureKind kind;   // a shadow's body is a 3D character, always size 1
    public float  size;         // body size — times a character's height, and how far its melee reaches (MobSizes)

    // THE stat block, carried through to the spawned mob so it can RE-DERIVE on level-up instead of being scaled
    // by a mob-only growth rate. The derived fields below are the values at THIS level — a convenience snapshot for
    // the builder; UnitStatBlock recomputes them from these inputs whenever level or gear changes.
    public UnitStatInputs stats;

    // What the mob SPAWNS WEARING, plus its ring's invested ranks. Nothing worn is ever dropped (see LootDropper).
    public Item[] startingGear;
    /// <summary>The grade, folds and Ink strength every worn piece comes to life at — the creature's own three
    /// settings, carried through untouched.</summary>
    public Quality gearQuality;
    public int gearFolds;
    public int gearInkTier;
    public StatId[] ringRankStats;
    public int[] ringRankValues;

    // No sub-stat is carried here: health, defence, evasion, attack, accuracy and spread are DERIVED from `stats` by
    // UnitStatBlock on the spawned mob, and speed, reach and swing timing are the shared character spec's.

    // Senses.
    public float aggroRadius, leashRadius, wanderRadius;

    // Behavior style (see MonsterAI3D): 0 preferredRange = melee; fleeHpFraction 0 = never flee.
    public float preferredRange, fleeHpFraction;

    // Element & status (RO). `element` = this mob's element, which its ordinary attacks carry AND which it is hit as
    // (Fire vs Earth = Weak!, etc.). Its level is not carried here: MobSpawner copies it from the creature data onto
    // Health at spawn. On-hit ailment is inflicted on the player when a hit lands (chance 0 = this mob inflicts no
    // status).
    public Element element;
    public Ailment onHitAilment;
    public float onHitAilmentChance, onHitAilmentSeconds, onHitAilmentMagnitude;

    // Item drops: parallel arrays — name must match a Resources/Items/*.asset displayName (without .asset).
    // Each entry is rolled independently on death. Leave null/empty for no item drops.
    public string[] itemDropNames;
    public float[]  itemDropChances;
}
