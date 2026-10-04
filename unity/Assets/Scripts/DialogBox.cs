using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A bottom-of-screen conversation box for the F-interact feature. Shows the speaker + one line at a time with a
/// NEXT button (bottom-right, labelled with a ▶) that pages through the lines; on the last line the button reads
/// CLOSE. The Right-Arrow key advances too. When it closes (CLOSE, or advancing past the last line) it fires the
/// caller's <c>onClosed</c> callback — <see cref="PlayerInteractor"/> uses that to end the cinematic and restore
/// the HUD.
///
/// <para>Self-building like <see cref="SpeechBubble"/>/<see cref="DamagePopup"/>: it constructs its own screen-space
/// canvas on first use (no scene wiring), and rebuilds lazily if a scene unload destroys it. ESC is handled by the
/// caller (it owns the whole interaction), which calls <see cref="Close"/>.</para>
/// </summary>
[DisallowMultipleComponent]
public class DialogBox : MonoBehaviour
{
    public static DialogBox Instance { get; private set; }

    GameObject panel;
    TMP_Text speakerText, bodyText, nextLabel, acceptLabel;
    GameObject acceptButton;

    string[] lines;
    int index;
    Action onClosed;
    // A choice offered on the last line — a quest to accept. Null for an ordinary conversation.
    Action onAccept;

    public bool IsOpen => panel != null && panel.activeSelf;

    /// <summary>The speaker and every line of the conversation that is open, or null when none is — for a player
    /// reading the game as text (AgentLink).</summary>
    public string Speaker => lines != null && speakerText != null ? speakerText.text : null;
    public System.Collections.Generic.IReadOnlyList<string> Lines => lines;

    /// <summary>Open the box on <paramref name="newLines"/> (one per NEXT). <paramref name="closed"/> fires when the
    /// player pages past the end or hits CLOSE (or the caller calls <see cref="Close"/>).</summary>
    public static void Open(string speaker, string[] newLines, Action closed)
        => Open(speaker, newLines, closed, null, null);

    /// <summary>The same, with a second button on the last line — <paramref name="choice"/>, e.g. "Accept" — that runs
    /// <paramref name="chosen"/> and then closes the box as CLOSE would.</summary>
    public static void Open(string speaker, string[] newLines, Action closed, string choice, Action chosen)
    {
        // NOTHING TO SAY still ends the conversation: the caller has already frozen the player and hidden the HUD,
        // and only `closed` gives them back.
        if (newLines == null || newLines.Length == 0) { closed?.Invoke(); return; }
        if (Instance == null) BuildInstance();
        Instance.Begin(speaker, newLines, closed, choice, chosen);
    }

    static void BuildInstance()
    {
        var go = new GameObject("DialogBoxSystem") { hideFlags = HideFlags.DontSaveInEditor };
        Instance = go.AddComponent<DialogBox>();
        Instance.Construct();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Construct()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;   // above the game HUD (which is hidden during a chat anyway)
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        UiKit.ApplyTextCrispness(scaler);
        gameObject.AddComponent<GraphicRaycaster>();

        // Panel: a box with the window edge, bottom-centre.
        var border = UiKit.Round(UiKit.Img("Panel", transform, UiKit.WindowEdge), UiKit.WindowRadius);
        var pr = border.rectTransform;
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0f);
        pr.pivot = new Vector2(0.5f, 0f);
        pr.sizeDelta = new Vector2(760f, 150f);
        pr.anchoredPosition = new Vector2(0f, 28f);
        panel = border.gameObject;

        var fill = UiKit.Round(UiKit.Img("Fill", border.transform, UiKit.PanelFill), UiKit.WindowRadius - 1);
        UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        UiKit.ClickShield(pr);

        speakerText = UiKit.MakeText("Speaker", fill.transform, "", 17, FontStyle.Bold, TextAnchor.UpperLeft);
        speakerText.color = UiKit.TitleText; speakerText.raycastTarget = false;
        var sr = speakerText.rectTransform;
        sr.anchorMin = new Vector2(0, 1); sr.anchorMax = new Vector2(1, 1); sr.pivot = new Vector2(0, 1);
        sr.sizeDelta = new Vector2(-40f, 26f); sr.anchoredPosition = new Vector2(20f, -12f);

        bodyText = UiKit.MakeText("Body", fill.transform, "", 19, FontStyle.Normal, TextAnchor.UpperLeft, wrap: true);
        bodyText.color = UiKit.TextMain; bodyText.raycastTarget = false;
        UiKit.Fill(bodyText.rectTransform, new Vector2(20f, 46f), new Vector2(-20f, -44f));

        // NEXT / CLOSE button, bottom-right.
        var btn = UiKit.Btn("NEXT ▶", fill.transform, Advance);
        var brt = btn.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = new Vector2(1, 0); brt.pivot = new Vector2(1, 0);
        brt.sizeDelta = new Vector2(140f, 40f); brt.anchoredPosition = new Vector2(-14f, 12f);
        nextLabel = btn.GetComponentInChildren<TMP_Text>();

        // THE CHOICE, beside it — shown only on the last line of a conversation that offers one.
        var accept = UiKit.Btn("Accept", fill.transform, Choose);
        var art = accept.GetComponent<RectTransform>();
        art.anchorMin = art.anchorMax = new Vector2(1, 0); art.pivot = new Vector2(1, 0);
        art.sizeDelta = new Vector2(140f, 40f); art.anchoredPosition = new Vector2(-164f, 12f);
        acceptLabel = accept.GetComponentInChildren<TMP_Text>();
        acceptButton = accept.gameObject;
        acceptButton.SetActive(false);

        panel.SetActive(false);
    }

    void Begin(string speaker, string[] newLines, Action closed, string choice, Action chosen)
    {
        lines = newLines;
        index = 0;
        onClosed = closed;
        onAccept = chosen;
        if (acceptLabel != null) acceptLabel.text = string.IsNullOrEmpty(choice) ? "Accept" : choice;
        if (speakerText != null) speakerText.text = speaker;
        panel.SetActive(true);
        Refresh();
    }

    void Refresh()
    {
        if (bodyText != null) bodyText.text = lines[index];
        bool last = index >= lines.Length - 1;
        if (nextLabel != null) nextLabel.text = last ? "CLOSE" : "NEXT ▶";
        if (acceptButton != null) acceptButton.SetActive(last && onAccept != null);
    }

    /// <summary>Show <paramref name="line"/> now, in the conversation that is open, before whatever was still to come — a
    /// quest handed in, said while the player is still talking. Does nothing when no conversation is open.</summary>
    public static void Interject(string line)
    {
        var d = Instance;
        if (d == null || !d.IsOpen || d.lines == null || string.IsNullOrEmpty(line)) return;
        var rest = new string[d.lines.Length - d.index + 1];
        rest[0] = line;
        System.Array.Copy(d.lines, d.index, rest, 1, d.lines.Length - d.index);
        d.lines = rest;
        d.index = 0;
        d.Refresh();
    }

    void Choose()
    {
        var chosen = onAccept;
        onAccept = null;
        chosen?.Invoke();
        Close();
    }

    /// <summary>Advance to the next line, or close if this was the last (what the NEXT/CLOSE button + Right-Arrow do).</summary>
    public void Advance()
    {
        if (lines == null) return;
        index++;
        if (index >= lines.Length) { Close(); return; }
        Refresh();
    }

    /// <summary>Close the box and fire the caller's onClosed once. Safe to call when already closed.</summary>
    public void Close()
    {
        if (panel != null) panel.SetActive(false);
        lines = null;
        onAccept = null;
        var cb = onClosed;
        onClosed = null;
        cb?.Invoke();
    }

    void Update()
    {
        if (IsOpen && Hotkeys.Down(GameKeys.DialogNext)) Advance();
    }
}
