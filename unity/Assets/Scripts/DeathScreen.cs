using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Phase F death → respawn flow. Lives on the persistent <c>Systems</c> layer (never unloaded) and
/// listens to <see cref="Character.OnDied"/>. When the player hits 0 HP it fades in a full-screen
/// "YOU DIED" overlay, counts down, then offers a Respawn button. Respawn calls
/// <see cref="Character.Revive"/> (back to full HP) and teleports the avatar to the current map's
/// SpawnPoint via <see cref="PersistentPlayer3D.Respawn"/>.
///
/// Self-contained: it builds its own ScreenSpaceOverlay canvas in Awake (drawn above the HUD), so no
/// theme assets or prefab wiring are needed — just attach the component to a Systems GameObject.
/// </summary>
public class DeathScreen : MonoBehaviour
{
    [Tooltip("Seconds the player must wait before the Respawn button unlocks.")]
    public float respawnDelay = 3f;
    [Tooltip("Beat before the overlay fades in, so the player's death pose reads first.")]
    public float preFadeDelay = 0.5f;
    [Tooltip("Overlay fade-in time, in seconds.")]
    public float fadeTime = 0.8f;

    CanvasGroup group;     // drives the fade + blocks clicks while shown
    TMP_Text countdown;
    Button respawnButton;
    TMP_Text respawnLabel;
    Character character;
    bool building;

    void Awake() => Build();

    void Start()
    {
        character = Character.Instance;
        if (character != null) character.OnDied += Show;
        Hide();
    }

    void OnDestroy() { if (character != null) character.OnDied -= Show; }

    // ---- flow -----------------------------------------------------------------

    void Show()
    {
        if (building) return;            // already dead / showing
        StopAllCoroutines();
        StartCoroutine(ShowRoutine());
    }
    // Built once for this screen, not once per death — see MobRespawn.
    WaitForSeconds preFadeWait;
    float preFadeWaitFor = -1f;


    IEnumerator ShowRoutine()
    {
        building = true;
        group.gameObject.SetActive(true);
        group.blocksRaycasts = true;
        group.alpha = 0f;
        SetButtonEnabled(false);

        // Let the player's death pose land before the screen takes over.
        if (preFadeDelay > 0f)
        {
            if (preFadeWait == null || preFadeWaitFor != preFadeDelay)
            {
                preFadeWait = new WaitForSeconds(preFadeDelay);
                preFadeWaitFor = preFadeDelay;
            }
            yield return preFadeWait;
        }

        // Fade the overlay in.
        for (float t = 0f; t < fadeTime; t += Time.deltaTime)
        {
            group.alpha = Mathf.Clamp01(t / fadeTime);
            yield return null;
        }
        group.alpha = 1f;

        // Countdown, then unlock the button.
        float remaining = respawnDelay;
        int shown = -1;
        while (remaining > 0f)
        {
            // Only when the number on screen actually changes — a rewrite every frame both allocates a
            // string and forces the canvas to rebuild, for a digit that moves once a second.
            int secs = Mathf.CeilToInt(remaining);
            if (secs != shown) { countdown.text = $"Respawn in {secs}…"; shown = secs; }
            remaining -= Time.deltaTime;
            yield return null;
        }
        countdown.text = "";
        SetButtonEnabled(true);
        building = false;
    }

    void OnRespawnClicked()
    {
        if (building) return;            // still counting down
        UISounds.PlayClick();            // button click (this canvas is separate, so play it directly)
        SetButtonEnabled(false);
        StopAllCoroutines();
        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        if (Character.Instance != null) Character.Instance.Revive();

        // Put the avatar back at the map's SpawnPoint.
        if (PersistentPlayer3D.Instance != null) PersistentPlayer3D.Instance.Respawn();

        // Fade the overlay back out.
        for (float t = group.alpha; t > 0f; t -= Time.deltaTime / Mathf.Max(fadeTime, 0.01f))
        {
            group.alpha = t;
            yield return null;
        }
        Hide();
    }

    void Hide()
    {
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.gameObject.SetActive(false);
        building = false;
    }

    void SetButtonEnabled(bool on)
    {
        respawnButton.interactable = on;
        respawnLabel.text = on ? "Respawn" : "…";
        var c = respawnLabel.color; c.a = on ? 1f : 0.4f; respawnLabel.color = c;
    }

    // ---- UI construction (runtime, no assets) ---------------------------------

    void Build()
    {
        var canvasGO = new GameObject("DeathScreenCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup)) { hideFlags = HideFlags.DontSaveInEditor };
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;                       // above the HUD / panels

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        UiKit.ApplyTextCrispness(scaler);

        group = canvasGO.GetComponent<CanvasGroup>();

        // Full-screen vignette: near-black with a red bleed.
        var overlay = NewImage("Overlay", canvasGO.transform, new Color(0.06f, 0.01f, 0.02f, 0.92f));
        Stretch(overlay.rectTransform);

        var title = NewText("Title", canvasGO.transform, "YOU DIED", 84, FontStyle.Bold,
                            new Color(0.78f, 0.12f, 0.14f));
        Place(title.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(900, 120));

        var subtitle = NewText("Subtitle", canvasGO.transform, "You were slain in the wilds.",
                            26, FontStyle.Normal, new Color(0.85f, 0.82f, 0.80f));
        Place(subtitle.rectTransform, new Vector2(0.5f, 0.52f), new Vector2(900, 44));

        countdown = NewText("Countdown", canvasGO.transform, "", 24, FontStyle.Italic,
                            new Color(0.75f, 0.72f, 0.70f));
        Place(countdown.rectTransform, new Vector2(0.5f, 0.40f), new Vector2(600, 40));

        // Respawn button.
        var btnImg = NewImage("RespawnButton", canvasGO.transform, new Color(0.55f, 0.12f, 0.14f, 1f));
        Place(btnImg.rectTransform, new Vector2(0.5f, 0.30f), new Vector2(240, 60));
        respawnButton = btnImg.gameObject.AddComponent<Button>();
        respawnButton.targetGraphic = btnImg;
        var colors = respawnButton.colors;
        colors.highlightedColor = new Color(0.72f, 0.18f, 0.20f);
        colors.pressedColor = new Color(0.40f, 0.08f, 0.10f);
        colors.disabledColor = new Color(0.30f, 0.10f, 0.11f);
        respawnButton.colors = colors;
        respawnButton.onClick.AddListener(OnRespawnClicked);

        respawnLabel = NewText("Label", btnImg.transform, "Respawn", 26, FontStyle.Bold, Color.white);
        Stretch(respawnLabel.rectTransform);
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static TMP_Text NewText(string name, Transform parent, string content, int size, FontStyle style, Color color)
    {
        var t = UiKit.MakeText(name, parent, content, size, style, TextAnchor.MiddleCenter, wrap: false);
        t.color = color;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
    }
}
