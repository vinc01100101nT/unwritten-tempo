using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builds the Phase E UI shell (HUD + Bag/Character/Skills toggle panels) skinned
/// with a chosen Ninja Adventure UI theme. The theme + frame scale are picked in
/// the <see cref="UIShellWindow"/> (Tools ▸ unwritten ▸ UI Shell Theme…) and stored
/// in EditorPrefs; the plain menu item rebuilds with whatever was last chosen.
///
/// Only "Theme Wood" ships a complete kit (panel + cell + bar); the Wip themes are
/// panel-only, so cells/bars fall back to Theme Wood. The pack's frames are
/// Multiple-mode sprites (which makes the 9-slice border a no-op), so
/// <see cref="SlicedSprite"/> forces them to Single before applying the border.
/// </summary>
public static class UIShellBuilder
{
    const string CanvasName = "GameUICanvas";
    const string UIRoot = "Assets/Art/NinjaAdventure/UI";
    const string WOOD   = UIRoot + "/Theme/Theme Wood";
    const string ITEMS  = UIRoot + "/Skill Icon/Items & Weapon";
    const string FontPath = UIRoot + "/Font/NormalFont.ttf";
    const string ResourcePrefabPath = "Assets/Resources/GameUICanvas.prefab";

    // EditorPrefs keys (shared with the picker window).
    public const string PrefTheme = "unwritten.ui.theme";
    public const string PrefScale = "unwritten.ui.refppu";
    public const string DefaultTheme = WOOD;
    public const int DefaultScale = 32;     // referencePixelsPerUnit; lower = thicker frame

    static Font uiFont;
    static Sprite panelSprite, cellSprite, barBgSprite;

    [MenuItem("Tools/unwritten/Build UI Shell")]
    static void BuildUIShellMenu()
    {
        Build(EditorPrefs.GetString(PrefTheme, DefaultTheme),
              EditorPrefs.GetInt(PrefScale, DefaultScale));
    }

    /// <summary>Builds (or rebuilds) the UI shell with the given theme folder and
    /// frame scale (= CanvasScaler.referencePixelsPerUnit; lower → chunkier).</summary>
    public static void Build(string themeFolder, int refPixelsPerUnit)
    {
        // The pack's NormalFont.ttf is a pixel font that renders blurry when scaled and
        // collapses spaces ("Leather Vest" -> "LeatherVest"); use the crisp built-in font.
        uiFont      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        panelSprite = SlicedSprite(themeFolder + "/nine_path_panel.png", new Vector4(6, 6, 6, 6));
        cellSprite  = SlicedSprite(FirstExisting(themeFolder + "/inventory_cell.png", WOOD + "/inventory_cell.png"), new Vector4(3, 3, 3, 3));
        barBgSprite = SlicedSprite(FirstExisting(themeFolder + "/nine_path_bg.png",    WOOD + "/nine_path_bg.png"),    new Vector4(2, 2, 2, 2));

        if (panelSprite == null)
        {
            EditorUtility.DisplayDialog("Build UI Shell",
                $"No 'nine_path_panel.png' in '{themeFolder}'. Pick a theme in " +
                "Tools ▸ unwritten ▸ UI Shell Theme…", "OK");
            return;
        }

        var existing = GameObject.Find(CanvasName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var canvasGO = new GameObject(CanvasName,
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.referencePixelsPerUnit = Mathf.Clamp(refPixelsPerUnit, 8, 96);
        var root = canvasGO.transform;

        SetupEventSystem(root);
        BuildHud(root);
        var bag    = BuildBagPanel(root);
        var chr    = BuildCharacterPanel(root);
        var skills = BuildSkillsPanel(root);
        BuildTooltip(root);       // on top of every panel
        // (The old UI-image sword cursor was retired — GameCursor now drives a hardware
        //  cursor from the Kenney pack, wired by Tools ▸ unwritten ▸ Setup Global Systems.)

        var toggle = canvasGO.AddComponent<PanelToggle>();
        toggle.panels = new[]
        {
            new PanelToggle.Binding { name = "Bag",       key = KeyCode.I, panel = bag },
            new PanelToggle.Binding { name = "Character", key = KeyCode.C, panel = chr },
            new PanelToggle.Binding { name = "Skills",    key = KeyCode.K, panel = skills },
        };

        canvasGO.AddComponent<PersistentUI>();        // survives scene loads (singleton)
        canvasGO.AddComponent<CharacterUIBinder>();   // drives HUD/stats/equip from Character
        canvasGO.AddComponent<BagSeeder>();           // fills the bag from Resources at runtime

        Undo.RegisterCreatedObjectUndo(canvasGO, "Build UI Shell");

        // Save as a Resources prefab so the UI auto-spawns in any scene and persists.
        EnsureFolder("Assets/Resources");
        PrefabUtility.SaveAsPrefabAssetAndConnect(canvasGO, ResourcePrefabPath, InteractionMode.AutomatedAction);

        EditorSceneManager.MarkSceneDirty(canvasGO.scene);
        Selection.activeGameObject = canvasGO;
        Debug.Log($"[unwritten] Built UI shell — theme '{ShortName(themeFolder)}', scale {scaler.referencePixelsPerUnit}. " +
                  $"Saved persistent prefab → {ResourcePrefabPath}. It now survives scene loads and auto-spawns " +
                  "in any scene. Press Play: I = Bag, C = Character, K = Skills, Esc = close all.");
    }

    // ---- theme discovery (used by the picker window) --------------------------

    /// <summary>All theme folders under UI/Theme that contain a nine_path_panel.png.</summary>
    public static List<string> DiscoverThemes()
    {
        var themes = AssetDatabase.FindAssets("nine_path_panel t:Texture2D", new[] { UIRoot + "/Theme" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => Path.GetFileNameWithoutExtension(p) == "nine_path_panel")
            .Select(p => p.Substring(0, p.LastIndexOf('/')))
            .Distinct()
            .OrderBy(p => p != WOOD)      // Theme Wood first (the complete kit)
            .ThenBy(p => p)
            .ToList();
        if (themes.Count == 0) themes.Add(WOOD);
        return themes;
    }

    public static string ShortName(string themeFolder)
    {
        var n = themeFolder.Replace(UIRoot + "/Theme/", "").Replace("Wip/", "");
        return n.StartsWith("Theme ") ? n.Substring(6) : n;   // "Theme Wood" -> "Wood"
    }

    // ---- HUD ------------------------------------------------------------------

    static void BuildHud(Transform canvas)
    {
        var bar = NewSliced("HUD_HealthBar", canvas, barBgSprite);
        Anchor(bar.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero);
        bar.rectTransform.anchoredPosition = new Vector2(16, 16);
        bar.rectTransform.sizeDelta = new Vector2(240, 28);
        // Filled image so CharacterUIBinder can drive fillAmount (needs a sprite + Filled type).
        var fill = NewColor("Fill", bar.transform, new Color(0.80f, 0.20f, 0.22f, 1f));
        fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        Fill(fill.rectTransform, new Vector2(4, 4), new Vector2(-4, -4));
        var hp = NewText("Label", bar.transform, "HP", 16, FontStyle.Bold, TextAnchor.MiddleCenter, true);
        Fill(hp.rectTransform, Vector2.zero, Vector2.zero);

        var holder = NewColor("HUD_Hotbar", canvas, new Color(0, 0, 0, 0f));
        Anchor(holder.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
        holder.rectTransform.anchoredPosition = new Vector2(0, 14);
        holder.rectTransform.sizeDelta = new Vector2(6 * 54, 54);
        var h = holder.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = false;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        for (int i = 0; i < 6; i++)
            NewSliced("Hotslot", holder.transform, cellSprite).rectTransform.sizeDelta = new Vector2(48, 48);
    }

    // ---- Panels ---------------------------------------------------------------

    static GameObject BuildBagPanel(Transform canvas)
    {
        var content = NewPanel(canvas, "BagPanel", new Vector2(0.62f, 0.16f), new Vector2(0.97f, 0.84f), "Bag");
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(48, 48);
        grid.spacing = new Vector2(6, 6);
        grid.padding = new RectOffset(2, 2, 2, 2);

        ItemStarterSet.EnsureStarterItems();   // make sure the starter assets exist (in Resources)
        for (int i = 0; i < 24; i++)           // slots are built empty; BagSeeder fills them at runtime
            MakeItemSlot(NewSliced("Slot", content, cellSprite), EquipSlotType.None, null);
        return content.parent.gameObject;
    }

    static GameObject BuildCharacterPanel(Transform canvas)
    {
        var content = NewPanel(canvas, "CharacterPanel", new Vector2(0.04f, 0.14f), new Vector2(0.44f, 0.86f), "Character");

        var stats = NewText("Stats", content,
            "Level   1\nHP      —\nATK     —\nDEF     —\nSPD     —",
            16, FontStyle.Normal, TextAnchor.UpperLeft, true);
        stats.rectTransform.anchorMin = new Vector2(0, 0);
        stats.rectTransform.anchorMax = new Vector2(0.46f, 1);
        stats.rectTransform.offsetMin = new Vector2(8, 8);
        stats.rectTransform.offsetMax = new Vector2(-4, -8);

        var equip = NewColor("Equipment", content, new Color(0, 0, 0, 0f));
        equip.rectTransform.anchorMin = new Vector2(0.46f, 0);
        equip.rectTransform.anchorMax = new Vector2(1f, 1);
        equip.rectTransform.offsetMin = new Vector2(4, 8);
        equip.rectTransform.offsetMax = new Vector2(-8, -8);
        var vlg = equip.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;
        foreach (var (label, type, icon) in new (string, EquipSlotType, string)[]
                 { ("Head",EquipSlotType.Head,"Helmet"),("Body",EquipSlotType.Body,"Armor"),
                   ("Weapon",EquipSlotType.Weapon,"Kunai"),("Off-hand",EquipSlotType.OffHand,"Guard"),
                   ("Boots",EquipSlotType.Boots,"Boot"),("Trinket",EquipSlotType.Trinket,"Amulet") })
            BuildEquipRow(equip.transform, label, type, icon);

        return content.parent.gameObject;
    }

    static void BuildEquipRow(Transform parent, string label, EquipSlotType type, string iconName)
    {
        var row = NewColor("Equip_" + label, parent, new Color(0, 0, 0, 0f));
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 42;

        var cell = NewSliced("Slot", row.transform, cellSprite);
        cell.rectTransform.anchorMin = new Vector2(0, 0.5f);
        cell.rectTransform.anchorMax = new Vector2(0, 0.5f);
        cell.rectTransform.pivot = new Vector2(0, 0.5f);
        cell.rectTransform.anchoredPosition = new Vector2(2, 0);
        cell.rectTransform.sizeDelta = new Vector2(40, 40);

        // Empty equip slots show the greyed "Disabled" icon as a hint.
        var placeholder = AssetDatabase.LoadAssetAtPath<Sprite>($"{ITEMS}/{iconName}Disabled.png");
        MakeItemSlot(cell, type, placeholder);

        var t = NewText("Label", row.transform, label, 16, FontStyle.Normal, TextAnchor.MiddleLeft, true);
        Fill(t.rectTransform, new Vector2(50, 0), new Vector2(-4, 0));
    }

    // A single shared item tooltip that follows the cursor (filled by ItemTooltip).
    static void BuildTooltip(Transform canvas)
    {
        var panel = NewSliced("ItemTooltip", canvas, panelSprite);
        var rt = panel.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);   // anchored top-left; ItemTooltip moves it
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(240, 80);
        panel.raycastTarget = false;

        var cg = panel.gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 8, 10);
        vlg.spacing = 1;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;

        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;  // keep the fixed 240 width
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;    // grow tall enough for the text

        var title = NewText("Title", panel.transform, "Item", 20, FontStyle.Bold, TextAnchor.UpperLeft, true);
        title.raycastTarget = false;
        var body = NewText("Body", panel.transform, "", 16, FontStyle.Normal, TextAnchor.UpperLeft, false);
        body.raycastTarget = false;
        body.supportRichText = true;

        var tip = panel.gameObject.AddComponent<ItemTooltip>();
        tip.panel = rt;
        tip.titleText = title;
        tip.bodyText = body;
        // Stays ACTIVE (so ItemTooltip.Awake runs and registers Instance); it hides
        // itself via the CanvasGroup alpha instead of being deactivated.
    }

    static GameObject BuildSkillsPanel(Transform canvas)
    {
        var content = NewPanel(canvas, "SkillsPanel", new Vector2(0.30f, 0.22f), new Vector2(0.70f, 0.78f), "Skills");
        var text = NewText("Placeholder", content,
            "Your drafted skills will live here.\nThe level-up draft + skill tree (Phase G).",
            16, FontStyle.Normal, TextAnchor.MiddleCenter, true);
        Fill(text.rectTransform, new Vector2(16, 16), new Vector2(-16, -16));
        return content.parent.gameObject;
    }

    static RectTransform NewPanel(Transform canvas, string name, Vector2 aMin, Vector2 aMax, string title)
    {
        var bg = NewSliced(name, canvas, panelSprite);
        bg.rectTransform.anchorMin = aMin;
        bg.rectTransform.anchorMax = aMax;
        bg.rectTransform.offsetMin = Vector2.zero;
        bg.rectTransform.offsetMax = Vector2.zero;

        var titleText = NewText("Title", bg.transform, title, 24, FontStyle.Bold, TextAnchor.UpperCenter, true);
        titleText.rectTransform.anchorMin = new Vector2(0, 1);
        titleText.rectTransform.anchorMax = new Vector2(1, 1);
        titleText.rectTransform.pivot = new Vector2(0.5f, 1);
        titleText.rectTransform.sizeDelta = new Vector2(0, 32);
        titleText.rectTransform.anchoredPosition = new Vector2(0, -10);

        var content = NewColor("Content", bg.transform, new Color(0, 0, 0, 0f));
        content.rectTransform.anchorMin = Vector2.zero;
        content.rectTransform.anchorMax = Vector2.one;
        content.rectTransform.offsetMin = new Vector2(16, 16);
        content.rectTransform.offsetMax = new Vector2(-16, -46);

        bg.gameObject.SetActive(false);
        return content.rectTransform;
    }

    // ---- primitives -----------------------------------------------------------

    static Image NewSliced(string name, Transform parent, Sprite sprite)
    {
        var img = NewColor(name, parent, Color.white);
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        return img;
    }

    // Turns an inventory-cell Image into a drag-and-drop ItemSlot with a child icon.
    static ItemSlot MakeItemSlot(Image cell, EquipSlotType accepts, Sprite placeholder)
    {
        cell.raycastTarget = true;                 // so OnDrop fires on the cell
        var icon = NewColor("Icon", cell.transform, Color.white);
        Fill(icon.rectTransform, new Vector2(4, 4), new Vector2(-4, -4));

        var slot = cell.gameObject.AddComponent<ItemSlot>();
        slot.accepts = accepts;
        slot.iconImage = icon;
        slot.emptyPlaceholder = placeholder;

        icon.gameObject.AddComponent<DraggableItem>().slot = slot;
        slot.SetItem(null);                        // show placeholder / hide
        return slot;
    }

    static Image NewColor(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static Text NewText(string name, Transform parent, string content, int size, FontStyle style, TextAnchor anchor, bool shadow)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = anchor;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = content;
        if (shadow)
        {
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.7f);
            sh.effectDistance = new Vector2(1, -1);
        }
        return t;
    }

    static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot)
    {
        rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot;
    }

    static void Fill(RectTransform rt, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
    }

    // One EventSystem, parented under the Canvas so it travels with the persistent
    // UI. Removes any standalone EventSystems first so there's never a duplicate.
    static void SetupEventSystem(Transform canvas)
    {
        foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
            Undo.DestroyObjectImmediate(es.gameObject);
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        go.transform.SetParent(canvas, false);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        var leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    static string FirstExisting(string a, string b)
        => AssetDatabase.LoadAssetAtPath<Texture2D>(a) != null ? a : b;

    // Sets 9-slice border + FullRect mesh, then loads the sprite.
    //  • Force Single mode: the pack ships frames as Multiple, where the top-level
    //    border is ignored (so the frame stretches).
    //  • spriteBorder is set LAST (SetTextureSettings re-reads the on-disk border).
    //  • Force a synchronous reimport so the Library sprite isn't a stale border-0 copy.
    static Sprite SlicedSprite(string path, Vector4 border)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null)
        {
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteMode = (int)SpriteImportMode.Single;
            s.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(s);
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spriteBorder = border;
            ti.SaveAndReimport();
            AssetDatabase.ImportAsset(path,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite != null && sprite.border == Vector4.zero)
            Debug.LogWarning($"[unwritten] 9-slice border still zero on '{path}'. Right-click ▸ Reimport, then rebuild.");
        return sprite;
    }
}
