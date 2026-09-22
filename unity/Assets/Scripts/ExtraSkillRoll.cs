using System.Collections.Generic;

/// <summary>
/// The ONE place a creature's EXTRA skills are decided — the skills it carries in slots of their own, apart from the
/// kit its held weapon rolled. A creature rolls them fresh every life, the same moment its weapon is re-rolled.
///
/// <para><b>The pool is every PUBLISHED skill</b>, whatever weapon it belongs to — ultimates and passives included
/// (decided by the user, 2026-09-17). A creature's skills are never gated by the weapon it holds, so a Bow creature
/// can carry a Sword skill here. Per-biome hand-written extra skills are a later piece of work and are not this.</para>
/// </summary>
public static class ExtraSkillRoll
{
    // Reused across rolls; a roll is once per creature life, but there is no reason to hand the GC a list per death.
    static readonly List<SkillDef> pool = new List<SkillDef>(64);

    /// <summary>Replace <paramref name="into"/> with up to <paramref name="slots"/> skill ids drawn at random from every
    /// published skill, never the same skill twice. Rolls short when the pool is smaller than the slots.</summary>
    public static void Roll(List<string> into, int slots)
    {
        into.Clear();
        pool.Clear();
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            var d = all[i];
            if (d == null || !d.published || string.IsNullOrEmpty(d.id)) continue;
            pool.Add(d);
        }
        for (int n = 0; n < slots && pool.Count > 0; n++)
        {
            int pick = CombatRng.Range(0, pool.Count);
            into.Add(pool[pick].id);
            pool[pick] = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
        }
        pool.Clear();
    }
}
