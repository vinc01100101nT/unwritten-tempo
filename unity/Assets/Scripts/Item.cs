using UnityEngine;

/// <summary>Which equipment slot an item fits. None = a bag-only item (consumable / material).</summary>
public enum EquipSlotType { None, Head, Body, Weapon, OffHand, Boots, Trinket }

/// <summary>
/// A piece of gear or a bag item: an icon, which slot it fits, and (Phase F) the
/// stat bonuses it grants while equipped, or how much it heals when used. Create
/// more via Assets ▸ Create ▸ unwritten ▸ Item, or Tools ▸ unwritten ▸ Create
/// Starter Items.
/// </summary>
[CreateAssetMenu(fileName = "Item", menuName = "unwritten/Item")]
public class Item : ScriptableObject
{
    public string displayName = "Item";
    public Sprite icon;

    [Tooltip("Which equipment slot this fits. None = bag-only (consumable/material).")]
    public EquipSlotType slot = EquipSlotType.None;

    [Header("Equipment bonuses (applied while equipped)")]
    public int bonusHP;
    public int bonusAttack;
    public int bonusDefense;

    [Header("Consumable")]
    [Tooltip("Right-click in the bag to heal this much, then consume it. 0 = not a consumable.")]
    public int healAmount;

    [TextArea] public string description;
}
