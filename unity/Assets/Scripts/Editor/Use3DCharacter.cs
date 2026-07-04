using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Swaps the REAL player to a live-3D skinned Quaternius character — the 3D counterpart of
/// <c>Use Baked Character</c>. It builds a full AnimatorController (locomotion blend + a state per authored stance
/// clip, straight from the Stance Composer's <see cref="StanceLibrary"/>) and drops a <see cref="Character3DVisual"/>
/// rig onto the player, so
/// every action the animation brain drives (attack/jump/roll/hurt/pickup/cast/die/…) plays as a 3D clip through
/// the same <see cref="StanceController"/> choke point the billboard uses. Movement/combat stay the real player's.
///
/// <para>Open the <b>Systems</b> scene (it owns the player), run the menu, press Play. Fully reversible via
/// <c>Revert to Billboard Player</c>.</para>
/// </summary>
// QUICKFIX 2026-07-04 — 3D-character migration (experiment/3d-characters); revisit once 3D vs billboard is decided.
public static class Use3DCharacter
{
    const string QuatDir      = "Assets/Art/Quat/FBX";
    const string DefaultModel = "Assets/Art/Quat/FBX/Ninja_Female.fbx";
    const string OutDir       = "Assets/Art/Quat/Player3D";
    const string ControllerPath = OutDir + "/QuatPlayerController.controller";

    [MenuItem("Tools/unwritten/Player/Use 3D Character (Systems scene)", priority = 606)]
    static void Use()
    {
        var player = Object.FindFirstObjectByType<Player3DController>();
        if (player == null)
        {
            EditorUtility.DisplayDialog("Use 3D Character",
                "No player in the open scenes — open the Systems scene first (it owns the player).", "OK");
            return;
        }

        string modelPath = PickModel();
        if (!File.Exists(modelPath))
        {
            EditorUtility.DisplayDialog("Use 3D Character",
                $"Model not found:\n{modelPath}\n\nSelect a Quaternius FBX under {QuatDir}, or add Ninja_Female.", "OK");
            return;
        }

        Build3DCharacterSpike.EnsureAnimations(modelPath);        // native Generic rig + looped Idle/Walk/Run
        var controller = BuildFullController(modelPath);
        if (controller == null) return;
        Apply(player, modelPath, controller);
    }

    [MenuItem("Tools/unwritten/Player/Revert 3D Character (back to billboard)", priority = 607)]
    static void Revert()
    {
        var player = Object.FindFirstObjectByType<Player3DController>();
        int n = RemoveExisting();
        if (player != null)
        {
            var sr = player.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = true;                    // restore the player's own sprite (edit-mode)
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        }
        EditorUtility.DisplayDialog("Use 3D Character",
            n > 0 ? "Reverted — the 3D visual was removed. Re-run Use Baked Character for the billboard." : "Nothing to revert.", "OK");
    }

    // Selected Quat FBX wins; else Ninja_Female.
    static string PickModel()
    {
        var sel = Selection.activeObject;
        if (sel != null)
        {
            string p = AssetDatabase.GetAssetPath(sel);
            if (!string.IsNullOrEmpty(p) && p.StartsWith(QuatDir) && p.ToLower().EndsWith(".fbx")) return p;
        }
        return DefaultModel;
    }

    // Full player controller: built straight from the authored StanceLibrary (the Stance Composer owns every clip
    // choice — no hardcoded action→clip map). Quaternius embedded clips are the only source here (no Mixamo
    // resolver). Character3DPose CrossFades to states by the clip name, so the controller is just a bag of clips.
    internal static AnimatorController BuildFullController(string modelPath)
    {
        var clips = Build3DCharacterSpike.LoadClips(modelPath);   // keyed by clean suffix: Idle, Walk, SwordSlash, …
        return Character3DControllerBuilder.Build(ControllerPath, clips, resolver: null, writeDefaultsOff: false);
    }

    static void Apply(Player3DController player, string modelPath, AnimatorController controller)
    {
        RemoveExisting();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        string charName = Path.GetFileNameWithoutExtension(modelPath);
        go.name = "Player3DVisual (" + charName + ")";
        SceneManager.MoveGameObjectToScene(go, player.gameObject.scene);
        go.transform.position = player.transform.position;

        float nativeH = Build3DCharacterSpike.MeasureHeight(go);
        float s = nativeH > 1e-3f ? Build3DCharacterSpike.TargetHeight / nativeH : 1f;
        go.transform.localScale = Vector3.one * s;

        var anim = go.GetComponent<Animator>();
        if (anim == null) anim = go.AddComponent<Animator>();
        anim.runtimeAnimatorController = controller;
        var avatar = Build3DCharacterSpike.LoadAvatar(modelPath);
        if (avatar != null) anim.avatar = avatar;
        anim.applyRootMotion = false;

        // Sink + choke point + brain-feeder, all on the model root (StanceController resolves the IStancePose here).
        if (go.GetComponent<Character3DPose>() == null) go.AddComponent<Character3DPose>();
        if (go.GetComponent<StanceController>() == null) go.AddComponent<StanceController>();
        var vis = go.GetComponent<Character3DVisual>() ?? go.AddComponent<Character3DVisual>();
        vis.player = player;

        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Selection.activeGameObject = go;
        EditorUtility.DisplayDialog("Use 3D Character",
            $"Now playing as a 3D '{charName}'. Press Play — WASD/click to move; attack/jump/roll/hurt/pickup all " +
            "drive the Animator through the SAME StanceController the billboard used.\n\n" +
            "Movement/combat are still the real player's. Use 'Revert 3D Character' to undo.", "OK");
    }

    // Remove any player visual (billboard OR 3D) so the two never coexist.
    static int RemoveExisting()
    {
        int n = 0;
        foreach (var v in Object.FindObjectsByType<Character3DVisual>(FindObjectsSortMode.None))
            if (v != null) { Object.DestroyImmediate(v.gameObject); n++; }
        foreach (var v in Object.FindObjectsByType<BakedPlayerVisual>(FindObjectsSortMode.None))
            if (v != null) { Object.DestroyImmediate(v.gameObject); n++; }
        return n;
    }
}
