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
