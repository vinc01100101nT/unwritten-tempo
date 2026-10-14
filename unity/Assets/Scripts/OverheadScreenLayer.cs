using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE one way to draw words that ride something in the world but keep the SAME SIZE ON SCREEN at every zoom —
/// chat bubbles and the names of things on the floor. They are drawn flat on the screen, like the HUD, and only
/// their POSITION comes from the world: each frame the thing they ride is projected to a screen point and the
/// words are put there. Zooming moves them, never grows or shrinks them.
///
/// <para>Their size is the size they had when they were drawn in the world, at the zoom the camera starts the
/// game at — so nothing looks different until you scroll.</para>
///
/// <para>Drawn BENEATH every window by default: the game's HUD canvas sits at sorting order 0, so an open bag or the
/// chat box covers a bubble rather than the other way round. <see cref="AboveWindows"/> is the one exception — words
/// about your own character that must be read whatever is open.</para>
/// </summary>
public static class OverheadScreenLayer
{
    const int SortingOrder = -10;

    /// <summary>Over every window of the game's HUD (sorting order 0), and under the item tooltip's layer (200) and
    /// the conversation box (30000), so a hovered card is never covered.</summary>
    public const int AboveWindows = 150;

    /// <summary>Build one screen-drawn canvas for a system's words. Scaled with the window exactly as the game's
    /// HUD canvas is (1280×720, matched on width), so these words and the HUD grow and shrink together when the
    /// window is resized. <paramref name="clickable"/> adds the raycaster that lets the cursor find them.</summary>
    public static Canvas CreateCanvas(string name, bool clickable, int sortingOrder = SortingOrder)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSaveInEditor };
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;
        UiKit.ApplyTextCrispness(scaler);
        if (clickable) go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    static int memoFrame = -1;
    static float memoPerWorldUnit;

    /// <summary>The scale that makes a piece of UI drawn at <paramref name="worldUnitsPerUiUnit"/> in the world
    /// look, on <paramref name="canvas"/>, exactly as it did in the world at the camera's STARTING zoom. Zero
    /// while there is no camera to measure against. Worked out once a frame, however many words ask.</summary>
    public static float ScaleFor(Canvas canvas, float worldUnitsPerUiUnit)
    {
        if (memoFrame != Time.frameCount)
        {
            memoFrame = Time.frameCount;
            memoPerWorldUnit = 0f;
            var cam = CameraRig.Cam;
            float depth = CameraFollow3D.StartViewDepth;
            if (cam != null && canvas != null && depth > 0f)
            {
                float canvasHeight = ((RectTransform)canvas.transform).rect.height;
                float worldHeightInView = 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                memoPerWorldUnit = canvasHeight / worldHeightInView;
            }
        }
        return worldUnitsPerUiUnit * memoPerWorldUnit;
    }

    /// <summary>Put <paramref name="rt"/> on the screen over <paramref name="world"/> at <paramref name="scale"/>.
    /// A point behind the camera is not drawn (scaled to nothing) rather than drawn mirrored. Snapped to whole
    /// pixels so the words stay sharp.</summary>
    public static void Place(RectTransform rt, Vector3 world, Camera cam, float scale)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) scale = 0f;
        rt.position = new Vector3(Mathf.Round(sp.x), Mathf.Round(sp.y), 0f);
        if (rt.localScale.x != scale) rt.localScale = new Vector3(scale, scale, 1f);
    }
}
