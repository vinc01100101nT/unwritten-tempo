using System;
using System.Collections.Generic;
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
    public const int Version = 2;
    public const int SkillSlots = 4;

    public int v = Version;
    /// <summary>EACH WEAPON'S OWN FOUR SLOTS (v2): the skills auto-combat presses while that weapon is in hand, by id;
    /// "" for an empty slot. Skills only — never an item or gear. At most one entry per weapon type, so the list can
    /// never hold more entries than there are weapon types; a weapon with every slot empty has no entry.</summary>
    public List<WeaponAutoSlots> weapons = new List<WeaponAutoSlots>();
    /// <summary>READ ONLY TO CARRY A v1 DOCUMENT FORWARD: the four slots every weapon shared before v2.
    /// <see cref="AutoCombatMath.Clamped"/> moves each skill in it to its own weapon's slots and leaves this empty.</summary>
    public string[] slots = Array.Empty<string>();
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

    static readonly string[] NoSlots = Array.Empty<string>();

    /// <summary>The slots auto-combat presses while <paramref name="weapon"/> is in hand — empty when it has none.
    /// Read only; <see cref="EditableSlotsFor"/> is the one that may add an entry.</summary>
    public string[] SlotsFor(WeaponType weapon)
    {
        var e = Find(weapon);
        return e != null && e.slots != null ? e.slots : NoSlots;
    }

    /// <summary><paramref name="weapon"/>'s four slots, made (all empty) when it has none yet — to put a skill in.</summary>
    public string[] EditableSlotsFor(WeaponType weapon)
    {
        var e = Find(weapon);
        if (e == null)
        {
            e = new WeaponAutoSlots { weapon = weapon.ToString() };
            if (weapons == null) weapons = new List<WeaponAutoSlots>();
            weapons.Add(e);
        }
        if (e.slots == null || e.slots.Length != SkillSlots) e.slots = AutoCombatMath.FourSlots(e.slots);
        return e.slots;
    }

    WeaponAutoSlots Find(WeaponType weapon)
    {
        if (weapons == null) return null;
        string name = weapon.ToString();
        for (int i = 0; i < weapons.Count; i++)
            if (weapons[i] != null && weapons[i].weapon == name) return weapons[i];
        return null;
    }

    /// <summary>Is <paramref name="skillId"/> in any weapon's slots?</summary>
    public bool Holds(string skillId)
    {
        if (string.IsNullOrEmpty(skillId) || weapons == null) return false;
        for (int i = 0; i < weapons.Count; i++)
            if (weapons[i]?.slots != null && Array.IndexOf(weapons[i].slots, skillId) >= 0) return true;
        return false;
    }

    /// <summary>Every skill in any weapon's slots, added to <paramref name="into"/>.</summary>
    public void CollectAll(ICollection<string> into)
    {
        if (weapons == null) return;
        for (int i = 0; i < weapons.Count; i++)
        {
            var s = weapons[i]?.slots;
            if (s == null) continue;
            for (int j = 0; j < s.Length; j++) if (!string.IsNullOrEmpty(s[j])) into.Add(s[j]);
        }
    }

    /// <summary>Do <paramref name="a"/> and <paramref name="b"/> hold the same skills in the same slots of the same
    /// weapons?</summary>
    public static bool SameSlots(AutoCombatSettings a, AutoCombatSettings b)
    {
        if (a == null || b == null) return a == b;
        int na = a.weapons?.Count ?? 0, nb = b.weapons?.Count ?? 0;
        if (na != nb) return false;
        for (int i = 0; i < na; i++)
        {
            var x = a.weapons[i]; var y = b.weapons[i];
            if (x == null || y == null) { if (x != y) return false; continue; }
            if (x.weapon != y.weapon || !SameArray(x.slots, y.slots)) return false;
        }
        return true;
    }

    static bool SameArray(string[] a, string[] b)
    {
        if (a == null || b == null) return a == b;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        return true;
    }

    public AutoCombatSettings Clone()
    {
        var c = (AutoCombatSettings)MemberwiseClone();
        c.slots = slots != null ? (string[])slots.Clone() : null;
        c.weapons = new List<WeaponAutoSlots>(weapons != null ? weapons.Count : 0);
        if (weapons != null)
            foreach (var w in weapons)
                if (w != null) c.weapons.Add(new WeaponAutoSlots { weapon = w.weapon, slots = w.slots != null ? (string[])w.slots.Clone() : null });
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

/// <summary>One weapon's auto-combat slots, keyed by the weapon type's NAME so a renumbered type cannot hand one weapon's
/// skills to another.</summary>
[Serializable]
public class WeaponAutoSlots
{
    public string weapon;
    public string[] slots = { "", "", "", "" };
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

    /// <summary>The settings as the server keeps them: every number inside its range; one entry per weapon type that
    /// exists (not None), each with exactly <see cref="AutoCombatSettings.SkillSlots"/> slots and no nulls, and none
    /// with every slot empty; the hunt mode one that exists. A v1 document's shared slots are carried to the weapon each
    /// skill belongs to (<paramref name="weaponOf"/>), in their order. Never returns null.</summary>
    public static AutoCombatSettings Clamped(AutoCombatSettings s, Func<string, WeaponType> weaponOf)
    {
        var c = s != null ? s.Clone() : new AutoCombatSettings();
        c.v = AutoCombatSettings.Version;
        var kept = new List<WeaponAutoSlots>();
        if (c.weapons != null)
            foreach (var w in c.weapons)
            {
                if (w == null || !Enum.TryParse(w.weapon, false, out WeaponType type) || type == WeaponType.None
                    || !Enum.IsDefined(typeof(WeaponType), type) || type.ToString() != w.weapon) continue;
                bool seen = false;
                foreach (var k in kept) if (k.weapon == w.weapon) { seen = true; break; }
                if (!seen) kept.Add(new WeaponAutoSlots { weapon = w.weapon, slots = FourSlots(w.slots) });
            }
        c.weapons = kept;
        if (c.slots != null && weaponOf != null)
            foreach (var id in c.slots)
            {
                if (string.IsNullOrEmpty(id)) continue;
                var type = weaponOf(id);
                if (type == WeaponType.None) continue;
                var into = c.EditableSlotsFor(type);
                int free = Array.IndexOf(into, "");
                if (free >= 0 && Array.IndexOf(into, id) < 0) into[free] = id;
            }
        c.slots = Array.Empty<string>();
        DropEmptyWeapons(c.weapons);
        c.lootRateAtMostPercent = Percent(c.lootRateAtMostPercent);
        c.hpPotionBelowPercent = Percent(c.hpPotionBelowPercent);
        c.mpPotionBelowPercent = Percent(c.mpPotionBelowPercent);
        c.huntMode = (int)c.Hunt;
        return c;
    }

    /// <summary>Exactly <see cref="AutoCombatSettings.SkillSlots"/> slots from <paramref name="from"/>, "" for any missing.</summary>
    public static string[] FourSlots(string[] from)
    {
        var slots = new string[AutoCombatSettings.SkillSlots];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = from != null && i < from.Length && from[i] != null ? from[i] : "";
        return slots;
    }

    /// <summary>Take out every weapon whose slots are all empty, so the list holds only weapons with a skill set.</summary>
    public static void DropEmptyWeapons(List<WeaponAutoSlots> weapons)
    {
        if (weapons == null) return;
        for (int i = weapons.Count - 1; i >= 0; i--)
        {
            var s = weapons[i]?.slots;
            bool any = false;
            if (s != null) for (int j = 0; j < s.Length; j++) if (!string.IsNullOrEmpty(s[j])) { any = true; break; }
            if (!any) weapons.RemoveAt(i);
        }
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
