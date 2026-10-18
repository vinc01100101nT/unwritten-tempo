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
    ChatLinkInput linkInput;
    TMP_InputField nameBox;      // who a line goes to as a private message; empty = said out loud
    bool built;

    // WHICH KIND OF LINE A ROW IS — what a tab shows, and what the row cap counts against. Guild, party and world
    // become members here and tabs below; nothing else about a row changes.
    enum LineKind { General, Pm, CombatLog }
    const int LineKinds = 3;

    // Each kind's rows, oldest first, so a full kind drops its OWN oldest — a busy fight's skill calls can never push
    // a private message out of the PM tab. Each kind keeps up to MaxLines.
    readonly Queue<GameObject>[] rowsOfKind = { new Queue<GameObject>(), new Queue<GameObject>(), new Queue<GameObject>() };

    // The tab showing: null = All, otherwise only rows of that kind.
    LineKind? showing;
    Button allTab, pmTab, combatTab;

    // THE ALL TAB LEAVES THE COMBAT LOG OUT: a fight writes a line per blow, and All is where people's words are read.
    static bool InAll(int kind) => kind != (int)LineKind.CombatLog;

    // Whether a row of `kind` shows under the tab now chosen.
    bool Showing(int kind) => showing == null ? InAll(kind) : (int)showing.Value == kind;

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
    public static void ItemArrived(Item def, int count, string title)
    {
        if (instance == null || def == null || count <= 0) return;

        string name = count > 1 ? title + " ×" + count : title;
        instance.AddLine($"<color=#{ObtainedHex}>Obtained</color>  {Sanitize(name)}");
        // And in the combat log, beside what the kill paid.
        instance.AddCombat(new CombatEntry { line = CombatLine.Obtained, name = name });
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
        if (isSkillCall)
            instance.AddLine($"{SpeakerPrefix(speaker)} <color=#{CallHex}>{Sanitize(text)}</color>");
        else
            instance.AddLinkedLine(SpeakerPrefix(speaker) + " ", text, LineKind.General);
        // A bubble draws no colours: each linked item is its bare "[name]" there.
        if (over != null) SpeechBubble.ShowOverHead(over, ChatLinks.Plain(text), BubbleLife);
    }

    // A row whose text may name items (ChatLinks): each one drawn in its colour, and its tooltip shown while the pointer
    // is on it. The same for every kind of line that can carry one.
    void AddLinkedLine(string lead, string text, LineKind kind)
    {
        linkTitles.Clear(); linkColours.Clear(); linkBodies.Clear(); linkItems.Clear();
        string body = ChatLinks.ToRichText(text, Sanitize, linkTitles, linkColours, linkBodies, linkItems);
        var row = AddLine(lead + body, kind);
        if (row != null && linkTitles.Count > 0) ChatLinkRow.Attach(row, linkTitles, linkColours, linkBodies, linkItems);
    }

    static readonly List<string> linkTitles = new List<string>(ChatLinks.MostLinksInALine);
    static readonly List<Color> linkColours = new List<Color>(ChatLinks.MostLinksInALine);
    static readonly List<string> linkItems = new List<string>(ChatLinks.MostLinksInALine);
    static readonly List<string> linkBodies = new List<string>(ChatLinks.MostLinksInALine);

    /// <summary>Shift-click on a thing you hold: its name goes into the chat field as a link, where the caret is, and the
    /// field takes the keyboard so the line can be finished and sent.</summary>
    public static void InsertItemLink(BagEntry thing)
    {
        if (instance == null || thing == null || thing.def == null) return;
        if (!instance.built) instance.TryBuild();
        if (instance.input == null || instance.linkInput == null) return;
        if (instance.root != null && !UiAnim.IsOpen(instance.root)) UiAnim.Open(instance.root);
        instance.linkInput.Insert(thing);
        instance.FocusInput();
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
        instance.AddLinkedLine($"<color={PmHex}>{lead}{Sanitize(name)}:</color> ", text, LineKind.Pm);
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

    // All, PM and Combat Log, across the top of the window.
    void BuildTabs(RectTransform content)
    {
        var row = UiKit.TabRow(content, TabsHeight);
        allTab = UiKit.Btn("All", row, () => Show(null));
        pmTab = UiKit.Btn("PM", row, () => Show(LineKind.Pm));
        combatTab = UiKit.Btn("Combat Log", row, () => Show(LineKind.CombatLog));
        MarkTabs();
    }

    void Show(LineKind? kind)
    {
        showing = kind;
        for (int k = 0; k < LineKinds; k++)
        {
            bool visible = Showing(k);
            foreach (var row in rowsOfKind[k]) if (row != null) row.SetActive(visible);   // perf-ok: a tab click
        }
        MarkTabs();
        wantsScrollToNewest = true;
    }

    void MarkTabs()
    {
        UiKit.MarkTab(allTab, showing == null);
        UiKit.MarkTab(pmTab, showing == LineKind.Pm);
        UiKit.MarkTab(combatTab, showing == LineKind.CombatLog);
    }

    // The name box (left) and the line box (right), pinned to the bottom of the content area.
    void BuildInput(RectTransform content)
    {
        nameBox = BuildBox(content, "PmName", "PM to…", 0, NameBoxWidth);
        nameBox.onEndEdit.AddListener(OnNameEndEdit);

        input = BuildBox(content, "Input", "Say something, or /help…", NameBoxWidth + 4, 0);
        input.onEndEdit.AddListener(OnEndEdit);
        // Items named in the line: each one whole, in its colour, never edited from the inside.
        linkInput = input.gameObject.AddComponent<ChatLinkInput>();
        linkInput.Bind(input);
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
        UiKit.Paint(placeholder, UiKit.TextDim);

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

        // The items the line names go as references to the server, which turns them into links (ChatLinks).
        var text = linkInput != null ? linkInput.Encode(value) : value;
        text = text != null ? text.Trim() : "";
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
            if (!GameNetwork.SendSayToServer(text))
                Said(ChatLinks.Resolve(text, Character.Instance, GameNetwork.MostCharactersInALine));
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

    TMP_Text AddLine(string message, LineKind kind)
    {
        LineAdded?.Invoke(message);
        if (logContent == null) return null;

        var t = UiKit.MakeText("Line", logContent, message, 14, FontStyle.Normal, TextAnchor.UpperLeft);
        UiKit.Paint(t, UiKit.TextMain);
        // A typed "\n" or "<" stays the characters it is, never a line break or a tag: one row is one line from
        // one person, and nothing typed can draw a second row that looks like it came from somebody else.
        t.parseCtrlCharacters = false;
        if (!Showing((int)kind)) t.gameObject.SetActive(false);

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
        return t;
    }

    bool wantsScrollToNewest;
    float nextCombatDraw;
    const float CombatDrawSeconds = 0.25f;

    void LateUpdate()
    {
        FlushRewards();
        // Combat lines become rows only while they can be seen, and at most four times a second: each batch costs a
        // layout pass over the list, and a fight lands a line every few frames. The ring keeps them meanwhile.
        if (showing == LineKind.CombatLog && root != null && UiAnim.IsOpen(root) && Time.unscaledTime >= nextCombatDraw)
        {
            nextCombatDraw = Time.unscaledTime + CombatDrawSeconds;
            DrawNewCombat();
        }
        if (!wantsScrollToNewest) return;
        wantsScrollToNewest = false;
        Canvas.ForceUpdateCanvases();
        if (scroll != null) scroll.verticalNormalizedPosition = 0f;
    }

    // ---- the combat log ---------------------------------------------------------
    //
    // EVERY BLOW THIS PLAYER DEALS OR TAKES, every kill and what it paid — kept as the server's own numbers in a fixed
    // ring, and turned into words only while the Combat Log tab is the one showing. A fight lands a line per blow, so
    // nothing here builds a string or an object per blow: the ring is written in place, and the rows are made once and
    // then reused oldest-first. None of it is raised through LineAdded, and none of it shows under All.

    enum CombatLine : byte
    {
        Dealt, Missed, DealtTick, Taken, MissedMe, Dodged, Parried, Blocked, TookTick, Defeated, Experience, Zeny, Obtained,
    }

    struct CombatEntry
    {
        public CombatLine line;
        public string name;
        public int amount, fromCombo;
        public bool crit;
        public TrueDamageCause cause;
    }

    readonly CombatEntry[] combat = new CombatEntry[MaxLines];
    long combatWritten, combatDrawn;

    // The combat log's rows as text, in the same order as its kind's queue of rows, so a reused row needs no lookup.
    readonly Queue<TMP_Text> combatRows = new Queue<TMP_Text>();

    // A KILL'S PAYMENT WAITS FOR THE END OF THE FRAME. The creature dies inside the blow that kills it, so the server
    // tells this machine what the kill paid before it tells it about that blow — both in the same frame. Held here, the
    // blow's line goes in first and the kill's lines follow it; the rescue zeny a turned-back character pays goes last.
    readonly CombatEntry[] heldRewards = new CombatEntry[8];
    int heldCount;

    // The words of one row, written by hand so a number never becomes a string.
    readonly char[] lineChars = new char[160];
    int lineLength;

    /// <summary>A blow this machine's player landed on a creature, as the server drew it.</summary>
    public static void CombatDealt(string target, int number, bool missed, bool crit, TrueDamageCause cause)
    {
        if (instance == null) return;
        var line = missed ? CombatLine.Missed : cause != TrueDamageCause.None ? CombatLine.DealtTick : CombatLine.Dealt;
        instance.AddCombat(new CombatEntry { line = line, name = target, amount = number, crit = crit, cause = cause });
    }

    /// <summary>A blow on this machine's player, as the server decided it. <paramref name="tick"/> is the cause of a
    /// damage-over-time tick, or None for a blow.</summary>
    public static void CombatTaken(string striker, int amount, bool missed, bool dodged, bool crit, TrueDamageCause tick,
                                   bool parried, bool blocked)
    {
        if (instance == null) return;
        var line = missed ? CombatLine.MissedMe
                 : dodged ? CombatLine.Dodged
                 : tick != TrueDamageCause.None ? CombatLine.TookTick
                 : parried ? CombatLine.Parried
                 : blocked ? CombatLine.Blocked
                 : CombatLine.Taken;
        instance.AddCombat(new CombatEntry { line = line, name = striker, amount = amount, crit = crit, cause = tick });
    }

    /// <summary>This machine's player killed <paramref name="victim"/>.</summary>
    public static void CombatDefeated(string victim)
    {
        if (instance != null) instance.HoldReward(new CombatEntry { line = CombatLine.Defeated, name = victim });
    }

    /// <summary>What a kill paid in experience, and how much of it the run of kills added.</summary>
    public static void CombatExperience(int amount, int fromCombo)
    {
        if (instance != null) instance.HoldReward(new CombatEntry { line = CombatLine.Experience, amount = amount, fromCombo = fromCombo });
    }

    /// <summary>Zeny a kill caused, and how much of it the run of kills added.</summary>
    public static void CombatZeny(int amount, int fromCombo)
    {
        if (instance != null) instance.HoldReward(new CombatEntry { line = CombatLine.Zeny, amount = amount, fromCombo = fromCombo });
    }

    void AddCombat(in CombatEntry e)
    {
        combat[(int)(combatWritten % MaxLines)] = e;
        combatWritten++;
    }

    void HoldReward(in CombatEntry e)
    {
        if (heldCount == heldRewards.Length) FlushRewards();
        heldRewards[heldCount++] = e;
    }

    void FlushRewards()
    {
        if (heldCount == 0) return;
        for (int i = 0; i < heldCount; i++) if (heldRewards[i].line != CombatLine.Zeny) AddCombat(heldRewards[i]);
        for (int i = 0; i < heldCount; i++) if (heldRewards[i].line == CombatLine.Zeny) AddCombat(heldRewards[i]);
        heldCount = 0;
    }

    // Every entry written since the last draw becomes a row — at most the newest MaxLines of them.
    void DrawNewCombat()
    {
        if (logContent == null || combatDrawn == combatWritten) return;
        if (combatWritten - combatDrawn > MaxLines) combatDrawn = combatWritten - MaxLines;
        var rows = rowsOfKind[(int)LineKind.CombatLog];
        for (; combatDrawn < combatWritten; combatDrawn++)
        {
            ref readonly var e = ref combat[(int)(combatDrawn % MaxLines)];
            TMP_Text t;
            if (rows.Count < MaxLines)
            {
                t = UiKit.MakeText("CombatLine", logContent, "", 14, FontStyle.Normal, TextAnchor.UpperLeft);   // perf-ok: grows to MaxLines once, then every row is reused
                t.richText = false;
                t.parseCtrlCharacters = false;
            }
            else
            {
                rows.Dequeue();
                t = combatRows.Dequeue();
                t.transform.SetAsLastSibling();
            }
            rows.Enqueue(t.gameObject);
            combatRows.Enqueue(t);
            WriteCombat(e);
            t.SetCharArray(lineChars, 0, lineLength);
            UiKit.Paint(t, ColourOf(e.line));
        }
        wantsScrollToNewest = true;
    }

    static ThemeColor ColourOf(CombatLine line)
    {
        switch (line)
        {
            case CombatLine.Taken: case CombatLine.TookTick: return UiKit.Bad;
            case CombatLine.Defeated: return UiKit.Accent;
            case CombatLine.Experience: case CombatLine.Zeny: return UiKit.Good;
            case CombatLine.Obtained: return UiKit.TextDim;
            default: return UiKit.TextMain;
        }
    }

    // One entry in words: "You hit Poring for 12", "+120 EXP (+12 from combo)".
    void WriteCombat(in CombatEntry e)
    {
        lineLength = 0;
        switch (e.line)
        {
            case CombatLine.Dealt:
                Put("You hit "); Put(e.name ?? "a creature"); Put(" for "); Put(e.amount); if (e.crit) Put(" (critical)"); break;
            case CombatLine.Missed:
                Put("You missed "); Put(e.name ?? "a creature"); break;
            case CombatLine.DealtTick:
                Put(e.name ?? "A creature"); Put(" took "); Put(e.amount); Put(" from "); Put(CauseWords(e.cause)); break;
            case CombatLine.Taken:
                Put(e.name ?? "Something"); Put(" hit you for "); Put(e.amount); if (e.crit) Put(" (critical)"); break;
            case CombatLine.MissedMe:
                Put(e.name ?? "Something"); Put(" missed you"); break;
            case CombatLine.Dodged:
                Put("You dodged "); Put(e.name ?? "a blow"); break;
            case CombatLine.Parried:
                Put("You parried "); Put(e.name ?? "a blow"); break;
            case CombatLine.Blocked:
                Put("You blocked "); Put(e.name ?? "a blow"); break;
            case CombatLine.TookTick:
                Put("You took "); Put(e.amount); Put(" from "); Put(CauseWords(e.cause)); break;
            case CombatLine.Defeated:
                Put("You defeated "); Put(e.name ?? "a creature"); break;
            case CombatLine.Experience:
                Put("+"); Put(e.amount); Put(" EXP"); if (e.fromCombo > 0) { Put(" (+"); Put(e.fromCombo); Put(" from combo)"); } break;
            case CombatLine.Zeny:
                Put("+"); Put(e.amount); Put("u"); if (e.fromCombo > 0) { Put(" (+"); Put(e.fromCombo); Put(" from combo)"); } break;
            case CombatLine.Obtained:
                Put("Obtained "); Put(e.name); break;
        }
    }

    static string CauseWords(TrueDamageCause cause)
    {
        switch (cause)
        {
            case TrueDamageCause.Poison: return "poison";
            case TrueDamageCause.Bleed: return "bleeding";
            case TrueDamageCause.Burn: return "burning";
            case TrueDamageCause.BraceDebt: return "a brace coming due";
            case TrueDamageCause.ReflectedByPerson:
            case TrueDamageCause.ReflectedByCreature: return "reflected damage";
            case TrueDamageCause.GmKill: return "a GM kill";
            default: return "damage";
        }
    }

    void Put(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        int n = Mathf.Min(s.Length, lineChars.Length - lineLength);
        s.CopyTo(0, lineChars, lineLength, n);
        lineLength += n;
    }

    // A whole number, digit by digit into the row, so no string is made for it.
    void Put(int value)
    {
        if (value < 0) { Put("-"); value = value == int.MinValue ? int.MaxValue : -value; }
        int start = lineLength, v = value;
        do
        {
            if (lineLength == lineChars.Length) break;
            lineChars[lineLength++] = (char)('0' + v % 10);
            v /= 10;
        } while (v > 0);
        System.Array.Reverse(lineChars, start, lineLength - start);
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
