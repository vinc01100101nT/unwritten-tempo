using UnityEngine;

/// <summary>Which equipment slot an item fits. None = a bag-only item (consumable / material).
/// Integer values are PINNED so the rename from the old taxonomy (Body→Armor, OffHand→Shield,
/// Trinket→Accessory1) doesn't shift any serialized scene/prefab/item data — only the names changed.
/// The 7 VISIBLE slots (spec) are Head/Armor/Garment/Weapon/Shield/Accessory1/Accessory2; Boots is
/// legacy (kept for old data, not shown); the three Costume* slots are reserved for a future cosmetic
/// layer (also not shown in v1).</summary>
public enum EquipSlotType
{
    None = 0,
    Head = 1,
    Armor = 2,        // was Body
    Weapon = 3,
    Shield = 4,       // was OffHand — still the block source in combat
    Boots = 5,        // legacy; not a visible slot in the v1 window
    Accessory1 = 6,   // was Trinket
    Accessory2 = 7,
    Garment = 8,
    CostumeHead = 9,  // reserved cosmetic layer — not shown in v1 UI
    CostumeBody = 10,
    CostumeBack = 11,
    Ring = 12,        // the Ring of Progression vessel — holds the passive draft + active-skill ownership
}

/// <summary>Inventory tab bucket. Independent of <see cref="EquipSlotType"/>: an item with a
/// real slot is <see cref="Equip"/>; bag-only items split into Use / Material / Etc for the tabs.</summary>
public enum ItemCategory { Equip, Use, Material, Etc }

/// <summary>
/// A piece of gear or a bag item: an icon, which slot it fits, and (Phase F) the
/// stat bonuses it grants while equipped, or how much it heals when used. Create
/// more via Assets ▸ Create ▸ unwritten ▸ Item, or Tools ▸ unwritten ▸ Create
/// Starter Items.
/// </summary>
[CreateAssetMenu(fileName = "Item", menuName = "unwritten/Item")]
public class Item : ScriptableObject
{
    [Tooltip("Stable kebab id, e.g. 'item.life-potion'. Quests/loot/recipes reference THIS, never the display name. Must be unique.")]
    public string id = "item.new";
    public string displayName = "Item";
    public Sprite icon;

    [Tooltip("Which equipment slot this fits. None = bag-only (consumable/material).")]
    public EquipSlotType slot = EquipSlotType.None;

    [Tooltip("Weapon kind (combat feel + future class affinity + size matchup). None for non-weapons. " +
             "Advisory in the slice — informs stat fit and the deferred WeaponSizes table, not a hard class gate.")]
    public WeaponType weaponType = WeaponType.None;

    [Tooltip("Weapon only (Weapon slot): the FULL-SIZE melee/attack reach in world units this weapon grants while " +
             "equipped — a bow/spear reaches farther than a dagger. Drives Player3DAttacker.range on the wielder. " +
             "0 = leave the attacker's default range unchanged. Ignored for non-weapon items.")]
    public float attackRange;

    [Tooltip("The 3D model wielded in-hand while this weapon is equipped (live-3D characters mount it on the hand " +
             "bone via WeaponSocket). Its root WeaponGrip authors how it sits in the hand. None = no visible weapon. " +
             "Author the grip with Tools ▸ unwritten ▸ Weapons ▸ Weapon Grip Tuner.")]
    public GameObject equipModel;

    // V3: a weapon's kit is ROLLED PER COPY at the drop (WeaponSkillRoll → EquipmentInstance.skillIds), so it can
    // never live on the shared asset — two Swords off this def carry different skills. The old hand-authored
    // `grantedSkillIds` was removed for exactly that reason; don't re-add a def-level kit field.

    [Tooltip("Damage element for a weapon (what element its attacks deal); defensive/affinity element for gear. " +
             "Neutral = no special element. Feeds the Elements damage table when combat consumes it.")]
    public Element element = Element.Neutral;

    [Header("Identity (HUD relay spec)")]
    [Tooltip("Drives the slot BORDER color only (rarity is one independent visual channel).")]
    public Rarity rarity = Rarity.Common;
    [Tooltip("Which inventory tab this falls under. Equip = has a real slot.")]
    public ItemCategory category = ItemCategory.Etc;
    [Tooltip("Number of card sockets (◇ pips). 0 = no sockets. Only meaningful for equipment.")]
    public int cardSlots;
    [Tooltip("Display-only weight (v1). Real encumbrance lands with the loot loop.")]
    public int weight;
    [Tooltip("Sell value in zeny.")]
    public int zeny;

    [Header("Equipment bonuses (applied while equipped)")]
    public int bonusHP;
    public int bonusAttack;
    public int bonusDefense;

    [Header("Magic bonuses (applied while equipped)")]
    public int bonusMP;
    public int bonusMAtk;
    public int bonusMDef;

    [Header("Primary-stat bonuses (applied while equipped)")]
    public int bonusStr;
    public int bonusAgi;
    public int bonusVit;
    public int bonusInt;
    public int bonusDex;
    public int bonusLuk;

    [Header("Derived bonuses")]
    [Tooltip("Added to crit CHANCE, in whole percent (e.g. 5 = +5%).")]
    public int bonusCritPercent;
    [Tooltip("Added to attack SPEED, in whole percent (e.g. 4 = +4%). May be negative.")]
    public int bonusAspdPercent;

    [Header("Consumable")]
    [Tooltip("Right-click in the bag to heal this much, then consume it. 0 = not a consumable.")]
    public int healAmount;

    [Header("Combat audio (weapon / off-hand — overrides the player defaults while equipped)")]
    [Tooltip("Weapon: the swing sound.")]
    public SfxSet attackSfx;
    [Tooltip("Weapon: the impact sound when a swing lands.")]
    public SfxSet hitSfx;
    [Tooltip("Off-hand: the block/parry sound.")]
    public SfxSet guardSfx;
    [Tooltip("Off-hand: chance (0..1) to block an incoming hit (and play guardSfx).")]
    [Range(0f, 1f)] public float blockChance;

    [TextArea] public string description;
}
