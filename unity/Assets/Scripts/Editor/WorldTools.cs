using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor helpers for Phase C world-building: a one-click screen-space dialogue
/// box, and a one-click NPC built from a selected (sliced) character sheet.
/// </summary>
public static class WorldTools
{
    // The dialogue box and interact prompt are GLOBAL singletons — they live in the persistent Systems
    // layer, not per-map. These tools build them as Resources prefabs; Setup Systems Scene pulls them in.
    const string DialoguePrefabPath = "Assets/Resources/DialogueCanvas.prefab";
    const string PromptPrefabPath   = "Assets/Resources/InteractPromptCanvas.prefab";

    [MenuItem("Tools/unwritten/Build Dialogue UI")]
    static void BuildDialogueUI()
    {
        if (System.IO.File.Exists(DialoguePrefabPath) &&
            !EditorUtility.DisplayDialog("Build Dialogue UI",
                $"{DialoguePrefabPath} already exists.\n\nRebuild it? Systems pulls this prefab in, so this " +
                "replaces the global dialogue box.", "Rebuild", "Cancel"))
            return;

        var canvasGO = new GameObject("DialogueCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);

        // Bottom dialogue panel.
        var panel = new GameObject("Panel", typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        panel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.9f);
        var pr = panel.GetComponent<RectTransform>();
        pr.anchorMin = new Vector2(0.05f, 0.05f);
        pr.anchorMax = new Vector2(0.95f, 0.30f);
        pr.offsetMin = Vector2.zero;
        pr.offsetMax = Vector2.zero;

        var speaker = CreateText("Speaker", panel.transform, 24, FontStyle.Bold);
        var sr = speaker.GetComponent<RectTransform>();
        sr.anchorMin = new Vector2(0, 1);
        sr.anchorMax = new Vector2(1, 1);
        sr.pivot = new Vector2(0.5f, 1);
        sr.offsetMin = new Vector2(16, -36);
        sr.offsetMax = new Vector2(-16, -6);

        var body = CreateText("Body", panel.transform, 20, FontStyle.Normal);
        var br = body.GetComponent<RectTransform>();
        br.anchorMin = Vector2.zero;
        br.anchorMax = Vector2.one;
        br.offsetMin = new Vector2(16, 12);
        br.offsetMax = new Vector2(-16, -44);

        var ui = canvasGO.AddComponent<DialogueUI>();
        ui.panel = panel;
        ui.speakerText = speaker.GetComponent<Text>();
        ui.bodyText = body.GetComponent<Text>();
        panel.SetActive(false);

        SaveAsResourcesPrefab(canvasGO, DialoguePrefabPath);
        Debug.Log($"[unwritten] Built global DialogueCanvas → {DialoguePrefabPath}.  " +
                  "Run Setup Systems Scene to pull it into the persistent layer (it no longer lives per-map).");
    }

    [MenuItem("Tools/unwritten/Build Interact Prompt")]
    static void BuildInteractPrompt()
    {
        if (System.IO.File.Exists(PromptPrefabPath) &&
            !EditorUtility.DisplayDialog("Build Interact Prompt",
                $"{PromptPrefabPath} already exists.\n\nRebuild it? Systems pulls this prefab in, so this " +
                "replaces the global interact prompt.", "Rebuild", "Cancel"))
            return;

        var canvasGO = new GameObject("InteractPromptCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        // A black, semi-transparent badge that auto-sizes to whatever text it holds.
        var promptGO = new GameObject("Prompt",
            typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        promptGO.transform.SetParent(canvasGO.transform, false);
        promptGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);   // background: black, 50%

        var prt = promptGO.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);

        var hlg = promptGO.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(12, 12, 4, 4);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        var fitter = promptGO.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var labelGO = CreateText("Label", promptGO.transform, 20, FontStyle.Bold);
        var t = labelGO.GetComponent<Text>();
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;

        var ip = canvasGO.AddComponent<InteractPrompt>();
        ip.root = prt;       // move/show the badge; the text rides along as its child
        ip.label = t;
        promptGO.SetActive(false);

        SaveAsResourcesPrefab(canvasGO, PromptPrefabPath);
        Debug.Log($"[unwritten] Built global InteractPromptCanvas → {PromptPrefabPath}.  " +
                  "Run Setup Systems Scene to pull it into the persistent layer (it no longer lives per-map).");
    }

    /// <summary>Save a freshly-built canvas as a Resources prefab (the single source of truth that Setup
    /// Systems Scene instantiates into Systems.unity), then discard the scene instance so it never lingers
    /// in a map scene. Mirrors how Build UI Shell makes GameUICanvas.prefab.</summary>
    static void SaveAsResourcesPrefab(GameObject go, string path)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);   // the prefab is the artifact; don't leave a copy in the open scene
        AssetDatabase.SaveAssets();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    [MenuItem("Tools/unwritten/Create NPC from selected SpriteSheet")]
    static void CreateNpc()
    {
        Sprite sprite = null;
        string srcName = null;
        if (Selection.activeObject is Texture2D tex)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            sprite = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Sprite>().OrderBy(FrameIndex).FirstOrDefault();
            srcName = tex.name;
        }

        var go = new GameObject("NPC");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 0;                       // shared "units" band — depth is by Y (transparency axis)
        if (sprite != null) sr.sprite = sprite;

        var box = go.AddComponent<BoxCollider2D>();
        box.size = Vector2.one * 0.8f;

        var it = go.AddComponent<Interactable>();
        it.speaker = "Villager";
        it.lines = new[]
        {
            "Welcome to the field, traveler.",
            "The trees don't move — but out there, other things do.",
        };

        go.transform.position = new Vector3(2f, 0f, 0f);

        Undo.RegisterCreatedObjectUndo(go, "Create NPC");
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log(sprite != null
            ? $"[unwritten] Created NPC from '{srcName}'. Move it, and edit its dialogue on the Interactable."
            : "[unwritten] Created NPC (no sprite — select a sliced character sheet first, or assign one in its SpriteRenderer).");
    }

    [MenuItem("Tools/unwritten/Create Portal")]
    static void CreatePortal()
    {
        var go = new GameObject("Portal");
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = Vector2.one;                 // a one-tile doorway
        go.AddComponent<Portal>();
        go.transform.position = new Vector3(-6f, 0f, 0f);

        Undo.RegisterCreatedObjectUndo(go, "Create Portal");
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log("[unwritten] Created Portal. Set its 'Target Scene', drag it to a doorway/edge, " +
                  "and make sure both scenes are in File > Build Settings.");
    }

    [MenuItem("Tools/unwritten/Create Spawn Point")]
    static void CreateSpawnPoint()
    {
        var go = new GameObject("SpawnPoint");
        go.AddComponent<SpawnPoint>();
        go.transform.position = new Vector3(-4f, 0f, 0f);
        Undo.RegisterCreatedObjectUndo(go, "Create Spawn Point");
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log("[unwritten] Created SpawnPoint. Set its Id, match it from a Portal's Spawn Id, " +
                  "and place it just OUTSIDE the return portal's trigger.");
    }

    static GameObject CreateText(string name, Transform parent, int size, FontStyle style)
    {
        var go = new GameObject(name, typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");   // Unity 6 built-in font
        t.fontSize = size;
        t.fontStyle = style;
        t.color = Color.white;
        t.alignment = TextAnchor.UpperLeft;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.text = name;
        return go;
    }

    static int FrameIndex(Sprite s)
    {
        int u = s.name.LastIndexOf('_');
        return (u >= 0 && int.TryParse(s.name.Substring(u + 1), out int n)) ? n : 0;
    }
}
