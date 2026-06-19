using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// A small floating HP bar + name that sits at the BOTTOM of a billboard mob's sprite (RO/MMO style):
/// the bar hangs just under the feet and the mob's name reads below it. Billboards to the camera, drains
/// the bar from green→red as the mob is hurt, and hides while the mob is dead (restored on respawn).
///
/// PERFORMANCE (see PERFORMANCE.md): there is exactly ONE shared world-space <see cref="Canvas"/> for
/// EVERY mob nameplate — never a Canvas per mob (canvases are batch/rebuild boundaries; one-per-mob is
/// an explicit anti-pattern with dozens of mobs on screen). Each mob owns a lightweight widget (two
/// <see cref="Image"/>s + one <see cref="Text"/>) parented under that shared canvas; per frame it only
/// FOLLOWS the mob and faces the camera (cheap transform writes, no allocation — same as
/// <see cref="DamagePopup"/>), and it only rewrites the fill/colour when HP actually changes (the
/// <see cref="TrainingDummy"/>/<see cref="BossBanner"/> "update-on-change" discipline). The name is set
/// once. A shared ZTest-Always material keeps the bottom-anchored plate from being swallowed by the
/// terrain it sits on.
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public class MobNameplate : MonoBehaviour
{
    [Tooltip("Name shown under the HP bar. Set by the mob builder; falls back to the GameObject name.")]
    public string displayName;

    [Header("Layout (safe to tweak)")]
    [Tooltip("World size of the whole plate (shrinks the UI pixels into the 3D world).")]
    public float worldScale = 0.012f;
    [Tooltip("Bar width / height in UI pixels (before worldScale).")]
    public float barWidth = 64f, barHeight = 10f;
    [Tooltip("Extra drop below the sprite's feet, in world units.")]
    public float gap = 0.02f;
    [Tooltip("Name font size in UI pixels (before worldScale).")]
    public int nameFontSize = 20;

    Health health;
    Transform camT;
    RectTransform root;
    Image fill;
    float feetOffset;            // world units below the mob's centre to hang the plate (its feet)
    int lastShownHP = int.MinValue;
    bool shown = true;

    // ---- shared across ALL mobs: one canvas, one white sprite, the builtin font, one on-top material ----
    static Transform host;
    static Sprite whiteSprite;
    static Font font;
    static Material onTopMat;     // UI/Default with ZTest Always, so a feet-level plate isn't hidden by terrain

    void Awake()
    {
        // Used by regular mobs AND the field boss (the boss just gets a bigger one — see BossBanner).
        health = GetComponent<Health>();
        if (string.IsNullOrEmpty(displayName))
            displayName = gameObject.name.StartsWith("Monster_") ? gameObject.name.Substring("Monster_".Length)
                                                                 : gameObject.name;

        // The sprite is one world cell tall and centre-pivoted, so the feet sit ~0.4 of its (uniform) world
        // scale below the transform centre (0.5 cell − ~0.1 foot padding; see Build25DSpike.SpriteFoot).
        feetOffset = -0.4f * Mathf.Max(0.0001f, transform.lossyScale.y) - gap;
    }

    void OnDestroy()
    {
        // The widget lives under the shared canvas, not under the mob, so it must be cleaned up by hand
        // (a no-op if the canvas — and with it the child — was already torn down on scene unload).
        if (root != null) Destroy(root.gameObject);
    }

    void LateUpdate()
    {
        if (root == null) Build();                 // first frame: build the widget once
        if (camT == null) { var c = Camera.main; if (c == null) return; camT = c.transform; }

        bool alive = health.Current > 0;           // dead → remains/fade/respawn-wait: hide the plate
        if (alive != shown) { shown = alive; root.gameObject.SetActive(alive); }
        if (!alive) return;

        // Follow the mob (after it has moved this frame) and yaw to face the camera — same as DamagePopup.
        root.position = transform.position + new Vector3(0f, feetOffset, 0f);
        root.rotation = camT.rotation;

        if (health.Current != lastShownHP)         // only touch UI when HP actually changed (no per-frame rebuild)
        {
            lastShownHP = health.Current;
            float frac = Mathf.Clamp01((float)health.Current / Mathf.Max(1, health.maxHealth));
            fill.fillAmount = frac;
            fill.color = HpColor(frac);
        }
    }

    // Green when healthy → yellow → red when nearly dead, so HP reads at a glance.
    static Color HpColor(float f)
    {
        var green  = new Color(0.40f, 0.85f, 0.30f);
        var yellow = new Color(0.95f, 0.80f, 0.20f);
        var red    = new Color(0.88f, 0.20f, 0.20f);
        return f > 0.5f ? Color.Lerp(yellow, green, (f - 0.5f) * 2f) : Color.Lerp(red, yellow, f * 2f);
    }

    void Build()
    {
        EnsureHost();

        root = NewRect("MobNameplate", host);
        root.localScale = Vector3.one * worldScale;
        root.pivot = new Vector2(0.5f, 1f);                 // anchored at the top (the feet line); grows downward
        root.sizeDelta = new Vector2(barWidth, barHeight);

        // HP bar background (dark track), top of the plate.
        var bg = NewImage("Bar", root, new Color(0.06f, 0.05f, 0.07f, 0.85f));
        Place(bg.rectTransform, Vector2.zero, new Vector2(barWidth, barHeight));

        // HP bar fill, inset inside the track, drains right→left.
        fill = NewImage("Fill", bg.rectTransform, HpColor(1f));
        var frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.pivot = new Vector2(0.5f, 0.5f);
        frt.offsetMin = new Vector2(1.5f, 1.5f); frt.offsetMax = new Vector2(-1.5f, -1.5f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;

        // Name, just BELOW the bar.
        var labelGO = new GameObject("Name", typeof(RectTransform));
        var lrt = (RectTransform)labelGO.transform;
        lrt.SetParent(root, false);
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f); lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0f, -barHeight - 2f);
        lrt.sizeDelta = new Vector2(barWidth * 3f, nameFontSize + 4);
        var label = labelGO.AddComponent<Text>();
        label.font = Font;
        label.fontSize = nameFontSize;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.UpperCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        label.color = new Color(0.97f, 0.97f, 0.99f);
        label.text = displayName;
        if (OnTop != null) label.material = OnTop;
        var outline = labelGO.AddComponent<Outline>();      // dark edge so the name reads on any terrain
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        lastShownHP = int.MinValue;                         // force the first fill refresh in LateUpdate
    }

    // ---- shared resources --------------------------------------------------------------------------
    static void EnsureHost()
    {
        if (host != null) return;                           // alive (Unity-null check); recreated after a scene unload
        var go = new GameObject("MobNameplateSystem");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = DepthSortRuntime.FxPopup;     // FX band: above all entities + overhead
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        host = go.transform;
    }

    static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

    // One shared UI material that ignores the depth buffer, so a plate sitting at the mob's feet draws on
    // top of the terrain it stands on instead of being clipped into it.
    static Material OnTop
    {
        get
        {
            if (onTopMat != null) return onTopMat;
            var sh = Shader.Find("UI/Default");
            if (sh == null) return null;
            onTopMat = new Material(sh) { name = "MobNameplate (on top)" };
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

    // Anchor a child to the top-centre of the plate (the feet line) at the given size.
    static void Place(RectTransform rt, Vector2 anchoredPos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
    }
}
