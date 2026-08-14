using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared read-only helpers for Quaternius 3D character models: load their embedded clips/avatar and
/// measure world height, used by the live-3D player build path (<see cref="Player3DVisualBuilder"/>,
/// <see cref="StanceComposerWindow"/>). It does NOT force Humanoid — the pack ships a Generic rig whose
/// embedded takes match its own skeleton; Humanoid is only needed to retarget FOREIGN (Mixamo) clips.
/// </summary>
public static class Character3DRigBuilder
{
    // Quaternius FBX import several units tall (~3.3u); auto-fit every model to this world height instead.
    // READS THE SHARED SPEC, so the player this builder makes stands exactly as tall as the townsfolk and shadow mobs
    // built at runtime. It used to be its own 0.327 const sitting beside the spec's own 0.327 — two owners of one
    // number, agreeing only because they were typed the same, so retuning the spec would have silently resized
    // everyone EXCEPT the player.
    internal static float TargetHeight => CharacterSpec.Active.bodyHeight;

    // Every AnimationClip sub-asset of the FBX, keyed by CLEAN name (skips Unity's __preview clips).
    // Quaternius names its takes "CharacterArmature|Idle" (armature prefix) — CleanName strips that so
    // callers look up "Idle"/"Walk"/etc.
    internal static Dictionary<string, AnimationClip> LoadClips(string modelPath)
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

    internal static Avatar LoadAvatar(string modelPath)
    {
        foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath))
            if (o is Avatar a) return a;
        return null;
    }

    // World-space rendered height (union of all renderer bounds) — measured at the caller's current scale.
    internal static float MeasureHeight(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return 0f;
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b.size.y;
    }
}
