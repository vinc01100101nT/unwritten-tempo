using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Flow A: builds a <b>clean, empty 2.5D stage</b> you fill by hand. Unlike
/// <see cref="Build25DSpike"/> (which auto-scatters props + mobs for a look-test), this drops only
/// the boot rig — 3D follow camera, sun, a flat reference ground, the billboard player, the
/// HP/HUD, and the Dota cursor — and nothing else. Then you drag in the Kenney prefabs baked by
/// <see cref="KenneyPrefabBaker"/> to author the map.
///
/// Output: <b>Assets/Scenes/Stage25D.unity</b>. Reuses Build25DSpike's public helpers so the
/// player/camera/combat rig stays identical to the spike. Safe to re-run (overwrites the scene).
/// </summary>
public static class Stage25DBuilder
{
    const string ScenePath = "Assets/Scenes/Stage25D.unity";

    [MenuItem("Tools/unwritten/New 2.5D Stage (hand-place)", priority = 4)]
    static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("2.5D Stage", "Exit Play mode first, then re-run.", "OK"); return; }

        int rendererIndex = Build25DSpike.EnsureUniversalRenderer();
        if (rendererIndex < 0) return;   // EnsureUniversalRenderer already explained why

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        Build25DSpike.BuildLight();
        BuildGround();
        var player = Build25DSpike.BuildPlayer(Vector3.zero);   // billboard player at origin
        Build25DSpike.BuildCamera(rendererIndex, player);       // 3D follow camera
        Build25DSpike.BuildCharacter();                         // player HP/ATK/XP hub
        Build25DSpike.BuildHUD();                               // HP / Lv / XP bar + EventSystem
        if (player != null) Build25DSpike.BuildCursor(player);  // pointer / sword / crosshair

        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");

        var alreadyOpen = SceneManager.GetSceneByPath(ScenePath);
        if (alreadyOpen.IsValid() && alreadyOpen.isLoaded && alreadyOpen != scene)
            EditorSceneManager.CloseScene(alreadyOpen, true);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.playModeStartScene = null;           // play the open stage, don't boot Systems
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("2.5D Stage — built",
            $"Built a clean stage: {ScenePath}\n\n" +
            "It has only the boot rig (camera, sun, ground, billboard player, HP/HUD, cursor) — " +
            "no props or mobs.\n\n" +
            "HAND-PLACE:\n" +
            "1. Project window ▸ Assets/Prefabs/KenneyPlatformer (run 'Build Kenney Platformer " +
            "Prefabs' first if it's empty).\n" +
            "2. Drag prefabs into the Scene view; snap with V (vertex snap) or hold Ctrl to grid-snap.\n" +
            "3. Lay grass blocks for terrain, then add trees/props on top.\n\n" +
            "Press Play to walk it. LEFT-click ground = move; scroll = zoom; Q/E or right-drag = orbit.",
            "OK");
        Debug.Log($"[unwritten] Clean 2.5D stage built at {ScenePath} (renderer index {rendererIndex}). Drag in Kenney prefabs to author.");
    }

    /// <summary>A flat grass reference floor so the player has something to stand on while you
    /// lay Kenney blocks on top. Replace/cover it with grass blocks as the map fills in.</summary>
    static void BuildGround()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "Ground";
        go.transform.localScale = new Vector3(4f, 1f, 4f);   // 40 x 40 units
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.36f, 0.55f, 0.27f) };
        m.SetFloat("_Smoothness", 0f);
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
    }
}
