using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// SPIKE tool (experiment/3d-characters): one click stands up a LIVE 3D skinned Quaternius character
/// driven by a real AnimatorController — the "true 3D" alternative to the shipped baked-billboard path.
/// It does three things the runtime <see cref="Character3DDriver"/> can't do itself (all editor-only):
///
///   1. <b>Clip prep</b> — keeps the pack's native <b>Generic</b> rig (its embedded takes already match its
///      own skeleton) and loops Idle/Walk/Run(+Carry) so the blend tree cycles. It does NOT force Humanoid:
///      the Quaternius skeleton doesn't auto-map to a Humanoid avatar cleanly and forcing it drops the clips.
///      Humanoid is only needed later, to retarget FOREIGN clips (Mixamo) — a separate step.
///   2. <b>AnimatorController</b> — generates <c>QuatSpikeController.controller</c>: a 1-D blend tree
///      (Idle→Walk→Run on the <c>Speed</c> float) as the default state, plus <c>Attack</c> (SwordSlash)
///      and <c>Jump</c> states fired from AnyState by triggers. This is the piece people wrongly expect
///      Unity to "auto-map" — it is authored here, once, and reusable across models.
///   3. <b>Test scene</b> — <c>Spike3DCharacters.unity</c>: ground + light + follow camera + the model
///      wired with the controller, a CharacterController, and <see cref="Character3DDriver"/>. Press Play
///      and WASD/click to compare against the billboards.
///
/// Pick which model by SELECTING an FBX under Assets/Art/Quat/FBX before running; otherwise it uses
/// Knight_Male. Re-runnable — it overwrites the controller + scene in place.
/// </summary>
// QUICKFIX 2026-07-04 — 3D-character spike (experiment/3d-characters); delete after we decide 3D vs billboards.
public static class Build3DCharacterSpike
{
    const string QuatDir       = "Assets/Art/Quat/FBX";
    const string DefaultModel  = "Assets/Art/Quat/FBX/Knight_Male.fbx";
    const string OutDir        = "Assets/Art/Quat/Spike3D";
    const string ControllerPath = OutDir + "/QuatSpikeController.controller";
    const string ScenePath     = "Assets/Scenes/Spike3DCharacters.unity";

    // Quaternius FBX import several units tall (~3.3u); auto-fit every model to this world height instead.
    // 0.327u = the hand-tuned size (Ninja_Female at 0.1 scale) that reads right against the game's world.
    const float TargetHeight = 0.327f;

    // Clips looped so the locomotion blend tree cycles; everything else plays once (Attack/Jump/etc.).
    static readonly HashSet<string> LoopClips = new HashSet<string>
        { "Idle", "Walk", "Run", "Walk_Carry", "Run_Carry" };

    [MenuItem("Tools/unwritten/Spike/Build 3D Character Spike", priority = 900)]
    static void Build()
    {
        string modelPath = PickModel();
        if (!File.Exists(modelPath))
        {
            EditorUtility.DisplayDialog("3D Character Spike",
                $"Model not found:\n{modelPath}\n\nSelect a Quaternius FBX under {QuatDir} first.", "OK");
            return;
        }

        EnsureAnimations(modelPath);
        var controller = BuildController(modelPath);
        if (controller == null) return;
        BuildScene(modelPath, controller);

        Debug.Log($"[unwritten] 3D Character Spike ready → opened {ScenePath}. Press Play: WASD move, " +
                  "left-click swings, Space hops, Shift walks. Model = " + Path.GetFileName(modelPath));
        EditorUtility.DisplayDialog("3D Character Spike",
            "Built the 3D spike scene and opened it.\n\nPress Play, then:\n" +
            "• WASD — move (camera-relative)\n• Left-click — SwordSlash\n• Space — jump\n• Shift — walk\n\n" +
            "Right-drag orbits / scroll zooms the follow camera. Compare it against the billboard player.", "OK");
    }

    // Selected FBX under the Quat folder wins; else the default Knight.
    static string PickModel()
    {
        var sel = Selection.activeObject;
        if (sel != null)
        {
            string p = AssetDatabase.GetAssetPath(sel);
            if (!string.IsNullOrEmpty(p) && p.StartsWith(QuatDir) && p.ToLower().EndsWith(".fbx"))
                return p;
        }
        return DefaultModel;
    }

    // ---- 1. Keep the pack's native GENERIC rig + loop the locomotion clips --------------------------
    // We deliberately do NOT force Humanoid: the Quaternius skeleton doesn't auto-map to a Humanoid avatar
    // cleanly, and forcing it drops the embedded clips (import error). All Quat models share one skeleton,
    // so each plays its OWN embedded Generic takes perfectly — which is all the spike needs. Humanoid only
    // becomes necessary when we wire Mixamo (retargeting foreign clips); that's a later step.
    static void EnsureAnimations(string modelPath)
    {
        var imp = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (imp == null) return;

        // If a prior run (or hand-edit) left it Humanoid, restore Generic and reimport so the clip list
        // below is regenerated clean from the FBX takes.
        if (imp.animationType != ModelImporterAnimationType.Generic)
        {
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.SaveAndReimport();
            imp = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        }

        // Always re-derive the clip list from the current takes and set loop flags. Assigning the whole
        // fresh set to clipAnimations clobbers any stale entries from an earlier run and makes loops stick.
        var clips = imp.defaultClipAnimations;
        for (int i = 0; i < clips.Length; i++)
            clips[i].loopTime = LoopClips.Contains(CleanName(clips[i].name));
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
    }

    // ---- 2. AnimatorController (blend-tree locomotion + Attack/Jump triggers) -----------------------
    static AnimatorController BuildController(string modelPath)
    {
        Directory.CreateDirectory(OutDir);
        var clips = LoadClips(modelPath);

        string[] required = { "Idle", "Walk", "Run", "SwordSlash", "Jump" };
        foreach (var name in required)
            if (!clips.ContainsKey(name))
            {
                EditorUtility.DisplayDialog("3D Character Spike",
                    $"Clip '{name}' not found in {Path.GetFileName(modelPath)}.\n\n" +
                    "Expected the Quaternius embedded takes (Idle/Walk/Run/SwordSlash/Jump). " +
                    "Did the FBX import as Humanoid?", "OK");
                return null;
            }

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

        var sm = ctrl.layers[0].stateMachine;

        // 1-D blend tree: Speed 0→Idle, 0.5→Walk, 1→Run. CreateBlendTreeInController parents it correctly.
        var loco = sm.AddState("Locomotion");
        var blend = new BlendTree
        {
            name = "Locomotion",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed",
            useAutomaticThresholds = false,
        };
        AssetDatabase.AddObjectToAsset(blend, ctrl);
        blend.AddChild(clips["Idle"], 0f);
        blend.AddChild(clips["Walk"], 0.5f);
        blend.AddChild(clips["Run"], 1f);
        loco.motion = blend;
        sm.defaultState = loco;

        var attack = sm.AddState("Attack");
        attack.motion = clips["SwordSlash"];
        var jump = sm.AddState("Jump");
        jump.motion = clips["Jump"];

        // AnyState → Attack on the Attack trigger; return to Locomotion after the swing plays out.
        var toAtk = sm.AddAnyStateTransition(attack);
        toAtk.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
        toAtk.hasExitTime = false; toAtk.duration = 0.05f; toAtk.canTransitionToSelf = false;
        var atkBack = attack.AddTransition(loco);
        atkBack.hasExitTime = true; atkBack.exitTime = 0.85f; atkBack.duration = 0.1f;

        // AnyState → Jump on the Jump trigger; return to Locomotion after the hop.
        var toJump = sm.AddAnyStateTransition(jump);
        toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
        toJump.hasExitTime = false; toJump.duration = 0.05f; toJump.canTransitionToSelf = false;
        var jumpBack = jump.AddTransition(loco);
        jumpBack.hasExitTime = true; jumpBack.exitTime = 0.8f; jumpBack.duration = 0.1f;

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    // Every AnimationClip sub-asset of the FBX, keyed by CLEAN name (skips Unity's __preview clips).
    // Quaternius names its takes "CharacterArmature|Idle" (armature prefix) — CleanName strips that so
    // callers look up "Idle"/"Walk"/etc. The controller wires clips by OBJECT reference, so the prefix
    // only matters for these dictionary keys.
    static Dictionary<string, AnimationClip> LoadClips(string modelPath)
    {
        var dict = new Dictionary<string, AnimationClip>();
        foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath))
            if (o is AnimationClip c && !c.name.StartsWith("__preview"))
                dict[CleanName(c.name)] = c;
        return dict;
    }

    // "CharacterArmature|SwordSlash" → "SwordSlash"; passthrough when there's no armature prefix.
    static string CleanName(string n)
    {
        int bar = n.LastIndexOf('|');
        return bar >= 0 ? n.Substring(bar + 1) : n;
    }

    static Avatar LoadAvatar(string modelPath)
    {
        foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath))
            if (o is Avatar a) return a;
        return null;
    }

    // World-space rendered height (union of all renderer bounds) — measured at the caller's current scale.
    static float MeasureHeight(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return 0f;
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b.size.y;
    }

    // ---- 3. Test scene -----------------------------------------------------------------------------
    static void BuildScene(string modelPath, AnimatorController controller)
    {
        // Replacing the open scene — let the user save any unsaved edits first.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var lightGO = new GameObject("Directional Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = Vector3.one * 5f;
        int groundLayer = LayerMask.NameToLayer("Ground_Floor");
        if (groundLayer >= 0) ground.layer = groundLayer;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        inst.name = model.name + "_3D";
        inst.transform.position = Vector3.zero;

        var anim = inst.GetComponent<Animator>();
        if (anim == null) anim = inst.AddComponent<Animator>();
        anim.runtimeAnimatorController = controller;
        var avatar = LoadAvatar(modelPath);
        if (avatar != null) anim.avatar = avatar;
        anim.applyRootMotion = false;

        // Measure at native scale (inst is scale 1 here), then fit to TargetHeight.
        float nativeH = MeasureHeight(inst);
        float s = nativeH > 1e-3f ? TargetHeight / nativeH : 1f;
        inst.transform.localScale = Vector3.one * s;

        // Capsule sized in MODEL-LOCAL units (native height) so scaling the root scales it too — effective
        // capsule ends up ≈ TargetHeight tall regardless of how the FBX was authored.
        var cc = inst.GetComponent<CharacterController>();
        if (cc == null) cc = inst.AddComponent<CharacterController>();
        cc.height = nativeH; cc.radius = nativeH * 0.18f; cc.center = new Vector3(0f, nativeH * 0.5f, 0f); cc.skinWidth = 0.02f;

        if (inst.GetComponent<Character3DDriver>() == null) inst.AddComponent<Character3DDriver>();

        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
        camGO.AddComponent<AudioListener>();
        var follow = camGO.AddComponent<CameraFollow3D>();
        follow.target = inst.transform;
        camGO.transform.position = new Vector3(0f, 10f, -4f);
        camGO.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = inst;
    }
}
