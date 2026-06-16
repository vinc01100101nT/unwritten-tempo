using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click "build the persistent layer". Assembles <b>Assets/Scenes/Systems.unity</b> — the
/// scene that's loaded once and never unloaded — from the three Resources prefabs plus a
/// character, a camera and the <see cref="GameRoot"/> boot component:
///
///   Systems.unity = GameUICanvas (HUD) + GameSystems (cursor) + Player + Character +
///                   DialogueCanvas + InteractPromptCanvas + Main Camera (CameraFollow2D) + GameRoot
///
/// It then makes Systems Build index 0 (so builds boot it first) and the editor Play-start
/// scene (so pressing Play in any map boots the layer first, then loads that map underneath —
/// see <c>SystemsPlayMode</c>). After this, maps (Field/Town) only need tilemaps + SpawnPoints +
/// Portals; the HUD/cursor/player/camera live up in Systems and never reset on a portal.
///
/// Prereqs (run once): Build UI Shell (GameUICanvas), Setup Global Systems (Player + GameSystems),
/// Build Dialogue UI (DialogueCanvas) and Build Interact Prompt (InteractPromptCanvas). Safe to re-run —
/// it rebuilds Systems.unity from the current prefabs. After re-running, use "Remove Per-Map Dialogue +
/// Prompt" to strip the old baked copies out of Field/Town.
/// </summary>
public static class SystemsSceneBuilder
{
    const string ScenesDir = "Assets/Scenes";
    const string SystemsPath = ScenesDir + "/Systems.unity";

    [MenuItem("Tools/unwritten/Setup Systems Scene", priority = 3)]
    static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Setup Systems Scene", "Exit Play mode first, then re-run.", "OK");
            return;
        }

        var canvas   = Resources.Load<GameObject>("GameUICanvas");
        var systems  = Resources.Load<GameObject>("GameSystems");
        var player   = Resources.Load<GameObject>("Player");
        var dialogue = Resources.Load<GameObject>("DialogueCanvas");
        var prompt   = Resources.Load<GameObject>("InteractPromptCanvas");
        if (canvas == null || systems == null || player == null || dialogue == null || prompt == null)
        {
            EditorUtility.DisplayDialog("Setup Systems Scene",
                "Missing a Resources prefab. Run these first, then re-run:\n\n" +
                "• Tools ▸ unwritten ▸ Build UI Shell         (GameUICanvas)\n" +
                "• Tools ▸ unwritten ▸ Setup Global Systems    (Player + GameSystems)\n" +
                "• Tools ▸ unwritten ▸ Build Dialogue UI       (DialogueCanvas)\n" +
                "• Tools ▸ unwritten ▸ Build Interact Prompt   (InteractPromptCanvas)",
                "OK");
            return;
        }

        if (!AssetDatabase.IsValidFolder(ScenesDir)) AssetDatabase.CreateFolder("Assets", "Scenes");

        var prevActive = SceneManager.GetActiveScene();

        // Systems.unity may already be OPEN in the editor (it IS the user's working scene). Unity won't
        // SaveScene onto a path another open scene occupies ("Overwriting the same path as another open
        // scene is not allowed"), so remember the open copy and close it just before the save.
        var existing = SceneManager.GetSceneByPath(SystemsPath);
        bool systemsWasOpen = existing.IsValid() && existing.isLoaded;
        bool activeWasSystems = systemsWasOpen && prevActive == existing;

        // Create the Systems scene ALONGSIDE whatever is open (additive) so we never disturb the
        // user's current scene, and make it active so new objects land in it.
        var sys = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(sys);

        // The persistent layer.
        PrefabUtility.InstantiatePrefab(canvas,   sys);   // HUD (+ EventSystem child)
        PrefabUtility.InstantiatePrefab(systems,  sys);   // GameSystems + GameCursor
        PrefabUtility.InstantiatePrefab(player,   sys);   // the avatar
        PrefabUtility.InstantiatePrefab(dialogue, sys);   // global DialogueUI (was duplicated per-map)
        PrefabUtility.InstantiatePrefab(prompt,   sys);   // global InteractPrompt (was duplicated per-map)
        new GameObject("Character").AddComponent<Character>();

        // One persistent camera that follows the player and survives every map swap.
        var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 6f;                       // tweak in the scene if zoom looks off
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
        camGO.transform.position = new Vector3(0f, 0f, -10f);
        camGO.AddComponent<AudioListener>();
        camGO.AddComponent<CameraFollow2D>();

        new GameObject("GameRoot").AddComponent<GameRoot>();   // loads the first map under Systems

        // One global 2D light for the whole game (URP allows only one). Maps must NOT keep their
        // own — Systems is the single source of truth. Created here so rebuilds stay self-sufficient.
        LightingFix.CreateGlobalLight("Global Light 2D");

        // Free the target path: close the previously-open Systems (we've just rebuilt it from prefabs).
        if (systemsWasOpen) EditorSceneManager.CloseScene(existing, true);

        if (!EditorSceneManager.SaveScene(sys, SystemsPath))
        {
            Debug.LogError("[unwritten] Setup Systems Scene FAILED to save " + SystemsPath +
                           ". Make sure you're NOT in Play mode and that no other scene shares that path, then re-run.");
            return;
        }

        // Build Settings: Systems must be index 0 so a build boots the persistent layer first.
        var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(SystemsPath, true) };
        foreach (var s in EditorBuildSettings.scenes)
            if (s.path != SystemsPath) list.Add(s);
        EditorBuildSettings.scenes = list.ToArray();

        // In the editor, always enter Play from Systems (the open map is remembered + reloaded).
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SystemsPath);

        // Restore the user's view.
        if (systemsWasOpen)
        {
            // We replaced an already-open Systems in place — leave the freshly-built one LOADED so the
            // user sees the new DialogueCanvas/InteractPromptCanvas, and restore their active map (if any).
            if (!activeWasSystems && prevActive.IsValid())
                SceneManager.SetActiveScene(prevActive);
        }
        else if (prevActive.IsValid())
        {
            // Systems wasn't open before; don't leave it cluttering the hierarchy (it's saved on disk).
            SceneManager.SetActiveScene(prevActive);
            EditorSceneManager.CloseScene(sys, true);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[unwritten] Setup Systems Scene ✓ — built " + SystemsPath +
                  " (HUD + cursor + character + player + camera + dialogue + interact prompt), set as Build " +
                  "index 0 and the editor Play-start scene. Press Play from any map: Systems boots first, your " +
                  "map loads under it, portals swap only the map. Tweak the Systems 'Main Camera' Size if the " +
                  "zoom looks off. Now run Tools ▸ unwritten ▸ Remove Per-Map Dialogue + Prompt in each map to " +
                  "strip the old baked copies (and delete any leftover per-map Player/Camera/Canvas too).");
    }
}
