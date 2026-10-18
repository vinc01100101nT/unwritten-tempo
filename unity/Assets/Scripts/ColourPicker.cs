using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// THE colour picker: the chosen colour on the left, a shade/brightness square beside it, a rainbow strip under
/// them for the colour itself, and a HEX box with a copy button. Built into any parent by <see cref="Build"/>;
/// raises <see cref="OnChanged"/> as the player picks.
///
/// Perf: the square's picture is redrawn only when the colour strip moves (into one reused pixel buffer); the
/// rainbow is drawn once. Nothing runs per frame.
/// </summary>
public sealed class ColourPicker
{
    public event Action<Color> OnChanged;

    /// <summary>The colour on show.</summary>
    public Color Value => Color.HSVToRGB(h, s, v);

    public RectTransform Root { get; private set; }

    const int SquareRes = 64, HueRes = 128;
    const float SwatchWidth = 90f, SquareHeight = 130f, HueHeight = 10f, HexRowHeight = 28f;

    float h, s, v;
    Image chosen;
    RawImage square;
    Texture2D squareTex;
    Color32[] squarePixels;
    RectTransform squareRing, hueRing;
    Image hueRingFill;
    TMP_InputField hexField;
    bool writingHex;

    public static ColourPicker Build(Transform parent)
    {
        var p = new ColourPicker();
        var frame = UiKit.Field(UiKit.Img("ColourPicker", parent, UiKit.SlotFill));
        p.Root = frame.rectTransform;

        // The chosen colour, full height of the square, on the left.
        p.chosen = UiKit.Img("Chosen", frame.transform, Color.white);
        p.chosen.raycastTarget = false;
        Place(p.chosen.rectTransform, new Vector2(1, -1), new Vector2(SwatchWidth, SquareHeight), stretchRight: false);

        // The shade/brightness square.
        p.squareTex = new Texture2D(SquareRes, SquareRes, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        p.squarePixels = new Color32[SquareRes * SquareRes];
        var sq = new GameObject("Square", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        sq.transform.SetParent(frame.transform, false);
        p.square = sq.GetComponent<RawImage>();
        p.square.texture = p.squareTex;
        var sqrt = p.square.rectTransform;
        sqrt.anchorMin = new Vector2(0, 1); sqrt.anchorMax = new Vector2(1, 1); sqrt.pivot = new Vector2(0, 1);
        sqrt.offsetMin = new Vector2(SwatchWidth + 1, -1 - SquareHeight); sqrt.offsetMax = new Vector2(-1, -1);
        sq.AddComponent<PointerSurface>().onPoint = uv => { p.s = uv.x; p.v = uv.y; p.Refresh(true, false); };
        p.squareRing = Ring(sq.transform, 14f, out _);

        // The rainbow strip.
        var hueTex = new Texture2D(HueRes, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < HueRes; x++) hueTex.SetPixel(x, 0, Color.HSVToRGB(x / (HueRes - 1f), 1f, 1f));
        hueTex.Apply();
        var hu = new GameObject("Hue", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        hu.transform.SetParent(frame.transform, false);
        hu.GetComponent<RawImage>().texture = hueTex;
        var hrt = (RectTransform)hu.transform;
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1); hrt.pivot = new Vector2(0.5f, 1);
        hrt.offsetMin = new Vector2(14, -1 - SquareHeight - 12 - HueHeight); hrt.offsetMax = new Vector2(-14, -1 - SquareHeight - 12);
        hu.AddComponent<PointerSurface>().onPoint = uv => { p.h = Mathf.Min(uv.x, 0.9999f); p.Refresh(true, true); };
        p.hueRing = Ring(hu.transform, 16f, out p.hueRingFill);

        // HEX: the colour as text, typed or copied.
        var row = new GameObject("HexRow", typeof(RectTransform));
        row.transform.SetParent(frame.transform, false);
        var rrt = (RectTransform)row.transform;
        rrt.anchorMin = new Vector2(0, 0); rrt.anchorMax = new Vector2(1, 0); rrt.pivot = new Vector2(0.5f, 0);
        rrt.offsetMin = new Vector2(14, 10); rrt.offsetMax = new Vector2(-14, 10 + HexRowHeight);
        var label = UiKit.MakeText("Label", row.transform, "HEX", 12, FontStyle.Normal, TextAnchor.MiddleLeft, null, false);
        UiKit.Paint(label, UiKit.TextDim);
        Place(label.rectTransform, new Vector2(0, 0), new Vector2(34, HexRowHeight), stretchRight: false, fromBottom: true);

        var box = UiKit.Field(UiKit.Img("Hex", row.transform, UiKit.SlotFill));
        var brt = box.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = new Vector2(40, 0); brt.offsetMax = new Vector2(-56, 0);
        var text = UiKit.MakeText("Text", box.transform, "", 14, FontStyle.Normal, TextAnchor.MiddleCenter, null, false);
        UiKit.Fill(text.rectTransform, new Vector2(4, 0), new Vector2(-4, 0));
        p.hexField = box.gameObject.AddComponent<TMP_InputField>();
        p.hexField.textViewport = brt;
        p.hexField.textComponent = text;
        p.hexField.lineType = TMP_InputField.LineType.SingleLine;
        p.hexField.characterLimit = 7;
        p.hexField.customCaretColor = true;
        UiKit.StyleTyping(p.hexField);
        p.hexField.onValueChanged.AddListener(p.OnHexTyped);

        var copy = UiKit.Btn("Copy", row.transform, () => GUIUtility.systemCopyBuffer = p.hexField.text);
        var crt = (RectTransform)copy.transform;
        crt.anchorMin = new Vector2(1, 0); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(1, 0.5f);
        crt.sizeDelta = new Vector2(50, 0); crt.anchoredPosition = Vector2.zero;

        p.Set(Color.white);
        return p;
    }

    /// <summary>The total height the picker needs, for whoever lays it out.</summary>
    public const float Height = 1 + SquareHeight + 12 + HueHeight + 12 + HexRowHeight + 10;

    /// <summary>Show <paramref name="c"/> without raising <see cref="OnChanged"/> — for pointing the picker at an
    /// existing colour.</summary>
    public void Set(Color c)
    {
        Color.RGBToHSV(c, out h, out s, out v);
        Refresh(false, true);
    }

    void OnHexTyped(string typed)
    {
        if (writingHex) return;
        if (typed == null || typed.Length != 7 || typed[0] != '#') return;
        if (!ColorUtility.TryParseHtmlString(typed, out var c)) return;
        Color.RGBToHSV(c, out h, out s, out v);
        Refresh(true, true, keepHexText: true);
    }

    void Refresh(bool notify, bool hueMoved, bool keepHexText = false)
    {
        if (hueMoved) DrawSquare();
        var c = Value;
        chosen.color = c;
        // Placed by proportion, not pixels, so it is right before the window has ever been laid out.
        squareRing.anchorMin = squareRing.anchorMax = new Vector2(s, v);
        hueRing.anchorMin = hueRing.anchorMax = new Vector2(h, 0.5f);
        hueRingFill.color = Color.HSVToRGB(h, 1f, 1f);
        if (!keepHexText)
        {
            writingHex = true;
            hexField.text = "#" + ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant();
            writingHex = false;
        }
        if (notify) OnChanged?.Invoke(c);
    }

    void DrawSquare()
    {
        for (int y = 0; y < SquareRes; y++)
            for (int x = 0; x < SquareRes; x++)
                squarePixels[y * SquareRes + x] = Color.HSVToRGB(h, x / (SquareRes - 1f), y / (SquareRes - 1f));
        squareTex.SetPixels32(squarePixels);
        squareTex.Apply();
    }

    // A white ring with a thin dark edge; Refresh anchors it where the colour sits.
    static RectTransform Ring(Transform parent, float size, out Image fill)
    {
        var edge = UiKit.Round(UiKit.Img("Ring", parent, new Color(0f, 0f, 0f, 0.4f)), UiKit.WindowRadius);
        edge.raycastTarget = false;
        var rt = edge.rectTransform;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size + 2, size + 2);
        rt.anchoredPosition = Vector2.zero;
        var white = UiKit.Round(UiKit.Img("White", edge.transform, Color.white), UiKit.WindowRadius);
        white.raycastTarget = false;
        UiKit.Fill(white.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));
        fill = UiKit.Round(UiKit.Img("Fill", white.transform, new Color(0f, 0f, 0f, 0f)), UiKit.ButtonRadius);
        fill.raycastTarget = false;
        UiKit.Fill(fill.rectTransform, new Vector2(3, 3), new Vector2(-3, -3));
        return rt;
    }

    // Anchor to the parent's top-left (or bottom-left) at an offset, a fixed size.
    static void Place(RectTransform rt, Vector2 offset, Vector2 size, bool stretchRight, bool fromBottom = false)
    {
        var corner = fromBottom ? new Vector2(0, 0) : new Vector2(0, 1);
        rt.anchorMin = rt.anchorMax = corner; rt.pivot = corner;
        rt.sizeDelta = size; rt.anchoredPosition = offset;
    }

    /// <summary>Reports a press or drag inside this rectangle as a position from 0 to 1 on each axis, bottom-left
    /// origin. Event-driven, nothing per frame.</summary>
    sealed class PointerSurface : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public Action<Vector2> onPoint;
        public void OnPointerDown(PointerEventData e) => Report(e);
        public void OnDrag(PointerEventData e) => Report(e);

        void Report(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local)) return;
            var r = rt.rect;
            onPoint?.Invoke(new Vector2(Mathf.Clamp01((local.x - r.xMin) / r.width), Mathf.Clamp01((local.y - r.yMin) / r.height)));
        }
    }
}
