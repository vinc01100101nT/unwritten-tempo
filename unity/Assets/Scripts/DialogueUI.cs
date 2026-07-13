using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Minimal dialogue box. One instance lives on the DialogueCanvas (built by
/// Tools ▸ unwritten ▸ Build Dialogue UI). Code calls
/// <c>DialogueUI.Instance.Begin(speaker, lines)</c> and steps lines with <see cref="Advance"/>.
/// DORMANT (kept for reuse): the 2D-era finder that drove it (PlayerInteractor/Interactable)
/// was retired; a future 3D press-to-talk system should rediscover NPCs via a registry scan
/// (see TownNPC) and call Begin/Advance here — the box itself is world-agnostic.
/// </summary>
public class DialogueUI : MonoBehaviour
{
    public static DialogueUI Instance { get; private set; }

    [Tooltip("The dialogue box root — hidden when no conversation is active.")]
    public GameObject panel;
    public TMP_Text speakerText;
    public TMP_Text bodyText;

    string[] lines;
    int index;

    public bool IsOpen => panel != null && UiAnim.IsOpen(panel);

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
        if (panel != null) UiAnim.Open(panel);
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
        if (panel != null) UiAnim.Close(panel);
        lines = null;
    }
}
