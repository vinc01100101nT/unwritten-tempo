using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// The HUD <b>buff bar</b> — a vertical stack of the player's active timed self-buffs on the RIGHT edge of the screen
/// (<see cref="Character.Buffs"/>). Each cell shows the source skill's icon (the same icon the hotbar uses) with its
/// remaining seconds and a shade that fills in as the buff runs down. Hovering a cell pops a shared info card with the
/// buff's name, a one-line "what it does" (from the source effect), and its live remaining time. Read-only mirror:
/// buffs are registered by the skill system when a Buff / Guard / Regen effect lands (see <see cref="SkillEffectApplier"/>).
///
/// <para>Self-builds its holder on the persistent GameUICanvas (added by <c>UIShellBuilder</c>); rebuilds its cells
/// only when the set of buffs changes (a new buff or a prune), and the per-frame refresh only rewrites the number on a
/// whole-second change — no per-frame allocation.</para>
/// </summary>
public class BuffBarPanel : MonoBehaviour
{
    class Cell
    {
        public PlayerBuffs.Buff buff;
        public RectTransform rt;      // the 44×44 frame — hover anchor for the info card
        public Image fill;
        public TMP_Text time;
        public int lastSecs = -1;
    }

    static readonly Color CellBg = new Color(0.10f, 0.10f, 0.13f, 0.9f);
    static readonly Color Shade  = new Color(0f, 0f, 0f, 0.5f);
    static readonly Color TipBg  = new Color(0.05f, 0.05f, 0.07f, 0.97f);

    Canvas canvas;
    RectTransform holder;
    Sprite whiteSprite;
    readonly List<Cell> cells = new List<Cell>();
    bool built;
    bool dirty;
    PlayerBuffs subscribed;

    // Shared hover card (one instance, reused by every cell): title + "what it does" body + live remaining time.
    RectTransform tipRoot;
    TMP_Text tipBody;
    TMP_Text tipTime;
    Cell hovered;
    int tipLastSecs = -1;
    readonly Vector3[] cornerBuf = new Vector3[4];

    void Update()
    {
        if (!built) { TryBuild(); return; }
        var ch = Character.Instance;
        if (ch == null) return;
        EnsureSubscribed(ch.Buffs);
        ch.Buffs.Prune();                       // drop expired entries (fires OnChanged → dirty)
        if (dirty) { dirty = false; Rebuild(); }
        Refresh();
    }

    void EnsureSubscribed(PlayerBuffs buffs)
    {
        if (subscribed == buffs) return;
        if (subscribed != null) subscribed.OnChanged -= MarkDirty;
        subscribed = buffs;
        if (subscribed != null) subscribed.OnChanged += MarkDirty;
        dirty = true;
    }

    void MarkDirty() => dirty = true;
    void OnDestroy() { if (subscribed != null) subscribed.OnChanged -= MarkDirty; }

    void TryBuild()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        if (holder == null)
        {
            var go = new GameObject("HUD_BuffBar", typeof(RectTransform));
            holder = (RectTransform)go.transform;
            holder.SetParent(canvas.transform, false);
            holder.anchorMin = holder.anchorMax = new Vector2(1f, 0.5f);   // right edge, vertically centered
            holder.pivot = new Vector2(1f, 0.5f);
            holder.anchoredPosition = new Vector2(-10f, 0f);               // small inset from the edge
            holder.sizeDelta = new Vector2(46f, 420f);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6; vlg.childAlignment = TextAnchor.MiddleRight;
            vlg.childControlWidth = vlg.childControlHeight = false;
            vlg.childForceExpandWidth = vlg.childForceExpandHeight = false;
        }

        built = true;
        dirty = true;
    }

    void Rebuild()
    {
        if (holder == null) return;
        HideTip();   // the cells (and the one being hovered) are about to be destroyed
        for (int i = holder.childCount - 1; i >= 0; i--) Destroy(holder.GetChild(i).gameObject);
        cells.Clear();

        var ch = Character.Instance;
        if (ch == null) return;
        var list = ch.Buffs.Active;
        for (int i = 0; i < list.Count; i++) cells.Add(BuildCell(list[i]));
    }

    Cell BuildCell(PlayerBuffs.Buff buff)
    {
        var frame = UiKit.Img("Buff", holder, CellBg);
        frame.rectTransform.sizeDelta = new Vector2(44f, 44f);
        var le = frame.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = 44f;
        frame.raycastTarget = true;   // the cell IS the hover target for the info card

        if (buff.icon != null)
        {
            var icon = UiKit.Img("Icon", frame.transform, buff.tint == default ? Color.white : buff.tint);
            icon.sprite = buff.icon; icon.preserveAspect = true; icon.raycastTarget = false;
            UiKit.Fill(icon.rectTransform, new Vector2(3, 3), new Vector2(-3, -3));
        }
        else
        {
            var lbl = UiKit.MakeText("Label", frame.transform, buff.name, 9, FontStyle.Bold, TextAnchor.MiddleCenter);
            lbl.color = Color.white; lbl.raycastTarget = false;
            UiKit.Fill(lbl.rectTransform, Vector2.zero, Vector2.zero);
        }

        // Shade that fills IN (radial) as the buff runs down — a "time left" ring, like an inverse cooldown.
        var fill = UiKit.Img("Fill", frame.transform, Shade);
        fill.sprite = whiteSprite; fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top; fill.fillClockwise = false; fill.fillAmount = 0f; fill.raycastTarget = false;
        UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));

        // Remaining whole seconds (bottom-right).
        var time = UiKit.MakeText("Time", frame.transform, "", 11, FontStyle.Bold, TextAnchor.LowerRight);
        time.color = Color.white; time.raycastTarget = false;
        UiKit.Fill(time.rectTransform, new Vector2(2, 1), new Vector2(-2, -1));

        var cell = new Cell { buff = buff, rt = frame.rectTransform, fill = fill, time = time };

        // Hover → show the shared info card over this cell; exit → hide it.
        var trig = frame.gameObject.AddComponent<EventTrigger>();
        AddTrigger(trig, EventTriggerType.PointerEnter, () => ShowTip(cell));
        AddTrigger(trig, EventTriggerType.PointerExit,  HideTip);
        return cell;
    }

    static void AddTrigger(EventTrigger trig, EventTriggerType type, System.Action fn)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => fn());
        trig.triggers.Add(entry);
    }

    void Refresh()
    {
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (c.buff == null || c.fill == null) continue;
            c.fill.fillAmount = 1f - c.buff.Fraction01;   // dark ring grows as the buff expires
            int secs = Mathf.CeilToInt(c.buff.Remaining);
            if (secs != c.lastSecs) { c.time.text = secs > 0 ? secs.ToString() : ""; c.lastSecs = secs; }
        }

        // Keep the open card's remaining-time line ticking (rewrite only on a whole-second change — no per-frame alloc).
        if (hovered != null && hovered.buff != null && tipTime != null)
        {
            int tsecs = Mathf.CeilToInt(hovered.buff.Remaining);
            if (tsecs != tipLastSecs) { tipTime.text = "Time left: " + tsecs + "s"; tipLastSecs = tsecs; }
        }
    }

    void EnsureTooltip()
    {
        if (tipRoot != null || canvas == null) return;

        var bg = UiKit.Img("BuffInfoCard", canvas.transform, TipBg);
        tipRoot = bg.rectTransform;
        tipRoot.anchorMin = tipRoot.anchorMax = new Vector2(0.5f, 0.5f);
        tipRoot.pivot = new Vector2(1f, 0.5f);        // right edge sits against the cell; card grows LEFT
        tipRoot.sizeDelta = new Vector2(230f, 60f);   // width fixed; height driven by the fitter below
        bg.raycastTarget = false;

        var vlg = bg.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8);
        vlg.spacing = 3;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = bg.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        tipBody = UiKit.MakeText("Body", tipRoot, "", 12, FontStyle.Normal, TextAnchor.UpperLeft);
        tipBody.richText = true; tipBody.raycastTarget = false;
        tipTime = UiKit.MakeText("Time", tipRoot, "", 11, FontStyle.Bold, TextAnchor.UpperLeft);
        tipTime.color = new Color(0.655f, 0.620f, 0.533f); tipTime.raycastTarget = false;

        tipRoot.gameObject.SetActive(false);
    }

    void ShowTip(Cell c)
    {
        if (c == null || c.buff == null) return;
        EnsureTooltip();
        if (tipRoot == null) return;

        hovered = c;
        tipBody.text = "<b>" + c.buff.name + "</b>"
                     + (string.IsNullOrEmpty(c.buff.description) ? "" : "\n" + c.buff.description);
        tipLastSecs = -1;                       // force the time line to write this frame
        tipRoot.gameObject.SetActive(true);
        tipRoot.SetAsLastSibling();             // above every cell
        PositionTip(c.rt);
    }

    void HideTip()
    {
        hovered = null;
        if (tipRoot != null) tipRoot.gameObject.SetActive(false);
    }

    // Seat the card's right edge just left of the hovered cell, vertically centered on it.
    void PositionTip(RectTransform cellRT)
    {
        if (tipRoot == null || cellRT == null) return;
        var canvasRT = (RectTransform)canvas.transform;
        Camera cam = canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        cellRT.GetWorldCorners(cornerBuf);                          // [0]=bottom-left, [1]=top-left
        Vector3 leftCenter = (cornerBuf[0] + cornerBuf[1]) * 0.5f;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, leftCenter);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screen, cam, out var local))
            tipRoot.anchoredPosition = local + new Vector2(-8f, 0f);
    }
}
