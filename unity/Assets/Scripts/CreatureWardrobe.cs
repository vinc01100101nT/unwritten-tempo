using System.Collections.Generic;

/// <summary>
/// WHAT A CREATURE PUTS ON AS IT COMES TO LIFE, beyond anything authored for it. Every place a player can wear a piece
/// in — weapon, head, armour, garment, shield — is offered one random item that fits there, rolled as a real copy
/// through the one door everything enters the world by, and the creature keeps it on a chance that falls with the
/// copy is Raw and unfolded.
///
/// <para><b>A stand-in for authored drop lists.</b> What a creature wears is what it can give up, so a random wardrobe
/// is random loot that still obeys that rule.</para>
///
/// <para>Dice and numbers only — no component — so the rule can be checked by calling it.</para>
/// </summary>
public static class CreatureWardrobe
{
    /// <summary>The places a creature is offered a piece for.</summary>
    public static readonly EquipSlotType[] Places =
        { EquipSlotType.Weapon, EquipSlotType.Head, EquipSlotType.Armor, EquipSlotType.Garment, EquipSlotType.Shield };

    /// <summary>The chance a creature keeps the piece it was offered. One flat number: every crafted piece a
    /// creature is offered is Raw and unfolded, so there is no tier to vary it by.</summary>
    public const float WearChance = 0.40f;

    /// <summary>Offer a piece for every place in <paramref name="worn"/> that is still empty, and keep each one on
    /// that chance. A place already filled — authored gear — is never touched.</summary>
    public static void FillEmptyPlaces(Dictionary<EquipSlotType, EquipmentInstance> worn)
    {
        if (worn == null) return;
        for (int i = 0; i < Places.Length; i++)
        {
            var place = Places[i];
            if (worn.ContainsKey(place)) continue;
            var def = ItemCatalog.RandomFor(place);
            if (def == null) continue;
            if (CombatRng.Chance(WearChance)) worn[place] = EquipmentInstance.CreateFresh(def);
        }
    }
}
