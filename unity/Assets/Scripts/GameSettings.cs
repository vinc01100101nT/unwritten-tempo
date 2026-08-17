using UnityEngine;

/// <summary>
/// THE store for player-facing options chosen in the Settings window (System ▸ Settings). One owner per
/// option: the systems those options affect READ from here every frame instead of keeping their own copy,
/// so a setting can never drift out of sync with what the game is actually doing.
///
/// Values persist across sessions in <see cref="PlayerPrefs"/> and are cached in memory on first read, so a
/// per-frame reader (e.g. <see cref="CameraFollow3D"/>) never touches disk.
///
/// JS/TS analogy: a tiny settings module backed by localStorage, read through a getter.
/// </summary>
public static class GameSettings
{
    const string SmoothCameraFollowKey = "set_smooth_camera_follow";

    static bool smoothCameraFollowValue;
    static bool smoothCameraFollowLoaded;

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
            PlayerPrefs.Save();
        }
    }
}
