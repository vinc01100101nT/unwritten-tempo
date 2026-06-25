// QUICKFIX 2026-06-24 — one-shot migration: stamp the new melee EffectiveRange default onto every already-baked
// Player3DAttacker (code defaults never rewrite serialized instances). Delete after the player/mob assets have
// all been re-saved with range == TargetRange and no stale 1.4 instances remain.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sets <see cref="Player3DAttacker.range"/> (the FULL-SIZE melee reach; live reach = this × UnitScale.MeleeReachFactor)
/// to <see cref="TargetRange"/> on EVERY melee unit in the project — all prefab assets AND every scene under Assets —
/// in one click. Use after changing the code default, since a C# default only applies to newly added components and
/// never rewrites a value already serialized on a baked prefab/scene instance.
/// </summary>
public static class SetMeleeRange
{
    const float TargetRange = 0.5f;

    [MenuItem("Tools/unwritten/Combat/Set Melee Attack Range (all units)")]
    public static void Apply()
    {
        if (!EditorUtility.DisplayDialog("Set Melee Attack Range",
            $"Set Player3DAttacker.range = {TargetRange} on EVERY prefab and scene in the project?\n\n" +
            "This re-saves matching assets. Make sure your work is committed first.",
            "Apply", "Cancel"))
            return;

        int prefabs = ApplyToPrefabs();
        int scenes  = ApplyToScenes(out int sceneComponents);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg = $"Melee range set to {TargetRange}.\n\n" +
                     $"Prefab components updated: {prefabs}\n" +
                     $"Scene components updated: {sceneComponents} (across {scenes} scene(s))";
        Debug.Log("[unwritten] " + msg.Replace("\n\n", " — ").Replace("\n", "; "));
        EditorUtility.DisplayDialog("Set Melee Attack Range", msg, "OK");
    }

    static int ApplyToPrefabs()
    {
        int changed = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool dirty = false;
                foreach (var atk in root.GetComponentsInChildren<Player3DAttacker>(true))
                    if (!Mathf.Approximately(atk.range, TargetRange)) { atk.range = TargetRange; dirty = true; changed++; }
                if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return changed;
    }

    static int ApplyToScenes(out int components)
    {
        components = 0;
        // Remember what's open so we can restore the editor to where the user left it.
        var setup = EditorSceneManager.GetSceneManagerSetup();
        int scenesTouched = 0;

        var scenePaths = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Scene"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/")) scenePaths.Add(path);
        }

        foreach (string path in scenePaths)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            bool dirty = false;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var atk in root.GetComponentsInChildren<Player3DAttacker>(true))
                    if (!Mathf.Approximately(atk.range, TargetRange)) { atk.range = TargetRange; dirty = true; components++; }
            if (dirty)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                scenesTouched++;
            }
        }

        if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
        return scenesTouched;
    }
}
