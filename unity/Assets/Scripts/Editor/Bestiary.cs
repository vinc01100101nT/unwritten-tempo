using UnityEngine;

/// <summary>
/// Author-time enemy roster (M1 — combat-feel + variety). Each <see cref="MonsterArchetype"/> is plain
/// config that <see cref="Build25DSpike.BuildMonster(in MonsterArchetype, Vector3)"/> stamps onto the
/// shared billboard-mob template — its own Ninja Adventure sprite sheet, tint, size, stats and behavior
/// style. Nothing here runs at play time, so adding variety costs zero frame budget: the mobs differ in
/// data, not in per-frame code.
///
/// The two behavior axes (kiting via <c>preferredRange</c>, fleeing via <c>fleeHpFraction</c>) are read by
/// <see cref="MonsterAI3D"/>; everything else is HP / damage / senses already on Health + MonsterAI3D.
/// </summary>
public struct MonsterArchetype
{
    public string name;
    public string sheet;        // folder under Assets/Art/NinjaAdventure/Actor/Monster (e.g. "Slime")
    public int    weight;       // relative spawn frequency in a mixed field
    public Color  tint;         // SpriteRenderer tint (white = none)
    public float  scale;        // size multiplier on the base billboard scale
    public Color  flash;        // hit-flash color

    // Survivability.
    public int maxHealth, defense, magicDefense, flee;

    // Offense.
    public MonsterAI3D.AttackType attackType;
    public int   attack, hit, xpReward;
    public float damageSpread, touchRange, touchInterval;

    // Senses / movement.
    public float moveSpeed, aggroRadius, leashRadius, stopDistance, wanderRadius;

    // Behavior style (see MonsterAI3D): 0 preferredRange = melee; fleeHpFraction 0 = never flee.
    public float preferredRange, fleeHpFraction;
}

public static class Bestiary
{
    static readonly Color None  = Color.white;
    static readonly Color Arcane = new Color(0.62f, 0.72f, 1f);   // the old "mystic" blue, now the caster's
    static readonly Color Cyan  = new Color(0.78f, 0.95f, 1f);

    /// <summary>The five starter archetypes. Weights bias the field toward the two commons.</summary>
    public static readonly MonsterArchetype[] All =
    {
        // 1) GRUNT — the baseline melee slime. Common.
        new MonsterArchetype {
            name = "Slime", sheet = "Slime", weight = 4, tint = None, scale = 1.0f, flash = Color.white,
            maxHealth = 12, defense = 0, magicDefense = 0, flee = 8,
            attackType = MonsterAI3D.AttackType.Physical, attack = 3, hit = 8, xpReward = 5,
            damageSpread = 0.25f, touchRange = 0.9f, touchInterval = 1.0f,
            moveSpeed = 2.2f, aggroRadius = 6f, leashRadius = 10f, stopDistance = 0.7f, wanderRadius = 3f,
            preferredRange = 0f, fleeHpFraction = 0f,
        },

        // 2) SWARMER — fast, fragile, dodgy harasser. Common; comes in numbers.
        new MonsterArchetype {
            name = "Bat", sheet = "BlueBat", weight = 4, tint = Cyan, scale = 0.78f, flash = Color.white,
            maxHealth = 6, defense = 0, magicDefense = 0, flee = 16,
            attackType = MonsterAI3D.AttackType.Physical, attack = 2, hit = 9, xpReward = 3,
            damageSpread = 0.3f, touchRange = 0.8f, touchInterval = 0.8f,
            moveSpeed = 3.6f, aggroRadius = 7f, leashRadius = 12f, stopDistance = 0.6f, wanderRadius = 4f,
            preferredRange = 0f, fleeHpFraction = 0f,
        },

        // 3) BRUISER — slow, tanky, heavy hits. Uncommon; a real threat one-on-one.
        new MonsterArchetype {
            name = "Kappa", sheet = "KappaGreen", weight = 2, tint = None, scale = 1.15f, flash = Color.white,
            maxHealth = 30, defense = 3, magicDefense = 1, flee = 4,
            attackType = MonsterAI3D.AttackType.Physical, attack = 6, hit = 7, xpReward = 12,
            damageSpread = 0.2f, touchRange = 1.0f, touchInterval = 1.4f,
            moveSpeed = 1.6f, aggroRadius = 5f, leashRadius = 9f, stopDistance = 0.8f, wanderRadius = 2.5f,
            preferredRange = 0f, fleeHpFraction = 0f,
        },

        // 4) CASTER — holds JUST outside melee and lobs magic (hits MagicDefense, can't be dodged). Uncommon.
        // Tuned to be catchable: it kites only when you're inside ~2.6u, holds & casts from ~3.2u, and the
        // player (speed 4) closes the gap in ~1s. A real telegraphed bolt is the follow-up that sells it.
        new MonsterArchetype {
            name = "Flam", sheet = "Flam", weight = 2, tint = Arcane, scale = 0.95f, flash = Color.white,
            maxHealth = 10, defense = 0, magicDefense = 0, flee = 10,
            attackType = MonsterAI3D.AttackType.Magic, attack = 5, hit = 8, xpReward = 10,
            damageSpread = 0.2f, touchRange = 3.6f, touchInterval = 1.6f,
            moveSpeed = 2.2f, aggroRadius = 7f, leashRadius = 11f, stopDistance = 0.7f, wanderRadius = 3f,
            preferredRange = 3.2f, fleeHpFraction = 0f,
        },

        // 5) DARTER — hit-and-run; flees once wounded below 40% HP. Uncommon.
        new MonsterArchetype {
            name = "Cyclope", sheet = "Cyclope", weight = 2, tint = None, scale = 0.9f, flash = Color.white,
            maxHealth = 9, defense = 0, magicDefense = 0, flee = 14,
            attackType = MonsterAI3D.AttackType.Physical, attack = 3, hit = 9, xpReward = 8,
            damageSpread = 0.3f, touchRange = 0.9f, touchInterval = 1.0f,
            moveSpeed = 3.2f, aggroRadius = 6f, leashRadius = 11f, stopDistance = 0.7f, wanderRadius = 4f,
            preferredRange = 0f, fleeHpFraction = 0.4f,
        },
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
