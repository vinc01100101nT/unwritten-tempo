using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The curtain over every move between maps and into the world. It goes up the instant the player commits — a
/// portal touched, "Enter World", "Return to last saved point" — or the instant the server starts a move nobody
/// pressed for (a wing, a Keeper's trip, a GM's teleport), and comes down when they stand in the new map.
///
/// <para><b>A bar, filled by what has genuinely happened.</b> Most of a move happens on machines this one cannot
/// see — a character being written down, a zone being placed — and none of it can report how far along it is. So
/// the bar jumps to a fixed point as each <see cref="Stage"/> is reached, and only the last stretch, this machine
/// loading the map, moves with a real number. It never goes backwards.</para>
///
/// <para>It borrows the sign-in screen's own look rather than copying it, so the two always match.</para>
/// </summary>
public static class LoadingScreen
{
    /// <summary>The steps of a move this machine can see happen, in order.</summary>
    public enum Stage
    {
        /// <summary>The move was asked for — the press, or the server saying it has started one.</summary>
        Asked,
        /// <summary>The character is written down and the server is finding the map.</summary>
        Saved,
        /// <summary>This machine is joined to the world the map is in; only loading the map is left.</summary>
        Connected,
    }

    // How full the bar is once each stage is reached. Loading the map fills the rest with its real amount.
    const float AskedFill = 0.2f;
    const float SavedFill = 0.4f;
    const float ConnectedFill = 0.6f;

    static RectTransform root;
    static RectTransform barFill;
    static TMPro.TMP_Text percentLabel;
    // Where the bar is heading, and where it is drawn on its way there.
    static float target;
    static float shown;
    static int shownPercent = -1;

    // How quickly the drawn bar closes the gap to where it is heading: most of the way in a quarter of a second.
    const float BarCatchUpPerSecond = 12f;
    static CanvasGroup group;
    static LoadingScreenTicker ticker;
    // a new random picture each time the curtain goes up
    static FrontBackdrop backdrop;
    static bool fading;

    // The map the player was standing in when the curtain went up. Lowering it in a DIFFERENT map fades the new one
    // in; lowering it in the same map — a door within one map, or a move that was refused — just takes it away.
    static string shownInMap;

    /// <summary>How long the new map takes to fade in from behind the curtain, in seconds.</summary>
    public const float FadeInSeconds = 0.3f;

    /// <summary>True while the curtain is up. False once it has started fading away: the new map is already being
    /// shown and played in by then.</summary>
    public static bool Showing => root != null && root.gameObject.activeSelf && !fading;

    /// <summary>Raise the curtain, at <see cref="Stage.Asked"/>. Already up, it stays as it is, so a press and the
    /// server's word about the same move cannot pull the bar back. Still fading from the last move, it comes
    /// straight back up, whole.</summary>
    public static void Show()
    {
        if (Showing) return;
        Build();
        if (root == null) return;
        fading = false;
        group.alpha = 1f;
        group.blocksRaycasts = true;
        shownInMap = MapManager.CurrentMap;
        target = 0f;
        Draw(0f);
        if (backdrop != null) backdrop.Restart();
        root.gameObject.SetActive(true);
        UiLayers.Raise(root, UiLayers.Layer.Loading);
        ticker.enabled = true;
        Reached(Stage.Asked);
    }

    /// <summary>Fill the bar to where <paramref name="stage"/> puts it. Ignored when the curtain is down, so a late
    /// answer to a move that already finished cannot raise anything.</summary>
    public static void Reached(Stage stage)
        => Fill(stage == Stage.Asked ? AskedFill : stage == Stage.Saved ? SavedFill : ConnectedFill);

    // Only ever forwards. The drawn bar slides there over the next frames (see Tick).
    static void Fill(float amount)
    {
        if (!Showing || barFill == null) return;
        amount = Mathf.Min(1f, amount);
        if (amount > target) target = amount;
    }

    // The fill is the bar's own width cut short, so it needs no picture behind it. The number under it is rewritten
    // only when the whole percent changes — at most a hundred times a move.
    static void Draw(float amount)
    {
        shown = amount;
        barFill.anchorMax = new Vector2(amount, 1f);
        int percent = Mathf.RoundToInt(amount * 100f); // server-math: this machine's own map-load amount, never a game number
        if (percent == shownPercent) return;
        shownPercent = percent;
        percentLabel.text = percent + "%";
    }

    /// <summary>Lower the curtain. Called when the player has a body on the far side, or when the move failed
    /// and they are standing where they started. Landing in a different map fades that map in over
    /// <see cref="FadeInSeconds"/>; anything else takes the curtain away at once.</summary>
    public static void Hide()
    {
        // NOT WHILE THE GROUND IS STILL COMING IN. A seamless world holds the player back until the ground where they
        // land has loaded, and lowers the curtain itself the moment it has (SeamlessWorld.PlaceWhenReady); lowered
        // earlier — by the map arriving, or a trip within the map — it would show them standing over nothing.
        if (SeamlessWorld.PlacementPending) return;
        // A TELEPORT'S CURTAIN STANDS ON BLACK (TeleportLook.CoverWithCurtain): it goes at once and the black fades
        // away, the same fade a wing's trip ends with. Also when the curtain never got up — the answer came back
        // before the screen was black — so the black waiting to raise it is called off.
        if (TeleportLook.OwnWaitingToAppear)
        {
            if (root != null && root.gameObject.activeSelf)
            {
                fading = false;
                ticker.enabled = false;
                root.gameObject.SetActive(false);
            }
            ScreenFade.FromBlack();
            return;
        }
        if (root == null || !root.gameObject.activeSelf) return;
        bool landedElsewhere = !fading && !string.IsNullOrEmpty(MapManager.CurrentMap)
                               && MapManager.CurrentMap != shownInMap;
        if (landedElsewhere)
        {
            target = 1f;
            // Clicks reach the new map from the first frame of the fade, not the last.
            group.blocksRaycasts = false;
            fading = true;
            return;
        }
        fading = false;
        ticker.enabled = false;
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

    // One frame, driven by LoadingScreenTicker while the curtain is up or fading: the map load's real amount while
    // it is up, the fade while it is going. Done fading: the curtain is put away.
    internal static void Tick(float seconds)
    {
        if (group == null) return;
        if (shown < target)
        {
            float next = shown + (target - shown) * (1f - Mathf.Exp(-BarCatchUpPerSecond * seconds));
            Draw(target - next < 0.002f ? target : next);
        }
        if (!fading)
        {
            if (MapManager.ArrivingAt != null) Fill(ConnectedFill + (1f - ConnectedFill) * MapManager.LoadProgress01);
            return;
        }
        group.alpha = Mathf.Max(0f, group.alpha - seconds / FadeInSeconds);
        if (group.alpha > 0f) return;
        fading = false;
        ticker.enabled = false;
        root.gameObject.SetActive(false);
    }

    static void Build()
    {
        if (root != null) return;

        // THE SIGN-IN SCREEN'S OWN LOOK, borrowed rather than copied — the same opaque ground and the same
        // faint emblem behind it, so a doorway and a sign-in cannot drift into two different-looking screens.
        root = FrontScreen.Canvas(null, slideshow: false);
        if (root == null) return;
        backdrop = root.GetComponentInChildren<FrontBackdrop>(true);
        Object.DontDestroyOnLoad(root.GetComponentInParent<Canvas>().gameObject);

        // In the middle of the screen, a little below centre, inside a white border two units wide.
        var borderGO = new GameObject("Border", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var borderRT = (RectTransform)borderGO.transform;
        borderRT.SetParent(root, false);
        borderRT.anchorMin = borderRT.anchorMax = new Vector2(0.5f, 0.5f);
        borderRT.pivot = new Vector2(0.5f, 0.5f);
        borderRT.sizeDelta = new Vector2(424f, 12f);
        borderRT.anchoredPosition = new Vector2(0f, -60f);
        var border = borderGO.GetComponent<Image>();
        border.color = Color.white;
        border.raycastTarget = false;

        var stripGO = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var stripRT = (RectTransform)stripGO.transform;
        stripRT.SetParent(borderRT, false);
        stripRT.anchorMin = stripRT.anchorMax = new Vector2(0.5f, 0.5f);
        stripRT.pivot = new Vector2(0.5f, 0.5f);
        stripRT.sizeDelta = new Vector2(420f, 8f);
        stripRT.anchoredPosition = Vector2.zero;
        var strip = stripGO.GetComponent<Image>();
        UiKit.Paint(strip, UiKit.Rule);
        strip.raycastTarget = false;

        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var fillRT = (RectTransform)fillGO.transform;
        fillRT.SetParent(stripRT, false);
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = new Vector2(0f, 1f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        var fill = fillGO.GetComponent<Image>();
        UiKit.Paint(fill, UiKit.Accent);
        fill.raycastTarget = false;
        barFill = fillRT;

        percentLabel = UiKit.MakeText("Percent", root, "0%", 18, FontStyle.Normal, TextAnchor.MiddleCenter, wrap: false);
        var percentRT = percentLabel.rectTransform;
        percentRT.anchorMin = percentRT.anchorMax = new Vector2(0.5f, 0.5f);
        percentRT.pivot = new Vector2(0.5f, 0.5f);
        percentRT.sizeDelta = new Vector2(120f, 28f);
        percentRT.anchoredPosition = new Vector2(0f, -86f);
        UiKit.Paint(percentLabel, UiKit.TextOnDim);
        percentLabel.raycastTarget = false;

        group = root.gameObject.AddComponent<CanvasGroup>();
        ticker = root.gameObject.AddComponent<LoadingScreenTicker>();
        ticker.enabled = false;

        root.gameObject.SetActive(false);
    }
}

/// <summary>Ticks <see cref="LoadingScreen"/> while the curtain is up or fading — switched off the rest of the
/// time, so it costs nothing between moves.</summary>
public sealed class LoadingScreenTicker : MonoBehaviour
{
    // Unscaled: a fade is screen time, not game time.
    void Update() => LoadingScreen.Tick(Time.unscaledDeltaTime);
}
