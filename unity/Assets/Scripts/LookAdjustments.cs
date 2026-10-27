using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// THE PLAYER'S BRIGHTNESS, CONTRAST AND SATURATION (Settings ▸ Graphics), laid on every map's own look. A map's look
/// is the colour profile on its global volume; each choice is added to what that profile says, on a copy made for
/// this play session (<see cref="Volume.profile"/>), so the saved profile is never changed and 0 shows the look exactly
/// as it was made.
///
/// <para>Cost: nothing per frame. It runs when a map loads and when a slider moves, and then only rewrites three
/// numbers on each global volume. The colour grading pass that reads them runs every frame regardless.</para>
/// </summary>
public static class LookAdjustments
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        GameSettings.LookChanged -= Apply;
        GameSettings.LookChanged += Apply;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

    /// <summary>Lay the player's choices on every global volume loaded now.</summary>
    public static void Apply()
    {
        // Nobody is looking at a picture on a server or on a game started with no window.
        if (GameServer.IsServer || Application.isBatchMode) return;
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {
            if (!v.isGlobal || v.sharedProfile == null) continue;
            if (!v.sharedProfile.TryGet(out ColorAdjustments made)) continue;
            // Only a profile that is a map's LOOK — one that sets brightness, contrast or saturation itself — is adjusted.
            // A volume that sets none of them (the time-of-day tint) is left alone, or its high priority would carry
            // these three over the look's own.
            if (!made.postExposure.overrideState && !made.contrast.overrideState && !made.saturation.overrideState) continue;
            if (!v.profile.TryGet(out ColorAdjustments shown)) continue;
            Set(shown.postExposure, LookMath.Adjusted(made.postExposure.value, GameSettings.LookBrightness, float.MinValue, float.MaxValue));
            Set(shown.contrast, LookMath.Adjusted(made.contrast.value, GameSettings.LookContrast, -100f, 100f));
            Set(shown.saturation, LookMath.Adjusted(made.saturation.value, GameSettings.LookSaturation, -100f, 100f));
        }
    }

    static void Set(FloatParameter p, float value)
    {
        p.overrideState = true;
        p.value = value;
    }
}

/// <summary>The picture's own arithmetic, kept apart so it can be checked by calling it with numbers.</summary>
public static class LookMath
{
    /// <summary>A look's own value with the player's amount added, kept inside what the setting accepts.</summary>
    public static float Adjusted(float made, float added, float min, float max) => Mathf.Clamp(made + added, min, max);
}
