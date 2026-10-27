using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHAT A CHARACTER HAS PRACTISED AND LEARNED — one mastery per weapon type, and the level of every skill they have
/// spent mastery points on. It is PART OF THE CHARACTER and permanent: the weapon in hand caps what may be USED and
/// what may be EARNED, but nothing in play takes away what was earned or bought.
///
/// <para>A plain C# class, like the ring: <see cref="Character"/> owns one. Mastery levels are DERIVED from the
/// practice, and the points a weapon has left are DERIVED from its level and what was spent (<see cref="MasteryMath"/>),
/// never stored beside them, so none of them can disagree.</para>
/// </summary>
public sealed class MasteryProgress
{
    readonly Dictionary<WeaponType, double> weaponPoints = new Dictionary<WeaponType, double>();
    // Skill id → the level bought. Only skills with a level of 1 or more have an entry: an entry IS "learned".
    readonly Dictionary<string, int> skillLevels = new Dictionary<string, int>(32);

    /// <summary>Raised whenever practice or a skill level changes, so a window can redraw.</summary>
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

    /// <summary>The weapon mastery level as this machine knows it: earned, on the server; as the server sent it, on a
    /// player's machine.</summary>
    public int KnownLevel(WeaponType weapon) => GameNetwork.TheWorldIsElsewhere ? SentLevel(weapon) : EarnedLevel(weapon);

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

    // ---- skill levels, bought with mastery points ----------------------------------------------------------

    /// <summary>The level bought in <paramref name="skillId"/> — 0 when it is not learned.</summary>
    public int LevelOf(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return 0;
        var def = SkillCatalog.Get(skillId);
        // A FREE FIRST LEVEL (the Staff's orbs and Release) is known by everyone; points bought sit on top of it.
        int free = def != null && def.freeFirstLevel ? 1 : 0;
        if (skillLevels.TryGetValue(skillId, out int l)) return l + free;
        if (free > 0) return free;
        // A COMPANION is never bought: it is known, at the same level, whenever the skill it rides is.
        return def != null && def.IsCompanion && def.companionOf != skillId ? LevelOf(def.companionOf) : 0;
    }

    /// <summary>True when this character has learned <paramref name="skillId"/> — at least one point is in it.</summary>
    public bool HasLearned(string skillId) => LevelOf(skillId) >= 1;

    /// <summary>How many skills are known.</summary>
    public int LearnedCount => skillLevels.Count;

    /// <summary>Changes every time a skill level does, so a sender can tell "nothing was bought" from "the same
    /// number of skills, different levels".</summary>
    public int LevelsVersion { get; private set; }

    /// <summary>The mastery points already spent on <paramref name="weapon"/>'s skills.</summary>
    public int PointsSpentOn(WeaponType weapon)
    {
        int spent = 0;
        foreach (var kv in skillLevels)
        {
            var def = SkillCatalog.Get(kv.Key);
            if (def != null && def.weaponType == weapon) spent += kv.Value;
        }
        return spent;
    }

    /// <summary>SERVER: the mastery points <paramref name="weapon"/> still has to spend — one per mastery level
    /// earned, less what is already in its skills.</summary>
    public int PointsLeft(WeaponType weapon)
        => weapon == WeaponType.None ? 0 : Mathf.Max(0, MasteryMath.SkillPointsFor(EarnedLevel(weapon)) - PointsSpentOn(weapon));

    /// <summary>SERVER: spend one mastery point on <paramref name="skillId"/>, raising it a level — learning it when it
    /// was at 0. Refused, with the reason in one sentence for the player, when the skill belongs to no weapon, is
    /// already at its highest level, opens at a mastery level not reached yet, needs a skill not learned yet, or its
    /// weapon has no point left.</summary>
    public bool TrySpend(string skillId, out string why)
    {
        why = null;
        // THE SERVER SPENDS, never the machine whose world is elsewhere: it asks, and wears what comes back.
        if (GameNetwork.TheWorldIsElsewhere) return false;
        var def = SkillCatalog.Get(skillId);
        if (def == null || def.weaponType == WeaponType.None || def.maxLevel <= 0 || def.IsCompanion)
        {
            why = "That skill cannot be learned.";
            return false;
        }
        string name = string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName;
        int level = LevelOf(skillId);
        if (level >= def.maxLevel)
        {
            why = name + " is already at its highest level.";
            return false;
        }
        int opensAt = Mathf.Max(1, def.masteryLevel);
        if (EarnedLevel(def.weaponType) < opensAt)
        {
            why = name + " opens at " + WeaponTypes.Label(def.weaponType) + " Mastery " + opensAt + ".";
            return false;
        }
        if (!string.IsNullOrEmpty(def.needsSkillId) && !HasLearned(def.needsSkillId))
        {
            var need = SkillCatalog.Get(def.needsSkillId);
            why = name + " needs " + (need != null ? need.displayName : def.needsSkillId) + " first.";
            return false;
        }
        if (PointsLeft(def.weaponType) <= 0)
        {
            why = "No " + WeaponTypes.Label(def.weaponType) + " mastery points left.";
            return false;
        }

        // What is STORED is the points bought; a free first level is never stored, so it is never refunded or counted.
        skillLevels[def.id] = level + 1 - (def.freeFirstLevel ? 1 : 0);
        LevelsVersion++;
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>SERVER: give a CREATURE <paramref name="skillId"/> at <paramref name="level"/> outright, with no weapon
    /// and no points — how a summoning kind holds the summon skill whatever it wields. A creature never spends points.</summary>
    public void GrantCreatureSkill(string skillId, int level)
    {
        if (string.IsNullOrEmpty(skillId) || level < 1 || LevelOf(skillId) == level) return;
        skillLevels[skillId] = level;
        LevelsVersion++;
        OnChanged?.Invoke();
    }

    /// <summary>SERVER: take back every point spent on <paramref name="weapon"/>'s skills (every weapon's, when it is
    /// <see cref="WeaponType.None"/>), so they can be spent again. The testing door (<c>/resetskills</c>) — nothing in
    /// the game calls it. Returns how many points came back.</summary>
    public int ResetPoints(WeaponType weapon)
    {
        int back = 0;
        resetScratch.Clear();
        foreach (var kv in skillLevels)
        {
            var def = SkillCatalog.Get(kv.Key);
            if (weapon != WeaponType.None && (def == null || def.weaponType != weapon)) continue;
            resetScratch.Add(kv.Key);
            back += kv.Value;
        }
        for (int i = 0; i < resetScratch.Count; i++) skillLevels.Remove(resetScratch[i]);
        if (resetScratch.Count > 0)
        {
            LevelsVersion++;
            OnChanged?.Invoke();
        }
        return back;
    }
    readonly List<string> resetScratch = new List<string>(16);

    // ---- the points left, as the server sent them -----------------------------------------------------------
    // A player's machine works out no points of its own (CLAUDE.md, RULE NOCLIENTMATH): the server sends each
    // weapon's points left beside its level, and the Skills window draws that number as it arrived.
    readonly Dictionary<WeaponType, int> sentPointsLeft = new Dictionary<WeaponType, int>();

    /// <summary>SERVER: the points left of each weapon in <paramref name="weapons"/>, in the same order, to send.</summary>
    public int[] PointsLeft(int[] weapons)
    {
        var left = new int[weapons != null ? weapons.Length : 0];
        for (int i = 0; i < left.Length; i++) left[i] = PointsLeft((WeaponType)weapons[i]);
        return left;
    }

    /// <summary>PLAYER'S MACHINE: keep the points left the server sent, replacing the last set.</summary>
    public void TakeSentPointsLeft(int[] weapons, int[] left)
    {
        sentPointsLeft.Clear();
        if (weapons != null && left != null)
            for (int i = 0; i < weapons.Length && i < left.Length; i++) sentPointsLeft[(WeaponType)weapons[i]] = left[i];
        OnChanged?.Invoke();
    }

    /// <summary>The points left the server last sent for <paramref name="weapon"/> — 0 before any arrives.</summary>
    public int SentPointsLeft(WeaponType weapon) => sentPointsLeft.TryGetValue(weapon, out int l) ? l : 0;

    // ---- what each level is worth, as the server sent it ----------------------------------------------------
    // The hover card shows every growing number now and at the next level. Those are game numbers, so the server
    // works them out (SkillLevelNumbers) and this machine only keeps and draws them.
    readonly Dictionary<string, SkillLevelView> sentViews = new Dictionary<string, SkillLevelView>(32);

    // The orb lifts a combination's hover numbers are worked out with — reused, one per orb element.
    readonly float[] comboOrbLifts = new float[5];

    /// <summary>SERVER: for every skill that belongs to a weapon, its raised numbers at the level bought and at the
    /// next level, as <paramref name="owner"/> would land them — finished values for the hover card, flattened with a
    /// count per skill. Every level bought counts, whatever is in hand; a skill of the weapon in hand also carries
    /// that weapon's trademark's lift. An unlearned skill's "now" is its numbers at level 1 and its next level is
    /// level 1; a skill at its highest level shows the same numbers in both.</summary>
    public void CaptureViews(Character owner, out string[] ids, out int[] counts, out float[] now, out float[] next)
    {
        var held = owner != null ? owner.HeldWeapon : WeaponType.None;
        viewIds.Clear(); viewCounts.Clear(); viewNow.Clear(); viewNext.Clear();
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            var def = all[i];
            if (def == null || string.IsNullOrEmpty(def.id) || def.weaponType == WeaponType.None) continue;
            bool inHand = def.weaponType == held;
            int bought = LevelOf(def.id);
            int boughtNext = Mathf.Min(bought + 1, def.maxLevel);
            float trademark = inHand && owner != null ? owner.TrademarkLift(def.id) : 0f;
            int before = viewNow.Count;
            float[] orbs = def.IsOrbCombo ? OrbLifts(owner, inHand, comboOrbLifts) : null;
            SkillLevelNumbers.Collect(def, owner, MasteryMath.LevelLift(def, bought) + trademark, viewNow, orbs);
            SkillLevelNumbers.Collect(def, owner, MasteryMath.LevelLift(def, boughtNext) + trademark, viewNext, orbs);
            viewIds.Add(def.id);
            viewCounts.Add(viewNow.Count - before);
        }
        ids = viewIds.ToArray();
        counts = viewCounts.ToArray();
        now = viewNow.ToArray();
        next = viewNext.ToArray();
    }
    /// <summary>SERVER: what each orb's level (and, with a Staff in hand, its trademark) lifts the parts of a Staff
    /// combination by — one per <see cref="OrbElement"/>, written into <paramref name="into"/> and returned. A
    /// combination's part grows with its own orb, exactly as the fight lifts it.</summary>
    public float[] OrbLifts(Character owner, bool inHand, float[] into)
    {
        for (int o = 1; o <= 4 && o < into.Length; o++)
        {
            string orbId = OrbMath.SkillIdOf((OrbElement)o);
            into[o] = MasteryMath.LevelLift(SkillCatalog.Get(orbId), LevelOf(orbId))
                    + (inHand && owner != null ? owner.TrademarkLift(orbId) : 0f);
        }
        return into;
    }

    readonly List<string> viewIds = new List<string>(32);
    readonly List<int> viewCounts = new List<int>(32);
    readonly List<float> viewNow = new List<float>(48), viewNext = new List<float>(48);

    /// <summary>PLAYER'S MACHINE: keep what the server sent about every weapon skill — its growing numbers now and at
    /// the next level, flattened with a count per skill.</summary>
    public void TakeSentViews(string[] ids, int[] counts, float[] now, float[] next)
    {
        sentViews.Clear();
        if (ids != null && counts != null && now != null && next != null)
        {
            int at = 0;
            for (int i = 0; i < ids.Length && i < counts.Length; i++)
            {
                int c = counts[i];
                if (c < 0 || at + c > now.Length || at + c > next.Length) break;
                var v = new SkillLevelView { now = new float[c], next = new float[c] };
                System.Array.Copy(now, at, v.now, 0, c);
                System.Array.Copy(next, at, v.next, 0, c);
                sentViews[ids[i]] = v;
                at += c;
            }
        }
        OnChanged?.Invoke();
    }

    /// <summary>What the server last sent about <paramref name="skillId"/>; false before any arrives.</summary>
    public bool SentView(string skillId, out SkillLevelView view)
        => sentViews.TryGetValue(skillId ?? "", out view);

    // ---- the skill card's finished numbers, as the server sent them -----------------------------------------
    // For the skills of the weapon in hand: the cast time and every damage line as this character lands it right now.
    // Game numbers, so the server works them out (SkillCardNumbers) and this machine only keeps and draws them.
    readonly Dictionary<string, SkillCardView> sentCards = new Dictionary<string, SkillCardView>(32);

    /// <summary>PLAYER'S MACHINE: keep the card numbers the server sent, replacing the last set — flattened with a line
    /// count per skill, <see cref="SkillCardNumbers.CastFloats"/> cast numbers and <see cref="SkillCardNumbers.LineInts"/>
    /// numbers per line.</summary>
    public void TakeSkillCards(string[] ids, float[] cast, int[] counts, int[] lines)
    {
        sentCards.Clear();
        if (ids != null && cast != null && counts != null && lines != null)
        {
            int at = 0;
            for (int i = 0; i < ids.Length && i < counts.Length; i++)
            {
                int c = counts[i];
                int from = at * SkillCardNumbers.LineInts, n = c * SkillCardNumbers.LineInts;
                if (c < 0 || from + n > lines.Length || (i + 1) * SkillCardNumbers.CastFloats > cast.Length) break;
                var v = new SkillCardView { cast = new float[SkillCardNumbers.CastFloats], lines = new int[n] };
                System.Array.Copy(cast, i * SkillCardNumbers.CastFloats, v.cast, 0, SkillCardNumbers.CastFloats);
                System.Array.Copy(lines, from, v.lines, 0, n);
                sentCards[ids[i]] = v;
                at += c;
            }
        }
        OnChanged?.Invoke();
    }

    /// <summary>The card numbers the server last sent for <paramref name="skillId"/>; false for a skill of a weapon not
    /// in hand, and before any arrive.</summary>
    public bool SentCard(string skillId, out SkillCardView view)
        => sentCards.TryGetValue(skillId ?? "", out view);

    // ---- save / load --------------------------------------------------------------------------------------

    /// <summary>Everything, as the flat arrays a save carries.</summary>
    public void Capture(out int[] weapons, out double[] points, out string[] skillIds, out int[] levels)
    {
        weapons = new int[weaponPoints.Count];
        points = new double[weaponPoints.Count];
        int at = 0;
        foreach (var kv in weaponPoints) { weapons[at] = (int)kv.Key; points[at] = kv.Value; at++; }

        skillIds = new string[skillLevels.Count];
        levels = new int[skillLevels.Count];
        at = 0;
        foreach (var kv in skillLevels) { skillIds[at] = kv.Key; levels[at] = kv.Value; at++; }
    }

    /// <summary>Replace everything from a save. A skill id the game no longer has, or one that belongs to no weapon,
    /// is dropped, and a level above the skill's highest is held to it — so a retired or retuned skill can never
    /// linger as something nobody can cast, or count for more than it can be bought to.</summary>
    public void Restore(int[] weapons, double[] points, string[] skillIds, int[] levels)
    {
        weaponPoints.Clear();
        skillLevels.Clear();

        if (weapons != null && points != null)
            for (int i = 0; i < weapons.Length && i < points.Length; i++)
            {
                var w = (WeaponType)weapons[i];
                if (w != WeaponType.None && points[i] > 0.0) weaponPoints[w] = points[i];
            }

        if (skillIds != null && levels != null)
            for (int i = 0; i < skillIds.Length && i < levels.Length; i++)
            {
                var def = string.IsNullOrEmpty(skillIds[i]) ? null : SkillCatalog.Get(skillIds[i]);
                if (def == null || def.weaponType == WeaponType.None) continue;
                int level = Mathf.Min(levels[i], def.maxLevel);
                if (level >= 1) skillLevels[def.id] = level;
            }

        LevelsVersion++;
        OnChanged?.Invoke();
    }
}

/// <summary>What the server told this machine about one skill's levels: each growing number (in the order
/// <see cref="SkillLevelNumbers"/> lists them) now and at the next level.</summary>
public struct SkillLevelView
{
    public float[] now;
    public float[] next;
}

/// <summary>What the server told this machine about one skill's card, finished: <see cref="cast"/> in the order
/// <see cref="SkillCardNumbers"/> writes it, and <see cref="lines"/> holding <see cref="SkillCardNumbers.LineInts"/>
/// numbers per line, one line per growing number in <see cref="SkillLevelNumbers"/>' order.</summary>
public struct SkillCardView
{
    public float[] cast;
    public int[] lines;
    public int LineCount => lines != null ? lines.Length / SkillCardNumbers.LineInts : 0;
    public int At(int line, int field) => lines[line * SkillCardNumbers.LineInts + field];
}
