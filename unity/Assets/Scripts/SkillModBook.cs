using System.Collections.Generic;

/// <summary>
/// One base skill's modifier loadout — six phase slots, each holding at most one <see cref="SkillModifier"/>.
/// Modifiers are NOT picked: a random legal one is auto-rolled into a random OPEN phase (Charter §12), so
/// the phase fill ORDER is itself random and two players' Bash diverge in both which effect and when.
/// </summary>
public sealed class SkillModLoadout
{
    public const int PhaseCount = 6;

    public readonly string SkillId;
    readonly string[] tags;
    readonly SkillModifier[] byPhase = new SkillModifier[PhaseCount];

    static readonly List<SkillPhase> openScratch = new List<SkillPhase>(PhaseCount);

    public SkillModLoadout(string skillId, string[] tags)
    {
        SkillId = skillId;
        this.tags = tags;
    }

    public IReadOnlyList<string> Tags => tags;
    public SkillModifier At(SkillPhase phase) => byPhase[(int)phase];

    /// <summary>True once every phase that CAN hold a modifier for this skill is filled (phases with no
    /// eligible modifier don't block fullness — they simply can't be filled).</summary>
    public bool Full
    {
        get
        {
            for (int i = 0; i < byPhase.Length; i++)
                if (byPhase[i] == null && ModifierCatalog.HasEligible(tags, (SkillPhase)i)) return false;
            return true;
        }
    }

    /// <summary>Roll one random legal modifier into one random open phase. Returns the modifier rolled,
    /// or null if the skill is already full (nothing left to fill).</summary>
    public SkillModifier RollOne(System.Random rng)
    {
        openScratch.Clear();
        for (int i = 0; i < byPhase.Length; i++)
        {
            var phase = (SkillPhase)i;
            if (byPhase[i] == null && ModifierCatalog.HasEligible(tags, phase)) openScratch.Add(phase);
        }
        if (openScratch.Count == 0) return null;

        var chosenPhase = openScratch[rng.Next(openScratch.Count)];
        var mod = ModifierCatalog.RollFor(tags, chosenPhase, rng);
        byPhase[(int)chosenPhase] = mod;
        return mod;
    }

    /// <summary>Fold every equipped modifier into the cast context, in phase order. Zero-alloc.</summary>
    public void ApplyAll(ref SkillContext ctx)
    {
        for (int i = 0; i < byPhase.Length; i++)
            if (byPhase[i] != null) ctx.Apply(byPhase[i]);
    }

    // ---- save / load -----------------------------------------------------------
    /// <summary>The six slots as modifier ids ("" for empty), phase-indexed — for the save file.</summary>
    public string[] CaptureSlots()
    {
        var ids = new string[byPhase.Length];
        for (int i = 0; i < byPhase.Length; i++) ids[i] = byPhase[i] != null ? byPhase[i].Id : "";
        return ids;
    }

    /// <summary>Restore the slots from saved ids. Unknown/removed ids (catalog changed) load as empty,
    /// so they simply re-roll later — graceful, never throws.</summary>
    public void LoadSlots(string[] idsByPhase)
    {
        if (idsByPhase == null) return;
        for (int i = 0; i < byPhase.Length && i < idsByPhase.Length; i++)
            byPhase[i] = ModifierCatalog.ById(idsByPhase[i]);
    }
}

/// <summary>
/// The player's modifier state across all base skills — owned by <see cref="Character"/> (the analog of
/// <see cref="PassiveRanks"/> for the active-skill layer). A skill registers itself once (and is "born
/// wearing" one random modifier, never bare), then gains one more on every even level until full.
/// Persisted via <see cref="Capture"/>/<see cref="Restore"/> so a forever character keeps its rolls.
/// </summary>
public sealed class SkillModBook
{
    readonly Dictionary<string, SkillModLoadout> skills = new Dictionary<string, SkillModLoadout>();
    readonly System.Random rng;

    public SkillModBook(int? seed = null)
    {
        rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
    }

    /// <summary>Register a base skill the player owns. First registration creates its loadout AND rolls
    /// its first modifier (born wearing one) — UNLESS a restored save already created it (then it just
    /// returns the loaded loadout). Idempotent.</summary>
    public SkillModLoadout Register(string skillId, string[] tags)
    {
        if (!skills.TryGetValue(skillId, out var loadout))
        {
            loadout = new SkillModLoadout(skillId, tags);
            skills[skillId] = loadout;
            loadout.RollOne(rng);   // never bare
        }
        return loadout;
    }

    public SkillModLoadout Get(string skillId) => skills.TryGetValue(skillId, out var l) ? l : null;

    /// <summary>Advance the modifier cadence for a new level: on EVEN levels, every owned skill that
    /// isn't full rolls one more random modifier. No-op on odd levels.</summary>
    public void OnLevel(int level)
    {
        if (level % 2 != 0) return;
        foreach (var loadout in skills.Values)
            if (!loadout.Full) loadout.RollOne(rng);
    }

    // ---- save / load -----------------------------------------------------------
    /// <summary>Snapshot every owned skill's loadout into three parallel arrays (JsonUtility can't do
    /// dictionaries): skill ids, tag sets (comma-joined), and phase slots (comma-joined modifier ids).
    /// Tags are persisted so a restore reconstructs eligibility without waiting for the skill component
    /// to re-register.</summary>
    public void Capture(out string[] ids, out string[] tagSets, out string[] slotSets)
    {
        ids = new string[skills.Count];
        tagSets = new string[skills.Count];
        slotSets = new string[skills.Count];
        int i = 0;
        foreach (var kv in skills)
        {
            ids[i] = kv.Key;
            tagSets[i] = string.Join(",", kv.Value.Tags);
            slotSets[i] = string.Join(",", kv.Value.CaptureSlots());
            i++;
        }
    }

    /// <summary>Rebuild loadouts from a save. Safe against null/short/older saves (no-op then).</summary>
    public void Restore(string[] ids, string[] tagSets, string[] slotSets)
    {
        skills.Clear();
        if (ids == null || tagSets == null || slotSets == null) return;
        int n = System.Math.Min(ids.Length, System.Math.Min(tagSets.Length, slotSets.Length));
        for (int i = 0; i < n; i++)
        {
            if (string.IsNullOrEmpty(ids[i])) continue;
            var tags = tagSets[i].Length > 0 ? tagSets[i].Split(',') : new string[0];
            var loadout = new SkillModLoadout(ids[i], tags);
            loadout.LoadSlots(slotSets[i].Length > 0 ? slotSets[i].Split(',') : null);
            skills[ids[i]] = loadout;
        }
    }
}
