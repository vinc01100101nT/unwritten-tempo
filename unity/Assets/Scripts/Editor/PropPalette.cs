using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A Figma/Storybook-style <b>prop palette</b> for hand-authoring 2.5D maps. Open it, pick a prefab from the
/// thumbnail grid, then <b>click in the Scene view to place it</b> — at its native prefab size, dropped onto the ground by
/// its measured bounds (no floating), optionally grid-snapped, parented under a "Handmade" root in the active
/// scene, and fully Undo-able. Hold and keep clicking to place many; <b>Q/E</b> rotate the next one;
/// <b>Esc</b> or right-click stops placing.
///
/// Built for a Unity beginner: no manual scaling, no manual ground-snapping, no dragging from the Project
/// window. It hides the <c>UCX_</c> collision hulls and <c>_LOD1/_LOD2</c> duplicates by default (toggle
/// "Show all" to see them). Uses the shared <see cref="PropPlacement"/> engine.
/// </summary>
public class PropPalette : EditorWindow
{
    const string PropsDir = "Assets/Prefabs/Props";

    [MenuItem("Tools/unwritten/Authoring/Prop Palette", priority = 400)]
    static void Open() => GetWindow<PropPalette>("Prop Palette").minSize = new Vector2(260, 360);

    // settings
    bool snapGrid = true;
    float gridSize = 0.5f;
    bool randomYaw = false;
    bool showAll = false;
    bool walkThrough = true;   // strip colliders on placement (auto-set per prop in Arm: on for flora)
    string search = "";

    // state
    readonly List<GameObject> prefabs = new List<GameObject>();
    // Sticky thumbnail cache: once a prop's preview has rendered we hold onto it, so a cell never
    // blinks back to the gray/label placeholder if Unity's internal preview cache evicts it.
    readonly Dictionary<GameObject, Texture2D> previewCache = new Dictionary<GameObject, Texture2D>();
    GameObject armed;          // prefab currently being placed
    float yaw;                 // rotation for the next placement
    Vector2 scroll;

    void OnEnable() { Refresh(); SceneView.duringSceneGui += OnScene; }
    void OnDisable() { SceneView.duringSceneGui -= OnScene; }

    void Refresh()
    {
        prefabs.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PropsDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!showAll && (name.StartsWith("UCX_") || name.EndsWith("_LOD1") || name.EndsWith("_LOD2"))) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) prefabs.Add(go);
        }
        prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        // Make the preview cache big enough to hold EVERY prop at once. The default (128, shared with
        // other editor windows) churns for 3D-prefab previews: entries get evicted + regenerated every
        // repaint, so cells flicker between the thumbnail and the placeholder. Sizing it past our count
        // (with headroom for other windows) stops the eviction, which stops the blinking.
        AssetPreview.SetPreviewTextureCacheSize(Mathf.Max(64, prefabs.Count + 32));
        previewCache.Clear();
    }

    // Cached preview lookup: returns the stored thumbnail if we have it, otherwise asks Unity and
    // stores the first non-null result. Unity-null check means a destroyed (evicted) texture is re-fetched.
    Texture2D Preview(GameObject p)
    {
        if (previewCache.TryGetValue(p, out var cached) && cached != null) return cached;
        var tex = AssetPreview.GetAssetPreview(p);
        if (tex != null) previewCache[p] = tex;
        return tex;
    }

    // ── window UI ───────────────────────────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Pick a prop, then CLICK in the Scene view to place it on the ground.\nQ/E rotate · Esc or right-click stops.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            snapGrid = EditorGUILayout.ToggleLeft("Snap to grid", snapGrid, GUILayout.Width(110));
            using (new EditorGUI.DisabledScope(!snapGrid))
                gridSize = EditorGUILayout.FloatField(gridSize);
        }
        randomYaw = EditorGUILayout.ToggleLeft("Random Y rotation each placement", randomYaw);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            showAll = EditorGUILayout.ToggleLeft(new GUIContent("Show all (UCX/LODs)", "Also list collision hulls and LOD1/2 duplicates."), showAll, GUILayout.Width(160));
            if (EditorGUI.EndChangeCheck()) Refresh();
            search = EditorGUILayout.TextField(search);
        }

        if (armed != null)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label($"Placing: {armed.name}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                walkThrough = GUILayout.Toggle(walkThrough,
                    new GUIContent("Walk-through", "Strip colliders so the player can walk through this prop (auto-on for grass/flowers/ferns)."),
                    GUILayout.Width(104));
                if (GUILayout.Button("Stop", GUILayout.Width(60))) Disarm();
            }
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawGrid();
        EditorGUILayout.EndScrollView();
    }

    void DrawGrid()
    {
        const int cell = 84;
        int perRow = Mathf.Max(1, (int)((position.width - 24) / cell));
        int col = 0;
        EditorGUILayout.BeginHorizontal();
        foreach (var p in prefabs)
        {
            if (!string.IsNullOrEmpty(search) && p.name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (col >= perRow) { EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal(); col = 0; }

            var tex = Preview(p);
            var content = tex != null ? new GUIContent(tex, p.name) : new GUIContent(p.name, p.name);
            var prevBg = GUI.backgroundColor;
            if (armed == p) GUI.backgroundColor = new Color(0.4f, 0.9f, 1f);   // highlight the armed prop
            if (GUILayout.Button(content, GUILayout.Width(cell - 6), GUILayout.Height(cell - 6)))
                Arm(p);
            GUI.backgroundColor = prevBg;   // tooltip carries the name; thumbnails fill in as they load
            col++;
        }
        EditorGUILayout.EndHorizontal();

        if (prefabs.Count == 0)
            EditorGUILayout.HelpBox($"No prefabs in {PropsDir}. Run Build Prefabs ▸ Fantasy Assets (Pxltgr) → Props first.", MessageType.Warning);

        if (AssetPreview.IsLoadingAssetPreviews()) Repaint();   // keep refreshing until thumbnails finish
    }

    void Arm(GameObject p) { armed = p; yaw = 0f; walkThrough = PropPlacement.IsWalkThroughDecor(p.name); SceneView.RepaintAll(); }
    void Disarm() { armed = null; SceneView.RepaintAll(); Repaint(); }

    // ── scene-view placement ──────────────────────────────────────────────────────────────────────────────
    void OnScene(SceneView sv)
    {
        if (armed == null) return;
        var e = Event.current;

        // Make our tool the default "control" so clicks place props instead of selecting/deselecting objects.
        if (e.type == EventType.Layout)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Escape) { Disarm(); e.Use(); return; }
            if (e.keyCode == KeyCode.Q) { yaw -= 15f; e.Use(); }
            if (e.keyCode == KeyCode.E) { yaw += 15f; e.Use(); }
        }

        if (!GroundPointUnderMouse(e, out var point)) { if (e.type == EventType.MouseMove) sv.Repaint(); return; }

        // ghost marker so you can see where it lands
        if (e.type == EventType.Repaint)
        {
            Handles.color = new Color(0.4f, 0.9f, 1f, 0.9f);
            Vector3 snapped = snapGrid ? PropPlacement.SnapToGrid(point, gridSize) : point;
            Handles.DrawWireDisc(snapped, Vector3.up, 0.5f);
            Handles.DrawLine(snapped, snapped + Vector3.up * 1.5f);
        }
        if (e.type == EventType.MouseMove) sv.Repaint();

        if (e.type == EventType.MouseDown && e.button == 1) { Disarm(); e.Use(); return; }   // right-click stop
        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            Place(snapGrid ? PropPlacement.SnapToGrid(point, gridSize) : point);
            e.Use();
        }
    }

    // Where the mouse ray meets the ground (collider on the Ground layer), else the y=0 plane.
    static bool GroundPointUnderMouse(Event e, out Vector3 point)
    {
        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out var hit, 5000f, PropPlacement.GroundMask(), QueryTriggerInteraction.Ignore))
        { point = hit.point; return true; }
        if (Mathf.Abs(ray.direction.y) > 1e-4f)                       // fall back to the ground plane
        {
            float t = -ray.origin.y / ray.direction.y;
            if (t > 0f) { point = ray.origin + ray.direction * t; return true; }
        }
        point = default; return false;
    }

    void Place(Vector3 at)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(armed);
        Undo.RegisterCreatedObjectUndo(go, "Place Prop");
        // Leave localScale as the prefab's own — the prefab owns its size (eyeballed + applied); nothing here rescales it.
        go.transform.rotation = Quaternion.Euler(0f, randomYaw ? Random.Range(0f, 360f) : yaw, 0f);
        PropPlacement.PlaceOnGround(go, at, PropPlacement.GroundMask());
        go.transform.SetParent(HandmadeRoot(), true);
        if (walkThrough) PropPlacement.StripColliders(go);   // decor you can walk through (no phantom walls)
        Selection.activeGameObject = go;
    }

    static Transform HandmadeRoot()
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Handmade") return root.transform;
        var go = new GameObject("Handmade");
        Undo.RegisterCreatedObjectUndo(go, "Create Handmade root");
        SceneManager.MoveGameObjectToScene(go, scene);
        return go.transform;
    }
}
