using UnityEngine;

/// <summary>
/// Global, live-tunable knobs for how billboard sprites pick up real-time additional lights (lamps/torches),
/// pushed into <c>Unwritten/BillboardLit</c> via <see cref="Shader.SetGlobalFloat"/>. One set of dials tunes
/// every billboard at once (same pattern as DayNightLight's <c>_SpriteEnv</c>), so there's nothing to hunt for
/// in per-material inspectors — adjust them from the DEV panel (P ▸ Sprite Light).
///
/// Seeded to defaults before the first frame (play) and on every editor reload, so sprites are never multiplied
/// by an unset (0) global when no one has driven them yet.
/// </summary>
public static class SpriteAddLight
{
    static readonly int StrengthId   = Shader.PropertyToID("_AddLightStrength");
    static readonly int ShapeId      = Shader.PropertyToID("_AddLightShape");
    static readonly int BulgeId      = Shader.PropertyToID("_AddLightBulge");
    static readonly int AlbedoTintId = Shader.PropertyToID("_AddLightAlbedoTint");
    static readonly int SelfShadowBiasId = Shader.PropertyToID("_SelfShadowBias");

    [Tooltip("Overall strength of the added lamp light (0 = legacy: sprites ignore additional lights).")]
    public static float Strength = 1.44f;
    [Tooltip("0 = whole sprite lit flat · 1 = fully directional (only the lamp-facing side lit).")]
    public static float Shape = 1f;
    [Tooltip("Fake-normal roundness: low = flat/sharply sided, high = soft wrap around the body.")]
    public static float Bulge = 2f;
    [Tooltip("How much the added light is tinted by the sprite's own colour (0 = pure lamp colour even on a black body, 1 = physically-correct).")]
    public static float AlbedoTint = 0.92f;
    [Tooltip("Metres the shadow-receive sample is pushed toward the sun so the billboard stops sampling its OWN cast-shadow proxy (the dark band climbing the body). Bigger = cleaner self, but coarser tree shade.")]
    public static float SelfShadowBias = 3f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Apply()
    {
        Shader.SetGlobalFloat(StrengthId, Strength);
        Shader.SetGlobalFloat(ShapeId, Shape);
        Shader.SetGlobalFloat(BulgeId, Bulge);
        Shader.SetGlobalFloat(AlbedoTintId, AlbedoTint);
        Shader.SetGlobalFloat(SelfShadowBiasId, SelfShadowBias);
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    static void SeedInEditor() => Apply();
#endif
}
