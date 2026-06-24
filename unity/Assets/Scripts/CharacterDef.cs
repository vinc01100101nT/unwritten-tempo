using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The reproducible recipe for ONE character: which rigged model, which clip drives each
/// <see cref="CharacterAction"/>, the bake look, and the runtime grounding/scale. Edit it, hit
/// <b>Bake &amp; Build Prefab</b> (the custom inspector button — see <c>CharacterBuilder</c>) and it
/// (re)bakes every action's 8-direction strips next to this asset and (re)writes a ready-to-drop
/// <see cref="BakedBillboard8"/> prefab. Same SO + button flow as the map recipe pipeline.
///
/// Convention: create one of these inside <c>Assets/Art/Characters/&lt;Name&gt;/</c> so the asset, its
/// <c>Sprites/</c> folder, and the built prefab all live together.
/// </summary>
[CreateAssetMenu(fileName = "New Character", menuName = "unwritten/Character", order = 30)]
public class CharacterDef : ScriptableObject
{
    [System.Serializable]
    public class ActionDef
    {
        public CharacterAction action = CharacterAction.Idle;
        [Tooltip("Use the clip from the SAME FBX as the model (AnimationMode matches by bone name).")]
        public AnimationClip clip;
        [Range(4, 60)] public int fps = 12;
        [Tooltip("0 = auto (clip length × fps).")]
        public int frames = 0;
    }

    [Header("Source")]
    public GameObject model;        // rigged FBX
    [Tooltip("Optional: separate palette PNG (Quaternius). Auto-applied at bake if the model bakes grey.")]
    public Texture2D albedo;

    [Header("Actions — each baked into 8 directional strips")]
    public List<ActionDef> actions = new List<ActionDef>
    {
        new ActionDef { action = CharacterAction.Idle, fps = 12 },
        new ActionDef { action = CharacterAction.Walk, fps = 18 },
    };

    [Header("Bake look")]
    public int cellSize = 96;
    public bool pixelate = true;
    [Range(1, 8)] public int superSample = 4;
    [Range(0f, 75f)] public float cameraPitch = 30f;
    [Range(1f, 2.5f)] public float framePadding = 1.25f;
    [Range(0f, 360f)] public float yawOffset = 180f;
    public bool clockwise = true;

    [Header("Runtime (written onto the prefab's BakedBillboard8)")]
    [Tooltip("Lift so the feet rest on the transform; ~0.45 × sprite height.")]
    public float feetOffset = 0.45f;
    public float worldScale = 1.7f;
    public float walkThreshold = 0.15f;
    public float runThreshold = 3.5f;
}
