using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The curtain over a doorway that leads to another server. It goes up the instant a player touches the portal
/// and comes down when they have a body on the far side.
///
/// <para><b>Named steps, not a percentage.</b> Most of what happens between the two doors is on machines this
/// one cannot see — a character being written down, a zone being placed, a map being brought up — and none of
/// it can report how far along it is. A bar over that would crawl to nine tenths and then sit still, which is
/// worse than no bar. So the step is NAMED and changes when something has genuinely happened, and the one
/// stretch that has a real number — this machine loading the map — gets a real bar.</para>
///
/// <para>It borrows the sign-in screen's own look rather than copying it, so the two always match.</para>
/// </summary>
public static class LoadingScreen
{
    static RectTransform root;
    static TMPro.TMP_Text stepLabel;
    static Image barFill;
    static string step = "";
    static CanvasGroup group;
    static LoadingScreenFade fade;

    // The map the player was standing in when the curtain went up. Lowering it in a DIFFERENT map fades the new one
    // in; lowering it in the same map — a door within one map, or a move that was refused — just takes it away.
    static string shownInMap;

    /// <summary>How long the new map takes to fade in from behind the curtain, in seconds.</summary>
    public const float FadeInSeconds = 0.3f;

    /// <summary>True while the curtain is up. False once it has started fading away: the new map is already being
    /// shown and played in by then.</summary>
    public static bool Showing => root != null && root.gameObject.activeSelf && (fade == null || !fade.enabled);

    /// <summary>Raise the curtain and say what is happening. Called the moment a portal is touched, before any
    /// asking begins — the player has already committed by walking into it, and the world they are leaving must
    /// stop being something they can act on.</summary>
    public static void Show(string firstStep)
    {
        Build();
        if (root == null) return;
        // A curtain still fading from the last door comes straight back up, whole.
        if (fade != null) fade.enabled = false;
        group.alpha = 1f;
        group.blocksRaycasts = true;
        shownInMap = MapManager.CurrentMap;
        root.gameObject.SetActive(true);
        UiLayers.Raise(root, UiLayers.Layer.Loading);
        SetStep(firstStep);
        SetBar(-1f);
    }

    /// <summary>Say what is happening now. Ignored when the curtain is down, so a late answer to a move that
    /// already finished cannot put words on a screen nobody is looking at.</summary>
    public static void SetStep(string what)
    {
        if (!Showing || stepLabel == null || what == step) return;
        step = what;
        stepLabel.text = what;
    }

    /// <summary>How far the map load has got, 0 to 1. Below zero hides the bar — for the stretches that have no
    /// number, where drawing an empty bar would be a promise this cannot keep.</summary>
    public static void SetBar(float amount01)
    {
        if (!Showing || barFill == null) return;
        var strip = barFill.transform.parent as RectTransform;
        bool has = amount01 >= 0f;
        if (strip != null && strip.gameObject.activeSelf != has) strip.gameObject.SetActive(has);
        if (has) barFill.fillAmount = Mathf.Clamp01(amount01);
    }

    /// <summary>Lower the curtain. Called when the player has a body on the far side, or when the move failed
    /// and they are standing where they started. Landing in a different map fades that map in over
    /// <see cref="FadeInSeconds"/>; anything else takes the curtain away at once.</summary>
    public static void Hide()
    {
        step = "";
        if (root == null) return;
        bool landedElsewhere = Showing && !string.IsNullOrEmpty(MapManager.CurrentMap)
                               && MapManager.CurrentMap != shownInMap;
        if (landedElsewhere && fade != null)
        {
            // Clicks reach the new map from the first frame of the fade, not the last.
            group.blocksRaycasts = false;
            fade.enabled = true;
            return;
        }
        if (fade != null) fade.enabled = false;
        root.gameObject.SetActive(false);
    }

    /// <summary>Lower the curtain the moment the next map has loaded, with the player already standing in it.
    /// Signed up for ONE arrival and taken straight off again, so a later ordinary door cannot lower a curtain
    /// that is not up.</summary>
    public static void HideWhenMapArrives()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnMapArrived;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnMapArrived;
    }

    // Signed up after the map's own housekeeping and the player's placement, which both sign up at start-up — so by
    // now the map is the current one and the player and camera already stand in it.
    static void OnMapArrived(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (scene.name == MapManager.SystemsScene) return;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnMapArrived;
        Hide();
    }

    // One frame of the fade, driven by LoadingScreenFade while it runs. Done: the curtain is put away.
    internal static void StepFade(float seconds)
    {
        if (group == null) return;
        group.alpha = Mathf.Max(0f, group.alpha - seconds / FadeInSeconds);
        if (group.alpha > 0f) return;
        fade.enabled = false;
        root.gameObject.SetActive(false);
    }

    static void Build()
    {
        if (root != null) return;

        // THE SIGN-IN SCREEN'S OWN LOOK, borrowed rather than copied — the same opaque ground and the same
        // faint emblem behind it, so a doorway and a sign-in cannot drift into two different-looking screens.
        root = FrontScreen.Canvas(null);
        if (root == null) return;
        Object.DontDestroyOnLoad(root.GetComponentInParent<Canvas>().gameObject);

        stepLabel = UiKit.MakeText("Step", root, "", 18, FontStyle.Normal, TextAnchor.MiddleCenter, wrap: false);
        FrontScreen.Place(stepLabel.rectTransform, new Vector2(0f, -40f), new Vector2(520f, 34f));
        stepLabel.color = UiKit.TextOnDim;
        stepLabel.raycastTarget = false;

        var stripGO = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var stripRT = (RectTransform)stripGO.transform;
        stripRT.SetParent(root, false);
        FrontScreen.Place(stripRT, new Vector2(0f, -84f), new Vector2(320f, 6f));
        var strip = stripGO.GetComponent<Image>();
        strip.color = UiKit.Rule;
        strip.raycastTarget = false;

        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var fillRT = (RectTransform)fillGO.transform;
        fillRT.SetParent(stripRT, false);
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        barFill = fillGO.GetComponent<Image>();
        barFill.color = UiKit.Accent;
        barFill.type = Image.Type.Filled;
        barFill.fillMethod = Image.FillMethod.Horizontal;
        barFill.fillAmount = 0f;
        barFill.raycastTarget = false;

        group = root.gameObject.AddComponent<CanvasGroup>();
        fade = root.gameObject.AddComponent<LoadingScreenFade>();
        fade.enabled = false;

        root.gameObject.SetActive(false);
    }
}

/// <summary>Ticks <see cref="LoadingScreen"/>'s fade-in, and only while one is running — switched off the rest of
/// the time, so it costs nothing between doors.</summary>
public sealed class LoadingScreenFade : MonoBehaviour
{
    // Unscaled: a fade is screen time, not game time.
    void Update() => LoadingScreen.StepFade(Time.unscaledDeltaTime);
}
