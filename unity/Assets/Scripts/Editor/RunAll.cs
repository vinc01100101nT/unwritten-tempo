using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click setup, pinned to the TOP of Tools ▸ unwritten (priority 1) so it's
/// easy to find as the menu grows. It runs the safe, idempotent, no-selection
/// tools the workflow needs every time: refresh the starter items, then rebuild
/// the UI shell with the saved theme/scale.
///
/// It deliberately does NOT run the tools that need a selected asset or a target
/// in the scene (Build Player, Create Monster/NPC/Spawner, Create Portal/Spawn
/// Point, Build Map Boundary) or the destructive Clear Tilemaps — those need your
/// input, so run them from the menu when you need them.
/// </summary>
public static class RunAll
{
    [MenuItem("Tools/unwritten/Run All", priority = 1)]
    static void Run()
    {
        ItemStarterSet.EnsureStarterItems();
        UIShellBuilder.Build(
            EditorPrefs.GetString(UIShellBuilder.PrefTheme, UIShellBuilder.DefaultTheme),
            EditorPrefs.GetInt(UIShellBuilder.PrefScale, UIShellBuilder.DefaultScale));

        // If a player is in the scene, make sure mouse-target combat is wired up.
        var pc = Object.FindFirstObjectByType<PlayerController2D>();
        string combat = pc != null ? MonsterBuilder.EnsurePlayerCombat(pc) : " (No player in this scene — skipped combat setup.)";

        Debug.Log("[unwritten] Run All ✓ — refreshed starter items + rebuilt the UI shell." + combat +
                  " (Selection-only tools — Build Player, Create Monster/NPC/Spawner, Create Portal/" +
                  "Spawn Point, Build Map Boundary, Clear Tilemaps — are left for you to run manually.)");
    }
}
