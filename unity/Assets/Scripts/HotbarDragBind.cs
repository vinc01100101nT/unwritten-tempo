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
/// the two swap (drop onto an empty slot = move). A translucent ghost follows the cursor. Dropping away from
/// any slot is a no-op (the skill stays put). Added to each occupied cell by <see cref="HotbarPanel"/>; the
/// cell's <see cref="Button"/> still fires the skill on a plain click (a drag won't trip the click).
/// </summary>
public class HotbarSlotDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int slotIndex;
    public Sprite iconSprite;
    public Color tint = Color.white;

    GameObject ghost;
    static readonly List<RaycastResult> raycastBuf = new List<RaycastResult>(16);

    public void OnBeginDrag(PointerEventData e)
    {
        if (Character.Instance == null) return;
        if (string.IsNullOrEmpty(Character.Instance.Hotbar.Get(slotIndex))) return;   // empty slot — nothing to drag
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        ghost = new GameObject("HotbarDragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ghost.transform.SetParent(canvas.transform, false);
        ghost.transform.SetAsLastSibling();
        var img = ghost.GetComponent<Image>();
        img.sprite = iconSprite;
        img.color = new Color(tint.r, tint.g, tint.b, iconSprite != null ? 0.9f : 0.55f);
        img.raycastTarget = false;
        img.preserveAspect = true;
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
            if (Character.Instance.Hotbar.Swap(slotIndex, target.slotIndex))
            {
                HotbarPanel.RebuildRequested?.Invoke();
                if (SaveManager.Instance != null) SaveManager.Instance.SaveNow();
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

/// <summary>
/// Makes a skill icon in the Skills (K) menu draggable onto a hotbar slot. On drop over a
/// <see cref="HotbarSlotTarget"/>, binds this skill (by id) to that slot (<see cref="HotbarBindings.Bind"/>);
/// dropping elsewhere is a no-op. A translucent ghost icon follows the cursor during the drag. The
/// click-to-bind fallback (bind to the first empty slot) is a sibling <see cref="Button"/> the menu wires up.
/// </summary>
public class SkillDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public string skillId;
    public Sprite iconSprite;       // for the drag ghost (null → a plain tinted square)
    public Color tint = Color.white;

    GameObject ghost;
    static readonly List<RaycastResult> raycastBuf = new List<RaycastResult>(16);

    public void OnBeginDrag(PointerEventData e)
    {
        if (string.IsNullOrEmpty(skillId)) return;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        ghost = new GameObject("SkillDragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ghost.transform.SetParent(canvas.transform, false);
        ghost.transform.SetAsLastSibling();
        var img = ghost.GetComponent<Image>();
        img.sprite = iconSprite;
        img.color = new Color(tint.r, tint.g, tint.b, iconSprite != null ? 0.9f : 0.55f);
        img.raycastTarget = false;
        img.preserveAspect = true;
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
            if (Character.Instance.Hotbar.Bind(target.slotIndex, skillId))
            {
                HotbarPanel.RebuildRequested?.Invoke();
                if (SaveManager.Instance != null) SaveManager.Instance.SaveNow();
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
