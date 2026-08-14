using UnityEngine;

/// <summary>
/// THE rule for "how far can this unit reach with what it's holding" — one function every unit reads, player and
/// mob alike. The equipped <see cref="Item"/>'s <see cref="Item.attackRange"/> is the single owner of a weapon's
/// reach; a unit's own authored range is only the UNARMED fallback.
///
/// <para>WHY IT'S SHARED: reach used to be weapon-derived for the PLAYER only (<see cref="Player3DAttacker"/>) while
/// mobs used a hand-authored <c>touchRange</c> that ignored their weapon entirely. That is exactly the player-only
/// gap the project treats as a bug: a mob swinging a spear bit from dagger distance, and arming a mob changed its
/// damage but not its reach. Both sides now resolve reach here, so equipping a weapon means the same thing on
/// anybody.</para>
///
/// <para>The two unit types differ only in WHERE their gear lives — the player's in <see cref="Character"/>, a mob's
/// in <see cref="UnitEquipment"/> — so each has a lookup below that ends in the same <see cref="Of"/> rule.</para>
/// </summary>
public static class WeaponReach
{
    /// <summary>Reach of <paramref name="weapon"/>, or <paramref name="unarmedFallback"/> when there's no weapon (or
    /// it authors no range). FULL-SIZE reach — blend it against the other body via <see cref="UnitScale.MeleeBlend"/> for the live
    /// value on resized units.</summary>
    public static float Of(Item weapon, float unarmedFallback)
        => weapon != null && weapon.attackRange > 0f ? weapon.attackRange : unarmedFallback;

    /// <summary>The item in <paramref name="equipment"/>'s weapon slot, or null. A mob's gear lives on
    /// <see cref="UnitEquipment"/>; this unwraps the instance to the <see cref="Item"/> that owns the numbers.</summary>
    public static Item WeaponOf(UnitEquipment equipment)
    {
        var inst = equipment != null ? equipment.Get(EquipSlotType.Weapon) : null;
        return inst != null ? inst.def : null;
    }

    /// <summary>Reach for a unit whose gear lives on a <see cref="UnitEquipment"/> (every mob). Reads the weapon at
    /// USE time — no per-equip copy to keep in sync, the same pattern the player's reach uses.</summary>
    public static float For(UnitEquipment equipment, float unarmedFallback)
        => Of(WeaponOf(equipment), unarmedFallback);
}
