using UnityEngine;

/// <summary>
/// The ONE place that answers "where is a character's hand bone?" — so weapon mounting (runtime
/// <see cref="WeaponSocket"/> and the author-time grip tuner) can't drift on how the bone is found. Prefers the
/// Humanoid avatar's mapped bone (the committed player/NPC rigs are Mixamo-Humanoid); falls back to a name search
/// for any Generic rig so it degrades gracefully instead of returning null. Called only on equip changes / at
/// author time — never a per-frame path.
/// </summary>
public static class HandSocketResolver
{
    public static Transform Find(Animator anim, WeaponHand hand)
    {
        if (anim == null) return null;
        if (anim.isHuman)
        {
            var b = anim.GetBoneTransform(hand == WeaponHand.Left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (b != null) return b;
        }
        return FindByName(anim.transform, hand == WeaponHand.Left);
    }

    // Generic-rig fallback: first descendant that reads like a hand/wrist bone on the requested side.
    static Transform FindByName(Transform t, bool left)
    {
        string n = t.name.ToLowerInvariant();
        bool isHand = n.Contains("hand") || n.Contains("wrist") || n.Contains("palm");
        bool sideOk = left
            ? (n.Contains("left")  || n.Contains(".l") || n.Contains("_l") || n.EndsWith("l"))
            : (n.Contains("right") || n.Contains(".r") || n.Contains("_r") || n.EndsWith("r"));
        if (isHand && sideOk) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var f = FindByName(t.GetChild(i), left);
            if (f != null) return f;
        }
        return null;
    }
}
