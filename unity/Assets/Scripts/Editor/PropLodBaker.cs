using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// FAR PROPS ARE DRAWN SIMPLER, NEVER LEFT OUT. Gives every palette prop that has no level-of-detail switch yet two
/// simpler copies of its meshes — about <see cref="Level1Share"/> and <see cref="Level2Share"/> of its triangles, made by
/// <see cref="MeshDecimation"/> — and an <see cref="LODGroup"/> that switches to them as the prop gets smaller on screen,
/// on the one switching table (<see cref="LodGroupWirer.Levels"/>), whose simplest level is never hidden.
///
/// <para>The full prop is untouched and is what you see up close. Each simpler copy is a sibling of the renderer it
/// copies — same parent, same place, same materials, same static flags, no collider — so a plant's wind still bends it
/// from the same pivot and the prop is still solid exactly once. The meshes are written to
/// <see cref="OutputDir"/> (tool output: safe to delete and bake again).</para>
///
/// <para>Left alone: a prop that already switches; a family with its own hand-made <c>_LODn</c> copies (that is
/// <see cref="LodGroupWirer"/>'s); and a prop under <see cref="MinTriangles"/>, where a simpler copy saves nothing worth
/// a switch. Run through the live editor link: <c>PropLodBaker.BakeCore(null)</c> for every prop, or with a list of
/// prefab paths for some.</para>
/// </summary>
public static class PropLodBaker
{
    const string PropsDir = "Assets/1_Authored/Prefabs/Props";
    public const string OutputDir = "Assets/2_Generated/PropLODs";

    /// <summary>A prop with fewer triangles than this keeps one level: there is nothing worth simplifying.</summary>
    public const int MinTriangles = 500;

    /// <summary>The share of its triangles each simpler level keeps. Judged 2026-10-11 from pictures of House_A at the
    /// exact distance each level takes over, from the game camera's angle: 40% could not be told from the full house;
    /// 15% showed jagged roof edges and a broken chimney at a distance a house beside the player is seen from.</summary>
    public const float Level1Share = 0.4f, Level2Share = 0.25f;

    // KEEPS THE OUTLINE, LETS THE TEXTURE SEAMS GO. Measured 2026-10-11 on House_A (72,945 triangles, 84,676 corners —
    // almost every triangle its own patch of texture): with its seams kept it could only come down to 69,343, with
    // them let go to 10,940. A seam that slips is a smear of colour too small to see at the distance these levels are
    // drawn at; the full prop, untouched, is what is seen up close.
    static readonly MeshDecimation.Options FarOptions = new MeshDecimation.Options
        { preserveBorders = true, preserveUvSeams = false, smartLink = true };

    // A mesh made simpler once per run is reused by every prop that draws it.
    static readonly Dictionary<(Mesh, int), Mesh> made = new Dictionary<(Mesh, int), Mesh>();

    /// <summary>Bake the props at <paramref name="prefabPaths"/>, or every palette prop when null. Returns a report.</summary>
    public static string BakeCore(string[] prefabPaths)
    {
        made.Clear();
        var paths = new List<string>();
        if (prefabPaths != null) paths.AddRange(prefabPaths);
        else foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { PropsDir })) paths.Add(AssetDatabase.GUIDToAssetPath(g));

        EnsureFolder(OutputDir);
        var report = new StringBuilder();
        int baked = 0, skipped = 0;
        long before = 0, far = 0;
        foreach (var path in paths)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (IsFamilyMember(name)) { skipped++; continue; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponentInChildren<LODGroup>(true) != null) { skipped++; continue; }
                var originals = new List<Renderer>();
                var filters = new List<MeshFilter>();
                int tris = 0;
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    if (!mr.enabled || mf == null || mf.sharedMesh == null) continue;
                    originals.Add(mr);
                    filters.Add(mf);
                    tris += MeshDecimation.Tris(mf.sharedMesh);
                }
                if (originals.Count == 0 || tris < MinTriangles) { skipped++; continue; }

                var perLevel = new List<Renderer[]> { originals.ToArray() };
                int lastTris = tris;
                foreach (var (level, share) in new[] { (1, Level1Share), (2, Level2Share) })
                {
                    var renderers = new Renderer[originals.Count];
                    lastTris = 0;
                    for (int i = 0; i < originals.Count; i++)
                    {
                        var simpler = Simpler(filters[i].sharedMesh, level, share, name);
                        renderers[i] = simpler != null ? AddSibling((MeshRenderer)originals[i], simpler, level) : originals[i];
                        lastTris += MeshDecimation.Tris(simpler != null ? simpler : filters[i].sharedMesh);
                    }
                    perLevel.Add(renderers);
                }

                var group = root.AddComponent<LODGroup>();
                group.SetLODs(LodGroupWirer.Levels(perLevel));
                group.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                report.AppendLine(name + ": " + tris + " → " + lastTris + " triangles at its simplest");
                before += tris;
                far += lastTris;
                baked++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        AssetDatabase.SaveAssets();
        report.Insert(0, "Far-prop levels: " + baked + " props baked (" + before + " → " + far + " triangles at their simplest), "
                         + skipped + " left alone.\n");
        Debug.Log("[unwritten] " + report);
        return report.ToString();
    }

    // "Stone1_LOD0", "Stone1_LOD2": one of a family with hand-made copies, which LodGroupWirer wires.
    static bool IsFamilyMember(string name)
    {
        int at = name.LastIndexOf("_LOD", System.StringComparison.Ordinal);
        return at >= 0 && int.TryParse(name.Substring(at + 4), out _);
    }

    static Mesh Simpler(Mesh src, int level, float share, string propName)
    {
        if (made.TryGetValue((src, level), out var known)) return known;
        int target = Mathf.Max(12, Mathf.RoundToInt(MeshDecimation.Tris(src) * share));
        var mesh = MeshDecimation.Decimate(src, target, FarOptions);
        if (mesh != null)
        {
            mesh.name = src.name + "_LOD" + level;
            string dir = OutputDir + "/" + propName;
            EnsureFolder(dir);
            // editor-write-ok: run by hand through the live editor link, at the user's request, never on its own.
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(dir + "/" + Safe(mesh.name) + ".asset"));
        }
        made[(src, level)] = mesh;
        return mesh;
    }

    static MeshRenderer AddSibling(MeshRenderer original, Mesh mesh, int level)
    {
        var src = original.transform;
        var go = new GameObject(src.name + "_LOD" + level) { layer = src.gameObject.layer };
        if (src.parent != null)
        {
            go.transform.SetParent(src.parent, false);
            go.transform.localPosition = src.localPosition;
            go.transform.localRotation = src.localRotation;
            go.transform.localScale = src.localScale;
            go.transform.SetSiblingIndex(src.GetSiblingIndex() + 1);
        }
        else
        {
            // THE PREFAB'S OWN TOP OBJECT draws: the copy goes UNDER it, standing exactly where it does. With no parent
            // the copy would be left outside the prefab, in whatever scene is open in the editor.
            go.transform.SetParent(src, false);
        }
        GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(src.gameObject));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = original.sharedMaterials;
        mr.shadowCastingMode = original.shadowCastingMode;
        mr.receiveShadows = original.receiveShadows;
        mr.lightProbeUsage = original.lightProbeUsage;
        mr.reflectionProbeUsage = original.reflectionProbeUsage;
        return mr;
    }

    static string Safe(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
