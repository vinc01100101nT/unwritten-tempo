using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "hold Alt" panel: the FULL card for every skill a weapon rolled — ultimate, actives and passives alike,
/// nothing summarised, nothing cut. A weapon's kit IS its identity now, so the player has to be able to read all
/// of it before deciding to wear it.
///
/// <para><b>Why it flows into COLUMNS.</b> A Legendary carries 9 skills. Measured against the real sword roster
/// (1–2 effects and up to a 112-character description each), one card runs ~110–145px, so nine stacked is roughly
/// 1230px — taller than the ~950px a 1080p screen can give it. Scrolling nine cards inside a hold-a-key panel is
/// hostile: the cursor has to stay parked on the item slot, so you'd hold Alt with one hand and wheel with the
/// other. Instead the cards FLOW into as many columns as the screen actually fits (3 × 440px sits comfortably in
/// 1920), so the whole kit is on screen at once with nothing to scroll. Wheel-scroll survives only as the
/// last-resort fallback for a screen too small even for the column flow.</para>
///
/// <para>Each card is <see cref="SkillTooltip.Build"/> — the SAME renderer the hotbar hover and the Grimoire use,
/// so a skill can never read differently in two places. Owned and driven by <see cref="ItemTooltip"/>, built lazily
/// in code so no scene re-bake is needed.</para>
/// </summary>
public class WeaponSkillDetailsPanel
{
    const float CardWidth = 440f;      // one card's text measure; also one column's width
    const float ColumnGap = 10f;
    const float Padding = 12f;
    const float TitleStrip = 30f;
    const float TooltipGap = 12f;
    const float MaxHeightFraction = 0.86f;   // of the screen
    const float MaxWidthFraction = 0.94f;
    const float ScrollbarWidth = 14f;

    readonly Canvas canvas;
    readonly List<RectTransform> columns = new List<RectTransform>();
    readonly List<RectTransform> cards = new List<RectTransform>();
    readonly List<float> cardHeights = new List<float>();

    GameObject root;
    RectTransform panelRT, viewportRT, flowRT;
    ScrollRect scrollRect;
    Scrollbar scrollbar;
    EquipmentInstance shown;
    bool centered;   // a multi-column panel is too wide to sit beside the tooltip — it takes the middle instead

    /// <summary>Is the panel on screen right now?</summary>
    public bool IsOpen => root != null && root.activeSelf;

    /// <summary>The weapon copy currently on display (null when closed) — lets Alt over a DIFFERENT weapon swap the
    /// contents instead of closing.</summary>
    public EquipmentInstance Showing => IsOpen ? shown : null;

    static readonly Color CardBg = new Color(0.07f, 0.075f, 0.095f, 0.98f);
    static readonly Color BorderCol = new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.55f);
    static readonly Color WhenCol = new Color(0.62f, 0.85f, 0.66f);

    public WeaponSkillDetailsPanel(Canvas canvas)
    {
        this.canvas = canvas;
        Build();
    }

    public void Hide()
    {
        if (root != null && root.activeSelf) root.SetActive(false);
        shown = null;   // re-opening rebuilds, so the live damage estimates are always current
    }

    /// <summary>Show every skill on <paramref name="inst"/>, rebuilding only when the hovered copy changes.</summary>
    public void Show(EquipmentInstance inst)
    {
        if (root == null || inst == null) return;
        if (!root.activeSelf) root.SetActive(true);
        // Front of the canvas every time it opens: a window built earlier than some other panel would otherwise sit
        // UNDER it, and a covered close button is an un-closable window.
        root.transform.SetAsLastSibling();
        if (shown == inst) return;
        shown = inst;
        Populate(inst);
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;   // a new weapon starts at the top
    }

    // ---- build ---------------------------------------------------------------------------------------------

    void Build()
    {
        var border = UiKit.Img("WeaponSkillDetails", canvas.transform, BorderCol);
        root = border.gameObject;
        panelRT = border.rectTransform;
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(0f, 0f);
        panelRT.pivot = new Vector2(0f, 1f);            // top-left, the same convention the item tooltip uses
        panelRT.sizeDelta = new Vector2(CardWidth, 300f);
        // The panel IS interactive: you must be able to put the cursor on it and drag the scrollbar. It only ever
        // appears over the inventory UI, never over the world, so taking the raycast here costs nothing.
        border.raycastTarget = true;

        var fill = UiKit.Img("Fill", border.transform, UiKit.PanelFill);
        UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        fill.raycastTarget = true;

        var title = UiKit.MakeText("Title", fill.transform, "Weapon skills   <size=75%>(Esc, Alt or X to close)</size>",
                                   15, FontStyle.Bold, TextAnchor.MiddleLeft);
        title.color = UiKit.Gold;
        title.richText = true;
        title.raycastTarget = false;
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.offsetMin = new Vector2(Padding, 0f); trt.offsetMax = new Vector2(-(Padding + 30f), 0f);
        trt.sizeDelta = new Vector2(trt.sizeDelta.x, 26f);

        BuildCloseButton(fill.transform);

        var viewport = UiKit.Img("Viewport", fill.transform, new Color(0, 0, 0, 0));
        viewport.raycastTarget = true;                    // the drag surface for scrolling the content directly
        viewportRT = viewport.rectTransform;
        // The right gutter is always reserved for the scrollbar, so cards never reflow when it appears.
        UiKit.Fill(viewportRT, new Vector2(Padding, Padding), new Vector2(-(Padding + ScrollbarWidth), -TitleStrip));
        viewport.gameObject.AddComponent<RectMask2D>();   // clip, so scrolling reads as a window

        var flow = new GameObject("Flow", typeof(RectTransform));
        flowRT = (RectTransform)flow.transform;
        flowRT.SetParent(viewportRT, false);
        flowRT.anchorMin = new Vector2(0f, 1f); flowRT.anchorMax = new Vector2(0f, 1f);
        flowRT.pivot = new Vector2(0f, 1f);
        flowRT.anchoredPosition = Vector2.zero;

        var hlg = flow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = ColumnGap;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
        hlg.childAlignment = TextAnchor.UpperLeft;
        var fit = flow.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildScrollbar(fill.transform);

        // A real ScrollRect, so scrolling works by DRAGGING the bar or the content itself — no mouse wheel needed
        // (plenty of mice have none). The wheel still works for those who have one; it's a bonus, not the mechanism.
        scrollRect = fill.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false; scrollRect.vertical = true;
        scrollRect.viewport = viewportRT;
        scrollRect.content = flowRT;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        root.SetActive(false);
    }

    // A close X in the top-right. The window is PINNED (it doesn't die with the hover), so it needs a way out that
    // doesn't rely on a modifier key the player may not remember.
    void BuildCloseButton(Transform parent)
    {
        var hit = UiKit.Img("Close", parent, new Color(0.55f, 0.16f, 0.16f, 0.85f));
        var rt = hit.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-6f, -4f);
        rt.sizeDelta = new Vector2(22f, 22f);
        hit.raycastTarget = true;

        var x = UiKit.MakeText("X", hit.transform, "X", 13, FontStyle.Bold, TextAnchor.MiddleCenter);
        UiKit.Fill(x.rectTransform, Vector2.zero, Vector2.zero);
        x.raycastTarget = false;

        var btn = hit.gameObject.AddComponent<Button>();
        btn.targetGraphic = hit;
        btn.onClick.AddListener(Hide);
    }

    // A plain draggable vertical scrollbar: track + handle. Built from primitives like the rest of this HUD.
    void BuildScrollbar(Transform parent)
    {
        var track = UiKit.Img("Scrollbar", parent, new Color(0.04f, 0.045f, 0.06f, 0.9f));
        var trt = track.rectTransform;
        trt.anchorMin = new Vector2(1f, 0f); trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(1f, 0.5f);
        trt.offsetMin = new Vector2(-ScrollbarWidth - Padding * 0.5f, Padding);
        trt.offsetMax = new Vector2(-Padding * 0.5f, -TitleStrip);
        trt.sizeDelta = new Vector2(ScrollbarWidth, trt.sizeDelta.y);

        var area = new GameObject("SlidingArea", typeof(RectTransform));
        var art = (RectTransform)area.transform;
        art.SetParent(trt, false);
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(2f, 2f); art.offsetMax = new Vector2(-2f, -2f);

        var handle = UiKit.Img("Handle", art, new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.75f));
        handle.rectTransform.offsetMin = Vector2.zero;
        handle.rectTransform.offsetMax = Vector2.zero;

        scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
    }

    // Build every card, measure it for real, then deal the cards into as many columns as the screen allows.
    void Populate(EquipmentInstance inst)
    {
        ClearFlow();

        // 1) One column to start; every card goes in so each can be measured at its true wrapped height.
        var first = NewColumn();
        AddCardsInReadingOrder(inst, first);

        LayoutRebuilder.ForceRebuildLayoutImmediate(first);
        cardHeights.Clear();
        float totalH = 0f;
        for (int i = 0; i < cards.Count; i++)
        {
            float h = cards[i].rect.height;
            cardHeights.Add(h);
            totalH += h + (i > 0 ? ColumnGap : 0f);
        }

        // 2) How many columns does the screen allow, and how many does this kit need?
        float refH = Screen.height / Mathf.Max(0.0001f, canvas.scaleFactor);
        float refW = Screen.width / Mathf.Max(0.0001f, canvas.scaleFactor);
        float maxColumnH = Mathf.Max(120f, refH * MaxHeightFraction - TitleStrip - Padding);
        int columnsThatFit = Mathf.Max(1, Mathf.FloorToInt((refW * MaxWidthFraction + ColumnGap) / (CardWidth + ColumnGap)));
        int columnsNeeded = Mathf.Max(1, Mathf.CeilToInt(totalH / maxColumnH));
        int columnCount = Mathf.Min(columnsNeeded, columnsThatFit);

        // 3) Deal the cards top-to-bottom, breaking to the next column when this one is full. Reading order is
        //    preserved down each column — a player scans a column, then moves right.
        if (columnCount > 1)
        {
            float perColumn = Mathf.Max(maxColumnH, totalH / columnCount);
            var target = first;
            float used = 0f;
            int made = 1;
            for (int i = 0; i < cards.Count; i++)
            {
                float h = cardHeights[i];
                if (used > 0f && used + h > perColumn && made < columnCount)
                {
                    target = NewColumn(); made++; used = 0f;
                }
                cards[i].SetParent(target, false);
                used += h + ColumnGap;
            }
        }

        // 4) Size the panel to what it actually became, and only scroll if even the column flow overflows.
        LayoutRebuilder.ForceRebuildLayoutImmediate(flowRT);
        float flowH = flowRT.rect.height, flowW = flowRT.rect.width;
        float panelW = Mathf.Min(flowW, refW * MaxWidthFraction) + Padding * 2f;
        float panelH = Mathf.Min(flowH + TitleStrip + Padding, refH * MaxHeightFraction);
        panelRT.sizeDelta = new Vector2(panelW, panelH);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT);

        centered = columnCount > 1;
        // Hide the bar outright when everything fits — a scrollbar that can't scroll is a lie about the content.
        if (scrollbar != null) scrollbar.gameObject.SetActive(flowH > viewportRT.rect.height + 1f);
    }

    void ClearFlow()
    {
        // DETACH before destroying: Destroy is deferred to the end of the frame, so old cards would still be in the
        // layout when heights are measured below and the panel would size itself for two weapons at once.
        for (int i = flowRT.childCount - 1; i >= 0; i--)
        {
            var child = flowRT.GetChild(i).gameObject;
            child.transform.SetParent(null, false);
            Object.Destroy(child);
        }
        columns.Clear();
        cards.Clear();
    }

    RectTransform NewColumn()
    {
        var go = new GameObject("Column" + columns.Count, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(flowRT, false);

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = ColumnGap;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;
        var fit = go.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = CardWidth;   // fixes the wrap measure, so every card reads at the same width

        columns.Add(rt);
        return rt;
    }

    // Reading order: the ultimate anchors the build, then the actives you press, then the passives that run
    // themselves. Every one gets the complete card.
    void AddCardsInReadingOrder(EquipmentInstance inst, RectTransform parent)
    {
        AddGroup(inst, parent, ultimates: true, passives: false);
        AddGroup(inst, parent, ultimates: false, passives: false);
        AddGroup(inst, parent, ultimates: false, passives: true);
    }

    void AddGroup(EquipmentInstance inst, RectTransform parent, bool ultimates, bool passives)
    {
        var ids = inst.skillIds;
        for (int i = 0; i < ids.Count; i++)
        {
            var def = SkillCatalog.Get(ids[i]);
            if (def == null) continue;
            bool isPassive = def.IsPassive;
            bool isUlt = def.IsUltimate && !isPassive;
            if (isPassive != passives) continue;
            if (!passives && isUlt != ultimates) continue;
            cards.Add(BuildCard(def, parent));
        }
    }

    RectTransform BuildCard(SkillDef def, RectTransform parent)
    {
        var card = UiKit.Img("Card_" + def.id, parent, CardBg);
        card.raycastTarget = false;
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8);
        vlg.spacing = 2f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // A passive's WHEN is extra information on top of the standard card, never a replacement for it.
        if (def.IsPassive)
        {
            var when = UiKit.MakeText("When", card.transform, SkillTooltip.TriggerLine(def), 12, FontStyle.Bold, TextAnchor.UpperLeft);
            when.color = WhenCol;
            when.raycastTarget = false;
        }

        var body = UiKit.MakeText("Body", card.transform, SkillTooltip.Build(def), 12, FontStyle.Normal, TextAnchor.UpperLeft);
        body.richText = true;
        body.raycastTarget = false;
        return card.rectTransform;
    }

    // ---- per-frame, only while visible --------------------------------------------------------------------

    /// <summary>Place the panel: a single-column kit peeks beside the item tooltip; a multi-column one is far too
    /// wide for that, so it takes the middle of the screen.</summary>
    public void PlaceBeside(RectTransform tooltipPanel)
    {
        if (root == null) return;
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        float w = panelRT.rect.width * scale;
        float h = panelRT.rect.height * scale;

        float x, y;
        if (centered || tooltipPanel == null)
        {
            x = (Screen.width - w) * 0.5f;
            y = (Screen.height + h) * 0.5f;
        }
        else
        {
            Vector3 tip = tooltipPanel.position;
            float tipW = tooltipPanel.rect.width * scale;
            x = tip.x + tipW + TooltipGap * scale;
            if (x + w > Screen.width) x = tip.x - w - TooltipGap * scale;
            y = tip.y;
        }
        x = Mathf.Clamp(x, 0f, Mathf.Max(0f, Screen.width - w));
        y = Mathf.Clamp(y, h, Screen.height);
        panelRT.position = new Vector3(x, y, 0f);
    }

}
