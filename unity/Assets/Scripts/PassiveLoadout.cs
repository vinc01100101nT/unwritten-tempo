using System.Collections.Generic;

/// <summary>
/// WHICH passive skills are live on a character — the ONE rule every passive is judged by, so no fire-site can
/// forget it. A passive is live the moment it is LEARNED (a mastery point spent on it), and it fires only while a
/// weapon of its type is in hand — the same rule an active plays by. Nothing else is ticked, chosen or saved: the
/// level the character bought in it already says it all.
///
/// <para>Owned by <see cref="Character"/> as <c>PassiveSkills</c>. The gate itself is applied in exactly one place —
/// <see cref="PassiveEvents.Fire"/> — so a passive that isn't live simply never dispatches, whatever raised its
/// trigger.</para>
///
/// <para>Distinct from <see cref="PassiveRanks"/>, which is the RING's stat walk (a different meaning of
/// "passive" — invested stat percentages, not authored skills).</para>
/// </summary>
public class PassiveLoadout
{
    // THE CHARACTER THIS LOADOUT BELONGS TO — never Character.Instance. That handle names whoever is at this
    // keyboard, which on a server holding twenty people is nobody.
    readonly Character owner;

    public PassiveLoadout(Character owner) { this.owner = owner; }

    /// <summary>THE gate: may this passive run right now? Learned, AND the weapon it belongs to in hand. A skill that
    /// belongs to no weapon is never learned, so a potion's skill (which also has no press) never fires through here.
    /// Non-passives always pass: an active is gated by the hotbar and by the cast, not by this.</summary>
    public bool IsActive(SkillDef def)
    {
        if (def == null) return false;
        if (!def.IsPassive) return true;
        return owner != null && owner.Mastery.HasLearned(def.id) && owner.MayUse(def.id);
    }

    /// <summary>Every passive this character has learned, in the catalog's order — what a character carries a
    /// position for. Whether each one may fire right now is <see cref="IsActive"/>.</summary>
    public void CollectLearned(ICollection<string> into)
    {
        if (into == null || owner == null) return;
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            var def = all[i];
            if (def != null && def.IsPassive && owner.Mastery.HasLearned(def.id)) into.Add(def.id);
        }
    }

    /// <summary>Every passive live right now — the exact list the row of passive slots draws, in the catalog's
    /// order.</summary>
    public void CollectActive(List<string> into)
    {
        if (into == null) return;
        into.Clear();
        if (owner == null) return;
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
            if (IsActive(all[i]) && all[i].IsPassive) into.Add(all[i].id);
    }
}
