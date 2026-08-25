using System;

/// <summary>
/// The serializable snapshot of a character's PROGRESSION. Produced and consumed by
/// <see cref="Character.CaptureSave"/> / <see cref="Character.RestoreSave"/>, and stored as one document in
/// the account service's database — the single place a character's data lives.
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
    public const int CurrentVersion = 7;

    // 0 means "nobody has played this character yet" — the server hands back an empty document for a freshly
    // created character, which deserializes to exactly this. CaptureSave stamps the real version, so a zero
    // can only ever mean unplayed. Without that distinction an empty document would look like a real level-1
    // save and quietly stand in for the fresh character the game is supposed to build.
    public int version;

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

    // Hotbar layout (v3+): slot → bound skill id ("" = empty), nine entries (keys 1–9). Null on an older save.
    public string[] hotbarBindings;


    // Passive skills ticked ON in the Grimoire (v7+) — the player's own choices only; a passive that's live because
    // the equipped weapon rolled it is NOT stored here (that answer comes from the weapon). Null on an older save.
    public string[] activePassives;

    // Equipped gear (v6+): parallel arrays, one entry per occupied slot EXCEPT the Ring vessel (auto-worn on
    // load by EnsureRingEquipped). Owned by Character. Null on a pre-v6 save → nothing equipped, ring re-worn.
    public int[]    equipSlots;     // (int)EquipSlotType
    public string[] equipItemIds;   // Item.id, resolved via ItemCatalog
    public int[]    equipRefines;   // +N enhancement
    public string[] equipCards;     // comma-joined card asset names ("" = none)
    // v7+: what makes each COPY unique now that a weapon rolls its own identity at the drop.
    public int[]    equipRarities;  // (int)Rarity rolled for this copy; null/short on a pre-v7 save → the def's rarity
    public string[] equipSkills;    // '|'-joined rolled SkillDef ids ("" = none)

    // Bag (v6+): parallel arrays, one entry per stack. Owned by InventoryStore (canvas-scoped); captured/
    // restored by SaveManager. Null on a pre-v6 save (or before the bag window is ever opened) → re-seed.
    // Equipment stacks carry refine/cards and count 1; stackables carry a count and refine 0 / cards "".
    public string[] bagItemIds;
    public int[]    bagCounts;
    public int[]    bagRefines;
    public string[] bagCards;
    public int[]    bagRarities;    // v7+: per-copy rolled rarity (see equipRarities)
    public string[] bagSkills;      // v7+: per-copy rolled kit (see equipSkills)

    // NOTE — APPEARANCE IS NOT HERE. Which body a character wears and how it is recoloured belong to the
    // CHARACTER RECORD on the server (see AccountSession), not to this progress document. They used to live
    // in both, which is two owners for one thing: the pair drifts the moment either side is written alone.
    // Appearance is identity — chosen once at creation and true before the character has ever been played —
    // while everything above is progress, which only exists after playing. Do not add them back here.
}
