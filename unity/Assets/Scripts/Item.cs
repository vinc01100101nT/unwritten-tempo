using UnityEngine;

/// <summary>Which equipment slot an item fits. None = a bag-only item (consumable / material).
/// Integer values are PINNED so the rename from the old taxonomy (Body→Armor) doesn't shift any serialized
/// scene/prefab/item data — only the names changed. The VISIBLE places are Head/Armor/Garment/Weapon/Boots — shown to a
/// player as Head, Body, Garment, Weapon and Shoes (see <see cref="EquipSlots"/>), plus Costume Body — a look-only
/// layer whose model is the wearer's body in the costume (<see cref="CostumePiece"/>). Costume Head and Costume Back are
/// reserved and not shown.
///
/// <para>6 and 7 were the two accessory places, removed because the ring already fills that role. Never give those
/// numbers to a new slot: an old save or asset still holding one would silently land in it.</para></summary>
public enum EquipSlotType
{
    None = 0,
    Head = 1,
    Armor = 2,        // was Body
    Weapon = 3,
    // 4 was the off-hand, removed on 2026-10-06 with every shield: blocking comes from skills now. Never give 4 to a
    // new slot: an old save still naming it would silently land in it.
    Boots = 5,        // shown as "Shoes"
    Garment = 8,
    // Costume Head and Costume Back are reserved and not shown; Costume Body is worn and shown.
    CostumeHead = 9,
    CostumeBody = 10,
    CostumeBack = 11,
    // 12 was the Ring of Progression vessel. The ring is part of the character now, not a worn item, so no
    // slot holds it. Never give 12 to a new slot: an old save still naming it would silently land in it.
}

/// <summary>What KIND of crafting material an item is, and so what it can be used for. <see cref="MaterialKind.None"/>
/// is every item that is not a crafting material at all — the zero value, so the seventeen items authored before
/// crafting existed read correctly without being touched.
///
/// <para>The three crafts are the material a piece is made of: metal at a smith, cloth at a loom, leather at a
/// tannery. <see cref="MaterialKind.TemperDust"/> is what every fold takes and <see cref="MaterialKind.QuenchOil"/>
/// what every quench takes; <see cref="MaterialKind.Ink"/> writes a stat onto a piece and
/// <see cref="MaterialKind.Salt"/> boosts a quench — all four shared by the three crafts.
/// <see cref="MaterialKind.Creature"/> is the material only one creature drops (<see cref="CreatureMaterials"/>): no
/// station works it, and its step is its rarity tier.</para>
///
/// <para>⚠️ The numbers are saved on every item: never renumber, and a new kind takes the next number.</para></summary>
public enum MaterialKind { None = 0, Metal = 1, Cloth = 2, Leather = 3, Ink = 4, Salt = 5, Creature = 6, TemperDust = 7, QuenchOil = 8 }

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
            case EquipSlotType.Garment:     return 3;
            case EquipSlotType.Boots:       return 4;
            case EquipSlotType.CostumeHead: return 5;
            case EquipSlotType.CostumeBody: return 6;
            case EquipSlotType.CostumeBack: return 7;
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

    [Tooltip("The 3D model shown on the body while this is equipped. A weapon: wielded in-hand (WeaponSocket mounts " +
             "it on the hand bone; its root WeaponGrip authors how it sits — Tools ▸ unwritten ▸ Weapons ▸ Weapon " +
             "Grip Tuner). A Head item: a headgear piece (its root HeadPiece) that HeadSocket puts on the head. " +
             "A Garment: a back piece such as a cape (its root BackPiece) that GarmentSocket hangs on the upper back. " +
             "A Costume Body: a costume (its root CostumePiece) that CostumeSocket swaps in for the wearer's body. " +
             "None = nothing shown.")]
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
    [Tooltip("How heavy it is. A weapon's weight sets how long it takes to draw and to put away (0.2 s plus 0.006 s " +
             "per point) and the time between its swings (0.8 s plus 0.005 s per point a hand carries — a pair " +
             "weapon's two hands carry half each).")]
    public int weight;
    [Tooltip("What one is worth, in Unsigned — the ONE price of this item. A shop sells it for this plus Tax, and buys " +
             "it for this less Tax (ShopMath). Whether a shop buys it at all is the Unsellable tag, never this price.")]
    public int value;

    // THE TAGS ARE THE ONE RULE for each thing a player may do with an item (decided 2026-10-16): nothing else —
    // not its price, not its category — decides whether it can be sold, dropped, stored or traded. Each is checked by
    // the server at the one place that action is decided, and every window reads the same tag.
    [Header("Tags")]
    [Tooltip("Cannot be put on the ground.")]
    public bool undroppable;
    [Tooltip("No shop buys it. An item priced 0 without this tag sells for 0.")]
    public bool unsellable;
    [Tooltip("Cannot go into account storage.")]
    public bool unstorable;
    [Tooltip("Cannot be handed to another player.")]
    public bool untradeable;

    /// <summary>Can a shop buy this: it is not tagged Unsellable. The one rule, for every kind of item.</summary>
    public bool CanBeSold => !unsellable;
    /// <summary>Can this be put on the ground: it is not tagged Undroppable.</summary>
    public bool CanBeDropped => !undroppable;
    /// <summary>Can this go into account storage: it is not tagged Unstorable.</summary>
    public bool CanBeStored => !unstorable;
    /// <summary>Can this be handed to another player: it is not tagged Untradeable. Nothing trades yet; trading reads this.</summary>
    public bool CanBeTraded => !untradeable;

    /// <summary>What every readout says in place of a sell value of 0.</summary>
    public const string CannotBeSoldText = "Cannot be sold to NPC";
    public const string CannotBeDroppedText = "Cannot be dropped";
    public const string CannotBeStoredText = "Cannot be stored";
    public const string CannotBeTradedText = "Cannot be traded";

    /// <summary>The red chat line when a player tries what a tag forbids: "Item cannot be dropped".</summary>
    public static string RefusedLine(string restriction) => "Item " + char.ToLowerInvariant(restriction[0]) + restriction.Substring(1);

    // A CRAFTED PIECE HAS NO NUMBERS OF ITS OWN. What a weapon or a piece of armour is worth comes from its folds,
    // its quality and its rarity, worked out by the server when it is made, folded or quenched, plus whatever Ink has
    // written on it — all of it on the COPY (EquipmentInstance), never on the shared design. The flat bonus fields
    // and the armour roll ranges that used to sit here are gone for that reason; do not add either back.
    // HEADGEAR IS NOT CRAFTED: a hat never rolls, folds or takes a rarity, so every copy of it is the same and what it
    // adds is written once, below, on the design.

    [Header("Headgear")]
    [Tooltip("Headgear only: what wearing it adds. The same on every copy — a hat is never rolled, folded or quenched.")]
    public HeadgearBonus headgear;
    [Tooltip("Headgear only: the passive skill that runs while it is worn, e.g. \"skill.headgear.top-hat\". It must " +
             "have no On Cast trigger. Empty = it runs none.")]
    public string wornSkillId = "";

    [Header("Crafting")]
    [Tooltip("What this PIECE is made of, which decides the station that works it: Metal at a smith, Cloth at a " +
             "loom, Leather at a tannery. None = not something any station works (a consumable, a material, or a " +
             "piece nobody has given a craft to yet). A worn piece made of None is worn for its look alone: it rolls " +
             "no word and is never handed out at random.")]
    public MaterialKind madeOf = MaterialKind.None;

    [Header("Crafting material")]
    [Tooltip("What kind of crafting material this is. None = not a material at all (every non-material item).")]
    public MaterialKind materialKind = MaterialKind.None;
    [Tooltip("Which step of its kind this is. An Ink: its tier, 1 (Watery) to 5 (Sharp). A Salt: its tier, 1 " +
             "(Coarse) to 3 (Pure). A Creature material: its rarity, 1 (Common) to 5 (Legendary). Temper Dust and " +
             "Quench Oil have no steps. Ignored when Material Kind is None; 0 there means nothing was chosen.")]
    public int materialStep;
    [Tooltip("The material ten of this one merge into — the next tier up. Only Ink and Salt merge; every other " +
             "material leaves this empty, and so does the top tier of Ink and Salt.")]
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

    [Header("Combat audio (weapon — overrides the player defaults while equipped)")]
    [Tooltip("Weapon: the swing sound.")]
    public SfxSet attackSfx;
    [Tooltip("Weapon: the impact sound when a swing lands.")]
    public SfxSet hitSfx;

    [TextArea] public string description;
}

/// <summary>What a piece of headgear adds while it is worn — the same on every copy. Read by <see cref="Headgear"/>.</summary>
[System.Serializable]
public struct HeadgearBonus
{
    public int str, agi, vit, intel, dex, luk;
    [Tooltip("World units added to how far a basic attack reaches.")]
    public float attackReach;
    [Tooltip("Whole percent added to the experience every kill pays (15 = +15%).")]
    public float experiencePercent;
}
