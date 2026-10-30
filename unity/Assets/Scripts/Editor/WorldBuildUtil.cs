using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Neutral, pack-agnostic helpers shared by the world toolchain (<see cref="RecipeGenerator"/>,
/// <see cref="RegionBuilder"/>,
/// <see cref="SystemsSceneBuilder"/>, <see cref="RigBuilder25D"/>). These used to live on
/// <c>NatureStageBuilder</c>, which was tied to the (now-removed) LowPolySimpleNature pack — they were
/// extracted here so the builders no longer depend on that pack to reach the utilities.
///
/// Nothing here references any art pack: it's all layer / collider / scene-registration plumbing.
/// </summary>
public static class WorldBuildUtil
{
    /// <summary>Find or create the user layer named "Ground" (movement + pathfinding rays hit it). Returns its
    /// index, or -1 if all user slots are taken.</summary>
    public static int EnsureGroundLayer() => EnsureLayer("Ground");

    /// <summary>Find or create a user layer with the given name (user slots 8..31). Returns its index, or -1
    /// if all 24 user slots are taken or the TagManager can't be read.</summary>
    public static int EnsureLayer(string name)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return -1;
        var so = new SerializedObject(assets[0]);
        var layers = so.FindProperty("layers");
        if (layers == null) return -1;

        for (int i = 8; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;
        for (int i = 8; i < layers.arraySize; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue)) { p.stringValue = name; so.ApplyModifiedProperties(); return i; }
        }
        return -1;
    }

    /// <summary>Set a GameObject and all its descendants to a layer.</summary>
    public static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }

    /// <summary>Add a MeshCollider to any MeshFilter that doesn't already have a collider (guards raw prefabs).</summary>
    public static void EnsureMeshColliders(GameObject go)
    {
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
    }

    /// <summary>Drop a point onto the ground surface below 'p' (else keep p) — used to seat spawns/portals.</summary>
    public static Vector3 SurfacePoint(Vector3 p, int groundMask)
    {
        if (Physics.Raycast(new Vector3(p.x, p.y + 60f, p.z), Vector3.down, out var hit, 200f, groundMask, QueryTriggerInteraction.Ignore))
            return hit.point;
        return p;
    }

    /// <summary>Register a scene in Build Settings (so <see cref="MapManager"/> can load it by name). Idempotent.</summary>
    public static void RegisterInBuildSettings(string scenePath)
    {
        foreach (var s in EditorBuildSettings.scenes)
            if (s.path == scenePath) return;   // already listed
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
        { new EditorBuildSettingsScene(scenePath, true) };
        EditorBuildSettings.scenes = list.ToArray();
    }

    /// <summary>Saves <paramref name="mesh"/> at <paramref name="path"/>. A mesh already saved there is overwritten in
    /// place, keeping its identity, so every prefab pointing at it keeps pointing at it. Returns the saved mesh.</summary>
    public static Mesh SaveMeshInPlace(Mesh mesh, string path)
    {
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        // Every channel written afresh through the mesh's own setters. MEASURED 2026-10-26: copied over wholesale
        // (CopySerialized, even with a re-import or MarkModified after it), the editor kept DRAWING the old vertex
        // colours — a soil band an earlier build painted — while the mesh itself held the new ones; setting a
        // channel again is what hands the new data to the graphics card.
        old.Clear();
        old.name = mesh.name;
        old.indexFormat = mesh.indexFormat;
        int count = mesh.vertexCount;
        old.SetVertices(mesh.vertices);
        if (mesh.normals.Length == count) old.SetNormals(mesh.normals);
        if (mesh.tangents.Length == count) old.SetTangents(mesh.tangents);
        if (mesh.colors.Length == count) old.SetColors(mesh.colors);
        var uv = new List<Vector2>();
        for (int ch = 0; ch < 2; ch++) { uv.Clear(); mesh.GetUVs(ch, uv); if (uv.Count == count) old.SetUVs(ch, uv); }
        // The further channels a town's tile ground carries (GroundTileArray: its corners' tiles and tilings, the world's rim),
        // each kept at the size it was written.
        for (int ch = 2; ch < 8; ch++)
        {
            int dim = mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + ch)
                ? mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord0 + ch) : 0;
            if (dim == 2) { var l = new List<Vector2>(); mesh.GetUVs(ch, l); old.SetUVs(ch, l); }
            else if (dim == 3) { var l = new List<Vector3>(); mesh.GetUVs(ch, l); old.SetUVs(ch, l); }
            else if (dim == 4) { var l = new List<Vector4>(); mesh.GetUVs(ch, l); old.SetUVs(ch, l); }
        }
        old.subMeshCount = mesh.subMeshCount;
        for (int sm = 0; sm < mesh.subMeshCount; sm++) old.SetTriangles(mesh.GetTriangles(sm), sm);
        old.RecalculateBounds();
        EditorUtility.SetDirty(old);
        AssetDatabase.SaveAssetIfDirty(old);
        return old;
    }
}
