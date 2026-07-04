using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// SPIKE (experiment/3d-characters): plays the downloaded Mixamo CASTER clips on the Quaternius body — <b>1:1</b>,
/// exactly as previewed in Mixamo. The key fact: the user auto-rigged the Quaternius model IN Mixamo, so the
/// downloaded clips are authored on the SAME Quaternius skeleton (bones <c>Bone/Body/Hips/…</c>), not Mixamo's
/// <c>mixamorig</c>. So NO Humanoid retargeting is needed (that only distorts) — the clips just need their root
/// path reconciled with the model. Unity's <b>Generic "Copy From Other Avatar"</b> does exactly that: it binds
/// the clips to the model's Generic avatar by bone name, an identity map on the same skeleton.
///
/// <para>Pipeline: give the isolated <c>Caster_NinjaFemale</c> duplicate a Generic avatar; import the 9 Mixamo
/// clips Generic + CopyFromOther that avatar; build a caster controller + spike scene. The committed Generic
/// player and <c>Ninja_Female</c> are never touched.</para>
/// </summary>
// QUICKFIX 2026-07-04 — Mixamo caster retarget spike (experiment/3d-characters); fold into the class system once proven.
public static class CasterRig
{
    const string MixamoDir   = "Assets/Art/Mixamo";
    const string BodyPath    = MixamoDir + "/Caster_NinjaFemale.fbx";
    const string ControllerPath = MixamoDir + "/CasterController.controller";
    const string ScenePath   = "Assets/Scenes/MixamoCasterSpike.unity";

    // Caster action state -> Mixamo FBX file (clips are all named "mixamo.com", so key by file). Idle is the default.
    static readonly (string state, string file)[] Actions =
    {
        ("Idle",       "Standing Idle 03"),
        ("Cast",       "Standing 2H Cast Spell 01"),
        ("Attack",     "Standing 2H Magic Attack 03"),
        ("AreaAttack", "Standing 2H Magic Area Attack 02"),
        ("LookAround", "Look Around"),
        ("Death",      "Standing React Death Backward"),
        ("Cast_Magic", "Magic Spell Casting"),
    };
    static readonly HashSet<string> LoopStates = new HashSet<string> { "Idle" };

    [MenuItem("Tools/unwritten/Spike/Build Mixamo Caster Spike", priority = 901)]
    static void Build()
    {
        if (!File.Exists(BodyPath))
        {
            EditorUtility.DisplayDialog("Mixamo Caster", $"Caster body not found:\n{BodyPath}", "OK");
            return;
        }
        var avatar = EnsureCasterAvatar();
        if (avatar == null)
        {
            EditorUtility.DisplayDialog("Mixamo Caster", "Couldn't create a Generic avatar for the Quaternius body — see Console.", "OK");
            return;
        }
        EnsureMixamoGeneric(avatar);
        var controller = BuildController();
        BuildScene(avatar, controller);
        Debug.Log("[unwritten] Mixamo Caster spike (1:1 Generic) built → " + ScenePath);
        EditorUtility.DisplayDialog("Mixamo Caster",
            "Built the Mixamo caster spike (1:1, no retargeting) and opened it.\n\nPress Play — the Quaternius caster " +
            "idles with your Mixamo motion. Number keys: 1 Cast · 2 Attack · 3 AreaAttack · 4 LookAround · 5 Death.\n\n" +
            "This plays the clips on the exact skeleton they were authored on, so it matches your Mixamo preview.", "OK");
    }

    // The isolated caster duplicate gets a GENERIC avatar (defines its skeleton) that the Mixamo clips copy from.
    // Ninja_Female (the committed player) is never touched.
    internal const string CasterBodyPath = BodyPath;
    internal static Avatar EnsureCasterAvatar()
    {
        var imp = AssetImporter.GetAtPath(BodyPath) as ModelImporter;
        if (imp == null) return null;
        if (imp.animationType != ModelImporterAnimationType.Generic ||
            imp.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.humanDescription = new HumanDescription();
            imp.SaveAndReimport();
        }
        foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(BodyPath))
            if (o is Avatar a) return a;
        return null;
    }

    // Import each Mixamo clip as Generic and bind it to the caster's Generic avatar by bone name (identity on the
    // same skeleton → 1:1). Loop the idle so it cycles.
    internal static void EnsureMixamoGeneric(Avatar avatar)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { MixamoDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = Path.GetFileNameWithoutExtension(path);
            if (file.StartsWith("Caster_")) continue;   // the body
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) continue;

            bool loop = file == "Standing Idle 03";
            bool changed = false;
            if (imp.animationType != ModelImporterAnimationType.Generic ||
                imp.avatarSetup != ModelImporterAvatarSetup.CopyFromOther ||
                imp.sourceAvatar != avatar)
            {
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                imp.sourceAvatar = avatar;
                changed = true;
            }
            var clips = imp.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i].loopTime != loop) { clips[i].loopTime = loop; changed = true; }
            if (changed) { imp.clipAnimations = clips; imp.SaveAndReimport(); }
        }
    }

    // Caster controller: Idle (default, loop) + one state per action, each returning to Idle on exit (Death holds).
    static AnimatorController BuildController()
    {
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = ctrl.layers[0].stateMachine;
        AnimatorState idle = null;
        var oneShots = new List<AnimatorState>();

        foreach (var (stateName, file) in Actions)
        {
            var clip = LoadMixamoClip(file);
            if (clip == null) { Debug.LogWarning("[unwritten] caster clip missing (no takes?): " + file); continue; }
            var st = sm.AddState(stateName);
            st.motion = clip;
            if (stateName == "Idle") { idle = st; sm.defaultState = st; }
            else if (stateName != "Death") oneShots.Add(st);
        }
        if (idle != null)
            foreach (var st in oneShots)
            {
                var back = st.AddTransition(idle);
                back.hasExitTime = true; back.exitTime = 0.85f; back.duration = 0.15f;
            }

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    internal static AnimationClip LoadMixamoClip(string file)
    {
        foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(MixamoDir + "/" + file + ".fbx"))
            if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
        return null;
    }

    static void BuildScene(Avatar avatar, AnimatorController controller)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var lightGO = new GameObject("Directional Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.1f;
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground"; ground.transform.localScale = Vector3.one * 5f;
        int gl = LayerMask.NameToLayer("Ground_Floor"); if (gl >= 0) ground.layer = gl;

        var asset = (GameObject)AssetDatabase.LoadAssetAtPath(BodyPath, typeof(GameObject));
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        inst.name = "MixamoCaster";
        inst.transform.position = Vector3.zero;

        float nativeH = Build3DCharacterSpike.MeasureHeight(inst);
        inst.transform.localScale = Vector3.one * (nativeH > 1e-3f ? Build3DCharacterSpike.TargetHeight / nativeH : 1f);

        var anim = inst.GetComponent<Animator>();
        if (anim == null) anim = inst.AddComponent<Animator>();
        anim.avatar = avatar;
        anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false;
        if (inst.GetComponent<CasterSpikeDriver>() == null) inst.AddComponent<CasterSpikeDriver>();

        var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGO.AddComponent<Camera>();
        cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
        camGO.AddComponent<AudioListener>();
        // Frame the small (~0.33u) caster from the front, pulled back so a forward cast reach doesn't jam the lens.
        cam.fieldOfView = 50f;
        camGO.transform.position = new Vector3(0.3f, 0.18f, 1.2f);
        camGO.transform.LookAt(new Vector3(0f, 0.16f, 0f));

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = inst;
    }
}
