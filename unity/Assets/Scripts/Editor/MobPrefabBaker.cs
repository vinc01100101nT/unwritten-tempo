using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// Bakes every <see cref="MonsterDef"/> in the catalog into a prefab and marks it ADDRESSABLE so the game
/// can summon mobs from data during Play — the builder (<see cref="RigBuilder25D"/>) is editor-only and
/// can't run in a build. Each prefab is built the normal way, <c>RigBuilder25D.BuildMonster(def.ToArchetype())</c>,
/// plus a <see cref="BossBanner"/> for elites and shadows, which gives them their display name.
/// <see cref="MobSpawner"/> loads them by address.
///
/// Output: <c>Assets/2_Generated/MobPrefabs/Mobs/&lt;def name&gt;.prefab</c> (NOT under Resources — Addressables
/// bundles them), each entered in the <b>Mobs</b> Addressable group under address <c>Mobs/&lt;def name&gt;</c>.
/// Tool OUTPUT, safe to delete and regenerate. Re-runnable; overwrites in place.
/// </summary>
public static class MobPrefabBaker
{
    const string OutDir = "Assets/2_Generated/MobPrefabs/Mobs";
    const string GroupName = "Mobs";
    const string AddressPrefix = "Mobs/";

    [MenuItem("Tools/unwritten/Mobs & NPCs/Bake Mob Prefabs", priority = 312)]
    static void BakeAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Bake Mob Prefabs", "Exit Play mode first, then re-run.", "OK"); return; }
        string result = BakeAllCore();
        EditorUtility.DisplayDialog("Bake Mob Prefabs", result, "OK");
    }

    /// <summary>The bake itself, with no dialog — safe to run through the live editor link. Returns what happened, in
    /// the words the menu's dialog shows.</summary>
    public static string BakeAllCore()
    {
        MonsterCatalog.Reload();
        var defs = MonsterCatalog.All;
        if (defs == null || defs.Count == 0) return "No MonsterDef assets found under Resources/Monsters.";

        var settings = AddressableAssetSettingsDefaultObject.Settings
                       ?? AddressableAssetSettingsDefaultObject.GetSettings(true);   // creates default settings on first use
        if (settings == null)
            return "Couldn't create Addressable settings. Open Window ▸ Asset Management ▸ Addressables ▸ Groups once, then re-run.";

        var group = settings.FindGroup(GroupName)
                    ?? settings.CreateGroup(GroupName, false, false, true, null,
                                            typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));

        EnsureFolder(OutDir);
        var billboardMat = EnsureBillboardMaterial();   // a PERSISTED .mat — a prefab can't reference RigBuilder's in-memory material (→ fileID:0 = pink)

        int baked = 0;
        try
        {
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def == null || string.IsNullOrWhiteSpace(def.name)) continue;
                EditorUtility.DisplayProgressBar("Bake Mob Prefabs", def.name, (float)i / defs.Count);

                var go = RigBuilder25D.BuildMonster(def.ToArchetype(), Vector3.zero);
                // An elite or a shadow carries its display name on a BossBanner.
                if (def.kind != CreatureKind.Normal)
                    go.AddComponent<BossBanner>().bossName = string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName;

                // Repoint the billboard renderer at the saved .mat asset so the prefab keeps a real material
                // reference (RigBuilder assigns a runtime `new Material` that serializes to null in a prefab → pink).
                if (billboardMat != null)
                { var msr = go.GetComponent<SpriteRenderer>(); if (msr != null) msr.sharedMaterial = billboardMat; }

                string path = $"{OutDir}/{def.name}.prefab";
                PrefabUtility.SaveAsPrefabAsset(go, path);
                Object.DestroyImmediate(go);

                string guid = AssetDatabase.AssetPathToGUID(path);
                var entry = settings.CreateOrMoveEntry(guid, group);
                entry.address = AddressPrefix + def.name;
                baked++;
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[unwritten] Baked {baked} mob prefab(s) → {OutDir} (Addressable group '{GroupName}')");
        return $"Baked {baked} mob prefab(s) into the '{GroupName}' Addressable group (address Mobs/<name>).\n\n" +
               "Summon them in Play with /summon <count> <normal|elite|shadow|creature>.";
    }

    const string MatPath = "Assets/2_Generated/MobPrefabs/BillboardMob.mat";

    // The billboard sprites' shared material, as a PERSISTED .mat asset (created once, reused after). A prefab
    // can only reference a material that lives on disk; RigBuilder's runtime `new Material` serializes to
    // fileID:0 in a prefab → magenta "missing material". This gives the baked mobs a real, GUID-referenced mat.
    static Material EnsureBillboardMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (existing != null) return existing;

        var shader = Shader.Find("Unwritten/BillboardLit");
        if (shader == null)
        { Debug.LogWarning("[unwritten] Shader 'Unwritten/BillboardLit' not found — baked mobs won't get the lit material."); return null; }

        var mat = new Material(shader) { name = "BillboardMob" };
        AssetDatabase.CreateAsset(mat, MatPath);
        return mat;
    }

    // Create a project folder (and any missing parents) so AssetDatabase.CreateFolder never fails on a gap.
    static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath)) return;
        string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
    }
}
