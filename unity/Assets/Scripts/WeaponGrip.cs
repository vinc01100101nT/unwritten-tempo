using UnityEngine;

/// <summary>Which hand a weapon is wielded in. Right = the default one-hand grip; Left is reserved for the
/// off-hand / shield pass. Kept separate from <see cref="EquipSlotType"/> because a slot doesn't imply a hand.</summary>
public enum WeaponHand { Right, Left }

/// <summary>
/// The SINGLE SOURCE OF TRUTH for how a weapon MESH sits in the wielding hand — authored once on the weapon
/// prefab's root and read at runtime by <see cref="WeaponSocket"/>. The offset is LOCAL to the hand bone, so it
/// rides every animation frame automatically (Humanoid skinning does the work; no per-frame code).
///
/// <para>Author these visually with <c>Tools ▸ unwritten ▸ Weapons ▸ Weapon Grip Tuner</c> — never hand-type the
/// numbers. Because the grip lives on the mesh prefab, every <see cref="Item"/> that references the same model
/// shares one grip (no per-item copy to hand-sync).</para>
/// </summary>
[DisallowMultipleComponent]
public class WeaponGrip : MonoBehaviour
{
    [Tooltip("Which hand bone this weapon parents under.")]
    public WeaponHand hand = WeaponHand.Right;
    [Tooltip("Local position offset from the hand bone (so the hilt sits in the palm, not through it).")]
    public Vector3 localPosition;
    [Tooltip("Local rotation (Euler) relative to the hand bone.")]
    public Vector3 localEuler;
    [Tooltip("Local scale under the hand bone (leave 1,1,1 unless the mesh is authored at the wrong size).")]
    public Vector3 localScale = Vector3.one;

    // ---- PUT AWAY: where the weapon rests on the body while its holder is at peace ----------------------------------
    // Local to the body bone below, so it rides the body's every move. Drawn at the same size as in the hand.

    [Header("Put away (on the body)")]
    [Tooltip("True once the put-away place below has been set. A weapon without one is hidden while put away.")]
    public bool stowAuthored;
    [Tooltip("The body bone the weapon rests on while put away.")]
    public HumanBodyBones stowBone = HumanBodyBones.Hips;
    [Tooltip("Where the weapon rests, local to the bone above.")]
    public Vector3 stowPosition;
    [Tooltip("How it is turned there, local to the bone above.")]
    public Vector3 stowEuler;
    [Tooltip("A pair weapon (Katar, Axe): where the copy in the other hand rests, on the same bone.")]
    public Vector3 stowPairPosition;
    [Tooltip("A pair weapon: how that copy is turned there.")]
    public Vector3 stowPairEuler;
}
