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
    const string QuickCastSingleKey    = "set_quickcast_single";
    const string QuickCastGroundKey    = "set_quickcast_ground";
    const string MasterVolumeKey       = "set_volume_master";
    const string SfxVolumeKey          = "set_volume_sfx";
    const string MusicVolumeKey        = "set_volume_music";
    const string ScrollSensitivityKey  = "set_scroll_sensitivity";
    const string HideOtherCallsKey     = "set_hide_other_skill_calls";

    public const float ScrollSensitivityMin = 0.25f;
    public const float ScrollSensitivityMax = 3f;

    static bool smoothCameraFollowValue;
    static bool smoothCameraFollowLoaded;
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
