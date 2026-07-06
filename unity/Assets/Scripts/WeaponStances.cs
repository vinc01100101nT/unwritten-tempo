/// <summary>
/// The SINGLE OWNER of "which basic-attack stance does each weapon TYPE play". The player brain
/// (<see cref="PlayerStanceBrain"/>) calls this every swing to pick the weapon-specific <see cref="StanceId"/>
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
}
