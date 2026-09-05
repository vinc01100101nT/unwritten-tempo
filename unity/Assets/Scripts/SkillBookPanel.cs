using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The <b>Grimoire</b> (J) — a HUD window listing every skill authored in the <b>Skill Composer</b>
/// (<see cref="SkillCatalog"/>). Each composed <see cref="SkillDef"/> shows its icon, name, MP cost and
/// cooldown; drag a row onto a hotbar slot or click it to bind to the first empty slot (same binding path as
/// the Skills (K) menu — <see cref="SkillDragSource"/> / <see cref="HotbarBindings.BindToFirstEmpty"/>).
///
/// <para>This is the browse-and-bind surface for the data-driven skill pool that replaced the hand-written
/// Player3D* skill classes. Self-builds a <see cref="UiKit.Window"/> on the persistent GameUICanvas (added by
/// <see cref="Editor.UIShellBuilder"/>); the J toggle lives here in code so changing the key needs no re-bake.</para>
/// </summary>
public class SkillBookPanel : MonoBehaviour
{
    const KeyCode ToggleKey = KeyCode.J;

    static readonly Color RowBg    = new Color(0.16f, 0.16f, 0.20f, 0.95f);
    static readonly Color SubColor = new Color(0.75f, 0.8f, 0.95f, 0.85f);
    static readonly Color MpColor  = new Color(0.6f, 0.78f, 1f);
    // Checkbox palette: a live box reads bright, one the weapon already grants reads muted (ticked but untouchable).
    static readonly Color BoxFill       = new Color(0.09f, 0.10f, 0.13f, 1f);
    static readonly Color BoxFillLocked = new Color(0.16f, 0.17f, 0.19f, 1f);
    static readonly Color BoxTickLocked = new Color(0.45f, 0.48f, 0.52f, 1f);

    GameObject root;                 // the window we show/hide on J
    RectTransform scrollContent;     // where rows are built
    bool built;

    Character watched;   // whose equip changes we're listening to (the player appears after this panel does)

    void Update()
    {
        WatchGearChanges();
        if (!built) { TryBuild(); return; }
        HandleHotkey();
    }

    // Swapping a weapon changes WHICH passives are weapon-granted (greyed + forced on), so a list left open across an
    // equip has to redraw — otherwise it keeps offering a checkbox for a passive the new weapon now owns.
    void WatchGearChanges()
    {
        var c = Character.Instance;
        if (c == watched) return;
        if (watched != null) watched.OnStatsChanged -= OnGearChanged;
        watched = c;
        if (watched != null) watched.OnStatsChanged += OnGearChanged;
    }

    void OnDestroy() { if (watched != null) watched.OnStatsChanged -= OnGearChanged; }

    void OnGearChanged() { if (built && root != null && UiAnim.IsOpen(root)) Rebuild(); }

    void HandleHotkey()
    {
        if (root == null) return;
        if (Hotkeys.Down(ToggleKey))
        {
            bool open = UiAnim.Toggle(root);
            if (open) { Rebuild(); UISounds.PlayOpen(); } else UISounds.PlayClose();
        }
        else if (UiAnim.IsOpen(root) && Hotkeys.CancelDown())
        {
            UiAnim.Close(root);
            UISounds.PlayClose();
            Hotkeys.MarkCancelHandled();
        }
    }

    void TryBuild()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        var content = UiKit.Window(canvas.transform, "Grimoire", new Vector2(360, 440), Vector2.zero, out root);
        scrollContent = EnsureScroll(content);

        var vlg = scrollContent.GetComponent<VerticalLayoutGroup>() ?? scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        built = true;   // starts hidden (UiKit.Window inactivates the root); Rebuild runs on first open
    }

    // Turn the window's fixed "Content" node into a clipped vertical scroll and return the inner build target.
    RectTransform EnsureScroll(RectTransform viewport)
    {
        var existing = viewport.Find("ScrollContent") as RectTransform;
        if (existing != null) return existing;

        if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.GetComponent<Image>();
        if (vpImg != null) vpImg.raycastTarget = true;   // a graphic for the wheel to land on

        var scroll = viewport.GetComponent<ScrollRect>() ?? viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 22f;

        var sc = new GameObject("ScrollContent", typeof(RectTransform));
        var scrt = (RectTransform)sc.transform;
        scrt.SetParent(viewport, false);
        scrt.anchorMin = new Vector2(0, 1); scrt.anchorMax = new Vector2(1, 1); scrt.pivot = new Vector2(0.5f, 1);
        scrt.offsetMin = scrt.offsetMax = Vector2.zero;
        sc.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = scrt;
        return scrt;
    }

    // ---- (re)build the list -------------------------------------------------

    void Rebuild()
    {
        if (scrollContent == null) return;
        for (int i = scrollContent.childCount - 1; i >= 0; i--) Destroy(scrollContent.GetChild(i).gameObject);

        var pool = SkillCatalog.OrderedIds();
        if (pool.Length == 0)
        {
            var empty = NewText(scrollContent, "No composed skills yet.\nMake one in the Skill Composer\n(Tools ▸ unwritten ▸ Skills).",
                                14, FontStyle.Italic, TextAnchor.MiddleCenter);
            empty.color = SubColor;
            SetHeight(empty.gameObject, 80);
            return;
        }

        var caption = NewText(scrollContent, "Drag or click a skill to bind it.\nTick a passive to turn it on.", 13, FontStyle.Italic, TextAnchor.MiddleCenter);
        caption.color = SubColor;
        SetHeight(caption.gameObject, 34);

        for (int i = 0; i < pool.Length; i++)
            BuildRow(SkillCatalog.Get(pool[i]));
    }

    void BuildRow(SkillDef def)
    {
        var row = NewImage(scrollContent, RowBg);
        row.raycastTarget = true;
        SetHeight(row.gameObject, 48);

        // A PASSIVE takes no hotbar slot — it runs on its trigger. So its row isn't a bind target at all: it gets a
        // checkbox that turns the passive on for the player. One the equipped weapon already rolled is shown ticked
        // and greyed, because the weapon is what makes it live and ticking it again would mean nothing.
        if (def.IsPassive) { BuildPassiveRow(row, def); return; }

        BuildRowFace(row, def, $"<color=#99C7FF>MP {def.manaCost}</color>   ·   CD {def.cooldown:0.#}s", 8f);

        // Click = bind to first empty slot; drag = drop onto any slot (same path as the K menu).
        var btn = row.gameObject.AddComponent<Button>();
        btn.targetGraphic = row;
        var id = def.id;
        btn.onClick.AddListener(() => BindFirstEmpty(id));

        var drag = row.gameObject.AddComponent<SkillDragSource>();
        drag.skillId = id; drag.tint = SkillSymbol.ColorFor(def);
    }

    // The shared look of every row — icon, name, and one sub-line. Both kinds of skill draw the same face; only what
    // the sub-line SAYS and what sits on the right differ, so the layout lives here once.
    void BuildRowFace(Image row, SkillDef def, string subLine, float rightPad)
    {
        // The skill's face: its two letters on its weapon's colour, in place of the picture that used to sit here.
        var tile = NewImage(row.transform, SkillSymbol.ColorFor(def));
        tile.raycastTarget = false;
        var irt = tile.rectTransform;
        irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
        irt.pivot = new Vector2(0f, 0.5f);
        irt.anchoredPosition = new Vector2(6f, 0f);
        irt.sizeDelta = new Vector2(38f, 38f);

        var sym = NewText(tile.transform, def.Symbol, 16, FontStyle.Bold, TextAnchor.MiddleCenter);
        sym.color = Color.white; sym.raycastTarget = false;
        var symRt = sym.rectTransform;
        symRt.anchorMin = Vector2.zero; symRt.anchorMax = Vector2.one;
        symRt.offsetMin = Vector2.zero; symRt.offsetMax = Vector2.zero;

        float left = 50f;
        string name = string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName;
        var title = NewText(row.transform, name, 14, FontStyle.Bold, TextAnchor.LowerLeft);
        title.color = def.tint == default ? Color.white : def.tint;
        title.raycastTarget = false;
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0f, 0.5f); trt.anchorMax = new Vector2(1f, 1f);
        trt.offsetMin = new Vector2(left, 0f);
        trt.offsetMax = new Vector2(-rightPad, -4f);

        var sub = NewText(row.transform, subLine, 11, FontStyle.Normal, TextAnchor.UpperLeft);
        sub.color = SubColor;
        sub.raycastTarget = false;
        var srt = sub.rectTransform;
        srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 0.5f);
        srt.offsetMin = new Vector2(left, 4f);
        srt.offsetMax = new Vector2(-rightPad, 0f);
    }

    // A passive row: icon + name + what fires it, and a checkbox on the right that turns it on.
    void BuildPassiveRow(Image row, SkillDef def)
    {
        var loadout = Character.Instance != null ? Character.Instance.PassiveSkills : null;
        bool byWeapon = loadout != null && loadout.GrantedByWeapon(def.id);
        bool on = byWeapon || (loadout != null && loadout.IsChecked(def.id));

        BuildRowFace(row, def, byWeapon ? "Active from your weapon" : SkillTooltip.TriggerLine(def), CheckboxWidth);

        var box = MakeCheckbox(row.transform, on, !byWeapon && loadout != null);
        if (byWeapon) return;   // the weapon owns this one — nothing to toggle

        var id = def.id;
        box.onValueChanged.AddListener(v =>
        {
            if (!loadout.Set(id, v)) return;
            PassivesSaved.Now();
            UISounds.PlayClick();
        });
    }

    const float CheckboxWidth = 34f;

    // A 20×20 tick box. Built from primitives (this whole HUD is code-built — there are no UI prefabs to reference).
    Toggle MakeCheckbox(Transform parent, bool on, bool interactable)
    {
        var bg = NewImage(parent, interactable ? BoxFill : BoxFillLocked);
        var brt = bg.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
        brt.pivot = new Vector2(1f, 0.5f);
        brt.anchoredPosition = new Vector2(-9f, 0f);
        brt.sizeDelta = new Vector2(20f, 20f);

        var tick = NewImage(bg.transform, interactable ? UiKit.Cyan : BoxTickLocked);
        tick.raycastTarget = false;
        var trt = tick.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(4f, 4f); trt.offsetMax = new Vector2(-4f, -4f);

        var toggle = bg.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = bg;
        toggle.graphic = tick;
        toggle.isOn = on;
        toggle.interactable = interactable;
        return toggle;
    }

    void BindFirstEmpty(string id)
    {
        var c = Character.Instance;
        if (c == null || !c.Hotbar.AutoBind(id)) return;   // unknown, already bound, or bar full (ultimates → ult slot)
        PassivesSaved.Now();
        HotbarPanel.RebuildRequested?.Invoke();
        UISounds.PlayClick();
    }

    // ---- tiny UI helpers (match SkillDraftPanel's style) --------------------

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

/// <summary>Where a changed passive loadout goes. In a world the server writes the character down from ITS own
/// copy, which has never heard of a choice made here — so it is told, and told immediately, because the next
/// write is at most fifteen seconds away and would otherwise put the old choice back.</summary>
static class PassivesSaved
{
    public static void Now()
    {
        // A TICKED PASSIVE IS A SKILL THIS CHARACTER NOW CARRIES. Without rebuilding the list it has no
        // component behind it and would never fire — the tick would look accepted and do nothing.
        Character.Instance?.RefreshUsableSkills();
        HotbarPanel.RebuildRequested?.Invoke();

        var me = GameNetwork.MyPlayer;
        if (me != null && Character.Instance != null)
        {
            me.AskToSetPassives(Character.Instance.PassiveSkills.Capture());
        }
    }
}
