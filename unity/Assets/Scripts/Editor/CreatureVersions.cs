using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A TAINTED OR BLIGHTED CREATURE IS A MIRROR OF ITS ORIGINAL (<see cref="MonsterDef.versionOf"/>): everything but its
/// own id, name, multiplier and material is copied from the original, so retuning the original retunes every version
/// of it and the two can never drift. Run by <see cref="DerivedItemValues"/> inside the user's own save of a creature
/// file, before the material prices that read the copied level. Every field the original gains is copied with no change
/// here, because the whole file is copied and only the version's own four are put back.
/// </summary>
public static class CreatureVersions
{
    /// <summary>Copy every original onto its versions. With <paramref name="write"/> the versions are changed and their
    /// paths added to <paramref name="changedPaths"/>; without, only counted. Returns how many differ.</summary>
    public static int Apply(bool write, StringBuilder said, List<string> changedPaths)
    {
        int n = 0;
        foreach (var d in MonsterCatalog.All)
        {
            if (d == null || d.versionOf == null || d.versionOf == d) continue;
            string before = EditorJsonUtility.ToJson(d);
            var copy = Object.Instantiate(d);
            try
            {
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(d.versionOf), copy);
                copy.name = d.name;
                copy.id = d.id;
                copy.displayName = d.displayName;
                copy.multiplier = d.multiplier;
                copy.material = d.material;
                copy.versionOf = d.versionOf;
                string after = EditorJsonUtility.ToJson(copy);
                if (after == before) continue;
                n++;
                said?.Append(d.displayName).Append(": copied from ").Append(d.versionOf.displayName).Append('\n');
                if (!write) continue;
                // editor-write-ok: runs only inside the user's own save of a creature file, or a Sync(true) they ran.
                EditorJsonUtility.FromJsonOverwrite(after, d);
                EditorUtility.SetDirty(d);
                changedPaths?.Add(AssetDatabase.GetAssetPath(d));
            }
            finally { Object.DestroyImmediate(copy); }
        }
        return n;
    }
}
