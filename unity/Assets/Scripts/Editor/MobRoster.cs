using UnityEngine;

/// <summary>
/// Author-time enemy SEED list (M1 — combat-feel + variety). These are the original hardcoded archetypes;
/// the live source of truth is now <see cref="MonsterDef"/> assets in <c>Resources/Monsters</c>, surfaced
/// through <see cref="MonsterCatalog"/>. This class is kept as (a) the data the one-click migration
/// (<c>Tools ▸ unwritten ▸ Quick Fixes ▸ Migrate MobRoster → Assets</c>) materializes into those assets,
/// and (b) a fallback so generation still works before the migration has been run. Once the assets are the
/// confirmed source of truth, this can be deleted.
///
/// The <see cref="MonsterArchetype"/> struct it returns now lives in runtime (<c>MonsterArchetype.cs</c>).
/// </summary>
public static class MobRoster
{
    static readonly Color None  = Color.white;
    static readonly Color Arcane = new Color(0.62f, 0.72f, 1f);   // the old "mystic" blue, now the caster's
    static readonly Color Cyan  = new Color(0.78f, 0.95f, 1f);

    /// <summary>The five starter archetypes. Weights bias the field toward the two commons.</summary>
    public static readonly MonsterArchetype[] All =
    {
        // 1) GRUNT — the baseline melee slime. Common.
        new MonsterArchetype {
            stats = UnitStatMath.Allocate(1, wStr: 3f, wAgi: 1f, wVit: 3f, wInt: 1f, wDex: 2f, wLuk: 1f),   // fodder bruiser
            name = "Slime", sheet = "Slime", weight = 4, tint = None, scale = 1.0f, flash = Color.white,
            hardDefPercent = 5, hardMagicDefPercent = 0,   // fodder: a sliver of DEF, no crit shield
            xpReward = 5,
            touchRange = 0.9f, touchInterval = 1.0f,
            moveSpeed = 2.2f, aggroRadius = 6f, leashRadius = 10f, stopDistance = 0.7f, wanderRadius = 3f,
            preferredRange = 0f, fleeHpFraction = 0f,
            element = Element.Earth, attackElement = Element.Neutral,   // Fireball melts it (Fire→Earth = Weak!)
            goldMin = 2, goldMax = 6,
            itemDropNames = new[]{ "Life Potion" }, itemDropChances = new[]{ 0.08f },
        },

        // 2) SWARMER — fast, fragile, dodgy harasser. Common; comes in numbers.
        new MonsterArchetype {
            stats = UnitStatMath.Allocate(2, wStr: 2f, wAgi: 5f, wVit: 1f, wInt: 1f, wDex: 2f, wLuk: 2f),   // fast + dodgy
            name = "Bat", sheet = "BlueBat", weight = 4, tint = Cyan, scale = 0.78f, flash = Color.white,
            hardDefPercent = 0, hardMagicDefPercent = 0,   // no DEF (dodges instead); lucky → some crit shield
            xpReward = 3,
            touchRange = 0.8f, touchInterval = 0.8f,
            moveSpeed = 3.6f, aggroRadius = 7f, leashRadius = 12f, stopDistance = 0.6f, wanderRadius = 4f,
            preferredRange = 0f, fleeHpFraction = 0f,
            element = Element.Wind, attackElement = Element.Neutral,   // Frost (Water→Wind) is Resisted on bats
            goldMin = 1, goldMax = 3,
            itemDropNames = new[]{ "Silver Ring" }, itemDropChances = new[]{ 0.05f },
        },

        // 3) BRUISER — slow, tanky, heavy hits. Uncommon; a real threat one-on-one.
        new MonsterArchetype {
            stats = UnitStatMath.Allocate(6, wStr: 3f, wAgi: 1f, wVit: 5f, wInt: 1f, wDex: 2f, wLuk: 1f),   // tanky
            name = "Kappa", sheet = "KappaGreen", weight = 2, tint = None, scale = 1.15f, flash = Color.white,
            hardDefPercent = 25, hardMagicDefPercent = 8,   // TANK: % DEF cuts normal hits hard — crit builds shine here
            xpReward = 12,
            touchRange = 1.0f, touchInterval = 1.4f,
            moveSpeed = 1.6f, aggroRadius = 5f, leashRadius = 9f, stopDistance = 0.8f, wanderRadius = 2.5f,
            preferredRange = 0f, fleeHpFraction = 0f,
            element = Element.Water, attackElement = Element.Neutral,   // water turtle: Fire is Resisted (use melee/wind)
            goldMin = 5, goldMax = 14,
            itemDropNames = new[]{ "Wooden Guard", "Leather Vest" }, itemDropChances = new[]{ 0.12f, 0.08f },
        },

        // 4) CASTER — holds JUST outside melee and attacks from there. Uncommon. (Its ordinary attack is physical, like every unit's.)
        // Tuned to be catchable: it kites only when you're inside ~2.6u, holds & casts from ~3.2u, and the
        // player (speed 4) closes the gap in ~1s. A real telegraphed bolt is the follow-up that sells it.
        new MonsterArchetype {
            stats = UnitStatMath.Allocate(5, wStr: 1f, wAgi: 2f, wVit: 2f, wInt: 5f, wDex: 2f, wLuk: 1f),   // caster
            name = "Flam", sheet = "Flam", weight = 2, tint = Arcane, scale = 0.95f, flash = Color.white,
            hardDefPercent = 0, hardMagicDefPercent = 10,   // squishy to melee; a caster's own magic resist
            xpReward = 10,
            touchRange = 3.6f, touchInterval = 1.6f,
            moveSpeed = 2.2f, aggroRadius = 7f, leashRadius = 11f, stopDistance = 0.7f, wanderRadius = 3f,
            preferredRange = 3.2f, fleeHpFraction = 0f,
            element = Element.Fire, attackElement = Element.Fire,   // flame: Frost (Water→Fire) = Weak!; its bolt BURNS
            onHitAilment = Ailment.Burn, onHitAilmentChance = 0.4f, onHitAilmentSeconds = 4f, onHitAilmentMagnitude = 2f,
            goldMin = 4, goldMax = 10,
            itemDropNames = new[]{ "Fire Scroll" }, itemDropChances = new[]{ 0.20f },
        },

        // 5) DARTER — hit-and-run; flees once wounded below 40% HP. Uncommon.
        new MonsterArchetype {
            stats = UnitStatMath.Allocate(8, wStr: 5f, wAgi: 1f, wVit: 3f, wInt: 1f, wDex: 2f, wLuk: 1f),   // heavy hitter
            name = "Cyclope", sheet = "Cyclope", weight = 2, tint = None, scale = 0.9f, flash = Color.white,
            hardDefPercent = 5, hardMagicDefPercent = 0,   // evasive hit-and-run: high crit shield + dodge
            xpReward = 8,
            touchRange = 0.9f, touchInterval = 1.0f,
            moveSpeed = 3.2f, aggroRadius = 6f, leashRadius = 11f, stopDistance = 0.7f, wanderRadius = 4f,
            preferredRange = 0f, fleeHpFraction = 0.4f,
            element = Element.Neutral, attackElement = Element.Neutral,   // plain baseline — but its hit-and-run bite POISONS
            onHitAilment = Ailment.Poison, onHitAilmentChance = 0.35f, onHitAilmentSeconds = 5f, onHitAilmentMagnitude = 2f,
            goldMin = 3, goldMax = 8,
            itemDropNames = new[]{ "Jade Amulet" }, itemDropChances = new[]{ 0.10f },
        },
    };

    /// <summary>The field BOSS (Ragnarok MVP style): a big, slow, tanky unique that roams the field and
    /// hits hard. NOT in <see cref="All"/>, so the weighted field spawner never rolls it — it's placed once
    /// by Tools ▸ unwritten ▸ Region ▸ Spawn Field Boss. Pair it with a <see cref="BossBanner"/> (name + HP
    /// bar) and a long respawn so it feels like an event, not trash.</summary>
    public static readonly MonsterArchetype Boss = new MonsterArchetype
    {
        stats = UnitStatMath.Allocate(30, wStr: 3f, wAgi: 1f, wVit: 4f, wInt: 2f, wDex: 2f, wLuk: 1f),   // boss
        name = "Ancient Dragon", sheet = "Dragon", weight = 0,
        tint = new Color(1f, 0.85f, 0.85f), scale = 2.3f, flash = new Color(1f, 0.4f, 0.3f),
        // A real wall of HP + real defenses so the whole kit (multi-hit, status, crit) matters.
        hardDefPercent = 30, hardMagicDefPercent = 20,   // WALL: heavy % DEF + strong crit shield — crit/element/status all matter

        xpReward = 400,
            touchRange = 1.6f, touchInterval = 1.5f,
        // Slow and relentless: wide aggro, long leash so it commits to the chase, never flees.
        moveSpeed = 1.7f, aggroRadius = 9f, leashRadius = 22f, stopDistance = 1.1f, wanderRadius = 5f,
        preferredRange = 0f, fleeHpFraction = 0f,
        // Fire dragon: resists Fire, MELTS to Frost (Water→Fire = Weak!), and its bite BURNS you.
        element = Element.Fire, attackElement = Element.Fire,
        onHitAilment = Ailment.Burn, onHitAilmentChance = 0.5f, onHitAilmentSeconds = 5f, onHitAilmentMagnitude = 4f,
        goldMin = 500, goldMax = 1000,
        itemDropNames   = new[]{ "Katana", "Iron Sword", "Boots" },
        itemDropChances = new[]{ 0.50f,    0.35f,        0.70f   },
    };

    /// <summary>Pick an archetype weighted by <see cref="MonsterArchetype.weight"/> (commons more often).</summary>
    public static MonsterArchetype WeightedPick()
    {
        int total = 0;
        foreach (var a in All) total += Mathf.Max(0, a.weight);
        if (total <= 0) return All[0];

        int roll = Random.Range(0, total);
        foreach (var a in All)
        {
            roll -= Mathf.Max(0, a.weight);
            if (roll < 0) return a;
        }
        return All[All.Length - 1];
    }
}
