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
    [Tooltip("A field boss / MVP: excluded from the weighted field spawn; placed deliberately (Spawn Field Boss).")]
    public bool isBoss = false;
    [Tooltip("Body size for the DEFERRED weapon-vs-size matchup (WeaponSizes). Unused by combat in the slice; " +
             "Medium reproduces RO's neutral '100% everywhere' baseline, so leaving it default is safe.")]
    public MobSize size = MobSize.Medium;

    [Header("Biome & tier (spawn grouping)")]
    [Tooltip("Which biome this mob spawns in. The map generator spawns a map's mobs from its recipe Biome. " +
             "Set to None for a biome-agnostic 'common' (rat/bat/slime): eligible in ANY biome's tier band.")]
    public Biome biome = Biome.Grassland;
    [Range(1, 4), Tooltip("Difficulty tier within the biome: 1 (weakest) … 4 (elite / mini-boss).")]
    public int tier = 1;

    [Header("Art")]
    [Tooltip("Sprite-sheet folder under Assets/Art/NinjaAdventure/Actor/Monster (e.g. 'Slime').")]
    public string sheet = "";
    [Tooltip("Relative spawn frequency in a mixed field (commons higher). Bosses use 0.")]
    public int weight = 1;
    [Tooltip("SpriteRenderer tint (white = none).")]
    public Color tint = Color.white;
    [Tooltip("Size multiplier on the base billboard scale.")]
    public float scale = 1f;
    [Tooltip("Hit-flash color.")]
    public Color flash = Color.white;

    [Header("Survivability")]
    public int maxHealth = 12;
    [Tooltip("SOFT (flat) physical DEF.")]
    public int defense = 0;
    [Tooltip("SOFT (flat) magic DEF.")]
    public int magicDefense = 0;
    [Tooltip("RO FLEE: dodges some of the player's swings (red MISS).")]
    public int flee = 0;
    [Tooltip("Pre-renewal HARD DEF % (applied first; a crit ignores it).")]
    public int hardDefPercent = 0;
    [Tooltip("Pre-renewal HARD MDEF %.")]
    public int hardMagicDefPercent = 0;
    [Tooltip("Crit shield (lowers attacker crit).")]
    public int luk = 0;

    [Header("Offense")]
    public MonsterAI3D.AttackType attackType = MonsterAI3D.AttackType.Physical;
    public int attack = 3;
    public int hit = 8;
    public int xpReward = 5;
    [Range(0f, 1f)] public float damageSpread = 0.25f;
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

    /// <summary>Produce the runtime DTO the mob builders consume. The asset is the source of truth; this is
    /// a plain copy, so authoring a mob in the Inspector changes nothing about how it's stamped into a scene.</summary>
    public MonsterArchetype ToArchetype() => new MonsterArchetype
    {
        name = string.IsNullOrEmpty(displayName) ? name : displayName,
        sheet = sheet,
        weight = weight,
        tint = tint,
        scale = scale,
        flash = flash,
        maxHealth = maxHealth,
        defense = defense,
        magicDefense = magicDefense,
        flee = flee,
        hardDefPercent = hardDefPercent,
        hardMagicDefPercent = hardMagicDefPercent,
        luk = luk,
        attackType = attackType,
        attack = attack,
        hit = hit,
        xpReward = xpReward,
        damageSpread = damageSpread,
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
