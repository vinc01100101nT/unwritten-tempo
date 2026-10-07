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
    const int MaxLines = 200;   // per kind of line; oldest are trimmed past this so the log can't grow unbounded

    // Rich-text tints read from the theme once: the speaker's name, a command you typed (echoed back), the hint line.
    static readonly string SpeakerHex = UiKit.Hex(UiKit.TitleText);
    static readonly string EchoHex = UiKit.Hex(UiKit.Accent);
    static readonly string HintHex = UiKit.Hex(UiKit.TextDim);

    const float BubbleLife = 4f;   // seconds a said line floats over the player's head
    const float BodyAlpha = 0.5f;  // how solid the window's body is — everything below its title bar

    GameObject root;             // the window we show/hide
    RectTransform logContent;    // vertical list of message rows
    ScrollRect scroll;
    TMP_InputField input;
    TMP_InputField nameBox;      // who a line goes to as a private message; empty = said out loud
    bool built;

    // WHICH KIND OF LINE A ROW IS — what a tab shows, and what the row cap counts against. Guild, party and world
    // become members here and tabs below; nothing else about a row changes.
    enum LineKind { General, Pm }
    const int LineKinds = 2;

    // Each kind's rows, oldest first, so a full kind drops its OWN oldest — a busy fight's skill calls can never push
    // a private message out of the PM tab. Each kind keeps up to MaxLines.
    readonly Queue<GameObject>[] rowsOfKind = { new Queue<GameObject>(), new Queue<GameObject>() };

    // The tab showing: null = All, otherwise only rows of that kind.
    LineKind? showing;
    Button allTab, pmTab;

    // Everyone this person has messaged or been messaged by this session, most recent first — what Up and Down step
    // through in the name box. Bounded, like the log.
    const int MostPmPartners = 50;
    readonly List<string> pmPartners = new List<string>();
    int partnerStep = -1;

    static string pmHex;
    static string PmHex => pmHex ??= UiKit.Hex(UiKit.PmText);

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
    /// <para>Built here rather than by each caller so there is one sentence and one word order.</para></summary>
    public static void ItemArrived(Item def, int count, int upgrade)
    {
        if (instance == null || def == null || count <= 0) return;

        string name = upgrade > 0 ? "+" + upgrade + " " + def.displayName : def.displayName;
        if (count > 1) name += " ×" + count;
        instance.AddLine($"<color=#{ObtainedHex}>Obtained</color>  {Sanitize(name)}");
    }

    /// <summary>THE GAME telling the person at this keyboard something in a sentence — why the server refused a
    /// press, for one. Dim, like an arrival, because it is the game speaking and not a person.</summary>
    public static void GameSaid(string line, bool ok = true)
    {
        if (instance == null || string.IsNullOrEmpty(line)) return;
        instance.AddLine($"<color=#{(ok ? ObtainedHex : BadHex)}>{Sanitize(line)}</color>");
    }

    static string badHex;
    static string BadHex => badHex ??= ColorUtility.ToHtmlStringRGB(UiKit.Bad);

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

    /// <summary>A saved character SAYING a line: the same bubble over its head a creature's call gets, and a row in the
    /// log under its <paramref name="speaker"/> name, as anybody's said line has.</summary>
    public static void CreatureSaid(string speaker, Transform body, IStancePose pose, string line)
    {
        if (instance == null || string.IsNullOrEmpty(line)) return;
        instance.AddLine($"{SpeakerPrefix(speaker)} {Sanitize(line)}");
        CreatureCall(body, pose, line);
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
            SpeechBubble.Show(body, UnitAnchor.Lift(body) + 2f * UnitAnchor.HalfHeight(body) + SpeechBubble.HeadClearance,
                              skillName, BubbleLife);
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
        => string.IsNullOrEmpty(name) ? $"<color={SpeakerHex}>You:</color>"
                                      : $"<color={SpeakerHex}>{Sanitize(name)}:</color>";

    /// <summary>A PRIVATE MESSAGE, as the server delivered it: one this person received, or — <paramref name="outgoing"/>
    /// — their own, confirmed delivered. A row in the PM kind and no bubble over any head; the window opens if it
    /// was shut, without taking the keyboard, so a message is never missed.</summary>
    public static void Pm(string name, string text, bool outgoing)
    {
        if (instance == null || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(text)) return;
        instance.RememberPartner(name);
        string lead = outgoing ? "To " : "From ";
        instance.AddLine($"<color={PmHex}>{lead}{Sanitize(name)}:</color> {Sanitize(text)}", LineKind.Pm);
        if (instance.root != null && !UiAnim.IsOpen(instance.root)) UiAnim.Open(instance.root);
    }

    void RememberPartner(string name)
    {
        for (int i = 0; i < pmPartners.Count; i++)
            if (string.Equals(pmPartners[i], name, System.StringComparison.OrdinalIgnoreCase)) { pmPartners.RemoveAt(i); break; }
        pmPartners.Insert(0, name);
        if (pmPartners.Count > MostPmPartners) pmPartners.RemoveAt(pmPartners.Count - 1);
        partnerStep = -1;
    }

    // The tint a call is logged in, taken from the palette's own colour rather than typed here as a second copy.
    static string callHex;
    static string CallHex => callHex ??= ColorUtility.ToHtmlStringRGB(UiKit.Accent);

    void Update()
    {
        if (!built) { TryBuild(); return; }
        if (root == null) return;
        HandleKeys();
    }

    void HandleKeys()
    {
        bool enter = Input.GetKeyDown(GameKeys.Chat) || Input.GetKeyDown(GameKeys.ChatAlt);
        // Chat is the one opener that reads the key directly rather than through Hotkeys (it does its own typing
        // check, just below), so an on-screen button's press has to be admitted here too.
        enter = enter || Hotkeys.PressedOnScreen(GameKeys.Chat);
        // Open + focus on Enter — but only when NOT already typing (a submit press has Hotkeys.Typing == true,
        // so this branch is skipped and the InputField's own submit handles it instead).
        if (enter && !Hotkeys.Typing)
        {
            if (!UiAnim.IsOpen(root)) { UiAnim.Open(root); UISounds.PlayOpen(); }
            FocusInput();
            return;
        }
        if (nameBox != null && nameBox.isFocused) StepPartner(Hotkeys.ArrowStep());

        if (UiAnim.IsOpen(root) && Hotkeys.CancelDown())
        {
            if (input != null) input.DeactivateInputField();
            if (nameBox != null) nameBox.DeactivateInputField();
            UiAnim.Close(root);
            UISounds.PlayClose();
            Hotkeys.MarkCancelHandled();
        }
    }

    // Up walks back through the people this person has messaged with, Down forward again, wrapping at both ends.
    void StepPartner(int step)
    {
        if (step == 0 || pmPartners.Count == 0) return;
        partnerStep = partnerStep < 0 && step < 0
            ? pmPartners.Count - 1
            : ((partnerStep + step) % pmPartners.Count + pmPartners.Count) % pmPartners.Count;
        nameBox.text = pmPartners[partnerStep];
        nameBox.caretPosition = nameBox.text.Length;
    }

    // ---- build (once) ---------------------------------------------------------

    void TryBuild()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        // Lower-left of the 1280×720 reference canvas; draggable via the window header afterwards.
        var content = UiKit.Window(canvas.transform, "Chat", new Vector2(430, 250), new Vector2(-405, -205), out root);
        // The window's body (everything the minimise button folds away) is see-through, so the world stays visible
        // behind the chat; the title bar stays solid.
        UiKit.SeeThroughBody(root.GetComponent<Image>(), content.parent.GetComponent<Image>(), BodyAlpha, UiKit.WindowRadius);

        BuildTabs(content);
        BuildInput(content);
        BuildLog(content);
        TabFocus.Attach(root);   // Tab steps between the name box and the line box

        built = true;   // starts hidden (UiKit.Window inactivates root); opens on the first Enter
    }

    const float TabsHeight = 26f;
    const float NameBoxWidth = 110f;

    // All and PM, across the top of the window.
    void BuildTabs(RectTransform content)
    {
        var row = UiKit.TabRow(content, TabsHeight);
        allTab = UiKit.Btn("All", row, () => Show(null));
        pmTab = UiKit.Btn("PM", row, () => Show(LineKind.Pm));
        MarkTabs();
    }

    void Show(LineKind? kind)
    {
        showing = kind;
        for (int k = 0; k < LineKinds; k++)
        {
            bool visible = kind == null || (int)kind.Value == k;
            foreach (var row in rowsOfKind[k]) if (row != null) row.SetActive(visible);   // perf-ok: a tab click
        }
        MarkTabs();
        wantsScrollToNewest = true;
    }

    void MarkTabs()
    {
        UiKit.MarkTab(allTab, showing == null);
        UiKit.MarkTab(pmTab, showing == LineKind.Pm);
    }

    // The name box (left) and the line box (right), pinned to the bottom of the content area.
    void BuildInput(RectTransform content)
    {
        nameBox = BuildBox(content, "PmName", "PM to…", 0, NameBoxWidth);
        nameBox.onEndEdit.AddListener(OnNameEndEdit);

        input = BuildBox(content, "Input", "Say something, or /help…", NameBoxWidth + 4, 0);
        input.onEndEdit.AddListener(OnEndEdit);
    }

    // One single-line box at the bottom of the content area: <paramref name="left"/> in from the left edge, and
    // <paramref name="width"/> wide — or, at 0, stretching to the right edge.
    TMP_InputField BuildBox(RectTransform content, string name, string hint, float left, float width)
    {
        var bg = UiKit.Field(UiKit.Img(name, content, UiKit.SlotFill));
        bg.raycastTarget = true;
        UiKit.SeeThroughBody(bg, bg.transform.Find("Well").GetComponent<Image>(), BodyAlpha, UiKit.ButtonRadius);
        bg.rectTransform.anchorMin = new Vector2(0, 0);
        bg.rectTransform.anchorMax = new Vector2(width > 0 ? 0 : 1, 0);
        bg.rectTransform.pivot = new Vector2(0, 0);
        bg.rectTransform.sizeDelta = new Vector2(width > 0 ? width : -left, 28);
        bg.rectTransform.anchoredPosition = new Vector2(left, 0);

        var text = UiKit.Label("Text", bg.transform, "", 14, FontStyle.Normal, TextAnchor.MiddleLeft);
        text.richText = false;   // typed text is literal, never markup

        var placeholder = UiKit.Label("Placeholder", bg.transform, hint, 14, FontStyle.Italic, TextAnchor.MiddleLeft);
        placeholder.color = UiKit.TextDim;

        var box = bg.gameObject.AddComponent<TMP_InputField>();
        box.targetGraphic = bg;
        box.textComponent = text;
        box.placeholder = placeholder;
        box.lineType = TMP_InputField.LineType.SingleLine;
        UiKit.ClipTyping(box, 8);
        return box;
    }

    // Scrolling message log filling the content area above the input box.
    void BuildLog(RectTransform content)
    {
        // Clear, so the log reads at the window body's own see-through shade rather than stacking a second layer on it.
        var view = UiKit.Img("Log", content, Color.clear);
        view.raycastTarget = true;   // a graphic for the scroll wheel to land on
        UiKit.Fill(view.rectTransform, new Vector2(0, 34), new Vector2(0, -(TabsHeight + 4)));   // between the tabs and the 28px input (+ gaps)
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

        AddLine($"<color={HintHex}>Chat ready. Enter to type · /help for commands · Esc to close.</color>");
    }

    // ---- submit + log ---------------------------------------------------------

    void OnEndEdit(string value)
    {
        // onEndEdit also fires on focus-loss; only act on an actual Enter press.
        if (!(Input.GetKeyDown(GameKeys.Chat) || Input.GetKeyDown(GameKeys.ChatAlt))) return;

        var text = value != null ? value.Trim() : "";
        input.text = "";
        if (text.Length == 0) { input.DeactivateInputField(); return; }

        Submit(text);
        FocusInput();   // keep the box focused for rapid entry
    }

    // Enter in the name box moves on to the line box, where the message is typed.
    void OnNameEndEdit(string value)
    {
        if (!(Input.GetKeyDown(GameKeys.Chat) || Input.GetKeyDown(GameKeys.ChatAlt))) return;
        nameBox.text = value != null ? value.Trim() : "";
        FocusInput();
    }

    void Submit(string text)
    {
        // A COMMAND IS ALWAYS A COMMAND, whatever the name box holds — it runs, and is never sent to anybody.
        if (GmCommands.IsCommand(text))
        {
            AddLine($"<color={EchoHex}>{Sanitize(text)}</color>");
            GmCommands.Execute(text, AddLine);
        }
        else if (nameBox != null && !string.IsNullOrWhiteSpace(nameBox.text))
        {
            // A PRIVATE MESSAGE: shown only when the server says it was delivered, like a said line.
            if (!GameNetwork.SendPmToServer(nameBox.text, text)) GameSaid("Could not deliver that message.", false);
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
    // READ FROM THE ONE OWNER OF "WHICH BODY IS MINE", never searched for: the worn body is a Play-time object marked
    // not-to-be-saved, and a scene search skips exactly those — so the search found nothing and no bubble ever showed.
    IStancePose MyPose => StanceController.Player != null ? StanceController.Player.Pose : null;

    /// <summary>Every row the chat log gains, as the text it was given — raised whether or not the log is drawn, so a
    /// player reading the game as text (AgentLink) sees exactly the rows a player reading the screen does.</summary>
    public static event System.Action<string> LineAdded;

    /// <summary>Send a line exactly as if it were typed into the chat box and Enter pressed: a /command runs, anything
    /// else is said out loud. False when there is no chat to type into.</summary>
    public static bool Send(string text)
    {
        if (instance == null || string.IsNullOrWhiteSpace(text)) return false;
        instance.Submit(text.Trim());
        return true;
    }

    void AddLine(string message) => AddLine(message, LineKind.General);

    void AddLine(string message, LineKind kind)
    {
        LineAdded?.Invoke(message);
        if (logContent == null) return;

        var t = UiKit.MakeText("Line", logContent, message, 14, FontStyle.Normal, TextAnchor.UpperLeft);
        t.color = UiKit.TextMain;
        // A typed "\n" or "<" stays the characters it is, never a line break or a tag: one row is one line from
        // one person, and nothing typed can draw a second row that looks like it came from somebody else.
        t.parseCtrlCharacters = false;
        if (showing != null && showing.Value != kind) t.gameObject.SetActive(false);

        // Trim each kind's oldest rows so the log stays bounded — a kind never pushes out another kind's rows.
        var rows = rowsOfKind[(int)kind];
        rows.Enqueue(t.gameObject);
        while (rows.Count > MaxLines) { var oldest = rows.Dequeue(); if (oldest != null) Destroy(oldest); }

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
    public static string Sanitize(string s) => string.IsNullOrEmpty(s) ? s : s.Replace("<", "<​");
}
