using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Bakes a <b>branch-only</b> collision shape for single-mesh trees (Trellis output is one fused blob of
/// trunk + branches + leaves, so its full <see cref="MeshCollider"/> makes the leaves solid — walling off
/// jump paths between branches). This trims the collision to just the woody parts so a character can jump
/// THROUGH foliage and land on a branch inside it, while the visible tree is left untouched.
///
/// How it tells bark from leaf: the tree's colour lives in its texture (painted on via UVs, not per-vertex),
/// so you eyedrop a <b>Bark</b> and a <b>Leaf</b> reference colour off the rendered tree, and every triangle
/// is kept or dropped by which reference its texture colour sits closer to. It's a tuning dial, not surgical —
/// nudge <b>Bark bias</b> and re-Preview until the green collider outline hugs the branches.
///
/// Workflow: select the tree → eyedrop Bark + Leaf → <b>Preview</b> (swaps the live collider so you judge the
/// green outline in the Scene view; <b>Revert</b> undoes) → <b>Bake</b> (saves the trimmed collision mesh and
/// overwrites the tree's collider in place, applying to the prefab).
/// </summary>
public class TreeColliderBaker : EditorWindow
{
    Color barkColor = new(0.35f, 0.22f, 0.10f);
    Color leafColor = new(0.20f, 0.45f, 0.15f);
    float barkBias = 0.10f;   // added to the bark side of the compare — higher keeps MORE solid

    // Live collider meshes we swapped during Preview, so Revert / re-Preview restore the original blob.
    static readonly Dictionary<MeshCollider, Mesh> s_originals = new();

    [MenuItem("Tools/unwritten/Props/Bake Tree Collider…", priority = 507)]
    static void Open() => GetWindow<TreeColliderBaker>(true, "Bake Tree Collider", true).minSize = new Vector2(440, 300);

    void OnSelectionChange() => Repaint();

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Double-click the tree PREFAB to open it in Prefab Mode, select the root, then eyedrop a Bark and a " +
            "Leaf colour off it. Preview shows the green collider outline; Bake writes the branch-only collider " +
            "into the prefab itself (works too on a scene instance or the prefab selected in the Project window).",
            MessageType.Info);

        var go = Selection.activeGameObject;
        if (!TryTarget(go, out var mf, out var mc, out var tex, out var why))
        {
            EditorGUILayout.HelpBox(why, MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("Target", go.name);
        EditorGUILayout.LabelField("Texture", tex.name);

        EditorGUILayout.Space();
        barkColor = EditorGUILayout.ColorField(new GUIContent("Bark colour", "Solid branches — eyedrop the trunk/branch"), barkColor);
        leafColor = EditorGUILayout.ColorField(new GUIContent("Leaf colour", "Jump-through foliage — eyedrop a leaf clump"), leafColor);
        barkBias = EditorGUILayout.Slider(new GUIContent("Bark bias", "Right = more of the tree stays solid; Left = only clearly-bark patches stay solid"), barkBias, -0.25f, 0.5f);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Preview", GUILayout.Height(30))) Bake(mf, mc, tex, apply: false);
            using (new EditorGUI.DisabledScope(!s_originals.ContainsKey(mc)))
                if (GUILayout.Button("Revert", GUILayout.Height(30))) Revert(mc);
        }
        if (GUILayout.Button("Bake (overwrite collider + apply prefab)", GUILayout.Height(30)))
            Bake(mf, mc, tex, apply: true);
    }

    void Bake(MeshFilter mf, MeshCollider mc, Texture2D tex, bool apply)
    {
        var src = mf.sharedMesh;   // the full VISIBLE blob — never swapped, so always the trim source
        if (src == null)
        {
            EditorUtility.DisplayDialog("Bake Tree Collider", "The selected object's MeshFilter has no mesh.", "OK");
            return;
        }
        if (src.uv == null || src.uv.Length == 0)
        {
            EditorUtility.DisplayDialog("Bake Tree Collider", "This mesh has no UVs, so its texture colour can't be sampled per-triangle.", "OK");
            return;
        }

        bool restore = EnsureReadable(tex, out var importer);
        int kept, total;
        var trimmed = BuildBranchMesh(src, tex, out kept, out total);
        if (restore) RestoreReadable(importer);

        if (kept == 0)
        {
            EditorUtility.DisplayDialog("Bake Tree Collider",
                "No triangles matched Bark — lower the Leaf colour distance or nudge Bark bias up.", "OK");
            trimmed = null;
        }
        if (trimmed == null) return;

        if (!s_originals.ContainsKey(mc)) s_originals[mc] = mc.sharedMesh;

        if (apply)
        {
            string assetPath = MeshAssetPath(src);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null) { existing.Clear(); EditorUtility.CopySerialized(trimmed, existing); trimmed = existing; }
            else AssetDatabase.CreateAsset(trimmed, assetPath);

            Undo.RecordObject(mc, "Bake Tree Collider");
            mc.sharedMesh = trimmed;
            mc.convex = false;
            s_originals.Remove(mc);
            Persist(mc);

            AssetDatabase.SaveAssets();
            Debug.Log($"[unwritten] Baked branch collider for {mf.name}: kept {kept}/{total} tris solid → {assetPath}");
        }
        else
        {
            Undo.RecordObject(mc, "Preview Tree Collider");
            mc.sharedMesh = trimmed;
            Debug.Log($"[unwritten] Preview: {kept}/{total} tris solid (bark). Revert to restore, Bake to keep.");
        }
        SceneView.RepaintAll();
    }

    // Keep only triangles whose texture colour lands nearer Bark than Leaf (plus the bias margin).
    Mesh BuildBranchMesh(Mesh src, Texture2D tex, out int kept, out int total)
    {
        var verts = src.vertices;
        var uv = src.uv;
        var keptIdx = new List<int>(src.triangles.Length);
        Vector3 bark = new(barkColor.r, barkColor.g, barkColor.b);
        Vector3 leaf = new(leafColor.r, leafColor.g, leafColor.b);
        kept = 0; total = 0;

        for (int s = 0; s < src.subMeshCount; s++)
        {
            var tris = src.GetTriangles(s);
            for (int t = 0; t < tris.Length; t += 3)
            {
                total++;
                Color c = TriColor(tex, uv, tris[t], tris[t + 1], tris[t + 2]);
                Vector3 cv = new(c.r, c.g, c.b);
                float dBark = Vector3.Distance(cv, bark);
                float dLeaf = Vector3.Distance(cv, leaf);
                if (dBark <= dLeaf + barkBias)
                {
                    keptIdx.Add(tris[t]); keptIdx.Add(tris[t + 1]); keptIdx.Add(tris[t + 2]);
                    kept++;
                }
            }
        }

        if (kept == 0) return null;
        var m = new Mesh { name = src.name + "_col", indexFormat = src.indexFormat };
        m.vertices = verts;
        m.SetTriangles(keptIdx, 0);
        m.RecalculateBounds();
        return m;
    }

    static Color TriColor(Texture2D tex, Vector2[] uv, int a, int b, int c)
    {
        Color ca = SampleWrapped(tex, uv[a]);
        Color cb = SampleWrapped(tex, uv[b]);
        Color cc = SampleWrapped(tex, uv[c]);
        return new Color((ca.r + cb.r + cc.r) / 3f, (ca.g + cb.g + cc.g) / 3f, (ca.b + cb.b + cc.b) / 3f);
    }

    static Color SampleWrapped(Texture2D tex, Vector2 uv)
    {
        float u = uv.x - Mathf.Floor(uv.x);   // wrap tiled UVs into 0..1
        float v = uv.y - Mathf.Floor(uv.y);
        return tex.GetPixelBilinear(u, v);
    }

    void Revert(MeshCollider mc)
    {
        if (s_originals.TryGetValue(mc, out var orig) && mc != null)
        {
            Undo.RecordObject(mc, "Revert Tree Collider");
            mc.sharedMesh = orig;
        }
        s_originals.Remove(mc);
        SceneView.RepaintAll();
    }

    // Write the collider change into the PREFAB, whichever way the tree is open:
    //  • Prefab Mode  → mark the stage dirty so it saves into the prefab asset.
    //  • Prefab asset selected in the Project window → save the asset directly.
    //  • Scene instance → push the override up to the source prefab (all instances inherit).
    static void Persist(MeshCollider mc)
    {
        var go = mc.gameObject;
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.IsPartOfPrefabContents(go))
        {
            EditorSceneManager.MarkSceneDirty(stage.scene);
            PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);   // persist even with Auto Save off
        }
        else if (PrefabUtility.IsPartOfPrefabAsset(go))
        {
            PrefabUtility.SavePrefabAsset(go.transform.root.gameObject);
        }
        else if (PrefabUtility.IsPartOfPrefabInstance(mc))
        {
            PrefabUtility.ApplyPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(go),
                                              InteractionMode.UserAction);
        }
    }

    static string MeshAssetPath(Mesh src)
    {
        string srcPath = AssetDatabase.GetAssetPath(src);
        string dir = string.IsNullOrEmpty(srcPath) ? "Assets/Prefabs/Props/Meshes" : System.IO.Path.GetDirectoryName(srcPath);
        return $"{dir.Replace('\\', '/')}/{src.name}_col.mesh";
    }

    static bool TryTarget(GameObject go, out MeshFilter mf, out MeshCollider mc, out Texture2D tex, out string why)
    {
        mf = null; mc = null; tex = null; why = "Select a tree in the scene.";
        if (go == null) return false;
        mf = go.GetComponent<MeshFilter>();
        mc = go.GetComponent<MeshCollider>();
        var mr = go.GetComponent<MeshRenderer>();
        if (mf == null || mf.sharedMesh == null) { why = "Selected object has no MeshFilter/mesh."; return false; }
        if (mc == null) { why = "Selected object has no MeshCollider to overwrite."; return false; }
        if (mr == null || mr.sharedMaterial == null) { why = "Selected object has no material to read the texture from."; return false; }
        tex = (mr.sharedMaterial.HasProperty("_BaseMap") ? mr.sharedMaterial.GetTexture("_BaseMap") : mr.sharedMaterial.mainTexture) as Texture2D;
        if (tex == null) { why = "Material has no readable base texture."; return false; }
        return true;
    }

    // GetPixelBilinear needs the texture flagged readable; flip it on temporarily, restore after.
    static bool EnsureReadable(Texture2D tex, out TextureImporter importer)
    {
        importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
        if (importer == null || importer.isReadable) return false;
        importer.isReadable = true;
        importer.SaveAndReimport();
        return true;
    }

    static void RestoreReadable(TextureImporter importer)
    {
        importer.isReadable = false;
        importer.SaveAndReimport();
    }
}
