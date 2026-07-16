using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bulk-sets rigged Mixamo FBX to Animation Type = Humanoid + Avatar = Create From This Model,
/// then reimports — the one-click complement to tools/mixamo_batch_rig.py so you never open the
/// Rig tab per file. Operates on selected FBX, else the whole DefaultFolder.
/// </summary>
public static class BulkHumanoidRig
{
    const string DefaultFolder = "Assets/1_Authored/Art/Characters/Models";

    [MenuItem("Tools/unwritten/Rigs/Bulk Set Humanoid (Rigged Mixamo)", priority = 621)]
    public static void SetHumanoid()
    {
        var paths = CollectFbx();
        if (paths.Count == 0)
        {
            EditorUtility.DisplayDialog("Bulk Set Humanoid",
                "No FBX found. Select FBX in the Project window, or put them in " + DefaultFolder + ".",
                "OK");
            return;
        }

        int changed = 0, skipped = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < paths.Count; i++)
            {
                string p = paths[i];
                EditorUtility.DisplayProgressBar("Bulk Set Humanoid",
                    Path.GetFileName(p), (float)i / paths.Count);

                var imp = AssetImporter.GetAtPath(p) as ModelImporter;
                if (imp == null) { skipped++; continue; }

                bool needs = imp.animationType != ModelImporterAnimationType.Human
                          || imp.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel;
                if (!needs) { skipped++; continue; }

                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.SaveAndReimport();
                changed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[BulkHumanoidRig] Humanoid set on {changed} FBX ({skipped} already-correct/skipped) of {paths.Count}.");
    }

    [MenuItem("Tools/unwritten/Rigs/Validate Humanoid Avatars (Rigged Mixamo)", priority = 622)]
    public static void ValidateAvatars()
    {
        var paths = CollectFbx();
        if (paths.Count == 0)
        {
            EditorUtility.DisplayDialog("Validate Humanoid Avatars",
                "No FBX found. Select FBX, or put them in " + DefaultFolder + ".", "OK");
            return;
        }

        int good = 0;
        var bad = new List<string>();
        for (int i = 0; i < paths.Count; i++)
        {
            string p = paths[i];
            EditorUtility.DisplayProgressBar("Validate Humanoid Avatars",
                Path.GetFileName(p), (float)i / paths.Count);

            var imp = AssetImporter.GetAtPath(p) as ModelImporter;
            if (imp == null || imp.animationType != ModelImporterAnimationType.Human)
            {
                bad.Add(Path.GetFileName(p) + "  (not set to Humanoid)");
                continue;
            }

            // The Avatar sub-asset is only valid+human when every required bone mapped (all green).
            Avatar avatar = null;
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(p))
                if (a is Avatar av) { avatar = av; break; }

            if (avatar != null && avatar.isValid && avatar.isHuman) good++;
            else bad.Add(Path.GetFileName(p) + "  (invalid/incomplete avatar — check red bones)");
        }
        EditorUtility.ClearProgressBar();

        if (bad.Count == 0)
            Debug.Log($"[BulkHumanoidRig] ✅ All {good} Humanoid avatars valid (all green).");
        else
            Debug.LogWarning($"[BulkHumanoidRig] {good} valid, {bad.Count} need attention:\n  - "
                + string.Join("\n  - ", bad));
    }

    [MenuItem("Tools/unwritten/Rigs/Fix Humanoid Clip Roots (Anim folder)", priority = 624)]
    public static void FixClipRoots()
    {
        // Selection wins; else every FBX in the shared Humanoid clip folder.
        var paths = new List<string>();
        foreach (var o in Selection.objects)
        {
            string p = AssetDatabase.GetAssetPath(o);
            if (IsFbx(p)) paths.Add(p);
        }
        if (paths.Count == 0 && AssetDatabase.IsValidFolder(HumanoidClipResolver.ClipDir))
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { HumanoidClipResolver.ClipDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (IsFbx(p)) paths.Add(p);
            }
        if (paths.Count == 0)
        {
            EditorUtility.DisplayDialog("Fix Humanoid Clip Roots",
                "No animation FBX found. Select some, or drop them in " + HumanoidClipResolver.ClipDir + ".", "OK");
            return;
        }

        int done = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < paths.Count; i++)
            {
                string p = paths[i];
                EditorUtility.DisplayProgressBar("Fix Humanoid Clip Roots", Path.GetFileName(p), (float)i / paths.Count);
                var imp = AssetImporter.GetAtPath(p) as ModelImporter;
                if (imp == null) continue;

                imp.animationType = ModelImporterAnimationType.Human;   // Humanoid = retargets to any rigged avatar
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

                // Take the current clip defs (author-edited, else the importer's defaults) and pin the root so feet
                // sit at the model origin (the un-bury fix): bake Y into pose based upon FEET, bake XZ + rotation too,
                // and loop locomotion. Then write them back as explicit clip animations.
                var clips = imp.clipAnimations;
                if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
                foreach (var c in clips) ApplyClipRoots(c);
                imp.clipAnimations = clips;
                imp.SaveAndReimport();
                done++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }
        Debug.Log($"[BulkHumanoidRig] Fixed clip roots (Humanoid + bake-to-feet + loop) on {done}/{paths.Count} FBX.");
    }

    /// <summary>The canonical Humanoid clip-root settings (feet-planted, looped) — shared by the manual tool AND the
    /// auto-import postprocessor so they can't drift. NOTE: keepOriginalPositionY (Original) takes precedence over
    /// heightFromFeet, so it MUST be cleared or "Based Upon (Y)" stays on Original and the character buries.</summary>
    public static void ApplyClipRoots(ModelImporterClipAnimation c)
    {
        c.loopTime = true;
        c.lockRootRotation = true;   c.keepOriginalOrientation = false;   // Bake rotation, Based Upon = Body Orientation
        c.lockRootHeightY = true;    c.keepOriginalPositionY = false;   c.heightFromFeet = true;   // Bake Y, Based Upon = Feet ← un-bury
        c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = false;   // Bake XZ, Based Upon = Center of Mass
    }

    static List<string> CollectFbx()
    {
        var paths = new List<string>();

        // 1) explicit selection wins
        foreach (var o in Selection.objects)
        {
            string p = AssetDatabase.GetAssetPath(o);
            if (IsFbx(p)) paths.Add(p);
        }
        if (paths.Count > 0) return paths;

        // 2) fall back to the default rigged folder
        if (AssetDatabase.IsValidFolder(DefaultFolder))
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { DefaultFolder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (IsFbx(p)) paths.Add(p);
            }
        return paths;
    }

    static bool IsFbx(string p) =>
        !string.IsNullOrEmpty(p) && p.ToLowerInvariant().EndsWith(".fbx");
}
