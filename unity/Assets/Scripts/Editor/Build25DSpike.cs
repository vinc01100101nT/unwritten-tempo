using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-click 2.5D spike (RO-style look test). Builds <b>Assets/Scenes/Spike2D5.unity</b>: your
/// Ninja Adventure player as a camera-facing <see cref="Billboard"/> walking a 3D Kenney town,
/// viewed from a tilted camera — so you can judge the 2.5D style before committing to it.
///
/// It adds a 3D (Universal) Renderer ALONGSIDE the existing 2D one and overrides it only on the
/// spike camera, so NewTown / Systems (2D renderer, index 0) are untouched. Safe to re-run.
///
/// After building: open Spike2D5.unity and press Play (WASD to move). Throwaway / branch-only.
/// </summary>
public static class Build25DSpike
{
    const string ScenePath      = "Assets/Scenes/Spike2D5.unity";
    const string UrpPath        = "Assets/Settings/UniversalRP.asset";
    const string Renderer3DPath = "Assets/Settings/Spike3DRenderer.asset";
    const string KitFbxDir      = "Assets/Art/KenneyFantasyTownKit/FBX format";
    const string ColormapPath   = KitFbxDir + "/Textures/colormap.png";
    const int CellSize = 16, WalkFrames = 4;
    // Transparent padding below the feet inside each (center-pivoted) sprite cell, in world units.
    // We drop the billboard by this and raise the CharacterController center to match, so the art's
    // feet meet the ground instead of floating. Tune if sprites sit slightly high/low.
    const float SpriteFoot = 0.22f;

    // Curated props (each guarded — skipped if not present in the kit).
    static readonly string[] Props = {
        "tree", "tree-high", "tree-crooked", "fountain-round", "cart", "cart-high",
        "lantern", "hedge-large", "pillar-stone", "banner-red", "pillar-wood", "hedge",
    };

    [MenuItem("Tools/unwritten/Build 2.5D Spike", priority = 2)]
    static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("2.5D Spike", "Exit Play mode first, then re-run.", "OK"); return; }

        int rendererIndex = EnsureUniversalRenderer();
        if (rendererIndex < 0) return;   // EnsureUniversalRenderer already explained why

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        BuildLight();
        BuildGround();
        int placed = ScatterProps();
        var player = BuildPlayer();              // returns the player GameObject (or null)
        BuildCamera(rendererIndex, player);      // 3D follow-camera tracks the player
        int monsters = BuildMonsters();          // billboard mobs that wander + chase (Phase 3D-D)
        BuildCharacter();                        // player HP/ATK/XP hub — turns on two-way combat (A)
        BuildHUD();                              // screen-space HP / Lv / XP bar (A)
        if (player != null) BuildCursor(player); // Dota hardware cursor (pointer / sword / crosshair)

        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");

        // If Spike2D5 is already open, close it first — Unity won't SaveScene onto a path another
        // open scene occupies ("Overwriting the same path as another open scene is not allowed").
        var alreadyOpen = SceneManager.GetSceneByPath(ScenePath);
        if (alreadyOpen.IsValid() && alreadyOpen.isLoaded && alreadyOpen != scene)
            EditorSceneManager.CloseScene(alreadyOpen, true);

        EditorSceneManager.SaveScene(scene, ScenePath);

        // The spike is standalone — Play must use the OPEN scene, not boot Systems.
        EditorSceneManager.playModeStartScene = null;

        // Re-open the freshly-built scene by itself so you can just press Play — no delete dance.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("2.5D Spike — built",
            $"Built {ScenePath}\n\n" +
            $"• {placed} Kenney props placed\n" +
            $"• Player billboard: {(player != null ? "yes" : "MISSING — see Console")}\n" +
            $"• Monsters: {monsters} (wander/chase; now bite back)\n" +
            "• Player HP + HUD bar (two-way combat)\n\n" +
            "NEXT: Spike2D5 is now open — just press Play.\n" +
            "Controls: LEFT-click ground = move, LEFT-click a mob = attack;  A then LEFT-click = attack-move;  S = stop.\n" +
            "Camera: scroll = zoom;  Q/E or RIGHT-drag = orbit (X/Y).\n" +
            "The hardware cursor changes: pointer / sword (over a mob) / crosshair (after A).\n\n" +
            "NOTE: I set Play-Mode Start Scene to None so the spike plays standalone. To go back " +
            "to the real game, open NewTown and re-run Tools ▸ unwritten ▸ Setup Systems Scene.",
            "OK");
        Debug.Log($"[unwritten] 2.5D Spike built ({placed} props, renderer index {rendererIndex}). Spike2D5 is open — press Play.");
    }

    /// <summary>Create a 3D Universal Renderer (once) and add it to the URP asset's renderer
    /// list; returns its index, or -1 (with a dialog) if URP's internals don't match.</summary>
    static int EnsureUniversalRenderer()
    {
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPath)
                  ?? GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null) return Fail("Couldn't find the URP pipeline asset (expected " + UrpPath + ").");

        var so = new SerializedObject(urp);
        var list = so.FindProperty("m_RendererDataList");
        if (list == null)
            return Fail("URP asset has no 'm_RendererDataList' (version mismatch). Manually add a " +
                        "Universal Renderer to " + UrpPath + " then re-run.");

        var existing = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(Renderer3DPath);
        if (existing != null)
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == existing) return i;

        ScriptableRendererData data = existing;
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<UniversalRendererData>();
            if (data == null)
                return Fail("Couldn't create a UniversalRendererData. Manually: Assets/Settings ▸ " +
                            "Create ▸ Rendering ▸ URP Universal Renderer, name it 'Spike3DRenderer', then re-run.");
            AssetDatabase.CreateAsset(data, Renderer3DPath);
        }

        int idx = list.arraySize;
        list.arraySize = idx + 1;
        list.GetArrayElementAtIndex(idx).objectReferenceValue = data;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(urp);
        AssetDatabase.SaveAssets();
        Debug.Log($"[unwritten] Added 3D Universal Renderer to {UrpPath} at index {idx} (2D renderer stays index 0).");
        return idx;
    }

    static void BuildCamera(int rendererIndex, GameObject player)
    {
        var go = new GameObject("Spike Camera") { tag = "MainCamera" };
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.70f, 0.85f);
        cam.fieldOfView = 35f;
        cam.farClipPlane = 200f;
        go.transform.SetPositionAndRotation(new Vector3(0f, 10f, -8f), Quaternion.Euler(52f, 0f, 0f));
        go.AddComponent<AudioListener>();
        cam.GetUniversalAdditionalCameraData().SetRenderer(rendererIndex);   // 3D renderer, this camera only

        var follow = go.AddComponent<CameraFollow3D>();    // orbit (Q/E + right-drag) + zoom follow rig
        follow.distance = 9f;
        follow.height = 10f;
        follow.yaw = 0f;
        if (player != null) follow.target = player.transform;
    }

    static void BuildLight()
    {
        var go = new GameObject("Sun");
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = new Color(1f, 0.96f, 0.90f);
        l.intensity = 1.1f;
        l.shadows = LightShadows.Soft;
        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    static void BuildGround()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "Ground";
        go.transform.localScale = new Vector3(4f, 1f, 4f);   // 40 x 40 units
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.36f, 0.55f, 0.27f) };
        m.SetFloat("_Smoothness", 0f);
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    static Material _kitMat;
    static Material KitMaterial()
    {
        if (_kitMat != null) return _kitMat;
        _kitMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ColormapPath);
        if (tex != null) { _kitMat.SetTexture("_BaseMap", tex); _kitMat.mainTexture = tex; }
        else Debug.LogWarning("[unwritten] colormap not found at " + ColormapPath + " — props will be untextured.");
        _kitMat.SetFloat("_Smoothness", 0f);
        return _kitMat;
    }

    static Material _billboardMat;
    // Flat-shaded, shadow-RECEIVING material for the sprite billboards (Unwritten/BillboardLit).
    // One shared instance — the sprite texture is fed per-renderer by the SpriteRenderer.
    static Material BillboardMaterial()
    {
        if (_billboardMat != null) return _billboardMat;
        var shader = Shader.Find("Unwritten/BillboardLit");
        if (shader == null)
        {
            Debug.LogWarning("[unwritten] Shader 'Unwritten/BillboardLit' not found — sprites won't " +
                             "receive shadows. Make sure Assets/Shaders/BillboardLit.shader imported.");
            return null;
        }
        _billboardMat = new Material(shader) { name = "BillboardLit (spike)" };
        return _billboardMat;
    }

    // Make a sprite billboard light up like the rest of the 3D world: flat art that darkens in
    // shadow (receive) and throws a clean cutout shadow (cast). No-op material if the shader is missing.
    static void ApplyBillboardLighting(SpriteRenderer sr)
    {
        var mat = BillboardMaterial();
        if (mat != null) sr.sharedMaterial = mat;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;  // thin quad: cast from both faces
        sr.receiveShadows = true;
    }

    static int ScatterProps()
    {
        var mat = KitMaterial();
        int placed = 0;
        float step = 360f / Props.Length;
        for (int i = 0; i < Props.Length; i++)
        {
            string path = FindFbx(Props[i]);
            if (path == null) continue;
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (fbx == null) continue;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            float a = (i * step) * Mathf.Deg2Rad;
            float r = 5f + (i % 3);
            inst.transform.position = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            inst.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = mr.sharedMaterials;
                for (int k = 0; k < mats.Length; k++) mats[k] = mat;
                mr.sharedMaterials = mats;
            }
            // Solid collision so the player can't walk through props (Phase 3D-B). Hollow props
            // (fountain/well) get a CONVEX hull so you can't stand inside the ring — keeps the
            // walkable footprint consistent between physical collision and the A* (3D-D fix).
            bool hollow = inst.name.ToLowerInvariant().Contains("fountain") || inst.name.ToLowerInvariant().Contains("well");
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    if (hollow) mc.convex = true;
                }
            placed++;
        }
        return placed;
    }

    static string FindFbx(string baseName)
    {
        string exact = $"{KitFbxDir}/{baseName}.fbx";
        if (File.Exists(exact)) return exact;
        foreach (var g in AssetDatabase.FindAssets(baseName + " t:GameObject", new[] { KitFbxDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (p.ToLowerInvariant().EndsWith(".fbx")) return p;
        }
        return null;
    }

    static void BuildCursor(GameObject host)
    {
        const string dir = "Assets/Art/KenneyCursorPack/PNG/Outline/Default";
        foreach (var n in new[] { "pointer_b", "tool_sword_a", "target_b" })
            AssetDatabase.ImportAsset($"{dir}/{n}.png", ImportAssetOptions.ForceUpdate);   // ensure readable Cursor textures

        var cur = host.AddComponent<GameCursor3D>();
        cur.defaultCursor    = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/pointer_b.png");      // normal
        cur.attackCursor     = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/tool_sword_a.png");   // hovering a mob
        cur.attackMoveCursor = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/target_b.png");       // A-armed
        cur.defaultHotspot   = new Vector2(3, 3);
        if (cur.defaultCursor == null)
            Debug.LogWarning("[unwritten] Kenney cursors not found under " + dir + " — using the OS cursor.");
    }

    static void BuildCharacter()
    {
        // Always give the spike its OWN Character. The scene is rebuilt fresh each run and plays
        // standalone, so there's no duplicate — and a FindObjectByType guard wrongly skipped this
        // when another open scene (e.g. Systems) already had a Character, leaving Instance null.
        new GameObject("Character").AddComponent<Character>();       // sets Character.Instance on Play
    }

    static void BuildHUD()
    {
        var canvasGO = new GameObject("HUD Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        // The GraphicRaycaster only reports "pointer over HUD" if an EventSystem drives it. The real
        // game gets one from the Systems scene; the standalone spike needs its own, or clicking the
        // HP bar would fall through to the world and move the player (Player3DController.PointerOverUI).
        if (Object.FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        // Bar root, top-left.
        var bar = new GameObject("HealthBar", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(canvasGO.transform, false);
        var barRT = (RectTransform)bar.transform;
        barRT.anchorMin = barRT.anchorMax = barRT.pivot = new Vector2(0f, 1f);
        barRT.anchoredPosition = new Vector2(20f, -20f);
        barRT.sizeDelta = new Vector2(340f, 30f);
        var bg = bar.GetComponent<Image>();
        bg.sprite = uiSprite; bg.type = Image.Type.Sliced; bg.color = new Color(0f, 0f, 0f, 0.6f);

        // Fill.
        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(bar.transform, false);
        var fillRT = (RectTransform)fillGO.transform;
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(3f, 3f); fillRT.offsetMax = new Vector2(-3f, -3f);
        var fill = fillGO.GetComponent<Image>();
        fill.sprite = uiSprite; fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
        fill.color = new Color(0.85f, 0.25f, 0.25f); fill.fillAmount = 1f;

        // Label.
        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(bar.transform, false);
        var lRT = (RectTransform)labelGO.transform;
        lRT.anchorMin = Vector2.zero; lRT.anchorMax = Vector2.one; lRT.offsetMin = lRT.offsetMax = Vector2.zero;
        var label = labelGO.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.alignment = TextAnchor.MiddleCenter; label.color = Color.white;
        label.fontSize = 16; label.fontStyle = FontStyle.Bold;

        var hud = bar.AddComponent<HealthBarHUD>();
        hud.fill = fill; hud.label = label;
    }

    static int BuildMonsters()
    {
        string sheetPath = FindMonsterSheet();
        if (sheetPath == null) { Debug.LogWarning("[unwritten] Spike: no Ninja Adventure monster sheet found."); return 0; }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
        if (tex == null) return 0;

        const int count = 3;
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Monster_" + tex.name);
            float a = (i * (360f / count) + 25f) * Mathf.Deg2Rad;
            go.transform.position = new Vector3(Mathf.Cos(a) * 8f, 0.5f - SpriteFoot, Mathf.Sin(a) * 8f);   // start outside aggro

            ApplyBillboardLighting(go.AddComponent<SpriteRenderer>());   // flat art + receive/cast shadows
            go.AddComponent<Billboard>();

            var agent = go.AddComponent<PathAgent3D>();         // auto-adds CharacterController
            agent.moveSpeed = 2.5f; agent.agentRadius = 0.3f;
            var cc = go.GetComponent<CharacterController>();
            cc.radius = 0.3f; cc.height = 1f; cc.center = new Vector3(0f, SpriteFoot, 0f); cc.stepOffset = 0.05f; cc.slopeLimit = 10f;

            go.AddComponent<Health>().maxHealth = 12;
            go.AddComponent<MonsterAI3D>();
            go.AddComponent<BillboardAnimator3D>().sheet = tex;
            go.AddComponent<CameraDepthSorter>();        // camera-relative depth sorting (occlude / be occluded)
        }
        return count;
    }

    static string FindMonsterSheet()
    {
        const string dir = "Assets/Art/NinjaAdventure/Actor/Monster";
        foreach (var n in new[] { "Slime", "KappaGreen", "Cyclope", "BlueBat", "Flam" })
        {
            string p = $"{dir}/{n}/SpriteSheet.png";
            if (File.Exists(p)) return p;
        }
        foreach (var g in AssetDatabase.FindAssets("SpriteSheet t:Texture2D", new[] { dir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (p.EndsWith("SpriteSheet.png")) return p;
        }
        return null;
    }

    static GameObject BuildPlayer()
    {
        string sheetPath = PlayerSheetPath();
        if (sheetPath == null) { Debug.LogWarning("[unwritten] Spike: no player sprite sheet found."); return null; }

        var frames = AssetDatabase.LoadAllAssetsAtPath(sheetPath).OfType<Sprite>().OrderBy(FrameIndex).ToArray();
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
        if (frames.Length == 0 || tex == null)
        { Debug.LogWarning("[unwritten] Spike: player sheet not sliced (Sprite Mode = Multiple?): " + sheetPath); return null; }

        int cols = Mathf.Max(1, tex.width / CellSize);
        int walk = Mathf.Min(WalkFrames, frames.Length / cols);
        if (cols < 4 || walk < 1)
        { Debug.LogWarning("[unwritten] Spike: player sheet isn't a 4-column grid: " + sheetPath); return null; }

        var go = new GameObject("Player3D");
        go.transform.position = new Vector3(0f, 0.5f - SpriteFoot, 0f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        ApplyBillboardLighting(sr);                  // flat art + receive/cast environment shadows
        go.AddComponent<Billboard>();
        var ctrl = go.AddComponent<Player3DController>();
        go.AddComponent<Player3DAttacker>();
        go.AddComponent<CameraDepthSorter>();        // camera-relative depth sorting (occlude / be occluded)

        var cc = go.GetComponent<CharacterController>();     // auto-added; size to the sprite (Phase 3D-B)
        // stepOffset near 0 so the player can't climb/mount low props (e.g. the well rim) — keeps
        // physical collision consistent with what A* treats as solid (Phase 3D-C consistency fix).
        cc.radius = 0.3f; cc.height = 1f; cc.center = new Vector3(0f, SpriteFoot, 0f); cc.stepOffset = 0.05f; cc.slopeLimit = 10f;

        Sprite[] Col(int c) => Enumerable.Range(0, walk).Select(r => frames[c + r * cols]).ToArray();
        ctrl.down = Col(0); ctrl.up = Col(1); ctrl.left = Col(2); ctrl.right = Col(3);
        return go;
    }

    static string PlayerSheetPath()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player.prefab");
        var sr = prefab != null ? prefab.GetComponentInChildren<SpriteRenderer>() : null;
        if (sr != null && sr.sprite != null)
        {
            string p = AssetDatabase.GetAssetPath(sr.sprite.texture);
            if (!string.IsNullOrEmpty(p)) return p;
        }
        const string fallback = "Assets/Art/NinjaAdventure/Actor/Character/Boy/SpriteSheet.png";
        return File.Exists(fallback) ? fallback : null;
    }

    static int FrameIndex(Sprite s)
    {
        int u = s.name.LastIndexOf('_');
        return (u >= 0 && int.TryParse(s.name.Substring(u + 1), out int n)) ? n : 0;
    }

    static int Fail(string msg)
    {
        EditorUtility.DisplayDialog("2.5D Spike", msg, "OK");
        Debug.LogError("[unwritten] 2.5D Spike: " + msg);
        return -1;
    }
}
