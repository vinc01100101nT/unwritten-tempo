using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The <b>Skills</b> window (K) — where a character learns and raises skills with mastery points, by weapon. It
/// opens on a list of weapons that have skills ("Sword Skills"); picking one shows that weapon's skills on a
/// TIMELINE of weapon mastery, 0 on the left to the highest mastery any grade allows on the right. Each skill's left
/// edge sits on the mastery level that opens it, a skill and every skill that needs it share rows joined by wires,
/// and a line marks the mastery the server last sent.
///
/// <para>Three looks: a skill the mastery has not reached is grey and faint (30% opacity); one in reach with no point
/// in it is grey at full opacity; one with a point in it wears its own colours. A skill's icon always shows — grey
/// until a point is in it (the user, 2026-10-19). Its <b>+</b> asks the server to spend one point on it —
/// the server decides and sends the whole picture back. A learned active can be dragged onto the hotbar; hovering
/// any skill shows the shared skill card.</para>
///
/// <para>Built in code on the persistent GameUICanvas; the K toggle lives here so changing the key needs no
/// re-bake. Every number it shows is one the server sent.</para>
/// </summary>
public class SkillsWindow : MonoBehaviour
{

    // The timeline: pixels per mastery level, the left margin, one skill's box, one row, the ruler's height.
    const float Px = 20f, Left = 14f, NodeW = 92f, NodeH = 44f, Lane = 50f, RulerH = 34f, TileSize = 22f;
    const float Unreached = 0.3f;

    static ThemeColor LineBg     => UiKit.Rule;
    static ThemeColor LockedTile => UiKit.Rule;
    static ThemeColor LockedText => UiKit.TextDim;
    static Material grey;
    // The material a skill's icon is drawn grey with until a point is in it.
    static Material Grey => grey != null ? grey : (grey = Resources.Load<Material>("UI/GrayscaleUI"));
    static ThemeColor NodeBg     => UiKit.PanelRaise;

    GameObject root;
    RectTransform content;          // the window's fixed Content node; each view is rebuilt into it
    WeaponType shownWeapon;         // None = the weapon list
    ScrollRect scroll;
    RectTransform rulerTrack;       // the ruler's strip, moved sideways with the timeline
    bool built;

    Character watched;

    void Update()
    {
        WatchMastery();
        if (!built) { TryBuild(); return; }
        HandleHotkey();
        // ONE redraw per frame, however many parts of one server message changed: a delivery updates the levels,
        // the points, the progress and the card's numbers one after another, and each says so.
        if (redrawDue && !SkillDragSource.AnyDragging && root != null && UiAnim.IsOpen(root)) { redrawDue = false; Show(shownWeapon); }
    }
    bool redrawDue;

    // Mastery arrives from the server whenever practice or a skill level changes, so an open timeline redraws.
    void WatchMastery()
    {
        var c = Character.Instance;
        if (c == watched) return;
        if (watched != null) watched.Mastery.OnChanged -= OnMasteryChanged;
        watched = c;
        if (watched != null) watched.Mastery.OnChanged += OnMasteryChanged;
    }

    void OnDestroy() { if (watched != null) watched.Mastery.OnChanged -= OnMasteryChanged; }

    void OnMasteryChanged() => redrawDue = true;

    void HandleHotkey()
    {
        if (root == null) return;
        if (Hotkeys.Down(GameKeys.Skills))
        {
            bool open = UiAnim.Toggle(root);
            if (open) { Show(WeaponType.None); UISounds.PlayOpen(); }
            else { SkillHoverCard.Hide(); UISounds.PlayClose(); }
        }
        else if (UiAnim.IsOpen(root) && Hotkeys.CancelDown())
        {
            UiAnim.Close(root);
            SkillHoverCard.Hide();
            UISounds.PlayClose();
            Hotkeys.MarkCancelHandled();
        }
    }

    void TryBuild()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        content = UiKit.Window(canvas.transform, "Skills", new Vector2(760, 560), Vector2.zero, out root);
        built = true;   // starts hidden (UiKit.Window inactivates the root); a view is built on open
    }

    // ---- views ------------------------------------------------------------------------------------------

    void Show(WeaponType weapon)
    {
        if (content == null) return;
        SkillHoverCard.Hide();
        // A redraw of the same timeline (a point spent, a level reached) keeps where it was scrolled to.
        Vector2 keep = weapon == shownWeapon && scroll != null ? scroll.normalizedPosition : new Vector2(-1f, -1f);
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        scroll = null;
        rulerTrack = null;
        shownWeapon = weapon;
        if (weapon == WeaponType.None) BuildWeaponList();
        else BuildTimeline(weapon, keep);
    }

    // Every weapon that has at least one skill in the game, in enum order.
    static readonly List<WeaponType> weaponsBuf = new List<WeaponType>();
    static List<WeaponType> WeaponsWithSkills()
    {
        weaponsBuf.Clear();
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            var w = all[i] != null ? all[i].weaponType : WeaponType.None;
            if (w != WeaponType.None && !weaponsBuf.Contains(w)) weaponsBuf.Add(w);
        }
        weaponsBuf.Sort();
        return weaponsBuf;
    }

    void BuildWeaponList()
    {
        var col = NewRect("WeaponList", content);
        UiKit.Fill(col, Vector2.zero, Vector2.zero);
        var vlg = col.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8; vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        var weapons = WeaponsWithSkills();
        for (int i = 0; i < weapons.Count; i++)
        {
            var w = weapons[i];
            var btn = UiKit.Btn(WeaponTypes.Label(w) + " Skills", col, () => { UISounds.PlayClick(); Show(w); });
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
        }
    }

    void BuildTimeline(WeaponType weapon, Vector2 keepScroll)
    {
        var me = Character.Instance;
        var mastery = me != null ? me.Mastery : null;
        int level = mastery != null ? mastery.SentLevel(weapon) : 0;
        int pointsLeft = mastery != null ? mastery.SentPointsLeft(weapon) : 0;
        int topMastery = CraftingMath.WeaponMasteryCap(CraftingMath.Best);
        Color weaponColor = WeaponColors.Load()?.Lookup(weapon) ?? SkillSymbol.NoWeapon;

        // Top bar: back to the weapon list, the points left to spend, and the mastery the timeline is filled to.
        var bar = NewRect("TopBar", content);
        bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1); bar.pivot = new Vector2(0.5f, 1);
        bar.sizeDelta = new Vector2(0, 30); bar.anchoredPosition = Vector2.zero;
        var back = UiKit.Btn("Back", bar, () => { UISounds.PlayClick(); Show(WeaponType.None); });
        var brt = (RectTransform)back.transform;
        brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(0, 1); brt.pivot = new Vector2(0, 0.5f);
        brt.sizeDelta = new Vector2(70, 0); brt.anchoredPosition = Vector2.zero;
        var points = UiKit.Label("Points", bar, "Mastery Points <color=" + UiKit.Hex(UiKit.TitleText) + ">" + pointsLeft + "</color>",
                                 13, FontStyle.Bold, TextAnchor.MiddleLeft);
        points.richText = true; points.raycastTarget = false;
        var prt = points.rectTransform;
        prt.anchorMin = new Vector2(0, 0); prt.anchorMax = new Vector2(0, 1); prt.pivot = new Vector2(0, 0.5f);
        prt.sizeDelta = new Vector2(220, 0); prt.anchoredPosition = new Vector2(82, 0);
        // The experience toward the next level, exactly as the server sent it — this window only prints the numbers.
        string heading = WeaponTypes.Label(weapon) + " Mastery " + level;
        if (mastery != null && mastery.SentProgress(weapon, out double into, out double size, out bool capped))
            heading += capped ? "  ·  capped by this weapon's grade"
                              : "  ·  " + into.ToString("0.00") + " / " + size.ToString("0") + " exp";
        var head = UiKit.Label("Mastery", bar, heading, 15, FontStyle.Bold, TextAnchor.MiddleRight);
        UiKit.Fill(head.rectTransform, new Vector2(310, 0), Vector2.zero);
        UiKit.Paint(head, UiKit.TitleText); head.raycastTarget = false;

        // This weapon's skills, and the row each sits on.
        var skills = new List<SkillDef>();
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i].weaponType == weapon) skills.Add(all[i]);
        int n = skills.Count;
        var opens = new int[n];
        var parent = new int[n];
        var rows = new int[n];
        for (int i = 0; i < n; i++)
        {
            // AN UNWRITTEN LEVEL IS LEVEL ONE — the same reading the server's spend and the skill card give it.
            opens[i] = Mathf.Max(1, skills[i].masteryLevel);
            parent[i] = skills.FindIndex(s => s.id == skills[i].needsSkillId);
        }
        int rowCount = SkillTreeLayout.Rows(opens, parent, (NodeW + 6f) / Px, rows);

        float trackW = X(topMastery) + NodeW + Left;
        float trackH = RowY(rowCount) + 6f;

        // The ruler: its own strip above the timeline, so it stays in view while the rows scroll down, and moved
        // sideways with them.
        var rulerView = UiKit.Img("RulerView", content, new Color(0, 0, 0, 0)).rectTransform;
        rulerView.anchorMin = new Vector2(0, 1); rulerView.anchorMax = new Vector2(1, 1); rulerView.pivot = new Vector2(0.5f, 1);
        rulerView.sizeDelta = new Vector2(0, RulerH); rulerView.anchoredPosition = new Vector2(0, -38);
        rulerView.gameObject.AddComponent<RectMask2D>();
        rulerView.GetComponent<Image>().raycastTarget = false;
        rulerTrack = NewRect("Ruler", rulerView);
        TopLeft(rulerTrack, 0, 0, trackW, RulerH);
        Box(rulerTrack, "Line", LineBg, X(0), 22, X(topMastery) - X(0), 4);
        if (level > 0) Box(rulerTrack, "Filled", weaponColor, X(0), 22, X(Mathf.Min(level, topMastery)) - X(0), 4);
        for (int lv = 0; lv <= topMastery; lv += 5)
        {
            Box(rulerTrack, "Notch", LineBg, X(lv), 18, 1, 12);
            var t = UiKit.Label("Tick", rulerTrack, lv.ToString(), 11, FontStyle.Bold, TextAnchor.UpperCenter);
            UiKit.Paint(t, level >= lv ? UiKit.TitleText : LockedText); t.raycastTarget = false;
            TopLeft(t.rectTransform, X(lv) - 20, 2, 40, 14);
        }

        // The timeline itself, scrolling both ways.
        var viewport = UiKit.Img("TimelineView", content, new Color(0, 0, 0, 0)).rectTransform;
        UiKit.Fill(viewport, new Vector2(0, 14), new Vector2(0, -(38 + RulerH)));
        viewport.gameObject.AddComponent<RectMask2D>();
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 22f;
        var track = NewRect("Track", viewport);
        TopLeft(track, 0, 0, trackW, trackH);
        scroll.viewport = viewport;
        scroll.content = track;
        scroll.horizontalScrollbar = NewScrollbar();
        scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        var follow = rulerTrack;
        scroll.onValueChanged.AddListener(_ => { if (follow != null) follow.anchoredPosition = new Vector2(track.anchoredPosition.x, follow.anchoredPosition.y); });

        for (int lv = 10; lv <= topMastery; lv += 10)
        {
            var g = Box(track, "Guide", LineBg, X(lv), 0, 1, trackH);
            var gc = g.color; gc.a = 0.6f; g.color = gc;
        }
        Box(track, "Now", weaponColor, X(Mathf.Min(level, topMastery)) - 1, 0, 2, trackH);

        // Wires first, so the skills sit on top of them.
        for (int i = 0; i < n; i++)
        {
            int p = parent[i];
            if (p < 0) continue;
            bool on = mastery != null && mastery.HasLearned(skills[p].id);
            float px = X(opens[p]) + NodeW, py = RowY(rows[p]) + NodeH * 0.5f;
            float cx = X(opens[i]), cy = RowY(rows[i]) + NodeH * 0.5f;
            float elbow = px + 4f;
            Wire(track, on, weaponColor, px, py - 1, elbow - px + 1, 3);
            if (!Mathf.Approximately(cy, py)) Wire(track, on, weaponColor, elbow - 1, Mathf.Min(py, cy) - 1, 3, Mathf.Abs(cy - py) + 3);
            Wire(track, on, weaponColor, elbow, cy - 1, Mathf.Max(0f, cx - elbow), 3);
        }

        for (int i = 0; i < n; i++)
            BuildNode(track, skills[i], X(opens[i]), RowY(rows[i]), opens[i], parent[i] >= 0 ? skills[parent[i]] : null,
                      weaponColor, level, pointsLeft, mastery);

        // Where it opens: a redraw keeps its place; a fresh look puts the mastery line a third of the way across.
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        if (keepScroll.x >= 0f) scroll.normalizedPosition = keepScroll;
        else
        {
            float view = viewport.rect.width;
            float want = X(Mathf.Min(level, topMastery)) - view / 3f;   // server-math: a scroll position in pixels, worked from the level the server sent
            scroll.horizontalNormalizedPosition = trackW > view ? Mathf.Clamp01(want / (trackW - view)) : 0f;
            scroll.verticalNormalizedPosition = 1f;
        }
        rulerTrack.anchoredPosition = new Vector2(track.anchoredPosition.x, 0f);
    }

    void BuildNode(RectTransform track, SkillDef def, float x, float y, int opensAt, SkillDef needs,
                   Color weaponColor, int masteryLevel, int pointsLeft, MasteryProgress mastery)
    {
        int bought = mastery != null ? mastery.LevelOf(def.id) : 0;
        int top = Mathf.Max(1, def.maxLevel);
        bool learned = bought >= 1;
        bool reached = masteryLevel >= opensAt;
        // Every part of this test compares numbers the server sent; the server asks it again before spending.
        bool canRaise = mastery != null && reached && bought < top && pointsLeft > 0
                        && (needs == null || mastery.HasLearned(needs.id));

        var node = UiKit.Round(UiKit.Img("Skill_" + def.id, track, NodeBg), 3);
        TopLeft(node.rectTransform, x, y, NodeW, NodeH);
        node.raycastTarget = true;
        if (!reached) node.gameObject.AddComponent<CanvasGroup>().alpha = Unreached;

        var tile = UiKit.Round(learned ? UiKit.Img("Tile", node.transform, SkillSymbol.ColorFor(def))
                                       : UiKit.Img("Tile", node.transform, LockedTile), 3);
        tile.raycastTarget = false;
        TopLeft(tile.rectTransform, 4, 3, TileSize, TileSize);
        tile.gameObject.AddComponent<DragFace>();   // dragging this skill carries its badge, not the whole card
        var iconSprite = SkillSymbol.IconFor(def);
        if (iconSprite != null)
        {
            // An unlearned skill shows its whole icon in grey, at the brightness it was painted.
            var icon = ItemIcon.Make(tile.transform, 0f);
            ItemIcon.Show(icon, iconSprite);
            if (!learned) icon.material = Grey;
        }
        else
        {
            var sym = UiKit.Label("Symbol", tile.transform, def.Symbol, 12, FontStyle.Bold, TextAnchor.MiddleCenter);
            UiKit.Fill(sym.rectTransform, Vector2.zero, Vector2.zero);
            UiKit.Paint(sym, learned ? UiKit.TextOnDim : LockedText); sym.raycastTarget = false;
        }

        string name = string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName;
        var title = UiKit.Label("Name", node.transform, name, 11, FontStyle.Bold, TextAnchor.UpperLeft);
        title.lineSpacing = -12f;
        if (learned && def.tint != default) UiKit.PaintReadable(title, def.tint);
        else UiKit.Paint(title, learned ? UiKit.TextMain : LockedText);
        title.raycastTarget = false;
        TopLeft(title.rectTransform, 4 + TileSize + 4, 3, NodeW - TileSize - 12, 24);

        var lvl = UiKit.Label("Level", node.transform, bought + "/" + top, 11, FontStyle.Bold, TextAnchor.MiddleRight);
        UiKit.Paint(lvl, learned ? UiKit.TextMain : LockedText); lvl.raycastTarget = false;
        TopLeft(lvl.rectTransform, 4, 27, NodeW - 8 - 19, 14);

        string id = def.id;
        var plus = UiKit.Btn("+", node.transform, () =>
        {
            var net = GameNetwork.MyPlayer;
            if (net == null) return;
            UISounds.PlayClick();
            net.AskToSpendSkillPoint(id);
        });
        TopLeft((RectTransform)plus.transform, NodeW - 4 - 15, 27, 15, 15);
        var plusText = plus.GetComponentInChildren<TextMeshProUGUI>();
        if (plusText != null) { plusText.fontSize = 12; plusText.fontStyle = FontStyles.Bold; if (!canRaise) UiKit.Paint(plusText, LockedText); }
        plus.interactable = canRaise;

        SkillHoverCard.Attach(node.transform, def, beside: true);

        // A learned skill goes on the hotbar by dragging it there. A passive is never pressed, so it does not drag (the
        // server refuses one on the bar too); a skill not learned yet would be a slot the server refuses.
        if (learned && !def.IsPassive)
        {
            var drag = node.gameObject.AddComponent<SkillDragSource>();
            drag.skillId = def.id;
        }
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    static float X(int masteryLevel) => Left + masteryLevel * Px;
    static float RowY(int row) => 6f + row * Lane;

    // A plain coloured box, placed from the top-left of its parent in pixels.
    static Image Box(RectTransform parent, string name, Color color, float x, float y, float w, float h)
        => Placed(UiKit.Img(name, parent, color), x, y, w, h);

    static Image Box(RectTransform parent, string name, ThemeColor color, float x, float y, float w, float h)
        => Placed(UiKit.Img(name, parent, color), x, y, w, h);

    // A wire between two skills: the weapon's colour once learned, the plain line colour until then.
    static Image Wire(RectTransform parent, bool on, Color weaponColor, float x, float y, float w, float h)
        => on ? Box(parent, "Wire", weaponColor, x, y, w, h) : Box(parent, "Wire", LineBg, x, y, w, h);

    static Image Placed(Image img, float x, float y, float w, float h)
    {
        img.raycastTarget = false;
        TopLeft(img.rectTransform, x, y, w, h);
        return img;
    }

    static void TopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, -y);
    }

    // The thin bar along the timeline's bottom edge that shows, and drags, where along the mastery levels it is.
    Scrollbar NewScrollbar()
    {
        var track = UiKit.Round(UiKit.Img("HorizontalScroll", content, LineBg), 3);
        var rt = track.rectTransform;
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
        rt.sizeDelta = new Vector2(0, 8); rt.anchoredPosition = new Vector2(0, 2);
        var area = NewRect("Area", rt);
        UiKit.Fill(area, Vector2.zero, Vector2.zero);
        var handle = UiKit.Round(UiKit.Img("Handle", area, UiKit.WindowEdge), 3);
        UiKit.Fill(handle.rectTransform, Vector2.zero, Vector2.zero);
        var bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle.rectTransform;
        bar.targetGraphic = handle;
        bar.direction = Scrollbar.Direction.LeftToRight;
        return bar;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}
