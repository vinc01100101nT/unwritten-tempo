using UnityEngine;
using TMPro;

/// <summary>
/// The ONE hover card that explains a buff/debuff — its name, its one-line "what it does", and its live remaining
/// time. Shared by every icon tray in the game: the player's own status bar (<see cref="BuffBarPanel"/>) and the
/// engaged target's readout (<see cref="UnitStatusPanel"/>). It was born inside the player's bar; extracting it is
/// what lets a target's icons explain themselves in exactly the same words, with no second copy to keep in sync.
///
/// <para>One instance for the whole game, created on first hover and reparented to whichever canvas is asking, so it
/// always draws above the tray that opened it. It ticks its own remaining-time line (rewritten only on a whole-second
/// change) and closes itself the moment the state it describes runs out.</para>
/// </summary>
public class StateInfoCard : MonoBehaviour
{
    /// <summary>Where the card sits relative to the hovered icon. The player's bar hugs the right edge of the screen
    /// so its card grows LEFT; a tray along the top has room underneath instead.</summary>
    public enum Side { Left, Below }


    static StateInfoCard inst;

    RectTransform root;
    TMP_Text body;
    TMP_Text time;
    Canvas host;
    UnitStates.State shown;
    int lastSecs = -1;
    readonly Vector3[] cornerBuf = new Vector3[4];

    /// <summary>Open the card for <paramref name="state"/>, seated against <paramref name="cell"/>.</summary>
    public static void Show(Canvas canvas, RectTransform cell, UnitStates.State state, Side side)
    {
        if (canvas == null || cell == null || state == null) return;
        if (inst == null)
        {
            var go = new GameObject("StateInfoCard") { hideFlags = HideFlags.DontSaveInEditor };
            inst = go.AddComponent<StateInfoCard>();
        }
        inst.Open(canvas, cell, state, side);
    }

    /// <summary>Close the card. Safe to call when nothing is open.</summary>
    public static void Hide()
    {
        if (inst != null) inst.Close();
    }

    /// <summary>Close the card ONLY if it is currently explaining <paramref name="state"/> — used when a tray drops
    /// that state, so one tray closing its own card can't steal another tray's.</summary>
    public static void HideIfShowing(UnitStates.State state)
    {
        if (inst != null && inst.shown == state) inst.Close();
    }

    void Open(Canvas canvas, RectTransform cell, UnitStates.State state, Side side)
    {
        Build(canvas);
        if (root == null) return;

        shown = state;
        body.text = "<b>" + (string.IsNullOrEmpty(state.name) ? state.id.ToString() : state.name) + "</b>"
                  + (string.IsNullOrEmpty(state.description) ? "" : "\n" + state.description);
        lastSecs = -1;                    // force the time line to write this frame
        root.gameObject.SetActive(true);
        UiLayers.Raise(root, UiLayers.Layer.Tooltip);
        Position(cell, side);
        Tick();
    }

    void Close()
    {
        shown = null;
        if (root != null) root.gameObject.SetActive(false);
    }

    void Update()
    {
        if (shown == null) return;
        if (shown.Remaining <= 0f) { Close(); return; }   // the state it explains is gone
        Tick();
    }

    // Rewritten only on a whole-second change — a per-frame string here would allocate during combat.
    void Tick()
    {
        int secs = Mathf.CeilToInt(shown.Remaining);
        if (secs == lastSecs) return;
        time.text = "Time left: " + secs + "s";
        lastSecs = secs;
    }

    void Build(Canvas canvas)
    {
        if (root == null)
        {
            var bg = UiKit.Card(UiKit.Img("StateInfoCard", canvas.transform, UiKit.PanelFill));
            root = bg.rectTransform;
            root.sizeDelta = new Vector2(230f, 60f);   // width fixed; height driven by the fitter below
            bg.raycastTarget = false;

            var vlg = bg.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 8, 8);
            vlg.spacing = 3;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var fit = bg.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;

            body = UiKit.MakeText("Body", root, "", 12, FontStyle.Normal, TextAnchor.UpperLeft);
            body.richText = true; body.raycastTarget = false;
            time = UiKit.MakeText("Time", root, "", 11, FontStyle.Bold, TextAnchor.UpperLeft);
            time.color = UiKit.TextDim; time.raycastTarget = false;

            root.gameObject.SetActive(false);
        }

        if (host != canvas)   // follow whichever tray opened it, so it always draws above that tray
        {
            host = canvas;
            root.SetParent(canvas.transform, false);
        }
    }

    // Seat the card against the hovered icon: to its LEFT (card's right edge on the icon) or BELOW it (card's top
    // edge under the icon), in the host canvas's own coordinates.
    void Position(RectTransform cell, Side side)
    {
        var canvasRT = (RectTransform)host.transform;
        Camera cam = host.renderMode != RenderMode.ScreenSpaceOverlay ? host.worldCamera : null;
        cell.GetWorldCorners(cornerBuf);   // [0]=bottom-left, [1]=top-left, [2]=top-right, [3]=bottom-right

        Vector3 anchorPoint;
        Vector2 nudge;
        if (side == Side.Left)
        {
            anchorPoint = (cornerBuf[0] + cornerBuf[1]) * 0.5f;   // middle of the icon's left edge
            root.pivot  = new Vector2(1f, 0.5f);
            nudge       = new Vector2(-8f, 0f);
        }
        else
        {
            anchorPoint = (cornerBuf[0] + cornerBuf[3]) * 0.5f;   // middle of the icon's bottom edge
            root.pivot  = new Vector2(0.5f, 1f);
            nudge       = new Vector2(0f, -8f);
        }

        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, anchorPoint);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screen, cam, out var local))
            root.anchoredPosition = local + nudge;
    }
}
