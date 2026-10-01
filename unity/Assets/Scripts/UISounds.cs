using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-stop UI click audio for the game's UI shell. Lives on the persistent <c>GameUICanvas</c>
/// (built by <c>UIShellBuilder</c>, which assigns the clips). It plays a soft latch "open" when a
/// panel opens (Bag/Character/Skills) and a "close" when it hides, and auto-wires every
/// <see cref="Button"/> under the canvas to a generic "click". Other persistent UI that builds its
/// own canvas in code reaches the click via the static helpers.
///
/// All playback goes through the pooled <see cref="Sfx"/> player, so this adds no AudioSources of its
/// own and is safe to call before/after scene loads (the statics no-op until an instance exists).
/// </summary>
public class UISounds : MonoBehaviour
{
    public static UISounds Instance { get; private set; }

    [Tooltip("Played when a panel opens.")]
    public SfxSet open;
    [Tooltip("Played when a panel closes (or Esc closes everything).")]
    public SfxSet close;
    [Tooltip("Generic button click (auto-wired to every Button under this canvas).")]
    public SfxSet click;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() => HookButtons();   // panels exist by now (even inactive ones are found)

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Wire every Button under this canvas (incl. those on hidden panels) to the click sound.</summary>
    void HookButtons()
    {
        foreach (var b in GetComponentsInChildren<Button>(true))
            b.onClick.AddListener(PlayClick);
    }

    public static void PlayOpen()  { if (Instance != null) Sfx.Play(Instance.open); }
    public static void PlayClose() { if (Instance != null) Sfx.Play(Instance.close); }
    public static void PlayClick() { if (Instance != null) Sfx.Play(Instance.click); }
}
