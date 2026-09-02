using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Marks a hotbar cell as a drop target for skill binding. Carries the slot index so a dragged
/// skill knows where it landed (see <see cref="SkillDragSource"/>). Added to each cell by <see cref="HotbarPanel"/>.</summary>
public class HotbarSlotTarget : MonoBehaviour
{
    public int slotIndex;
}

/// <summary>
/// Makes a FILLED hotbar cell draggable so the player can rearrange the bar: drag one slot onto another and
/// the two swap (drop onto an empty slot = move). A translucent ghost follows the cursor. Dropping AWAY from
/// every hotbar cell UNBINDS the skill (drag it off the bar to remove it). Added to each occupied cell by
/// <see cref="HotbarPanel"/>; a <see cref="slotIndex"/> of −1 marks the dedicated ULT cell (removes via
/// <see cref="HotbarBindings.ClearUlt"/>). The cell's <see cref="Button"/> still fires the skill on a plain
/// click (a drag won't trip the click).
/// </summary>
public class HotbarSlotDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int slotIndex;
    public Color tint = Color.white;

    GameObject ghost;
    static readonly List<RaycastResult> raycastBuf = new List<RaycastResult>(16);

    // The skill id this cell currently holds (−1 = the dedicated ULT slot).
    string DraggedId => slotIndex < 0 ? Character.Instance.Hotbar.Ult : Character.Instance.Hotbar.Get(slotIndex);

    public void OnBeginDrag(PointerEventData e)
    {
        if (Character.Instance == null) return;
        if (string.IsNullOrEmpty(DraggedId)) return;   // empty slot — nothing to drag
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        ghost = new GameObject("HotbarDragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) { hideFlags = HideFlags.DontSaveInEditor };
        ghost.transform.SetParent(canvas.transform, false);
        ghost.transform.SetAsLastSibling();
        var img = ghost.GetComponent<Image>();
        img.color = new Color(tint.r, tint.g, tint.b, 0.75f);
        img.raycastTarget = false;
        ((RectTransform)ghost.transform).sizeDelta = new Vector2(44, 44);
        Position(e);
    }

    public void OnDrag(PointerEventData e) => Position(e);

    public void OnEndDrag(PointerEventData e)
    {
        if (ghost != null) { Destroy(ghost); ghost = null; }
        if (Character.Instance == null || EventSystem.current == null) return;

        raycastBuf.Clear();
        EventSystem.current.RaycastAll(e, raycastBuf);
        for (int i = 0; i < raycastBuf.Count; i++)
        {
            var target = raycastBuf[i].gameObject.GetComponentInParent<HotbarSlotTarget>();
            if (target == null) continue;
            // Dropped ON a hotbar cell: swap (rearrange). An ult↔numbered swap is rejected by Swap (invalid
            // slot), so it's a harmless no-op — either way the drop landed on the bar, never a removal.
            if (Character.Instance.Hotbar.Swap(slotIndex, target.slotIndex)) Commit();
            return;
        }
        // Dropped AWAY from every hotbar cell → take the skill off the bar.
        bool removed = slotIndex < 0 ? Character.Instance.Hotbar.ClearUlt() : Character.Instance.Hotbar.Clear(slotIndex);
        if (removed) Commit();
    }

    static void Commit()
    {
        HotbarPanel.RebuildRequested?.Invoke();
        HotbarSaved.Now();
        UISounds.PlayClick();
    }

    void Position(PointerEventData e)
    {
        if (ghost == null) return;
        var canvas = ghost.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)canvas.transform, e.position, canvas.worldCamera, out var local);
        ((RectTransform)ghost.transform).anchoredPosition = local;
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
    public Color tint = Color.white;

    GameObject ghost;
    static readonly List<RaycastResult> raycastBuf = new List<RaycastResult>(16);

    public void OnBeginDrag(PointerEventData e)
    {
        if (string.IsNullOrEmpty(skillId)) return;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        ghost = new GameObject("SkillDragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) { hideFlags = HideFlags.DontSaveInEditor };
        ghost.transform.SetParent(canvas.transform, false);
        ghost.transform.SetAsLastSibling();
        var img = ghost.GetComponent<Image>();
        img.color = new Color(tint.r, tint.g, tint.b, 0.75f);
        img.raycastTarget = false;
        ((RectTransform)ghost.transform).sizeDelta = new Vector2(44, 44);
        Position(e);
    }

    public void OnDrag(PointerEventData e) => Position(e);

    public void OnEndDrag(PointerEventData e)
    {
        if (ghost != null) { Destroy(ghost); ghost = null; }
        if (string.IsNullOrEmpty(skillId) || Character.Instance == null || EventSystem.current == null) return;

        raycastBuf.Clear();
        EventSystem.current.RaycastAll(e, raycastBuf);
        for (int i = 0; i < raycastBuf.Count; i++)
        {
            var target = raycastBuf[i].gameObject.GetComponentInParent<HotbarSlotTarget>();
            if (target == null) continue;
            if (Character.Instance.Hotbar.BindFromDrag(target.slotIndex, skillId))
            {
                HotbarPanel.RebuildRequested?.Invoke();
                HotbarSaved.Now();
                UISounds.PlayClick();
            }
            return;
        }
    }

    void Position(PointerEventData e)
    {
        if (ghost == null) return;
        var canvas = ghost.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)canvas.transform, e.position, canvas.worldCamera, out var local);
        ((RectTransform)ghost.transform).anchoredPosition = local;
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
            return;
        }
        if (SaveManager.Instance != null) SaveManager.Instance.SaveNow();
    }
}
