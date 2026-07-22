using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A folder-scoped sprite picker for the Skill Composer's icon field — a thumbnail grid limited to the curated
/// Skill Icon folder, instead of Unity's global object picker that lists every sprite in the project. Opens as a
/// popup under the "Skill Icons" button; clicking a thumbnail assigns it and closes.
/// </summary>
public class SkillIconPicker : PopupWindowContent
{
    /// <summary>The one curated icon folder — the single source of the picker's contents. Change here if it moves.</summary>
    public const string Folder = "Assets/1_Authored/Art/Billboards/Ui/Skill Icon";

    readonly System.Action<Sprite> _onPick;
    static List<Sprite> _cache;
    string _search = "";
    Vector2 _scroll;

    public SkillIconPicker(System.Action<Sprite> onPick) { _onPick = onPick; }

    /// <summary>Every Sprite under <see cref="Folder"/> (recursively), including the sliced sub-sprites of a
    /// spritesheet. Cached across opens; pass refresh to rescan after re-slicing/importing.</summary>
    public static List<Sprite> Icons(bool refresh = false)
    {
        if (_cache != null && !refresh) return _cache;
        _cache = new List<Sprite>();
        if (AssetDatabase.IsValidFolder(Folder))
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // A single-sprite texture is the MAIN asset; a sliced sheet exposes its frames as sub-representations.
                var main = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (main != null) _cache.Add(main);
                foreach (var rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                    if (rep is Sprite s && s != main) _cache.Add(s);
            }
        }
        return _cache;
    }

    public override Vector2 GetWindowSize() => new Vector2(380, 420);

    public override void OnGUI(Rect rect)
    {
        var icons = Icons();

        EditorGUILayout.LabelField($"Skill Icons  ({icons.Count})", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            _search = EditorGUILayout.TextField(_search);
            if (GUILayout.Button("Rescan", EditorStyles.miniButton, GUILayout.Width(60))) icons = Icons(true);
        }

        if (icons.Count == 0)
        {
            EditorGUILayout.HelpBox("No sprites found under\n" + Folder, MessageType.Warning);
            return;
        }

        const int cell = 46, pad = 2;
        int cols = Mathf.Max(1, (int)((GetWindowSize().x - 16) / (cell + pad)));
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        int shown = 0;
        for (int i = 0; i < icons.Count; i++)
        {
            var s = icons[i];
            if (s == null) continue;
            if (!string.IsNullOrEmpty(_search) &&
                s.name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

            if (shown % cols == 0)
            {
                if (shown > 0) EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
            }

            var tex = AssetPreview.GetAssetPreview(s);
            var gc = tex != null ? new GUIContent(tex, s.name) : new GUIContent(s.name, s.name);
            if (GUILayout.Button(gc, GUILayout.Width(cell), GUILayout.Height(cell)))
            {
                _onPick?.Invoke(s);
                editorWindow.Close();
            }
            shown++;
        }
        if (shown > 0) EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();

        // Previews generate asynchronously; keep repainting until they're all in so blank cells fill.
        if (AssetPreview.IsLoadingAssetPreviews()) editorWindow.Repaint();
    }
}
