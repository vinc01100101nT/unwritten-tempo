using System;
using System.Collections.Generic;

/// <summary>
/// WHICH passive skills are live on the player — the ONE rule every passive is judged by, so no fire-site can
/// forget it. A passive counts as active when EITHER:
/// <list type="bullet">
/// <item>the player ticked it on in the Grimoire (J) — the checked set this class owns, and</item>
/// <item>the equipped weapon ROLLED it into its kit (<see cref="EquipmentInstance.skillIds"/>) — those are
/// always live while the weapon is worn, which is why the Grimoire greys their checkbox out.</item>
/// </list>
///
/// <para>Owned by <see cref="Character"/> as <c>PassiveSkills</c> and persisted in the save. The gate itself is
/// applied in exactly one place — <see cref="PassiveEvents.Fire"/> — so a passive that isn't active simply never
/// dispatches, whatever raised its trigger.</para>
///
/// <para>Distinct from <see cref="PassiveRanks"/>, which is the RING's stat walk (a different meaning of
/// "passive" — invested stat percentages, not authored skills).</para>
/// </summary>
public class PassiveLoadout
{
    readonly HashSet<string> checkedIds = new HashSet<string>();

    // THE CHARACTER THIS LOADOUT BELONGS TO — never Character.Instance. That handle names whoever is at this
    // keyboard, which on a server holding twenty people is nobody, so a weapon check through it said "no weapon"
    // for every player and no weapon-granted passive ever fired there (play-test, 2026-09-14).
    readonly Character owner;

    public PassiveLoadout(Character owner) { this.owner = owner; }

    /// <summary>How many passives a player may tick on at once — the same four a weapon can grant, so the row of
    /// passive slots beside the hotbar has a fixed size and a player has a budget to spend rather than a list
    /// that grows for free. A weapon-granted passive does not count against it: it is live because of the
    /// weapon, and it already has its own slot.</summary>
    public const int MaxChecked = 4;

    /// <summary>How many the player has ticked on themselves, against <see cref="MaxChecked"/>.</summary>
    public int CheckedCount => checkedIds.Count;

    /// <summary>True when ticking one more would be refused.</summary>
    public bool Full => checkedIds.Count >= MaxChecked;

    /// <summary>Fired whenever the checked set changes — the Grimoire redraws off this.</summary>
    public event Action OnChanged;

    /// <summary>True if the player ticked this passive on themselves.</summary>
    public bool IsChecked(string id) => !string.IsNullOrEmpty(id) && checkedIds.Contains(id);

    /// <summary>THE gate: may this passive run right now? Ticked on, AND the weapon it belongs to in hand — the
    /// same rule an active plays by, so swapping to a bow silences the sword's passives without clearing what the
    /// player chose. Non-passives always pass: an active is gated by the hotbar and by the cast, not by this.</summary>
    public bool IsActive(SkillDef def)
    {
        if (def == null) return false;
        if (!def.IsPassive) return true;
        if (!IsChecked(def.id)) return false;
        return owner == null || owner.MayUse(def.id);
    }

    /// <summary>Tick a passive on or off. Returns true if anything changed.</summary>
    public bool Set(string id, bool on)
    {
        if (string.IsNullOrEmpty(id)) return false;
        // FULL IS A REFUSAL, NOT A SWAP. Deciding which of the four to drop is the player's, and quietly picking
        // one for them would take a passive off their bar that they never chose to remove.
        if (on && !IsChecked(id) && Full) return false;
        bool changed = on ? checkedIds.Add(id) : checkedIds.Remove(id);
        if (changed) OnChanged?.Invoke();
        return changed;
    }

    public bool Toggle(string id) => Set(id, !IsChecked(id));

    /// <summary>Every passive this character has ticked on — the exact list the row of passive slots draws, in
    /// the order it draws them. A weapon grants none of them any more: a passive is learned and ticked, and the
    /// weapon in hand only decides whether it fires (see <see cref="IsActive"/>).</summary>
    public void CollectActive(System.Collections.Generic.List<string> into)
    {
        if (into == null) return;
        into.Clear();

        foreach (var id in checkedIds)
        {
            var def = SkillCatalog.Get(id);
            if (def != null && def.IsPassive && !into.Contains(id)) into.Add(id);
        }
    }

    /// <summary>The checked ids, for the save file.</summary>
    public string[] Capture()
    {
        var arr = new string[checkedIds.Count];
        checkedIds.CopyTo(arr);
        return arr;
    }

    /// <summary>Restore the checked set, dropping ids that are no longer authored (or are no longer passive) so a
    /// retired skill can't linger as an invisible always-on effect.</summary>
    public void Restore(string[] saved)
    {
        checkedIds.Clear();
        if (saved != null)
            for (int i = 0; i < saved.Length; i++)
            {
                var def = SkillCatalog.Get(saved[i]);
                // THE CAP HOLDS ON LOAD TOO. A character saved before there was one can be carrying more than
                // four; the extras are dropped here rather than being allowed in through a door the player
                // cannot use. Which four survive is the order they were written in, and a player can retick.
                if (def != null && def.IsPassive && checkedIds.Count < MaxChecked) checkedIds.Add(saved[i]);
            }
        OnChanged?.Invoke();
    }
}
