using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click "make a GLB drop-ready". A raw <c>.glb</c> dragged in (from the prop-gen pipeline or any
/// other source) imports as a <i>model</i> (not a prefab), often on a non-URP shader (→ magenta), with no
/// collider, an arbitrary export scale, and its pivot wherever the generator left it. This bakes it into
/// a proper prop prefab in <c>Assets/Prefabs/Props/</c>:
/// <list type="bullet">
/// <item>forces every material to <b>URP/Lit</b>, preserving the GLB's own baked texture + colour;</item>
/// <item>adds a <see cref="MeshCollider"/> per mesh (solid static scenery);</item>
/// <item>normalises scale so the prop's largest ground footprint ≈ <see cref="TargetFootprint"/> units;</item>
/// <item>re-seats the pivot to <b>bottom-centre</b> so it ground-snaps cleanly (world Y0 standard).</item>
/// </list>
/// Select the GLB model asset(s) in the Project window and run. Idempotent-ish: overwrites the prefab in
/// place so already-placed instances keep their link. Keeper (this is the standing GLB → prop pipeline).
/// </summary>
public static class GlbPropImporter
{
    const float TargetFootprint = 1.5f;   // tune via "Scale ×0.3" / Prop Palette afterwards if needed
    const string PropDir = "Assets/Prefabs/Props";
    const string MatDir = "Assets/Prefabs/Props/Materials";
    const string MeshDir = "Assets/Prefabs/Props/Meshes";

    [MenuItem("Tools/unwritten/Props/Import GLB Prop…", priority = 505)]
    static void Import()
    {
        var lit = FindLit();
        if (lit == null)
        { EditorUtility.DisplayDialog("Import GLB Prop", "URP/Lit shader not found — is this a URP project?", "OK"); return; }

        var models = Selection.objects
            .Select(o => AssetDatabase.GetAssetPath(o))
            .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase))
            .Distinct().ToArray();

        if (models.Length == 0)
        {
            EditorUtility.DisplayDialog("Import GLB Prop",
                "Select one or more .glb model assets in the Project window first, then re-run.", "OK");
            return;
        }

        EnsureFolders();
        int built = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var path in models)
                if (BakeOne(path, lit)) built++;
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[unwritten] Imported {built}/{models.Length} GLB prop(s) into {PropDir}.");
        EditorUtility.DisplayDialog("Import GLB Prop — done",
            $"Baked {built} drop-ready prop prefab(s) into:\n{PropDir}\n\n" +
            "Each is URP-materialed, collider'd, scaled to a sensible footprint, and pivoted at its base.\n" +
            "Drag the prefab into the scene (it ground-snaps), or place it with the Prop Palette.\n\n" +
            "Footprint not right? Use Tools ▸ unwritten ▸ Authoring ▸ Scale ×0.3 or the Prop Palette to tune.", "OK");
    }

    // The URP/Lit shader every baked prop is re-pointed onto (null if not a URP project).
    internal static Shader FindLit() => Shader.Find("Universal Render Pipeline/Lit");

    internal static bool BakeOne(string glbPath, Shader lit)
    {
        var glb = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
        if (glb == null) { Debug.LogWarning($"[unwritten] Couldn't load GLB at {glbPath}"); return false; }

        string name = Path.GetFileNameWithoutExtension(glbPath);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(glb);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.transform.position = Vector3.zero;
        inst.transform.rotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one;

        ForceUrpMaterials(inst, name, lit);
        AddColliders(inst);
        NormalizeScaleAndPivot(inst, name);

        var prefab = PrefabUtility.SaveAsPrefabAsset(inst, $"{PropDir}/{name}.prefab");
        Object.DestroyImmediate(inst);
        return prefab != null;
    }

    // Re-point every material to URP/Lit, carrying over the GLB's baked base texture + colour.
    static void ForceUrpMaterials(GameObject inst, string name, Shader lit)
    {
        int slot = 0;
        foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = mr.sharedMaterials;
            for (int k = 0; k < mats.Length; k++)
            {
                var src = mats[k];
                if (src != null && src.shader != null &&
                    src.shader.name.StartsWith("Universal Render Pipeline", System.StringComparison.Ordinal))
                    continue;   // already a URP material — leave it (e.g. glTFast output)

                var tex = src != null ? FindBaseTexture(src) : null;
                var col = src != null && src.HasProperty("_Color") ? src.color
                        : src != null && src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor")
                        : Color.white;

                var urp = new Material(lit);
                if (tex != null) { urp.SetTexture("_BaseMap", tex); urp.mainTexture = tex; }
                urp.SetColor("_BaseColor", col);
                urp.SetFloat("_Smoothness", 0f);   // flat stylized look, kill PBR sheen
                urp.SetFloat("_Metallic", 0f);

                string matPath = AssetDatabase.GenerateUniqueAssetPath($"{MatDir}/{name}_{slot++}.mat");
                AssetDatabase.CreateAsset(urp, matPath);
                mats[k] = urp;
            }
            mr.sharedMaterials = mats;
        }
    }

    static Texture FindBaseTexture(Material m)
    {
        foreach (var prop in new[] { "_BaseMap", "_MainTex", "_BaseColorMap", "baseColorTexture" })
            if (m.HasProperty(prop) && m.GetTexture(prop) != null) return m.GetTexture(prop);
        return m.mainTexture;
    }

    static void AddColliders(GameObject inst)
    {
        foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
    }

    // Scale so the larger ground (X/Z) extent ≈ TargetFootprint, and re-seat the pivot to bottom-centre
    // (base on Y0, centred in X/Z) for clean ground-snapping — WITHOUT adding a wrapper GameObject, so the
    // prop stays a single selectable node.
    // A flattened/optimized GLB imports as a SINGLE node (mesh on the root itself). We can't move the root's
    // own pivot by transform, so we bake the offset into a copy of that mesh's vertices (a new .mesh asset,
    // since the source mesh is a read-only sub-asset of the .glb). Meshes that live on CHILD transforms are
    // re-seated the cheap way — shift the child transform.
    // NOTE: bounds come from each MeshFilter.sharedMesh.bounds (the mesh asset's own bounds), NOT
    // Renderer.bounds — the latter is stale/half for a freshly-instantiated glTFast QUANTIZED mesh (the
    // dequant scale isn't reflected until a later update), which under-shifted the pivot. Called with the root
    // at position 0 / rotation identity / scale 1, so root-local == world (and == mesh-local for a root mesh).
    static void NormalizeScaleAndPivot(GameObject inst, string name)
    {
        var w2l = inst.transform.worldToLocalMatrix;
        bool has = false;
        Bounds b = default;
        foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;
            var m = w2l * mf.transform.localToWorldMatrix;   // mesh-local → root-local
            var mb = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = m.MultiplyPoint3x4(corner);
                if (!has) { b = new Bounds(p, Vector3.zero); has = true; }
                else b.Encapsulate(p);
            }
        }
        if (!has) return;

        var offset = new Vector3(b.center.x, b.min.y, b.center.z);
        if (offset.sqrMagnitude > 1e-8f)
        {
            int idx = 0;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.transform == inst.transform)   // mesh on the root: bake the offset into its vertices
                {
                    var shifted = BakePivotIntoMesh(mf.sharedMesh, offset, name, idx++);
                    if (shifted != null)
                    {
                        mf.sharedMesh = shifted;
                        var mc = mf.GetComponent<MeshCollider>();
                        if (mc != null) mc.sharedMesh = shifted;
                    }
                }
            }
            foreach (Transform child in inst.transform) child.localPosition -= offset;   // meshes under children
        }

        float footprint = Mathf.Max(b.size.x, b.size.z);
        if (footprint > 1e-4f)
            inst.transform.localScale = Vector3.one * (TargetFootprint / footprint);
    }

    // Save a copy of the mesh with every vertex shifted by -offset (base → Y0), so a mesh sitting on the prop
    // root gives the root a bottom-centre pivot without a wrapper. Returns null if the source can't be read.
    static Mesh BakePivotIntoMesh(Mesh src, Vector3 offset, string name, int idx)
    {
        if (src == null || !src.isReadable)
        {
            Debug.LogWarning($"[unwritten] '{name}' root mesh isn't readable — pivot left at mesh centre. " +
                             "Enable Read/Write on the GLB import, or the mesh must live on a child.");
            return null;
        }
        var m = Object.Instantiate(src);
        var verts = m.vertices;
        for (int i = 0; i < verts.Length; i++) verts[i] -= offset;
        m.vertices = verts;
        m.RecalculateBounds();
        m.name = $"{name}_based";
        string path = AssetDatabase.GenerateUniqueAssetPath($"{MeshDir}/{name}{(idx > 0 ? "_" + idx : "")}.mesh");
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    internal static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PropDir)) AssetDatabase.CreateFolder("Assets/Prefabs", "Props");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(PropDir, "Materials");
        if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder(PropDir, "Meshes");
    }
}
