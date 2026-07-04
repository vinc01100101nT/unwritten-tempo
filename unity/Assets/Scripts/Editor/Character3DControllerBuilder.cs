using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds a live-3D character's AnimatorController straight from the AUTHORED <see cref="StanceLibrary"/> — the one
/// place the clip-per-stance choice lives (the Stance Composer). There is NO hardcoded action→clip map: the
/// locomotion blend tree is fed by the Idle/Walk/Run stance clips, and every other distinct non-empty stance clip
/// becomes a one-shot state NAMED after the clip, so <see cref="Character3DPose"/> CrossFades to the state whose
/// name equals the composer's clip string. An unassigned stance simply has no state → the character rests.
///
/// <para>Shared by <c>Use 3D Character</c> (Quaternius-only) and <c>Use 3D Caster</c> (Quaternius + Mixamo). Each
/// clip name is resolved against the model's Quaternius embedded clips first, then an optional per-model resolver
/// (the caster's Mixamo rebind) — that's the only per-character difference.</para>
/// </summary>
public static class Character3DControllerBuilder
{
    const string LibPath = "Assets/Resources/StanceLibrary.asset";

    /// <summary>Resolve a composer clip NAME to a playable Motion: Quaternius embedded clip by clean suffix, else the
    /// optional per-model resolver (Mixamo). Null if the name matches nothing (the caller skips + warns).</summary>
    public static Motion Resolve(string clipName, Dictionary<string, AnimationClip> quat, Func<string, Motion> resolver)
    {
        if (string.IsNullOrEmpty(clipName)) return null;
        if (quat != null && quat.TryGetValue(clipName, out var c) && c != null) return c;
        return resolver != null ? resolver(clipName) : null;
    }

    static string ClipFor(StanceLibrary lib, StanceId id)
    {
        if (lib == null || lib.stances == null) return "";
        for (int i = 0; i < lib.stances.Count; i++)
            if (lib.stances[i] != null && lib.stances[i].id == id) return lib.stances[i].clip ?? "";
        return "";
    }

    /// <summary>Build the controller from the authored library. <paramref name="quat"/> = the model's embedded clips
    /// by clean suffix; <paramref name="resolver"/> = optional fallback lookup for names not in <paramref name="quat"/>
    /// (the caster's Mixamo rebind); <paramref name="writeDefaultsOff"/> forces Write Defaults off on every state
    /// (the caster needs it so no state re-writes the root and clobbers facing).</summary>
    public static AnimatorController Build(string controllerPath, Dictionary<string, AnimationClip> quat,
                                           Func<string, Motion> resolver, bool writeDefaultsOff)
    {
        var lib = AssetDatabase.LoadAssetAtPath<StanceLibrary>(LibPath);
        if (lib == null || lib.stances == null || lib.stances.Count == 0)
            Debug.LogWarning("[unwritten] StanceLibrary has no authored clips — open Tools ▸ unwritten ▸ Art ▸ " +
                             "Stance Composer and pick a clip per stance, then rebuild. The character will just stand.");

        Directory.CreateDirectory(Path.GetDirectoryName(controllerPath));
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        var sm = ctrl.layers[0].stateMachine;

        // Locomotion blend tree from the Idle/Walk/Run STANCE clips (author-picked). A missing clip is left out of
        // the blend (unassigned Idle → the tree rests on whatever remains, or the bind pose).
        string idleClip = ClipFor(lib, StanceId.Idle), walkClip = ClipFor(lib, StanceId.Walk), runClip = ClipFor(lib, StanceId.Run);
        var loco = sm.AddState("Locomotion");
        var blend = new BlendTree { name = "Locomotion", blendType = BlendTreeType.Simple1D,
                                    blendParameter = "Speed", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(blend, ctrl);
        AddLoco(blend, Resolve(idleClip, quat, resolver), 0f);
        AddLoco(blend, Resolve(walkClip, quat, resolver), 0.5f);
        AddLoco(blend, Resolve(runClip,  quat, resolver), 1f);
        loco.motion = blend;
        sm.defaultState = loco;

        // One state per DISTINCT non-empty stance clip that isn't already a locomotion clip, NAMED by the clip so the
        // pose CrossFades to the state whose name == the composer string.
        var locoClips = new HashSet<string> { idleClip, walkClip, runClip };
        var made = new HashSet<string>();
        if (lib != null && lib.stances != null)
            foreach (var b in lib.stances)
            {
                if (b == null || string.IsNullOrEmpty(b.clip)) continue;
                if (locoClips.Contains(b.clip)) continue;           // already in the blend tree
                if (!made.Add(b.clip)) continue;                    // distinct only
                var m = Resolve(b.clip, quat, resolver);
                if (m == null) { Debug.LogWarning($"[unwritten] stance clip '{b.clip}' not found on this model/Mixamo set — skipped."); continue; }
                sm.AddState(b.clip).motion = m;
            }

        if (writeDefaultsOff)
            foreach (var cs in sm.states) cs.state.writeDefaultValues = false;

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    static void AddLoco(BlendTree blend, Motion m, float threshold)
    {
        if (m != null) blend.AddChild(m, threshold);
    }

    /// <summary>Rebuild the ACTIVE live-3D visual's controller from the current library and reassign it, so a clip
    /// change in the Stance Composer applies with one Save — no manual Use-3D-Character/Caster trip. Picks the caster
    /// vs plain builder by the visual's current controller path; reflects live in Play (re-inits the pose caches).
    /// Returns the visual's name, or null if there's no live-3D visual in the open scenes (e.g. a billboard player).</summary>
    public static string RebuildActiveFromLibrary()
    {
        var pose = UnityEngine.Object.FindFirstObjectByType<Character3DPose>();
        if (pose == null) return null;
        var anim = pose.GetComponent<Animator>();
        if (anim == null) return null;

        string curPath = anim.runtimeAnimatorController != null
            ? AssetDatabase.GetAssetPath(anim.runtimeAnimatorController) : "";
        AnimatorController ctrl = curPath == Use3DCaster.ControllerPath
            ? Use3DCaster.BuildController()
            : Use3DCharacter.BuildFullController(AssetDatabase.GetAssetPath(anim.avatar));
        if (ctrl == null) return null;

        anim.runtimeAnimatorController = ctrl;   // reattach (CreateAnimatorControllerAtPath returns a fresh asset ref)
        if (Application.isPlaying) pose.Reinitialize();
        else UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pose.gameObject.scene);
        EditorUtility.SetDirty(anim);
        return pose.gameObject.name;
    }
}
