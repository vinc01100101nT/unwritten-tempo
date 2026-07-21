using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click setup for the ambient-life layer: drops an <see cref="AmbientLife"/> director into the open scene,
/// wires it to the CIVILIAN rigged Quaternius models + the shared Humanoid controller, and points its ground mask
/// at the Ground layer. After running it, just press Play — wandering live-3D townsfolk appear and chatter.
///
/// The cast is filtered to civilians: it excludes soldiers/knights (those become combat NPCs later), monsters and
/// animals (they'd walk wrong on the human controller), and accessory-only meshes (hats/hair/helmets).
///
/// Idempotent: re-running re-uses the existing director (re-wires the cast) instead of stacking duplicates.
/// </summary>
public static class AmbientLifeTools
{
    const string ModelsDir = "Assets/1_Authored/Art/Characters/Models";
    const string ControllerPath = "Assets/1_Authored/Art/Characters/Animators/HumanoidPlayerController.controller";

    // Case-insensitive name tokens that disqualify a model from the civilian townsfolk cast. Soldiers/knights are
    // held back for combat NPCs; Goblin/Zombie/Cow/Pug aren't humanoid civilians; BaseCharacter is the player's
    // default; _Hat/_Hair/Helmet are accessory meshes, not full bodies.
    static readonly string[] ExcludeTokens =
        { "Soldier", "Knight", "Goblin", "Zombie", "Cow", "Pug", "BaseCharacter", "_Hat", "_Hair", "Helmet" };

    [MenuItem("Tools/unwritten/World/Add Ambient Townsfolk")]
    public static void AddAmbientTownsfolk()
    {
        var existing = Object.FindFirstObjectByType<AmbientLife>();
        var al = existing != null ? existing : new GameObject("AmbientLife").AddComponent<AmbientLife>();

        var models = FindCivilianModels();
        al.models = models;

        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (controller != null) al.sharedController = controller;

        if (models.Count == 0 || controller == null)
            EditorUtility.DisplayDialog("Ambient Townsfolk",
                (models.Count == 0
                    ? "No civilian character models found in " + ModelsDir + ".\n"
                    : "") +
                (controller == null
                    ? "The shared Humanoid controller wasn't found at " + ControllerPath + " — build the 3D player " +
                      "first (Tools ▸ unwritten ▸ Rigs ▸ Set 3D Player Model), which creates it.\n"
                    : "") +
                "\nThe director was still added — assign the missing field(s) by hand.", "OK");

        int ground = LayerMask.NameToLayer("Ground");
        if (ground >= 0) al.groundMask = 1 << ground;

        if (existing == null)
            al.transform.position = SceneCenter(ground);

        EditorUtility.SetDirty(al);
        EditorSceneManager.MarkSceneDirty(al.gameObject.scene);
        Selection.activeGameObject = al.gameObject;
        EditorGUIUtility.PingObject(al.gameObject);

        Debug.Log($"[unwritten] Ambient townsfolk ready — {models.Count} civilian models wired, " +
                  $"spawning {al.count} live-3D townsfolk at Play. Tweak Count / Area Radius on the AmbientLife object.");
    }

    // Every rigged model in the Models folder that is a civilian (name passes the exclude filter) AND has a skinned
    // mesh (a real character body, not an accessory/clip FBX).
    static List<GameObject> FindCivilianModels()
    {
        var list = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { ModelsDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.ToLower().EndsWith(".fbx")) continue;

            string fileName = Path.GetFileNameWithoutExtension(path);
            if (IsExcluded(fileName)) continue;

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null || go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;
            list.Add(go);
        }
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list;
    }

    static bool IsExcluded(string name)
    {
        foreach (var tok in ExcludeTokens)
            if (name.IndexOf(tok, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // Centre on the map: average of any SpawnPoints, else the world origin — ground-snapped so the director's
    // fallback height is sane.
    static Vector3 SceneCenter(int groundLayer)
    {
        Vector3 c = Vector3.zero;
        var spawns = Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
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
