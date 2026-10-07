using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One click to refresh the shared <see cref="CharacterSpec"/>'s CAST (creating the asset if it doesn't exist yet) from
/// the rigged models on disk, and point it at the shared Humanoid controller. Run it after adding a character model.
/// Which townsperson lives where is not this tool's business: that is the townsfolk list (<see cref="WorldTownsfolk"/>).
///
/// <para>The cast is the CIVILIANS — the bodies townsfolk wear. Soldiers, knights, goblins and zombies are left out of
/// it, and so are Cow, Pug, BaseCharacter and the hat, hair and helmet models. Those are NOT accessory-only meshes:
/// every one of them is a full body imported with a Humanoid avatar, and every one can be worn — a Shadow of the Past
/// picks its body from the WHOLE roster in the Mob Table (<see cref="MonsterDef.modelName"/>), so nothing here decides
/// a shadow's body. They are only kept out of the townsfolk crowd.</para>
///
/// Idempotent: re-running re-uses the existing spec instead of making a second one.
/// </summary>
public static class CharacterCastTools
{
    const string ModelsDir = "Assets/1_Authored/Art/Characters/Models";
    const string ControllerPath = "Assets/1_Authored/Art/Characters/Animators/HumanoidPlayerController.controller";
    const string SpecPath = "Assets/3_Data/Resources/CharacterSpec.asset";

    // Case-insensitive name tokens for the models kept OUT of the townsfolk crowd: combatants (a soldier is not a
    // civilian), and the animal, untextured and hat/hair/helmet bodies.
    static readonly string[] NotCivilianTokens =
        { "Soldier", "Knight", "Goblin", "Zombie", "Cow", "Pug", "BaseCharacter", "_Hat", "_Hair", "Helmet" };

    [MenuItem("Tools/unwritten/World/Refresh Character Cast")]
    public static void RefreshCharacterCast()
    {
        var spec = LoadOrCreateSpec();
        var civilians = new List<string>();
        CollectCivilians(civilians);

        spec.civilianModels = civilians;

        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (controller != null) spec.animatorController = controller;
        EditorUtility.SetDirty(spec);
        AssetDatabase.SaveAssets();
        CharacterSpec.Reload();

        if (civilians.Count == 0 || controller == null)
            EditorUtility.DisplayDialog("Character Cast",
                (civilians.Count == 0
                    ? "No civilian character models found in " + ModelsDir + ".\n"
                    : "") +
                (controller == null
                    ? "The shared Humanoid controller wasn't found at " + ControllerPath + " — build the 3D player " +
                      "first (Tools ▸ unwritten ▸ Rigs ▸ Set 3D Player Model), which creates it.\n"
                    : "") +
                "\nAssign the missing field(s) on the CharacterSpec asset by hand.", "OK");

        Debug.Log($"[unwritten] Character cast wired on {SpecPath} — {civilians.Count} civilians, the bodies " +
                  $"townsfolk wear.");
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

    // Every rigged model in the Models folder that is a real character body (has a skinned mesh — a clip-only FBX
    // doesn't) and is not kept out by name. The pool stores the model's NAME: the spec must not hold object links, or
    // loading it would drag the whole cast into memory to dress one unit. The name is resolved to an asset on demand
    // by CharacterModels.
    static void CollectCivilians(List<string> civilians)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { ModelsDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.ToLower().EndsWith(".fbx")) continue;

            string fileName = Path.GetFileNameWithoutExtension(path);
            if (HasToken(fileName, NotCivilianTokens)) continue;

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null || go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;

            civilians.Add(fileName);
        }
        civilians.Sort(string.CompareOrdinal);
    }

    static bool HasToken(string name, string[] tokens)
    {
        foreach (var tok in tokens)
            if (name.IndexOf(tok, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
}
