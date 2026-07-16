using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Splits ONE multi-object FBX (a pack where every house/tree lives inside a single .fbx as a child
/// mesh — e.g. the Pxltgr "Fantasy Assets" pack) into <b>individual drop-ready prefabs</b>, one per
/// top-level child. This is the counterpart to <see cref="FbxKitPrefabBaker"/>: that bakes packs that
/// ship one FBX per prop and share ONE colormap atlas; this one slices a single FBX whose children each
/// carry their OWN material/texture, so it <b>preserves the imported materials</b> instead of overriding.
///
/// Output: <c>&lt;outDir&gt;/&lt;child name&gt;.prefab</c>. Each prefab is centred at the origin, keeps the
/// FBX's import scale/rotation, and gets a non-convex <see cref="MeshCollider"/> per solid mesh (static
/// scenery → cheap; see PERFORMANCE.md). Foliage meshes (name contains leaf/leaves/canopy/foliage) are left
/// collider-free so the player can walk UNDER tree canopies — the RO-style top-down behaviour we want.
/// Safe to re-run; overwrites prefabs in place so already-placed instances keep their links.
/// </summary>
public static class FbxPropExtractor
{
    /// <summary>One-click: slice the Pxltgr Fantasy Assets pack straight into Assets/1_Authored/Prefabs/Props.</summary>
    [MenuItem("Tools/unwritten/Props/Build Fantasy Assets (Pxltgr) → Props", priority = 502)]
    static void ExtractFantasyAssets() =>
        ExtractFbx("Assets/0_Raw/Pxltgr/fantasy-assets/source/Fantasy_Assets/Assets.fbx", "Assets/1_Authored/Prefabs/Props");

    /// <summary>Asset-agnostic: pick ANY single FBX that bundles many objects and split each top-level
    /// child into its own prefab under Assets/1_Authored/Prefabs/&lt;fbx name&gt;.</summary>
    [MenuItem("Tools/unwritten/Props/Split Multi-Object FBX…", priority = 503)]
    static void ExtractPicked()
    {
        string abs = EditorUtility.OpenFilePanel("Pick a multi-object FBX (must be inside Assets)", Application.dataPath, "fbx");
        if (string.IsNullOrEmpty(abs)) return;

        abs = abs.Replace('\\', '/');
        string root = Application.dataPath.Replace('\\', '/');   // <project>/Assets
        if (!abs.StartsWith(root))
        { EditorUtility.DisplayDialog("Split FBX", "That FBX isn't inside this project's Assets folder.\nMove it under Assets/1_Authored/Art and re-run.", "OK"); return; }

        string fbxPath = "Assets" + abs.Substring(root.Length);
        ExtractFbx(fbxPath, "Assets/1_Authored/Prefabs/" + Sanitize(Path.GetFileNameWithoutExtension(fbxPath)));
    }

    /// <summary>Slice <paramref name="fbxPath"/>'s top-level children into one prefab each under
    /// <paramref name="outDir"/>. Materials are kept as the FBX imported them.</summary>
    public static void ExtractFbx(string fbxPath, string outDir)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbx == null)
        {
            EditorUtility.DisplayDialog("Split FBX",
                $"Couldn't load an FBX at:\n{fbxPath}\n\nCheck the path / that Unity finished importing it.", "OK");
            return;
        }

        EnsureFolder(outDir);

        var root = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // Descend through pure wrapper nodes (Root → "Fantasy_Assets" → props) until we hit the node that
        // actually branches into the individual props. Stop before stepping INTO a single prop's sub-meshes.
        Transform group = root.transform;
        while (group.childCount == 1)
        {
            var only = group.GetChild(0);
            if (only.childCount == 0 || only.GetComponent<MeshFilter>() != null) break;   // that's a prop, not a wrapper
            group = only;
        }

        // Snapshot children first — we reparent/destroy them as we go.
        var children = new List<Transform>();
        foreach (Transform c in group) children.Add(c);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int built = 0, skipped = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < children.Count; i++)
            {
                var c = children[i];
                if (c == null) continue;

                // Skip empties (locators, lights, cameras) — only bake things that actually render.
                if (c.GetComponentInChildren<MeshRenderer>() == null) { skipped++; continue; }

                string name = UniqueName(Sanitize(c.name), used);
                EditorUtility.DisplayProgressBar("Splitting FBX into prefabs", name, (float)i / children.Count);

                // Promote the child to a root: SetParent(null, worldPositionStays:true) bakes the FBX
                // root's scale/rotation into the child, then we drop it at the origin.
                c.SetParent(null, true);
                c.localPosition = Vector3.zero;

                // Rocks/stones are lumpy: a non-convex (concave) MeshCollider lets the player capsule
                // wedge into the bumps & jitter, so bake them as a convex hull (a smooth walk-around
                // shape) — same trick the hollow props use. Solid scenery stays cheap non-convex.
                bool rock = IsRock(name);
                foreach (var mf in c.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                    if (IsFoliage(mf.gameObject.name)) continue;   // walk-under canopies → no collider
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    if (rock) mc.convex = true;
                }

                PrefabUtility.SaveAsPrefabAsset(c.gameObject, $"{outDir}/{name}.prefab");
                UnityEngine.Object.DestroyImmediate(c.gameObject);
                built++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[unwritten] Split {Path.GetFileName(fbxPath)} → {built} prefabs in {outDir} ({skipped} empty skipped).");
        EditorUtility.DisplayDialog("Split FBX — done",
            $"Extracted {built} prefabs ({skipped} empty nodes skipped).\n\nFolder: {outDir}\n\n" +
            "Materials were kept as the FBX imported them. If any prop is grey/pink, select the FBX → " +
            "Inspector → Materials tab → Extract Materials, then re-run (placed instances auto-update).\n\n" +
            "Foliage meshes were left collider-free so you can walk under canopies.", "OK");
    }

    static bool IsFoliage(string n)
    {
        n = n.ToLowerInvariant();
        return n.Contains("leaf") || n.Contains("leaves") || n.Contains("canopy") || n.Contains("foliage");
    }

    /// <summary>Rock/stone/boulder obstacles want a convex hull collider. Floor tiles (ground_*) are
    /// flat terrain, not obstacles, so they're excluded.</summary>
    static bool IsRock(string n)
    {
        n = n.ToLowerInvariant();
        if (n.StartsWith("ground_")) return false;
        return n.Contains("rock") || n.Contains("stone") || n.Contains("boulder");
    }

    static string Sanitize(string s)
    {
        foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        return s.Trim().Replace(' ', '_');
    }

    static string UniqueName(string baseName, HashSet<string> used)
    {
        if (string.IsNullOrEmpty(baseName)) baseName = "Prop";
        string name = baseName;
        for (int n = 1; !used.Add(name); n++) name = $"{baseName}_{n}";
        return name;
    }

    static void EnsureFolder(string projectPath)
    {
        if (AssetDatabase.IsValidFolder(projectPath)) return;
        string parent = Path.GetDirectoryName(projectPath).Replace('\\', '/');
        string leaf = Path.GetFileName(projectPath);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);   // recurse for nested dirs
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
