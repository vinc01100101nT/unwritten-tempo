using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the persistent <b>2.5D</b> <c>Systems.unity</c> — the scene loaded once and never unloaded —
/// holding the whole player rig so maps stay pure content:
///
///   Systems.unity = Sun + billboard Player (+ PersistentPlayer3D) + 3D follow Camera + Character +
///                   HUD + GameSystems (GameCursor3D) + GameRoot
///
/// Then it makes Systems Build index 0 and the editor Play-start scene, so pressing Play in any map
/// boots Systems first and loads that map underneath via <see cref="GameRoot"/> / <see cref="MapManager"/>;
/// the player arrives at the map's <see cref="SpawnPoint"/> (see <see cref="PersistentPlayer3D"/>). Maps
/// (built by the <see cref="RecipeGenerator"/>) only carry terrain + props + mobs + a SpawnPoint.
/// Safe to re-run (rebuilds Systems.unity from the 2.5D rig). Reuses <see cref="RigBuilder25D"/> helpers.
/// </summary>
public static class SystemsSceneBuilder
{
    const string ScenesDir = "Assets/Scenes";
    const string SystemsPath = ScenesDir + "/Systems.unity";
    const string FirstMap = "town_0";   // boot into the starting town

    [MenuItem("Tools/unwritten/Scene Setup/Setup Systems Scene", priority = 101)]
    static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Setup Systems Scene", "Exit Play mode first, then re-run.", "OK"); return; }

        int rendererIndex = RigBuilder25D.EnsureUniversalRenderer();
        if (rendererIndex < 0) return;   // EnsureUniversalRenderer already explained why
        WorldBuildUtil.EnsureGroundLayer();   // so the arrival snap has a Ground layer to ray against

        if (!AssetDatabase.IsValidFolder(ScenesDir)) AssetDatabase.CreateFolder("Assets", "Scenes");

        var prevActive = SceneManager.GetActiveScene();
        var existing = SceneManager.GetSceneByPath(SystemsPath);
        bool systemsWasOpen = existing.IsValid() && existing.isLoaded;
        bool activeWasSystems = systemsWasOpen && prevActive == existing;

        // Build alongside whatever's open (additive) so we don't disturb the user's scene.
        var sys = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(sys);

        // The persistent 2.5D rig.
        var sun = RigBuilder25D.BuildLight();
        sun.gameObject.AddComponent<DayNightLight>();   // sun color/intensity/angle + ambient by time-of-day
        sun.gameObject.AddComponent<CloudShadows>();    // drifting full-screen cloud shadows (DayNightLight also self-adds)
        sun.gameObject.AddComponent<SkyboxSky>();        // procedural wrap-around sky (gradient + sun + drifting clouds); DayNightLight also self-adds
        sun.gameObject.AddComponent<WindGlobals>();     // feeds _GlobalWind for foliage sway (DayNightLight also self-adds)
        sun.gameObject.AddComponent<RainOverlay>();     // screen-space rain overlay (intensity 0 = dry/zero-cost until raised)
        // Water FX get their OWN named object so the tunable knobs are findable (not buried in the Sun's pile).
        var waterFx = new GameObject("WaterFX");
        waterFx.transform.SetParent(sun.transform.parent, false);
        waterFx.AddComponent<WetGround>();              // HEAVY: wet/puddle screen-space pass + rain-impact rings (High tier, follows rain)
        waterFx.AddComponent<PlanarReflection>();       // HEAVY: mirror-camera reflection of the 3D world into puddles
        waterFx.AddComponent<WaterStep>();              // puddle FOOTSTEP SPLASH — droplet burst when a foot lands in standing water (Scale knob lives here)
        sun.gameObject.AddComponent<SnowOverlay>();     // screen-space drifting-flake overlay (intensity 0 = clear/zero-cost)
        sun.gameObject.AddComponent<SnowGround>();      // snow build-up on roofs/ground (all tiers) + footprints (High only)
        sun.gameObject.AddComponent<FogOverlay>();      // screen-space drifting fog haze (all tiers; fog 0 = clear/zero-cost)
        sun.gameObject.AddComponent<FootstepTracker>(); // single source of footstep positions, shared by wet + snow
        var rainAudio = sun.gameObject.AddComponent<RainAudio>();   // day/night storm loop, crossfaded + scaled by rain
        const string bgs = "Assets/Audio/TomMusic/WAV Files/BGS Loops";
        rainAudio.dayStorm   = AssetDatabase.LoadAssetAtPath<AudioClip>(bgs + "/Forest Day/Forest Day Storm.wav");
        rainAudio.nightStorm = AssetDatabase.LoadAssetAtPath<AudioClip>(bgs + "/Forest Night/Forest Night Storm.wav");
        var player = RigBuilder25D.BuildPlayer(Vector3.zero);
        if (player != null) player.AddComponent<PersistentPlayer3D>();
        RigBuilder25D.BuildCamera(rendererIndex, player);
        RigBuilder25D.BuildCharacter();

        // Full UI shell: HP bar + Inventory(B)/Character(C)/Skills(K) windows, bound to Character.Instance
        // (each panel's controller owns its own hotkey; CharacterUIBinder lives on the prefab). Falls back to the bare HUD.
        var uiCanvas = Resources.Load<GameObject>("GameUICanvas");
        if (uiCanvas != null) PrefabUtility.InstantiatePrefab(uiCanvas, sys);
        else
        {
            RigBuilder25D.BuildHUD();
            Debug.LogWarning("[unwritten] GameUICanvas prefab missing — using the bare HP bar. " +
                             "Run Tools ▸ unwritten ▸ Build UI Shell to get the I/C/K panels, then re-run this.");
        }
        if (Object.FindAnyObjectByType<EventSystem>() == null)   // panels + HUD-click need one
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var gameSystems = new GameObject("GameSystems");
        gameSystems.AddComponent<GameSystems>();        // global services host + frame-rate cap (60 FPS, VSync off)
        RigBuilder25D.BuildCursor(gameSystems);         // hardware GameCursor3D (pointer / sword / crosshair)
        gameSystems.AddComponent<DeathScreen>();        // Phase F: "YOU DIED" overlay + respawn (builds its own canvas)
        gameSystems.AddComponent<Sfx>();                // pooled one-shot SFX player (attack/hit/block/footsteps)
        gameSystems.AddComponent<MusicDirector>();      // persistent BGM: crossfades to each map's BgmTrack, survives map swaps
        gameSystems.AddComponent<TimeOfDay>();          // real-UTC-anchored day/night clock (2-hour cycle, 12×) + DayIndex + live tuner
        gameSystems.AddComponent<GameCalendar>();       // 4-season / 48-day calendar derived from TimeOfDay.DayIndex (+ OnNewDay/OnSeasonChange)
        gameSystems.AddComponent<WeatherChannels>();    // the 4 independent weather channels (rain/cloud/snow/puddle) every renderer follows
        gameSystems.AddComponent<WeatherDirector>();    // deterministic per-day weather: season-gated weighted pick → plays a composed WeatherSequence on the channels
        gameSystems.AddComponent<UnitStatusPanel>();    // engaged target (top-center): name, element, HP, MP, states, its gear
        gameSystems.AddComponent<HoverPlate>();        // whoever is under the cursor: name, HP, MP and buff icons, floating over them

        var root = new GameObject("GameRoot").AddComponent<GameRoot>();   // loads the first map under Systems
        root.firstMap = FirstMap;

        // Free the target path: close a previously-open Systems before saving onto it.
        if (systemsWasOpen) EditorSceneManager.CloseScene(existing, true);

        if (!EditorSceneManager.SaveScene(sys, SystemsPath))
        {
            Debug.LogError("[unwritten] Setup Systems Scene FAILED to save " + SystemsPath +
                           " — make sure you're NOT in Play mode and no other scene shares that path, then re-run.");
            return;
        }

        // Build Settings: Systems index 0 so a build boots the persistent layer first.
        var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(SystemsPath, true) };
        foreach (var s in EditorBuildSettings.scenes)
            if (s.path != SystemsPath) list.Add(s);
        EditorBuildSettings.scenes = list.ToArray();

        // In the editor, always enter Play from Systems (the open map is remembered + reloaded).
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SystemsPath);

        // Restore the user's view.
        if (systemsWasOpen)
        {
            if (!activeWasSystems && prevActive.IsValid()) SceneManager.SetActiveScene(prevActive);
        }
        else if (prevActive.IsValid())
        {
            SceneManager.SetActiveScene(prevActive);
            EditorSceneManager.CloseScene(sys, true);   // saved on disk; don't clutter the hierarchy
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[unwritten] Setup Systems Scene (2.5D) ✓ — " + SystemsPath +
                  " holds the live-3D player + 3D camera + HUD + cursor + Character + GameRoot, set as Build " +
                  "index 0 and the editor Play-start scene.");
        EditorUtility.DisplayDialog("Setup Systems Scene — done",
            "Built the persistent 2.5D Systems layer.\n\n" +
            "Press Play from a content map (Town / the field): Systems boots, the map loads under it, and the " +
            "player spawns at its SpawnPoint. Player changes now happen ONCE here — maps never rebuild the player.\n\n" +
            "If you haven't yet: run World ▸ New Field (Pxltgr grass) to (re)build the content-only field.", "OK");
    }

}
