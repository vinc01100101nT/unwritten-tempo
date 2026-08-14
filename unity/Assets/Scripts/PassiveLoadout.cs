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

    /// <summary>Fired whenever the checked set changes — the Grimoire redraws off this.</summary>
    public event Action OnChanged;

    /// <summary>True if the player ticked this passive on themselves (ignores the weapon kit).</summary>
    public bool IsChecked(string id) => !string.IsNullOrEmpty(id) && checkedIds.Contains(id);

    /// <summary>True if the equipped WEAPON rolled this skill — such a passive is live and can't be unticked.</summary>
    public bool GrantedByWeapon(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var c = Character.Instance;
        var weapon = c != null ? c.GetEquippedInstance(EquipSlotType.Weapon) : null;
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
        bool changed = on ? checkedIds.Add(id) : checkedIds.Remove(id);
        if (changed) OnChanged?.Invoke();
        return changed;
    }

    public bool Toggle(string id) => Set(id, !IsChecked(id));

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
                if (def != null && def.IsPassive) checkedIds.Add(saved[i]);
            }
        OnChanged?.Invoke();
    }
}
