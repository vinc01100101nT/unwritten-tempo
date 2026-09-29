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
/// Builds the UI shell (the HUD + the Calendar window) in the game's one interface theme — every
/// colour, shape and font comes from <see cref="UiKit"/>, exactly as the windows built at runtime do.
/// </summary>
public static class UIShellBuilder
{
    const string CanvasName = "GameUICanvas";
    const string UIRoot = "Assets/1_Authored/Art/Billboards/Ui";
    const string METEO  = UIRoot + "/Skill Icon/Meteo";   // weather icons (Sun/Rain/Snow) for the calendar
    const string ResourcePrefabPath = "Assets/3_Data/Resources/GameUICanvas.prefab";

    [MenuItem("Tools/unwritten/UI/Build UI Shell (Systems scene)", priority = 110)]
    static void BuildUIShellMenu() => Build();

    /// <summary>Builds (or rebuilds) the UI shell.</summary>
    public static void Build()
    {
        var existing = GameObject.Find(CanvasName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var canvasGO = new GameObject(CanvasName,
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        // The theme's shape images are 100 pixels per unit; at this matching value one image pixel is one UI unit,
        // the same as on every other canvas, so a corner is the same size everywhere.
        scaler.referencePixelsPerUnit = 100;
        UiKit.ApplyTextCrispness(scaler);
        var root = canvasGO.transform;

        SetupEventSystem(root);
        BuildHud(root);
        // All panels' hotkeys/Esc are owned by their own runtime controllers (code-driven, no rebake to change a key):
        //   Skills (K)→SkillsWindow · Calendar (L)→CalendarPanel ·
        //   Inventory (B)/Character (C, the Ring of Progression included)→ProgressionWindows. PanelToggle is retired.
        BuildCalendarPanel(root);
        // The Inventory and Character windows are built by ProgressionWindows (added below), and stat-point
        // allocation lives in that Character window, so StatPanel + BagSeeder are not added here.
        // (The old UI-image sword cursor was retired — GameCursor now drives a hardware
        //  cursor from the Kenney pack, wired by Tools ▸ unwritten ▸ Setup Global Systems.)

        // (No PanelToggle: every window now owns its own hotkey + Esc in its runtime controller.)

        canvasGO.AddComponent<PersistentUI>();             // survives scene loads (singleton)
        canvasGO.AddComponent<SkillDraftPanel>();          // Phase G: builds the Trio-Ring draft into the Character (C) window
        canvasGO.AddComponent<SkillsWindow>();             // Skills (K): the skills you have learned, by weapon, on a mastery line
        canvasGO.AddComponent<CraftStationPanel>();        // the smith / loom / tannery window, opened by reaching one
        canvasGO.AddComponent<HotbarPanel>();              // populates HUD_Hotbar with the player's actives (icon + cooldown)
        canvasGO.AddComponent<BuffBarPanel>();             // HUD buff bar (right edge): active self-buffs w/ icon + countdown
        canvasGO.AddComponent<StatusBox>();                // top-left: name, HP, MP, levels + experience, zeny, window buttons
        canvasGO.AddComponent<ChatController>();            // in-game chat window (Enter) + /command console (GmCommands)
        canvasGO.AddComponent<SettingsWindow>();           // Gameplay/Sound/Graphics tabs, opened from the System window
        canvasGO.AddComponent<SystemWindow>();             // Esc (when nothing else answers it) → System menu ▸ Settings

        // The Inventory (B) and Character (C) windows. Builds itself into this canvas at runtime.
        var prog = canvasGO.AddComponent<ProgressionWindows>();   // also pulls in InventoryStore (RequireComponent)
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
        Debug.Log($"[unwritten] Built UI shell. " +
                  $"Saved persistent prefab → {ResourcePrefabPath}. It now survives scene loads and auto-spawns " +
                  "in any scene. Press Play: B = Bag (Inventory + Equipment), C = Character, K = Skills, V = Sprint, Esc = close all.");
    }

    // ---- HUD ------------------------------------------------------------------

    static void BuildHud(Transform canvas)
    {
        // The character's name, HP, MP, levels, experience and zeny are the status box's (StatusBox, top-left).

        // Minimap — top-right corner (the map's picture + player/mob blips). Sits above the gold counter.
        BuildMinimap(canvas);

        // Gold counter — top-right, tucked just under the minimap; GoldCounter.cs wires it to Wallet.OnChanged.
        var goldPanel = NewPlate("HUD_GoldPanel", canvas);
        Anchor(goldPanel.rectTransform, Vector2.one, Vector2.one, Vector2.one);
        goldPanel.rectTransform.anchoredPosition = new Vector2(-16, -(16 + MinimapSize + 8));
        goldPanel.rectTransform.sizeDelta = new Vector2(120, 26);
        var goldLabel = NewText("HUD_Gold", goldPanel.transform, "0 z", 15, FontStyle.Bold, TextAnchor.MiddleCenter);
        Fill(goldLabel.rectTransform, new Vector2(4, 2), new Vector2(-4, -2));
        goldLabel.gameObject.AddComponent<GoldCounter>();

        // Calendar + weather readout — continues the top-right stack (minimap ▸ gold ▸ date/weather).
        // CalendarWeatherHUD.cs wires the two text lines to GameCalendar.DateString + WeatherDirector.CurrentName.
        var calPanel = NewPlate("HUD_CalendarPanel", canvas);
        Anchor(calPanel.rectTransform, Vector2.one, Vector2.one, Vector2.one);
        calPanel.rectTransform.anchoredPosition = new Vector2(-16, -(16 + MinimapSize + 8 + 26 + 8));
        calPanel.rectTransform.sizeDelta = new Vector2(150, 42);
        calPanel.raycastTarget = false;   // purely informational — never eats clicks
        var calLabel = NewText("HUD_Calendar", calPanel.transform, "Spring · Day 1\nClear", 13, FontStyle.Bold, TextAnchor.MiddleCenter);
        Fill(calLabel.rectTransform, new Vector2(4, 2), new Vector2(-4, -2));
        calLabel.raycastTarget = false;
        calLabel.gameObject.AddComponent<CalendarWeatherHUD>();

        var holder = NewColor("HUD_Hotbar", canvas, new Color(0, 0, 0, 0f));
        Anchor(holder.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
        holder.rectTransform.anchoredPosition = new Vector2(0, 42);
        holder.rectTransform.sizeDelta = new Vector2(5 * 54, 54);   // four bindable slots (keys 1–4) + the ult
        var h = holder.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = false;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        for (int i = 0; i < HotbarBindings.SlotCount; i++)
            UiKit.Round(NewColor("Hotslot", holder.transform, UiKit.ButtonEdge), UiKit.ButtonRadius).rectTransform.sizeDelta = new Vector2(48, 48);
    }

    const int MinimapSize = 180;   // square minimap edge (px @ reference resolution)

    // Top-right minimap: a framed plate holding a RawImage (the current map's picture) + a clipped blip layer.
    // The Minimap component loads that picture at runtime and projects player/mob dots onto blipRoot.
    static void BuildMinimap(Transform canvas)
    {
        var panel = NewPlate("HUD_Minimap", canvas);
        Anchor(panel.rectTransform, Vector2.one, Vector2.one, Vector2.one);   // top-right corner
        panel.rectTransform.anchoredPosition = new Vector2(-16, -16);
        panel.rectTransform.sizeDelta = new Vector2(MinimapSize, MinimapSize);
        panel.raycastTarget = false;

        // The map picture, inset so the frame shows around it.
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

    // Empty themed window — CalendarPanel.cs builds the header + 4×3 day-cell grid into Content at runtime
    // (each cell shows that day's deterministic weather). Toggled by L / clicking the top-right date widget.
    static GameObject BuildCalendarPanel(Transform canvas)
    {
        return NewPanel(canvas, "CalendarPanel", new Vector2(0.28f, 0.18f), new Vector2(0.72f, 0.82f), "Calendar");
    }

    // A window stretched between two anchor points, dressed exactly like every window built at runtime. Returns the
    // window itself (hidden); its content area is "Fill/Content".
    static GameObject NewPanel(Transform canvas, string name, Vector2 aMin, Vector2 aMax, string title)
    {
        var bg = NewColor(name, canvas, UiKit.WindowEdge);
        bg.rectTransform.anchorMin = aMin;
        bg.rectTransform.anchorMax = aMax;
        bg.rectTransform.offsetMin = Vector2.zero;
        bg.rectTransform.offsetMax = Vector2.zero;

        UiKit.DressWindow(bg, title);
        bg.gameObject.SetActive(false);
        return bg.gameObject;
    }

    // ---- primitives -----------------------------------------------------------

    // A HUD plate: the window edge around the window body, rounded. Children added later sit on the body.
    static Image NewPlate(string name, Transform parent)
        => UiKit.Framed(NewColor(name, parent, UiKit.WindowEdge), UiKit.WindowEdge, UiKit.PanelFill, UiKit.WindowRadius);

    static Image NewColor(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    // Delegates to the one central TMP factory, in the theme's primary text colour.
    static TextMeshProUGUI NewText(string name, Transform parent, string content, int size, FontStyle style, TextAnchor anchor)
        => UiKit.MakeText(name, parent, content, size, style, anchor);

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
}
