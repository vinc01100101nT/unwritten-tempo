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
/// Out of scope for this slice: equipment + bag (UI-layer; lands with the inventory milestone, M3).
/// Bump <see cref="CurrentVersion"/> and branch in RestoreSave if the shape ever changes.
/// </summary>
[Serializable]
public class SaveData
{
    public const int CurrentVersion = 4;

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
}
