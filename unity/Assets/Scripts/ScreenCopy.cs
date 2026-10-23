using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// THE CAMERA'S COPY OF THE SCREEN, KEPT ONLY WHILE SOMETHING ON SCREEN NEEDS IT. A heat haze bends the picture behind
/// it by reading that copy; without it the haze draws as a flat dark sheet. Making the copy costs a screen-sized copy
/// every frame, so it is switched on when the first such effect starts and back to what the camera had when the last
/// one ends — nothing is paid while none is playing.
///
/// <para>Which effects need it is decided at author time (<see cref="EffectDef.measuredReadsScreen"/>); each playing
/// instance holds once and releases once (<see cref="Vfx.EffectHandle"/>). Nothing here runs per frame — only when an
/// effect starts or ends.</para>
/// </summary>
public static class ScreenCopy
{
    static int holders;
    static Camera appliedTo;
    static UniversalAdditionalCameraData appliedData;
    static CameraOverrideOption before;

    public static void Hold()
    {
        // The camera's option is saved with its scene, so it is only ever touched while the game runs.
        if (!Application.isPlaying) return;
        holders++;
        Apply();
    }

    public static void Release()
    {
        if (!Application.isPlaying) return;
        if (holders > 0) holders--;
        Apply();
    }

    // Puts the copy on the camera in use while anything holds it, and gives the camera back its own setting once
    // nothing does. A new map's camera is picked up by the next effect that starts.
    static void Apply()
    {
        var cam = holders > 0 ? CameraRig.Cam : null;
        if (cam == appliedTo) return;
        if (appliedData != null) appliedData.requiresColorOption = before;
        appliedTo = null;
        appliedData = null;
        if (cam == null) return;
        var data = cam.GetComponent<UniversalAdditionalCameraData>();
        if (data == null) return;
        before = data.requiresColorOption;
        data.requiresColorOption = CameraOverrideOption.On;
        appliedTo = cam;
        appliedData = data;
    }
}
