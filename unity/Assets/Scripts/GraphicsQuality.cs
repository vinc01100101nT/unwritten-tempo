using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// THE PLAYER'S GRAPHICS CHOICES THAT LIVE IN THE RENDER SETTINGS (Settings ▸ Graphics ▸ Shadow quality). The game
/// works on a COPY of the render settings, made the moment it starts and handed to the renderer, so a choice never
/// changes the project's saved render settings — in the editor a play session leaves them exactly as they were, and
/// the copy is let go and the original handed back when the game stops.
/// </summary>
public static class GraphicsQuality
{
    static UniversalRenderPipelineAsset original, copy;
    static bool originalWasTheQualityLevels;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        Apply();
        Application.quitting -= PutBack;
        Application.quitting += PutBack;
    }

    /// <summary>Push the saved choices onto the renderer. Called as the game starts and whenever one changes.</summary>
    public static void Apply()
    {
        if (!Application.isPlaying) return;
        if (copy == null)
        {
            var level = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            original = level != null ? level : GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (original == null) return;
            originalWasTheQualityLevels = level != null;
            copy = Object.Instantiate(original);
            copy.name = original.name + " (this game's own)";
            copy.hideFlags = HideFlags.DontSave;
            QualitySettings.renderPipeline = copy;
        }
        copy.mainLightShadowmapResolution = GameSettings.ShadowResolutions[GameSettings.ShadowQuality];
    }

    static void PutBack()
    {
        if (copy == null) return;
        QualitySettings.renderPipeline = originalWasTheQualityLevels ? original : null;
        Object.DestroyImmediate(copy);
        copy = null;
    }
}
