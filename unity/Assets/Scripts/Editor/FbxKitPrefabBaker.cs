using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns raw kit FBX meshes into <b>drop-ready prefabs</b> for the hand-place / auto-generate
/// 2.5D flows. A bare FBX dragged into a scene imports untextured and with no collider; each baked
/// prefab instead has the kit colormap material applied and a <see cref="MeshCollider"/> per mesh,
/// so the player collides with it and the 3D town baker can instantiate it directly.
///
/// Output: <c>Assets/Prefabs/&lt;PrefabFolder&gt;/&lt;name&gt;.prefab</c> + a shared material asset per kit.
/// Characters (<c>character-*</c>) are skipped — players/mobs/NPCs are 2D billboards here (see the
/// 2.5D direction decision). Safe to re-run; overwrites prefabs in place so placed instances keep links.
/// </summary>
public static class FbxKitPrefabBaker
{
    /// <summary>Asset-agnostic baker: pick ANY kit's FBX folder (e.g. a downloaded prop pack's
    /// <c>FBX format</c> folder placed under Assets/Art/…) and turn every FBX in it into drop-ready
    /// house/prop prefabs. Auto-detects the colormap atlas (Textures/colormap.png, else the first .png in
    /// the folder or its Textures subfolder). Output → Assets/Prefabs/&lt;folder name&gt;.</summary>
    [MenuItem("Tools/unwritten/Prefabs & Art/Build From Folder…", priority = 501)]
    static void BuildFromFolder()
    {
        string abs = EditorUtility.OpenFolderPanel("Pick the kit's FBX folder (must be inside Assets)", Application.dataPath, "");
        if (string.IsNullOrEmpty(abs)) return;

        abs = abs.Replace('\\', '/');
        string root = Application.dataPath.Replace('\\', '/');   // <project>/Assets
        if (!abs.StartsWith(root))
        { EditorUtility.DisplayDialog("Build Prefabs", "That folder isn't inside this project's Assets folder.\nMove the kit under Assets/Art and re-run.", "OK"); return; }

        string kitDir = "Assets" + abs.Substring(root.Length);     // project-relative
        string leaf = Path.GetFileName(kitDir.TrimEnd('/'));
        string prefabFolder = SanitizeFolderName(leaf);

        if (Directory.GetFiles(kitDir, "*.fbx", SearchOption.TopDirectoryOnly).Length == 0)
        { EditorUtility.DisplayDialog("Build Prefabs", $"No .fbx files directly in:\n{kitDir}\n\nPoint at the pack's 'FBX format' folder (where the .fbx files live).", "OK"); return; }

        BakeKit(leaf, kitDir, prefabFolder, DetectColormap(kitDir));
    }

    // Find a texture atlas for a kit that doesn't follow the Textures/colormap.png convention.
    static string DetectColormap(string kitDir)
    {
        string colormap = kitDir + "/Textures/colormap.png";
        if (File.Exists(colormap)) return colormap;
        foreach (var dir in new[] { kitDir, kitDir + "/Textures" })
        {
            if (!Directory.Exists(dir)) continue;
            var png = Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (png != null) return png.Replace('\\', '/');
        }
        return null;   // BakeKit warns + bakes untextured
    }

    static string SanitizeFolderName(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    /// <summary>Bake every FBX in <paramref name="kitDir"/> into drop-ready prefabs under
    /// Assets/Prefabs/<paramref name="prefabFolder"/>, sharing one colormap material.
    /// <paramref name="colormapPath"/> overrides the texture location (default =
    /// <c>&lt;kitDir&gt;/Textures/colormap.png</c>) for packs that ship their atlas elsewhere.</summary>
    public static void BakeKit(string kitName, string kitDir, string prefabFolder, string colormapPath = null)
    {
        if (!Directory.Exists(kitDir))
        {
            EditorUtility.DisplayDialog("Build Prefabs",
                $"Couldn't find the {kitName} kit at:\n{kitDir}\n\nPut the pack's 'FBX format' folder there and re-run.", "OK");
            return;
        }

        string colormap = colormapPath ?? kitDir + "/Textures/colormap.png";
        string matPath = kitDir + "/" + prefabFolder + ".mat";
        string prefabDir = "Assets/Prefabs/" + prefabFolder;

        var mat = EnsureMaterial(colormap, matPath);
        EnsurePrefabFolder(prefabFolder);

        var fbxPaths = Directory.GetFiles(kitDir, "*.fbx", SearchOption.TopDirectoryOnly)
                                .Select(p => p.Replace('\\', '/'))
                                .Where(p => !Path.GetFileNameWithoutExtension(p)
                                             .StartsWith("character-", System.StringComparison.OrdinalIgnoreCase))
                                .OrderBy(p => p)
                                .ToArray();

        int built = 0, skipped = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < fbxPaths.Length; i++)
            {
                string fbxPath = fbxPaths[i];
                string name = Path.GetFileNameWithoutExtension(fbxPath);
                EditorUtility.DisplayProgressBar($"Baking {kitName} prefabs", name, (float)i / fbxPaths.Length);

                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (fbx == null) { skipped++; continue; }

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
                {
                    var mats = mr.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++) mats[k] = mat;
                    mr.sharedMaterials = mats;
                }

                // Solid collision per mesh (static scenery → non-convex MeshCollider is correct + cheap).
                // Hollow-ring props get a convex hull so you can't stand inside them (matches the A* footprint).
                bool hollow = name.Contains("fountain") || name.Contains("well") || name.Contains("ring");
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                    {
                        var mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        if (hollow) mc.convex = true;
                    }

                PrefabUtility.SaveAsPrefabAsset(inst, $"{prefabDir}/{name}.prefab");
                Object.DestroyImmediate(inst);
                built++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[unwritten] {kitName} prefabs baked: {built} into {prefabDir} ({skipped} skipped).");
        EditorUtility.DisplayDialog($"{kitName} Prefabs — done",
            $"Baked {built} drop-ready prefabs ({skipped} skipped).\n\n" +
            $"Folder: {prefabDir}\nMaterial: {matPath}\n\n" +
            "Each has the kit colormap + a MeshCollider. Drag them in by hand, or let the 3D town " +
            "baker place them.", "OK");
    }

    static Material EnsureMaterial(string colormap, string matPath)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(colormap);
        if (tex != null) { mat.SetTexture("_BaseMap", tex); mat.mainTexture = tex; }
        else Debug.LogWarning("[unwritten] colormap not found at " + colormap + " — prefabs will be untextured.");
        mat.SetFloat("_Smoothness", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void EnsurePrefabFolder(string prefabFolder)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/" + prefabFolder))
            AssetDatabase.CreateFolder("Assets/Prefabs", prefabFolder);
    }
}
