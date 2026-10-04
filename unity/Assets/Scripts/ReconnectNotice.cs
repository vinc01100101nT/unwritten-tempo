using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The line that says the game is trying to get its world back. Shown while a connection is being retried —
/// at start-up, when the server has not answered yet, and mid-play, when it has gone away.
///
/// <para><b>It dims rather than hides.</b> The world stays visible behind it on purpose: a player who is being
/// reconnected has not gone anywhere, and blanking the screen would say they had. The sign-in screen's own
/// front canvas deliberately blacks the world out (it owns a camera that clears the frame) — right for a door
/// onto the game, wrong for a moment inside it, which is why this is its own thin overlay instead.</para>
///
/// <para>Built once and reused. Showing and hiding switches it on and off rather than making a new one, so a
/// connection that flickers a dozen times does not leave a dozen of these behind.</para>
/// </summary>
public static class ReconnectNotice
{
    const string Message = "Reconnecting…";

    static GameObject root;

    public static void Show()
    {
        if (root == null) Build();
        if (root != null) root.SetActive(true);
    }

    public static void Hide()
    {
        if (root != null) root.SetActive(false);
    }

    static void Build()
    {
        var go = new GameObject("ReconnectNotice", typeof(Canvas), typeof(CanvasScaler));
        go.hideFlags = HideFlags.DontSaveInEditor;
        Object.DontDestroyOnLoad(go);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above every HUD canvas, for the same reason the sign-in veil is: "in front of the game" is not one
        // number, and the highest slot there is is the only one that is reliably in front of all of them.
        canvas.sortingOrder = short.MaxValue;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        // SIZED LIKE THE MAIN SCREEN LAYER — laid out as if every screen were 1280 wide — while staying on a layer of
        // its own, because it must cover everything, the hidden HUD included.
        var hud = UiKit.HudCanvas != null ? UiKit.HudCanvas.GetComponent<CanvasScaler>() : null;
        scaler.referenceResolution = hud != null ? hud.referenceResolution : new Vector2(1280, 720);
        scaler.screenMatchMode = hud != null ? hud.screenMatchMode : CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = hud != null ? hud.matchWidthOrHeight : 0f;
        UiKit.ApplyTextCrispness(scaler);

        // The dim. Deliberately partial — the world reads through it, so it is plainly still there.
        var veil = UiKit.Img("Veil", go.transform, new Color(0.051f, 0.055f, 0.063f, 0.55f));
        var veilRect = veil.rectTransform;
        veilRect.anchorMin = Vector2.zero;
        veilRect.anchorMax = Vector2.one;
        veilRect.offsetMin = Vector2.zero;
        veilRect.offsetMax = Vector2.zero;

        var label = UiKit.Label("Message", go.transform, Message, 15,
                                FontStyle.Normal, TextAnchor.MiddleCenter);
        label.color = UiKit.TextOnDim;
        var labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.sizeDelta = new Vector2(280, 27);
        labelRect.anchoredPosition = Vector2.zero;

        root = go;
    }
}
