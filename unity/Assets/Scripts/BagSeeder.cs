using System.Linq;
using UnityEngine;

/// <summary>
/// Fills empty bag slots from the starter <see cref="Item"/> assets at runtime,
/// so the bag never depends on item references baked into the canvas prefab
/// (those go stale if the assets are deleted/regenerated). Lives on the UI canvas;
/// loads everything in <c>Resources/{resourcesFolder}</c> and drops them into the
/// bag's empty slots once, on the first Start.
/// </summary>
public class BagSeeder : MonoBehaviour
{
    [Tooltip("Resources subfolder holding the Item assets to seed (Assets/Resources/<this>).")]
    public string resourcesFolder = "Items";

    bool seeded;

    void Start()
    {
        if (seeded) return;
        seeded = true;

        var items = Resources.LoadAll<Item>(resourcesFolder)
            .OrderBy(i => i.displayName)
            .ToArray();
        if (items.Length == 0)
        {
            Debug.LogWarning($"[unwritten] BagSeeder found no items in Resources/{resourcesFolder}. " +
                             "Run Tools ▸ unwritten ▸ Create Starter Items.");
            return;
        }

        // Bag slots are the ItemSlots that accept anything (equip slots have a type).
        var slots = GetComponentsInChildren<ItemSlot>(true)
            .Where(s => s.accepts == EquipSlotType.None && s.Item == null)
            .ToList();

        for (int i = 0; i < slots.Count && i < items.Length; i++)
            slots[i].SetItem(items[i]);
    }
}
