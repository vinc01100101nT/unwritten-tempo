using UnityEngine;

/// <summary>
/// THE one place an item is used. Every consume path — the bag's right-click, a hotkey, a shortcut we add later —
/// calls <see cref="Use"/>, so the rule for what using an item means lives once and no call site can forget it.
///
/// <para>Before this existed the bag slot and the inventory store each had their own copy of "heal the player, then
/// drop one", which is exactly the scattered-primitive shape the project's centralization rule exists to kill: the
/// moment one gained a check the other didn't, they would disagree about whether a potion had been drunk.</para>
///
/// <para><b>What using an item does:</b> it runs the skill the item names (<see cref="Item.useSkillId"/>), self-cast,
/// paying no mana and no cooldown — being consumed IS the cost. A restorative is a Heal effect, a food is a sub-stat
/// buff, an antidote is a Cleanse; all of them are composed in the Skill Composer like any other skill, so an item
/// can never drift from what the same effect does when a spell casts it.</para>
/// </summary>
public static class ItemUse
{
    /// <summary>Run <paramref name="item"/>'s effect. Returns TRUE only if it actually fired — the caller spends the
    /// item on true and leaves the stack alone on false, so a potion is never eaten by a cast that never happened.</summary>
    public static bool Use(Item item)
    {
        if (item == null || !item.IsUsable) return false;
        if (Character.Instance == null || Character.Instance.IsDead) return false;

        var runner = PlayerSkills.ById(item.useSkillId) as SkillRunner;
        if (runner == null || runner.def == null)
        {
            Debug.LogError($"[ItemUse] \"{item.id}\" names skill \"{item.useSkillId}\", which the player has no runner " +
                           $"for. Either the id is wrong or that skill isn't in the catalog.");
            return false;
        }

        var entry = runner.def.ItemUseEntry;
        if (entry == null)
        {
            Debug.LogError($"[ItemUse] \"{item.id}\" runs skill \"{item.useSkillId}\", but that skill has no On Item " +
                           $"Used trigger — so nothing would happen. Add one in the Skill Composer.");
            return false;
        }

        // The same path a passive takes: a real cast with no press behind it. No victim is passed, so an aimed skill
        // would have nothing to land on — an item's skill is authored self-cast.
        runner.FireAsPassive(entry, null);
        return true;
    }
}
