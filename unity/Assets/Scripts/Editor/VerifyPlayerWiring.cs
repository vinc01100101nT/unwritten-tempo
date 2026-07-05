using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click sanity check for the baked player rig. The player is GENERATED into the saved
/// <c>Systems.unity</c> by <see cref="RigBuilder25D.BuildPlayer"/> — sheets, icons, stats and SFX are all
/// frozen into the scene at author-time. If that scene drifts from the code (re-baked before some wiring
/// existed, or before an asset was importable) the references go EMPTY with no error — just missing sound /
/// invisible FX. This tool surfaces that class of rot loudly:
///   • every empty, non-optional <see cref="SfxSet"/> (shares <see cref="SfxWiring"/> with the runtime guard), and
///   • every null <c>*Sheet</c> texture (a skill with no sprite sheet renders nothing).
/// It reads the scene only (loads Systems additively if needed and closes it WITHOUT saving), so it never
/// mutates anything. The fix it points to is re-running Setup Systems Scene.
/// </summary>
public static class VerifyPlayerWiring
{
    const string SystemsPath = "Assets/Scenes/Systems.unity";

    [MenuItem("Tools/unwritten/Scene Setup/Verify Player Wiring", priority = 102)]
    static void Verify()
    {
        // Prefer a player already loaded in an open scene (what Play would actually use); otherwise peek into
        // the saved Systems scene without disturbing the user's open scenes.
        var player = UnityEngine.Object.FindFirstObjectByType<Player3DController>();
        bool openedTemp = false;
        Scene temp = default;

        if (player == null)
        {
            if (!System.IO.File.Exists(SystemsPath))
            {
                EditorUtility.DisplayDialog("Verify Player Wiring",
                    "No player is loaded and " + SystemsPath + " doesn't exist yet.\n\n" +
                    "Run Tools ▸ unwritten ▸ Scene Setup ▸ Setup Systems Scene first.", "OK");
                return;
            }
            temp = EditorSceneManager.OpenScene(SystemsPath, OpenSceneMode.Additive);
            openedTemp = true;
            player = UnityEngine.Object.FindFirstObjectByType<Player3DController>();
        }

        try
        {
            if (player == null)
            {
                EditorUtility.DisplayDialog("Verify Player Wiring",
                    "Couldn't find a Player3DController in the Systems scene. Re-run Setup Systems Scene.", "OK");
                return;
            }

            var audioIssues = new List<string>();
            SfxWiring.ScanInto(player.gameObject, audioIssues);

            if (audioIssues.Count == 0)
            {
                Debug.Log("[unwritten] Verify Player Wiring: OK — every skill SFX on the player is assigned.");
                EditorUtility.DisplayDialog("Verify Player Wiring",
                    "All good ✓\n\nEvery skill SFX on the player is assigned.", "OK");
                return;
            }

            string body = "Empty SFX (" + audioIssues.Count + "):\n  • " + string.Join("\n  • ", audioIssues) +
                "\n\nFix: re-run Tools ▸ unwritten ▸ Scene Setup ▸ Setup Systems Scene, then save.";
            Debug.LogWarning("[unwritten] Verify Player Wiring found problems:\n" + body);
            EditorUtility.DisplayDialog("Verify Player Wiring — problems found", body, "OK");
        }
        finally
        {
            // Never leave a side-effect: close the scene we opened, discarding (we made no changes anyway).
            if (openedTemp && temp.IsValid()) EditorSceneManager.CloseScene(temp, true);
        }
    }
}
