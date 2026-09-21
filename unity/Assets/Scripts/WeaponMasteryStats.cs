/// <summary>
/// WHICH THREE RING STATS EACH WEAPON'S MASTERY RAISES — the table from the V4 design, taken from each weapon's
/// one-sentence identity in the Weapon Roster. Only the ten RING stats are used, because mastery adds to the
/// ring's own term for that stat.
///
/// <para>The five weapons the roster names that the engine has no type for yet (Greatsword, Warhammer, Shield,
/// Wand, Scythe) map onto the types that exist: a greatsword is a two-handed sword, a wand is a staff. The rest
/// arrive when their types do.</para>
/// </summary>
public static class WeaponMasteryStats
{
    /// <summary>True when <paramref name="weapon"/>'s mastery raises <paramref name="stat"/>.</summary>
    public static bool Raises(WeaponType weapon, StatId stat)
    {
        switch (weapon)
        {
            // Sword — precise duels, timed defence.
            case WeaponType.Sword:
                return stat == StatId.AttackSpeed || stat == StatId.Atk || stat == StatId.HP;
            // Greatsword — delete groups with cleaves.
            case WeaponType.TwoHandSword:
                return stat == StatId.Atk || stat == StatId.Critical || stat == StatId.HP;
            // Axe — relentless aggression.
            case WeaponType.Axe:
            case WeaponType.TwoHandAxe:
                return stat == StatId.AttackSpeed || stat == StatId.Atk || stat == StatId.Critical;
            // Warhammer (Mace) — deny enemy actions.
            case WeaponType.Mace:
                return stat == StatId.Atk || stat == StatId.Def || stat == StatId.HP;
            // Spear — control positioning.
            case WeaponType.Spear:
                return stat == StatId.Accuracy || stat == StatId.Atk || stat == StatId.HP;
            // Bow — mobility and spacing.
            case WeaponType.Bow:
                return stat == StatId.Evasion || stat == StatId.Accuracy || stat == StatId.AttackSpeed;
            // Dagger / Katar — finish weakened targets.
            case WeaponType.Dagger:
            case WeaponType.Katar:
                return stat == StatId.Critical || stat == StatId.Evasion || stat == StatId.AttackSpeed;
            // Firearm — devastating single shots.
            case WeaponType.Firearm:
                return stat == StatId.Critical || stat == StatId.Accuracy || stat == StatId.Atk;
            // Staff — destructive area magic (and the Wand's home until it has a type of its own).
            case WeaponType.Staff:
                return stat == StatId.MAtk || stat == StatId.CastSpeed || stat == StatId.MP;
            // Knuckle — the fist has no roster identity yet; it trains what it swings with.
            case WeaponType.Knuckle:
                return stat == StatId.AttackSpeed || stat == StatId.Atk || stat == StatId.Evasion;
            default:
                return false;
        }
    }
}
