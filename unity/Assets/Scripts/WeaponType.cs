using UnityEngine;

/// <summary>
/// The kind of weapon an <see cref="Item"/> is — the layer that gives gear a combat FEEL (fast dagger vs
/// heavy axe vs ranged bow) and, later, a class affinity and a size-modifier matchup (see
/// <see cref="WeaponSizes"/>). Forward-declared in full so an item authored today and a balance pass next
/// year agree by definition; only a few types ship art/stats at first. <b>Append new members at the END</b> —
/// integer values are serialized onto Item assets, so reordering would silently remap existing gear.
///
/// <para>RO splits some of these (1H/2H sword, spear, axe) to trade a shield slot for damage; we keep the
/// distinct ones and collapse RO's five firearms into a single <see cref="Firearm"/> (they were
/// mechanically identical). Weapon type is ADVISORY in the slice — it informs stat fit and the size table,
/// it does not hard-gate which class may hold what (that can harden later).</para>
/// </summary>
public enum WeaponType
{
    None = 0,           // unarmed / not a weapon (bag items, armor)
    Sword = 1,          // one-handed — balanced baseline (Swordsman home), pairs with a shield
    TwoHandSword = 2,   // heavy two-hander, anti-Large
    Dagger = 3,         // fast, low per-hit, shines vs Small / swarms
    Axe = 4,            // one-handed axe
    TwoHandAxe = 5,     // slow, huge, anti-Large
    Mace = 6,           // blunt / holy flavor
    Spear = 7,          // reach / anti-Large
    Staff = 8,          // the magic vector (MATK / Element)
    Bow = 9,            // physical ranged, kiting
    Katar = 10,         // assassin double-dagger, crit-leaning
    Knuckle = 11,       // monk / fist
    Firearm = 12,       // collapses RO's revolver/rifle/shotgun/gatling/launcher
}

/// <summary>A mob's body size — pure data for now (nothing reads it in the slice). It's the column index of
/// the deferred <see cref="WeaponSizes"/> modifier table; defaulting everything to <see cref="Medium"/>
/// reproduces RO's "100% everywhere" baseline, so backfill is a no-op.</summary>
public enum MobSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

/// <summary>
/// DEFERRED RO-style weapon-vs-size matchup, modelled exactly like <see cref="Elements"/>: a
/// <c>[weaponType, mobSize]</c> multiplier on physical damage. <b>Not wired into combat yet</b> — this is the
/// dormant data so flipping the mechanic on later is a one-line call (<see cref="Modifier"/>) with no data
/// migration. Values are RO's size-penalty chart (the in-game table) normalised to multipliers (100→1.0).
/// Defaults to 1.0 for anything unlisted, so it can never silently nerf a weapon you forgot to fill in.
/// </summary>
public static class WeaponSizes
{
    public const int TypeCount = 13;   // WeaponType members
    public const int SizeCount = 3;    // MobSize members

    // T[weaponType, size] — rows in WeaponType order, columns Small / Medium / Large.
    //                              Sml    Med    Lrg
    static readonly float[,] T = {
        /* None         */        { 1.00f, 1.00f, 1.00f },
        /* Sword        */        { 0.75f, 1.00f, 0.75f },
        /* TwoHandSword */        { 0.75f, 0.75f, 1.00f },
        /* Dagger       */        { 1.00f, 0.75f, 0.50f },
        /* Axe          */        { 0.50f, 0.75f, 1.00f },
        /* TwoHandAxe   */        { 0.50f, 0.75f, 1.00f },
        /* Mace         */        { 0.75f, 1.00f, 1.00f },
        /* Spear        */        { 0.75f, 0.75f, 1.00f },
        /* Staff        */        { 1.00f, 1.00f, 1.00f },
        /* Bow          */        { 1.00f, 1.00f, 0.75f },
        /* Katar        */        { 0.75f, 1.00f, 0.75f },
        /* Knuckle      */        { 1.00f, 0.75f, 0.50f },
        /* Firearm      */        { 1.00f, 1.00f, 1.00f },
    };

    /// <summary>Damage multiplier for <paramref name="weapon"/> striking a <paramref name="size"/> mob:
    /// 1.0 = full, &lt;1 = size-penalised. Currently UNUSED by combat (deferred); call it when the mechanic
    /// is turned on. Out-of-range inputs fall back to 1.0.</summary>
    public static float Modifier(WeaponType weapon, MobSize size)
    {
        int w = (int)weapon, s = (int)size;
        if (w < 0 || w >= TypeCount || s < 0 || s >= SizeCount) return 1f;
        return T[w, s];
    }
}
