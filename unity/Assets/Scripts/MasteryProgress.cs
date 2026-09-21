using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHAT A CHARACTER HAS PRACTISED — one mastery per weapon type, one per skill, and the skills they have learned
/// because of them. It is PART OF THE CHARACTER and permanent: the weapon in hand caps what may be USED and what
/// may be EARNED, but nothing ever takes away what was earned before.
///
/// <para>A plain C# class, like the ring: <see cref="Character"/> owns one. Levels are DERIVED from the practice
/// (<see cref="MasteryMath"/>), never stored beside it, so the two can never disagree.</para>
/// </summary>
public sealed class MasteryProgress
{
    readonly Dictionary<WeaponType, float> weaponPoints = new Dictionary<WeaponType, float>();
    readonly Dictionary<string, float> skillUses = new Dictionary<string, float>();
    readonly HashSet<string> learned = new HashSet<string>();

    /// <summary>Raised whenever practice or the learned set changes, so a window can redraw.</summary>
    public event System.Action OnChanged;

    // ---- weapon mastery -----------------------------------------------------------------------------------

    /// <summary>Practice earned with <paramref name="weapon"/>, whatever is in hand now.</summary>
    public float PointsWith(WeaponType weapon)
        => weapon != WeaponType.None && weaponPoints.TryGetValue(weapon, out float p) ? p : 0f;

    /// <summary>The weapon mastery level EARNED with <paramref name="weapon"/> — before any cap.</summary>
    public int EarnedLevel(WeaponType weapon) => MasteryMath.WeaponLevelFrom(PointsWith(weapon));

    /// <summary>The level actually in use: what was earned, held to what the weapon in hand allows. A master with
    /// a poor blade is capped down, and gets it all back the moment a better one is held.</summary>
    public int UsableLevel(WeaponType weapon, Quality heldQuality)
        => Mathf.Min(EarnedLevel(weapon), CraftingMath.WeaponMasteryCap(heldQuality));

    /// <summary>True when this weapon's mastery has reached what the weapon in hand allows — past which fighting
    /// with it teaches nothing more.</summary>
    public bool CappedOut(WeaponType weapon, Quality heldQuality)
        => EarnedLevel(weapon) >= CraftingMath.WeaponMasteryCap(heldQuality);

    /// <summary>Add practice with a weapon. Refused once capped by what is in hand. Returns the levels gained.</summary>
    public int AddPractice(WeaponType weapon, float points, Quality heldQuality)
    {
        if (weapon == WeaponType.None || points <= 0f) return 0;
        if (CappedOut(weapon, heldQuality)) return 0;

        int before = EarnedLevel(weapon);
        float now = PointsWith(weapon) + points;
        // Never past the cap's own total, so a single huge fight cannot vault a poor weapon's limit.
        float ceiling = MasteryMath.WeaponPointsFor(CraftingMath.WeaponMasteryCap(heldQuality));
        weaponPoints[weapon] = Mathf.Min(now, ceiling);
        OnChanged?.Invoke();
        return EarnedLevel(weapon) - before;
    }

    /// <summary>Set the practice in one weapon outright, ignoring every cap — the testing door (<c>/mastery</c>),
    /// and nothing in the game calls it.</summary>
    public void SetPractice(WeaponType weapon, float points)
    {
        if (weapon == WeaponType.None) return;
        weaponPoints[weapon] = Mathf.Max(0f, points);
        OnChanged?.Invoke();
    }

    // ---- skill mastery ------------------------------------------------------------------------------------

    /// <summary>Uses recorded for one skill.</summary>
    public float UsesOf(string skillId)
        => !string.IsNullOrEmpty(skillId) && skillUses.TryGetValue(skillId, out float u) ? u : 0f;

    /// <summary>The skill mastery level EARNED for one skill — before any cap.</summary>
    public int EarnedSkillLevel(string skillId) => MasteryMath.SkillLevelFrom(UsesOf(skillId));

    /// <summary>The skill mastery level in use, held to what the weapon in hand allows.</summary>
    public int UsableSkillLevel(string skillId, Quality heldQuality)
        => Mathf.Min(EarnedSkillLevel(skillId), CraftingMath.SkillMasteryCap(heldQuality));

    /// <summary>Record one completed press. Refused once capped by what is in hand.</summary>
    public void AddUse(string skillId, float uses, Quality heldQuality)
    {
        if (string.IsNullOrEmpty(skillId) || uses <= 0f) return;
        int cap = CraftingMath.SkillMasteryCap(heldQuality);
        if (EarnedSkillLevel(skillId) >= cap) return;
        skillUses[skillId] = Mathf.Min(UsesOf(skillId) + uses, MasteryMath.SkillUsesFor(cap));
        OnChanged?.Invoke();
    }

    // ---- what has been learned ----------------------------------------------------------------------------

    /// <summary>True when this character has learned <paramref name="skillId"/>.</summary>
    public bool HasLearned(string skillId) => !string.IsNullOrEmpty(skillId) && learned.Contains(skillId);

    /// <summary>How many skills are known.</summary>
    public int LearnedCount => learned.Count;

    /// <summary>Learn one skill outright. Used by the server when a mastery level opens it, and by a save.</summary>
    public bool Learn(string skillId)
    {
        if (string.IsNullOrEmpty(skillId) || !learned.Add(skillId)) return false;
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>Learn everything this character's masteries now open, in the catalog's own order so a skill that
    /// needs another is always reached after it. Returns how many were newly learned.</summary>
    public int LearnWhatIsOpen()
    {
        int gained = 0;
        var all = SkillCatalog.All;
        // Two passes, because a skill may need one that is opened by the same call.
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def == null || string.IsNullOrEmpty(def.id) || learned.Contains(def.id)) continue;
                if (def.weaponType == WeaponType.None) continue;
                // AN UNWRITTEN LEVEL IS LEVEL ONE — the same reading the skill's own tooltip, the creature's
                // list and the Grimoire give it, so a pool authored without one is not learned by everybody.
                if (EarnedLevel(def.weaponType) < Mathf.Max(1, def.masteryLevel)) continue;
                if (!string.IsNullOrEmpty(def.needsSkillId) && !learned.Contains(def.needsSkillId)) continue;
                learned.Add(def.id);
                gained++;
            }
        if (gained > 0) OnChanged?.Invoke();
        return gained;
    }

    // ---- save / load --------------------------------------------------------------------------------------

    /// <summary>Everything, as the flat arrays a save carries.</summary>
    public void Capture(out int[] weapons, out float[] points, out string[] skillIds, out float[] uses, out string[] known)
    {
        weapons = new int[weaponPoints.Count];
        points = new float[weaponPoints.Count];
        int at = 0;
        foreach (var kv in weaponPoints) { weapons[at] = (int)kv.Key; points[at] = kv.Value; at++; }

        skillIds = new string[skillUses.Count];
        uses = new float[skillUses.Count];
        at = 0;
        foreach (var kv in skillUses) { skillIds[at] = kv.Key; uses[at] = kv.Value; at++; }

        known = new string[learned.Count];
        learned.CopyTo(known);
    }

    /// <summary>Replace everything from a save. A skill id the game no longer has is dropped, so a retired skill
    /// cannot linger as something nobody can cast.</summary>
    public void Restore(int[] weapons, float[] points, string[] skillIds, float[] uses, string[] known)
    {
        weaponPoints.Clear();
        skillUses.Clear();
        learned.Clear();

        if (weapons != null && points != null)
            for (int i = 0; i < weapons.Length && i < points.Length; i++)
            {
                var w = (WeaponType)weapons[i];
                if (w != WeaponType.None && points[i] > 0f) weaponPoints[w] = points[i];
            }

        if (skillIds != null && uses != null)
            for (int i = 0; i < skillIds.Length && i < uses.Length; i++)
                if (!string.IsNullOrEmpty(skillIds[i]) && SkillCatalog.IsKnown(skillIds[i]) && uses[i] > 0f)
                    skillUses[skillIds[i]] = uses[i];

        if (known != null)
            for (int i = 0; i < known.Length; i++)
                if (!string.IsNullOrEmpty(known[i]) && SkillCatalog.IsKnown(known[i])) learned.Add(known[i]);

        OnChanged?.Invoke();
    }
}
