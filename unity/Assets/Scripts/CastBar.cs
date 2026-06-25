using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// The RO-style spell <b>cast bar</b> — a small bar that floats over the caster's head and FILLS
/// left→right as a spell winds up, then vanishes the instant the cast completes or is interrupted.
/// There is exactly ONE in the game (the player only casts one spell at a time), so this is a static,
/// lazily-built singleton driven by <see cref="Player3DCaster"/>: it calls <see cref="Set"/> every
/// casting frame and <see cref="Hide"/> when the cast ends.
///
/// PERFORMANCE (see PERFORMANCE.md): one shared world-space <see cref="Canvas"/>, built once and reused
/// for every cast (canvases are batch/rebuild boundaries — never one-per-cast). Per casting frame it only
/// writes transforms (follow + billboard) and the fill amount — no allocation, no Find/GetComponent, no
/// per-frame string work (the label is set only when it changes). Same "update-on-change" + shared
/// ZTest-Always material tricks as <see cref="MobNameplate"/>, so the bar isn't swallowed by terrain.
/// </summary>
[DisallowMultipleComponent]
public class CastBar : MonoBehaviour
{
    // Layout (UI pixels, scaled into the world by worldScale — matched to MobNameplate so the bar reads at
    // the same size as a mob's HP plate).
    const float WorldScale = 0.0075f;
    const float BarWidth = 60f, BarHeight = 8f;
    const int   LabelFontSize = 14;

    static readonly Color TrackColor = new Color(0.06f, 0.05f, 0.07f, 0.88f);

    static CastBar instance;          // the single live bar (Unity-null after a scene unload → rebuilt lazily)
    static bool quitting;

    Transform camT;
    RectTransform root;
    Image fill;
    Text label;
    string shownLabel;                // last label written (so we only touch the Text when it changes)

    // ---- shared resources (one each, across the whole game) -------------------
    static Sprite whiteSprite;
    static Font font;
    static Material onTopMat;         // UI/Default with ZTest Always so the bar draws over terrain/props

    /// <summary>Show/refresh the cast bar at <paramref name="worldPos"/> (the caster's head), filled to
    /// <paramref name="progress01"/> (0 = just started, 1 = done). Cheap to call every frame.</summary>
    public static void Set(Vector3 worldPos, float progress01, string text, Color tint)
    {
        var bar = Ensure();
        if (bar == null) return;
        bar.Apply(worldPos, Mathf.Clamp01(progress01), text, tint);
    }

    /// <summary>Hide the cast bar (cast finished or interrupted). Safe to call when already hidden.</summary>
    public static void Hide()
    {
        if (instance != null && instance.root != null && instance.root.gameObject.activeSelf)
            instance.root.gameObject.SetActive(false);
    }

    static CastBar Ensure()
    {
        if (quitting) return null;
        if (instance != null) return instance;
        var go = new GameObject("CastBarSystem") { hideFlags = HideFlags.DontSave };
        instance = go.AddComponent<CastBar>();
        return instance;
    }

    void OnApplicationQuit() => quitting = true;
    void OnDestroy() { if (instance == this) instance = null; }

    void Apply(Vector3 worldPos, float progress01, string text, Color tint)
    {
        if (camT == null) { var c = Camera.main; if (c == null) return; camT = c.transform; }
        if (root == null) Build();
        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);

        // Follow the caster's head and yaw to face the camera (same as MobNameplate/DamagePopup).
        root.position = worldPos;
        root.rotation = camT.rotation;

        fill.fillAmount = progress01;
        fill.color = tint;

        if (text != shownLabel) { shownLabel = text; label.text = text; }   // only touch the Text on change
    }

    void Build()
    {
        // One shared world-space canvas in the FX band (above entities), like the mob nameplates.
        var canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = DepthSortRuntime.FxPopup;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
        }

        root = NewRect("CastBar", transform);
        root.localScale = Vector3.one * WorldScale;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(BarWidth, BarHeight);

        // Dark track.
        var bg = NewImage("Track", root, TrackColor);
        var brt = bg.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;

        // Fill — inset inside the track, grows left→right as the spell winds up.
        fill = NewImage("Fill", bg.rectTransform, Color.white);
        var frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.pivot = new Vector2(0.5f, 0.5f);
        frt.offsetMin = new Vector2(1.5f, 1.5f); frt.offsetMax = new Vector2(-1.5f, -1.5f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 0f;

        // Skill name, just ABOVE the bar.
        var labelGO = new GameObject("Name", typeof(RectTransform));
        var lrt = (RectTransform)labelGO.transform;
        lrt.SetParent(root, false);
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f); lrt.pivot = new Vector2(0.5f, 0f);
        lrt.anchoredPosition = new Vector2(0f, 2f);
        lrt.sizeDelta = new Vector2(BarWidth * 3f, LabelFontSize + 4);
        label = labelGO.AddComponent<Text>();
        label.font = Font;
        label.fontSize = LabelFontSize;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.LowerCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        label.color = new Color(0.95f, 0.95f, 0.99f);
        if (OnTop != null) label.material = OnTop;
        var outline = labelGO.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.1f, -1.1f);
        shownLabel = null;
    }

    // ---- shared resources (mirrors MobNameplate) ------------------------------
    static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

    static Material OnTop
    {
        get
        {
            if (onTopMat != null) return onTopMat;
            var sh = Shader.Find("UI/Default");
            if (sh == null) return null;
            onTopMat = new Material(sh) { name = "CastBar (on top)" };
            onTopMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            return onTopMat;
        }
    }

    static Sprite White => whiteSprite != null
        ? whiteSprite
        : (whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f)));

    static RectTransform NewRect(string name, Transform parent)
    {
        var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = White;
        img.color = color;
        img.raycastTarget = false;
        if (OnTop != null) img.material = OnTop;
        return img;
    }
}
