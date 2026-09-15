using System.Collections.Generic;

/// <summary>
/// WHAT A CREATURE PUTS ON AS IT COMES TO LIFE, beyond anything authored for it. Every place a player can wear a piece
/// in — weapon, head, armour, garment, shield — is offered one random item that fits there, rolled as a real copy
/// through the one door everything enters the world by, and the creature keeps it on a chance that falls with the
/// copy's rarity.
///
/// <para><b>A stand-in for authored drop lists.</b> What a creature wears is what it can give up, so a random wardrobe
/// is random loot that still obeys that rule.</para>
///
/// <para>Dice and numbers only — no component — so the rule can be checked by calling it.</para>
/// </summary>
public static class CreatureWardrobe
{
    /// <summary>The places a creature is offered a piece for. The ring is not one: it is never handed out at random.</summary>
    public static readonly EquipSlotType[] Places =
        { EquipSlotType.Weapon, EquipSlotType.Head, EquipSlotType.Armor, EquipSlotType.Garment, EquipSlotType.Shield };

    // The chance a creature keeps the piece it was offered, by that copy's rarity: Common, Uncommon, Rare, Epic, Legendary.
    static readonly float[] WearChances = { 0.40f, 0.30f, 0.20f, 0.10f, 0.05f };

    /// <summary>The chance a creature keeps an offered piece of rarity <paramref name="r"/>.</summary>
    public static float WearChance(Rarity r) => WearChances[(int)r];

    /// <summary>Offer a piece for every place in <paramref name="worn"/> that is still empty, and keep each one on its
    /// rarity's chance. A place already filled — authored gear — is never touched.</summary>
    public static void FillEmptyPlaces(Dictionary<EquipSlotType, EquipmentInstance> worn)
    {
        if (worn == null) return;
        for (int i = 0; i < Places.Length; i++)
        {
            var place = Places[i];
            if (worn.ContainsKey(place)) continue;
            var def = ItemCatalog.RandomFor(place);
            if (def == null) continue;
            var copy = EquipmentInstance.CreateRolled(def);
            if (CombatRng.Chance(WearChance(copy.Rarity))) worn[place] = copy;
        }
    }
}
