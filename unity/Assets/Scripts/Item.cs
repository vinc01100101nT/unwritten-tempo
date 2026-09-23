using UnityEngine;

/// <summary>Which equipment slot an item fits. None = a bag-only item (consumable / material).
/// Integer values are PINNED so the rename from the old taxonomy (Body→Armor, OffHand→Shield) doesn't shift any
/// serialized scene/prefab/item data — only the names changed. The VISIBLE places are Head/Armor/Garment/Weapon/
/// Shield/Boots — shown to a player as Head, Body, Garment, Weapon, Off-hand and Shoes (see <see cref="EquipSlots"/>);
/// the three Costume* slots are reserved for a future cosmetic layer (not shown in v1).
///
/// <para>6 and 7 were the two accessory places, removed because the ring already fills that role. Never give those
/// numbers to a new slot: an old save or asset still holding one would silently land in it.</para></summary>
public enum EquipSlotType
{
    None = 0,
    Head = 1,
    Armor = 2,        // was Body
    Weapon = 3,
    Shield = 4,       // shown as "Off-hand" — still the block source in combat
    Boots = 5,        // shown as "Shoes"
    Garment = 8,
    CostumeHead = 9,  // reserved cosmetic layer — not shown in v1 UI
    CostumeBody = 10,
    CostumeBack = 11,
    // 12 was the Ring of Progression vessel. The ring is part of the character now, not a worn item, so no
    // slot holds it. Never give 12 to a new slot: an old save still naming it would silently land in it.
}

/// <summary>What KIND of crafting material an item is, and so what it can be used for. <see cref="MaterialKind.None"/>
/// is every item that is not a crafting material at all — the zero value, so the seventeen items authored before
/// crafting existed read correctly without being touched.
///
/// <para>The three crafts are the material a piece is made of: metal is forged, cloth is woven, leather is tanned.
/// <see cref="MaterialKind.Ink"/> writes a stat onto a piece and <see cref="MaterialKind.Salt"/> boosts a quench;
/// both are shared by all three crafts.</para></summary>
public enum MaterialKind { None = 0, Metal = 1, Cloth = 2, Leather = 3, Ink = 4, Salt = 5 }

/// <summary>The words a slot goes by on screen. A text inventory has to name the slot, because with no icon the
/// slot is the only thing telling a helm from a sword at a glance.
///
/// <para><see cref="Order"/> is a body's order, not alphabetical: a reader scans a body, so sorting a list by slot
/// groups it that way.</para></summary>
public static class EquipSlots
{
    public static string Label(EquipSlotType s)
    {
        switch (s)
        {
            case EquipSlotType.Head:        return "Head";
            case EquipSlotType.Armor:       return "Body";
            case EquipSlotType.Weapon:      return "Weapon";
            case EquipSlotType.Shield:      return "Off-hand";
            case EquipSlotType.Boots:       return "Shoes";
            case EquipSlotType.Garment:    return "Garment";
            case EquipSlotType.CostumeHead: return "Costume Head";
            case EquipSlotType.CostumeBody: return "Costume Body";
            case EquipSlotType.CostumeBack: return "Costume Back";
            default:                        return "";
        }
    }

    /// <summary>Where this slot sits when a list is grouped by slot. Anything unslotted sorts last.</summary>
    public static int Order(EquipSlotType s)
    {
        switch (s)
        {
            case EquipSlotType.Head:        return 0;
            case EquipSlotType.Armor:       return 1;
            case EquipSlotType.Weapon:      return 2;
            case EquipSlotType.Shield:      return 3;
            case EquipSlotType.Garment:     return 4;
            case EquipSlotType.Boots:       return 5;
            case EquipSlotType.CostumeHead: return 6;
            case EquipSlotType.CostumeBody: return 7;
            case EquipSlotType.CostumeBack: return 8;
            default:                        return 99;
        }
    }
}

/// <summary>Inventory tab bucket. Independent of <see cref="EquipSlotType"/>: an item with a
/// real slot is <see cref="Equip"/>; bag-only items split into Use / Material / Etc / Quest for the tabs.
///
/// <para>⚠️ APPEND ONLY — never reorder. The category is stored as a plain int on every item asset, so inserting a
/// member would silently re-file every existing item under a different bucket.</para></summary>
public enum ItemCategory { Equip, Use, Material, Etc, Quest }

/// <summary>
/// A piece of gear or a bag item: an icon, which slot it fits, and the stat bonuses it grants while equipped, or
/// the skill it runs when used. Create more via Assets ▸ Create ▸ unwritten ▸ Item.
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
    [Tooltip("Which inventory tab this falls under. Equip = has a real slot.")]
    public ItemCategory category = ItemCategory.Etc;
    [Tooltip("Number of card sockets (◇ pips). 0 = no sockets. Only meaningful for equipment.")]
    public int cardSlots;
    [Tooltip("Display-only weight (v1). Real encumbrance lands with the loot loop.")]
    public int weight;
    [Tooltip("Sell value in zeny.")]
    public int zeny;

    // A CRAFTED PIECE HAS NO NUMBERS OF ITS OWN. What a weapon or a piece of armour is worth comes from its folds
    // and its quality, worked out by the server when it is forged, folded or quenched, plus whatever Ink has
    // written on it — all of it on the COPY (EquipmentInstance), never on the shared design. The flat bonus fields
    // and the armour roll ranges that used to sit here are gone for that reason; do not add either back.

    [Header("Crafting")]
    [Tooltip("What this PIECE is made of, which decides where it is made and which material band it folds with: " +
             "Metal at a smith, Cloth at a loom, Leather at a tannery. None = not something anyone crafts " +
             "(a consumable, a material, or a piece nobody has given a craft to yet).")]
    public MaterialKind madeOf = MaterialKind.None;
    [Tooltip("What it costs to FORGE one, in units of its craft's band-1 material, plus gold. 0 units = nobody " +
             "can forge this piece.")]
    public int forgeMaterialCost;
    [Tooltip("Gold one forge of this piece costs.")]
    public int forgeGoldCost;

    [Header("Crafting material")]
    [Tooltip("What kind of crafting material this is. None = not a material at all (every non-material item).")]
    public MaterialKind materialKind = MaterialKind.None;
    [Tooltip("Which step of its kind this is. A Metal/Cloth/Leather material: the BAND, 1 (Raw) to 7 (A) — a piece " +
             "folds only with the band of its own quality. An Ink: its tier, 1 (Watery) to 5 (Sharp). A Salt: its " +
             "tier, 1 (Coarse) to 3 (Pure). Ignored when Material Kind is None; 0 there means nothing was chosen.")]
    public int materialStep;
    [Tooltip("The material ten of this one merge into — the next tier up. Only Ink and Salt merge; a band material " +
             "leaves this empty, and so does the top tier of Ink and Salt.")]
    public Item mergesInto;

    [Header("Consumable")]
    // ONE way to say what a consumable does: it runs a skill. The old `healAmount` int was deleted with this change —
    // it was a second, weaker effect system racing the composed one (heal only, no duration, no cure, no buff), and a
    // potion and a spell could disagree about what "heals 80" means. A restorative is a Heal effect, a food is a
    // sub-stat buff, an antidote is a Cleanse: all of it already exists in the Skill Composer.
    [Tooltip("The skill this item runs when used, e.g. \"skill.item.life-potion\". That skill must carry an On Item " +
             "Used trigger. Empty = this item isn't usable. The skill is self-cast and pays no mana or cooldown — " +
             "consuming the item IS the cost.")]
    public string useSkillId = "";

    /// <summary>True when using this item does something — the one test every consume path asks.</summary>
    public bool IsUsable => !string.IsNullOrEmpty(useSkillId);

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
