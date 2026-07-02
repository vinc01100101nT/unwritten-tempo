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
    const string METEO  = UIRoot + "/Skill Icon/Meteo";   // weather icons (Sun/Rain/Snow) for the calendar
    const string FontPath = UIRoot + "/Font/NormalFont.ttf";
    const string ResourcePrefabPath = "Assets/Resources/GameUICanvas.prefab";

    // EditorPrefs keys (shared with the picker window).
    public const string PrefTheme = "unwritten.ui.theme";
    public const string PrefScale = "unwritten.ui.refppu";
    public const string DefaultTheme = WOOD;
    public const int DefaultScale = 32;     // referencePixelsPerUnit; lower = thicker frame

    static Font uiFont;
    static Sprite panelSprite, cellSprite, barBgSprite;

    [MenuItem("Tools/unwritten/Scene Setup/Build UI Shell (Systems scene)", priority = 110)]
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
        // All panels' hotkeys/Esc are owned by their own runtime controllers (code-driven, no rebake to change a key):
        //   Skills (K)→SkillDraftPanel · Outfit (O)→CharacterCustomizePanel · Calendar (L)→CalendarPanel ·
        //   Inventory (B)/Equipment (V)/Character (C)→ProgressionWindows. PanelToggle is retired.
        BuildSkillsPanel(root);
        BuildCustomizePanel(root);
        BuildCalendarPanel(root);
        // Bag / Character / Equipment panels + the item tooltip were REMOVED — replaced by the new
        // ProgressionWindows trio (Inventory I · Equipment E · Character C, built via
        // Tools ▸ unwritten ▸ UI ▸ Build Progression Windows). Stat-point allocation moved into that
        // Character window, so StatPanel + BagSeeder are no longer added here.
        // (The old UI-image sword cursor was retired — GameCursor now drives a hardware
        //  cursor from the Kenney pack, wired by Tools ▸ unwritten ▸ Setup Global Systems.)

        // (No PanelToggle: every window now owns its own hotkey + Esc in its runtime controller.)

        canvasGO.AddComponent<PersistentUI>();             // survives scene loads (singleton)
        canvasGO.AddComponent<CharacterUIBinder>();        // drives the HUD health/mana bars from Character
        canvasGO.AddComponent<SkillDraftPanel>();          // Phase G: builds the Trio-Ring draft into the Skills (K) panel
        canvasGO.AddComponent<SkillBookPanel>();           // Grimoire (J): browse all composed skills + bind to the hotbar
        canvasGO.AddComponent<HotbarPanel>();              // populates HUD_Hotbar with the player's actives (icon + cooldown)
        canvasGO.AddComponent<XpBarHUD>();                 // bottom-of-screen XP bars: character (left) + ring (right)

        // The new progression window trio (Inventory I · Equipment E · Character C) — replaces the old
        // Bag/Character panels. Builds itself into this canvas at runtime; needs the starter Item/Card assets.
        ItemStarterSet.EnsureStarterItems();
        ProgressionWindowsBuilder.EnsureStarterCards();
        var prog = canvasGO.AddComponent<ProgressionWindows>();   // also pulls in InventoryStore (RequireComponent)
        // Bake the empty equip-slot silhouettes (faint gold watermarks) from the Ninja "Items & Weapon" set —
        // the runtime can't AssetDatabase-load, so wire the Sprite refs into the prefab now (same pattern as the
        // calendar weather icons below). Mismatched-but-close stubs until the final flourish kit ships.
        Sprite SlotIcon(string n) => AssetDatabase.LoadAssetAtPath<Sprite>(ITEMS + "/" + n + ".png");
        prog.silHead       = SlotIcon("Helmet");
        prog.silArmor      = SlotIcon("Armor");
        prog.silGarment    = SlotIcon("Scroll");
        prog.silWeapon     = SlotIcon("Kunai");
        prog.silShield     = SlotIcon("Guard");
        prog.silAccessory1 = SlotIcon("Ring");
        prog.silAccessory2 = SlotIcon("Amulet");
        canvasGO.AddComponent<CharacterCustomizePanel>();  // O = Outfit: animated preview + cloth colour palette
        var calPanel = canvasGO.AddComponent<CalendarPanel>();  // L = Calendar: 12-day season grid w/ per-day weather (+ click the date widget)
        // Bake in the Ninja Adventure "Meteo" weather icons (the runtime can't AssetDatabase-load, so we wire the
        // Sprite refs into the prefab now — same pattern as the skill-icon Sprites the hotbar reads).
        calPanel.sunIcon  = AssetDatabase.LoadAssetAtPath<Sprite>(METEO + "/Sun.png");
        calPanel.rainIcon = AssetDatabase.LoadAssetAtPath<Sprite>(METEO + "/Rain.png");
        calPanel.snowIcon = AssetDatabase.LoadAssetAtPath<Sprite>(METEO + "/Snow.png");

        // UI click sounds (TomMusic latch foley): panel open/close + a generic button click.
        var ui = canvasGO.AddComponent<UISounds>();
        ui.open  = SfxAssets.Set(0.6f, "Doors Gates and Chests", "Chest Open 1",  "Chest Open 2");
        ui.close = SfxAssets.Set(0.6f, "Doors Gates and Chests", "Chest Close 1", "Chest Close 2");
        ui.click = SfxAssets.Set(0.5f, "Doors Gates and Chests", "Lock Unlock");

        Undo.RegisterCreatedObjectUndo(canvasGO, "Build UI Shell");

        // Save as a Resources prefab so the UI auto-spawns in any scene and persists.
        EnsureFolder("Assets/Resources");
        PrefabUtility.SaveAsPrefabAssetAndConnect(canvasGO, ResourcePrefabPath, InteractionMode.AutomatedAction);

        EditorSceneManager.MarkSceneDirty(canvasGO.scene);
        Selection.activeGameObject = canvasGO;
        Debug.Log($"[unwritten] Built UI shell — theme '{ShortName(themeFolder)}', scale {scaler.referencePixelsPerUnit}. " +
                  $"Saved persistent prefab → {ResourcePrefabPath}. It now survives scene loads and auto-spawns " +
                  "in any scene. Press Play: B = Inventory, V = Equipment, C = Character, K = Skills, Esc = close all.");
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
        // Bottom-left stack is lifted 28px to clear the full-width XP strip (XpBarHUD) along the very bottom.
        bar.rectTransform.anchoredPosition = new Vector2(16, 44);
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

        // Mana bar, sitting just above the health bar (blue).
        var mana = NewSliced("HUD_ManaBar", canvas, barBgSprite);
        Anchor(mana.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero);
        mana.rectTransform.anchoredPosition = new Vector2(16, 76);
        mana.rectTransform.sizeDelta = new Vector2(200, 18);
        var mfill = NewColor("Fill", mana.transform, new Color(0.25f, 0.45f, 0.95f, 1f));
        mfill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        mfill.type = Image.Type.Filled;
        mfill.fillMethod = Image.FillMethod.Horizontal;
        mfill.fillOrigin = (int)Image.OriginHorizontal.Left;
        Fill(mfill.rectTransform, new Vector2(3, 3), new Vector2(-3, -3));
        var mp = NewText("Label", mana.transform, "MP", 13, FontStyle.Bold, TextAnchor.MiddleCenter, true);
        Fill(mp.rectTransform, Vector2.zero, Vector2.zero);

        // Hotkey legend, sitting just above the mana bar (lower-left stack: hotkeys ▸ MP ▸ HP).
        BuildHotkeyLegend(canvas);

        // Minimap — top-right corner (terrain RT + player/mob blips). Sits above the gold counter.
        BuildMinimap(canvas);

        // Gold counter — top-right, tucked just under the minimap; GoldCounter.cs wires it to Wallet.OnChanged.
        var goldPanel = NewSliced("HUD_GoldPanel", canvas, barBgSprite);
        Anchor(goldPanel.rectTransform, Vector2.one, Vector2.one, Vector2.one);
        goldPanel.rectTransform.anchoredPosition = new Vector2(-16, -(16 + MinimapSize + 8));
        goldPanel.rectTransform.sizeDelta = new Vector2(120, 26);
        var goldLabel = NewText("HUD_Gold", goldPanel.transform, "0 z", 15, FontStyle.Bold, TextAnchor.MiddleCenter, true);
        Fill(goldLabel.rectTransform, new Vector2(4, 2), new Vector2(-4, -2));
        goldLabel.color = new Color(1f, 0.90f, 0.3f);   // warm gold tint
        goldLabel.gameObject.AddComponent<GoldCounter>();

        // Calendar + weather readout — continues the top-right stack (minimap ▸ gold ▸ date/weather).
        // CalendarWeatherHUD.cs wires the two text lines to GameCalendar.DateString + WeatherDirector.CurrentName.
        var calPanel = NewSliced("HUD_CalendarPanel", canvas, barBgSprite);
        Anchor(calPanel.rectTransform, Vector2.one, Vector2.one, Vector2.one);
        calPanel.rectTransform.anchoredPosition = new Vector2(-16, -(16 + MinimapSize + 8 + 26 + 8));
        calPanel.rectTransform.sizeDelta = new Vector2(150, 42);
        calPanel.raycastTarget = false;   // purely informational — never eats clicks
        var calLabel = NewText("HUD_Calendar", calPanel.transform, "Spring · Day 1\nClear", 13, FontStyle.Bold, TextAnchor.MiddleCenter, true);
        Fill(calLabel.rectTransform, new Vector2(4, 2), new Vector2(-4, -2));
        calLabel.raycastTarget = false;
        calLabel.gameObject.AddComponent<CalendarWeatherHUD>();

        var holder = NewColor("HUD_Hotbar", canvas, new Color(0, 0, 0, 0f));
        Anchor(holder.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
        holder.rectTransform.anchoredPosition = new Vector2(0, 42);   // lifted to clear the bottom XP strip (XpBarHUD)
        holder.rectTransform.sizeDelta = new Vector2(9 * 54, 54);   // nine bindable slots (keys 1–9)
        var h = holder.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = false;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        for (int i = 0; i < 9; i++)
            NewSliced("Hotslot", holder.transform, cellSprite).rectTransform.sizeDelta = new Vector2(48, 48);
    }

    const int MinimapSize = 180;   // square minimap edge (px @ reference resolution)

    // Top-right minimap: a themed frame holding a RawImage (terrain RenderTexture) + a clipped blip layer.
    // The Minimap component spawns the top-down camera/RT at runtime and projects player/mob dots onto blipRoot.
    static void BuildMinimap(Transform canvas)
    {
        var panel = NewSliced("HUD_Minimap", canvas, panelSprite);
        Anchor(panel.rectTransform, Vector2.one, Vector2.one, Vector2.one);   // top-right corner
        panel.rectTransform.anchoredPosition = new Vector2(-16, -16);
        panel.rectTransform.sizeDelta = new Vector2(MinimapSize, MinimapSize);
        panel.raycastTarget = false;

        // Terrain RenderTexture surface, inset so the themed frame border shows around it.
        var viewGo = new GameObject("MinimapView", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        viewGo.transform.SetParent(panel.transform, false);
        var view = viewGo.GetComponent<RawImage>();
        view.raycastTarget = false;
        Fill(view.rectTransform, new Vector2(7, 7), new Vector2(-7, -7));

        // Blip layer over the RT, clipped to the same inset so dots can't spill onto the frame.
        var blipGo = new GameObject("Blips", typeof(RectTransform), typeof(RectMask2D));
        blipGo.transform.SetParent(panel.transform, false);
        Fill(blipGo.GetComponent<RectTransform>(), new Vector2(7, 7), new Vector2(-7, -7));

        var minimap = canvas.GetComponent<Minimap>() ?? canvas.gameObject.AddComponent<Minimap>();
        minimap.view = view;
        minimap.blipRoot = blipGo.GetComponent<RectTransform>();
    }

    // A small always-on controls cheat-sheet in the lower-left, just above the HP/MP bars.
    // Two text columns (keys + actions) inside a themed panel; purely informational, so it
    // never eats clicks (raycastTarget off on every graphic).
    static void BuildHotkeyLegend(Transform canvas)
    {
        // Keep the two arrays in lock-step — left column = key, right column = what it does.
        const string keys =
            "L-Click\nR-Drag\nA\nQ\nT\nS\nC\nB\nV\nK\nO\nL\nM\nP\nEsc";
        const string actions =
            "Move\nOrbit camera\nArm attack (+click)\nCast spell\nAuto-combat\nStop\nCharacter\nInventory\nEquipment\nSkills\nOutfit\nCalendar\nMinimap\nDev panel\nClose / cancel";

        var panel = NewSliced("HUD_Hotkeys", canvas, panelSprite);
        Anchor(panel.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero);   // bottom-left
        panel.rectTransform.anchoredPosition = new Vector2(16, 102);             // above MP bar (y 76 + 18 + gap)
        panel.rectTransform.sizeDelta = new Vector2(210, 278);
        panel.raycastTarget = false;

        var title = NewText("Title", panel.transform, "HOTKEYS", 14, FontStyle.Bold, TextAnchor.UpperCenter, true);
        title.rectTransform.anchorMin = new Vector2(0, 1);
        title.rectTransform.anchorMax = new Vector2(1, 1);
        title.rectTransform.pivot = new Vector2(0.5f, 1);
        title.rectTransform.sizeDelta = new Vector2(0, 22);
        title.rectTransform.anchoredPosition = new Vector2(0, -8);
        title.color = new Color(1f, 0.88f, 0.5f);   // warm accent
        title.raycastTarget = false;

        var keyText = NewText("Keys", panel.transform, keys, 13, FontStyle.Bold, TextAnchor.UpperLeft, true);
        Fill(keyText.rectTransform, new Vector2(14, 10), new Vector2(-118, -32));
        keyText.color = new Color(1f, 0.85f, 0.45f);   // keys stand out in amber
        keyText.raycastTarget = false;

        var actText = NewText("Actions", panel.transform, actions, 13, FontStyle.Normal, TextAnchor.UpperLeft, true);
        Fill(actText.rectTransform, new Vector2(92, 10), new Vector2(-10, -32));
        actText.raycastTarget = false;
    }

    // ---- Panels ---------------------------------------------------------------

    static GameObject BuildSkillsPanel(Transform canvas)
    {
        var content = NewPanel(canvas, "SkillsPanel", new Vector2(0.30f, 0.22f), new Vector2(0.70f, 0.78f), "Skills");
        var text = NewText("Placeholder", content,
            "Your drafted skills will live here.\nThe level-up draft + skill tree (Phase G).",
            16, FontStyle.Normal, TextAnchor.MiddleCenter, true);
        Fill(text.rectTransform, new Vector2(16, 16), new Vector2(-16, -16));
        return content.parent.gameObject;
    }

    // Centre-wide panel — leaves room for the left preview + right swatch palette.
    // CharacterCustomizePanel.TryBuild() populates the Content area at runtime.
    static GameObject BuildCustomizePanel(Transform canvas)
    {
        var content = NewPanel(canvas, "CustomizePanel",
            new Vector2(0.22f, 0.18f), new Vector2(0.78f, 0.84f), "Outfit");
        // Content is intentionally left empty here — CharacterCustomizePanel builds
        // the animated preview + colour palette into it at runtime.
        return content.parent.gameObject;
    }

    // Empty themed window — CalendarPanel.cs builds the header + 4×3 day-cell grid into Content at runtime
    // (each cell shows that day's deterministic weather). Toggled by L / clicking the top-right date widget.
    static GameObject BuildCalendarPanel(Transform canvas)
    {
        var content = NewPanel(canvas, "CalendarPanel", new Vector2(0.28f, 0.18f), new Vector2(0.72f, 0.82f), "Calendar");
        return content.parent.gameObject;
    }

    const int HeaderH = 30;   // height of the draggable title bar across the top of every window

    static RectTransform NewPanel(Transform canvas, string name, Vector2 aMin, Vector2 aMax, string title)
    {
        var bg = NewSliced(name, canvas, panelSprite);
        bg.rectTransform.anchorMin = aMin;
        bg.rectTransform.anchorMax = aMax;
        bg.rectTransform.offsetMin = Vector2.zero;
        bg.rectTransform.offsetMax = Vector2.zero;

        BuildWindowHeader(bg, title);

        var content = NewColor("Content", bg.transform, new Color(0, 0, 0, 0f));
        content.rectTransform.anchorMin = Vector2.zero;
        content.rectTransform.anchorMax = Vector2.one;
        content.rectTransform.offsetMin = new Vector2(16, 16);
        content.rectTransform.offsetMax = new Vector2(-16, -(HeaderH + 16));   // start below the header bar

        bg.gameObject.SetActive(false);
        return content.rectTransform;
    }

    // A draggable title bar across the top of a window: title on the left, an ✕ close
    // button on the right, and the whole bar acts as the drag handle (WindowDragHandle
    // moves the window; WindowCloseButton hides it). The bar is inset a little so the
    // window's 9-slice frame still shows around it.
    static void BuildWindowHeader(Image bg, string title)
    {
        var header = NewSliced("Header", bg.transform, barBgSprite);
        header.rectTransform.anchorMin = new Vector2(0, 1);
        header.rectTransform.anchorMax = new Vector2(1, 1);
        header.rectTransform.pivot = new Vector2(0.5f, 1);
        header.rectTransform.offsetMin = new Vector2(6, -(6 + HeaderH));   // left 6, bottom = top - HeaderH
        header.rectTransform.offsetMax = new Vector2(-6, -6);              // right 6, top 6 below the frame
        header.gameObject.AddComponent<WindowDragHandle>().window = bg.rectTransform;

        var titleText = NewText("Title", header.transform, title, 20, FontStyle.Bold, TextAnchor.MiddleLeft, true);
        titleText.rectTransform.anchorMin = Vector2.zero;
        titleText.rectTransform.anchorMax = Vector2.one;
        titleText.rectTransform.offsetMin = new Vector2(12, 0);
        titleText.rectTransform.offsetMax = new Vector2(-(HeaderH + 4), 0);   // leave room for the ✕ on the right
        titleText.raycastTarget = false;   // let clicks fall through to the drag handle

        // ✕ close button — a square red cell pinned to the right edge of the header.
        var close = NewColor("Close", header.transform, new Color(0.62f, 0.14f, 0.14f, 0.95f));
        close.raycastTarget = true;   // must catch the click (WindowCloseButton listens here)
        close.rectTransform.anchorMin = new Vector2(1, 0.5f);
        close.rectTransform.anchorMax = new Vector2(1, 0.5f);
        close.rectTransform.pivot = new Vector2(1, 0.5f);
        close.rectTransform.sizeDelta = new Vector2(HeaderH - 8, HeaderH - 8);
        close.rectTransform.anchoredPosition = new Vector2(-5, 0);
        close.gameObject.AddComponent<WindowCloseButton>().window = bg.gameObject;

        var x = NewText("X", close.transform, "X", 16, FontStyle.Bold, TextAnchor.MiddleCenter, true);
        Fill(x.rectTransform, Vector2.zero, Vector2.zero);
        x.raycastTarget = false;   // the parent cell catches the click
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
