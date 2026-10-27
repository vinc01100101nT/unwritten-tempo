using UnityEngine;

/// <summary>
/// THE store for player-facing options chosen in the Settings window (System ▸ Settings). One owner per
/// option: the systems those options affect READ from here (or are pushed from here by
/// <see cref="ApplyAudio"/>) instead of keeping their own copy, so a setting can never drift out of sync
/// with what the game is actually doing.
///
/// Values persist across sessions in <see cref="PlayerPrefs"/> and are cached in memory on first read, so a
/// per-frame reader (e.g. <see cref="CameraFollow3D"/>) never touches disk. Writes go into PlayerPrefs'
/// in-memory cache only — Unity flushes it to disk on quit, the same way window positions are kept
/// (<see cref="WindowFrame"/>). Nothing here calls <c>PlayerPrefs.Save()</c>, because a volume slider fires
/// on every drag frame and flushing the whole prefs file at 60 Hz would stutter.
///
/// JS/TS analogy: a tiny settings module backed by localStorage, read through getters.
/// </summary>
public static class GameSettings
{
    const string SmoothCameraFollowKey = "set_smooth_camera_follow";
    const string DontAskQuantityKey = "set_shop_dont_ask_quantity";
    const string QuickCastSingleKey    = "set_quickcast_single";
    const string QuickCastGroundKey    = "set_quickcast_ground";
    const string MasterVolumeKey       = "set_volume_master";
    const string SfxVolumeKey          = "set_volume_sfx";
    const string MusicVolumeKey        = "set_volume_music";
    const string ScrollSensitivityKey  = "set_scroll_sensitivity";
    const string HideOtherCallsKey     = "set_hide_other_skill_calls";
    const string ShadowQualityKey      = "set_shadow_quality";
    const string PlantDistanceKey      = "set_plant_distance";
    const string PlantDensityKey       = "set_plant_density";
    const string PlantShadowsKey       = "set_plant_shadows";
    const string ShowPerformanceKey    = "set_show_performance";
    const string LookBrightnessKey     = "set_look_brightness";
    const string LookContrastKey       = "set_look_contrast";
    const string LookSaturationKey     = "set_look_saturation";
    const string FogThicknessKey       = "set_fog_thickness";

    public const float ScrollSensitivityMin = 0.25f;
    public const float ScrollSensitivityMax = 3f;

    static bool smoothCameraFollowValue;
    static bool smoothCameraFollowLoaded;
    static bool dontAskQuantityValue;
    static bool dontAskQuantityLoaded;
    static bool? quickCastSingle, quickCastGround, hideOtherCalls;
    static float? master, sfx, music, scrollSensitivity;

    /// <summary>How far one scroll moves things, as a multiple of one mouse-wheel notch: every scrolling list in
    /// a window and the character preview (through <see cref="ScaledScrollInput"/>), and the camera zoom
    /// (through <see cref="PointerInput.ScrollAmount"/>). 1 = one notch moves exactly what it always has.</summary>
    public static float ScrollSensitivity
    {
        get
        {
            if (scrollSensitivity == null)
                scrollSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(ScrollSensitivityKey, 1f),
                                                ScrollSensitivityMin, ScrollSensitivityMax);
            return scrollSensitivity.Value;
        }
        set
        {
            value = Mathf.Clamp(value, ScrollSensitivityMin, ScrollSensitivityMax);
            if (scrollSensitivity != null && Mathf.Approximately(scrollSensitivity.Value, value)) return;
            scrollSensitivity = value;
            PlayerPrefs.SetFloat(ScrollSensitivityKey, value);
        }
    }

    /// <summary>The shop's "Don't ask quantity": a stack goes into the cart without the "how many?" box — the whole stack
    /// to sell, one to buy. Off by default.</summary>
    public static bool DontAskQuantity
    {
        get
        {
            if (!dontAskQuantityLoaded)
            {
                dontAskQuantityValue = PlayerPrefs.GetInt(DontAskQuantityKey, 0) != 0;
                dontAskQuantityLoaded = true;
            }
            return dontAskQuantityValue;
        }
        set
        {
            if (dontAskQuantityLoaded && dontAskQuantityValue == value) return;
            dontAskQuantityValue = value;
            dontAskQuantityLoaded = true;
            PlayerPrefs.SetInt(DontAskQuantityKey, value ? 1 : 0);
        }
    }

    /// <summary>Ease the follow-camera toward the player instead of snapping to it. On by default.</summary>
    public static bool SmoothCameraFollow
    {
        get
        {
            if (!smoothCameraFollowLoaded)
            {
                smoothCameraFollowValue = PlayerPrefs.GetInt(SmoothCameraFollowKey, 1) != 0;
                smoothCameraFollowLoaded = true;
            }
            return smoothCameraFollowValue;
        }
        set
        {
            if (smoothCameraFollowLoaded && smoothCameraFollowValue == value) return;
            smoothCameraFollowValue = value;
            smoothCameraFollowLoaded = true;
            PlayerPrefs.SetInt(SmoothCameraFollowKey, value ? 1 : 0);
        }
    }

    /// <summary>Draw every window with the dark palette (<see cref="UiKit.SetDarkMode"/>). Always on: the light palette
    /// is switched off for now, and the setting is gone from the Settings window.</summary>
    public static bool DarkMode => true;

    /// <summary>Fire a single-target skill straight at the current target instead of opening the reticle
    /// (<see cref="UnitTargeter"/>). The reticle still opens when there is no legal, in-range target to fire
    /// at. Off by default.</summary>
    public static bool QuickCastSingleTarget
    {
        get => Flag(ref quickCastSingle, QuickCastSingleKey);
        set => SetFlag(ref quickCastSingle, QuickCastSingleKey, value);
    }

    /// <summary>Fire a ground-placed skill straight at the point under the cursor instead of opening the
    /// reticle (<see cref="GroundTargeter"/>). The reticle still opens when the cursor isn't over ground.
    /// Off by default.</summary>
    public static bool QuickCastGroundTarget
    {
        get => Flag(ref quickCastGround, QuickCastGroundKey);
        set => SetFlag(ref quickCastGround, QuickCastGroundKey, value);
    }

    /// <summary>Hide the skill calls other people make — the bubble over their head and the line in the log. Your own
    /// calls always show. Off by default.</summary>
    public static bool HideOtherPlayersSkillCalls
    {
        get => Flag(ref hideOtherCalls, HideOtherCallsKey);
        set => SetFlag(ref hideOtherCalls, HideOtherCallsKey, value);
    }

    /// <summary>Overall loudness — scales EVERY sound the game makes (effects, music, ambience, rain).</summary>
    public static float MasterVolume
    {
        get => Volume(ref master, MasterVolumeKey);
        set => SetVolume(ref master, MasterVolumeKey, value);
    }

    /// <summary>Loudness of one-shot sound effects (<see cref="Sfx"/>) only.</summary>
    public static float SfxVolume
    {
        get => Volume(ref sfx, SfxVolumeKey);
        set => SetVolume(ref sfx, SfxVolumeKey, value);
    }

    /// <summary>Loudness of the music beds (<see cref="MusicDirector"/>) only.</summary>
    public static float MusicVolume
    {
        get => Volume(ref music, MusicVolumeKey);
        set => SetVolume(ref music, MusicVolumeKey, value);
    }

    // ---- graphics: what each player's own screen draws ----------------------------------------------------------
    // None of these reaches the server or changes a saved file: they only choose how much this machine draws.

    /// <summary>The sun's shadow picture per <see cref="ShadowQuality"/> step: Low, Medium, High, Very high. A sharper
    /// picture shows the edges of small things (a flower) instead of a blob, at the cost of graphics memory.</summary>
    public static readonly int[] ShadowResolutions = { 1024, 2048, 4096, 8192 };
    public static readonly string[] ShadowQualityNames = { "Low", "Medium", "High", "Very high" };
    public const float PlantDistanceMin = 10f, PlantDistanceMax = 40f;
    public const float PlantDensityMin = 0.25f;

    static int? shadowQuality;
    static float? plantDistance, plantDensity;
    static bool? plantShadows, showPerformance;

    /// <summary>Raised when the share of grass and flowers drawn changes, so every carpet lays its plants again.</summary>
    public static event System.Action PlantDensityChanged;
    /// <summary>Raised when the performance panel is switched on or off (here or by its key).</summary>
    public static event System.Action ShowPerformanceChanged;

    /// <summary>Index into <see cref="ShadowResolutions"/>. Very high (8192) by default.</summary>
    public static int ShadowQuality
    {
        get
        {
            if (shadowQuality == null) shadowQuality = Mathf.Clamp(PlayerPrefs.GetInt(ShadowQualityKey, ShadowResolutions.Length - 1), 0, ShadowResolutions.Length - 1);
            return shadowQuality.Value;
        }
        set
        {
            value = Mathf.Clamp(value, 0, ShadowResolutions.Length - 1);
            if (shadowQuality == value) return;
            shadowQuality = value;
            PlayerPrefs.SetInt(ShadowQualityKey, value);
            GraphicsQuality.Apply();
        }
    }

    /// <summary>How far from the camera grass and flower carpets are drawn, in world units. 20 by default.</summary>
    public static float PlantDistance
    {
        get
        {
            if (plantDistance == null) plantDistance = Mathf.Clamp(PlayerPrefs.GetFloat(PlantDistanceKey, 20f), PlantDistanceMin, PlantDistanceMax);
            return plantDistance.Value;
        }
        set
        {
            value = Mathf.Clamp(value, PlantDistanceMin, PlantDistanceMax);
            if (plantDistance != null && Mathf.Approximately(plantDistance.Value, value)) return;
            plantDistance = value;
            PlayerPrefs.SetFloat(PlantDistanceKey, value);
        }
    }

    /// <summary>The share of the world's built grass and flowers drawn, <see cref="PlantDensityMin"/> to 1. The built
    /// plants are the ceiling; a lower share leaves plants out at random, evenly. 1 by default.</summary>
    public static float PlantDensity
    {
        get
        {
            if (plantDensity == null) plantDensity = Mathf.Clamp(PlayerPrefs.GetFloat(PlantDensityKey, 1f), PlantDensityMin, 1f);
            return plantDensity.Value;
        }
        set
        {
            value = Mathf.Clamp(value, PlantDensityMin, 1f);
            if (plantDensity != null && Mathf.Approximately(plantDensity.Value, value)) return;
            plantDensity = value;
            PlayerPrefs.SetFloat(PlantDensityKey, value);
            PlantDensityChanged?.Invoke();
        }
    }

    /// <summary>Do grass and flowers cast shadows. On by default.</summary>
    public static bool PlantShadows
    {
        get
        {
            if (plantShadows == null) plantShadows = PlayerPrefs.GetInt(PlantShadowsKey, 1) != 0;
            return plantShadows.Value;
        }
        set => SetFlag(ref plantShadows, PlantShadowsKey, value);
    }

    /// <summary>Show the performance panel. Off by default.</summary>
    public static bool ShowPerformance
    {
        get => Flag(ref showPerformance, ShowPerformanceKey);
        set
        {
            if (showPerformance != null && showPerformance.Value == value) return;
            SetFlag(ref showPerformance, ShowPerformanceKey, value);
            ShowPerformanceChanged?.Invoke();
        }
    }

    // ---- the picture: brightness, contrast, saturation, fog ----------------------------------------------------------
    // Each colour choice is an AMOUNT ADDED to the map's own look (its colour profile), 0 = the look exactly as it was
    // made, so the same choice holds on every map whatever its look. Fog is a share of the fog's own thickness; it stops
    // at FogThicknessMin because the fog is what hides the edge of the ground a seamless world has loaded.
    public const float LookBrightnessMin = -1f, LookBrightnessMax = 1f;
    public const float LookContrastMin = -50f, LookContrastMax = 50f;
    public const float LookSaturationMin = -50f, LookSaturationMax = 100f;
    public const float FogThicknessMin = 0.5f;

    static float? lookBrightness, lookContrast, lookSaturation, fogThickness;

    /// <summary>Raised when brightness, contrast or saturation changes, so the picture is graded again.</summary>
    public static event System.Action LookChanged;

    /// <summary>Added to the look's brightness (its exposure, in stops). 0 by default.</summary>
    public static float LookBrightness
    {
        get => Ranged(ref lookBrightness, LookBrightnessKey, 0f, LookBrightnessMin, LookBrightnessMax);
        set { if (SetRanged(ref lookBrightness, LookBrightnessKey, value, LookBrightnessMin, LookBrightnessMax)) LookChanged?.Invoke(); }
    }

    /// <summary>Added to the look's contrast. 0 by default.</summary>
    public static float LookContrast
    {
        get => Ranged(ref lookContrast, LookContrastKey, 0f, LookContrastMin, LookContrastMax);
        set { if (SetRanged(ref lookContrast, LookContrastKey, value, LookContrastMin, LookContrastMax)) LookChanged?.Invoke(); }
    }

    /// <summary>Added to the look's saturation. 0 by default.</summary>
    public static float LookSaturation
    {
        get => Ranged(ref lookSaturation, LookSaturationKey, 0f, LookSaturationMin, LookSaturationMax);
        set { if (SetRanged(ref lookSaturation, LookSaturationKey, value, LookSaturationMin, LookSaturationMax)) LookChanged?.Invoke(); }
    }

    /// <summary>The share of the fog's own thickness drawn, <see cref="FogThicknessMin"/> to 1. 1 by default.</summary>
    public static float FogThickness
    {
        get => Ranged(ref fogThickness, FogThicknessKey, 1f, FogThicknessMin, 1f);
        set => SetRanged(ref fogThickness, FogThicknessKey, value, FogThicknessMin, 1f);
    }

    static float Ranged(ref float? cache, string key, float fallback, float min, float max)
    {
        if (cache == null) cache = Mathf.Clamp(PlayerPrefs.GetFloat(key, fallback), min, max);
        return cache.Value;
    }

    // FALSE when the value did not change, so nothing is redone for a slider that did not move.
    static bool SetRanged(ref float? cache, string key, float value, float min, float max)
    {
        value = Mathf.Clamp(value, min, max);
        if (cache != null && Mathf.Approximately(cache.Value, value)) return false;
        cache = value;
        PlayerPrefs.SetFloat(key, value);
        return true;
    }

    // ---- the hotbar's third row: the player's own keys ----------------------------------------------------------
    // One key per slot, KeyCode.None until chosen. Kept on this device like every other setting here, because which
    // keys suit a person depends on the keyboard in front of them.
    const string RowThreeKeyPrefix = "set_hotbar_row3_key_";
    static KeyCode[] rowThree;

    /// <summary>The key third-row slot <paramref name="column"/> (0–8) answers to, or <see cref="KeyCode.None"/>.</summary>
    public static KeyCode HotbarRowThreeKey(int column)
    {
        if (column < 0 || column >= HotbarBindings.PerRow) return KeyCode.None;
        if (rowThree == null)
        {
            rowThree = new KeyCode[HotbarBindings.PerRow];
            for (int c = 0; c < rowThree.Length; c++)
            {
                rowThree[c] = (KeyCode)PlayerPrefs.GetInt(RowThreeKeyPrefix + c, (int)KeyCode.None);
                // A KEY THE GAME HAS SINCE TAKEN is let go, so one press never fires a slot and a window together.
                if (GameKeys.IsFixed(rowThree[c])) rowThree[c] = KeyCode.None;
            }
        }
        return rowThree[column];
    }

    /// <summary>Give third-row slot <paramref name="column"/> a key. Refused (false) for a key the game already uses
    /// or another slot holds (<see cref="GameKeys.IsFree"/>); <see cref="KeyCode.None"/> clears it.</summary>
    public static bool SetHotbarRowThreeKey(int column, KeyCode key)
    {
        if (column < 0 || column >= HotbarBindings.PerRow) return false;
        if (key != KeyCode.None && !GameKeys.IsFree(key, column)) return false;
        HotbarRowThreeKey(column);
        rowThree[column] = key;
        PlayerPrefs.SetInt(RowThreeKeyPrefix + column, (int)key);
        PlayerPrefs.Save();
        return true;
    }

    // ---- the screen: resolution and fullscreen ------------------------------------------------------------------
    // Unity keeps both itself (its own "Screenmanager …" entries in the same PlayerPrefs store) and restores them on
    // the next launch, so nothing is stored here — keeping a second copy would be one that can disagree with it.

    /// <summary>Borderless fullscreen (the game's default) when true, an ordinary window when false.</summary>
    public static bool Fullscreen
    {
        get => Screen.fullScreenMode != FullScreenMode.Windowed;
        set => Screen.SetResolution(Screen.width, Screen.height,
                                    value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
    }

    /// <summary>Draw the game at <paramref name="width"/> × <paramref name="height"/>, keeping the current fullscreen
    /// choice. The new size shows from the next frame, so <see cref="Screen.width"/> still reads the old one until then.</summary>
    public static void SetResolution(int width, int height) => Screen.SetResolution(width, height, Screen.fullScreenMode);

    /// <summary>Every size the monitor offers, largest first, each once (the monitor lists a size again for every
    /// refresh rate it runs it at).</summary>
    public static void Resolutions(System.Collections.Generic.List<Vector2Int> into)
    {
        into.Clear();
        var all = Screen.resolutions;
        for (int i = all.Length - 1; i >= 0; i--)
        {
            var size = new Vector2Int(all[i].width, all[i].height);
            if (!into.Contains(size)) into.Add(size);
        }
    }

    /// <summary>Push the three volumes onto the systems that play sound. Called whenever one changes, and by
    /// each audio system as it comes online, so a system that spawns late still starts at the saved level.
    /// Master rides <see cref="AudioListener.volume"/> — Unity's one global multiplier over every
    /// <see cref="AudioSource"/> — so it covers beds and effects this class doesn't know about.</summary>
    public static void ApplyAudio()
    {
        AudioListener.volume = MasterVolume;
        if (Sfx.Instance != null) Sfx.Instance.masterVolume = SfxVolume;
        if (MusicDirector.Instance != null) MusicDirector.Instance.masterVolume = MusicVolume;
    }

    static bool Flag(ref bool? cache, string key)
    {
        if (cache == null) cache = PlayerPrefs.GetInt(key, 0) != 0;
        return cache.Value;
    }

    static void SetFlag(ref bool? cache, string key, bool value)
    {
        if (cache != null && cache.Value == value) return;
        cache = value;
        PlayerPrefs.SetInt(key, value ? 1 : 0);
    }

    static float Volume(ref float? cache, string key)
    {
        if (cache == null) cache = Mathf.Clamp01(PlayerPrefs.GetFloat(key, 1f));
        return cache.Value;
    }

    static void SetVolume(ref float? cache, string key, float value)
    {
        value = Mathf.Clamp01(value);
        if (cache != null && Mathf.Approximately(cache.Value, value)) return;
        cache = value;
        PlayerPrefs.SetFloat(key, value);
        ApplyAudio();
    }
}
