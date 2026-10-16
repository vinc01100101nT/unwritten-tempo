using System;
using UnityEngine;

/// <summary>How somebody hunts while auto-combat is on.</summary>
public enum HuntMode
{
    /// <summary>Fight only what comes within reach, and never take a step.</summary>
    Standing = 0,
    /// <summary>Fight around the spot auto-combat was switched on at (see <see cref="AutoCombatMath.StartWithin"/>).</summary>
    Radius = 1,
    /// <summary>Go after whatever is nearest, anywhere on the map. Only where the server allows it.</summary>
    FreeRoam = 2,
}

/// <summary>
/// A CHARACTER'S AUTO-COMBAT SETTINGS — exactly the document the character server keeps in its own row
/// (accounts/sql/013_character_auto_combat.sql), so the field names here ARE the stored keys and nothing translates
/// between them. Written only when the player changes a setting; read with the character when it is handed to a map
/// server. A character that never changed one has no row, which reads as these defaults: empty slots, auto-loot
/// everything at 100%, a healing item below 70% health, no mana draught, Free Roam.
///
/// <para>Whether auto-combat is ON is not in here and is never saved: it is the server's to decide while somebody is
/// playing (see <c>NetPlayer</c>'s auto-combat half), and a sign-in always starts with it off.</para>
/// </summary>
[Serializable]
public class AutoCombatSettings
{
    public const int Version = 1;
    public const int SkillSlots = 4;

    public int v = Version;
    /// <summary>The skills auto-combat presses, by id; "" for an empty slot. Skills only — never an item or gear.</summary>
    public string[] slots = { "", "", "", "" };
    /// <summary>Auto-loot takes a thing only when its drop chance was at most this many percent — 100 takes everything
    /// the filters below allow, 5 only the rare ones, 0 nothing.</summary>
    public float lootRateAtMostPercent = 100f;
    public bool lootGears;
    public bool lootConsumables;
    public bool lootMaterials;
    /// <summary>Every kind of thing, whatever the three above say.</summary>
    public bool lootAll = true;
    /// <summary>Drink a healing item when health falls below this many percent of the most; 0 = never.</summary>
    public float hpPotionBelowPercent = 70f;
    /// <summary>Drink a mana draught when mana falls below this many percent of the most; 0 = never.</summary>
    public float mpPotionBelowPercent;
    public int huntMode = (int)HuntMode.FreeRoam;

    public HuntMode Hunt => (HuntMode)Mathf.Clamp(huntMode, 0, (int)HuntMode.FreeRoam);

    public AutoCombatSettings Clone()
    {
        var c = (AutoCombatSettings)MemberwiseClone();
        c.slots = slots != null ? (string[])slots.Clone() : null;
        return c;
    }

    public string ToJson() => JsonUtility.ToJson(this);

    /// <summary>Read a settings document, or the defaults when there is none or it cannot be read.</summary>
    public static AutoCombatSettings FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return new AutoCombatSettings();
        try { return JsonUtility.FromJson<AutoCombatSettings>(json) ?? new AutoCombatSettings(); }
        catch (ArgumentException) { return new AutoCombatSettings(); }
    }
}

/// <summary>
/// EVERY RULE AUTO-COMBAT FOLLOWS, as plain functions that take numbers and return answers — so each can be checked by
/// calling it, without standing a game up. The server decides with these (what it keeps of a setting, what it loots,
/// what it drinks, whether it may start); the player's machine uses only the distance rules, which steer its own legs
/// (the movement exception in CLAUDE.md).
/// </summary>
public static class AutoCombatMath
{
    /// <summary>Radius mode starts a fight only with a creature this close to the spot auto-combat began at…</summary>
    public const float StartWithin = 10f;
    /// <summary>…and drops it, walking back, once either of them is further from that spot than this.</summary>
    public const float KeepWithin = 14f;
    /// <summary>The fewest seconds between two drinks of the same kind — health, or mana.</summary>
    public const float SecondsBetweenDrinks = 1f;
    /// <summary>How often the player's machine looks at its auto-skill slots for one that is ready.</summary>
    public const float SecondsBetweenSkillLooks = 0.25f;
    /// <summary>A target that has neither lost health nor been walked any closer for this long is given up on — one the
    /// hunt cannot reach (a ledge, a wall, no route) or cannot hurt.</summary>
    public const float GiveUpAfterSeconds = 6f;
    /// <summary>…and is left alone for this long before the hunt may choose it again.</summary>
    public const float IgnoreGivenUpSeconds = 20f;
    /// <summary>How much closer (flat, in world units) counts as getting somewhere.</summary>
    public const float CloserByAtLeast = 0.5f;

    /// <summary>IS THE HUNT GETTING ANYWHERE with its target — has the target lost health, or has the gap closed by at
    /// least <see cref="CloserByAtLeast"/> on the closest it has been?</summary>
    public static bool MadeProgress(int targetHpNow, int targetHpBefore, float gapSqrNow, float closestGapSqr)
    {
        if (targetHpNow < targetHpBefore) return true;
        float closest = Mathf.Sqrt(closestGapSqr);
        float need = Mathf.Max(0f, closest - CloserByAtLeast);
        return gapSqrNow <= need * need;
    }

    /// <summary>Radius mode walks back to its spot once further from it than this, when nothing is being fought.</summary>
    public const float HomeSlack = 1.5f;

    /// <summary>The settings as the server keeps them: every number inside its range, the slot count exactly
    /// <see cref="AutoCombatSettings.SkillSlots"/> with no nulls, the hunt mode one that exists. Never returns null.</summary>
    public static AutoCombatSettings Clamped(AutoCombatSettings s)
    {
        var c = s != null ? s.Clone() : new AutoCombatSettings();
        c.v = AutoCombatSettings.Version;
        var slots = new string[AutoCombatSettings.SkillSlots];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = c.slots != null && i < c.slots.Length && c.slots[i] != null ? c.slots[i] : "";
        c.slots = slots;
        c.lootRateAtMostPercent = Percent(c.lootRateAtMostPercent);
        c.hpPotionBelowPercent = Percent(c.hpPotionBelowPercent);
        c.mpPotionBelowPercent = Percent(c.mpPotionBelowPercent);
        c.huntMode = (int)c.Hunt;
        return c;
    }

    static float Percent(float p) => float.IsNaN(p) ? 0f : Mathf.Clamp(Mathf.Round(p), 0f, 100f);

    /// <summary>The same skill twice in the slots is pressed once: every repeat after the first is emptied.</summary>
    public static void EmptyRepeats(string[] slots)
    {
        if (slots == null) return;
        for (int i = 0; i < slots.Length; i++)
        {
            if (string.IsNullOrEmpty(slots[i])) continue;
            for (int j = 0; j < i; j++)
                if (slots[j] == slots[i]) { slots[i] = ""; break; }
        }
    }

    /// <summary>MAY AUTO-COMBAT BE SWITCHED ON HERE, in this hunt mode — null when it may, otherwise the sentence the
    /// player is told. Never in a town; Free Roam only in a field or a dungeon.</summary>
    public static string WhyItMayNotStart(MapKind map, HuntMode mode, bool dead)
    {
        if (dead) return "You cannot hunt while dead.";
        if (map == MapKind.Town) return "Auto-combat does not work in town.";
        if (mode == HuntMode.FreeRoam && map != MapKind.Field && map != MapKind.Dungeon)
            return "Free Roam only works in a field or a dungeon.";
        return null;
    }

    // ---- hunting: distances on the flat ground, squared on both sides so nothing takes a root ---------------------

    /// <summary>Flat distance squared between two points — height ignored.</summary>
    public static float FlatSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    /// <summary>Radius mode: may a fight START with a creature standing at <paramref name="creature"/>?</summary>
    public static bool MayStartFightAt(Vector3 anchor, Vector3 creature)
        => FlatSqr(anchor, creature) <= StartWithin * StartWithin;

    /// <summary>Radius mode: may a fight already going on CONTINUE, with the character at <paramref name="me"/> and the
    /// creature at <paramref name="creature"/>? Both must still be inside <see cref="KeepWithin"/> of the spot. The gap
    /// between the two numbers is the hysteresis PERFORMANCE.md asks of any edge a chase is gated on.</summary>
    public static bool MayKeepFightingAt(Vector3 anchor, Vector3 me, Vector3 creature)
        => FlatSqr(anchor, me) <= KeepWithin * KeepWithin && FlatSqr(anchor, creature) <= KeepWithin * KeepWithin;

    /// <summary>Radius mode: is the character far enough from the spot to walk back to it while nothing is being
    /// fought?</summary>
    public static bool ShouldWalkBack(Vector3 anchor, Vector3 me) => FlatSqr(anchor, me) > HomeSlack * HomeSlack;

    // ---- auto-loot ------------------------------------------------------------------------------------------------

    /// <summary>What kind of thing an item is, for the loot filters.</summary>
    public enum LootKind { Gear, Consumable, Material, Other }

    public static LootKind KindOf(EquipSlotType slot, bool usable, ItemCategory category, MaterialKind material)
    {
        if (slot != EquipSlotType.None) return LootKind.Gear;
        if (usable || category == ItemCategory.Use) return LootKind.Consumable;
        if (material != MaterialKind.None || category == ItemCategory.Material) return LootKind.Material;
        return LootKind.Other;
    }

    /// <summary>DOES AUTO-LOOT TAKE THIS ONE WON ENTRY — a thing of <paramref name="kind"/> whose drop chance was
    /// <paramref name="chance01"/> (0–1). Its kind must be one the player ticked (or "everything"), and the chance at
    /// most their limit. A limit of 0 takes nothing.</summary>
    public static bool Takes(AutoCombatSettings s, LootKind kind, float chance01)
    {
        if (s == null || s.lootRateAtMostPercent <= 0f) return false;
        bool kindWanted = s.lootAll
                          || (kind == LootKind.Gear && s.lootGears)
                          || (kind == LootKind.Consumable && s.lootConsumables)
                          || (kind == LootKind.Material && s.lootMaterials);
        if (!kindWanted) return false;
        // Compared in whole hundredths of a percent, so a 5% drop under a 5% limit is never lost to float rounding.
        long chance = (long)Math.Round(Mathf.Clamp01(chance01) * 10000.0);
        long limit = (long)Math.Round(s.lootRateAtMostPercent * 100.0);
        return chance <= limit;
    }

    // ---- auto-potion ----------------------------------------------------------------------------------------------

    /// <summary>Is <paramref name="current"/> of <paramref name="most"/> below <paramref name="belowPercent"/>?
    /// A threshold of 0 is never.</summary>
    public static bool Below(int current, int most, float belowPercent)
    {
        if (belowPercent <= 0f || most <= 0) return false;
        // current/most < p/100  ⇔  current·100 < p·most — no division.
        return current * 100.0 < belowPercent * (double)most;
    }

    /// <summary>Has enough time passed since the last drink of this kind?</summary>
    public static bool MayDrinkAgain(float now, float lastDrankAt) => now - lastDrankAt >= SecondsBetweenDrinks;
}
