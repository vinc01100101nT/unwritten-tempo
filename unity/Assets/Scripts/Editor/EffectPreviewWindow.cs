using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Live tuning window for the <see cref="EffectLibrary"/>: pick an effect id, spawn it in the open scene, drag
/// scale / offset / lifetime sliders and watch it update in real time, then <b>Save to Library</b> to store
/// those numbers as that effect's defaults. Every runtime <see cref="Vfx.PlayAt(string,Vector3)"/> then uses
/// them — so this is the one place you dial in how big / where / how long an effect plays, for hits, auras,
/// skills, everything. Menu: <b>Tools ▸ unwritten ▸ Art ▸ Effect Preview</b>.
///
/// The preview instance is a throwaway (HideFlags.DontSave) parented under a "[EffectPreview]" root; it's
/// selected on spawn so Unity's built-in scene-view particle playback animates it. Nothing here touches the
/// prefab — only the library asset's per-effect numbers.
/// </summary>
public class EffectPreviewWindow : EditorWindow
{
    const string LibraryPath = "Assets/Resources/EffectLibrary.asset";
    const string RootName = "[EffectPreview]";

    EffectLibrary lib;
    string[] ids = System.Array.Empty<string>();
    int selected;

    GameObject preview;      // the live throwaway instance
    Vector3 basePos;         // spawn point (offset is added on top of this)
    bool spawnOnSelection;

    // Working values (edited by the sliders); seeded from the effect's stored def on Spawn.
    float scale = 1f;
    Vector3 offset;
    float lifetime;

    [MenuItem("Tools/unwritten/Art/Effect Preview", priority = 625)]
    public static void Open() => GetWindow<EffectPreviewWindow>("Effect Preview");

    void OnEnable()
    {
        LoadLibrary();
        AdoptExistingPreview();
    }

    void OnDisable() => Cleanup();

    void LoadLibrary()
    {
        lib = AssetDatabase.LoadAssetAtPath<EffectLibrary>(LibraryPath);
        var list = new List<string>();
        if (lib != null)
            foreach (var e in lib.effects)
                if (e != null && !string.IsNullOrWhiteSpace(e.id) && e.prefab != null) list.Add(e.id);
        list.Sort(string.CompareOrdinal);
        ids = list.ToArray();
        selected = Mathf.Clamp(selected, 0, Mathf.Max(0, ids.Length - 1));
    }

    // After a domain reload the window fields reset but a preview object may still be in the scene — re-grab it.
    void AdoptExistingPreview()
    {
        var root = GameObject.Find(RootName);
        if (root != null && root.transform.childCount > 0)
            preview = root.transform.GetChild(0).gameObject;
    }

    EffectDef Current => (lib != null && selected >= 0 && selected < ids.Length) ? Find(ids[selected]) : null;

    EffectDef Find(string id)
    {
        foreach (var e in lib.effects) if (e != null && e.id == id) return e;
        return null;
    }

    void OnGUI()
    {
        if (lib == null)
        {
            EditorGUILayout.HelpBox("No EffectLibrary asset found at " + LibraryPath +
                                    ".\nBuild it first, then reopen this window.", MessageType.Warning);
            if (GUILayout.Button("Rebuild Effect Library")) { EffectLibraryBuilder.Rebuild(); LoadLibrary(); }
            return;
        }
        if (ids.Length == 0)
        {
            EditorGUILayout.HelpBox("The library has no effects. Run Rebuild Effect Library.", MessageType.Info);
            if (GUILayout.Button("Reload")) LoadLibrary();
            return;
        }

        EditorGUILayout.LabelField("Effect", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            int newSel = EditorGUILayout.Popup(selected, ids);
            if (newSel != selected) { selected = newSel; if (preview != null) Spawn(); }
            if (GUILayout.Button("↻", GUILayout.Width(24))) LoadLibrary();
        }

        spawnOnSelection = EditorGUILayout.ToggleLeft(
            "Spawn on selected object (else scene-view center)", spawnOnSelection);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(preview == null ? "Spawn" : "Respawn")) Spawn();
            using (new EditorGUI.DisabledScope(preview == null))
            {
                if (GUILayout.Button("Restart")) Restart();
                if (GUILayout.Button("Remove")) Cleanup();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Tune (live)", EditorStyles.boldLabel);
        using (var check = new EditorGUI.ChangeCheckScope())
        {
            scale = EditorGUILayout.Slider("Scale", scale, 0.02f, 5f);
            offset = EditorGUILayout.Vector3Field("Offset", offset);
            lifetime = EditorGUILayout.Slider("Lifetime (0 = auto)", lifetime, 0f, 10f);
            if (check.changed && preview != null) ApplyToPreview();
        }

        var def = Current;
        if (def != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Stored in library",
                $"scale {def.scale:0.###}   offset {def.offset}   life {def.lifetime:0.##}", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(preview == null))
                    if (GUILayout.Button("Save to Library")) SaveToLibrary(def);
                if (GUILayout.Button("Revert to Stored")) { scale = def.scale <= 0 ? 1f : def.scale; offset = def.offset; lifetime = def.lifetime; if (preview != null) ApplyToPreview(); }
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Spawn, drag the sliders until it looks right in the Scene view, then Save to " +
                                "Library. Runtime Vfx.PlayAt uses the saved scale/offset/lifetime for every use " +
                                "of this effect.", MessageType.None);
    }

    void Spawn()
    {
        Cleanup();
        var def = Current;
        if (def == null || def.prefab == null) return;

        // seed the sliders from what's stored so tuning starts from the current default
        scale = def.scale <= 0f ? 1f : def.scale;
        offset = def.offset;
        lifetime = def.lifetime;

        basePos = (spawnOnSelection && Selection.activeTransform != null)
            ? Selection.activeTransform.position
            : (SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero);

        var root = GameObject.Find(RootName) ?? new GameObject(RootName) { hideFlags = HideFlags.DontSave };
        var go = (GameObject)PrefabUtility.InstantiatePrefab(def.prefab);
        go.transform.SetParent(root.transform, true);
        go.hideFlags = HideFlags.DontSave;
        preview = go;
        ApplyToPreview();
        Selection.activeGameObject = go;   // drives Unity's scene-view particle playback
        Restart();
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Repaint();
    }

    void ApplyToPreview()
    {
        if (preview == null) return;
        preview.transform.position = basePos + offset;
        preview.transform.localScale = Vector3.one * Mathf.Max(0.001f, scale);
    }

    void Restart()
    {
        if (preview == null) return;
        foreach (var ps in preview.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Clear(true);
            ps.Play(true);
        }
    }

    void SaveToLibrary(EffectDef def)
    {
        Undo.RecordObject(lib, "Tune Effect");
        def.scale = scale;
        def.offset = offset;
        def.lifetime = lifetime;
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        EffectCatalog.Reload();
        Debug.Log($"[EffectPreview] Saved '{def.id}': scale {scale:0.###}, offset {offset}, life {lifetime:0.##}");
    }

    void Cleanup()
    {
        if (preview != null) DestroyImmediate(preview);
        preview = null;
        var root = GameObject.Find(RootName);
        if (root != null && root.transform.childCount == 0) DestroyImmediate(root);
    }
}
