using System;

/// <summary>
/// The serializable snapshot of a character's progression (M1 save slice). Written to / read from
/// disk as JSON by <see cref="SaveSystem"/>; produced and consumed by <see cref="Character.CaptureSave"/>
/// and <see cref="Character.RestoreSave"/>.
///
/// JsonUtility can't serialize a Dictionary, so the Trio-Ring ranks are stored as two parallel arrays
/// (<see cref="rankStats"/> / <see cref="rankValues"/>). Every field has a safe default so an older or
/// partial save still loads.
///
/// Equipment + bag land in v6 (below). A pre-v6 save simply leaves those arrays null → the character
/// loads with no gear and the bag re-seeds from the starter items, exactly as before.
/// Bump <see cref="CurrentVersion"/> and branch in RestoreSave if the shape ever changes.
/// </summary>
[Serializable]
public class SaveData
{
    public const int CurrentVersion = 6;

    public int version = CurrentVersion;

    // Core progression.
    public int level = 1;
    public int xp;
    public int currentHP;
    public int currentMP;

    // RO-style primary stats (indexed by PrimaryStat) + unspent allocation points.
    public int[] primary;
    public int statPoints;

    // Trio-Ring passive walk (parallel arrays — see class summary).
    public StatId[] rankStats;
    public int[] rankValues;
    public int bankedPoints = 1;   // 1 = the level-1 free start
    public bool hasLeader;
    public StatId leader;

    // Ring of Progression vessel (v4+): its own level/XP track, separate from the character's. Pre-v4 saves
    // leave these 0 → RestoreSave derives the ring level from the points already granted (allocated + banked).
    public int ringLevel;
    public int ringXp;

    // Currency (M2 loot slice).
    public int gold;

    // Active-skill MODIFIER loadouts (Charter §12) — three parallel arrays, one entry per owned skill:
    // id, its tag set (comma-joined), and its six phase slots (comma-joined modifier ids). v2+.
    public string[] skillModIds;
    public string[] skillModTags;
    public string[] skillModSlots;

    // Class-agnostic active-skill OWNERSHIP (v3+): the stable ids the player has unlocked from the shared
    // pool (one chosen every 10 levels). Null/empty on an older save → nothing owned yet, re-earned by level.
    public string[] ownedSkillIds;

    // Hotbar layout (v3+): slot → bound skill id ("" = empty), nine entries (keys 1–9). Null on an older save.
    public string[] hotbarBindings;

    // Combo chains (v5+): one entry per chain, encoded "<hotkeyKeyCodeInt>|id0,id1,id2,id3" (empty slot = ""),
    // owned by ComboBook. Null on an older save → no chains yet.
    public string[] comboChains;

    // Equipped gear (v6+): parallel arrays, one entry per occupied slot EXCEPT the Ring vessel (auto-worn on
    // load by EnsureRingEquipped). Owned by Character. Null on a pre-v6 save → nothing equipped, ring re-worn.
    public int[]    equipSlots;     // (int)EquipSlotType
    public string[] equipItemIds;   // Item.id, resolved via ItemCatalog
    public int[]    equipRefines;   // +N enhancement
    public string[] equipCards;     // comma-joined card asset names ("" = none)

    // Bag (v6+): parallel arrays, one entry per stack. Owned by InventoryStore (canvas-scoped); captured/
    // restored by SaveManager. Null on a pre-v6 save (or before the bag window is ever opened) → re-seed.
    // Equipment stacks carry refine/cards and count 1; stackables carry a count and refine 0 / cards "".
    public string[] bagItemIds;
    public int[]    bagCounts;
    public int[]    bagRefines;
    public string[] bagCards;

    // Per-cloth-part outfit dye (v6+): parallel authored-sRGB hex arrays — the atlas source part-color and the
    // chosen color. Owned by Character, applied by CharacterDye3D. Null on an older save → no dye (art colors).
    public string[] outfitDyeSources;
    public string[] outfitDyeTargets;

    // Which character model the dye above was authored for (a signature of the worn mesh names). Lets
    // CharacterDye3D drop the dye when the model changes, so a color-keyed dye can't bleed onto a different
    // character that happens to share a base color. Null on an older save → adopted by the current model.
    public string outfitDyeModel;
}
