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
    readonly Dictionary<WeaponType, double> weaponPoints = new Dictionary<WeaponType, double>();
    readonly Dictionary<string, float> skillUses = new Dictionary<string, float>();
    readonly HashSet<string> learned = new HashSet<string>();

    /// <summary>Raised whenever practice or the learned set changes, so a window can redraw.</summary>
    public event System.Action OnChanged;

    // ---- weapon mastery -----------------------------------------------------------------------------------

    /// <summary>Mastery experience earned with <paramref name="weapon"/>, whatever is in hand now.</summary>
    public double PointsWith(WeaponType weapon)
        => weapon != WeaponType.None && weaponPoints.TryGetValue(weapon, out double p) ? p : 0.0;

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

    /// <summary>Add experience to a weapon's mastery. Refused once capped by what is in hand. Returns the levels
    /// gained.</summary>
    public int AddXP(WeaponType weapon, double xp, Quality heldQuality)
    {
        if (weapon == WeaponType.None || xp <= 0.0) return 0;
        if (CappedOut(weapon, heldQuality)) return 0;

        int before = EarnedLevel(weapon);
        double now = UnitStatMath.RoundExperience(PointsWith(weapon) + xp);   // server-math: only Character.AddXP calls this, and only on the server
        // Never past the cap's own total, so a single huge kill cannot vault a poor weapon's limit.
        double ceiling = MasteryMath.WeaponXpFor(CraftingMath.WeaponMasteryCap(heldQuality));
        weaponPoints[weapon] = System.Math.Min(now, ceiling);
        OnChanged?.Invoke();
        return EarnedLevel(weapon) - before;
    }

    // ---- the level as the server sent it ------------------------------------------------------------------
    // A player's machine never works a level out of practice (CLAUDE.md, RULE NOCLIENTMATH): the server sends each
    // weapon's earned level beside its points, and a window draws that number as it arrived.
    readonly Dictionary<WeaponType, int> sentLevels = new Dictionary<WeaponType, int>();

    /// <summary>SERVER: the earned level of each weapon in <paramref name="weapons"/>, in the same order, to send.</summary>
    public int[] EarnedLevels(int[] weapons)
    {
        var levels = new int[weapons != null ? weapons.Length : 0];
        for (int i = 0; i < levels.Length; i++) levels[i] = EarnedLevel((WeaponType)weapons[i]);
        return levels;
    }

    /// <summary>PLAYER'S MACHINE: keep the levels the server sent, replacing the last set.</summary>
    public void TakeSentLevels(int[] weapons, int[] levels)
    {
        sentLevels.Clear();
        if (weapons != null && levels != null)
            for (int i = 0; i < weapons.Length && i < levels.Length; i++) sentLevels[(WeaponType)weapons[i]] = levels[i];
        OnChanged?.Invoke();
    }

    /// <summary>The weapon mastery level the server last sent for <paramref name="weapon"/> — 0 before any arrives.</summary>
    public int SentLevel(WeaponType weapon) => sentLevels.TryGetValue(weapon, out int l) ? l : 0;

    // ---- the practice toward the next level, as the server sent it ---------------------------------------
    readonly Dictionary<WeaponType, (double into, double size, bool capped)> sentProgress =
        new Dictionary<WeaponType, (double, double, bool)>();

    /// <summary>SERVER: for every weapon practised, and the one in hand even if it never was, how far its practice is
    /// into the current level, how many points that level takes, and whether the weapon in hand's grade has stopped
    /// it — finished numbers for a window to draw.</summary>
    public void CaptureProgress(WeaponType held, Quality heldQuality,
                                out int[] weapons, out double[] into, out double[] size, out bool[] capped)
    {
        int n = weaponPoints.Count + (held != WeaponType.None && !weaponPoints.ContainsKey(held) ? 1 : 0);
        weapons = new int[n]; into = new double[n]; size = new double[n]; capped = new bool[n];
        int at = 0;
        foreach (var kv in weaponPoints) weapons[at++] = (int)kv.Key;
        if (at < n) weapons[at] = (int)held;
        for (int i = 0; i < n; i++)
        {
            var w = (WeaponType)weapons[i];
            MasteryMath.WeaponProgress(PointsWith(w), out into[i], out size[i]);
            capped[i] = w == held && CappedOut(w, heldQuality);
        }
    }

    /// <summary>PLAYER'S MACHINE: keep the progress the server sent, replacing the last set.</summary>
    public void TakeSentProgress(int[] weapons, double[] into, double[] size, bool[] capped)
    {
        sentProgress.Clear();
        if (weapons != null && into != null && size != null && capped != null)
            for (int i = 0; i < weapons.Length && i < into.Length && i < size.Length && i < capped.Length; i++)
                sentProgress[(WeaponType)weapons[i]] = (into[i], size[i], capped[i]);
        OnChanged?.Invoke();
    }

    /// <summary>The progress the server last sent for <paramref name="weapon"/>; false before any arrives.</summary>
    public bool SentProgress(WeaponType weapon, out double into, out double size, out bool capped)
    {
        if (sentProgress.TryGetValue(weapon, out var p)) { into = p.into; size = p.size; capped = p.capped; return true; }
        into = size = 0.0; capped = false;
        return false;
    }

    /// <summary>Set the practice in one weapon outright, ignoring every cap — the testing door (<c>/mastery</c>),
    /// and nothing in the game calls it.</summary>
    public void SetPractice(WeaponType weapon, double points)
    {
        if (weapon == WeaponType.None) return;
        weaponPoints[weapon] = System.Math.Max(0.0, points);
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
                // list and the Skills window give it, so a pool authored without one is not learned by everybody.
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
    public void Capture(out int[] weapons, out double[] points, out string[] skillIds, out float[] uses, out string[] known)
    {
        weapons = new int[weaponPoints.Count];
        points = new double[weaponPoints.Count];
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
    public void Restore(int[] weapons, double[] points, string[] skillIds, float[] uses, string[] known)
    {
        weaponPoints.Clear();
        skillUses.Clear();
        learned.Clear();

        if (weapons != null && points != null)
            for (int i = 0; i < weapons.Length && i < points.Length; i++)
            {
                var w = (WeaponType)weapons[i];
                if (w != WeaponType.None && points[i] > 0.0) weaponPoints[w] = points[i];
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
