/// <summary>The coarse item bucket — one per catalog table, and the "Type" column inside each. NOT a stored field: it
/// is DERIVED from the item's real owners (<see cref="Item.slot"/> + <see cref="Item.category"/>) so there is no
/// parallel value to hand-sync. Editing it maps back onto slot/category via <see cref="ItemTypes.Apply"/>.
///
/// <para>Every item lands in exactly one bucket and no item lands in none — <see cref="ItemType.Misc"/> is the
/// catch-all, which is what keeps an item from becoming invisible in every table at once.</para></summary>
public enum ItemType { Weapon, Armor, Ring, Consumable, Material, Quest, Misc }

/// <summary>Two-way bridge between the coarse <see cref="ItemType"/> and the fine-grained slot/category that actually
/// own an item's classification. <see cref="Of"/> reads the bucket; <see cref="Apply"/> writes a bucket back onto the
/// real fields (picking a sensible default slot) — called only when the bucket genuinely changes, so a specific armor
/// slot (Head/Shield/…) isn't clobbered just by re-picking "Armor".</summary>
public static class ItemTypes
{
    public static ItemType Of(Item it)
    {
        if (it == null) return ItemType.Misc;
        // Slot decides first: anything wearable is gear, whatever its tab says.
        if (it.slot == EquipSlotType.Weapon) return ItemType.Weapon;
        if (it.slot == EquipSlotType.Ring)   return ItemType.Ring;    // its own bucket — the progression vessel, not armor
        if (it.slot != EquipSlotType.None)   return ItemType.Armor;   // any other equip slot = worn gear
        // Bag-only items are sorted by their tab.
        switch (it.category)
        {
            case ItemCategory.Use:      return ItemType.Consumable;
            case ItemCategory.Material: return ItemType.Material;
            case ItemCategory.Quest:    return ItemType.Quest;
            default:                    return ItemType.Misc;
        }
    }

    /// <summary>Set the item's slot/category to a sensible default for the chosen bucket. Caller should invoke this
    /// only when the bucket actually changed (so a Head/Shield/… armor keeps its slot when Type is re-picked as Armor).</summary>
    public static void Apply(Item it, ItemType t)
    {
        if (it == null) return;
        switch (t)
        {
            case ItemType.Weapon:     it.slot = EquipSlotType.Weapon; it.category = ItemCategory.Equip;   break;
            case ItemType.Armor:      it.slot = EquipSlotType.Armor;  it.category = ItemCategory.Equip;   break;
            case ItemType.Ring:       it.slot = EquipSlotType.Ring;   it.category = ItemCategory.Equip;   break;
            case ItemType.Consumable: it.slot = EquipSlotType.None;   it.category = ItemCategory.Use;     break;
            case ItemType.Material:   it.slot = EquipSlotType.None;   it.category = ItemCategory.Material; break;
            case ItemType.Quest:      it.slot = EquipSlotType.None;   it.category = ItemCategory.Quest;   break;
            case ItemType.Misc:       it.slot = EquipSlotType.None;   it.category = ItemCategory.Etc;     break;
        }
    }
}
