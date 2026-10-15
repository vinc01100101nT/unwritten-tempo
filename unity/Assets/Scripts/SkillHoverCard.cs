using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// THE ONE TRIGGER for a skill drawn anywhere — the hotbar and the Skills (K) window both call <see cref="Attach"/>, so
/// a skill answers the pointer the same wherever it is, and exactly as an item does: hovering puts up only its name,
/// white on the dark strip (<see cref="ItemTooltip.ShowName"/>); right-clicking opens the item card with the skill in it
/// (<see cref="ItemTooltip.PinSkill"/>) — its badge, its name, its full description (<see cref="SkillTooltip.Build"/>).
/// </summary>
public static class SkillHoverCard
{
    /// <summary>Make <paramref name="cell"/> answer for <paramref name="def"/>. Calling it again on a rebuilt cell
    /// re-points it, so a cell never answers for two skills.</summary>
    public static void Attach(Transform cell, SkillDef def, bool beside = false)
    {
        if (cell == null || def == null) return;
        string name = string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName;
        HoverRelay.Set(cell.gameObject, () => ItemTooltip.ShowName(name), ItemTooltip.HideTip);
        var right = cell.GetComponent<SkillRightClick>();
        if (right == null) right = cell.gameObject.AddComponent<SkillRightClick>();
        right.def = def;
    }

    /// <summary>Take down what hovering a skill put up.</summary>
    public static void Hide() => ItemTooltip.HideTip();
}

/// <summary>A right-click on a skill's cell opens its card (<see cref="SkillHoverCard"/>). Left clicks are left to the
/// cell's own button — pressing the skill, raising it.</summary>
public class SkillRightClick : MonoBehaviour, IPointerClickHandler
{
    public SkillDef def;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right && def != null) ItemTooltip.PinSkill(def);
    }
}
