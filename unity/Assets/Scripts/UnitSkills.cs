using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// THE SKILLS ONE UNIT CARRIES, in a fixed order — what a passive moment is dispatched against, what a cast is found
/// by, and what gives each passive its place in the no-self-feeding brake. One per unit: a person's is built from
/// their bar, their learned passives and their bag; a creature's from the weapon it is holding plus its extra skills.
///
/// <para><b>Why it is its own thing.</b> It used to live inside <see cref="Character"/>, so only a person could carry
/// skills and every creature's answer was "none". A creature wearing a weapon with a rolled kit is the same kind of
/// caster, so the list, the lazy building and the trigger index are written once here and both kinds hold one.</para>
///
/// <para><b>NOTHING IS BUILT ON BIND.</b> Every skill gets a position; the component that plays it is made the first
/// moment it is genuinely needed. A server holds one of these per person and per armed creature, so building every
/// component up front would be paid by units that never cast.</para>
/// </summary>
public class UnitSkills
{
    readonly ICombatUnit owner;

    public UnitSkills(ICombatUnit owner) { this.owner = owner; }

    SkillDef[] defs = System.Array.Empty<SkillDef>();
    SkillRunner[] runners = System.Array.Empty<SkillRunner>();
    GameObject host;
    readonly Dictionary<string, int> slotById = new Dictionary<string, int>(16);
    int[][] slotsByTrigger;

    /// <summary>The runner at each position, null until that skill is first used.</summary>
    public SkillRunner[] Runners => runners;

    /// <summary>How many positions there are.</summary>
    public int Count => defs.Length;

    /// <summary>True once this unit has been told at least one skill.</summary>
    public bool HasSkills => defs.Length > 0;

    /// <summary>Say which skills are this unit's, and which object their components live on. Called again whenever
    /// that list is rebuilt. A component already sitting on the host for one of these skills is ADOPTED into its
    /// position rather than duplicated.</summary>
    public void Bind(GameObject onHost, IReadOnlyList<SkillDef> list)
    {
        host = onHost;
        int n = list != null ? list.Count : 0;
        defs = new SkillDef[n];
        runners = new SkillRunner[n];
        slotById.Clear();
        for (int i = 0; i < n; i++)
        {
            var def = list[i];
            defs[i] = def;
            if (def != null && !string.IsNullOrEmpty(def.id)) slotById[def.id] = i;
        }

        if (host != null)
        {
            var already = host.GetComponents<SkillRunner>();
            for (int k = 0; k < already.Length; k++)
            {
                var r = already[k];
                var def = r != null ? r.def : null;
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                if (slotById.TryGetValue(def.id, out int slot) && runners[slot] == null)
                {
                    r.BindCaster(owner);
                    runners[slot] = r;
                }
            }
        }

        IndexTriggers();
    }

    /// <summary>The authored skill at <paramref name="slot"/>, WITHOUT building anything. This is what a moment asks
    /// first, so a skill that turns out not to answer never costs a component.</summary>
    public SkillDef DefAt(int slot) => slot >= 0 && slot < defs.Length ? defs[slot] : null;

    /// <summary>The live component for <paramref name="slot"/>, BUILDING it if this is the first time this unit has
    /// needed that skill. Never null for a real slot on a bound unit.</summary>
    public SkillRunner RunnerAt(int slot)
    {
        if (slot < 0 || slot >= runners.Length) return null;
        var r = runners[slot];
        if (r != null && r) return r;
        var def = defs[slot];
        if (def == null || host == null) return null;
        // perf-ok: builds once per skill this unit ever uses, then never again — the memo shape.
        r = host.AddComponent<SkillRunner>();
        r.def = def;
        r.BindCaster(owner);
        runners[slot] = r;
        return r;
    }

    /// <summary>The component this unit casts <paramref name="skillId"/> with, built on first use, or null if that is
    /// not one of its skills.</summary>
    public SkillRunner RunnerById(string skillId)
        => !string.IsNullOrEmpty(skillId) && slotById.TryGetValue(skillId, out int slot) ? RunnerAt(slot) : null;

    /// <summary>The positions of the skills that answer <paramref name="trigger"/> — the only ones a moment needs to
    /// ask. Never null; empty when none of this unit's skills answer it.</summary>
    public int[] SlotsFor(SkillTrigger trigger)
    {
        int t = (int)trigger;
        if (slotsByTrigger == null || t < 0 || t >= slotsByTrigger.Length) return System.Array.Empty<int>();
        return slotsByTrigger[t] ?? System.Array.Empty<int>();
    }

    void IndexTriggers()
    {
        int triggerKinds = System.Enum.GetValues(typeof(SkillTrigger)).Length;
        var counts = new int[triggerKinds];
        for (int i = 0; i < defs.Length; i++) CountTriggers(defs[i], counts);

        slotsByTrigger = new int[triggerKinds][];
        for (int t = 0; t < triggerKinds; t++)
            slotsByTrigger[t] = counts[t] > 0 ? new int[counts[t]] : System.Array.Empty<int>();

        var filled = new int[triggerKinds];
        for (int i = 0; i < defs.Length; i++) FileTriggers(defs[i], i, filled);
    }

    // A skill with no authored trigger list still answers a press — the same fallback the dispatch makes, said once
    // here so the index and the dispatch cannot disagree about which moments a skill has.
    static void CountTriggers(SkillDef def, int[] counts)
    {
        if (def == null) return;
        var list = def.triggers;
        if (list == null || list.Count == 0) { counts[(int)SkillTrigger.OnCast]++; return; }
        for (int k = 0; k < list.Count; k++)
        {
            var e = list[k];
            if (e == null) continue;
            int t = (int)e.trigger;
            if (t >= 0 && t < counts.Length) counts[t]++;
        }
    }

    void FileTriggers(SkillDef def, int slot, int[] filled)
    {
        if (def == null) return;
        var list = def.triggers;
        if (list == null || list.Count == 0)
        {
            int c = (int)SkillTrigger.OnCast;
            slotsByTrigger[c][filled[c]++] = slot;
            return;
        }
        for (int k = 0; k < list.Count; k++)
        {
            var e = list[k];
            if (e == null) continue;
            int t = (int)e.trigger;
            if (t < 0 || t >= filled.Length) continue;
            slotsByTrigger[t][filled[t]++] = slot;
        }
    }
}
