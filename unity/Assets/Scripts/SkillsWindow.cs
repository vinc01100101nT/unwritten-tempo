using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The <b>Skills</b> window (K) — the skills a character has learned, by weapon. It opens on a list of weapons that
/// have skills ("Sword Skills"); picking one shows that weapon's skills on one vertical line, lowest mastery level at
/// the bottom, each beside the mastery level that opens it. The line fills from the bottom up to the weapon mastery
/// the server last sent, so it shows how far the next skill still is. A skill not learned yet is grey and does
/// nothing; a learned active can be dragged onto the hotbar. Hovering any skill shows the shared skill card.
///
/// <para>Built in code on the persistent GameUICanvas; the K toggle lives here so changing the key needs no
/// re-bake.</para>
/// </summary>
public class SkillsWindow : MonoBehaviour
{
    const KeyCode ToggleKey = KeyCode.K;

    const float RowH = 46f, LinePad = 18f, LevelColW = 44f, LineX = 58f, LineW = 4f, NodeSize = 12f, TileX = 76f, TileSize = 36f;

    static readonly Color LineBg     = UiKit.Rule;
    static readonly Color LockedTile = UiKit.Rule;
    static readonly Color LockedText = UiKit.TextDim;
    static readonly Color RowBg      = UiKit.PanelRaise;

    GameObject root;
    RectTransform content;          // the window's fixed Content node; each view is rebuilt into it
    WeaponType shownWeapon;         // None = the weapon list
    ScrollRect scroll;
    bool built;

    Character watched;

    void Update()
    {
        WatchMastery();
        if (!built) { TryBuild(); return; }
        HandleHotkey();
    }

    // Mastery arrives from the server whenever practice or the learned set changes, so an open line redraws.
    void WatchMastery()
    {
        var c = Character.Instance;
        if (c == watched) return;
        if (watched != null) watched.Mastery.OnChanged -= OnMasteryChanged;
        watched = c;
        if (watched != null) watched.Mastery.OnChanged += OnMasteryChanged;
    }

    void OnDestroy() { if (watched != null) watched.Mastery.OnChanged -= OnMasteryChanged; }

    void OnMasteryChanged() { if (built && root != null && UiAnim.IsOpen(root)) Show(shownWeapon); }

    void HandleHotkey()
    {
        if (root == null) return;
        if (Hotkeys.Down(ToggleKey))
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
        content = UiKit.Window(canvas.transform, "Skills", new Vector2(380, 560), Vector2.zero, out root);
        built = true;   // starts hidden (UiKit.Window inactivates the root); a view is built on open
    }

    // ---- views ------------------------------------------------------------------------------------------

    void Show(WeaponType weapon)
    {
        if (content == null) return;
        SkillHoverCard.Hide();
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        scroll = null;
        shownWeapon = weapon;
        if (weapon == WeaponType.None) BuildWeaponList();
        else BuildLine(weapon);
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
            var btn = UiKit.Btn(w + " Skills", col, () => { UISounds.PlayClick(); Show(w); });
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
        }
    }

    void BuildLine(WeaponType weapon)
    {
        var me = Character.Instance;
        int level = me != null ? me.Mastery.SentLevel(weapon) : 0;
        Color weaponColor = WeaponColors.Load()?.Lookup(weapon) ?? SkillSymbol.NoWeapon;

        // Top bar: back to the weapon list, and the mastery the line is filled to.
        var bar = NewRect("TopBar", content);
        bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1); bar.pivot = new Vector2(0.5f, 1);
        bar.sizeDelta = new Vector2(0, 30); bar.anchoredPosition = Vector2.zero;
        var back = UiKit.Btn("Back", bar, () => { UISounds.PlayClick(); Show(WeaponType.None); });
        var brt = (RectTransform)back.transform;
        brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(0, 1); brt.pivot = new Vector2(0, 0.5f);
        brt.sizeDelta = new Vector2(70, 0); brt.anchoredPosition = Vector2.zero;
        // The experience toward the next level, exactly as the server sent it — this window only prints the numbers.
        string heading = weapon + " Mastery " + level;
        if (me != null && me.Mastery.SentProgress(weapon, out double into, out double size, out bool capped))
            heading += capped ? "  ·  capped by this weapon's grade"
                              : "  ·  " + into.ToString("0.00") + " / " + size.ToString("0") + " exp";
        var head = UiKit.Label("Mastery", bar, heading, 15, FontStyle.Bold, TextAnchor.MiddleRight);
        UiKit.Fill(head.rectTransform, new Vector2(80, 0), Vector2.zero);
        head.color = UiKit.TitleText; head.raycastTarget = false;

        // The line itself scrolls: 22 sword skills are taller than the window.
        var viewport = UiKit.Img("LineViewport", content, new Color(0, 0, 0, 0)).rectTransform;
        UiKit.Fill(viewport, Vector2.zero, new Vector2(0, -38));
        var scrollContent = UiKit.ScrollColumn(viewport);
        scroll = viewport.GetComponent<ScrollRect>();
        var svlg = scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
        svlg.childControlWidth = svlg.childControlHeight = true;
        svlg.childForceExpandWidth = true; svlg.childForceExpandHeight = false;

        // Lowest level first; the list is drawn bottom-up so the line fills upward.
        var skills = new List<SkillDef>();
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i].weaponType == weapon) skills.Add(all[i]);
        skills.Sort((a, b) =>
        {
            int la = Mathf.Max(1, a.masteryLevel), lb = Mathf.Max(1, b.masteryLevel);
            return la != lb ? la.CompareTo(lb) : string.CompareOrdinal(a.id, b.id);
        });

        float treeH = LinePad * 2f + skills.Count * RowH;
        var tree = NewRect("Tree", scrollContent);
        var tle = tree.gameObject.AddComponent<LayoutElement>();
        tle.minHeight = tle.preferredHeight = treeH;

        float NodeY(int i) => LinePad + i * RowH + RowH * 0.5f;

        // The empty line, from the bottom (mastery 0) to the top skill.
        float lineTop = skills.Count > 0 ? NodeY(skills.Count - 1) : LinePad;
        BottomBar(tree, LineBg, LinePad, lineTop - LinePad);

        // The filled part, up to the weapon mastery the server sent.
        var needs = new int[skills.Count];
        for (int i = 0; i < needs.Length; i++) needs[i] = Mathf.Max(1, skills[i].masteryLevel);
        float fillTop = SkillLineLayout.FillTop(level, needs, LinePad, NodeY(0), RowH);
        if (fillTop > LinePad) BottomBar(tree, weaponColor, LinePad, fillTop - LinePad);

        for (int i = 0; i < skills.Count; i++) BuildRow(tree, skills[i], NodeY(i), weaponColor);

        // Open at the bottom, where the line starts.
        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
        scroll.verticalNormalizedPosition = 0f;
    }

    void BuildRow(RectTransform tree, SkillDef def, float y, Color weaponColor)
    {
        var me = Character.Instance;
        bool learned = me != null && me.Mastery.HasLearned(def.id);
        int need = Mathf.Max(1, def.masteryLevel);

        // The mastery level that opens this skill, left of the line.
        var lvl = UiKit.Label("Level", tree, "Lv " + need, 13, FontStyle.Bold, TextAnchor.MiddleRight);
        var lrt = lvl.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0, 0); lrt.pivot = new Vector2(0, 0.5f);
        lrt.sizeDelta = new Vector2(LevelColW, RowH); lrt.anchoredPosition = new Vector2(0, y);
        lvl.color = learned ? UiKit.TitleText : LockedText; lvl.raycastTarget = false;

        // The node on the line.
        var node = UiKit.Img("Node", tree, learned ? weaponColor : LockedTile);
        node.raycastTarget = false;
        var nrt = node.rectTransform;
        nrt.anchorMin = nrt.anchorMax = new Vector2(0, 0); nrt.pivot = new Vector2(0.5f, 0.5f);
        nrt.sizeDelta = new Vector2(NodeSize, NodeSize); nrt.anchoredPosition = new Vector2(LineX, y);

        // The skill: its two letters on its weapon's colour and its name — grey until it is learned.
        var row = UiKit.Img("Skill_" + def.id, tree, RowBg);
        var rrt = row.rectTransform;
        rrt.anchorMin = new Vector2(0, 0); rrt.anchorMax = new Vector2(1, 0); rrt.pivot = new Vector2(0, 0.5f);
        rrt.sizeDelta = new Vector2(-TileX, RowH - 6f); rrt.anchoredPosition = new Vector2(TileX - 4f, y);
        row.raycastTarget = true;

        var tile = UiKit.Img("Tile", row.transform, learned ? SkillSymbol.ColorFor(def) : LockedTile);
        tile.raycastTarget = false;
        var trt = tile.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0, 0.5f); trt.pivot = new Vector2(0, 0.5f);
        trt.sizeDelta = new Vector2(TileSize, TileSize); trt.anchoredPosition = new Vector2(4f, 0);
        var sym = UiKit.Label("Symbol", tile.transform, def.Symbol, 15, FontStyle.Bold, TextAnchor.MiddleCenter);
        UiKit.Fill(sym.rectTransform, Vector2.zero, Vector2.zero);
        sym.color = learned ? Color.white : LockedText; sym.raycastTarget = false;

        string name = string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName;
        var title = UiKit.Label("Name", row.transform, name, 14, FontStyle.Bold, TextAnchor.MiddleLeft);
        UiKit.Fill(title.rectTransform, new Vector2(TileSize + 14f, 0), new Vector2(-6f, 0));
        title.color = !learned ? LockedText : def.tint == default ? UiKit.TextMain : UiKit.Readable(def.tint);
        title.raycastTarget = false;

        SkillHoverCard.Attach(row.transform, def, beside: true);

        // A learned ACTIVE goes on the hotbar by dragging it there. A passive takes no slot, and a skill not learned
        // yet would be a key the server refuses.
        if (learned && !def.IsPassive)
        {
            var drag = row.gameObject.AddComponent<SkillDragSource>();
            drag.skillId = def.id; drag.tint = SkillSymbol.ColorFor(def);
        }
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    // A vertical bar on the line, from `bottom` up `height`, measured from the tree's bottom edge.
    static void BottomBar(RectTransform tree, Color color, float bottom, float height)
    {
        var bar = UiKit.Img("Line", tree, color);
        bar.raycastTarget = false;
        var rt = bar.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(0.5f, 0);
        rt.sizeDelta = new Vector2(LineW, Mathf.Max(0f, height)); rt.anchoredPosition = new Vector2(LineX, bottom);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}
