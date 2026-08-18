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
/// "am I typing?" checks. Plain lines echo as chat; lines starting with '/' route to <see cref="GmCommands"/>,
/// the single command dispatcher (gameplay commands are declared there later).</para>
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

    /// <summary>A skill CALL: the player names the skill they just fired. Identical in every way to saying that word
    /// in chat — the same speaker prefix, the same white bubble over the head for the same time, the same log row —
    /// only tinted, so a call reads apart from a said line at a glance. The ONE entry point, so the two can't drift.</summary>
    public static void SkillCall(string skillName)
    {
        if (instance == null || string.IsNullOrEmpty(skillName)) return;
        instance.AddLine($"{SpeakerPrefix} <color=#{CallHex}>{Sanitize(skillName)}</color>");
        instance.ShowOverheadBubble(skillName);
    }

    // Who is speaking, written once: a said line and a skill call are the same person talking, so they share this
    // rather than each typing their own copy.
    const string SpeakerPrefix = "<color=#9FB4CC>You:</color>";

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
            AddLine($"{SpeakerPrefix} {Sanitize(text)}");
            ShowOverheadBubble(text);
        }
    }

    // Float the said line over the player's head in the same white bubble the NPCs use for their barks, through the
    // exact same anchor path (SpeechBubble.ShowOverHead) so it sits identically. Cold path: once per sent line.
    void ShowOverheadBubble(string line)
    {
        if (playerVisual == null) playerVisual = FindAnyObjectByType<Character3DVisual>();
        if (playerVisual == null) return;
        SpeechBubble.ShowOverHead(playerVisual.Pose, line, BubbleLife);
    }

    void AddLine(string message)
    {
        if (logContent == null) return;

        var t = UiKit.MakeText("Line", logContent, message, 14, FontStyle.Normal, TextAnchor.UpperLeft);
        t.color = UiKit.TextMain;

        // Trim the oldest rows so the log stays bounded.
        while (logContent.childCount > MaxLines) Destroy(logContent.GetChild(0).gameObject);

        // Snap to the newest line (force a layout pass first so the scroll clamps correctly).
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
