using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// THE ONE place an error is shown in front of the game: a window in the front screens' own look (the blue bar
/// reading "Error"), the whole message, and an OK that closes it. Sign-in, character select, and being put out of a
/// world all show their errors here, so no screen keeps an error line of its own.
///
/// <para>Built the first time an error happens and kept, hidden, for the next one — it outlives every screen, so a
/// world lost in the middle of play can still say why on the screen the player lands on. Everything behind it is
/// unclickable until it is closed.</para>
/// </summary>
public static class ErrorPopup
{
    const float Width = 460f, BodyHeight = 170f;

    static GameObject root;
    static TextMeshProUGUI text;
    static Button ok;
    // The frame it opened on: the Enter that raised it (a sign-in pressed with Enter) must not close it at once.
    static int openedOnFrame = -1;

    /// <summary>Show <paramref name="message"/>. A second error while one is up replaces the first.</summary>
    public static void Show(string message)
    {
        if (GameServer.IsServer || string.IsNullOrEmpty(message)) return;   // a server has nobody to show it to
        if (root == null) Build();
        text.text = message;
        root.SetActive(true);
        openedOnFrame = Time.frameCount;
        root.transform.SetAsLastSibling();
        // THE KEYBOARD IS THE POPUP'S while it is up: OK takes the focus off whatever field or button sits behind.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(ok.gameObject);
    }

    static void Close()
    {
        if (root == null || !root.activeSelf || Time.frameCount == openedOnFrame) return;
        // The keyboard goes back to what held it before the popup — EnterKey does that as OK goes away.
        root.SetActive(false);
    }

    static void Build()
    {
        root = new GameObject("ErrorPopup", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
        {
            hideFlags = HideFlags.DontSaveInEditor
        };
        Object.DontDestroyOnLoad(root);

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // THE TOP SLOT, shared with the front screens; Show moves it last in the hierarchy to draw over them.
        canvas.sortingOrder = short.MaxValue;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        UiKit.ApplyTextCrispness(scaler);

        // Catches every click outside the window, so nothing behind it can be pressed while it is up.
        var shield = UiKit.Img("Shield", root.transform, new Color(0f, 0f, 0f, 0.5f));
        shield.raycastTarget = true;
        UiKit.Fill(shield.rectTransform, Vector2.zero, Vector2.zero);

        var body = FrontScreen.Panel(shield.transform, new Vector2(Width, BodyHeight), "Error", out _);

        text = UiKit.Label("Message", body, "", 14, FontStyle.Normal, TextAnchor.MiddleCenter);
        UiKit.Paint(text, UiKit.TextMain);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.enableAutoSizing = true;
        text.fontSizeMin = 10;
        text.fontSizeMax = 14;
        FrontScreen.Row(text.rectTransform, -16f, BodyHeight - 16f - 58f);

        ok = UiKit.PrimaryBtn("OK", body, Close);
        FrontScreen.Place(ok.GetComponent<RectTransform>(), new Vector2(0, -(BodyHeight - 48f)), new Vector2(120, 34));
    }
}
