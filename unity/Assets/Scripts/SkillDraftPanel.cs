using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The Phase G Skills (K) panel — the Trio-Ring passive draft, presented as <b>bankable
/// points</b> (doc §11 #4). Lives on the persistent <c>GameUICanvas</c> and drives the
/// panel <see cref="UIShellBuilder"/> built as an empty placeholder ("SkillsPanel/Content").
///
/// The 4 cards live in a clipped <b>carousel viewport</b> so a pick can be ANIMATED to show
/// the steering (doc §2 — "the draft is a steering wheel"):
///   • pick a NEIGHBOR → the card set <b>slides</b> left/right (the Trio window walks the ring),
///   • pick the LEADER (stack) or the WILDCARD (jump) → a non-directional zoom/flash,
/// then the next point's draft slides/zooms in. The ring view's new leader pulses to confirm.
/// </summary>
public class SkillDraftPanel : MonoBehaviour
{
    RectTransform content;          // the panel's "Content" node we build into
    GameObject panelRoot;           // the "SkillsPanel" root we show/hide on K (code-driven, like the window trio)
    DraftService.Draft? pending;    // the draft currently shown (cached so the wildcard is stable between renders)
    bool bound, animating;

    // Persistent sub-pieces so a pick can refresh the header/ring without nuking the animating cards.
    // cardViewport clips the sliding TRIO (cardSet); wildcardRow is the static wildcard slot beside it.
    TMP_Text headerText;
    RectTransform cardViewport, cardSet, wildcardRow, ringContainer;
    Coroutine toastCo;

    const float SlideDur = 0.45f;

    static readonly Color CardBg     = new Color(0.16f, 0.16f, 0.20f, 0.95f);
    static readonly Color LeaderEdge = new Color(1f, 0.92f, 0.5f);     // gold = the center "stack" card
    static readonly Color WildEdge   = new Color(0.85f, 0.55f, 1f);    // purple = the gamble
    static readonly Color RowDim     = new Color(1f, 1f, 1f, 0.5f);

    void Start() => TryBind();
    void Update()
    {
        if (!bound) TryBind();   // Character/UI may not be ready on the very first frame
        HandleHotkey();          // K = toggle, Esc = close — owned here in code so editing the key needs no rebake
    }

    // The Skills (K) toggle lives in runtime code (mirroring ProgressionWindows' B/V/C) rather than as
    // serialized PanelToggle data, so changing the key is a source edit — no "Build UI Shell" rebake.
    void HandleHotkey()
    {
        if (panelRoot == null)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "SkillsPanel") { panelRoot = t.gameObject; break; }
            if (panelRoot == null) return;
        }

        if (Hotkeys.Down(KeyCode.K))
        {
            bool open = UiAnim.Toggle(panelRoot);
            if (open) { Render(); UISounds.PlayOpen(); } else UISounds.PlayClose();   // refresh live ring XP on open
        }
        else if (UiAnim.IsOpen(panelRoot) && Input.GetKeyDown(KeyCode.Escape))
        {
            UiAnim.Close(panelRoot);
            UISounds.PlayClose();
        }
    }

    void TryBind()
    {
        if (Character.Instance == null) return;
        var viewport = FindContent();
        if (viewport == null) return;

        // The panel can hold more than its height (ring header + draft + the 10-row walk + the skills grid),
        // so the build target is a SCROLLING child clipped to the panel — never spilling past the frame.
        content = EnsureScroll(viewport);

        var vlg = content.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        Character.Instance.Passives.OnChanged += Render;
        Character.Instance.OnStatsChanged += OnStatsChanged;   // equipping/unequipping the ring flips RingEquipped
        Character.Instance.Ring.OnChanged += OnRingChanged;
        Character.Instance.Ring.OnLeveledUp += ShowRingLevelToast;
        bound = true;
        Render();
    }

    // Ring XP ticks every kill — rebuilding the panel each time would be wasteful, so only refresh the live
    // XP bar while the panel is actually open (you only see it then).
    void OnRingChanged() { if (panelRoot != null && UiAnim.IsOpen(panelRoot)) Render(); }

    // The ring being equipped/unequipped arrives as a stat change (SetEquipped fires OnStatsChanged). Re-render
    // while open so the panel flips between its active and "ring unequipped" states immediately.
    void OnStatsChanged() { if (panelRoot != null && UiAnim.IsOpen(panelRoot)) Render(); }

    // Turn the panel's fixed-size "Content" node into a vertical ScrollRect viewport (mask + scroll) and
    // return the inner scrolling content (the build target). Idempotent — reuses the child on a rebind.
    RectTransform EnsureScroll(RectTransform viewport)
    {
        var existing = viewport.Find("ScrollContent") as RectTransform;
        if (existing != null) return existing;

        if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.GetComponent<Image>();
        if (vpImg != null) vpImg.raycastTarget = true;   // a graphic for the wheel/drag to land on

        var scroll = viewport.GetComponent<ScrollRect>() ?? viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 22f;

        var sc = new GameObject("ScrollContent", typeof(RectTransform));
        var scrt = (RectTransform)sc.transform;
        scrt.SetParent(viewport, false);
        scrt.anchorMin = new Vector2(0, 1); scrt.anchorMax = new Vector2(1, 1); scrt.pivot = new Vector2(0.5f, 1);
        scrt.offsetMin = scrt.offsetMax = Vector2.zero;   // full width, top-anchored; height grows downward
        sc.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = scrt;
        return scrt;
    }

    // The panel the shell builder created is named "SkillsPanel" with a "Content" child;
    // it starts inactive (PanelToggle shows it on K), so search includes inactive objects.
    RectTransform FindContent()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "SkillsPanel")
            {
                panelRoot = t.gameObject;
                UiKit.ClickShield(t as RectTransform);   // catch clicks over the whole window (no leak to world)
                return t.Find("Content") as RectTransform;
            }
        return null;
    }

    void OnDestroy()
    {
        if (Character.Instance == null) return;
        Character.Instance.Passives.OnChanged -= Render;
        Character.Instance.OnStatsChanged -= OnStatsChanged;
        Character.Instance.Ring.OnChanged -= OnRingChanged;
        Character.Instance.Ring.OnLeveledUp -= ShowRingLevelToast;
    }

    // ---- full (re)build -------------------------------------------------------

    // The OnChanged handler. Skipped mid-animation (the coroutine owns the card area then and
    // refreshes the header/ring itself); otherwise rebuilds the whole panel from scratch.
    void Render()
    {
        if (content == null || animating) return;
        BuildContent();
    }

    void BuildContent()
    {
        var c = Character.Instance;
        var p = c.Passives;
        bool equipped = c.RingEquipped;

        if (equipped && p.BankedPoints > 0) { if (pending == null) pending = DraftService.BuildDraft(p); }
        else pending = null;

        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        headerText = null; cardViewport = null; cardSet = null; wildcardRow = null; ringContainer = null;

        if (!equipped)
        {
            // The whole progression lives ON the ring item. With no ring worn there is nothing to show here —
            // not the level, not the XP, not the walk — just the prompt to put a ring on.
            var warn = NewText(content, "No ring equipped.", 18, FontStyle.Bold, TextAnchor.MiddleCenter);
            warn.color = new Color(1f, 0.7f, 0.5f);
            SetHeight(warn.gameObject, 40);
            var hint = NewText(content,
                "Equip a Ring of Progression (V) to view its level, spend ring points, and use active skills.",
                14, FontStyle.Normal, TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 1f, 1f, 0.75f);
            SetHeight(hint.gameObject, 60);
            return;
        }

        BuildRingHeader(c);   // ◈ ring level + XP bar + "N points to next skill" — the ring's own progression

        headerText = NewText(content, HeaderString(p), 17, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetHeight(headerText.gameObject, 26);

        if (pending.HasValue)
        {
            NewLabel(content, pending.Value.isOpening ? "Choose your starting passive:" : "Choose one — the trio is your ring window:");
            BuildDraftArea(pending.Value);
        }

        BuildRingReadout(p);

        BuildActiveSkillsSection();   // class-agnostic active-skill menu: choose unlocks + drag/click to bind
    }

    // The "Your ring" divider + the 10 ring rows (the walk). Shared by the equipped and unequipped states.
    void BuildRingReadout(PassiveRanks p)
    {
        SetHeight(NewImage(content, new Color(1, 1, 1, 0.12f)).gameObject, 2);   // divider
        NewLabel(content, "Your ring");

        ringContainer = NewImage(content, new Color(0, 0, 0, 0)).rectTransform;
        ringContainer.GetComponent<Image>().raycastTarget = false;
        var rvlg = ringContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        rvlg.childControlWidth = rvlg.childControlHeight = true;
        rvlg.childForceExpandWidth = true; rvlg.spacing = 0;
        ringContainer.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        BuildRingRows(p);
    }

    // The ring's OWN progression banner: level, an XP fill bar, and how many ring points until the next
    // active-skill pick. This is the "ring level/exp" surface the design calls for — separate from the
    // character's level (which lives in the Character window).
    void BuildRingHeader(Character c)
    {
        var r = c.Ring;

        var title = NewText(content, $"<color=#FFDB80>◈ Ring of Progression</color>   <b>Lv {r.Level}</b>",
                            16, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetHeight(title.gameObject, 24);

        // XP fill bar (bg + left-anchored fill scaled to the XP fraction).
        var barBg = NewImage(content, new Color(1, 1, 1, 0.10f));
        SetHeight(barBg.gameObject, 12);
        barBg.raycastTarget = false;
        float frac = Mathf.Clamp01(r.XP / (float)Mathf.Max(1, r.XPToNext));
        var fill = NewImage(barBg.transform, new Color(0.55f, 0.8f, 1f, 0.85f));
        fill.raycastTarget = false;
        var frt = fill.rectTransform;
        frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(frac, 1f);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        var xpLabel = NewText(barBg.transform, $"XP {r.XP} / {r.XPToNext}", 10, FontStyle.Bold, TextAnchor.MiddleCenter);
        xpLabel.raycastTarget = false;
        var xrt = xpLabel.rectTransform;
        xrt.anchorMin = Vector2.zero; xrt.anchorMax = Vector2.one; xrt.offsetMin = xrt.offsetMax = Vector2.zero;

        // Points → next active-skill pick (the new gate: 1 pick per 10 ring points ALLOCATED).
        int toNext = c.Skills.PointsToNextPick(r.PointsAllocated);
        bool allOwned = c.Skills.OwnedCount >= SkillRoster.AllIds.Length;
        string pickLine = allOwned ? "All active skills unlocked"
                        : c.Skills.HasPendingPick(r.PointsAllocated) ? "<color=#D98CFF>New active skill ready to choose!</color>"
                        : $"{toNext} ring point{(toNext == 1 ? "" : "s")} to your next active skill";
        var pl = NewText(content, pickLine, 12, FontStyle.Normal, TextAnchor.MiddleCenter);
        pl.color = new Color(1, 1, 1, 0.8f);
        SetHeight(pl.gameObject, 18);
    }

    // ---- active-skill menu (class-agnostic) -----------------------------------
    // Below the passive ring: the active-skill ownership + hotbar-binding surface. When a 10-level milestone
    // grants a pick, the still-locked skills appear as "learn me" cards; owned skills are a grid you drag onto
    // a hotbar slot (SkillDragSource) or click to bind to the first empty slot.

    static readonly List<string> ownedBuf  = new List<string>();
    static readonly List<string> lockedBuf = new List<string>();
    static readonly Color SkillCardBg = new Color(0.16f, 0.16f, 0.20f, 0.95f);

    void BuildActiveSkillsSection()
    {
        var c = Character.Instance;
        if (c == null) return;

        SetHeight(NewImage(content, new Color(1, 1, 1, 0.12f)).gameObject, 2);   // divider
        NewLabel(content, "Active skills");

        int allocated = c.Ring.PointsAllocated;
        int pending = c.Skills.PendingPicks(allocated);
        if (pending > 0)
        {
            NewLabel(content, $"Choose a new skill — {pending} pick{(pending == 1 ? "" : "s")} (click to learn):");
            c.Skills.LockedIds(lockedBuf);
            BuildSkillGrid(lockedBuf, true);
        }

        c.Skills.OwnedIds(ownedBuf);
        if (ownedBuf.Count == 0)
        {
            if (pending == 0) NewLabel(content, $"Allocate {SkillRoster.PointsPerUnlock} ring points to unlock your first skill.");
        }
        else
        {
            NewLabel(content, "Your skills — drag onto a slot, or click to bind:");
            BuildSkillGrid(ownedBuf, false);
        }
    }

    void BuildSkillGrid(List<string> ids, bool isChoice)
    {
        var grid = NewImage(content, new Color(0, 0, 0, 0)).rectTransform;
        grid.GetComponent<Image>().raycastTarget = false;
        var g = grid.gameObject.AddComponent<GridLayoutGroup>();
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = 3;
        g.spacing = new Vector2(6, 6);
        g.cellSize = new Vector2(108, 44);
        g.childAlignment = TextAnchor.UpperLeft;
        grid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        for (int i = 0; i < ids.Count; i++) BuildSkillCard(grid, ids[i], isChoice);
    }

    void BuildSkillCard(Transform parent, string id, bool isChoice)
    {
        var skill = PlayerSkills.ById(id);
        string label = skill != null ? skill.ShortLabel : id;
        Color tint = skill != null ? skill.Tint : Color.white;
        Sprite icon = skill != null ? skill.Icon : null;

        var card = NewImage(parent, SkillCardBg);
        card.raycastTarget = true;

        if (icon != null)
        {
            var im = NewImage(card.transform, Color.white);
            im.sprite = icon; im.preserveAspect = true; im.raycastTarget = false;
            var irt = im.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(5f, 0f);
            irt.sizeDelta = new Vector2(34f, 34f);
        }

        var t = NewText(card.transform, label + (isChoice ? "  <size=9><color=#D98CFF>NEW</color></size>" : ""),
                        12, FontStyle.Bold, TextAnchor.MiddleLeft);
        t.color = tint; t.raycastTarget = false;
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(icon != null ? 44f : 8f, 0f);
        trt.offsetMax = new Vector2(-4f, 0f);

        var btn = card.gameObject.AddComponent<Button>();
        btn.targetGraphic = card;
        var sid = id;
        if (isChoice)
        {
            btn.onClick.AddListener(() => OnChooseSkill(sid));
        }
        else
        {
            btn.onClick.AddListener(() => OnBindFirstEmpty(sid));
            var drag = card.gameObject.AddComponent<SkillDragSource>();   // drag this card onto a hotbar slot
            drag.skillId = sid; drag.iconSprite = icon; drag.tint = tint;
        }
    }

    void OnChooseSkill(string id)
    {
        if (animating) return;
        var c = Character.Instance;
        if (c == null || !c.RingEquipped || !c.Skills.HasPendingPick(c.Ring.PointsAllocated) || !c.Skills.Unlock(id)) return;
        c.Hotbar.BindToFirstEmpty(id);                 // a freshly learned skill is immediately usable
        if (SaveManager.Instance != null) SaveManager.Instance.SaveNow();
        HotbarPanel.RebuildRequested?.Invoke();
        UISounds.PlayClick();
        BuildContent();                                // reflect the new owned/pending state
    }

    void OnBindFirstEmpty(string id)
    {
        if (animating) return;
        var c = Character.Instance;
        if (c == null || c.Hotbar.BindToFirstEmpty(id) < 0) return;   // already bound or bar full → no-op
        if (SaveManager.Instance != null) SaveManager.Instance.SaveNow();
        HotbarPanel.RebuildRequested?.Invoke();
        UISounds.PlayClick();
        BuildContent();
    }

    static string HeaderString(PassiveRanks p) => p.BankedPoints > 0
        ? $"<b>{p.BankedPoints}</b> skill point{(p.BankedPoints == 1 ? "" : "s")} to spend"
        : "No points to spend — level up to earn one.";

    const int TrioCols = 3, CardW = 108, CardSpacing = 6;
    static int TrioViewportW => TrioCols * CardW + (TrioCols - 1) * CardSpacing;   // exactly three cards wide

    void BuildDraftArea(DraftService.Draft draft)
    {
        wildcardRow = null;

        if (draft.isOpening)
        {
            // Opening: all 10 stats, no wildcard. Full-width grid; the pick is a non-directional "jump".
            cardViewport = NewImage(content, new Color(0, 0, 0, 0)).rectTransform;
            cardViewport.GetComponent<Image>().raycastTarget = false;
            SetHeight(cardViewport.gameObject, 132);
            cardViewport.gameObject.AddComponent<RectMask2D>();
            cardSet = BuildCardSet(new List<DraftService.Option>(draft.options), cardViewport, true);
            return;
        }

        // Trio + wildcard in one horizontal band: ONLY the trio (the 3 known cards) sits in the clipped
        // viewport and slides; the wildcard is parked to the right, STATIC — so the eye can follow the
        // steered trio without the wildcard riding along (it isn't part of the ring window).
        var band = NewImage(content, new Color(0, 0, 0, 0)).rectTransform;
        band.GetComponent<Image>().raycastTarget = false;
        SetHeight(band.gameObject, 84);
        var hlg = band.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6; hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        cardViewport = NewImage(band, new Color(0, 0, 0, 0)).rectTransform;
        cardViewport.GetComponent<Image>().raycastTarget = false;
        var ve = cardViewport.gameObject.AddComponent<LayoutElement>();
        ve.preferredWidth = TrioViewportW; ve.preferredHeight = 84;
        cardViewport.gameObject.AddComponent<RectMask2D>();   // clip the strip that slides past the edges
        cardSet = BuildStrip(Character.Instance.Passives.Leader, cardViewport);

        // A thin divider makes it visually clear the wildcard is a SEPARATE option (not part of the window).
        var sep = NewImage(band, new Color(1, 1, 1, 0.18f));
        sep.raycastTarget = false;
        var se = sep.gameObject.AddComponent<LayoutElement>();
        se.preferredWidth = 2; se.preferredHeight = 60;

        wildcardRow = NewImage(band, new Color(0, 0, 0, 0)).rectTransform;
        wildcardRow.GetComponent<Image>().raycastTarget = false;
        var we = wildcardRow.gameObject.AddComponent<LayoutElement>();
        we.preferredWidth = CardW + 4; we.preferredHeight = 84;
        BuildWildcardCard(draft);
    }

    // The ring stat `off` steps clockwise from `leader` (wraps — it's a ring, so Def→Atk is one step).
    static StatId StatAtOffset(StatId leader, int off)
    {
        int n = SkillRing.Count;
        int idx = ((SkillRing.IndexOf(leader) + off) % n + n) % n;
        return SkillRing.Ring[idx];
    }

    // The trio as ONE continuous strip: five ring cards laid end-to-end ([-2..+2] around the leader),
    // centred so the leader sits mid-viewport (the viewport clips it to ~3). Steering slides this whole
    // strip by one card, so the clicked neighbour physically travels into the centre slot — a real ring
    // scroll, not a swap of separate trios. The two edge cards are the slide-in fodder (clipped at rest).
    RectTransform BuildStrip(StatId leader, RectTransform viewport)
    {
        var strip = NewImage(viewport, new Color(0, 0, 0, 0)).rectTransform;
        strip.name = "TrioStrip";
        strip.GetComponent<Image>().raycastTarget = false;
        strip.anchorMin = Vector2.zero; strip.anchorMax = Vector2.one;
        strip.offsetMin = Vector2.zero; strip.offsetMax = Vector2.zero;
        strip.gameObject.AddComponent<CanvasGroup>();

        float step = CardW + CardSpacing;
        for (int off = -2; off <= 2; off++)
        {
            var card = BuildCard(strip, new DraftService.Option { stat = StatAtOffset(leader, off), isLeader = off == 0 });
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = new Vector2(off * step, 0f);
            card.sizeDelta = new Vector2(CardW, 60);
        }
        return strip;
    }

    // The wildcard card, centred in its static slot. Rebuilt in place (it re-rolls each pick) but it
    // NEVER slides — it lives outside the trio viewport.
    void BuildWildcardCard(DraftService.Draft draft)
    {
        if (wildcardRow == null) return;
        DraftService.Option wild = default; bool found = false;
        foreach (var o in draft.options) if (o.isWildcard) { wild = o; found = true; break; }
        if (!found) return;

        var card = BuildCard(wildcardRow, wild);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = Vector2.zero;
        card.sizeDelta = new Vector2(CardW, 60);
    }

    void RebuildWildcardCard(DraftService.Draft draft)
    {
        if (wildcardRow == null) return;
        for (int i = wildcardRow.childCount - 1; i >= 0; i--) Destroy(wildcardRow.GetChild(i).gameObject);
        BuildWildcardCard(draft);
    }

    // One swappable set of cards, stretched to fill the viewport. Sliding == moving its anchoredPosition.x.
    RectTransform BuildCardSet(List<DraftService.Option> opts, RectTransform viewport, bool opening)
    {
        var set = NewImage(viewport, new Color(0, 0, 0, 0)).rectTransform;
        set.name = "CardSet";
        set.GetComponent<Image>().raycastTarget = false;
        set.anchorMin = Vector2.zero; set.anchorMax = Vector2.one;
        set.offsetMin = Vector2.zero; set.offsetMax = Vector2.zero;
        set.gameObject.AddComponent<CanvasGroup>();

        var grid = set.gameObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = opening ? 5 : opts.Count;        // opening: 10 stats wrap to 5×2
        grid.spacing = new Vector2(CardSpacing, CardSpacing);
        grid.cellSize = new Vector2(opening ? 86 : CardW, 60);
        grid.childAlignment = TextAnchor.MiddleCenter;

        foreach (var opt in opts) BuildCard(set, opt);
        return set;
    }

    RectTransform BuildCard(Transform parent, DraftService.Option opt)
    {
        var p = Character.Instance.Passives;
        var card = NewImage(parent, CardBg);
        card.raycastTarget = true;

        // A colored edge tells the player the card's role: purple = the across-the-ring wildcard;
        // every other card (leader included) keeps its native ring-region tint.
        var edge = NewImage(card.transform,
            opt.isWildcard ? WildEdge : SkillRing.TintFor(opt.stat));
        var ert = edge.rectTransform;
        ert.anchorMin = Vector2.zero; ert.anchorMax = Vector2.one;
        ert.offsetMin = Vector2.zero; ert.offsetMax = Vector2.zero;
        edge.raycastTarget = false;
        var face = NewImage(edge.transform, CardBg);   // inset dark face → the edge reads as a 2px border
        var frt = face.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(2, 2); frt.offsetMax = new Vector2(-2, -2);
        face.raycastTarget = false;

        int cur = p.RanksIn(opt.stat);
        float next = SkillRing.Percent(opt.stat, cur + 1);   // total this stat would reach after the pick
        string tag = opt.isLeader ? "<color=#CFCFCF>STACK</color>"
                   : opt.isWildcard ? "<color=#D98CFF>WILD</color>" : "";
        var label = NewText(face.transform,
            $"<b>{SkillRing.ShortName(opt.stat)}</b>\n+{Mathf.RoundToInt(next * 100)}%\n{tag}",
            14, FontStyle.Normal, TextAnchor.MiddleCenter);
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        label.raycastTarget = false;

        var btn = card.gameObject.AddComponent<Button>();
        btn.targetGraphic = card;
        var option = opt;
        btn.onClick.AddListener(() => OnPick(option));
        return card.rectTransform;
    }

    // Create the 10 ring rows once, then only re-text/re-colour them — no per-pick destroy → no reflow flicker.
    void BuildRingRows(PassiveRanks p)
    {
        if (ringContainer == null) return;
        if (ringContainer.childCount != SkillRing.Count)
        {
            for (int i = ringContainer.childCount - 1; i >= 0; i--) Destroy(ringContainer.GetChild(i).gameObject);
            for (int i = 0; i < SkillRing.Count; i++)
            {
                var row = NewText(ringContainer, "", 15, FontStyle.Normal, TextAnchor.MiddleLeft);
                row.richText = false;
                SetHeight(row.gameObject, 18);
            }
        }

        for (int i = 0; i < SkillRing.Count; i++)
        {
            var stat = SkillRing.Ring[i];
            int r = p.RanksIn(stat);
            bool isLeader = p.HasLeader && stat == p.Leader;
            bool inTrio = p.HasLeader && SkillRing.InTrio(p.Leader, stat);

            var row = ringContainer.GetChild(i).GetComponent<TMP_Text>();
            if (row == null) continue;
            row.text = $"{(isLeader ? "▶ " : "  ")}{SkillRing.ShortName(stat),-6}  ×{r,-2}   +{Mathf.RoundToInt(p.Percent(stat) * 100)}%";
            row.fontStyle = isLeader ? FontStyles.Bold : FontStyles.Normal;
            row.color = isLeader ? LeaderEdge : inTrio ? Color.white : RowDim;
        }
    }

    // ---- the pick animation ---------------------------------------------------

    void OnPick(DraftService.Option opt)
    {
        if (animating || content == null) return;
        var p = Character.Instance.Passives;

        // Direction only has meaning for a neighbor pick (the window slides along the ring).
        int dir = 0;
        if (p.HasLeader && !opt.isWildcard && !opt.isLeader)
            dir = opt.stat == SkillRing.LeftOf(p.Leader) ? -1 : +1;

        UISounds.PlayClick();
        StartCoroutine(AnimatePick(opt, dir));
    }

    IEnumerator AnimatePick(DraftService.Option opt, int dir)
    {
        animating = true;
        var p = Character.Instance.Passives;
        float step = CardW + CardSpacing;

        bool wasOpening = wildcardRow == null;   // the opening grid has no wildcard slot
        var oldSet = cardSet;
        var oldCg = oldSet != null ? oldSet.GetComponent<CanvasGroup>() : null;

        // Apply the pick. Render is gated by `animating`, so the panel won't rebuild under us.
        pending = null;
        p.Pick(opt.stat);

        // Header + ring reflect the new state immediately (they don't slide).
        if (headerText != null) headerText.text = HeaderString(p);
        BuildRingRows(p);

        bool morePoints = p.BankedPoints > 0;
        if (morePoints) pending = DraftService.BuildDraft(p);     // re-roll the wildcard for the next point

        // A structural change (no points left, or opening→trio) can't slide — rebuild cleanly afterwards.
        bool rebuildAtEnd = !morePoints || wasOpening;

        if (morePoints && !wasOpening)
        {
            RebuildWildcardCard(pending.Value);                   // refresh the parked wildcard (static — never slides)

            if (dir != 0)
            {
                // NEIGHBOUR: slide the whole strip one card so the clicked stat glides into the centre slot.
                float target = -dir * step;
                for (float e = 0; e < SlideDur; e += Time.unscaledDeltaTime)
                {
                    if (oldSet != null) SetX(oldSet, Mathf.Lerp(0f, target, Ease(Mathf.Clamp01(e / SlideDur))));
                    yield return null;
                }
                // Re-centre on the new leader. Seamless: it's already mid-viewport, and the visible 3 match.
                var fresh = BuildStrip(p.Leader, cardViewport);
                if (oldSet != null) Destroy(oldSet.gameObject);
                cardSet = fresh;
            }
            else
            {
                // STACK (window stays) / WILDCARD (jump across the ring): a non-directional zoom crossfade.
                var fresh = BuildStrip(p.Leader, cardViewport);
                var freshCg = fresh.GetComponent<CanvasGroup>();
                fresh.localScale = Vector3.one * 0.85f; freshCg.alpha = 0f;
                for (float e = 0; e < SlideDur; e += Time.unscaledDeltaTime)
                {
                    float u = Ease(Mathf.Clamp01(e / SlideDur));
                    if (oldSet != null) { oldSet.localScale = Vector3.one * Mathf.Lerp(1f, 1.1f, u); if (oldCg != null) oldCg.alpha = 1f - u; }
                    fresh.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, u); freshCg.alpha = u;
                    yield return null;
                }
                if (oldSet != null) Destroy(oldSet.gameObject);
                fresh.localScale = Vector3.one; freshCg.alpha = 1f; cardSet = fresh;
            }
        }
        else
        {
            // Opening→trio or last point: zoom the old set out, then a clean full rebuild lays out the new shape.
            for (float e = 0; e < SlideDur; e += Time.unscaledDeltaTime)
            {
                float u = Ease(Mathf.Clamp01(e / SlideDur));
                if (oldSet != null) { oldSet.localScale = Vector3.one * Mathf.Lerp(1f, 1.1f, u); if (oldCg != null) oldCg.alpha = 1f - u; }
                yield return null;
            }
        }

        animating = false;
        if (rebuildAtEnd) BuildContent();
        else StartCoroutine(PulseLeaderRing());
    }

    // Flash the new leader's ring row to confirm where the window landed.
    IEnumerator PulseLeaderRing()
    {
        var p = Character.Instance.Passives;
        if (ringContainer == null || !p.HasLeader) yield break;
        int idx = SkillRing.IndexOf(p.Leader);
        if (idx < 0 || idx >= ringContainer.childCount) yield break;
        var t = ringContainer.GetChild(idx).GetComponent<TMP_Text>();
        if (t == null) yield break;

        for (float e = 0; e < 0.35f; e += Time.unscaledDeltaTime)
        {
            t.color = Color.Lerp(Color.white, LeaderEdge, e / 0.35f);
            yield return null;
        }
        t.color = LeaderEdge;
    }

    static void SetX(RectTransform rt, float x) => rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
    // Smootherstep: eases gently IN and OUT (6t⁵−15t⁴+10t³). Zero velocity at both ends → no abrupt
    // start and a soft landing, which reads far smoother than a plain ease-out on a fast full-width slide.
    static float Ease(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    // ---- level-up toast -------------------------------------------------------

    // A ring level-up banks a passive draft point (and, every 10 points spent, a new active skill). Only fires
    // while the ring is worn (RingProgress only levels when equipped) — so the prompt is always actionable.
    void ShowRingLevelToast(int ringLevel)
    {
        var c = Character.Instance;
        // "A new skill is within reach" = the points still needed for the next pick are already in the bank.
        bool skillSoon = c != null && c.Skills.OwnedCount < SkillRoster.AllIds.Length
                                   && c.Skills.PointsToNextPick(c.Ring.PointsAllocated) <= c.Ring.BankedPoints;
        string msg = skillSoon
            ? $"RING LEVEL {ringLevel}!  +1 skill point  ·  a NEW SKILL is within reach  —  press K"
            : $"RING LEVEL {ringLevel}!  +1 skill point  —  press K";
        if (toastCo != null) StopCoroutine(toastCo);   // don't StopAllCoroutines — it'd kill a pick animation
        toastCo = StartCoroutine(ToastRoutine(msg));
    }

    IEnumerator ToastRoutine(string message)
    {
        var t = UiKit.MakeText("LevelToast", transform, message, 22, FontStyle.Bold, TextAnchor.MiddleCenter);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.78f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(520, 40);
        t.raycastTarget = false;
        var go = t.gameObject;

        const float hold = 1.4f, fade = 0.8f;
        var gold = new Color(1f, 0.92f, 0.5f);
        for (float e = 0; e < hold + fade; e += Time.unscaledDeltaTime)
        {
            float a = e < hold ? 1f : 1f - (e - hold) / fade;
            t.color = new Color(gold.r, gold.g, gold.b, a);
            rt.anchoredPosition = new Vector2(0, Mathf.Lerp(0, 18, e / (hold + fade)));
            yield return null;
        }
        Destroy(go);
        toastCo = null;
    }

    // ---- tiny UI helpers ------------------------------------------------------

    TMP_Text NewLabel(Transform parent, string str)
    {
        var t = NewText(parent, str, 14, FontStyle.Italic, TextAnchor.MiddleCenter);
        t.color = new Color(1, 1, 1, 0.75f);
        SetHeight(t.gameObject, 20);
        return t;
    }

    TMP_Text NewText(Transform parent, string str, int size, FontStyle style, TextAnchor anchor)
    {
        var t = UiKit.MakeText("Text", parent, str, size, style, anchor);
        t.color = Color.white;
        return t;
    }

    Image NewImage(Transform parent, Color color)
    {
        var go = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static void SetHeight(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = h;
    }
}
