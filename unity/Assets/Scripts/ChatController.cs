using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The in-game <b>chat</b> window — a scrolling message log plus a one-line input box, self-built onto the
/// persistent GameUICanvas (added by <see cref="Editor.UIShellBuilder"/>). Press <b>Enter</b> to open + focus
/// the box; type and Enter again to send; Esc closes.
///
/// <para>Input focus rides the shared typing guard: while the box is focused, <see cref="Hotkeys.Typing"/> is
/// true, so every gameplay/HUD key (WASD, hotbar, window toggles) is suppressed automatically — no per-site
/// "am I typing?" checks. Plain lines are SAID; lines starting with '/' route to <see cref="GmCommands"/>,
/// the single command dispatcher.</para>
///
/// <para><b>A said line goes to the server and comes back.</b> In a world nothing is shown at the keyboard that
/// typed it: the line crosses to the server, which says it to everybody standing near enough to see the speaker —
/// the speaker included — so one line said is one row and one bubble on every screen, decided in one place. With
/// no world there is nobody to ask and the line simply lands here. Somebody else's line, and somebody else's skill
/// call, arrive through <see cref="Heard"/> and are drawn by the very same body as your own.</para>
/// </summary>
public class ChatController : MonoBehaviour
{
    const int MaxLines = 200;   // oldest lines are trimmed past this so the log can't grow unbounded

    static readonly Color SayColor   = new Color(0.62f, 0.70f, 0.80f);   // "You:" prefix
    static readonly Color EchoColor  = new Color(0.784f, 0.659f, 0.384f); // a command you typed (echoed back)
    static readonly Color InputFill  = new Color(0.05f, 0.055f, 0.07f, 1f);

    const float BubbleLife = 4f;   // seconds a said line floats over the player's head

    GameObject root;             // the window we show/hide
    RectTransform logContent;    // vertical list of message rows
    ScrollRect scroll;
    TMP_InputField input;
    bool built;
    Character3DVisual playerVisual;   // resolved lazily on first say — its feet-seated pose anchors the chat bubble

    static ChatController instance;   // the one live chat, so the game can post a line without a lookup

    void Awake() { instance = this; }
    void OnDestroy() { if (instance == this) instance = null; }

    /// <summary>A skill CALL by the person at this keyboard: they name the skill they just fired. Identical in every
    /// way to saying that word in chat — the same speaker prefix, the same white bubble over the head for the same
    /// time, the same log row — only tinted, so a call reads apart from a said line at a glance.</summary>
    public static void SkillCall(string skillName)
    {
        if (instance == null) return;
        Post("", skillName, instance.MyPose, true);
    }

    /// <summary>A line the person at this keyboard said. Used when the server hands their own line back to them, and
    /// when there is no server to hand it back.</summary>
    public static void Said(string text)
    {
        if (instance == null) return;
        Post("", text, instance.MyPose, false);
    }

    /// <summary>THE game telling the person at this keyboard that something arrived in their bag — a kill's loot,
    /// a command, a thing picked up off the floor, and anything that is written later. It reads the same wherever
    /// it came from, because WHERE it came from is not what a player needs to be told; WHAT they now have is.
    ///
    /// <para>Built here rather than by each caller so there is one sentence, one word order and one colour. The
    /// name carries its rarity colour, exactly as it does in the bag list and on the floor.</para></summary>
    public static void ItemArrived(Item def, int count, Rarity rarity, int upgrade)
    {
        if (instance == null || def == null || count <= 0) return;

        string name = upgrade > 0 ? "+" + upgrade + " " + def.displayName : def.displayName;
        if (count > 1) name += " ×" + count;
        instance.AddLine($"<color=#{ObtainedHex}>Obtained</color>  "
                         + $"<color=#{ColorUtility.ToHtmlStringRGB(Rarities.Text(rarity))}>{Sanitize(name)}</color>");
    }

    // The dim tint the word in front of an arrival is written in, taken from the palette rather than typed here.
    static string obtainedHex;
    static string ObtainedHex => obtainedHex ??= ColorUtility.ToHtmlStringRGB(UiKit.TextDim);

    /// <summary>SOMEBODY ELSE speaking — a line they said, or the skill they just called out. It lands exactly as
    /// one of your own does: the same log row, the same white bubble over their head for the same time, only their
    /// name in front of it instead of "You". <paramref name="over"/> is the head to hang the bubble over; null when
    /// their body has not been built on this machine yet, which simply means no bubble.</summary>
    public static void Heard(string speaker, string text, IStancePose over, bool isSkillCall)
    {
        if (instance == null) return;
        Post(speaker, text, over, isSkillCall);
    }

    /// <summary>A creature calling out a skill: the bubble over its head and nothing in the log. A field of armed
    /// creatures casting would otherwise bury every line a person said.</summary>
    public static void CreatureCall(Transform body, IStancePose pose, string skillName)
    {
        if (instance == null || string.IsNullOrEmpty(skillName)) return;
        // A live-3D creature has a head pose to ride; a flat sprite creature has none, so its bubble sits just above
        // the crown of its sprite — the same clearance every bubble keeps.
        if (pose != null) SpeechBubble.ShowOverHead(pose, skillName, BubbleLife);
        else if (body != null)
            SpeechBubble.Show(body, 2f * UnitAnchor.HalfHeight(body) + SpeechBubble.HeadClearance, skillName, BubbleLife);
    }

    // THE one place a spoken line becomes a row in the log and a bubble over a head. A said line and a skill call,
    // mine and everybody else's, all four arrive here — so no two of them can drift apart.
    // Cold path: once per line somebody says.
    static void Post(string speaker, string text, IStancePose over, bool isSkillCall)
    {
        if (instance == null || string.IsNullOrEmpty(text)) return;
        string body = isSkillCall ? $"<color=#{CallHex}>{Sanitize(text)}</color>" : Sanitize(text);
        instance.AddLine($"{SpeakerPrefix(speaker)} {body}");
        if (over != null) SpeechBubble.ShowOverHead(over, text, BubbleLife);
    }

    // Who is speaking, written once: a said line and a skill call are the same person talking, so they share this
    // rather than each typing their own copy. An empty name is the person at this keyboard.
    static string SpeakerPrefix(string name)
        => string.IsNullOrEmpty(name) ? "<color=#9FB4CC>You:</color>"
                                      : $"<color=#9FB4CC>{Sanitize(name)}:</color>";

    // The tint a call is logged in, taken from the palette's own colour rather than typed here as a second copy.
    static string callHex;
    static string CallHex => callHex ??= ColorUtility.ToHtmlStringRGB(UiKit.Cyan);

    void Update()
    {
        if (!built) { TryBuild(); return; }
        if (root == null) return;
        HandleKeys();
    }

    void HandleKeys()
    {
        bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#if UNITY_ANDROID
        // Chat is the one opener that reads the key directly rather than through Hotkeys (it does its own typing
        // check, just below), so the on-screen button has to be admitted here too.
        enter = enter || TouchControls.VirtualDown(KeyCode.Return);
#endif
        // Open + focus on Enter — but only when NOT already typing (a submit press has Hotkeys.Typing == true,
        // so this branch is skipped and the InputField's own submit handles it instead).
        if (enter && !Hotkeys.Typing)
        {
            if (!UiAnim.IsOpen(root)) { UiAnim.Open(root); UISounds.PlayOpen(); }
            FocusInput();
            return;
        }
        if (UiAnim.IsOpen(root) && Hotkeys.CancelDown())
        {
            if (input != null) input.DeactivateInputField();
            UiAnim.Close(root);
            UISounds.PlayClose();
            Hotkeys.MarkCancelHandled();
        }
    }

    // ---- build (once) ---------------------------------------------------------

    void TryBuild()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        // Lower-left of the 1280×720 reference canvas; draggable via the window header afterwards.
        var content = UiKit.Window(canvas.transform, "Chat", new Vector2(430, 250), new Vector2(-405, -205), out root);

        BuildInput(content);
        BuildLog(content);

        built = true;   // starts hidden (UiKit.Window inactivates root); opens on the first Enter
    }

    // One-line input box pinned to the bottom of the content area.
    void BuildInput(RectTransform content)
    {
        var bg = UiKit.Img("Input", content, InputFill);
        bg.raycastTarget = true;
        bg.rectTransform.anchorMin = new Vector2(0, 0);
        bg.rectTransform.anchorMax = new Vector2(1, 0);
        bg.rectTransform.pivot = new Vector2(0.5f, 0);
        bg.rectTransform.sizeDelta = new Vector2(0, 28);
        bg.rectTransform.anchoredPosition = Vector2.zero;

        var text = UiKit.Label("Text", bg.transform, "", 14, FontStyle.Normal, TextAnchor.MiddleLeft);
        text.richText = false;   // typed text is literal, never markup
        UiKit.Fill(text.rectTransform, new Vector2(8, 0), new Vector2(-8, 0));

        var placeholder = UiKit.Label("Placeholder", bg.transform, "Say something, or /help…", 14, FontStyle.Italic, TextAnchor.MiddleLeft);
        placeholder.color = UiKit.TextDim;
        UiKit.Fill(placeholder.rectTransform, new Vector2(8, 0), new Vector2(-8, 0));

        input = bg.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = bg;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onEndEdit.AddListener(OnEndEdit);
    }

    // Scrolling message log filling the content area above the input box.
    void BuildLog(RectTransform content)
    {
        var view = UiKit.Img("Log", content, new Color(0, 0, 0, 0.15f));
        view.raycastTarget = true;   // a graphic for the scroll wheel to land on
        UiKit.Fill(view.rectTransform, new Vector2(0, 34), new Vector2(0, 0));   // sit above the 28px input (+ gap)
        view.gameObject.AddComponent<RectMask2D>();

        scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 22f;

        var sc = new GameObject("LogContent", typeof(RectTransform));
        logContent = (RectTransform)sc.transform;
        logContent.SetParent(view.rectTransform, false);
        logContent.anchorMin = new Vector2(0, 0); logContent.anchorMax = new Vector2(1, 0); logContent.pivot = new Vector2(0.5f, 0);
        logContent.offsetMin = logContent.offsetMax = Vector2.zero;

        var vlg = sc.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 2;
        vlg.padding = new RectOffset(6, 6, 4, 4);
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.LowerLeft;
        sc.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = view.rectTransform;
        scroll.content = logContent;

        AddLine("<color=#A79E88>Chat ready. Enter to type · /help for commands · Esc to close.</color>");
    }

    // ---- submit + log ---------------------------------------------------------

    void OnEndEdit(string value)
    {
        // onEndEdit also fires on focus-loss; only act on an actual Enter press.
        if (!(Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) return;

        var text = value != null ? value.Trim() : "";
        input.text = "";
        if (text.Length == 0) { input.DeactivateInputField(); return; }

        Submit(text);
        FocusInput();   // keep the box focused for rapid entry
    }

    void Submit(string text)
    {
        if (GmCommands.IsCommand(text))
        {
            AddLine($"<color=#C8A862>{Sanitize(text)}</color>");
            GmCommands.Execute(text, AddLine);
        }
        else
        {
            // SAID TO THE SERVER when there is one, and it says the line back to everybody standing near enough to
            // hear it — this machine included. That is why nothing is shown here in that case: one line said is one
            // row and one bubble for everybody, decided in one place, so the speaker cannot see a different chat
            // from the people listening. With no server there is nobody to ask and the line lands here.
            if (!GameNetwork.SendSayToServer(text)) Said(text);
        }
    }

    // The head THIS machine's own player speaks over — their avatar's feet-seated visual, which is what the white
    // NPC bark bubble already anchors to, through the same path so a said line sits exactly where a bark does.
    // The find happens once per session and is kept.
    IStancePose MyPose
    {
        get
        {
            if (playerVisual == null) playerVisual = FindAnyObjectByType<Character3DVisual>();   // perf-ok: once per line said or skill called, never per frame
            return playerVisual != null ? playerVisual.Pose : null;
        }
    }

    void AddLine(string message)
    {
        if (logContent == null) return;

        var t = UiKit.MakeText("Line", logContent, message, 14, FontStyle.Normal, TextAnchor.UpperLeft);
        t.color = UiKit.TextMain;

        // Trim the oldest rows so the log stays bounded.
        while (logContent.childCount > MaxLines) Destroy(logContent.GetChild(0).gameObject);

        // ASKED FOR, NOT DONE HERE. Snapping to the newest line needs a layout pass forced through first, and that
        // rebuilds every dirty canvas on screen. It used to run per line added, which was fine when the only lines
        // were the ones typed at this keyboard — a handful a minute. Every nearby person's said lines and every
        // nearby person's skill calls now land here too, so the cost multiplies by how many people are standing
        // around: five in a fight is several rebuilds a second, forever, for one scroll position. Once a frame,
        // however many lines arrived in it, costs the same as it ever did for one.
        wantsScrollToNewest = true;
    }

    bool wantsScrollToNewest;

    void LateUpdate()
    {
        if (!wantsScrollToNewest) return;
        wantsScrollToNewest = false;
        Canvas.ForceUpdateCanvases();
        if (scroll != null) scroll.verticalNormalizedPosition = 0f;
    }

    void FocusInput()
    {
        if (input == null) return;
        input.ActivateInputField();
        input.Select();
    }

    // Neutralize rich-text markup in user/echoed text so a stray '<' can't corrupt the log's formatting.
    static string Sanitize(string s) => string.IsNullOrEmpty(s) ? s : s.Replace("<", "<​");
}
