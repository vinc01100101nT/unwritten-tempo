using UnityEngine;

/// <summary>
/// A mob, as editable data instead of a hardcoded array entry. Create one per mob via
/// Assets ▸ Create ▸ unwritten ▸ Monster (or migrate the legacy <c>MobRoster</c> seed list with
/// Tools ▸ unwritten ▸ Quick Fixes ▸ Migrate MobRoster → Assets). Assets live in <c>Resources/Monsters</c>
/// so <see cref="MonsterCatalog"/> can load them at runtime and so quests/spawners can reference a mob by
/// its stable <see cref="id"/> — never by display name (which can be renamed).
///
/// This mirrors <see cref="MonsterArchetype"/> field-for-field; <see cref="ToArchetype"/> produces the
/// struct the builders already consume, so nothing about how a mob is stamped into the scene changes.
/// </summary>
[CreateAssetMenu(fileName = "Monster", menuName = "unwritten/Monster", order = 1)]
public class MonsterDef : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable kebab id, e.g. 'mob.slime'. Quests/spawners reference THIS, never the display name. Must be unique.")]
    public string id = "mob.new";
    [Tooltip("In-game name shown on the nameplate (the display name; can be renamed freely without breaking references).")]
    public string displayName = "Monster";
    [Tooltip("Normal fills the field and is drawn into a map's mix. Elite and Shadow are never drawn into a mix — a " +
             "row of the world spawn list names them. The kind sets extra skill slots, summoning, gear, respawn and drops " +
             "(see CreatureKinds); strength still comes from level and stats alone.")]
    public CreatureKind kind = CreatureKind.Normal;
    [Tooltip("Body size for the DEFERRED weapon-vs-size matchup (WeaponSizes). Unused by combat in the slice; " +
             "Medium reproduces RO's neutral '100% everywhere' baseline, so leaving it default is safe.")]
    public MobSize size = MobSize.Medium;

    [Header("Biome (spawn grouping)")]
    [Tooltip("Which biome this mob spawns in. The map generator spawns a map's mobs from its recipe Biome. " +
             "Set to None for a biome-agnostic 'common' (rat/bat/slime): eligible in ANY biome's level band.")]
    public Biome biome = Biome.Grassland;
    // Difficulty is LEVEL — there is no separate tier. A zone draws mobs from a level band, and how dangerous a mob
    // is falls out of its level + stats through the shared formulas, exactly as it does for the player.

    [Header("Art")]
    [Tooltip("Sprite-sheet folder under Assets/1_Authored/Art/Billboards/Actor/Monster (e.g. 'Slime').")]
    public string sheet = "";
    [Tooltip("Relative spawn frequency in a mixed field (commons higher). Bosses use 0.")]
    public int weight = 1;
    [Tooltip("SpriteRenderer tint (white = none).")]
    public Color tint = Color.white;
    [Tooltip("Size multiplier on the base billboard scale.")]
    public float scale = 1f;

    // ---- THE stat block: identical in shape and formula to the player's (design doc "Unified Unit Stats") -----------
    // A mob is authored as a LEVEL and six PRIMARIES; every combat number below is DERIVED from them through
    // UnitStatMath — the same functions Character runs. There is no mob-only tier or multiplier: a mob is dangerous
    // for the same reasons a player is (its level, its stats, and eventually its gear).
    [Header("Stats (level + primaries — derived exactly like the player)")]
    [Tooltip("Character level. Drives the base HP/ATK curve and feeds HIT/FLEE directly, exactly as it does for the player.")]
    public int level = 1;
    [Range(1, UnitStatMath.MaxStatValue)] public int str = 1;
    [Range(1, UnitStatMath.MaxStatValue)] public int agi = 1;
    [Range(1, UnitStatMath.MaxStatValue)] public int vit = 1;
    [Range(1, UnitStatMath.MaxStatValue)] public int intel = 1;
    [Range(1, UnitStatMath.MaxStatValue)] public int dex = 1;
    [Tooltip("LUK — feeds crit chance, and doubles as the pre-renewal CRIT SHIELD against attackers.")]
    [Range(1, UnitStatMath.MaxStatValue)] public int luk = 1;

    [Header("Equipment — the SAME items the player wears")]
    [Tooltip("Gear this creature spawns wearing, one piece per slot (the slot comes from the Item itself). Every " +
             "piece is Raw and unfolded, the same as one straight out of a forge, and none of it drops: a creature " +
             "gives up materials, consumables and gold, never what it is wearing.")]
    public Item[] startingGear = System.Array.Empty<Item>();
    [Tooltip("Which stats this creature's ring has invested in. Parallel to Ring Rank Values. The ring is part of "+
             "the creature, so these always count — nothing has to be worn.")]
    public StatId[] ringRankStats = System.Array.Empty<StatId>();
    [Tooltip("Ranks invested in each stat above. Converted to a % through the SAME soft-cap curve the player's ring " +
             "uses (SkillRing.Percent), so a mob's ring is worth exactly what a player's ring of equal ranks is worth.")]
    public int[] ringRankValues = System.Array.Empty<int>();

    [Tooltip("How practised this creature is with the weapon it holds — the same Weapon Mastery a player earns, " +
             "authored rather than trained. It raises that weapon's three stats by 0.5% a level and lifts what " +
             "its passives do. 0 = never picked the thing up.")]
    public int weaponMasteryLevel;

    [Header("Survivability (armour-sourced; not derived from primaries)")]
    [Tooltip("Pre-renewal HARD DEF % (applied first; a crit ignores it). Comes from armour, so it stays authored.")]
    public int hardDefPercent = 0;
    [Tooltip("Pre-renewal HARD MDEF %.")]
    public int hardMagicDefPercent = 0;

    // (Removed 2026-08-10: the hand-authored maxHealth/attack/defense/magicDefense/hit/flee/damageSpread fields.
    //  Every one of them is now DERIVED in ToArchetype through UnitStatMath from the level + primaries above, so
    //  storing them was a second copy that could disagree with the formulas. The reset migration doesn't preserve
    //  old values, so there was nothing left for them to carry.)

    [Header("Offense")]
    public int xpReward = 5;
    public float touchRange = 0.9f;
    public float touchInterval = 1.0f;

    [Header("Senses / movement")]
    public float moveSpeed = 2.2f;
    public float aggroRadius = 6f;
    public float leashRadius = 10f;
    public float stopDistance = 0.7f;
    public float wanderRadius = 3f;

    [Header("Behavior style")]
    [Tooltip(">0 = caster/archer: kites and holds at this range.")]
    public float preferredRange = 0f;
    [Tooltip(">0 = skittish: flees when wounded below this HP fraction.")]
    [Range(0f, 1f)] public float fleeHpFraction = 0f;

    [Header("Element & status (RO)")]
    [Tooltip("This mob's ARMOR property — what the player's attacks are checked against.")]
    public Element element = Element.Neutral;
    [Tooltip("This mob's OUTGOING property — checked vs the player's armor.")]
    public Element attackElement = Element.Neutral;
    [Tooltip("Ailment inflicted on the player when a hit lands. Chance 0 below = inflicts nothing (the type is ignored).")]
    public Ailment onHitAilment = Ailment.Poison;
    [Range(0f, 1f)] public float onHitAilmentChance = 0f;
    public float onHitAilmentSeconds = 0f;
    public float onHitAilmentMagnitude = 0f;

    [Header("Loot")]
    public int goldMin = 0;
    public int goldMax = 0;
    [Tooltip("Item drops. Each entry is rolled independently on death. Names must match a Resources/Items/*.asset.")]
    public string[] itemDropNames = System.Array.Empty<string>();
    [Tooltip("Drop chance (0–1) parallel to Item Drop Names.")]
    public float[] itemDropChances = System.Array.Empty<float>();

    /// <summary>This mob's stat inputs — level, primaries and the shared base/growth curve. The ONE thing its combat
    /// numbers are computed from, and the same struct the player fills.</summary>
    public UnitStatInputs StatInputs
    {
        get
        {
            var u = UnitStatInputs.Default(level);
            u.str = str; u.agi = agi; u.vit = vit; u.intel = intel; u.dex = dex; u.luk = luk;
            return u;
        }
    }

    /// <summary>Produce the runtime DTO the mob builders consume. Combat numbers are DERIVED here through
    /// <see cref="UnitStatMath"/> — the player's formulas — so a mob and a player of the same level and primaries
    /// have the same stats. Worn gear and the ring are not folded in here: the spawned mob's
    /// <see cref="UnitStatBlock"/> adds them.</summary>
    public MonsterArchetype ToArchetype()
    {
        var u = StatInputs;
        return new MonsterArchetype
        {
            // The stat block is ALL that's carried for combat numbers — UnitStatBlock derives HP/ATK/DEF/HIT/FLEE
            // from it on the spawned mob, and re-derives whenever something feeding it changes (see UnitStatBlock). Nothing is snapshotted
            // here, so there is no second copy able to disagree with the formulas.
            stats = u,
            startingGear = startingGear,
            ringRankStats = ringRankStats,
            ringRankValues = ringRankValues,
            weaponMasteryLevel = weaponMasteryLevel,
            name = string.IsNullOrEmpty(displayName) ? name : displayName,
            sheet = sheet,
            weight = weight,
            tint = tint,
            scale = scale,
            hardDefPercent = hardDefPercent,
            hardMagicDefPercent = hardMagicDefPercent,
            xpReward = xpReward,
            touchRange = touchRange,
            touchInterval = touchInterval,
            moveSpeed = moveSpeed,
            aggroRadius = aggroRadius,
            leashRadius = leashRadius,
            stopDistance = stopDistance,
            wanderRadius = wanderRadius,
            preferredRange = preferredRange,
            fleeHpFraction = fleeHpFraction,
            element = element,
            attackElement = attackElement,
            onHitAilment = onHitAilment,
            onHitAilmentChance = onHitAilmentChance,
            onHitAilmentSeconds = onHitAilmentSeconds,
            onHitAilmentMagnitude = onHitAilmentMagnitude,
            goldMin = goldMin,
            goldMax = goldMax,
            itemDropNames = itemDropNames,
            itemDropChances = itemDropChances,
        };
    }
}
