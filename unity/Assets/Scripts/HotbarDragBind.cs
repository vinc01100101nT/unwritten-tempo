using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Marks a hotbar cell as a drop target for skill binding. Carries the slot index so a dragged
/// skill knows where it landed (see <see cref="SkillDragSource"/>). Added to each cell by <see cref="HotbarPanel"/>.</summary>
/// <summary>What every change to the bar does afterwards, in one place so the two drag sources cannot drift.
/// </summary>
static class HotbarCommit
{
    public static void Now()
    {
        // THE SKILLS FIRST, THEN THE PICTURE OF THEM. A character carries only what it can use, and what is on
        // the bar is part of that — so a skill that has just been put there has no component behind it until
        // this runs, and the slot would draw empty however many times the bar was redrawn, which looks exactly
        // like the drop having failed.
        Character.Instance?.RefreshUsableSkills();
        HotbarPanel.RebuildRequested?.Invoke();
        HotbarSaved.Now();
        UISounds.PlayClick();
    }
}

public class HotbarSlotTarget : MonoBehaviour
{
    public int slotIndex;

    static readonly List<RaycastResult> raycastBuf = new List<RaycastResult>(16);

    /// <summary>The hotbar slot under the pointer at the end of a drag, or −1 when it was let go anywhere else.</summary>
    public static int SlotUnder(PointerEventData e)
    {
        if (EventSystem.current == null) return -1;
        raycastBuf.Clear();
        EventSystem.current.RaycastAll(e, raycastBuf);
        for (int i = 0; i < raycastBuf.Count; i++)
        {
            var target = raycastBuf[i].gameObject.GetComponentInParent<HotbarSlotTarget>();
            if (target != null) return target.slotIndex;
        }
        return -1;
    }

    /// <summary>THE ONE WAY onto the bar by dragging, from wherever the drag began — the Skills window, the bag: put
    /// <paramref name="entry"/> (<see cref="HotbarEntry"/>) in the slot under the pointer. True when it landed on a
    /// slot (whether or not that changed anything), so the caller knows not to treat the drop as something else.</summary>
    public static bool DropOnto(PointerEventData e, string entry)
    {
        int slot = SlotUnder(e);
        if (slot < 0) return false;
        if (Character.Instance != null && !string.IsNullOrEmpty(entry) && Character.Instance.Hotbar.Bind(slot, entry))
            HotbarCommit.Now();
        return true;
    }
}

/// <summary>
/// Makes a FILLED hotbar cell draggable so the player can rearrange the bar: drag one slot onto another and
/// the two swap (drop onto an empty slot = move). A translucent ghost follows the cursor. Dropping AWAY from
/// every hotbar cell empties the slot (drag a thing off the bar to remove it). Added to each occupied cell by
/// <see cref="HotbarPanel"/>. The cell's <see cref="Button"/> still presses the slot on a plain click (a drag won't
/// trip the click).
/// </summary>
public class HotbarSlotDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int slotIndex;

    GameObject ghost;

    // What this cell currently holds.
    string DraggedId => Character.Instance.Hotbar.Get(slotIndex);

    public void OnBeginDrag(PointerEventData e)
    {
        if (Character.Instance == null) return;
        if (string.IsNullOrEmpty(DraggedId)) return;   // empty slot — nothing to drag
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        // The one drag picture every drag in the game shows (DragPicture): this cell, see-through.
        ghost = DragPicture.Begin((RectTransform)transform, canvas, e.position);
    }

    public void OnDrag(PointerEventData e) => DragPicture.Move(ghost, e.position);

    public void OnEndDrag(PointerEventData e)
    {
        DragPicture.End(ref ghost, (RectTransform)transform);
        if (Character.Instance == null || EventSystem.current == null) return;

        // Dropped ON a hotbar cell: swap (rearrange). Dropped anywhere else: the slot is emptied — the bar is the
        // player's own, and every skill, item and piece can be put back from the Skills window or the bag.
        // Let go inside the bar but not on a cell — a gap between two cells — is no move at all.
        int target = HotbarSlotTarget.SlotUnder(e);
        if (target < 0 && transform.parent is RectTransform bar
            && RectTransformUtility.RectangleContainsScreenPoint(bar, e.position, e.pressEventCamera)) return;
        bool changed = target >= 0 ? Character.Instance.Hotbar.Swap(slotIndex, target)
                                   : Character.Instance.Hotbar.Clear(slotIndex);
        if (changed) HotbarCommit.Now();
    }

}

/// <summary>
/// Makes a skill icon in the Skills (K) menu draggable onto a hotbar slot. On drop over a
/// <see cref="HotbarSlotTarget"/>, binds this skill (by id) to that slot (<see cref="HotbarBindings.Bind"/>);
/// dropping elsewhere is a no-op. A translucent ghost icon follows the cursor during the drag. The
/// click-to-bind fallback (bind to the first empty slot) is a sibling <see cref="Button"/> the menu wires up.
/// </summary>
public class SkillDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public string skillId;

    GameObject ghost;

    /// <summary>True while any skill is being dragged out of the Skills window — that window holds its redraws
    /// until the drag ends, because a redraw destroys the very row the drag started from.</summary>
    public static bool AnyDragging { get; private set; }

    // A row destroyed mid-drag never hears the drag end, so the ghost it made would stay on screen.
    void OnDisable()
    {
        if (ghost == null) return;
        DragPicture.End(ref ghost, (RectTransform)transform);
        AnyDragging = false;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (string.IsNullOrEmpty(skillId)) return;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        // The one drag picture every drag in the game shows (DragPicture): this skill, see-through.
        ghost = DragPicture.Begin((RectTransform)transform, canvas, e.position);
        AnyDragging = true;
    }

    public void OnDrag(PointerEventData e) => DragPicture.Move(ghost, e.position);

    public void OnEndDrag(PointerEventData e)
    {
        DragPicture.End(ref ghost, (RectTransform)transform);
        AnyDragging = false;
        if (string.IsNullOrEmpty(skillId) || Character.Instance == null) return;
        HotbarSlotTarget.DropOnto(e, skillId);
    }
}

/// <summary>Where a rearranged hotbar goes. Its own small thing because BOTH drag sources in this file need it,
/// and a copy in each is a copy that drifts.</summary>
static class HotbarSaved
{
    // WHOEVER IS GOING TO WRITE THIS DOWN. In a world that is the server, which has never heard of this bar
    // until it is told — arranging it and saying nothing meant the server's next write put the old bar back.
    public static void Now()
    {
        var me = GameNetwork.MyPlayer;
        if (me != null && Character.Instance != null)
        {
            me.AskToSetHotbar(Character.Instance.Hotbar.Capture());
        }
    }
}
