using UnityEngine;

/// <summary>
/// The ONE place a piece of armour's per-copy numbers are rolled — the armour sibling of <see cref="WeaponSkillRoll"/>.
/// A Head, Armor, Garment or Shield copy rolls once, when it enters the world: its defence and magic defence inside the
/// ranges its <see cref="Item"/> authors, how many main stats it carries, which of the six they are (never the same one
/// twice) and how much each is worth. The result lives on the copy (<see cref="EquipmentInstance"/>), never on the shared
/// asset, so two copies of one piece can differ.
///
/// <para>It rolls RAW numbers only. What rarity adds to them is <see cref="Rarities.Lifted"/> and
/// <see cref="Rarities.StatExtra"/>, applied when the copy's bonuses are added up — so a saved roll never has a rarity
/// baked into it. Every die goes through <see cref="CombatRng"/>.</para>
/// </summary>
public static class ArmourRoll
{
    /// <summary>The places whose copies roll: Head, Armor, Garment and Shield.</summary>
    public static bool IsRolledSlot(EquipSlotType s) =>
        s == EquipSlotType.Head || s == EquipSlotType.Armor || s == EquipSlotType.Garment || s == EquipSlotType.Shield;

    // Which of the six main stats are still free to pick, rebuilt per roll. A drop is a cold path; scratch anyway.
    static readonly int[] free = new int[EquipmentInstance.MainStatCount];

    /// <summary>Roll <paramref name="inst"/>'s armour numbers from its item's authored ranges and mark it rolled.</summary>
    public static void Roll(EquipmentInstance inst)
    {
        var def = inst != null ? inst.def : null;
        if (def == null) return;
        inst.EnsureStatArray();

        inst.rolledDefense = Mathf.Max(0, Between(def.defenseMin, def.defenseMax));
        inst.rolledMagicDefense = Mathf.Max(0, Between(def.magicDefenseMin, def.magicDefenseMax));

        System.Array.Clear(inst.rolledStats, 0, inst.rolledStats.Length);
        int count = Mathf.Clamp(Between(def.mainStatCountMin, def.mainStatCountMax), 0, EquipmentInstance.MainStatCount);
        int left = EquipmentInstance.MainStatCount;
        for (int i = 0; i < left; i++) free[i] = i;
        for (int n = 0; n < count; n++)
        {
            // Swap-remove, so a stat can never be picked twice.
            int pick = CombatRng.Range(0, left);
            int stat = free[pick];
            free[pick] = free[left - 1];
            left--;
            // A picked stat is CARRIED, so it is at least 1 — a carried 0 would read as "not carried" and lose rarity's extra.
            inst.rolledStats[stat] = Mathf.Max(1, Between(def.mainStatValueMin, def.mainStatValueMax));
        }
        inst.rolled = true;
    }

    /// <summary>The middle of the defence and magic-defence ranges, with no dice — what the editor's creature table
    /// shows for armour a creature wears, since the real copy is only rolled when the creature spawns. Main stats are
    /// left at none: WHICH stats a copy carries is random, so no single answer describes them.</summary>
    public static void FillMidpoints(EquipmentInstance inst)
    {
        var def = inst != null ? inst.def : null;
        if (def == null) return;
        inst.EnsureStatArray();
        inst.rolledDefense = Mathf.Max(0, Middle(def.defenseMin, def.defenseMax));
        inst.rolledMagicDefense = Mathf.Max(0, Middle(def.magicDefenseMin, def.magicDefenseMax));
        System.Array.Clear(inst.rolledStats, 0, inst.rolledStats.Length);
        inst.rolled = true;
    }

    // A whole number inside [min, max], both ends included. A maximum authored below its minimum rolls the minimum.
    static int Between(int min, int max) => max <= min ? min : CombatRng.Range(min, max + 1);

    static int Middle(int min, int max) => max <= min ? min : Mathf.RoundToInt((min + max) * 0.5f);
}
