using Mirror;

/// <summary>
/// THE STAFF'S HELD ORBS, on the wire: the server packs this person's orbs (oldest first) and whether a Staff is in
/// hand into one number whenever either changes, and every machine drawing them circles that set around the body
/// (<see cref="OrbHalo"/>). One small change per orb press, to the people who can see this person.
/// </summary>
public partial class NetPlayer
{
    [SyncVar] int heldOrbs;

    readonly OrbHalo orbHalo = new OrbHalo();
    readonly OrbElement[] orbScratch = new OrbElement[OrbMath.Slots];
    int orbStatesSeen = int.MinValue, orbGearSeen = int.MinValue;

    // SERVER: re-pack only when this person's states or worn gear changed — two compares on the settled path.
    void PushOrbs()
    {
        var unit = ServerCombatUnit;
        var states = unit != null ? unit.States : null;
        if (states == null) return;
        int gear = serverGear != null ? serverGear.Version : 0;
        if (states.ChangeCount == orbStatesSeen && gear == orbGearSeen) return;
        orbStatesSeen = states.ChangeCount; orbGearSeen = gear;
        int n = states.OrbsInOrder(orbScratch);
        heldOrbs = OrbMath.Pack(orbScratch, n, unit.HeldWeapon == WeaponType.Staff);
    }
}
