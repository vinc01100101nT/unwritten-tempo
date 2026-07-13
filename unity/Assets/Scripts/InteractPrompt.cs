using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A single floating marker (e.g. "▽ Talk · E") parked above a world target via
/// <see cref="ShowAbove"/>, hidden with <see cref="Hide"/>. DORMANT (kept for reuse):
/// its 2D-era driver (PlayerInteractor/Interactable) was retired; a future 3D
/// press-to-talk system supplies the nearest-NPC transform and the verb text.
///
/// Lives on a screen-space-overlay canvas; each frame it projects the target's
/// world position to a screen point and moves the marker there.
/// </summary>
public class InteractPrompt : MonoBehaviour
{
    public static InteractPrompt Instance { get; private set; }

    [Tooltip("The marker's RectTransform — this is what gets moved + shown/hidden.")]
    public RectTransform root;
    public TMP_Text label;

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
