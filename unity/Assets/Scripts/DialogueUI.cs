using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimal dialogue box. One instance lives on the DialogueCanvas (built by
/// Tools ▸ unwritten ▸ Build Dialogue UI). Code calls
/// <c>DialogueUI.Instance.Begin(speaker, lines)</c>; the player's
/// <see cref="PlayerInteractor"/> calls <see cref="Advance"/> to step through the
/// lines and close at the end.
/// </summary>
public class DialogueUI : MonoBehaviour
{
    public static DialogueUI Instance { get; private set; }

    [Tooltip("The dialogue box root — hidden when no conversation is active.")]
    public GameObject panel;
    public Text speakerText;
    public Text bodyText;

    string[] lines;
    int index;

    public bool IsOpen => panel != null && panel.activeSelf;

    void Awake()
    {
        Instance = this;
        if (panel != null) panel.SetActive(false);
    }

    public void Begin(string speaker, string[] newLines)
    {
        if (newLines == null || newLines.Length == 0) return;
        lines = newLines;
        index = 0;
        if (speakerText != null) speakerText.text = speaker;
        if (panel != null) panel.SetActive(true);
        ShowCurrent();
    }

    public void Advance()
    {
        index++;
        if (lines == null || index >= lines.Length) { Close(); return; }
        ShowCurrent();
    }

    void ShowCurrent()
    {
        if (bodyText != null) bodyText.text = lines[index];
    }

    void Close()
    {
        if (panel != null) panel.SetActive(false);
        lines = null;
    }
}
