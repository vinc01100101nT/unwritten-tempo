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
    const string LibPath = "Assets/3_Data/Resources/StanceLibrary.asset";

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
        var ctrl = EmptyControllerAt(controllerPath);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        var sm = ctrl.layers[0].stateMachine;

        // Locomotion blend tree from the Idle/Walk/Run STANCE clips (author-picked). A missing clip is left out of
        // the blend (unassigned Idle → the tree rests on whatever remains, or the bind pose).
        string idleClip = ClipFor(lib, StanceId.Idle), walkClip = ClipFor(lib, StanceId.Walk),
               runClip = ClipFor(lib, StanceId.Run), sprintClip = ClipFor(lib, StanceId.Sprint);
        var loco = sm.AddState("Locomotion");
        var blend = new BlendTree { name = "Locomotion", blendType = BlendTreeType.Simple1D,
                                    blendParameter = "Speed", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(blend, ctrl);
        AddLoco(blend, Resolve(idleClip,   quat, resolver), 0f);
        AddLoco(blend, Resolve(walkClip,   quat, resolver), 0.5f);
        AddLoco(blend, Resolve(runClip,    quat, resolver), 1f);
        // Sprint is a 4th loco gait (Speed=2) — like Walk/Run it lives in the blend, matched by the composer clip
        // STRING (Character3DPose.sprintClip), NOT a dedicated state. This is why a distinct Sprint clip animates.
        AddLoco(blend, Resolve(sprintClip, quat, resolver), 2f);
        loco.motion = blend;
        sm.defaultState = loco;

        // One state per DISTINCT non-empty stance clip that isn't already a locomotion clip, NAMED by the clip so the
        // pose CrossFades to the state whose name == the composer string.
        var locoClips = new HashSet<string> { idleClip, walkClip, runClip, sprintClip };
        var made = new HashSet<string>();
        if (lib != null && lib.stances != null)
            foreach (var b in lib.stances)
            {
                if (b == null || string.IsNullOrEmpty(b.clip)) continue;
                if (locoClips.Contains(b.clip)) continue;           // already in the blend tree
                if (!made.Add(b.clip)) continue;                    // distinct only
                var m = Resolve(b.clip, quat, resolver);
                if (m == null) { Debug.LogWarning($"[unwritten] stance clip '{b.clip}' not found on this model/Mixamo set — skipped."); continue; }
                var st = sm.AddState(b.clip);
                st.motion = m;
                st.mirror = b.mirrored;
            }

        if (writeDefaultsOff)
            foreach (var cs in sm.states) cs.state.writeDefaultValues = false;

        AddWeaponLayers(ctrl, lib, quat, resolver, writeDefaultsOff);

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    /// <summary>The weapon layers (<see cref="StanceLibrary.weaponStances"/>), each with one state per distinct clip
    /// NAMED by the clip, starting at weight 0 on an empty state — <see cref="Character3DPose"/> eases them in:
    /// layer 1 "Carry", masked to the arms and fingers, plays each weapon's carry pose over the shared travel legs;
    /// layer 2 "Stand", the whole body, plays each weapon's own standing idle;
    /// layer 3 "Draw", the arms again, plays each weapon's put-away and take-out clips over whatever the legs are doing,
    /// scrubbed by the <see cref="DrawTimeParam"/> parameter (the draw's progress) rather than by the clock, because the
    /// draw lasts as long as the weapon's weight says, not as long as the clip;
    /// layers 4 "CarryRight" and 5 "CarryLeft", one arm each, play the carry pose of a weapon whose pose holds only that
    /// arm (<see cref="WeaponStanceBinding.carryArms"/>) — the other arm keeps the travel clip's swing;
    /// layer 6 "DrawUpper" is layer 3 for a weapon whose put-away turns the torso and head too.</summary>
    static void AddWeaponLayers(AnimatorController ctrl, StanceLibrary lib, Dictionary<string, AnimationClip> quat,
                              Func<string, Motion> resolver, bool writeDefaultsOff)
    {
        var mask = ArmsMask(ctrl, "Arms", true, true);
        AddWeaponLayer(ctrl, "Carry", mask, lib, c => CarryOn(c, CarryArms.Both), null, quat, resolver, writeDefaultsOff);
        AddWeaponLayer(ctrl, "Stand", null, lib, c => new[] { c.idleClip }, null, quat, resolver, writeDefaultsOff);
        ctrl.AddParameter(DrawTimeParam, AnimatorControllerParameterType.Float);
        AddWeaponLayer(ctrl, "Draw", mask, lib, c => c.putAwayMovesUpperBody ? new string[0] : new[] { c.sheatheClip, c.unsheatheClip }, DrawTimeParam, quat, resolver, writeDefaultsOff);
        // A carry pose that holds one arm sits on that arm's own layer, so the other arm swings with the legs.
        AddWeaponLayer(ctrl, "CarryRight", ArmsMask(ctrl, "RightArm", false, true), lib, c => CarryOn(c, CarryArms.Right), null, quat, resolver, writeDefaultsOff);
        AddWeaponLayer(ctrl, "CarryLeft", ArmsMask(ctrl, "LeftArm", true, false), lib, c => CarryOn(c, CarryArms.Left), null, quat, resolver, writeDefaultsOff);
        // Layer 6 "DrawUpper": the put-away and take-out of a weapon whose move turns the torso and head as well
        // (WeaponStanceBinding.putAwayMovesUpperBody) — the arms, the body and the head; the legs keep their own motion.
        var upper = ArmsMask(ctrl, "UpperBody", true, true);
        upper.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
        upper.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
        AddWeaponLayer(ctrl, "DrawUpper", upper, lib, c => c.putAwayMovesUpperBody ? new[] { c.sheatheClip, c.unsheatheClip } : new string[0], DrawTimeParam, quat, resolver, writeDefaultsOff);
    }

    static string[] CarryOn(WeaponStanceBinding c, CarryArms layer) => c.carryArms == layer ? new[] { c.carryClip } : new string[0];

    static AvatarMask ArmsMask(AnimatorController ctrl, string name, bool left, bool right)
    {
        var mask = new AvatarMask { name = name };
        for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++)
            mask.SetHumanoidBodyPartActive(p, (left && (p == AvatarMaskBodyPart.LeftArm || p == AvatarMaskBodyPart.LeftFingers)) ||
                                              (right && (p == AvatarMaskBodyPart.RightArm || p == AvatarMaskBodyPart.RightFingers)));
        AssetDatabase.AddObjectToAsset(mask, ctrl);
        return mask;
    }

    /// <summary>The parameter the "Draw" layer's states are scrubbed by — read by <see cref="Character3DPose"/>.</summary>
    public const string DrawTimeParam = Character3DPose.DrawTimeParam;

    // The layer list is a copy, so a new layer's settings are written back as a whole.
    static void AddWeaponLayer(AnimatorController ctrl, string name, AvatarMask mask, StanceLibrary lib,
                               Func<WeaponStanceBinding, string[]> clipsOf, string timeParam, Dictionary<string, AnimationClip> quat,
                               Func<string, Motion> resolver, bool writeDefaultsOff)
    {
        ctrl.AddLayer(name);
        int li = ctrl.layers.Length - 1;
        var layers = ctrl.layers;
        layers[li].avatarMask = mask;
        layers[li].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[li].defaultWeight = 0f;
        ctrl.layers = layers;

        var csm = ctrl.layers[li].stateMachine;
        csm.defaultState = csm.AddState("Empty");
        var made = new HashSet<string>();
        if (lib != null && lib.weaponStances != null)
            foreach (var c in lib.weaponStances)
            {
                if (c == null) continue;
                foreach (var clip in clipsOf(c))
                {
                    if (string.IsNullOrEmpty(clip) || !made.Add(clip)) continue;
                    var m = Resolve(clip, quat, resolver);
                    if (m == null) { Debug.LogWarning($"[unwritten] weapon {name} clip '{clip}' not found on this model/Mixamo set — skipped."); continue; }
                    var st = csm.AddState(clip);
                    st.motion = m;
                    if (timeParam != null) { st.timeParameterActive = true; st.timeParameter = timeParam; }
                }
            }
        if (writeDefaultsOff)
            foreach (var cs in csm.states) cs.state.writeDefaultValues = false;
    }

    /// <summary>The controller at <paramref name="path"/>, emptied to a single base layer and no parameters — the SAME
    /// object when it already exists, so everything that holds it (a scene's Animator, the character spec) stays
    /// linked. Writing a new controller over the path keeps the file's ID but destroys the object those links point at
    /// in memory, and the next save writes them blank: every character T-posed in game on 2026-10-09 that way.</summary>
    static AnimatorController EmptyControllerAt(string path)
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (ctrl == null) return AnimatorController.CreateAnimatorControllerAtPath(path);
        // Everything inside the controller file except the controller itself: its layers' state machines, states,
        // transitions, blend trees and mask from the last build. LoadAllAssets, not the visible-only listing: layer
        // state machines are stored hidden, and skipping them left every rebuild's old copy behind in the file.
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
            if (sub != null && sub != ctrl) UnityEngine.Object.DestroyImmediate(sub, true);
        ctrl.layers = new AnimatorControllerLayer[0];
        ctrl.parameters = new AnimatorControllerParameter[0];
        ctrl.AddLayer("Base Layer");
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
        var pose = UnityEngine.Object.FindAnyObjectByType<Character3DPose>();
        if (pose == null) return null;
        var anim = pose.GetComponent<Animator>();
        if (anim == null) return null;

        // Single committed path: rebuild the shared Humanoid player controller from the authored library.
        AnimatorController ctrl = Player3DVisualBuilder.BuildController();
        if (ctrl == null) return null;

        anim.runtimeAnimatorController = ctrl;   // reattach (CreateAnimatorControllerAtPath returns a fresh asset ref)
        // Every body shares this controller, so in Play every body re-reads it — not only the one found above.
        if (Application.isPlaying)
            foreach (var p in UnityEngine.Object.FindObjectsByType<Character3DPose>(FindObjectsSortMode.None)) p.Reinitialize();
        else UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pose.gameObject.scene);
        EditorUtility.SetDirty(anim);
        return pose.gameObject.name;
    }
}
