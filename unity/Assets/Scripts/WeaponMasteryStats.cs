/// <summary>
/// WHICH THREE RING STATS EACH WEAPON'S MASTERY RAISES — the table from the V4 design, taken from each weapon's
/// one-sentence identity in the Weapon Roster. Only the ten RING stats are used, because mastery adds to the
/// ring's own term for that stat.
///
/// <para>Four roster weapons ride a type under another name (<see cref="WeaponTypes.Label"/>): a Greatsword is the
/// two-handed sword type, a Warhammer the mace type, a Fist the knuckle type and a Sniper the firearm type.</para>
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
            // Dagger — poison from the blade.
            case WeaponType.Dagger:
                return stat == StatId.AttackSpeed || stat == StatId.Evasion || stat == StatId.Atk;
            // Katar — chains of critical hits.
            case WeaponType.Katar:
                return stat == StatId.Critical || stat == StatId.Evasion || stat == StatId.AttackSpeed;
            // Shuriken — bouncing throws, or many-bladed swings.
            case WeaponType.Shuriken:
                return stat == StatId.AttackSpeed || stat == StatId.Accuracy || stat == StatId.HP;
            // Sniper (the firearm type) — one painful magic shot from far away.
            case WeaponType.Firearm:
                return stat == StatId.Critical || stat == StatId.Accuracy || stat == StatId.MAtk;
            // Staff — destructive area magic.
            case WeaponType.Staff:
                return stat == StatId.MAtk || stat == StatId.CastSpeed || stat == StatId.MP;
            // Wand — keep allies alive.
            case WeaponType.Wand:
                return stat == StatId.MP || stat == StatId.CastSpeed || stat == StatId.Def;
            // Scythe — overwhelm with curses.
            case WeaponType.Scythe:
                return stat == StatId.MAtk || stat == StatId.MP || stat == StatId.HP;
            // Fist (the knuckle type) — physical bursts, or standing in front and taking the hits.
            case WeaponType.Knuckle:
                return stat == StatId.Atk || stat == StatId.HP || stat == StatId.MP;
            default:
                return false;
        }
    }
}
