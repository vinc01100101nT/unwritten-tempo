using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The pictures behind every front screen — sign-in, character select and the loading curtain — printed like pages
/// of the artist's book. Every picture in <c>Resources/UI/KeyArt</c> is used, in name order; dropping a new one in
/// that folder adds it with no other change.
///
/// <para><b>Each picture</b> covers the whole screen at its own shape, zoomed a tenth past that, and drifts slowly in
/// a direction picked at random — any angle — never far enough for an edge or corner to show. The sign-in and
/// character-select screens show the pictures one after another, each fading over the last; the loading curtain
/// shows one picked at random each time it goes up.</para>
///
/// <para><b>Over the pictures</b>, one full-screen image draws the world's own paper grain and a soft dark edge about
/// a centimetre wide (<c>Unwritten/FrontScreenPaper</c>).</para>
/// </summary>
public sealed class FrontBackdrop : MonoBehaviour
{
    const float HoldSeconds = 12f;
    const float FadeSeconds = 2f;
    const float Zoom = 1.10f;
    // the share of the overhang a drift leaves unused, so an edge never quite reaches the screen
    const float KeepSpare = 0.1f;
    const float EdgeDarkness = 0.85f;

    static Texture2D[] pictures;
    static int nextPicture;

    static readonly int InkColorID = Shader.PropertyToID("_InkColor");
    static readonly int PaperStrengthID = Shader.PropertyToID("_PaperStrength");
    static readonly int EdgeWidthID = Shader.PropertyToID("_EdgeWidth");
    static readonly int EdgeDarknessID = Shader.PropertyToID("_EdgeDarkness");

    sealed class Layer
    {
        public RawImage image;
        public RectTransform rt;
        public Texture2D picture;
        public float radians, age;
        public Vector2 drift;
    }

    readonly Layer[] layers = { new Layer(), new Layer() };
    int front;
    bool slideshow;
    RectTransform area;
    Vector2 laidOutFor;
    Material paper;
    int edgeForWidth, edgeForHeight;

    /// <summary>Put the pictures under <paramref name="veil"/>, the paper over them. Nothing in the folder: nothing is
    /// placed, and the veil alone is the backdrop.</summary>
    public static FrontBackdrop Place(Transform veil, bool slideshow)
    {
        if (Pictures().Length == 0) return null;
        var go = new GameObject("Backdrop", typeof(RectTransform));
        go.transform.SetParent(veil, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var backdrop = go.AddComponent<FrontBackdrop>();
        backdrop.Build(rt, slideshow);
        return backdrop;
    }

    static Texture2D[] Pictures()
    {
        if (pictures != null) return pictures;
        pictures = Resources.LoadAll<Texture2D>("UI/KeyArt");
        System.Array.Sort(pictures, (a, b) => string.CompareOrdinal(a.name, b.name));
        return pictures;
    }

    void Build(RectTransform rt, bool showAll)
    {
        area = rt;
        slideshow = showAll;
        foreach (var layer in layers)
        {
            var go = new GameObject("Picture", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(area, false);
            layer.rt = (RectTransform)go.transform;
            layer.rt.anchorMin = layer.rt.anchorMax = layer.rt.pivot = new Vector2(0.5f, 0.5f);
            layer.image = go.GetComponent<RawImage>();
            layer.image.raycastTarget = false;
            go.SetActive(false);
        }

        var paperGO = new GameObject("Paper", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        paperGO.transform.SetParent(area.parent, false);
        paperGO.transform.SetSiblingIndex(area.GetSiblingIndex() + 1);
        var paperRT = (RectTransform)paperGO.transform;
        paperRT.anchorMin = Vector2.zero;
        paperRT.anchorMax = Vector2.one;
        paperRT.offsetMin = paperRT.offsetMax = Vector2.zero;
        var paperImage = paperGO.GetComponent<RawImage>();
        paperImage.raycastTarget = false;
        var saved = Resources.Load<Material>("UI/FrontScreenPaper");
        if (saved != null)
        {
            // a copy, so nothing written to it at runtime reaches the saved material
            paper = new Material(saved);
            var world = HandDrawnFX.Instance;
            paper.SetColor(InkColorID, world != null ? world.inkColor : HandDrawnFX.DefaultInkColor);
            paper.SetFloat(PaperStrengthID, world != null ? world.paperGrain : HandDrawnFX.DefaultPaperGrain);
            paper.SetFloat(EdgeDarknessID, EdgeDarkness);
            paperImage.material = paper;
        }
        else paperGO.SetActive(false);

        Restart();
    }

    /// <summary>Start over: the next picture in turn on a slideshow, a random one on the loading curtain.</summary>
    public void Restart()
    {
        foreach (var layer in layers) layer.image.gameObject.SetActive(false);
        front = 0;
        Begin(layers[0], slideshow ? NextPicture() : pictures[Random.Range(0, pictures.Length)], 1f);
    }

    static Texture2D NextPicture()
    {
        var picture = pictures[nextPicture];
        nextPicture = (nextPicture + 1) % pictures.Length;
        return picture;
    }

    void Begin(Layer layer, Texture2D picture, float alpha)
    {
        layer.picture = picture;
        layer.image.texture = picture;
        layer.radians = Random.value * Mathf.PI * 2f;
        layer.age = 0f;
        layer.image.color = new Color(1f, 1f, 1f, alpha);
        LayOut(layer);
        layer.rt.SetAsLastSibling();
        layer.image.gameObject.SetActive(true);
    }

    void LayOut(Layer layer)
    {
        Vector2 screen = area.rect.size;
        if (screen.x <= 0f || screen.y <= 0f || layer.picture == null) return;
        Vector2 size = FrontBackdropMath.CoverSize(screen, (float)layer.picture.width / layer.picture.height, Zoom);
        layer.rt.sizeDelta = size;
        layer.drift = FrontBackdropMath.Drift(layer.radians, (size - screen) * 0.5f, KeepSpare);
        Move(layer);
    }

    void Move(Layer layer)
        => layer.rt.anchoredPosition = FrontBackdropMath.Offset(layer.drift, layer.age, HoldSeconds + FadeSeconds);

    void Update()
    {
        Vector2 screen = area.rect.size;
        if (screen != laidOutFor)
        {
            laidOutFor = screen;
            foreach (var layer in layers)
                if (layer.image.gameObject.activeSelf) LayOut(layer);
        }
        if (paper != null && (Screen.width != edgeForWidth || Screen.height != edgeForHeight))
        {
            edgeForWidth = Screen.width;
            edgeForHeight = Screen.height;
            paper.SetVector(EdgeWidthID, FrontBackdropMath.CentimetreShare(edgeForWidth, edgeForHeight, Screen.dpi));
        }

        float seconds = Time.unscaledDeltaTime;
        var current = layers[front];
        current.age += seconds;
        Move(current);
        if (!slideshow || current.age < HoldSeconds) return;

        var next = layers[1 - front];
        if (!next.image.gameObject.activeSelf) Begin(next, NextPicture(), 0f);
        next.age += seconds;
        Move(next);
        next.image.color = new Color(1f, 1f, 1f, FrontBackdropMath.FadeIn(next.age, FadeSeconds));
        if (next.age < FadeSeconds) return;
        current.image.gameObject.SetActive(false);
        front = 1 - front;
    }

    void OnDestroy()
    {
        if (paper != null) Destroy(paper);
    }
}
