/// <summary>
/// The SINGLE OWNER of "which basic-attack stance does each weapon TYPE play". The player brain
/// (<see cref="CharacterBrain"/>) calls this every swing to pick the weapon-specific <see cref="StanceId"/>
/// (AttackSword/AttackBow/…) instead of one generic Attack; the actual clip per variant is authored in the Stance
/// Composer. Unmapped / unarmed → <see cref="StanceId.AttackBareHands"/>. Pure switch — no allocation.
/// </summary>
public static class WeaponStances
{
    public static StanceId AttackFor(WeaponType w)
    {
        switch (w)
        {
            case WeaponType.Sword:
            case WeaponType.TwoHandSword:
            case WeaponType.Axe:
            case WeaponType.TwoHandAxe:
            case WeaponType.Mace:     return StanceId.AttackSword;
            case WeaponType.Dagger:
            case WeaponType.Katar:    return StanceId.AttackDagger;
            case WeaponType.Staff:    return StanceId.AttackStaff;
            case WeaponType.Spear:    return StanceId.AttackLance;
            case WeaponType.Bow:      return StanceId.AttackBow;
            case WeaponType.Firearm:  return StanceId.AttackRifle;
            case WeaponType.Knuckle:
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
            case StanceId.AttackBow:  return true;
            default:                  return false;
        }
    }
}
