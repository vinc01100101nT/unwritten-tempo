using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One-click preview of the Step-2 shared HUD widgets: builds a self-contained demo canvas with a
/// <see cref="WindowFrame"/> window holding a row of richly-wired <see cref="ItemSlot"/>s (border =
/// rarity · +N badge · ◇ pips · cyan select/hover) plus the shared <see cref="ItemTooltip"/>. Press
/// Play to see the locked channels render across every rarity / +N / card state; drag the title bar,
/// hover a slot for the tooltip. Self-contained — doesn't touch the real GameUICanvas. Re-run to rebuild.
///
/// QUICKFIX 2026-06-25 — preview harness for the HUD widget pass; delete once the real
/// Inventory/Equipment/Character windows (Step 3) render the channels in-game.
/// </summary>
public static class HudWidgetBuilder
{
    const string CanvasName = "HudWidgetDemoCanvas";
    const string ITEMS = "Assets/Art/NinjaAdventure/UI/Skill Icon/Items & Weapon";

    static readonly Color PanelFill  = new Color(0.082f, 0.086f, 0.102f, 0.98f); // #15161A
    static readonly Color SlotFill   = new Color(0.07f, 0.075f, 0.09f, 1f);
    static readonly Color GoldAccent = new Color(0.784f, 0.659f, 0.384f, 1f);    // #C8A862
    static readonly Color Cyan       = new Color(0.35f, 0.95f, 1f, 1f);
    static readonly Color CyanGlow   = new Color(0.30f, 0.85f, 1f, 0.45f);

    static Font uiFont;

    [MenuItem("Tools/unwritten/UI/Build HUD Widgets", priority = 120)]
    static void Build()
    {
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var existing = GameObject.Find(CanvasName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var canvasGO = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;   // sit above the game UI if both are present
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        var root = canvasGO.transform;

        EnsureEventSystem(root);

        // ---- window ----------------------------------------------------------
        var border = NewColor("Window", root, GoldAccent);
        border.rectTransform.anchorMin = border.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        border.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        border.rectTransform.sizeDelta = new Vector2(760, 230);
        var frame = border.gameObject.AddComponent<WindowFrame>();
        frame.persistKey = "hud_widget_demo";

        var fill = NewColor("Fill", border.transform, PanelFill);
        Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        fill.raycastTarget = true;   // catch clicks for bring-to-front

        // header (drag handle)
        var header = NewColor("Header", fill.transform, new Color(GoldAccent.r, GoldAccent.g, GoldAccent.b, 0.18f));
        header.rectTransform.anchorMin = new Vector2(0, 1);
        header.rectTransform.anchorMax = new Vector2(1, 1);
        header.rectTransform.pivot = new Vector2(0.5f, 1);
        header.rectTransform.sizeDelta = new Vector2(0, 34);
        header.gameObject.AddComponent<WindowDragHandle>().window = border.rectTransform;
        var title = NewText("Title", header.transform, "HUD WIDGETS — ItemSlot demo (drag me · hover a slot)",
            16, FontStyle.Bold, TextAnchor.MiddleLeft);
        Fill(title.rectTransform, new Vector2(14, 0), new Vector2(-14, 0));
        title.color = GoldAccent;
        title.raycastTarget = false;

        // slot row
        var rowGo = NewColor("Slots", fill.transform, new Color(0, 0, 0, 0));
        rowGo.rectTransform.anchorMin = new Vector2(0, 0);
        rowGo.rectTransform.anchorMax = new Vector2(1, 1);
        rowGo.rectTransform.offsetMin = new Vector2(16, 40);
        rowGo.rectTransform.offsetMax = new Vector2(-16, -44);
        rowGo.raycastTarget = false;
        var hlg = rowGo.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = false;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        const int count = 6;
        var slots = new ItemSlot[count];
        for (int i = 0; i < count; i++) slots[i] = BuildRichSlot(rowGo.transform, 84);

        var caption = NewText("Caption", fill.transform,
            "border = rarity   ·   +N = enhancement   ·   ◇/□ = card slots   ·   cyan ring = selected   ·   cyan bloom = hover",
            12, FontStyle.Normal, TextAnchor.LowerCenter);
        caption.rectTransform.anchorMin = new Vector2(0, 0);
        caption.rectTransform.anchorMax = new Vector2(1, 0);
        caption.rectTransform.pivot = new Vector2(0.5f, 0);
        caption.rectTransform.sizeDelta = new Vector2(-20, 22);
        caption.rectTransform.anchoredPosition = new Vector2(0, 8);
        caption.color = new Color(0.65f, 0.69f, 0.74f);
        caption.raycastTarget = false;

        BuildTooltip(root);

        // wire the runtime populator
        var demo = canvasGO.AddComponent<HudWidgetDemo>();
        demo.slots = slots;
        demo.icons = LoadDemoIcons();

        Undo.RegisterCreatedObjectUndo(canvasGO, "Build HUD Widgets");
        EditorSceneManager.MarkSceneDirty(canvasGO.scene);
        Selection.activeGameObject = canvasGO;
        Debug.Log("[unwritten] Built HUD Widgets demo canvas. Press Play to see the ItemSlot channels render; " +
                  "drag the window title bar, hover a slot for the tooltip. (Tools ▸ unwritten ▸ UI ▸ Build HUD Widgets)");
    }

    // A slot wired with all the locked channels. Sibling order matters: glow (back) → rarity border →
    // select ring → dark fill (+icon) → badge/pips (front), so the cyan ring pokes 1px INSIDE the
    // rarity border and the icon always stays on top.
    static ItemSlot BuildRichSlot(Transform parent, int size)
    {
        var border = NewColor("Slot", parent, Color.white);   // root Image = rarity border
        border.rectTransform.sizeDelta = new Vector2(size, size);
        var le = border.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = size;
        border.raycastTarget = true;

        var glow = NewColor("HoverGlow", border.transform, CyanGlow);
        Fill(glow.rectTransform, new Vector2(-6, -6), new Vector2(6, 6));
        glow.raycastTarget = false;
        glow.gameObject.SetActive(false);

        var ring = NewColor("SelectRing", border.transform, Cyan);
        Fill(ring.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        ring.raycastTarget = false;
        ring.gameObject.SetActive(false);

        var fill = NewColor("Fill", border.transform, SlotFill);
        Fill(fill.rectTransform, new Vector2(3, 3), new Vector2(-3, -3));
        fill.raycastTarget = false;

        var icon = NewColor("Icon", fill.transform, Color.white);
        Fill(icon.rectTransform, new Vector2(8, 8), new Vector2(-8, -8));
        icon.raycastTarget = false;
        icon.preserveAspect = true;

        var badge = NewText("Badge", border.transform, "", 15, FontStyle.Bold, TextAnchor.UpperLeft);
        badge.color = GoldAccent;
        badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(0, 1);
        badge.rectTransform.pivot = new Vector2(0, 1);
        badge.rectTransform.sizeDelta = new Vector2(44, 20);
        badge.rectTransform.anchoredPosition = new Vector2(4, -3);
        badge.raycastTarget = false;

        var pips = NewText("Pips", border.transform, "", 16, FontStyle.Bold, TextAnchor.LowerCenter);
        pips.rectTransform.anchorMin = new Vector2(0, 0);
        pips.rectTransform.anchorMax = new Vector2(1, 0);
        pips.rectTransform.pivot = new Vector2(0.5f, 0);
        pips.rectTransform.sizeDelta = new Vector2(0, 18);
        pips.rectTransform.anchoredPosition = new Vector2(0, 2);
        pips.raycastTarget = false;

        var slot = border.gameObject.AddComponent<ItemSlot>();
        slot.iconImage = icon;
        slot.rarityBorder = border;
        slot.upgradeBadge = badge;
        slot.cardPips = pips;
        slot.selectRing = ring.gameObject;
        slot.hoverGlow = glow.gameObject;
        return slot;
    }

    static void BuildTooltip(Transform canvas)
    {
        var border = NewColor("ItemTooltip", canvas, GoldAccent);
        var rt = border.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(264, 90);
        border.raycastTarget = false;

        var cg = border.gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        var fill = NewColor("Fill", border.transform, PanelFill);
        Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        fill.raycastTarget = false;
        var vlg = fill.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 8, 10);
        vlg.spacing = 2;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;

        var fitter = border.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var title = NewText("Title", fill.transform, "Item", 19, FontStyle.Bold, TextAnchor.UpperLeft);
        title.raycastTarget = false;
        var body = NewText("Body", fill.transform, "", 15, FontStyle.Normal, TextAnchor.UpperLeft);
        body.raycastTarget = false;
        body.supportRichText = true;

        var tip = border.gameObject.AddComponent<ItemTooltip>();
        tip.panel = rt;
        tip.titleText = title;
        tip.bodyText = body;
    }

    static Sprite[] LoadDemoIcons()
    {
        string[] names = { "Kunai", "Sword", "Axe", "Bow", "Staff", "Hammer", "Armor", "Helmet", "Amulet", "Boot", "Guard", "Ring" };
        var list = new System.Collections.Generic.List<Sprite>();
        foreach (var n in names)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{ITEMS}/{n}.png");
            if (s != null) list.Add(s);
        }
        return list.ToArray();
    }

    // ---- primitives (self-contained; mirror UIShellBuilder's helpers) ----------

    static Image NewColor(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static Text NewText(string name, Transform parent, string content, int size, FontStyle style, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = uiFont;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = anchor;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = content;
        var sh = go.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.7f);
        sh.effectDistance = new Vector2(1, -1);
        return t;
    }

    static void Fill(RectTransform rt, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
    }

    static void EnsureEventSystem(Transform canvas)
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        go.transform.SetParent(canvas, false);
    }
}
