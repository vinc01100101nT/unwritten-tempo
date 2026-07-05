using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Resolves a Stance Composer clip NAME to a shared <b>Humanoid</b> animation clip, so every live-3D character
/// retargets the SAME clip library onto its own avatar (Humanoid→Humanoid retarget is automatic in Unity). This
/// is the animation source for the committed live-3D player/NPCs — the Quaternius-embedded-Generic path
/// (<see cref="Character3DRigBuilder.LoadClips"/>) was the Generic-rig path; Mixamo-rigged bodies carry no embedded clips.
///
/// <para>Clips live in <see cref="ClipDir"/> as Humanoid-imported FBX/anim assets. A clip matches a composer string
/// by cleaned name (case-insensitive, spaces/underscores stripped, any "mixamo.com" dropped), so naming a file
/// <c>Idle</c>/<c>SwordSlash</c>/<c>Jump</c>… to match the Stance Composer's clip strings is all the wiring needed.</para>
/// </summary>
public static class HumanoidClipResolver
{
    public const string ClipDir = "Assets/Art/Quat/Anim/Humanoid";

    /// <summary>All Humanoid clips in <see cref="ClipDir"/>, keyed by the FILE name (not the clip's internal name —
    /// Mixamo clips are often internally called "mixamo.com"; the file name is what the author controls, e.g.
    /// Run.fbx → "run"). Rebuilt each call (edit-time only).</summary>
    static Dictionary<string, AnimationClip> LoadClips()
    {
        var map = new Dictionary<string, AnimationClip>();
        if (!AssetDatabase.IsValidFolder(ClipDir)) return map;

        // Both FBX (t:Model) and standalone .anim (t:AnimationClip) live here; scan by file and take its first clip.
        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            string key = Key(Path.GetFileNameWithoutExtension(p));
            if (string.IsNullOrEmpty(key) || map.ContainsKey(key)) continue;
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(p))
                if (a is AnimationClip clip && !clip.name.StartsWith("__preview"))
                { map[key] = clip; break; }   // the file's playable clip
        }
        return map;
    }

    /// <summary>The composer-facing clip names available (the file base-names in <see cref="ClipDir"/>), so the Stance
    /// Composer can list them in its dropdown. These are what the author picks per stance; the resolver matches them
    /// back to the clip by cleaned name.</summary>
    public static IEnumerable<string> ClipNames()
    {
        if (!AssetDatabase.IsValidFolder(ClipDir)) yield break;
        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipDir }))
            yield return Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
    }

    /// <summary>A resolver closure for <see cref="Character3DControllerBuilder.Build"/> (pass with quat:null): composer
    /// clip name → Humanoid clip Motion, or null if the folder has no matching clip (caller skips + warns).</summary>
    public static System.Func<string, Motion> Resolver()
    {
        var map = LoadClips();
        return name =>
        {
            if (string.IsNullOrEmpty(name)) return null;
            return map.TryGetValue(Key(name), out var c) ? c : null;
        };
    }

    /// <summary>How many Humanoid clips are available (0 → the folder is empty; the player will just T-pose/rest).</summary>
    public static int Count() => LoadClips().Count;

    // Normalise both sides of the match: drop the mixamo tag, lower-case, strip spaces/underscores/dashes and any
    // "Take 001" suffix, so "mixamo.com|Sword Slash" and a composer "SwordSlash" collapse to the same key.
    static string Key(string n)
    {
        if (string.IsNullOrEmpty(n)) return "";
        int bar = n.LastIndexOf('|');
        if (bar >= 0) n = n.Substring(bar + 1);
        n = n.Replace("mixamo.com", "");
        var sb = new System.Text.StringBuilder(n.Length);
        foreach (char ch in n)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        string k = sb.ToString();
        if (k.EndsWith("take001")) k = k.Substring(0, k.Length - "take001".Length);
        return k;
    }
}
