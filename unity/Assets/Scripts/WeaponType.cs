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
    Sword = 1,          // one-handed — balanced baseline
    TwoHandSword = 2,   // heavy two-hander, anti-Large
    Dagger = 3,         // fast, low per-hit, shines vs Small / swarms
    Axe = 4,            // one-handed axe
    Mace = 6,           // blunt / holy flavor
    Spear = 7,          // reach / anti-Large
    Staff = 8,          // the magic vector (MATK / Element)
    Bow = 9,            // physical ranged, kiting
    Katar = 10,         // assassin double-dagger, crit-leaning
    Knuckle = 11,       // monk / fist
    Firearm = 12,       // collapses RO's revolver/rifle/shotgun/gatling/launcher
    // The support caster — heals and keeps allies up; swings like a staff.
    Wand = 13,
    // The curse reaper — Silence, defence cut and mana drain that wear an enemy down.
    Scythe = 14,
    // Many blades — thrown, they bounce through a crowd; swung, one swing lands two or three times.
    Shuriken = 15,
}

/// <summary>WHEN a basic swing connects and HOW HARD, as fractions of the swing — the one answer a person's swing on
/// the server and every creature's swing both read, so the two can never land differently. A pair weapon
/// (<see cref="WeaponTypes.IsDualWield"/>) lands twice: the first hand at <c>hitTiming</c> at full power, then the other
/// hand at <c>offHandHitTiming</c> (never before the first, never past the end) at <c>offHandPower</c>. Every other
/// weapon lands once. Numbers in, numbers out.</summary>
public static class SwingContacts
{
    public static int Of(WeaponType w, float hitTiming, float offHandHitTiming, float offHandPower,
                         out float firstAt, out float secondAt, out float secondPower)
    {
        firstAt = Mathf.Clamp01(hitTiming);
        secondAt = Mathf.Min(1f, Mathf.Max(offHandHitTiming, firstAt + 0.01f));
        secondPower = Mathf.Clamp01(offHandPower);
        return WeaponTypes.IsDualWield(w) ? 2 : 1;
    }
}

/// <summary>Facts about a <see cref="WeaponType"/> that more than one system asks.</summary>
public static class WeaponTypes
{
    /// <summary>Whether a unit holding <paramref name="w"/> with these TOTAL primaries (gear and Ink included) swings
    /// RANGED — its blow travels rather than connecting, and it swings with ranged attack instead of melee attack. A
    /// bow and a firearm always do; a shuriken does when DEX is higher than STR (a tie is melee). The one answer the
    /// arrow, the server's landing check, the reach and the damage roll all read, so they can never disagree.</summary>
    public static bool SwingsRanged(WeaponType w, int str, int dex)
        => w == WeaponType.Bow || w == WeaponType.Firearm || (RangedByStat(w) && dex > str);

    /// <summary>A weapon that is melee or ranged depending on who holds it — today the shuriken only.</summary>
    public static bool RangedByStat(WeaponType w) => w == WeaponType.Shuriken;

    /// <summary>A weapon whose ranged attack is the weapon ITSELF flying — it leaves the hand, bounces through a crowd
    /// and comes back. Today the shuriken only; a bow and a firearm shoot something else and keep the weapon.</summary>
    public static bool ThrowsItself(WeaponType w) => w == WeaponType.Shuriken;

    /// <summary>A weapon wielded in BOTH hands as a pair (decided 2026-10-09): equipping one fills the left hand with the
    /// same weapon automatically — nobody equips a second one. Today the katar and the axe. The one answer the hand
    /// that draws the pair and the swing that lands a hit per hand both read, so they can never disagree.</summary>
    public static bool IsDualWield(WeaponType w) => w == WeaponType.Katar || w == WeaponType.Axe;

    /// <summary>A weapon whose holder's MAGIC can crit (decided 2026-10-06): the sniper, and nothing else. Every other
    /// unit's magic never crits — a crit pierces defence, and a spell that pierced too would collapse the
    /// Might › Arcane › Finesse triangle. A sniper's crit is the same crit a physical one is: full power × the crit
    /// multiplier, no defence taken off.</summary>
    public static bool MagicCanCrit(WeaponType w) => w == WeaponType.Firearm;

    /// <summary>A weapon whose ORDINARY ATTACK is a magic shot (decided 2026-10-06): the sniper. It rolls magic attack
    /// against magic defence, cannot miss, can crit (<see cref="MagicCanCrit"/>), and grows with the distance to its
    /// target (<see cref="StrikeMath.SniperShotGrowth"/>). Every other ordinary attack is physical.</summary>
    public static bool ShootsMagic(WeaponType w) => w == WeaponType.Firearm;

    /// <summary>The weapon's name as a player reads it. The enum keeps the engine's names — they are saved in skill ids
    /// and data — but four of them are called something else in the game.</summary>
    public static string Label(WeaponType w)
    {
        switch (w)
        {
            case WeaponType.TwoHandSword: return "Greatsword";
            case WeaponType.Mace:         return "Warhammer";
            case WeaponType.Knuckle:      return "Fist";
            case WeaponType.Firearm:      return "Sniper";
            default:                      return w.ToString();
        }
    }

    /// <summary>The weapon from a typed name — the engine's name or the one a player reads, any case. False for
    /// anything else, and for None.</summary>
    public static bool TryParse(string name, out WeaponType w)
    {
        w = WeaponType.None;
        if (string.IsNullOrEmpty(name) || char.IsDigit(name[0])) return false;
        foreach (WeaponType each in System.Enum.GetValues(typeof(WeaponType)))
        {
            if (each == WeaponType.None) continue;
            if (string.Equals(name, each.ToString(), System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, Label(each), System.StringComparison.OrdinalIgnoreCase)) { w = each; return true; }
        }
        return false;
    }

    /// <summary>The fighting STYLE a weapon's skills belong to — the style its Kashira fought in, which its shadow is
    /// also called by. Each of the weapon's skills is one numbered form of it (<see cref="SkillForms"/>). Empty for a
    /// weapon no Kashira wielded. The fourteen styles are in the story canon doc.</summary>
    public static string StyleOf(WeaponType w)
    {
        switch (w)
        {
            case WeaponType.Sword:        return "Blossom Wind";
            case WeaponType.TwoHandSword: return "Raging Tide";
            case WeaponType.Axe:          return "Wildfire";
            case WeaponType.Mace:         return "Earthrend Descent";
            case WeaponType.Spear:        return "Bending River";
            case WeaponType.Knuckle:      return "Northern Lights";
            case WeaponType.Bow:          return "Passing Cloud";
            case WeaponType.Dagger:       return "Nightshade Bloom";
            case WeaponType.Katar:        return "Veiled Mirage";
            case WeaponType.Shuriken:     return "Autumn Gale";
            case WeaponType.Firearm:      return "Dark Moon";
            case WeaponType.Staff:        return "Frozen Ashes";
            case WeaponType.Wand:         return "Morning Mist";
            case WeaponType.Scythe:       return "Black Lily";
            default:                      return "";
        }
    }
}

/// <summary>
/// DEFERRED RO-style weapon-vs-size matchup, modelled exactly like <see cref="Elements"/>: a
/// <c>[weaponType, mobSize]</c> multiplier on physical damage. <b>Not wired into combat yet</b> — this is the
/// dormant data so flipping the mechanic on later is a one-line call (<see cref="Modifier"/>) with no data
/// migration. Values are RO's size-penalty chart (the in-game table) normalised to multipliers (100→1.0).
/// Defaults to 1.0 for anything unlisted, so it can never silently nerf a weapon you forgot to fill in. The size is a
/// creature's size GROUP (<see cref="MobSizes.GroupOf"/>), never its size number.
/// </summary>
public static class WeaponSizes
{
    // Rows filled in below, in WeaponType order. Wand, Scythe and Shuriken have no row in RO's chart, so they fall back to 1.0.
    public const int TypeCount = 13;
    public const int SizeCount = 3;    // MobSize columns filled in (Small / Medium / Large)

    // T[weaponType, size] — rows in WeaponType order, columns Small / Medium / Large.
    //                              Sml    Med    Lrg
    static readonly float[,] T = {
        /* None         */        { 1.00f, 1.00f, 1.00f },
        /* Sword        */        { 0.75f, 1.00f, 0.75f },
        /* TwoHandSword */        { 0.75f, 0.75f, 1.00f },
        /* Dagger       */        { 1.00f, 0.75f, 0.50f },
        /* Axe          */        { 0.50f, 0.75f, 1.00f },
        /* 5, no weapon */        { 1.00f, 1.00f, 1.00f },
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
