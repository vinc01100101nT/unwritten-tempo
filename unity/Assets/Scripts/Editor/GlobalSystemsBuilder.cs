using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click "promote the player + cursor to system level". After you've built a player
/// in the scene (Tools ▸ unwritten ▸ Build Player), run this to:
///
///   1. Wire the player's mouse combat (movement / attack / pathing) and make it a
///      <see cref="PersistentPlayer"/>, then save it as <b>Resources/Player.prefab</b>.
///   2. Build a <see cref="GameSystems"/> object that owns the global hardware
///      <see cref="GameCursor"/>, saved as <b>Resources/GameSystems.prefab</b>.
///
/// These two prefabs are the building blocks of the persistent layer. The final step —
/// <b>Tools ▸ unwritten ▸ Setup Systems Scene</b> — assembles them (plus a camera + character)
/// into Systems.unity, the scene that's loaded once and never unloaded, so portalling between
/// maps no longer drops the cursor or click-to-move. Each map then just needs a
/// <see cref="SpawnPoint"/> and a <see cref="Portal"/>; it does NOT need its own Player object.
///
/// Safe to re-run: it rebuilds both prefabs from the current scene's player.
/// </summary>
public static class GlobalSystemsBuilder
{
    const string PlayerPrefabPath  = "Assets/Resources/Player.prefab";
    const string SystemsPrefabPath = "Assets/Resources/GameSystems.prefab";

    [MenuItem("Tools/unwritten/Setup Global Systems", priority = 2)]
    static void SetupGlobalSystems()
    {
        EnsureFolder("Assets/Resources");

        string playerMsg = BuildPlayerPrefab();
        string systemsMsg = BuildSystemsPrefab();

        AssetDatabase.SaveAssets();
        Debug.Log("[unwritten] Setup Global Systems ✓ — " + playerMsg + " " + systemsMsg +
                  " These are the building blocks of the persistent layer. Next: Tools ▸ unwritten ▸ " +
                  "Setup Systems Scene to assemble them into Systems.unity (the never-unloaded scene).");
    }

    // ---- 1) persistent Player prefab -----------------------------------------

    static string BuildPlayerPrefab()
    {
        var pc = Object.FindFirstObjectByType<PlayerController2D>();
        if (pc == null)
            return "No Player in the scene, so Resources/Player.prefab was left as-is — build one first " +
                   "(Tools ▸ unwritten ▸ Build Player from selected SpriteSheet), then re-run this.";

        // Full mouse combat (PlayerAttacker + PathAgent + PlayerCommander + slash FX / attack pose).
        MonsterBuilder.EnsurePlayerCombat(pc);

        // The cursor is global now — strip any old per-player GameCursor so it can't fight the
        // one on GameSystems (left over from the previous Setup Mouse Combat flow).
        var stray = pc.GetComponent<GameCursor>();
        if (stray != null) Undo.DestroyObjectImmediate(stray);

        // Make the avatar persistent.
        if (pc.GetComponent<PersistentPlayer>() == null)
            Undo.AddComponent<PersistentPlayer>(pc.gameObject);

        // Save (and keep the scene instance connected to) the prefab.
        PrefabUtility.SaveAsPrefabAssetAndConnect(pc.gameObject, PlayerPrefabPath, InteractionMode.AutomatedAction);
        EditorSceneManager.MarkSceneDirty(pc.gameObject.scene);

        return $"Saved persistent player → {PlayerPrefabPath} (movement + combat wired, made persistent via PersistentPlayer).";
    }

    // ---- 2) persistent GameSystems prefab (global cursor) --------------------

    static string BuildSystemsPrefab()
    {
        // Replace any previous GameSystems in the scene so re-running is clean.
        var existing = Object.FindFirstObjectByType<GameSystems>();
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        var go = new GameObject("GameSystems");
        go.AddComponent<GameSystems>();
        MonsterBuilder.ConfigureCursor(go);   // adds + configures the Kenney hardware GameCursor

        Undo.RegisterCreatedObjectUndo(go, "Build GameSystems");
        PrefabUtility.SaveAsPrefabAssetAndConnect(go, SystemsPrefabPath, InteractionMode.AutomatedAction);
        EditorSceneManager.MarkSceneDirty(go.scene);

        return $"Saved global systems → {SystemsPrefabPath} (hardware cursor at system level).";
    }

    // ---- helpers --------------------------------------------------------------

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        var leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
