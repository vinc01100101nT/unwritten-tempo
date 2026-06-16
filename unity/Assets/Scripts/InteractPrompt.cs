using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single floating marker (e.g. "▽ Talk · E") that the <see cref="PlayerInteractor"/>
/// parks above whichever <see cref="Interactable"/> is currently nearest, and hides
/// otherwise. Reusable for any interactable — the verb comes from Interactable.prompt.
///
/// Lives on a screen-space-overlay canvas; each frame it projects the target's
/// world position to a screen point and moves the marker there.
/// </summary>
public class InteractPrompt : MonoBehaviour
{
    public static InteractPrompt Instance { get; private set; }

    [Tooltip("The marker's RectTransform — this is what gets moved + shown/hidden.")]
    public RectTransform root;
    public Text label;

    [Tooltip("How far above the target (world units) the marker floats.")]
    public float worldYOffset = 0.7f;

    Camera cam;

    void Awake()
    {
        Instance = this;
        Hide();
    }

    public void ShowAbove(Transform target, string text)
    {
        if (root == null || target == null) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        if (label != null) label.text = text;

        Vector3 screen = cam.WorldToScreenPoint(target.position + Vector3.up * worldYOffset);
        screen.z = 0f;                       // overlay canvas ignores Z
        root.position = screen;

        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
    }
}
