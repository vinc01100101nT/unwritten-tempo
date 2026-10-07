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
             "row of the world spawn list names them. The kind sets how many summons it keeps, gear, respawn and drops " +
             "(see CreatureKinds); strength still comes from level and stats alone.")]
    public CreatureKind kind = CreatureKind.Normal;
    [Tooltip("Body size: how many times a character's height it stands, 1 being a character. Its melee reach counts " +
             "the same number. Under 0.6 it is Small, 0.6 to 1.5 Medium, over 1.5 Large; 0.3 to 5.2 — nothing is " +
             "smaller than Plopling, 0.3. A Shadow is always 1 whatever this says.")]
    public float size = MobSizes.Character;
    [Tooltip("Flies: its picture is drawn in the air, the middle of its body level with a character's head, and its " +
             "nameplate, numbers, aim point and click spot rise with it. Its walking body stays on the ground, so where " +
             "it can go and how far it reaches are unchanged. A creature twice a character's height or taller is not " +
             "raised. A Shadow never flies.")]
    public bool flies;

    [Header("Biome (spawn grouping)")]
    [Tooltip("Which biome this mob spawns in. The map generator spawns a map's mobs from its recipe Biome. " +
             "Set to None for a biome-agnostic 'common' (rat/bat/slime): eligible in ANY biome's level band.")]
    public Biome biome = Biome.Grassland;
    // Difficulty is LEVEL — there is no separate tier. A zone draws mobs from a level band, and how dangerous a mob
    // is falls out of its level + stats through the shared formulas, exactly as it does for the player.

    [Header("Art")]
    [Tooltip("The folder its pictures are in. Kept by Unity's own link to the folder, so renaming or moving the " +
             "folder changes nothing here.")]
    [FolderLink] public string artFolder = "";
    [Tooltip("Its portrait — the face drawn beside its name when you point at it or fight it. Empty = none (a shadow).")]
    public Texture2D faceset;
    [Tooltip("Relative spawn frequency in a mixed field (commons higher). Bosses use 0.")]
    public int weight = 1;
    [Tooltip("SpriteRenderer tint (white = none).")]
    public Color tint = Color.white;

    // ---- A SHADOW'S LOOK: the body it wears and its outfit colours, in the same shape a player's look is saved in --
    // (a model name, then each changed part's original colour and its chosen colour as "#RRGGBB", index-matched).
    // Hidden from the Inspector on purpose: they mean nothing on any kind that is not drawn as a character, and a
    // free-text box would accept a body that does not exist. The Mob Table is the one place they are edited, and
    // tools/check-shadow-looks.py fails any shadow whose body is missing or whose colours are not that body's parts.
    /// <summary>Which body this creature wears (a name in <see cref="CharacterModels"/>' roster). Shadows only.</summary>
    [HideInInspector] public string modelName = "";
    /// <summary>The original colour of each part this creature's look changes, "#RRGGBB". Index-matched with
    /// <see cref="dyeTargets"/>.</summary>
    [HideInInspector] public string[] dyeSources = System.Array.Empty<string>();
    /// <summary>The chosen colour for each part in <see cref="dyeSources"/>, "#RRGGBB".</summary>
    [HideInInspector] public string[] dyeTargets = System.Array.Empty<string>();

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
             "piece comes to life at the three settings below, and none of it drops: a creature gives up materials " +
             "and consumables, never what it is wearing.")]
    public Item[] startingGear = System.Array.Empty<Item>();
    [Tooltip("THE GRADE every piece this creature wears comes to life at — Raw through A, the same ladder a quench " +
             "climbs. It decides the fold cap and HOW MANY stats Ink writes on each piece (Raw none, F one, up to A six).")]
    public Quality gearQuality = Quality.Raw;
    [Tooltip("How many times every piece this creature wears is folded. Each fold is the piece's own attack or " +
             "defence. Never past the grade's cap: Raw 5, F 10, E 15, D 20, C 25, B 30, A 35.")]
    public int gearFolds;
    [Tooltip("Which Ink writes the stats on every piece this creature wears — 1 Watery, 2 Thin, 3 Faint, 4 Clear, " +
             "5 Sharp. WHICH stats are written is rolled fresh each life, exactly as a station rolls one.")]
    [Range(1, 5)] public int gearInkTier = 1;
    [Tooltip("This creature's Weapon Mastery in the ONE weapon it holds — the same mastery a player earns, put on that " +
             "weapon when the creature is made. It raises that weapon's three stats by 0.5% a level, counting only " +
             "what the weapon's grade allows. A creature has mastery in one weapon only, so with no weapon it is 0.")]
    public int weaponMastery;
    [Tooltip("Which stats this creature's ring has invested in. Parallel to Ring Rank Values. The ring is part of "+
             "the creature, so these always count — nothing has to be worn.")]
    public StatId[] ringRankStats = System.Array.Empty<StatId>();
    [Tooltip("Ranks invested in each stat above. Converted to a % through the SAME soft-cap curve the player's ring " +
             "uses (SkillRing.Percent), so a mob's ring is worth exactly what a player's ring of equal ranks is worth.")]
    public int[] ringRankValues = System.Array.Empty<int>();

    /// <summary>The weapon this creature holds: the last weapon piece in its gear, which is the one it wears (one piece
    /// per slot, the last one authored for a slot wins). Null when its hands are empty.</summary>
    public Item Weapon
    {
        get
        {
            Item held = null;
            if (startingGear != null)
                for (int i = 0; i < startingGear.Length; i++)
                    if (startingGear[i] != null && startingGear[i].slot == EquipSlotType.Weapon) held = startingGear[i];
            return held;
        }
    }

    /// <summary>The mastery that actually goes on its weapon: what is authored, or 0 with no weapon to put it on.</summary>
    public int MasteryOnWeapon => Weapon != null ? UnityEngine.Mathf.Max(0, weaponMastery) : 0;

    // NO SUB-STAT IS TYPED ON A CREATURE. Health, attack, defence, accuracy, evasion, speed, reach and swing timing
    // are all worked out from the level, primaries, gear and ring above, or read from the one shared character spec
    // — exactly as a person's are. A number typed here would be a second answer that could disagree with the formulas.

    [Header("Senses")]
    public float aggroRadius = 6f;
    public float leashRadius = 10f;
    public float wanderRadius = 3f;

    [Header("Behavior style")]
    [Tooltip(">0 = caster/archer: kites and holds at this range.")]
    public float preferredRange = 0f;
    [Tooltip(">0 = skittish: flees when wounded below this HP fraction.")]
    [Range(0f, 1f)] public float fleeHpFraction = 0f;

    [Header("Element & status (RO)")]
    [Tooltip("This mob's element — what its ordinary attacks land as, AND what it is hit as. A creature attacks and " +
             "defends with its nature.")]
    public Element element = Element.Neutral;
    [Tooltip("How strongly its element resists and is weak, 1 to 4 (Ragnarok's element levels). Only the defending " +
             "side has a level — an attack never does. Level 3 and 4 heal from their own element.")]
    [Range(1, 4)] public int elementLevel = 1;
    [Tooltip("Ailment inflicted on the player when a hit lands. Chance 0 below = inflicts nothing (the type is ignored).")]
    public Ailment onHitAilment = Ailment.Poison;
    [Range(0f, 1f)] public float onHitAilmentChance = 0f;
    public float onHitAilmentSeconds = 0f;
    public float onHitAilmentMagnitude = 0f;

    [Header("Loot")]
    [Tooltip("The gold range a saved character that never fights pays its saver, before its tier multiplies it. A death " +
             "itself pays no gold.")]
    public int goldMin = 0;
    [Tooltip("The top of that range.")]
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
            gearQuality = gearQuality,
            gearFolds = gearFolds,
            gearInkTier = gearInkTier,
            ringRankStats = ringRankStats,
            ringRankValues = ringRankValues,
            name = string.IsNullOrEmpty(displayName) ? name : displayName,
            artFolder = artFolder,
            weight = weight,
            tint = tint,
            kind = kind,
            size = size,
            flies = flies,
            aggroRadius = aggroRadius,
            leashRadius = leashRadius,
            wanderRadius = wanderRadius,
            preferredRange = preferredRange,
            fleeHpFraction = fleeHpFraction,
            element = element,
            onHitAilment = onHitAilment,
            onHitAilmentChance = onHitAilmentChance,
            onHitAilmentSeconds = onHitAilmentSeconds,
            onHitAilmentMagnitude = onHitAilmentMagnitude,
            itemDropNames = itemDropNames,
            itemDropChances = itemDropChances,
        };
    }
}
