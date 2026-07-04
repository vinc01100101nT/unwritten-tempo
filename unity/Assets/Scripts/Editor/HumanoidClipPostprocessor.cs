using UnityEditor;

/// <summary>
/// Auto-prepares any animation FBX dropped into the shared Humanoid clip folder (<see cref="HumanoidClipResolver.ClipDir"/>):
/// on import it forces Animation Type = Humanoid and applies the feet-planted, looped clip-root settings — so a new
/// Mixamo clip is game-ready the instant it lands, with ZERO menu clicks. The manual
/// <c>Tools ▸ unwritten ▸ Rigs ▸ Fix Humanoid Clip Roots</c> stays as the one-click fixer for clips imported before
/// this existed; both share <see cref="BulkHumanoidRig.ApplyClipRoots"/> so the settings can't drift.
/// </summary>
public class HumanoidClipPostprocessor : AssetPostprocessor
{
    bool InClipDir => assetPath != null && assetPath.Replace('\\', '/')
        .StartsWith(HumanoidClipResolver.ClipDir + "/", System.StringComparison.OrdinalIgnoreCase);

    // Runs before the rig/clips are built — set the rig type here so the Humanoid avatar + retarget are created.
    void OnPreprocessModel()
    {
        if (!InClipDir) return;
        var imp = (ModelImporter)assetImporter;
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
    }

    // Runs while clips are being built — pin every clip's root (bake-to-feet + loop) via the shared settings, and
    // NAME the clip after its FILE. Mixamo takes are all internally called "mixamo.com", so without this every clip
    // ends up named "mixamo.com" — collapsing Character3DPose's clip-length map onto one key AND breaking the one-shot
    // state lookup (the controller names one-shot states by the file/composer string, but the pose keys stateHash by
    // clip.name). Naming the clip = its file makes clip.name match the state name, so Attack/Roll/etc. actually fire.
    void OnPreprocessAnimation()
    {
        if (!InClipDir) return;
        var imp = (ModelImporter)assetImporter;
        var clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
        string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        foreach (var c in clips)
        {
            BulkHumanoidRig.ApplyClipRoots(c);
            if (clips.Length == 1) c.name = fileName;   // single-take Mixamo FBX → clip name == file (the composer string)
        }
        imp.clipAnimations = clips;
    }
}
