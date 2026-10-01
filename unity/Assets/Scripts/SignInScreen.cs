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
#if UNITY_ANDROID
    // Only a phone asks: Android carries no environment variables, so the address a desktop build reads from
    // one has to be typed here instead. See ServerAddress.
    TMP_InputField serverField;
#endif
    TextMeshProUGUI status;
    Button enterButton;
    Button createButton;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (AccountSession.IsActive) return;   // already playing (a scene change, not a fresh launch)
        Open();
    }

    /// <summary>Put the sign-in screen up. Called at launch, and again whenever somebody is put back out of the
    /// world — a lost connection ends a session exactly the way signing out does, so both arrive here rather than
    /// at two different-looking front doors.</summary>
    public static void Open()
    {
        if (GameServer.IsServer) return;       // a server has nobody sitting at it to sign in
        if (Object.FindAnyObjectByType<SignInScreen>() != null) return;   // already up; never a second one

        var go = new GameObject("SignInScreen") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);
        go.AddComponent<SignInScreen>();
    }

    /// <summary>Put the screen up carrying a line explaining why the player is looking at it. Used when a world
    /// was lost rather than left: arriving back at sign-in with no explanation reads as a crash.</summary>
    public static void OpenSaying(string message)
    {
        Open();
        var screen = Object.FindAnyObjectByType<SignInScreen>();
        if (screen != null) screen.pendingMessage = message;
    }

    string pendingMessage;

    // The panel's vertical rhythm, named once so a row can be inserted without re-typing every offset below it.
    const float RowGap = 56f;      // one labelled field to the next
    const float TitleSpace = 64f;  // the room the big title used to take above the first line
    const float LineSpace = 42f;
    // the room the "Sign in to continue" line used to take above the first field
    const float StatusGap = 60f;   // last field to the status line
    const float ButtonGap = 32f;   // status line to the buttons

    // Listening starts before anything can sign in and stops when this object goes, so a destroyed screen can
    // never be called back into. Paired deliberately: an event a dead object stays subscribed to is a leak that
    // throws on the next sign-in.
    void OnEnable()  { AccountSession.OnChanged += OnSignedIn; }
    void OnDisable() { AccountSession.OnChanged -= OnSignedIn; }

    void Start()
    {
        var canvas = FrontScreen.Canvas(transform);

        float panelHeight = 330f;
#if UNITY_ANDROID
        panelHeight += RowGap;   // the server line needs a row of its own
#endif
        // The title is the blue bar's ("Login"); the space the old big title took comes off the top.
        var panel = FrontScreen.Panel(canvas, new Vector2(430, panelHeight - TitleSpace - LineSpace), "Login", out _);

        // var line = UiKit.Label("Line", panel, "Sign in to continue", 14, FontStyle.Italic, TextAnchor.MiddleCenter);
        // line.color = UiKit.TextDim;
        // FrontScreen.Row(line.rectTransform, -82 + TitleSpace, 20);

        // Rows walk DOWN the panel from here rather than each carrying a hand-typed offset, so inserting the
        // phone's server line above them moves everything below it without a second copy of the layout.
        float y = -124f + TitleSpace + LineSpace;
#if UNITY_ANDROID
        serverField = FrontScreen.Field(panel, "Server", y, false);
        serverField.text = ServerAddress.TypedHost;   // whatever this device was told last time
        y -= RowGap;
#endif
        accountField = FrontScreen.Field(panel, "Account", y, false);
        y -= RowGap;
        passwordField = FrontScreen.Field(panel, "Password", y, true);
        y -= StatusGap;

        status = UiKit.Label("Status", panel, "", 13, FontStyle.Normal, TextAnchor.MiddleCenter);
        status.color = UiKit.TitleText;
        FrontScreen.Row(status.rectTransform, y, 18);
        y -= ButtonGap;

        enterButton = UiKit.Btn("Enter", panel, OnEnter);
        FrontScreen.Place(enterButton.GetComponent<RectTransform>(), new Vector2(-80, y), new Vector2(140, 34));

        createButton = UiKit.Btn("New Account", panel, OnCreate);
        FrontScreen.Place(createButton.GetComponent<RectTransform>(), new Vector2(80, y), new Vector2(140, 34));

        accountField.ActivateInputField();

        // Set before this screen's parts existed (the world was lost while it was being built), so it is said now.
        if (!string.IsNullOrEmpty(pendingMessage)) { Say(pendingMessage); pendingMessage = null; }
    }

    void Update()
    {
        // Enter moves down the form and then submits, which is what hands on a keyboard expect.
        if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;

        // ...unless a BUTTON holds the keyboard, which Tab now makes possible: pressing it belongs to the button,
        // and answering here as well would run the button AND a sign-in off one press.
        var events = EventSystem.current;
        var focused = events != null ? events.currentSelectedGameObject : null;
        if (focused != null && focused.GetComponent<Button>() != null) return;

        if (accountField.isFocused && passwordField.text.Length == 0) passwordField.ActivateInputField();
        else OnEnter();
    }

    void OnEnter() => Submit(signUpFirst: false);
    void OnCreate() => Submit(signUpFirst: true);

    void Submit(bool signUpFirst)
    {
        string email = accountField.text.Trim();
        string password = passwordField.text;

#if UNITY_ANDROID
        // Remembered before anything dials, because both the account service and the world read it from there.
        string host = serverField.text.Trim();
        if (host.Length == 0) { Say("Fill in the server, account and password."); return; }
        ServerAddress.TypedHost = host;
#endif
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

    // WHAT HAPPENS WHEN SOMEBODY SIGNS IN — answered ONCE, off the fact of signing in, not off the button that
    // usually causes it.
    //
    // It used to live inside the button's own reply handler, so it only ran for somebody who pressed the button.
    // Anything else that signed in — a test driving the game through the same functions a player's clicks reach —
    // got signed in and entered the world while this screen stayed up forever, a full-screen overlay at the very
    // top draw order covering the running game. The world was visible in the editor's scene view and completely
    // hidden in the game view, which reads as the game being stuck on the sign-in screen when it is not.
    //
    // ORDER MATTERS AND IS THE WHOLE REASON THIS SHAPE WORKS: the session is set BEFORE the reply handler runs
    // (AccountClient.cs:116, then :117). So this fires first, opens character select and takes the screen down,
    // and the reply handler below then finds itself destroyed and returns — which is now correct rather than a
    // dropped step, because everything it used to do has already happened here.
    void OnSignedIn()
    {
        // Unity's null test, so an already-destroyed screen answers true here. Called twice on purpose (see the
        // reply handler), and reaching gameObject on a destroyed object throws — so the second call stops here.
        if (this == null) return;
        if (string.IsNullOrEmpty(AccountSession.AccessToken)) return;   // a sign-OUT also raises this
        CharacterSelectScreen.Open();
        Destroy(gameObject);
    }

    void SignIn(string email, string password)
    {
        AccountClient.SignIn(email, password, (ok, error) =>
        {
            if (!ok) { if (this != null) { Busy(false); Say(error ?? "Could not sign in."); } return; }

            // BELT AND BRACES, DELIBERATELY. Normally this screen is already gone by now, because setting the
            // session raises the event above and that happens first. But relying on that order would make a
            // login silently do nothing if anybody ever reorders those two lines — and an ordering promise in
            // somebody else's file is exactly the kind that rots unnoticed. So say it again here; opening
            // character select twice is now a no-op, and destroying an already-destroyed object is too.
            OnSignedIn();
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
        ScaledScrollInput.Attach();

        // A camera of its own. The world's cameras are switched off until a character enters, and a screen with
        // no camera at all makes Unity paint "No cameras rendering" across the view. This one draws nothing —
        // it exists so the frame has an owner.
        var camGo = new GameObject("FrontCamera") { hideFlags = HideFlags.DontSaveInEditor };
        camGo.transform.SetParent(owner, false);
        var frontCam = camGo.AddComponent<Camera>();
        frontCam.clearFlags = CameraClearFlags.SolidColor;
        frontCam.backgroundColor = new Color(0.051f, 0.055f, 0.063f, 1f);   // #0D0E10
        frontCam.cullingMask = 0;
        frontCam.depth = -50;

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

        // Tab walks this screen's boxes and buttons in the order they are built. Attached HERE, at the one
        // canvas every front screen is made on, so sign-in and character select cannot end up with different
        // keyboard behaviour — and a screen added later gets it without being told.
        TabFocus.Attach(go);

        // Fully opaque: before you have signed in you are not in the world, so the world has no business being
        // visible. A partly-transparent veil reads as "the game is already running and you're locked out of it",
        // which is exactly the wrong impression.
        var veil = UiKit.Img("Veil", go.transform, new Color(0.051f, 0.055f, 0.063f, 1f));   // #0D0E10
        veil.raycastTarget = true;   // swallow every click so nothing behind the door can be reached
        UiKit.Fill(veil.rectTransform, Vector2.zero, Vector2.zero);

        Emblem(veil.transform);
        return veil.rectTransform;
    }

    // The game's mark, behind everything the screen puts on top of it. It is a SQUARE image, so it is placed as a
    // large centred emblem rather than stretched across a wide screen — stretching it would distort the logo, and
    // tiling it would read as wallpaper. Dimmed, because it is a backdrop and not the thing being read.
    static void Emblem(Transform veil)
    {
        var art = Resources.Load<Texture2D>("UI/unwritten-logo");
        if (art == null) return;   // no mark shipped — the screen is still perfectly usable without it

        var go = new GameObject("Emblem", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        go.transform.SetParent(veil, false);
        var image = go.GetComponent<RawImage>();
        image.texture = art;
        image.color = new Color(1f, 1f, 1f, 0.10f);
        image.raycastTarget = false;

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(900, 900);   // square art, square box — the ratio is never touched
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>A centred window with <paramref name="title"/> in its blue bar — no close, no minimise, no drag,
    /// because a front screen is the whole screen's business. <paramref name="size"/> is the area UNDER the bar;
    /// that area is returned to parent contents to, so every row's "from the top" starts below the title.
    /// The window root comes back through <paramref name="window"/>, to show or hide the whole thing.</summary>
    public static Transform Panel(Transform canvas, Vector2 size, string title, out GameObject window)
    {
        var border = UiKit.Round(UiKit.Img("Panel", canvas, UiKit.WindowEdge), UiKit.WindowRadius);
        border.rectTransform.anchorMin = border.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        border.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        border.rectTransform.sizeDelta = size + new Vector2(0f, BarHeight);
        border.rectTransform.anchoredPosition = Vector2.zero;
        window = border.gameObject;

        var fill = UiKit.Round(UiKit.Img("Fill", border.transform, UiKit.PanelFill), UiKit.WindowRadius - 1);
        UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        UiKit.Header(fill, title, border, closable: false, minimizable: false, draggable: false);

        var body = new GameObject("Body", typeof(RectTransform));
        body.transform.SetParent(fill.transform, false);
        UiKit.Fill((RectTransform)body.transform, Vector2.zero, new Vector2(0f, -BarHeight));
        return body.transform;
    }

    // The title bar's height, which a front screen's panel adds on top of its contents.
    const float BarHeight = UiKit.TitleBarHeight;

    /// <summary>A row whose top edge sits at <paramref name="top"/> from the panel's top edge, inset by
    /// <paramref name="left"/> and <paramref name="right"/>. The insets let a row share its line with something
    /// beside it — the body preview, for instance — without a second layout system.
    ///
    /// <para><paramref name="top"/> counts DOWNWARDS as a negative number, exactly like <see cref="Place"/>:
    /// one screen cannot have two opposite meanings for "how far from the top". (Measured: read as a positive
    /// distance it threw the title, the name box, both captions, the swatch strip and all three colour sliders
    /// clean off the top of the screen, above the panel they belong to.)</para></summary>
    public static void Row(RectTransform rt, float top, float height, float left = 24f, float right = 24f)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        // Position AND size come from the two offsets alone. Writing sizeDelta or anchoredPosition afterwards
        // discards them: the row keeps the right width but re-centres itself, so an uneven inset pushes it off
        // one edge. (Measured: left 250 / right 30 hung the name box 110 past the panel and slid the labels
        // over the preview.)
        rt.offsetMin = new Vector2(left, top - height);
        rt.offsetMax = new Vector2(-right, top);
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

        var box = UiKit.Field(UiKit.Img(label + "Box", panel, UiKit.SlotFill));
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
