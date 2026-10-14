using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The game's "unwritten" lettering across the top of every front screen, drawn the way the launcher draws it: the
/// lettering, on a rim in the panel colour that follows its outline, on a slightly wider rim in the window-edge
/// colour that shows as a line round it. All three are one picture, made in the editor from the launcher's own
/// lettering file (<c>TitleLetteringBaker</c>), so nothing is worked out while playing and it is drawn once.
/// </summary>
public static class TitleLettering
{
    /// <summary>Where the made picture is loaded from.</summary>
    public const string PicturePath = "UI/front-screen-title";

    /// <summary>The width of the made picture as the launcher shows it — its lettering plus rims, 572 wide.</summary>
    public const float ShownWidth = 572f;

    // From the top of the screen to the top of the picture.
    const float Top = -28f;

    public static void Place(Transform parent)
    {
        var art = Resources.Load<Texture2D>(PicturePath);
        // No lettering shipped — the screen is still perfectly usable without it.
        if (art == null) return;

        var go = new GameObject("Lettering", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<RawImage>();
        image.texture = art;
        image.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(ShownWidth, ShownWidth * art.height / art.width);
        rt.anchoredPosition = new Vector2(0f, Top);
    }
}
