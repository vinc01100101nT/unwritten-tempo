using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click setup for the ambient-life layer: refreshes the shared <see cref="CharacterSpec"/>'s CAST (creating the
/// asset if it doesn't exist yet), then drops an <see cref="AmbientLife"/> director into the open scene and points its
/// ground mask at the Ground layer. After running it, just press Play — wandering live-3D townsfolk appear and chatter.
///
/// <para>The cast lives on the spec, not on the director, because the SAME models feed the shadow mobs. It is split in
/// two: CIVILIANS (townsfolk) and FIGHTERS — soldiers, knights, goblins and zombies — which is the pool a Shadow of the
/// Past draws from. Animals (Cow/Pug), the untextured BaseCharacter, and accessory-only meshes (hats/hair/helmets) are
/// in neither: they'd walk wrong on the human controller or aren't full bodies.</para>
///
/// Idempotent: re-running re-uses the existing spec and director (re-wiring the cast) instead of stacking duplicates.
/// </summary>
public static class AmbientLifeTools
{
    const string ModelsDir = "Assets/1_Authored/Art/Characters/Models";
    const string ControllerPath = "Assets/1_Authored/Art/Characters/Animators/HumanoidPlayerController.controller";
    const string SpecPath = "Assets/3_Data/Resources/CharacterSpec.asset";

    // Case-insensitive name tokens deciding which pool a model lands in. FIGHTER wins first (a soldier is not a
    // civilian); anything EXCLUDED is in neither pool.
    static readonly string[] FighterTokens = { "Soldier", "Knight", "Goblin", "Zombie" };
    static readonly string[] ExcludeTokens = { "Cow", "Pug", "BaseCharacter", "_Hat", "_Hair", "Helmet" };

    [MenuItem("Tools/unwritten/World/Add Ambient Townsfolk")]
    public static void AddAmbientTownsfolk()
    {
        var spec = LoadOrCreateSpec();
        var civilians = new List<string>();
        var fighters = new List<string>();
        CollectCast(civilians, fighters);

        spec.civilianModels = civilians;
        spec.fighterModels = fighters;

        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (controller != null) spec.animatorController = controller;
        EditorUtility.SetDirty(spec);
        AssetDatabase.SaveAssets();
        CharacterSpec.Reload();

        var existing = Object.FindAnyObjectByType<AmbientLife>();
        var al = existing != null ? existing : new GameObject("AmbientLife").AddComponent<AmbientLife>();

        if (civilians.Count == 0 || controller == null)
            EditorUtility.DisplayDialog("Ambient Townsfolk",
                (civilians.Count == 0
                    ? "No civilian character models found in " + ModelsDir + ".\n"
                    : "") +
                (controller == null
                    ? "The shared Humanoid controller wasn't found at " + ControllerPath + " — build the 3D player " +
                      "first (Tools ▸ unwritten ▸ Rigs ▸ Set 3D Player Model), which creates it.\n"
                    : "") +
                "\nThe director was still added — assign the missing field(s) on the CharacterSpec asset by hand.", "OK");

        int ground = LayerMask.NameToLayer("Ground");
        if (ground >= 0) al.groundMask = 1 << ground;

        if (existing == null)
            al.transform.position = SceneCenter(ground);

        EditorUtility.SetDirty(al);
        EditorSceneManager.MarkSceneDirty(al.gameObject.scene);
        Selection.activeGameObject = al.gameObject;
        EditorGUIUtility.PingObject(al.gameObject);

        Debug.Log($"[unwritten] Character cast wired on {SpecPath} — {civilians.Count} civilians, {fighters.Count} " +
                  $"fighters. Spawning {al.count} live-3D townsfolk at Play; shadow mobs draw from the fighters.");
    }

    static CharacterSpec LoadOrCreateSpec()
    {
        var spec = AssetDatabase.LoadAssetAtPath<CharacterSpec>(SpecPath);
        if (spec != null) return spec;

        string dir = Path.GetDirectoryName(SpecPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/3_Data", "Resources");
        spec = ScriptableObject.CreateInstance<CharacterSpec>();
        AssetDatabase.CreateAsset(spec, SpecPath);
        return spec;
    }

    // Every rigged model in the Models folder that is a real character body (has a skinned mesh — an accessory or a
    // clip-only FBX doesn't), sorted into the two pools by name. The pools store the model's NAME: the spec must not
    // hold object links, or loading it would drag the whole cast into memory to dress one unit. The name is resolved
    // to an asset on demand by CharacterModels.
    static void CollectCast(List<string> civilians, List<string> fighters)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { ModelsDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.ToLower().EndsWith(".fbx")) continue;

            string fileName = Path.GetFileNameWithoutExtension(path);
            if (HasToken(fileName, ExcludeTokens)) continue;

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null || go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;

            (HasToken(fileName, FighterTokens) ? fighters : civilians).Add(fileName);
        }
        civilians.Sort(string.CompareOrdinal);
        fighters.Sort(string.CompareOrdinal);
    }

    static bool HasToken(string name, string[] tokens)
    {
        foreach (var tok in tokens)
            if (name.IndexOf(tok, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // Centre on the map: average of any SpawnPoints, else the world origin — ground-snapped so the director's
    // fallback height is sane.
    static Vector3 SceneCenter(int groundLayer)
    {
        Vector3 c = Vector3.zero;
        var spawns = Object.FindObjectsByType<SpawnPoint>();
        if (spawns != null && spawns.Length > 0)
        {
            c = Vector3.zero;
            foreach (var s in spawns) c += s.transform.position;
            c /= spawns.Length;
        }

        int mask = groundLayer >= 0 ? (1 << groundLayer) : Physics.DefaultRaycastLayers;
        if (Physics.Raycast(c + Vector3.up * 50f, Vector3.down, out var hit, 100f, mask, QueryTriggerInteraction.Ignore))
            c.y = hit.point.y;
        return c;
    }
}
