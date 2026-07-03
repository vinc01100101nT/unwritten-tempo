using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-window BGM authoring: assign a <see cref="BgmTrack"/> to each map WITHOUT re-generating the scene. It edits the
/// single <c>Resources/MapBgmTable</c> asset (scene name → track) that <see cref="MusicDirector"/> reads at runtime.
/// "Sync scene list" adds a row for every Assets/Scenes/*.unity map and prunes rows whose scene was deleted.
/// </summary>
public class MapBgmTableWindow : EditorWindow
{
    const string ScenesDir = "Assets/Scenes";
    const string ResourcesDir = "Assets/Resources";
    const string TablePath = ResourcesDir + "/MapBgmTable.asset";

    MapBgmTable table;
    Vector2 scroll;

    [MenuItem("Tools/unwritten/Audio/Map BGM Table", priority = 200)]
    static void Open() => GetWindow<MapBgmTableWindow>("Map BGM").Show();

    void OnEnable() { table = LoadOrCreate(); SyncScenes(); }

    void OnGUI()
    {
        if (table == null) { if (GUILayout.Button("Create MapBgmTable asset")) table = LoadOrCreate(); return; }

        EditorGUILayout.HelpBox("Assign a Bgm Track to each map. Changes take effect on the next map load — no " +
            "re-generate needed. Empty = no music entry (leaves whatever was playing).", MessageType.Info);

        if (GUILayout.Button("Sync scene list")) SyncScenes();

        var so = new SerializedObject(table);
        so.Update();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        for (int i = 0; i < table.entries.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(table.entries[i].sceneName, GUILayout.Width(160));
                var newTrack = (BgmTrack)EditorGUILayout.ObjectField(table.entries[i].track, typeof(BgmTrack), false);
                if (newTrack != table.entries[i].track)
                {
                    var e = table.entries[i]; e.track = newTrack; table.entries[i] = e;
                    EditorUtility.SetDirty(table);
                }
            }
        }
        EditorGUILayout.EndScrollView();

        if (GUI.changed) { EditorUtility.SetDirty(table); AssetDatabase.SaveAssetIfDirty(table); }
    }

    // Add a row for every map scene missing one; drop rows whose scene no longer exists. Keeps assignments intact.
    void SyncScenes()
    {
        if (table == null) return;
        var maps = Directory.GetFiles(ScenesDir, "*.unity")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != MapManager.SystemsScene)
            .OrderBy(n => n).ToList();

        table.entries.RemoveAll(e => !maps.Contains(e.sceneName));
        foreach (var m in maps)
            if (!table.entries.Any(e => e.sceneName == m))
                table.entries.Add(new MapBgmTable.Entry { sceneName = m, track = null });

        table.entries.Sort((a, b) => string.CompareOrdinal(a.sceneName, b.sceneName));
        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssetIfDirty(table);
    }

    static MapBgmTable LoadOrCreate()
    {
        var t = AssetDatabase.LoadAssetAtPath<MapBgmTable>(TablePath);
        if (t != null) return t;
        if (!Directory.Exists(ResourcesDir)) AssetDatabase.CreateFolder("Assets", "Resources");
        t = CreateInstance<MapBgmTable>();
        AssetDatabase.CreateAsset(t, TablePath);
        AssetDatabase.SaveAssets();
        return t;
    }
}
