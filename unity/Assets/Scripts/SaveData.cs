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
    // v11: a copy's rolled armour numbers ride the save (equipRolls / bagRolls). Older saves carry none, which loads
    // every piece as never rolled — nothing to migrate.
    // v14: a forged weapon's trademark rides the same roll text. Older saves carry none, which loads every weapon
    // with no trademark — nothing to migrate.
    // v15: the timed effects still running ride the save (timedEffects). Older saves carry none, which loads as
    // "nothing running" — nothing to migrate.
    // v16: experience on the Ragnarok curve (UnitStatMath.LevelCost), the level capped at 99, the ring at a third of
    // it and weapon mastery at two thirds. An older save's level is kept and the experience inside each level keeps
    // the same share of the way through it (Character.CarryOntoRagnarokCurve).
    public const int CurrentVersion = 16;

    // 0 means "nobody has played this character yet" — the server hands back an empty document for a freshly
    // created character, which deserializes to exactly this. CaptureSave stamps the real version, so a zero
    // can only ever mean unplayed. Without that distinction an empty document would look like a real level-1
    // save and quietly stand in for the fresh character the game is supposed to build.
    public int version;

    // Core progression.
    public int level = 1;
    public double xp;   // to two decimals (v16+); an older whole number reads straight in

    // EVERY POINT OF EXPERIENCE THIS CHARACTER HAS EVER BEEN GIVEN (v10+), including the part already spent
    // climbing levels. `xp` above is only what is standing inside the CURRENT level — it is reset on each
    // level-up — so it can never answer "who has earned the most", which is what a leaderboard is asking.
    //
    // A save older than v10 carries no figure, and 0 is a perfectly real one (a level-1 character who has
    // never fought), so the two cannot be told apart by the value. `version` is what separates them: below 10,
    // RestoreSave works the figure out from the level curve instead.
    public long lifetimeXp;

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
    public double ringXp;   // to two decimals, like xp

    // Currency (M2 loot slice).
    public int gold;

    // Hotbar layout (v3+): slot → bound skill id ("" = empty), nine entries (keys 1–9). Null on an older save.
    public string[] hotbarBindings;



    // Mastery (v13+): what this character has practised, and what that has opened. Parallel arrays, because a
    // dictionary does not serialize. Null on an older save → nothing practised and nothing learned, which is
    // exactly where a character who has never trained stands.
    public int[]    masteryWeapons;    // (int)WeaponType
    public double[] masteryPoints;     // practice points to two decimals, parallel to masteryWeapons
    public string[] masterySkillIds;   // SkillDef.id
    public float[]  masterySkillUses;  // uses, parallel to masterySkillIds
    public string[] learnedSkillIds;   // every skill this character knows

    // Equipped gear (v6+): parallel arrays, one entry per occupied slot. Owned by Character. Null on a pre-v6
    // save → nothing equipped.
    public int[]    equipSlots;     // (int)EquipSlotType
    public string[] equipItemIds;   // Item.id, resolved via ItemCatalog
    public int[]    equipRefines;   // +N enhancement
    public string[] equipCards;     // comma-joined card asset names ("" = none)
    // v12+: what makes each COPY unique — its grade and the stats Ink has written on it
    // (EquipmentInstance.EncodeRoll). A pre-v12 save has the old rolled-armour text here, which does not read as a
    // grade, so every piece on it loads Raw with nothing written — see RestoreEquipment.
    public string[] equipRolls;

    // Bag (v6+): parallel arrays, one entry per stack. Owned by the CHARACTER, alongside its worn gear, and
    // written and read by Character itself. NULL means the save predates bags being stored at all, and that
    // character is given the starter goods; an EMPTY array is a real answer — somebody carrying nothing — and
    // must never be mistaken for the first.
    // Equipment stacks carry refine/cards and count 1; stackables carry a count and refine 0 / cards "".
    public string[] bagItemIds;
    public int[]    bagCounts;
    public int[]    bagRefines;
    public string[] bagCards;
    public string[] bagRolls;       // v12+: per-copy grade + written stats (see equipRolls)

    // Appearance (v8+). A player changes their body and their colours in the game, exactly as they change
    // everything else here, so this is where it lives. It used to sit in its own columns beside the save on
    // the server; that was two homes for one fact, written by two different requests, and either could land
    // without the other. THIS document is now the only home — do not add columns for it back.
    //
    // Written at creation, before the character has ever been played, which is why an unplayed character's
    // document is no longer empty. What marks a save as a save is `version`, nothing else.
    public string   modelName;
    public string[] dyeSources;
    public string[] dyeTargets;

    // Where you left off (v8+). Empty scene name = "nowhere recorded yet" → the game spawns you where it
    // always did, so a save written before this existed still loads.
    public string sceneName;
    public float  posX, posY, posZ;

    // OR THE ARRIVAL POINT TO STAND AT in sceneName, instead of the position — written only for somebody who left
    // while dead with no save point of their own (SavePoint.DefaultSpawnId). Empty = use the position.
    public string arriveAtSpawnId;

    // The sprint pool (v8+), saved for the same reason HP and MP are: it is a current value against a
    // maximum, and coming back with it full is a small free refill.
    public float stamina = -1f;   // below zero = not recorded; the character keeps its own starting value

    // WHERE THEY STAND BACK UP AFTER DYING — the map and the position inside it, set by the GM's /save. An empty
    // map means none was ever set, which is every save written before this existed too: they come back at
    // SavePoint.DefaultMap's default arrival point. No version bump — "not set" and "not recorded" are one fact.
    public string savePointMap;
    public float  savePointX, savePointY, savePointZ;

    // COOLDOWNS STILL RUNNING (v9+), as parallel arrays like the trio-ring walk above: the skill's id, and the
    // MOMENT it becomes ready again, counted in seconds since 1970 on the world's clock.
    //
    // A moment, not a countdown, because a countdown means nothing once it leaves the process that was counting
    // it — every process starts its own timer at zero. A moment survives being written down, a map being put to
    // sleep, and a character moving to another server, and needs no arithmetic on arrival.
    //
    // A skill with NO ENTRY HERE IS READY. That is not a default standing in for a missing value — "no cooldown
    // is running" and "nothing was written for this skill" are the same fact, which is why an older save needs
    // no upgrading. Never repurpose these arrays to say a skill IS available, or that stops being true.
    public string[] cooldownSkillIds;
    public double[] cooldownDueAt;

    // TIMED EFFECTS STILL RUNNING (v15+): every buff, debuff, charge, barrier and damage-over-time the character carried
    // when it was written down, each with the SECONDS IT HAD LEFT. Seconds left — not a moment, unlike the cooldowns
    // above — because a timed effect's clock STOPS while the character is away: it resumes where it stopped on the
    // next login, or on arrival at a map another server holds. Only death clears them (decided 2026-09-23).
    // Written and read back only by the server (TimedEffectSave). Null = nothing running.
    public TimedEffect[] timedEffects;

    /// <summary>One pile of a timed effect, as the save holds it. Its kind is saved BY NAME so a removed state can
    /// never repoint the record at a different one. Each stack is kept on its own, in parallel arrays.</summary>
    [Serializable]
    public class TimedEffect
    {
        public string kind;
        public string sourceSkillId;
        public bool stacking;
        public string description;
        public float secondsRunning;
        public float secondsToNextTick;
        public float[] stackSecondsLeft;
        public float[] stackMagnitude;
        public int[] stackAmount;
    }
}
