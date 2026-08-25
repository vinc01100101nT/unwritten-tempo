using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

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
    public const string RiggedDir   = "Assets/1_Authored/Art/Characters/Models";
    public const string DefaultModel = RiggedDir + "/BaseCharacter.fbx";
    const string OutDir = "Assets/1_Authored/Art/Characters/Animators";
    public const string ControllerPath = OutDir + "/HumanoidPlayerController.controller";

    // Wire the runtime editor-Play model swapper (PlayerModelSwap) to THIS builder's Attach — the single
    // place a player visual is built. PlayerModelSwap lives in the runtime assembly and can't reference this
    // Editor-assembly class, so we push the delegate down to it on editor load instead.
    [InitializeOnLoadMethod]
    static void HookRuntimeModelSwap()
    {
        PlayerModelSwap.Attacher = AttachRuntimeSwap;
        PlayerModelSwap.RiggedDir = RiggedDir;
    }

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

    /// <summary>Attach a fresh live-3D visual onto the given player GameObject.
    /// Shared by Setup Systems Scene (default build) and the menu tool.
    /// <para><paramref name="controllerOverride"/> lets a caller REUSE an already-loaded shared Humanoid controller
    /// instead of rebuilding it (which writes an asset to disk) — the runtime editor-Play swapper passes the live
    /// player's controller so it never touches the AssetDatabase mid-swap. Null → build it (the menu/setup path).</para></summary>
    public static GameObject Attach(Player3DController player, string modelPath,
                                    UnityEditor.Animations.AnimatorController controllerOverride = null)
    {
        if (player == null) return null;
        if (!File.Exists(modelPath)) modelPath = DefaultModel;
        if (!File.Exists(modelPath))
        {
            Debug.LogWarning($"[unwritten] No rigged model at {modelPath}. Rig characters first " +
                             "(tools/mixamo_batch_rig.py → Tools ▸ unwritten ▸ Rigs ▸ Bulk Set Humanoid).");
            return null;
        }

        // Validate the model BEFORE touching the current visual, so a bad pick never leaves the player bare.
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null || model.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
        {
            Debug.LogWarning($"[unwritten] '{modelPath}' has no skinned mesh — it's not a character body (an animation " +
                             "clip FBX?). Player visual unchanged. Pick a rigged model from " + RiggedDir + ".");
            return null;
        }

        // A PREFAB instance, not a plain clone: at author time the scene must save a LINK to the model, not a copy of
        // it. That is the one thing this path genuinely does differently from the in-game swap — everything after it
        // is the shared recipe (Player3DBody), so the two can no longer drift.
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        string charName = Path.GetFileNameWithoutExtension(modelPath);
        var avatar = Character3DRigBuilder.LoadAvatar(modelPath);   // the model's own Humanoid avatar (retarget target)
        var controller = controllerOverride != null ? controllerOverride : BuildController();

        go = Player3DBody.Configure(player, go, charName, controller, avatar);
        if (go == null) return null;

        // Author-time only: dirty the scene so the swap persists on save. ILLEGAL in Play (throws
        // InvalidOperationException) — the runtime editor-Play swapper is transient, nothing to persist.
        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        return go;
    }

    /// <summary>Runtime editor-Play model swap (driven by <see cref="PlayerModelSwap"/>). Reuses the live player's
    /// already-loaded shared Humanoid controller so the swap never rebuilds a controller asset on disk — that
    /// AssetDatabase write is unreliable mid-frame in Play and was leaving the player bare. Falls back to building
    /// the controller once if there's no live visual to borrow it from.</summary>
    public static GameObject AttachRuntimeSwap(Player3DController player, string modelPath)
    {
        var live = Object.FindAnyObjectByType<Character3DVisual>();
        var anim = live != null ? live.GetComponent<Animator>() : null;
        var shared = anim != null ? anim.runtimeAnimatorController as UnityEditor.Animations.AnimatorController : null;
        return Attach(player, modelPath, shared != null ? shared : BuildController());
    }

    /// <summary>Remove any existing player visual (live-3D) so we never stack two.</summary>
    public static int RemoveExistingPlayerVisual()
    {
        int n = 0;
        foreach (var v in Object.FindObjectsByType<Character3DVisual>())
            if (v != null) { Object.DestroyImmediate(v.gameObject); n++; }
        return n;
    }

    // ---- menu: swap the live player to a chosen rigged model (Systems scene) ----

    [MenuItem("Tools/unwritten/Rigs/Set 3D Player Model (Systems scene)", priority = 623)]
    static void SetPlayerModel()
    {
        var player = Object.FindAnyObjectByType<Player3DController>();
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
