using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Bidirectional map linking. <see cref="MapLinker.LinkMaps"/> wires a freshly built map to one or more
/// existing maps with TWO-WAY doorways: for each pair (A,B) it adds — on A — a <see cref="Portal3D"/> →B plus
/// the matching arrival <see cref="SpawnPoint"/>, and — on B — the return Portal3D →A plus its arrival spawn.
/// Both halves reuse the idempotent <see cref="RegionBuilder"/> helpers (so re-linking never duplicates), and
/// portal COLOUR is auto-derived from the destination map name (town=yellow, field=green …) inside MakePortal.
///
/// The travel contract (see <see cref="MapManager"/> / PersistentPlayer3D): a portal stores the destination
/// scene NAME + the id of the SpawnPoint to arrive at. To support an arbitrary grid of maps we key every
/// spawn id by the SOURCE map name — <c>From_&lt;map&gt;</c> — so N maps interlink with no id collisions
/// (the legacy hand-wired FromTown/FromField pair is just the 2-map special case).
///
/// Doorways are spread around each map's edges (E / W / N / S, then corners) and snapped onto the Ground-layer
/// terrain; everything is hand-movable afterwards (nudge a pad, then Authoring ▸ Drop Selected to Ground).
/// </summary>
public static class MapLinker
{
    const float EdgeMargin = 3.5f;   // pull the doorway in from the very edge of the plot
    const float SpawnInset = 3.0f;   // arrival spawn sits this far inward from its doorway (no instant re-trigger)

    // Edge directions in XZ, in the order doorways are assigned: cardinals first, then corners.
    static readonly Vector2[] Edges =
    {
        new Vector2( 1, 0), new Vector2(-1, 0), new Vector2(0,  1), new Vector2(0, -1),
        new Vector2( 1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1),
    };

    /// <summary>Wire <paramref name="newMapPath"/> to each map in <paramref name="neighborPaths"/> with a
    /// two-way doorway. Opens/saves each scene as needed and leaves the new map open at the end.</summary>
    public static void LinkMaps(string newMapPath, List<string> neighborPaths)
    {
        if (string.IsNullOrEmpty(newMapPath) || neighborPaths == null || neighborPaths.Count == 0) return;
        string aName = Path.GetFileNameWithoutExtension(newMapPath);

        // ── A side: the new map is already open & active. Add one outgoing doorway per neighbour. ──
        var aScene = EnsureActive(newMapPath);
        int agm = GroundMask();
        Physics.SyncTransforms();
        Bounds ab = GroundBounds(aScene);
        int aBaseEdge = CountPortals(aScene);   // skip edges already used by A's existing doorways

        for (int i = 0; i < neighborPaths.Count; i++)
        {
            string bName = Path.GetFileNameWithoutExtension(neighborPaths[i]);
            Vector3 door  = WorldBuildUtil.SurfacePoint(EdgePoint(ab, aBaseEdge + i), agm);
            Vector3 spawn = WorldBuildUtil.SurfacePoint(Inward(ab, door, SpawnInset), agm);
            RegionBuilder.EnsureSpawn(aScene, "From_" + bName, spawn);
            RegionBuilder.EnsurePortal(aScene, "Portal_To_" + bName, door, bName, "From_" + aName, Color.white);
            WorldBuildUtil.RegisterInBuildSettings(neighborPaths[i]);   // so A's portal can load B by name
        }
        EditorSceneManager.MarkSceneDirty(aScene);
        EditorSceneManager.SaveScene(aScene);
        WorldBuildUtil.RegisterInBuildSettings(newMapPath);

        // ── B side: open each neighbour in turn, add the return doorway on a free edge, save. ──
        foreach (var bPath in neighborPaths)
        {
            string bName = Path.GetFileNameWithoutExtension(bPath);
            var bScene = EditorSceneManager.OpenScene(bPath, OpenSceneMode.Single);
            SceneManager.SetActiveScene(bScene);
            int bgm = GroundMask();
            Physics.SyncTransforms();
            Bounds bb = GroundBounds(bScene);

            int edgeIx = CountPortals(bScene);   // spread this return across B's still-free edges
            Vector3 door  = WorldBuildUtil.SurfacePoint(EdgePoint(bb, edgeIx), bgm);
            Vector3 spawn = WorldBuildUtil.SurfacePoint(Inward(bb, door, SpawnInset), bgm);
            RegionBuilder.EnsureSpawn(bScene, "From_" + aName, spawn);
            RegionBuilder.EnsurePortal(bScene, "Portal_To_" + aName, door, aName, "From_" + bName, Color.white);
            WorldBuildUtil.RegisterInBuildSettings(newMapPath);   // so B's portal can load A by name

            EditorSceneManager.MarkSceneDirty(bScene);
            EditorSceneManager.SaveScene(bScene);
        }

        // Back to the new field so the user sees what they just built.
        EditorSceneManager.OpenScene(newMapPath, OpenSceneMode.Single);
        AssetDatabase.SaveAssets();
        Debug.Log($"[unwritten] Linked '{aName}' to {neighborPaths.Count} map(s) with two-way doorways.");
    }

    // ── geometry ─────────────────────────────────────────────────────────────────────────────────────
    // A doorway position on the i-th edge of the plot, margin-inset from the rim.
    static Vector3 EdgePoint(Bounds b, int i)
    {
        var d = Edges[i % Edges.Length];
        float x = b.center.x + d.x * Mathf.Max(0f, b.extents.x - EdgeMargin);
        float z = b.center.z + d.y * Mathf.Max(0f, b.extents.z - EdgeMargin);
        return new Vector3(x, b.center.y, z);
    }

    // Step a point toward the plot centre (horizontally) by `inset` units.
    static Vector3 Inward(Bounds b, Vector3 p, float inset)
    {
        Vector3 toCenter = new Vector3(b.center.x - p.x, 0f, b.center.z - p.z);
        if (toCenter.sqrMagnitude < 0.0001f) return p;
        return p + toCenter.normalized * inset;
    }

    static int GroundMask()
    {
        int gl = WorldBuildUtil.EnsureGroundLayer();
        return gl >= 0 ? 1 << gl : ~0;
    }

    // Combined renderer bounds of everything on the Ground layer in `scene` (the walkable plot). Falls back to
    // a 60×60 plot at the origin (the builders' default size) if nothing is found.
    static Bounds GroundBounds(Scene scene)
    {
        int gl = LayerMask.NameToLayer("Ground");
        bool any = false; Bounds b = default;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.gameObject.scene != scene || r.gameObject.layer != gl) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any ? b : new Bounds(Vector3.zero, new Vector3(60f, 1f, 60f));
    }

    static int CountPortals(Scene scene)
    {
        int n = 0;
        foreach (var p in Object.FindObjectsByType<Portal3D>(FindObjectsSortMode.None))
            if (p.gameObject.scene == scene) n++;
        return n;
    }

    static Scene EnsureActive(string scenePath)
    {
        var active = SceneManager.GetActiveScene();
        if (active.path == scenePath) return active;
        var s = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(s);
        return s;
    }
}

/// <summary>The "connect to which maps?" popup shown after building a linkable field. Lists the other scenes in
/// Assets/Scenes as checkboxes; "Connect selected" wires two-way doorways via <see cref="MapLinker"/>.</summary>
public class MapLinkWindow : EditorWindow
{
    string newMapPath;
    int builtProps, builtMobs;
    readonly List<string> candidates = new List<string>();   // other scene paths
    readonly List<bool>   chosen     = new List<bool>();
    Vector2 scroll;

    public static void Open(string newMapPath, int props, int mobs)
    {
        var w = GetWindow<MapLinkWindow>(true, "Connect Field to Maps");
        w.newMapPath = newMapPath;
        w.builtProps = props;
        w.builtMobs  = mobs;
        w.Refresh();
        w.minSize = new Vector2(380f, 340f);
        w.Focus();
    }

    void Refresh()
    {
        candidates.Clear();
        chosen.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            string n = Path.GetFileNameWithoutExtension(p);
            if (p == newMapPath || n == MapManager.SystemsScene) continue;   // never link to self or Systems
            candidates.Add(p);
            chosen.Add(false);
        }
    }

    void OnGUI()
    {
        string newName = Path.GetFileNameWithoutExtension(newMapPath);
        EditorGUILayout.LabelField($"New field: {newName}", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"{builtProps} props · {builtMobs} mobs placed", EditorStyles.miniLabel);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Connect it to these maps (creates two-way doorways):");

        if (candidates.Count == 0)
            EditorGUILayout.HelpBox("No other maps in Assets/Scenes yet. Build a town or another field first, "
                + "then re-run to connect them.", MessageType.Info);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        for (int i = 0; i < candidates.Count; i++)
            chosen[i] = EditorGUILayout.ToggleLeft(Path.GetFileNameWithoutExtension(candidates[i]), chosen[i]);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Skip (no links)")) Close();

            bool anyPicked = chosen.Contains(true);
            using (new EditorGUI.DisabledScope(!anyPicked))
            {
                if (GUILayout.Button("Connect selected"))
                {
                    var picks = new List<string>();
                    for (int i = 0; i < candidates.Count; i++)
                        if (chosen[i]) picks.Add(candidates[i]);

                    MapLinker.LinkMaps(newMapPath, picks);
                    Close();
                    EditorUtility.DisplayDialog("Field connected",
                        $"'{newName}' is built and connected to {picks.Count} map(s) with two-way doorways.\n\n"
                        + "Each link added a coloured portal + arrival spawn on BOTH maps (colour = where it "
                        + "leads: town = yellow, field = green).\n\n"
                        + "PLAY from any connected map and walk into a pad to travel. Nudge any pad by hand, then "
                        + "Authoring ▸ Drop Selected to Ground.", "OK");
                }
            }
        }
    }
}

/// <summary>Standalone two-way linker for maps that ALREADY exist: pick a primary map (A) and tick the maps to
/// connect it to, then wire two-way doorways via <see cref="MapLinker"/>. The generate-time picker
/// (<see cref="MapLinkWindow"/>) is for a freshly built field; this one works on any pair of existing scenes.</summary>
public class LinkMapsWindow : EditorWindow
{
    readonly List<string> allPaths = new List<string>();   // every linkable scene (excludes Systems)
    readonly List<bool>   chosen   = new List<bool>();      // parallel to allPaths
    int aIndex;
    Vector2 scroll;

    [MenuItem("Tools/unwritten/World/Link Two Maps…", priority = 223)]
    static void OpenMenu()
    {
        var w = GetWindow<LinkMapsWindow>(true, "Link Maps");
        w.Refresh();
        w.minSize = new Vector2(400f, 360f);
        w.Focus();
    }

    void Refresh()
    {
        allPaths.Clear();
        chosen.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(p) == MapManager.SystemsScene) continue;
            allPaths.Add(p);
            chosen.Add(false);
        }
        aIndex = Mathf.Clamp(aIndex, 0, Mathf.Max(0, allPaths.Count - 1));
    }

    void OnGUI()
    {
        if (allPaths.Count < 2)
        {
            EditorGUILayout.HelpBox("Need at least two maps in Assets/Scenes to link. Build more first.", MessageType.Info);
            if (GUILayout.Button("Refresh")) Refresh();
            return;
        }

        var names = new string[allPaths.Count];
        for (int i = 0; i < allPaths.Count; i++) names[i] = Path.GetFileNameWithoutExtension(allPaths[i]);

        EditorGUILayout.LabelField("Two-way map linker (existing maps)", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        aIndex = EditorGUILayout.Popup("Map A", aIndex, names);
        EditorGUILayout.LabelField("Connect Map A to:");

        scroll = EditorGUILayout.BeginScrollView(scroll);
        for (int i = 0; i < allPaths.Count; i++)
        {
            if (i == aIndex) continue;   // can't link a map to itself
            chosen[i] = EditorGUILayout.ToggleLeft(names[i], chosen[i]);
        }
        EditorGUILayout.EndScrollView();

        bool anyPicked = false;
        for (int i = 0; i < allPaths.Count; i++) if (i != aIndex && chosen[i]) { anyPicked = true; break; }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Close")) Close();
            using (new EditorGUI.DisabledScope(!anyPicked))
            {
                if (GUILayout.Button("Connect"))
                {
                    string aPath = allPaths[aIndex];
                    var picks = new List<string>();
                    for (int i = 0; i < allPaths.Count; i++)
                        if (i != aIndex && chosen[i]) picks.Add(allPaths[i]);

                    MapLinker.LinkMaps(aPath, picks);
                    string aName = Path.GetFileNameWithoutExtension(aPath);
                    Close();
                    EditorUtility.DisplayDialog("Maps linked",
                        $"'{aName}' connected to {picks.Count} map(s) with two-way doorways.\n\n"
                        + "Each link added a coloured portal + arrival spawn on BOTH maps (colour = where it "
                        + "leads: town = yellow, field = green).\n\n"
                        + "PLAY from either map and walk into a pad to travel. Nudge any pad by hand, then "
                        + "Authoring ▸ Drop Selected to Ground.", "OK");
                }
            }
        }
    }
}
