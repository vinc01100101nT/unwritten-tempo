using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds the Phase E UI shell (HUD + the Skills, Customize and Calendar panels) skinned
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
    const string UIRoot = "Assets/1_Authored/Art/Billboards/Ui";
    const string WOOD   = UIRoot + "/Theme/Theme Wood";
    const string METEO  = UIRoot + "/Skill Icon/Meteo";   // weather icons (Sun/Rain/Snow) for the calendar
    const string FontPath = UIRoot + "/Font/NormalFont.ttf";
    const string ResourcePrefabPath = "Assets/3_Data/Resources/GameUICanvas.prefab";

    // EditorPrefs keys (shared with the picker window).
    public const string PrefTheme = "unwritten.ui.theme";
    public const string PrefScale = "unwritten.ui.refppu";
    public const string DefaultTheme = WOOD;
    public const int DefaultScale = 32;     // referencePixelsPerUnit; lower = thicker frame

    static Sprite panelSprite, cellSprite, barBgSprite;

    [MenuItem("Tools/unwritten/UI/Build UI Shell (Systems scene)", priority = 110)]
    static void BuildUIShellMenu()
    {
        Build(EditorPrefs.GetString(PrefTheme, DefaultTheme),
              EditorPrefs.GetInt(PrefScale, DefaultScale));
    }

    /// <summary>Builds (or rebuilds) the UI shell with the given theme folder and
    /// frame scale (= CanvasScaler.referencePixelsPerUnit; lower → chunkier).</summary>
    public static void Build(string themeFolder, int refPixelsPerUnit)
    {
        panelSprite = SlicedSprite(themeFolder + "/nine_path_panel.png", new Vector4(6, 6, 6, 6));

        // Self-heal a STALE saved theme (e.g. a pre-reorg EditorPrefs path that no longer exists): fall back to the
        // default Wood theme and re-save it, instead of aborting. Only the resave clears the bad pref for next time.
        if (panelSprite == null && themeFolder != DefaultTheme)
        {
            Debug.LogWarning($"[unwritten] UI theme '{themeFolder}' has no nine_path_panel.png — falling back to '{DefaultTheme}'.");
            themeFolder = DefaultTheme;
            EditorPrefs.SetString(PrefTheme, themeFolder);
            panelSprite = SlicedSprite(themeFolder + "/nine_path_panel.png", new Vector4(6, 6, 6, 6));
        }

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
        UiKit.ApplyTextCrispness(scaler);
        var root = canvasGO.transform;

        SetupEventSystem(root);
        BuildHud(root);
        // All panels' hotkeys/Esc are owned by their own runtime controllers (code-driven, no rebake to change a key):
        //   Skills (K)→SkillDraftPanel · Wardrobe (O)→CharacterWardrobePanel · Calendar (L)→CalendarPanel ·
        //   Inventory (B)/Character (C)→ProgressionWindows. PanelToggle is retired.
        BuildSkillsPanel(root);
        BuildCustomizePanel(root);
        BuildCalendarPanel(root);
        // The Inventory and Character windows are built by ProgressionWindows (added below), and stat-point
        // allocation lives in that Character window, so StatPanel + BagSeeder are not added here.
        // (The old UI-image sword cursor was retired — GameCursor now drives a hardware
        //  cursor from the Kenney pack, wired by Tools ▸ unwritten ▸ Setup Global Systems.)

        // (No PanelToggle: every window now owns its own hotkey + Esc in its runtime controller.)

        canvasGO.AddComponent<PersistentUI>();             // survives scene loads (singleton)
        canvasGO.AddComponent<CharacterUIBinder>();        // drives the HUD health/mana bars from Character
        canvasGO.AddComponent<SkillDraftPanel>();          // Phase G: builds the Trio-Ring draft into the Skills (K) panel
        canvasGO.AddComponent<SkillBookPanel>();           // Grimoire (J): browse all composed skills + bind to the hotbar
        canvasGO.AddComponent<CraftStationPanel>();        // the smith / loom / tannery window, opened by reaching one
        canvasGO.AddComponent<HotbarPanel>();              // populates HUD_Hotbar with the player's actives (icon + cooldown)
        canvasGO.AddComponent<BuffBarPanel>();             // HUD buff bar (right edge): active self-buffs w/ icon + countdown
        canvasGO.AddComponent<XpBarHUD>();                 // bottom-of-screen XP bars: character (left) + ring (right)
        canvasGO.AddComponent<ChatController>();            // in-game chat window (Enter) + /command console (GmCommands)
        canvasGO.AddComponent<SettingsWindow>();           // Gameplay/Sound/Graphics tabs, opened from the System window
        canvasGO.AddComponent<SystemWindow>();             // Esc (when nothing else answers it) → System menu ▸ Settings

        // The Inventory (B) and Character (C) windows. Builds itself into this canvas at runtime; needs the
        // starter Card assets.
        ProgressionWindowsBuilder.EnsureStarterCards();
        var prog = canvasGO.AddComponent<ProgressionWindows>();   // also pulls in InventoryStore (RequireComponent)
        canvasGO.AddComponent<CharacterWardrobePanel>();   // O = Wardrobe: per-part runtime clothing dye (live-3D)
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
        EnsureFolder("Assets/3_Data/Resources");
        PrefabUtility.SaveAsPrefabAssetAndConnect(canvasGO, ResourcePrefabPath, InteractionMode.AutomatedAction);
        // AND THE SCENE'S COPY IS MADE IDENTICAL TO THE PREFAB. The text built above points at font assets UiKit
        // makes in memory, which a prefab cannot hold (it saves them as nothing) but a scene CAN — as an override
        // on this instance, dragging the whole font, atlas and glyph table into the scene file. Measured
        // 2026-09-21: 96,000 lines added to Systems.unity by one save. Reverting the overrides leaves the
        // instance pointing at exactly what the prefab holds, and nothing else to serialize.
        // editor-write-ok: on the user's own Build UI Shell click, on the object that click just created
        PrefabUtility.RevertPrefabInstance(canvasGO, InteractionMode.AutomatedAction);

        EditorSceneManager.MarkSceneDirty(canvasGO.scene);
        Selection.activeGameObject = canvasGO;
        Debug.Log($"[unwritten] Built UI shell — theme '{ShortName(themeFolder)}', scale {scaler.referencePixelsPerUnit}. " +
                  $"Saved persistent prefab → {ResourcePrefabPath}. It now survives scene loads and auto-spawns " +
                  "in any scene. Press Play: B = Bag (Inventory + Equipment), C = Character, K = Skills, V = Sprint, Esc = close all.");
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
        holder.rectTransform.sizeDelta = new Vector2(5 * 54, 54);   // four bindable slots (keys 1–4) + the ult
        var h = holder.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = false;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        for (int i = 0; i < HotbarBindings.SlotCount; i++)
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
    // CharacterWardrobePanel populates the Content area at runtime.
    static GameObject BuildCustomizePanel(Transform canvas)
    {
        var content = NewPanel(canvas, "CustomizePanel",
            new Vector2(0.22f, 0.18f), new Vector2(0.78f, 0.84f), "Wardrobe");
        // Content is intentionally left empty here — CharacterWardrobePanel builds
        // the parts list + colour palette into it at runtime.
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

        // ✕ close button — a generous transparent hit area (full header height) pinned to the header's
        // right edge carries the raycast + WindowCloseButton; the visible red cell + X are non-raycast
        // children centered inside it. Decoupling the hit box from the small square keeps the clickable
        // region centered on the glyph with margin, so it can't drift off the icon.
        var close = NewColor("Close", header.transform, new Color(0, 0, 0, 0));
        close.raycastTarget = true;   // must catch the click (WindowCloseButton listens here)
        close.rectTransform.anchorMin = new Vector2(1, 0.5f);
        close.rectTransform.anchorMax = new Vector2(1, 0.5f);
        close.rectTransform.pivot = new Vector2(1, 0.5f);
        close.rectTransform.sizeDelta = new Vector2(HeaderH + 6, HeaderH);
        close.rectTransform.anchoredPosition = Vector2.zero;
        close.gameObject.AddComponent<WindowCloseButton>().window = bg.gameObject;

        var closeVis = NewColor("CloseVis", close.transform, new Color(0.62f, 0.14f, 0.14f, 0.95f));
        closeVis.raycastTarget = false;
        closeVis.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        closeVis.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        closeVis.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        closeVis.rectTransform.sizeDelta = new Vector2(HeaderH - 8, HeaderH - 8);
        closeVis.rectTransform.anchoredPosition = new Vector2(-3, 0);

        var x = NewText("X", closeVis.transform, "X", 16, FontStyle.Bold, TextAnchor.MiddleCenter, true);
        Fill(x.rectTransform, Vector2.zero, Vector2.zero);
        x.raycastTarget = false;   // the visual cell is non-raycast; the transparent hit area catches the click
    }

    // ---- primitives -----------------------------------------------------------

    static Image NewSliced(string name, Transform parent, Sprite sprite)
    {
        var img = NewColor(name, parent, Color.white);
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        return img;
    }

    static Image NewColor(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    // Delegates to the one central TMP factory. `shadow` is kept for call-site compatibility but is a no-op:
    // SDF text is crisp on its own and HUD elements sit on backing plates, so no drop shadow is needed.
    static TextMeshProUGUI NewText(string name, Transform parent, string content, int size, FontStyle style, TextAnchor anchor, bool shadow)
    {
        var t = UiKit.MakeText(name, parent, content, size, style, anchor);
        t.color = Color.white;
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
        foreach (var es in Object.FindObjectsByType<EventSystem>())
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
