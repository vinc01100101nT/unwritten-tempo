using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wires the Mixamo CASTER onto the REAL player — the caster counterpart of <c>Use 3D Character</c>. It reuses the
/// committed 3D player system wholesale (<see cref="Character3DVisual"/> → <see cref="StanceController"/> →
/// <see cref="Character3DPose"/>); only the CLIPS differ. Because the Mixamo clips are Generic on the SAME
/// Quaternius skeleton (see the mixamo-on-quaternius memory), the controller freely mixes them with the model's
/// own Quaternius takes. WHICH clip each stance plays is authored in the Stance Composer (no hardcoded map): every
/// stance clip resolves against the Quaternius embedded takes first, then a Mixamo clip of that name (rebound onto
/// the model). So a stance naming "Standing 2H Magic Attack 03" plays that Mixamo clip; one naming "Walk" plays the
/// Quaternius take.
///
/// Uses the isolated <c>Caster_NinjaFemale</c> body (Generic + a Generic avatar so the Copy-From-Other Mixamo
/// clips bind). Movement/combat stay the real player's. Reversible via <c>Revert 3D Character</c>.
/// </summary>
// QUICKFIX 2026-07-04 — Mixamo caster player integration (experiment/3d-characters); fold into the class system.
public static class Use3DCaster
{
    const string OutDir = "Assets/Art/Mixamo";
    internal const string ControllerPath = OutDir + "/CasterPlayerController.controller";

    [MenuItem("Tools/unwritten/Player/Use 3D Caster (Systems scene)", priority = 608)]
    static void Use()
    {
        var player = Object.FindFirstObjectByType<Player3DController>();
        if (player == null)
        {
            EditorUtility.DisplayDialog("Use 3D Caster", "No player — open the Systems scene first.", "OK");
            return;
        }
        if (!File.Exists(CasterRig.CasterBodyPath))
        {
            EditorUtility.DisplayDialog("Use 3D Caster", "Caster body missing: " + CasterRig.CasterBodyPath, "OK");
            return;
        }

        // Body → Generic + looped locomotion; Generic avatar; Mixamo clips bound to it.
        Build3DCharacterSpike.EnsureAnimations(CasterRig.CasterBodyPath);
        var avatar = CasterRig.EnsureCasterAvatar();
        if (avatar == null) { EditorUtility.DisplayDialog("Use 3D Caster", "Couldn't build the caster's Generic avatar — see Console.", "OK"); return; }
        CasterRig.EnsureMixamoGeneric(avatar);

        var controller = BuildController();
        if (controller == null) return;
        Apply(player, avatar, controller);
    }

    // Built straight from the authored StanceLibrary via the shared builder (the Stance Composer owns every clip —
    // no hardcoded action→clip map, no MixamoOverride). Each stance clip resolves against the Quaternius embedded
    // takes first, then falls to a Mixamo clip of that name (rebound onto the model). WD off on every state so no
    // clip re-writes the root and clobbers the facing yaw. States are named by the clip; Character3DPose CrossFades
    // to them by name.
    internal static AnimatorController BuildController()
    {
        var quat = Build3DCharacterSpike.LoadClips(CasterRig.CasterBodyPath);   // by clean suffix: Idle/Walk/Run/SwordSlash/…
        return Character3DControllerBuilder.Build(ControllerPath, quat,
            resolver: name => RebindMixamoToModel(CasterRig.LoadMixamoClip(name), name),
            writeDefaultsOff: true);
    }

    const string GenDir = OutDir + "/Generated";

    // Rebind a Mixamo clip's curves onto THIS model's transform paths — LOSSLESS, no baking. The Mixamo download's
    // skeleton is rooted one level higher than the committed Quaternius body: its curves target "" (the Mixamo root,
    // PARENT of "Bone") + "Bone/…", whereas the model's skeleton hangs under an extra "CharacterArmature" wrapper.
    // Measured (2026-07-04): the Mixamo root "" IS the SAME node as the model's "CharacterArmature" (both the direct
    // parent of "Bone"); the Quaternius clips animate that node via path "CharacterArmature" (rest = 270,0,0), the
    // Mixamo clips via path "". So the ONLY reconciliation needed is a straight re-path — NOT a bake:
    //   • ROOT "" (rotation + position) → REDIRECTED RAW onto "CharacterArmature". Copied verbatim, so the armature
    //     receives EXACTLY the transform Mixamo authored: the download's stand-up, body lean AND crouch dip play 1:1.
    //   • EVERY OTHER path → PREFIXED with "CharacterArmature/" so "Bone/…" → "CharacterArmature/Bone/…".
    // Because both clip families drive the SAME CharacterArmature node, this is Write-Defaults-off crossfade-safe.
    //
    // We deliberately DO NOT touch the root here beyond re-pathing. The retired BakeRootMotion tried to yaw-strip +
    // delta the root rotation to pre-solve FACING (keep the model aligned to Character3DPose's facing yaw), but that
    // quaternion surgery DISTORTED the pose — the "messed target points". Facing is a SEPARATE, deferred problem; the
    // consequences of the raw root are all facing/grounding, not pose fidelity:
    //   – The Mixamo root bakes a constant heading (~127° yaw) → the body faces a fixed offset from the facing yaw.
    //     DEFERRED (the wrong-direction-facing fix), not a target-point issue.
    //   – The Mixamo root rests at y≈−0.287 (its own feet-on-ground convention) vs Quaternius's 0 → a constant
    //     vertical offset, absorbed by the single runtime grounding knob Character3DVisual.mixamoFootOffsetY.
    static AnimationClip RebindMixamoToModel(AnimationClip src, string fileName)
    {
        if (src == null) return null;
        Directory.CreateDirectory(GenDir);
        // Name the asset EXACTLY the composer clip string (the one-shot state is named the same), so Character3DPose,
        // which keys state hashes + clip lengths by the clip's clean name, matches it.
        string outPath = $"{GenDir}/{fileName}.anim";
        var dst = new AnimationClip { frameRate = src.frameRate };
        foreach (var b in AnimationUtility.GetCurveBindings(src))
        {
            var nb = b;
            nb.path = b.path.Length == 0 ? "CharacterArmature" : "CharacterArmature/" + b.path;
            AnimationUtility.SetEditorCurve(dst, nb, AnimationUtility.GetEditorCurve(src, b));
        }
        AnimationUtility.SetAnimationClipSettings(dst, AnimationUtility.GetAnimationClipSettings(src));
        dst.EnsureQuaternionContinuity();
        AssetDatabase.CreateAsset(dst, outPath);
        return dst;
    }

    // The Mixamo clips baked into GenDir, by name — the set the runtime grounding knob targets.
    static List<string> MixamoClipNames()
    {
        var list = new List<string>();
        if (Directory.Exists(GenDir))
            foreach (var f in Directory.GetFiles(GenDir, "*.anim"))
                list.Add(Path.GetFileNameWithoutExtension(f));
        return list;
    }

    static void Apply(Player3DController player, Avatar avatar, AnimatorController controller)
    {
        RemoveExisting();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(CasterRig.CasterBodyPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.name = "Player3DCaster (Ninja_Female)";
        SceneManager.MoveGameObjectToScene(go, player.gameObject.scene);
        go.transform.position = player.transform.position;

        float nativeH = Build3DCharacterSpike.MeasureHeight(go);
        go.transform.localScale = Vector3.one * (nativeH > 1e-3f ? Build3DCharacterSpike.TargetHeight / nativeH : 1f);

        var anim = go.GetComponent<Animator>();
        if (anim == null) anim = go.AddComponent<Animator>();
        anim.avatar = avatar;                        // required so the Copy-From-Other Mixamo clips bind
        anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false;

        var pose = go.GetComponent<Character3DPose>() ?? go.AddComponent<Character3DPose>();
        pose.mixamoClipNames = MixamoClipNames();     // scope the runtime grounding knob to the Mixamo clips
        if (go.GetComponent<StanceController>() == null) go.AddComponent<StanceController>();
        var vis = go.GetComponent<Character3DVisual>() ?? go.AddComponent<Character3DVisual>();
        vis.player = player;

        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Selection.activeGameObject = go;
        EditorUtility.DisplayDialog("Use 3D Caster",
            "The player is now a 3D CASTER. Press Play — it walks/runs with Quaternius motion and casts/attacks/dies " +
            "with your Mixamo clips, all through the same StanceController.\n\nUse 'Revert 3D Character' to undo.", "OK");
    }

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
