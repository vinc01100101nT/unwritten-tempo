using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wires a baked roster character onto the REAL player as a visual swap (via <see cref="BakedPlayerVisual"/>),
/// so you can play one of the Quaternius characters in the actual game without touching
/// <see cref="Player3DController"/>. Fully reversible. Open the <b>Systems</b> scene (so the player exists in
/// the hierarchy), pick a character, press Play.
/// </summary>
public class UseBakedCharacter : EditorWindow
{
    const string RosterPath = "Assets/Art/Characters/CharacterRoster.asset";
    CharacterRoster roster;
    Vector2 scroll;

    [MenuItem("Tools/unwritten/Player/Use Baked Character (Systems scene)", priority = 605)]
    static void Open() => GetWindow<UseBakedCharacter>(true, "Use Baked Character");

    void OnEnable() => roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);

    void OnGUI()
    {
        var player = Object.FindFirstObjectByType<Player3DController>();
        EditorGUILayout.HelpBox(player != null
            ? "Player found. Pick a character to play AS it in the real game. Reversible."
            : "No player in the open scenes — open the Systems scene first (it owns the player).",
            player != null ? MessageType.Info : MessageType.Warning);

        if (GUILayout.Button("Revert to original player")) Revert(player);

        EditorGUILayout.Space();
        if (roster == null || roster.characters.Count == 0)
        {
            EditorGUILayout.HelpBox("No roster yet — bake characters first (Bake All Quaternius Characters).", MessageType.None);
            if (GUILayout.Button("Reload roster")) OnEnable();
            return;
        }

        using (new EditorGUI.DisabledScope(player == null))
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var e in roster.characters)
            {
                if (e == null || e.prefab == null) continue;
                EditorGUILayout.BeginHorizontal();
                if (e.previewStrip != null)
                {
                    var r = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
                    int cols = Mathf.Max(1, e.previewStrip.width / Mathf.Max(1, e.previewStrip.height));
                    GUI.DrawTextureWithTexCoords(r, e.previewStrip, new Rect(0f, 0f, 1f / cols, 1f), true);
                }
                if (GUILayout.Button("Play as " + e.name, GUILayout.Height(40))) Apply(player, e);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
    }

    static void Apply(Player3DController player, CharacterRoster.Entry e)
    {
        if (player == null || e == null || e.prefab == null) return;
        RemoveExisting();

        var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab);
        go.name = "PlayerBakedVisual (" + e.name + ")";
        SceneManager.MoveGameObjectToScene(go, player.gameObject.scene);
        go.transform.position = player.transform.position;

        var vis = go.GetComponent<BakedPlayerVisual>() ?? go.AddComponent<BakedPlayerVisual>();
        vis.player = player;

        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Selection.activeGameObject = go;
        EditorUtility.DisplayDialog("Use Baked Character",
            $"Now playing as '{e.name}'. Press Play — move with WASD/click; the baked sprite rides the player and " +
            "hides the original. Use 'Revert to original player' to undo.\n\n" +
            "Note: this is a visual swap (movement/combat are still the real player's).", "OK");
    }

    static void Revert(Player3DController player)
    {
        int n = RemoveExisting();
        // restore the player's own sprite in case OnDestroy didn't run (edit-mode removal)
        if (player != null)
        {
            var sr = player.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = true;
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        }
        EditorUtility.DisplayDialog("Use Baked Character", n > 0 ? "Reverted to the original player." : "Nothing to revert.", "OK");
    }

    static int RemoveExisting()
    {
        var existing = Object.FindObjectsByType<BakedPlayerVisual>(FindObjectsSortMode.None);
        foreach (var v in existing) if (v != null) Object.DestroyImmediate(v.gameObject);
        return existing.Length;
    }
}
