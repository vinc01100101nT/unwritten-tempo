using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

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

        var prevActive = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        BuildLight();
        BuildGround();
        int placed = ScatterProps();
        var player = BuildPlayer();              // returns the player GameObject (or null)
        BuildCamera(rendererIndex, player);      // 3D follow-camera tracks the player

        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);

        if (prevActive.IsValid()) SceneManager.SetActiveScene(prevActive);
        EditorSceneManager.CloseScene(scene, true);

        // The spike is standalone — Play must use the OPEN scene, not boot Systems.
        EditorSceneManager.playModeStartScene = null;

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("2.5D Spike — built",
            $"Built {ScenePath}\n\n" +
            $"• {placed} Kenney props placed\n" +
            $"• Player billboard: {(player != null ? "yes" : "MISSING — see Console")}\n\n" +
            "NEXT: open Assets/Scenes/Spike2D5.unity and press Play. Click the ground to walk there, or WASD.\n\n" +
            "NOTE: I set Play-Mode Start Scene to None so the spike plays standalone. To go back " +
            "to the real game, open NewTown and re-run Tools ▸ unwritten ▸ Setup Systems Scene.",
            "OK");
        Debug.Log($"[unwritten] 2.5D Spike built ({placed} props, renderer index {rendererIndex}). Open {ScenePath} and Play (WASD).");
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

        var follow = go.AddComponent<CameraFollow3D>();    // smoothly track the player
        follow.offset = new Vector3(0f, 10f, -8f);
        follow.pitch = 52f;
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
        go.transform.position = new Vector3(0f, 0.5f, 0f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        go.AddComponent<Billboard>();
        var ctrl = go.AddComponent<Player3DController>();   // click-to-move + WASD + 4-dir anim

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
