using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The first thing the game shows: sign in, or make an account. Nothing else can begin until it succeeds —
/// a character's data lives on the server, so until we know who is playing there is nothing to load.
///
/// <para>Self-bootstrapping, the same way <see cref="SaveManager"/> is, so the door exists without any scene
/// wiring. On success it hands over to <see cref="CharacterSelectScreen"/> and removes itself.</para>
///
/// <para>There is no server-choosing step. That screen exists in games that run several separate worlds; this
/// game has one, so a list with a single entry in it would be a click that asks nothing. If a second world is
/// ever added, it belongs between this screen and character select.</para>
/// </summary>
public class SignInScreen : MonoBehaviour
{
    TMP_InputField accountField;
    TMP_InputField passwordField;
    TextMeshProUGUI status;
    Button enterButton;
    Button createButton;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (AccountSession.IsActive) return;   // already playing (a scene change, not a fresh launch)
        var go = new GameObject("SignInScreen") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);
        go.AddComponent<SignInScreen>();
    }

    void Start()
    {
        var canvas = FrontScreen.Canvas(transform);
        var panel = FrontScreen.Panel(canvas, new Vector2(430, 330));

        var title = UiKit.Label("Title", panel, "unwritten", 34, FontStyle.Normal, TextAnchor.MiddleCenter, title: true);
        title.color = UiKit.Gold;
        FrontScreen.Row(title.rectTransform, -34, 44);

        var line = UiKit.Label("Line", panel, "Sign in to continue", 14, FontStyle.Italic, TextAnchor.MiddleCenter);
        line.color = UiKit.TextDim;
        FrontScreen.Row(line.rectTransform, -76, 20);

        accountField = FrontScreen.Field(panel, "Account", -124, false);
        passwordField = FrontScreen.Field(panel, "Password", -180, true);

        status = UiKit.Label("Status", panel, "", 13, FontStyle.Normal, TextAnchor.MiddleCenter);
        status.color = UiKit.Gold;
        FrontScreen.Row(status.rectTransform, -222, 18);

        enterButton = UiKit.Btn("Enter", panel, OnEnter);
        FrontScreen.Place(enterButton.GetComponent<RectTransform>(), new Vector2(-80, -272), new Vector2(140, 34));

        createButton = UiKit.Btn("New Account", panel, OnCreate);
        FrontScreen.Place(createButton.GetComponent<RectTransform>(), new Vector2(80, -272), new Vector2(140, 34));

        accountField.ActivateInputField();
    }

    void Update()
    {
        // Enter moves down the form and then submits, which is what hands on a keyboard expect.
        if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
        if (accountField.isFocused && passwordField.text.Length == 0) passwordField.ActivateInputField();
        else OnEnter();
    }

    void OnEnter() => Submit(signUpFirst: false);
    void OnCreate() => Submit(signUpFirst: true);

    void Submit(bool signUpFirst)
    {
        string email = accountField.text.Trim();
        string password = passwordField.text;

        if (email.Length == 0 || password.Length == 0) { Say("Fill in both lines."); return; }
        if (signUpFirst && password.Length < 8) { Say("A new password needs at least 8 characters."); return; }

        Busy(true);
        if (!signUpFirst) { SignIn(email, password); return; }

        // Make the account, then sign in with the very same details — one path establishes a session, so a new
        // account can never end up in a different state than a returning one.
        AccountClient.Register(email, password, (ok, error) =>
        {
            if (this == null) return;
            if (!ok) { Busy(false); Say(error ?? "Could not create that account."); return; }
            SignIn(email, password);
        });
    }

    void SignIn(string email, string password)
    {
        AccountClient.SignIn(email, password, (ok, error) =>
        {
            if (this == null) return;
            if (!ok) { Busy(false); Say(error ?? "Could not sign in."); return; }

            CharacterSelectScreen.Open();
            Destroy(gameObject);
        });
    }

    void Say(string message) { if (status != null) status.text = message; }

    // Nothing is clickable while a request is out, so a second press can't start a second sign-in.
    void Busy(bool busy)
    {
        if (enterButton != null) enterButton.interactable = !busy;
        if (createButton != null) createButton.interactable = !busy;
        if (busy) Say("…");
    }
}

/// <summary>The shared furniture of the screens that sit in FRONT of the game — the veil, the bordered panel,
/// a labelled text line, and the placement helpers. One definition, so sign-in and character select cannot
/// drift into two different-looking doors onto the same game.</summary>
public static class FrontScreen
{
    /// <summary>A full-screen canvas above the HUD, with the world dimmed behind it.</summary>
    public static RectTransform Canvas(Transform owner)
    {
        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("EventSystem") { hideFlags = HideFlags.DontSaveInEditor };
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
            Object.DontDestroyOnLoad(events);
        }

        var go = new GameObject("FrontCanvas", typeof(UnityEngine.Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.hideFlags = HideFlags.DontSaveInEditor;
        go.transform.SetParent(owner, false);

        var canvas = go.GetComponent<UnityEngine.Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // TOPMOST, deliberately. "In front of the HUD" is not one number: the HUD is many canvases at many
        // depths — the map sits at 5000, the death screen at 5000, the dialog box at 30000, the performance
        // overlay at 32760. A door onto the game has to be above ALL of them, so it takes the highest slot
        // there is. (Measured: at 500 the map, the bars and the overlay all drew straight through it.)
        canvas.sortingOrder = short.MaxValue;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        UiKit.ApplyTextCrispness(scaler);

        // Fully opaque: before you have signed in you are not in the world, so the world has no business being
        // visible. A partly-transparent veil reads as "the game is already running and you're locked out of it",
        // which is exactly the wrong impression.
        var veil = UiKit.Img("Veil", go.transform, new Color(0.051f, 0.055f, 0.063f, 1f));   // #0D0E10
        veil.raycastTarget = true;   // swallow every click so nothing behind the door can be reached
        UiKit.Fill(veil.rectTransform, Vector2.zero, Vector2.zero);

        return veil.rectTransform;
    }

    /// <summary>A gold-bordered charcoal panel, centred. Returns the fill to parent contents to.</summary>
    public static Transform Panel(Transform canvas, Vector2 size)
    {
        var border = UiKit.Img("Panel", canvas, UiKit.Gold);
        border.rectTransform.anchorMin = border.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        border.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        border.rectTransform.sizeDelta = size;
        border.rectTransform.anchoredPosition = Vector2.zero;

        var fill = UiKit.Img("Fill", border.transform, UiKit.PanelFill);
        UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        return fill.transform;
    }

    /// <summary>A row at <paramref name="top"/> pixels below the panel's top edge, inset by
    /// <paramref name="left"/> and <paramref name="right"/>. The insets let a row share its line with something
    /// beside it — the body preview, for instance — without a second layout system.</summary>
    public static void Row(RectTransform rt, float top, float height, float left = 24f, float right = 24f)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(left, 0);
        rt.offsetMax = new Vector2(-right, 0);
        rt.sizeDelta = new Vector2(-(left + right), height);
        rt.anchoredPosition = new Vector2(0, top);
    }

    /// <summary>Centre a fixed-size control, measured from the panel's top edge.</summary>
    public static void Place(RectTransform rt, Vector2 topCentreOffset, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = size;
        rt.anchoredPosition = topCentreOffset;
    }

    /// <summary>A labelled single-line text box. <paramref name="secret"/> masks what is typed.</summary>
    public static TMP_InputField Field(Transform panel, string label, float top, bool secret,
                                       float left = 24f, float right = 24f)
    {
        var caption = UiKit.Label(label + "Label", panel, label, 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        caption.color = UiKit.TextDim;
        Row(caption.rectTransform, top, 18, left, right);

        var box = UiKit.Img(label + "Box", panel, UiKit.SlotFill);
        box.raycastTarget = true;
        Row(box.rectTransform, top - 20, 32, left, right);

        var text = UiKit.Label("Text", box.transform, "", 15, FontStyle.Normal, TextAnchor.MiddleLeft);
        text.richText = false;   // typed text is literal, never markup
        UiKit.Fill(text.rectTransform, new Vector2(10, 0), new Vector2(-10, 0));

        var field = box.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = box;
        field.textComponent = text;
        field.lineType = TMP_InputField.LineType.SingleLine;
        if (secret) field.contentType = TMP_InputField.ContentType.Password;
        return field;
    }
}
