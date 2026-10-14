/// <summary>
/// The SINGLE OWNER of "which basic-attack stance does each weapon TYPE play". The player brain
/// (<see cref="CharacterBrain"/>) calls this every swing to pick the weapon-specific <see cref="StanceId"/>
/// (AttackSword/AttackBow/…) instead of one generic Attack; the actual clip per variant is authored in the Stance
/// Composer. Unmapped / unarmed → <see cref="StanceId.AttackBareHands"/>. Pure switch — no allocation.
/// </summary>
public static class WeaponStances
{
    /// <summary>The basic-attack stance for <paramref name="w"/> when the holder's attack is
    /// <paramref name="ranged"/> — a shuriken THROWN plays its own throw, a shuriken swung plays its swing. Every other
    /// weapon has one attack either way.</summary>
    public static StanceId AttackFor(WeaponType w, bool ranged)
        => ranged && WeaponTypes.ThrowsItself(w) ? StanceId.AttackShurikenThrow : AttackFor(w);

    public static StanceId AttackFor(WeaponType w)
    {
        switch (w)
        {
            case WeaponType.Sword:    return StanceId.AttackOneHandSword;
            case WeaponType.TwoHandSword: return StanceId.AttackSword;
            case WeaponType.Axe:      return StanceId.AttackAxe;
            case WeaponType.Mace:     return StanceId.AttackMace;
            case WeaponType.Dagger:   return StanceId.AttackDagger;
            case WeaponType.Katar:    return StanceId.AttackKatar;
            case WeaponType.Staff:    return StanceId.AttackStaff;
            case WeaponType.Wand:     return StanceId.AttackWand;
            case WeaponType.Scythe:   return StanceId.AttackScythe;
            case WeaponType.Shuriken: return StanceId.AttackShuriken;
            case WeaponType.Spear:    return StanceId.AttackLance;
            case WeaponType.Bow:      return StanceId.AttackBow;
            case WeaponType.Firearm:  return StanceId.AttackRifle;
            case WeaponType.Knuckle:  return StanceId.AttackKnuckle;
            case WeaponType.None:
            default:                  return StanceId.AttackBareHands;
        }
    }

    /// <summary>Is this stance one of the weapon swings above? Asked by the stance choke point, which must not
    /// report a swing to the machines watching: the server already announces one, from the weapon IT holds, and a
    /// second announcement would replay the same swing from the top on every screen.
    ///
    /// <para>Kept HERE, beside the list it is asking about, so a weapon variant added to that switch is covered by
    /// this the same day rather than the day somebody notices.</para></summary>
    public static bool IsBasicAttack(StanceId id)
    {
        switch (id)
        {
            case StanceId.AttackBareHands:
            case StanceId.AttackSword:
            case StanceId.AttackDagger:
            case StanceId.AttackStaff:
            case StanceId.AttackLance:
            case StanceId.AttackHandGun:
            case StanceId.AttackRifle:
            case StanceId.AttackBow:
            case StanceId.AttackScythe:
            case StanceId.AttackShuriken:
            case StanceId.AttackShurikenThrow:
            case StanceId.AttackOneHandSword:
            case StanceId.AttackAxe:
            case StanceId.AttackMace:
            case StanceId.AttackKatar:
            case StanceId.AttackKnuckle:
            case StanceId.AttackWand: return true;
            default:                  return false;
        }
    }
}
