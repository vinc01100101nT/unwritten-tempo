/// <summary>
/// One stack in a character's bag: an item, how many of it, and — for anything wearable — the particular copy
/// that is being carried, with its own upgrade level, cards, rarity and rolled kit.
///
/// <para><b>Why this is its own type rather than something the bag window owns.</b> It used to live inside
/// <see cref="InventoryStore"/>, which is a component on the inventory window's canvas — so the only home a
/// carried item had was a piece of screen furniture that does not exist until somebody opens the bag. A
/// character's belongings are a fact about the character, in the same way their worn gear already was, so the
/// list of these now lives on <see cref="Character"/> and the window reads it.</para>
///
/// <para>Plain data on purpose: no engine types, no behaviour beyond the one question everything asks of it.
/// That is what lets the character hold it without the character knowing anything about windows.</para>
/// </summary>
public class BagEntry
{
    /// <summary>What this stack is.</summary>
    public Item def;

    /// <summary>How many. Always 1 for anything wearable — each copy is its own stack, because each copy has
    /// its own upgrade level and cards and they cannot be merged without losing one of them.</summary>
    public int count;

    /// <summary>The individual copy, for wearables. Null for anything that stacks.</summary>
    public EquipmentInstance instance;

    /// <summary>Picked up since the player last looked, so the window can mark it.</summary>
    public bool isNew;

    /// <summary>This piece is being WORN. The bag holds only what is off your body, so a worn row is made for
    /// the list from what the character has equipped, and is the only kind of row that offers Unequip.</summary>
    public bool worn;

    /// <summary>Which slot it is worn in — meaningless unless <see cref="worn"/>.</summary>
    public EquipSlotType wornSlot;

    /// <summary>Is this something that can be worn.</summary>
    public bool IsEquip => def != null && def.slot != EquipSlotType.None;
}
