#if UNITY_ANDROID
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the on-screen controls: the move stick, the action buttons, and the tray that reaches the windows a
/// keyboard opens with a letter. Every button does exactly one thing — push a virtual key press into
/// <see cref="TouchControls"/> — so the game underneath is running the same code the desktop runs, and no window,
/// ability or hotbar script had to learn that a phone exists.
///
/// <para><b>Why its own canvas, sized in physical units:</b> the game's HUD scales against a reference screen,
/// which keeps a layout's proportions but says nothing about how big a thing ends up under a thumb. Controls are
/// the one part of a phone UI that must be a real-world size, so this canvas is measured in points (1/72 inch)
/// and a button is the same width of fingertip on every device.</para>
///
/// <para>The stick sits bottom-left and the actions ride the right edge at mid-height, deliberately leaving the
/// bottom-centre strip clear: that is where the hotbar lives, and its slots are already buttons, so they take
/// taps without any help from here.</para>
/// </summary>
public class TouchHud : MonoBehaviour
{
    const float Margin = 18f;      // points from the safe-area edge
    const float ActionSize = 44f;  // ≈0.6in — comfortably past the ~0.3in minimum for a fingertip
    const float StickSize = 110f;
    const float TrayWidth = 132f;
    const float TrayRowHeight = 26f;

    RectTransform safe;
    GameObject tray;
    Rect lastSafeArea;

    void Start()
    {
        var canvasGO = new GameObject("TouchHudCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
        { hideFlags = HideFlags.DontSaveInEditor };
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;   // above the game HUD, below nothing that matters on a phone

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPhysicalSize;
        scaler.physicalUnit = CanvasScaler.Unit.Points;
        scaler.fallbackScreenDPI = 400f;   // some devices and every emulator report no DPI at all

        safe = MakeChild("SafeArea", canvasGO.transform);
        ApplySafeArea();

        BuildStick();
        BuildActions();
        BuildTray();
    }

    void Update()
    {
        // The safe area moves when the device rotates or the system bars come and go.
        if (Screen.safeArea != lastSafeArea) ApplySafeArea();
    }

    // Keep every control inside the area the system is not covering with a notch, a camera hole or the gesture bar.
    void ApplySafeArea()
    {
        lastSafeArea = Screen.safeArea;
        if (Screen.width <= 0 || Screen.height <= 0) return;

        Rect r = lastSafeArea;
        safe.anchorMin = new Vector2(r.xMin / Screen.width, r.yMin / Screen.height);
        safe.anchorMax = new Vector2(r.xMax / Screen.width, r.yMax / Screen.height);
        safe.offsetMin = Vector2.zero;
        safe.offsetMax = Vector2.zero;
    }

    // ---- the controls ----------------------------------------------------------------------------------

    void BuildStick()
    {
        var ring = UiKit.Img("MoveStick", safe, new Color(1f, 1f, 1f, 0.16f));
        ring.sprite = Disc();
        var rt = ring.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.sizeDelta = new Vector2(StickSize, StickSize);
        rt.anchoredPosition = new Vector2(Margin, Margin);

        var knob = UiKit.Img("Knob", ring.transform, new Color(1f, 1f, 1f, 0.42f));
        knob.sprite = Disc();
        knob.raycastTarget = false;
        var krt = knob.rectTransform;
        krt.anchorMin = krt.anchorMax = krt.pivot = new Vector2(0.5f, 0.5f);
        krt.sizeDelta = new Vector2(StickSize * 0.42f, StickSize * 0.42f);
        krt.anchoredPosition = Vector2.zero;

        ring.gameObject.AddComponent<TouchStick>().knob = krt;
    }

    void BuildActions()
    {
        // Two columns up the right edge, centred on mid-height so the hotbar's strip along the bottom stays clear.
        float colA = -Margin, colB = -(Margin + ActionSize + 8f);
        Action("JUMP",   KeyCode.Space,     colA,  56f);
        Action("DASH",   KeyCode.E,         colA,   4f);
        Action("TALK",   KeyCode.F,         colA, -48f);
        Action("SPRINT", KeyCode.V,         colB,  56f);
        Action("AUTO",   KeyCode.T,         colB,   4f);
    }

    void Action(string label, KeyCode key, float x, float y)
    {
        var btn = UiKit.Btn(label, safe, () => TouchControls.PressVirtual(key));
        var rt = (RectTransform)btn.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(ActionSize, ActionSize);
        rt.anchoredPosition = new Vector2(x, y);
        Shrink(btn, 9f);
    }

    void BuildTray()
    {
        var toggle = UiKit.Btn("☰", safe, ToggleTray);   // the three-bar glyph, so no new word is coined
        var trt = (RectTransform)toggle.transform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(1f, 1f);
        trt.sizeDelta = new Vector2(ActionSize, ActionSize);
        trt.anchoredPosition = new Vector2(-Margin, -Margin);
        Shrink(toggle, 18f);

        var panel = UiKit.Img("WindowTray", safe, UiKit.PanelFill);
        tray = panel.gameObject;
        var prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 1f);
        prt.sizeDelta = new Vector2(TrayWidth, 0f);
        prt.anchoredPosition = new Vector2(-Margin, -(Margin + ActionSize + 8f));

        var layout = tray.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 3f;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        var fitter = tray.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Every one of these opens a window the desktop opens with a letter key, and each keeps the name that
        // window already carries in the game.
        TrayRow("Inventory", KeyCode.B);
        TrayRow("Character", KeyCode.C);
        TrayRow("Skills", KeyCode.K);
        TrayRow("Calendar", KeyCode.L);
        TrayRow("World Map", KeyCode.N);
        TrayRow("Minimap", KeyCode.M);
        TrayRow("Chat", KeyCode.Return);
        TrayRow("System", KeyCode.Escape);

        UiKit.ClickShield(prt);
        tray.SetActive(false);
    }

    void TrayRow(string label, KeyCode key)
    {
        var btn = UiKit.Btn(label, tray.transform, () => { TouchControls.PressVirtual(key); ToggleTray(); });
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = TrayRowHeight;
        Shrink(btn, 10f);
    }

    void ToggleTray() { if (tray != null) tray.SetActive(!tray.activeSelf); }

    // ---- small helpers ---------------------------------------------------------------------------------

    // The shared button factory sizes its label for a monitor; a control this small needs a smaller face.
    static void Shrink(Button btn, float fontSize)
    {
        var t = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = fontSize; t.enableWordWrapping = true; }
    }

    static RectTransform MakeChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Sprite disc;

    // A soft filled circle, drawn once into a small texture. uGUI ships no round sprite and the stick would be a
    // square without one; generating it here keeps the touch layer free of any art the project has to store.
    static Sprite Disc()
    {
        if (disc != null) return disc;

        const int Size = 128;
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSaveInEditor };
        var px = new Color32[Size * Size];
        float r = Size * 0.5f, edge = 1.5f;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                float a = Mathf.Clamp01((r - d) / edge);     // one-pixel feather so the rim isn't stair-stepped
                px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        disc = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
        return disc;
    }
}
#endif
