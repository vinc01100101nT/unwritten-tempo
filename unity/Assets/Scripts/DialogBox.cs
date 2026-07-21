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
    TMP_Text speakerText, bodyText, nextLabel;

    string[] lines;
    int index;
    Action onClosed;

    public bool IsOpen => panel != null && panel.activeSelf;

    /// <summary>Open the box on <paramref name="newLines"/> (one per NEXT). <paramref name="closed"/> fires when the
    /// player pages past the end or hits CLOSE (or the caller calls <see cref="Close"/>).</summary>
    public static void Open(string speaker, string[] newLines, Action closed)
    {
        if (newLines == null || newLines.Length == 0) return;
        if (Instance == null) BuildInstance();
        Instance.Begin(speaker, newLines, closed);
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

        // Panel: gold-bordered dark box, bottom-centre.
        var border = UiKit.Img("Panel", transform, UiKit.Gold);
        var pr = border.rectTransform;
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0f);
        pr.pivot = new Vector2(0.5f, 0f);
        pr.sizeDelta = new Vector2(760f, 150f);
        pr.anchoredPosition = new Vector2(0f, 28f);
        panel = border.gameObject;

        var fill = UiKit.Img("Fill", border.transform, UiKit.PanelFill);
        UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        UiKit.ClickShield(pr);

        speakerText = UiKit.MakeText("Speaker", fill.transform, "", 17, FontStyle.Bold, TextAnchor.UpperLeft, UiKit.TitleFont);
        speakerText.color = UiKit.Gold; speakerText.raycastTarget = false;
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

        panel.SetActive(false);
    }

    void Begin(string speaker, string[] newLines, Action closed)
    {
        lines = newLines;
        index = 0;
        onClosed = closed;
        if (speakerText != null) speakerText.text = speaker;
        panel.SetActive(true);
        Refresh();
    }

    void Refresh()
    {
        if (bodyText != null) bodyText.text = lines[index];
        bool last = index >= lines.Length - 1;
        if (nextLabel != null) nextLabel.text = last ? "CLOSE" : "NEXT ▶";
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
        var cb = onClosed;
        onClosed = null;
        cb?.Invoke();
    }

    void Update()
    {
        if (IsOpen && Hotkeys.Down(KeyCode.RightArrow)) Advance();
    }
}
