/// <summary>The coarse item bucket shown in the Item Table's "Type" column. NOT a stored field — it's DERIVED from the
/// item's real owners (<see cref="Item.slot"/> + <see cref="Item.category"/>) so there's no parallel value to hand-sync.
/// Editing it in the table maps back onto slot/category via <see cref="ItemTypes.Apply"/>.</summary>
public enum ItemType { Weapon, Armor, Consumable, Misc }

/// <summary>Two-way bridge between the coarse <see cref="ItemType"/> and the fine-grained slot/category that actually
/// own an item's classification. <see cref="Of"/> reads the bucket; <see cref="Apply"/> writes a bucket back onto the
/// real fields (picking a sensible default slot) — called only when the bucket genuinely changes, so a specific armor
/// slot (Head/Shield/…) isn't clobbered just by re-picking "Armor".</summary>
public static class ItemTypes
{
    public static ItemType Of(Item it)
    {
        if (it == null) return ItemType.Misc;
        if (it.slot == EquipSlotType.Weapon) return ItemType.Weapon;
        if (it.slot != EquipSlotType.None)   return ItemType.Armor;          // any other equip slot = worn gear
        if (it.category == ItemCategory.Use || it.healAmount > 0) return ItemType.Consumable;
        return ItemType.Misc;
    }

    /// <summary>Set the item's slot/category to a sensible default for the chosen bucket. Caller should invoke this
    /// only when the bucket actually changed (so a Head/Shield/… armor keeps its slot when Type is re-picked as Armor).</summary>
    public static void Apply(Item it, ItemType t)
    {
        if (it == null) return;
        switch (t)
        {
            case ItemType.Weapon:     it.slot = EquipSlotType.Weapon; it.category = ItemCategory.Equip; break;
            case ItemType.Armor:      it.slot = EquipSlotType.Armor;  it.category = ItemCategory.Equip; break;
            case ItemType.Consumable: it.slot = EquipSlotType.None;   it.category = ItemCategory.Use;   break;
            case ItemType.Misc:       it.slot = EquipSlotType.None;   it.category = ItemCategory.Etc;   break;
        }
    }
}
