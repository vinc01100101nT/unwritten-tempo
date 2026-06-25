using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the "Build HUD Widgets" demo: at Play it fills its wired <see cref="ItemSlot"/>s with
/// throwaway equipment instances spanning every rarity / +N / card state, so the locked visual
/// channels (rarity border · +N badge · ◇ pips · cyan select/hover) can be eyeballed in a real
/// scene. Items are built in memory at runtime — nothing is saved to the project. Purely a
/// preview harness; not part of the shipping HUD.
/// </summary>
public class HudWidgetDemo : MonoBehaviour
{
    [Tooltip("Slots to populate, left→right. Wired by the HUD widget builder.")]
    public ItemSlot[] slots;
    [Tooltip("Demo icons cycled across the slots. Wired by the builder; may be empty.")]
    public Sprite[] icons;

    static readonly Rarity[] Tiers =
        { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary };

    // Hold the runtime-created assets so they aren't garbage-collected mid-session.
    readonly List<Object> keepAlive = new List<Object>();

    void Start()
    {
        if (slots == null) return;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;

            var rarity = Tiers[i % Tiers.Length];
            int cardSlots = i % 4;                    // 0..3 sockets
            int filled = Mathf.Min(cardSlots, i % 3); // some filled, some open
            int upgrade = (i * 2) % 10;               // +0,+2,+4,+6,+8,...

            var def = ScriptableObject.CreateInstance<Item>();
            def.name = rarity + " Demo";
            def.displayName = Rarities.Label(rarity) + " Blade";
            def.rarity = rarity;
            def.category = ItemCategory.Equip;
            def.slot = EquipSlotType.Weapon;
            def.cardSlots = cardSlots;
            def.weight = 90 + i * 10;
            def.zeny = 1000 * (i + 1);
            def.bonusAttack = 120 + i * 14;
            def.bonusStr = i;
            def.icon = (icons != null && icons.Length > 0) ? icons[i % icons.Length] : null;
            def.description = "Runtime demo item — shows the locked slot channels.";
            keepAlive.Add(def);

            var inst = new EquipmentInstance(def, upgrade);
            for (int c = 0; c < filled; c++)
            {
                var card = ScriptableObject.CreateInstance<Card>();
                card.name = "DemoCard" + c;
                card.displayName = "Demo Card " + (c + 1);
                card.blurb = "+20 ATK";
                card.bonusAttack = 20;
                keepAlive.Add(card);
                inst.cards.Add(card);
            }

            slots[i].SetInstance(inst);
        }

        // Light the first slot's cyan SELECT ring so the selection state is visible at rest;
        // hover any slot to see the cyan outer bloom + the live tooltip.
        if (slots.Length > 0 && slots[0] != null) slots[0].SetSelected(true);
    }
}
