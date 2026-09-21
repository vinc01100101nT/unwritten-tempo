using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Plugs the decimated copies of a prop into the copy that is actually placed. The decimator writes a family of
/// prefabs beside each other — <c>Stone1_LOD0</c>, <c>Stone1_LOD1</c>, <c>Stone1_LOD2</c> — and only the
/// <c>_LOD0</c> one is ever placed in a map, so until this runs the simpler copies are never drawn at any
/// distance. For every family under the outdoor palette this gives the <c>_LOD0</c> prefab one child per
/// simpler copy (its mesh and materials, no collider — the placed prefab already has one) and a
/// <see cref="LODGroup"/> that switches between them, with its bounds worked out by Unity from the renderers.
///
/// <para>Idempotent: a family whose placed prefab already carries a <see cref="LODGroup"/> is left alone, so it
/// is safe to re-run after the decimator has produced new copies. Nothing placed in a scene changes identity —
/// the placed prefab keeps its file and its root, and its instances inherit the children.</para>
///
/// <para>Dialog-free by design, so it can be run through the live editor link:
/// <c>LodGroupWirer.WireAllCore()</c>.</para>
/// </summary>
public static class LodGroupWirer
{
    const string Folder = "Assets/1_Authored/Prefabs/Props/Outdoor";

    // The distance at which each copy takes over, as the fraction of the screen the prop's bounds still cover.
    // The first three are the values Unity itself gives a freshly added LODGroup; the fourth is only reached by
    // the one family (Tavern) that has four copies.
    static readonly float[] ScreenHeights = { 0.6f, 0.3f, 0.1f, 0.05f };

    public static string WireAllCore()
    {
        var families = new SortedDictionary<string, SortedDictionary<int, string>>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var name = Path.GetFileNameWithoutExtension(path);
            int at = name.LastIndexOf("_LOD", System.StringComparison.Ordinal);
            if (at < 0 || !int.TryParse(name.Substring(at + 4), out int level)) continue;
            var family = name.Substring(0, at);
            if (!families.TryGetValue(family, out var levels)) families[family] = levels = new SortedDictionary<int, string>();
            levels[level] = path;
        }

        var report = new StringBuilder();
        int wired = 0, skipped = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var kv in families)
            {
                var family = kv.Key;
                var levels = kv.Value;
                if (!levels.ContainsKey(0) || levels.Count < 2)
                {
                    report.AppendLine(family + ": no LOD0 or only one copy — skipped");
                    skipped++;
                    continue;
                }

                var root = PrefabUtility.LoadPrefabContents(levels[0]);
                try
                {
                    if (root.GetComponent<LODGroup>() != null)
                    {
                        report.AppendLine(family + ": already wired");
                        skipped++;
                        continue;
                    }

                    var rootRenderer = root.GetComponent<Renderer>();
                    if (rootRenderer == null)
                    {
                        report.AppendLine(family + ": LOD0 has no renderer — skipped");
                        skipped++;
                        continue;
                    }

                    var lods = new List<LOD>();
                    int index = 0;
                    foreach (var lv in levels)
                    {
                        Renderer r;
                        if (lv.Key == 0) r = rootRenderer;
                        else
                        {
                            r = AddCopyAsChild(root.transform, lv.Value, "LOD" + lv.Key);
                            if (r == null)
                            {
                                report.AppendLine(family + ": " + Path.GetFileName(lv.Value) + " has no mesh — skipped that level");
                                continue;
                            }
                        }
                        float height = index < ScreenHeights.Length ? ScreenHeights[index] : ScreenHeights[ScreenHeights.Length - 1] * 0.5f;
                        lods.Add(new LOD(height, new[] { r }));
                        index++;
                    }

                    var group = root.AddComponent<LODGroup>();
                    group.SetLODs(lods.ToArray());
                    group.RecalculateBounds();
                    PrefabUtility.SaveAsPrefabAsset(root, levels[0]);
                    report.AppendLine(family + ": " + lods.Count + " levels");
                    wired++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        AssetDatabase.SaveAssets();

        report.Insert(0, "LOD groups: " + wired + " wired, " + skipped + " skipped.\n");
        Debug.Log("[unwritten] " + report);
        return report.ToString();
    }

    /// <summary>A child carrying only what the simpler copy DRAWS — its mesh and materials. Its collider is
    /// deliberately not copied: the placed prefab already blocks the way, and a second, simpler collider would
    /// make a prop solid twice.</summary>
    static Renderer AddCopyAsChild(Transform parent, string prefabPath, string childName)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var filter = source != null ? source.GetComponent<MeshFilter>() : null;
        var renderer = source != null ? source.GetComponent<MeshRenderer>() : null;
        if (filter == null || filter.sharedMesh == null || renderer == null) return null;

        var child = new GameObject(childName);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = source.transform.localPosition;
        child.transform.localRotation = source.transform.localRotation;
        child.transform.localScale = source.transform.localScale;
        GameObjectUtility.SetStaticEditorFlags(child, GameObjectUtility.GetStaticEditorFlags(parent.gameObject));

        child.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        var mr = child.AddComponent<MeshRenderer>();
        mr.sharedMaterials = renderer.sharedMaterials;
        mr.shadowCastingMode = renderer.shadowCastingMode;
        mr.receiveShadows = renderer.receiveShadows;
        return mr;
    }
}
