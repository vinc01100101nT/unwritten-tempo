using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityMeshSimplifier;

/// <summary>
/// Author-time mesh decimation with a measured break-point workflow. TRELLIS/Hunyuan props arrive far too
/// dense (≈200–300k tris) for a 2.5D iso game; this finds the lowest tri count that still reads at the real
/// gameplay camera, then bakes a saved low-poly <c>.mesh</c> asset. NOT a runtime cost — it's a bake.
///
/// Workflow (the whole point): select the meshes → set a target tri count → <b>Preview</b> swaps the live
/// MeshFilter so you judge it in the Scene view at iso distance → <b>Apply</b> saves the asset, or
/// <b>Revert</b> restores the originals. Find the number by eye; don't trust a guessed budget.
///
/// Uses quadric-error decimation (UnityMeshSimplifier) — smarter than Blender's flat Decimate (weights by
/// error, can preserve borders/UV seams) but still NOT true retopology. Sweet spot: B/C/D mass props and
/// break-point testing. Hero S-tier buildings still want a hand retopo for clean quad topology.
/// </summary>
public class MeshDecimateWindow : EditorWindow
{
    const string MeshDir = "Assets/Meshes/Decimated";

    int targetTris = 6000;
    bool preserveBorders = true;
    bool preserveUvSeams = true;
    bool preserveUvFoldover = false;
    bool smartLink = true;
    bool alsoCollider = false;

    // Originals we swapped during Preview, so Revert / re-Preview restore from source, never from a
    // previously-decimated mesh (which would compound error).
    static readonly Dictionary<MeshFilter, Mesh> s_originals = new();

    [MenuItem("Tools/unwritten/Props/Decimate Selection…", priority = 506)]
    static void Open() => GetWindow<MeshDecimateWindow>(true, "Decimate Selection", true).minSize = new Vector2(360, 340);

    void OnSelectionChange() => Repaint();

    void OnGUI()
    {
        var filters = SelectedFilters();

        EditorGUILayout.HelpBox(
            "Select GameObjects with meshes in the scene/hierarchy.\n" +
            "Preview = non-destructive (swaps the live mesh so you can eyeball it at the iso camera).\n" +
            "Apply = saves a .mesh asset and points the MeshFilter at it. Revert = restore originals.",
            MessageType.Info);

        if (filters.Length == 0)
        {
            EditorGUILayout.HelpBox("Nothing selected with a MeshFilter + mesh.", MessageType.Warning);
            return;
        }

        int srcTotal = filters.Sum(f => Tris(Source(f)));
        int liveTotal = filters.Sum(f => Tris(f.sharedMesh));
        EditorGUILayout.LabelField($"Selected meshes: {filters.Length}");
        EditorGUILayout.LabelField($"Source tris: {srcTotal:N0}    Live tris: {liveTotal:N0}");

        EditorGUILayout.Space();
        targetTris = Mathf.Max(50, EditorGUILayout.IntField("Target tris (per mesh)", targetTris));
        EditorGUILayout.LabelField(" ", $"≈ ratio {(srcTotal > 0 ? (float)targetTris * filters.Length / srcTotal : 0f):0.00} of source");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preserve", EditorStyles.boldLabel);
        preserveBorders = EditorGUILayout.Toggle("Border edges", preserveBorders);
        preserveUvSeams = EditorGUILayout.Toggle("UV seams", preserveUvSeams);
        preserveUvFoldover = EditorGUILayout.Toggle("UV foldover", preserveUvFoldover);
        smartLink = EditorGUILayout.Toggle("Smart link (weld)", smartLink);
        alsoCollider = EditorGUILayout.Toggle("Also update MeshCollider", alsoCollider);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Preview", GUILayout.Height(30))) Run(filters, apply: false);
            using (new EditorGUI.DisabledScope(s_originals.Count == 0))
                if (GUILayout.Button("Revert", GUILayout.Height(30))) Revert();
        }
        if (GUILayout.Button("Apply (save .mesh asset)", GUILayout.Height(30))) Run(filters, apply: true);
    }

    void Run(MeshFilter[] filters, bool apply)
    {
        if (apply) EnsureFolder();
        int done = 0, after = 0;
        foreach (var mf in filters)
        {
            var src = Source(mf);
            if (src == null) continue;

            var simplified = Decimate(src, targetTris);
            if (simplified == null) continue;

            if (!s_originals.ContainsKey(mf)) s_originals[mf] = mf.sharedMesh;

            if (apply)
            {
                string path = AssetDatabase.GenerateUniqueAssetPath($"{MeshDir}/{src.name}_{Tris(simplified)}.mesh");
                AssetDatabase.CreateAsset(simplified, path);
                Undo.RecordObject(mf, "Apply Decimate");
                mf.sharedMesh = simplified;
                if (alsoCollider && mf.TryGetComponent<MeshCollider>(out var mc)) mc.sharedMesh = simplified;
                s_originals.Remove(mf);   // committed; no longer a pending preview
            }
            else
            {
                Undo.RecordObject(mf, "Preview Decimate");
                mf.sharedMesh = simplified;
                if (alsoCollider && mf.TryGetComponent<MeshCollider>(out var mc)) mc.sharedMesh = simplified;
            }
            after += Tris(simplified);
            done++;
        }

        if (apply) { AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }
        SceneView.RepaintAll();
        Debug.Log($"[unwritten] Decimate {(apply ? "applied" : "preview")}: {done} mesh(es), {after:N0} tris total" +
                  (apply ? $" → saved under {MeshDir}." : " (not yet saved — Apply to keep)."));
    }

    // Solve toward the target tri count: quality is a 0..1 ratio, so seed ratio=target/current and do up to
    // two proportional correction passes (the algorithm won't land exactly on a count).
    Mesh Decimate(Mesh src, int target)
    {
        int srcTris = Tris(src);
        if (srcTris <= target) return null;   // already under budget — nothing to do

        Mesh best = null;
        float quality = Mathf.Clamp01((float)target / srcTris);
        for (int pass = 0; pass < 3; pass++)
        {
            var ms = new MeshSimplifier
            {
                PreserveBorderEdges = preserveBorders,
                PreserveUVSeamEdges = preserveUvSeams,
                PreserveUVFoldoverEdges = preserveUvFoldover,
                EnableSmartLink = smartLink,
                MaxIterationCount = 100,
            };
            ms.Initialize(src);
            ms.SimplifyMesh(Mathf.Clamp01(quality));
            best = ms.ToMesh();
            best.name = src.name;

            int got = Tris(best);
            if (got <= 0) break;
            if (Mathf.Abs(got - target) <= target * 0.07f) break;   // within 7% — good enough
            quality *= (float)target / got;                          // proportional correction
        }
        return best;
    }

    void Revert()
    {
        foreach (var kv in s_originals)
            if (kv.Key != null) { Undo.RecordObject(kv.Key, "Revert Decimate"); kv.Key.sharedMesh = kv.Value; }
        s_originals.Clear();
        SceneView.RepaintAll();
    }

    static MeshFilter[] SelectedFilters() =>
        Selection.gameObjects
            .SelectMany(g => g.GetComponentsInChildren<MeshFilter>())
            .Where(f => Source(f) != null)
            .Distinct().ToArray();

    // Source = the original we stashed on first preview, else the current live mesh.
    static Mesh Source(MeshFilter f) =>
        f != null && s_originals.TryGetValue(f, out var o) ? o : (f != null ? f.sharedMesh : null);

    static int Tris(Mesh m) => m == null ? 0 : m.triangles.Length / 3;

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Meshes")) AssetDatabase.CreateFolder("Assets", "Meshes");
        if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Meshes", "Decimated");
    }
}
