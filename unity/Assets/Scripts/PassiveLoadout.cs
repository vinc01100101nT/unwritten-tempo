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

    /// <summary>True if the player ticked this passive on themselves (ignores the weapon kit).</summary>
    public bool IsChecked(string id) => !string.IsNullOrEmpty(id) && checkedIds.Contains(id);

    /// <summary>True if the equipped WEAPON rolled this skill — such a passive is live and can't be unticked.</summary>
    public bool GrantedByWeapon(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var weapon = owner != null ? owner.GetEquippedInstance(EquipSlotType.Weapon) : null;
        var kit = weapon != null ? weapon.skillIds : null;
        if (kit == null) return false;
        for (int i = 0; i < kit.Count; i++) if (kit[i] == id) return true;
        return false;
    }

    /// <summary>THE gate: may this passive run right now? Non-passives always pass — an active is gated by the
    /// hotbar, not by this.</summary>
    public bool IsActive(SkillDef def)
        => def != null && (!def.IsPassive || IsChecked(def.id) || GrantedByWeapon(def.id));

    /// <summary>Tick a passive on or off. A weapon-granted one is refused (it's live because of the weapon, and
    /// letting it also be checked would fork "why is this on" into two answers). Returns true if anything changed.</summary>
    public bool Set(string id, bool on)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (on && GrantedByWeapon(id)) return false;
        // FULL IS A REFUSAL, NOT A SWAP. Deciding which of the four to drop is the player's, and quietly picking
        // one for them would take a passive off their bar that they never chose to remove.
        if (on && !IsChecked(id) && Full) return false;
        bool changed = on ? checkedIds.Add(id) : checkedIds.Remove(id);
        if (changed) OnChanged?.Invoke();
        return changed;
    }

    public bool Toggle(string id) => Set(id, !IsChecked(id));

    /// <summary>Untick everything the player chose. Called when a weapon changes: the new weapon brings its own
    /// passives and they need the slots, and a player cannot be asked to guess which of their four to give up
    /// before they have seen what they just equipped. Returns true if anything was on.</summary>
    public bool ClearChecked()
    {
        if (checkedIds.Count == 0) return false;
        checkedIds.Clear();
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>Every passive live on this character right now, weapon-granted first and then the ticked ones —
    /// the exact list the row of passive slots draws, in the order it draws them.</summary>
    public void CollectActive(EquipmentInstance weapon, System.Collections.Generic.List<string> into)
    {
        if (into == null) return;
        into.Clear();

        var kit = weapon != null ? weapon.skillIds : null;
        if (kit != null)
            for (int i = 0; i < kit.Count; i++)
            {
                var def = SkillCatalog.Get(kit[i]);
                if (def != null && def.IsPassive && !into.Contains(kit[i])) into.Add(kit[i]);
            }

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
