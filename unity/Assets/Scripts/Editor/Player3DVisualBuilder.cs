using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the committed live-3D player visual: instantiates a rigged (Mixamo Humanoid) character model, gives it an
/// AnimatorController built from the authored <see cref="StanceLibrary"/> resolved against the SHARED Humanoid clip
/// library (<see cref="HumanoidClipResolver"/>), and drops the <see cref="Character3DPose"/> + <see cref="StanceController"/>
/// + <see cref="Character3DVisual"/> rig on it so it rides the real <see cref="Player3DController"/>.
///
/// <para>This REPLACES the baked-billboard player (BakedPlayerVisual) — the project committed to live-3D characters
/// (mobs stay billboards). Called at author time by <c>Setup Systems Scene</c> and by the menu tool below; the
/// runtime visual code (<see cref="Character3DVisual"/>) is untouched.</para>
/// </summary>
public static class Player3DVisualBuilder
{
    public const string RiggedDir   = "Assets/Art/Quat/Rigged";
    public const string DefaultModel = RiggedDir + "/BaseCharacter.fbx";
    const string OutDir = "Assets/Art/Quat/Player3D";
    public const string ControllerPath = OutDir + "/HumanoidPlayerController.controller";

    /// <summary>Build (or rebuild) the shared Humanoid player controller from the StanceLibrary + Humanoid clip folder.
    /// Write-defaults OFF (Mixamo-style) so no state re-writes the root and clobbers the model's facing.</summary>
    public static AnimatorController BuildController()
    {
        if (HumanoidClipResolver.Count() == 0)
            Debug.LogWarning($"[unwritten] No Humanoid clips in {HumanoidClipResolver.ClipDir} — the 3D player will " +
                             "rest/T-pose. Drop Humanoid-imported clips there named to match the Stance Composer strings " +
                             "(Idle/Walk/Run/…), then re-run Setup Systems Scene.");
        return Character3DControllerBuilder.Build(ControllerPath, quat: null,
                                                  resolver: HumanoidClipResolver.Resolver(), writeDefaultsOff: true);
    }

    /// <summary>Attach a fresh live-3D visual onto the given player GameObject, removing any prior player visual first.
    /// Shared by Setup Systems Scene (default build) and the menu tool.</summary>
    public static GameObject Attach(Player3DController player, string modelPath)
    {
        if (player == null) return null;
        if (!File.Exists(modelPath)) modelPath = DefaultModel;
        if (!File.Exists(modelPath))
        {
            Debug.LogWarning($"[unwritten] No rigged model at {modelPath}. Rig characters first " +
                             "(tools/mixamo_batch_rig.py → Tools ▸ unwritten ▸ Rigs ▸ Bulk Set Humanoid).");
            return null;
        }

        // Validate the model BEFORE removing the current visual, so a bad pick never leaves the player bare.
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null || model.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
        {
            Debug.LogWarning($"[unwritten] '{modelPath}' has no skinned mesh — it's not a character body (an animation " +
                             "clip FBX?). Player visual unchanged. Pick a rigged model from " + RiggedDir + ".");
            return null;
        }

        RemoveExistingPlayerVisual();
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        string charName = Path.GetFileNameWithoutExtension(modelPath);
        go.name = "Player3DVisual (" + charName + ")";
        SceneManager.MoveGameObjectToScene(go, player.gameObject.scene);
        go.transform.position = player.transform.position;

        float nativeH = Character3DRigBuilder.MeasureHeight(go);
        float s = nativeH > 1e-3f ? Character3DRigBuilder.TargetHeight / nativeH : 1f;
        go.transform.localScale = Vector3.one * s;

        var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
        anim.runtimeAnimatorController = BuildController();
        var avatar = Character3DRigBuilder.LoadAvatar(modelPath);   // the model's own Humanoid avatar (retarget target)
        if (avatar != null) anim.avatar = avatar;
        anim.applyRootMotion = false;

        if (go.GetComponent<Character3DPose>() == null) go.AddComponent<Character3DPose>();
        if (go.GetComponent<StanceController>() == null) go.AddComponent<StanceController>();
        var vis = go.GetComponent<Character3DVisual>() ?? go.AddComponent<Character3DVisual>();
        vis.player = player;
        if (go.GetComponent<WeaponSocket>() == null) go.AddComponent<WeaponSocket>();

        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        return go;
    }

    /// <summary>Remove any existing player visual (live-3D) so we never stack two.</summary>
    public static int RemoveExistingPlayerVisual()
    {
        int n = 0;
        foreach (var v in Object.FindObjectsByType<Character3DVisual>(FindObjectsSortMode.None))
            if (v != null) { Object.DestroyImmediate(v.gameObject); n++; }
        return n;
    }

    // ---- menu: swap the live player to a chosen rigged model (Systems scene) ----

    [MenuItem("Tools/unwritten/Rigs/Set 3D Player Model (Systems scene)", priority = 623)]
    static void SetPlayerModel()
    {
        var player = Object.FindFirstObjectByType<Player3DController>();
        if (player == null)
        {
            EditorUtility.DisplayDialog("Set 3D Player Model",
                "No player in the open scenes — open the Systems scene first (it owns the player).", "OK");
            return;
        }
        string modelPath = PickModel();
        var go = Attach(player, modelPath);
        if (go != null)
        {
            Selection.activeGameObject = go;
            EditorUtility.DisplayDialog("Set 3D Player Model",
                $"Player now wears live-3D '{Path.GetFileNameWithoutExtension(modelPath)}'. Press Play.\n\n" +
                "Animation comes from the shared Humanoid clip library; movement/combat stay the real player's.", "OK");
        }
    }

    // A selected RIGGED-FOLDER FBX wins; else the default BaseCharacter. Only the Rigged dir is accepted so a stray
    // selection (e.g. an animation clip FBX with no mesh) can't be instantiated as the "model" and make the player
    // invisible — that's a character body, not a clip.
    static string PickModel()
    {
        var sel = Selection.activeObject;
        if (sel != null)
        {
            string p = AssetDatabase.GetAssetPath(sel);
            if (!string.IsNullOrEmpty(p) && p.ToLower().EndsWith(".fbx")
                && p.Replace('\\', '/').StartsWith(RiggedDir + "/", System.StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return DefaultModel;
    }
}
