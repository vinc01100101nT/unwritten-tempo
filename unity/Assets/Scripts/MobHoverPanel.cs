using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Screen-space panel (top-right) that shows the hovered mob's HP bar, name, and element
/// when the cursor passes over a living mob in the 3D world. Self-builds its canvas on Awake.
/// Add to GameSystems (done by SystemsSceneBuilder).
/// </summary>
public class MobHoverPanel : MonoBehaviour
{
    const float W     = 180f;
    const float BarH  = 10f;
    const float Pad   = 8f;
    const float BadgeW = 72f, BadgeH = 14f;

    CanvasGroup group;
    Image       fill;
    TMP_Text    nameLabel;
    TMP_Text    hpLabel;
    Image       elementBadge;
    TMP_Text    elementLabel;

    Health  tracked;
    Camera  cam;
    int     lastHP = int.MinValue;

    // shared one-time resources (same discipline as MobNameplate)
    static Sprite white;

    // ── lifecycle ──────────────────────────────────────────────────────────────

    void Awake() => Build();

    void Update()
    {
        if (cam == null) cam = Camera.main;

        var h = Player3DController.MonsterUnderCursor(cam);
        if (h == null) { Hide(); return; }

        Show();
        if (h != tracked) Bind(h);

        // Refresh HP display only when the value changes (no per-frame allocation).
        if (tracked != null && tracked.Current != lastHP)
        {
            lastHP = tracked.Current;
            float frac = Mathf.Clamp01((float)tracked.Current / Mathf.Max(1, tracked.maxHealth));
            fill.fillAmount = frac;
            fill.color      = HpColor(frac);
            hpLabel.text    = $"{tracked.Current} / {tracked.maxHealth}";
        }
    }

    // ── binding ────────────────────────────────────────────────────────────────

    void Bind(Health h)
    {
        tracked = h;
        lastHP  = int.MinValue;   // force refresh next Update

        var plate = h.GetComponent<MobNameplate>();
        nameLabel.text = plate != null && !string.IsNullOrEmpty(plate.displayName)
            ? plate.displayName
            : (h.name.StartsWith("Monster_") ? h.name.Substring("Monster_".Length) : h.name);

        Element el = h.element;
        if (el == Element.Neutral)
        {
            elementBadge.gameObject.SetActive(false);
        }
        else
        {
            elementBadge.gameObject.SetActive(true);
            Color c = Elements.Tint(el);
            elementBadge.color  = new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 0.90f);
            elementLabel.color  = c;
            elementLabel.text   = el.ToString().ToUpper();
        }
    }

    void Show() => group.alpha = 1f;
    void Hide() { group.alpha = 0f; tracked = null; }

    // ── HP color (mirrors MobNameplate) ───────────────────────────────────────

    static Color HpColor(float f)
    {
        var green  = new Color(0.40f, 0.85f, 0.30f);
        var yellow = new Color(0.95f, 0.80f, 0.20f);
        var red    = new Color(0.88f, 0.20f, 0.20f);
        return f > 0.5f ? Color.Lerp(yellow, green, (f - 0.5f) * 2f)
                        : Color.Lerp(red,    yellow, f * 2f);
    }

    // ── UI build (runs once, on Awake) ─────────────────────────────────────────

    void Build()
    {
        // Dedicated ScreenSpaceOverlay canvas so this never conflicts with the game HUD.
        var cvGO   = new GameObject("MobHoverCanvas") { hideFlags = HideFlags.DontSaveInEditor };
        var canvas = cvGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;   // above HUD (MobNameplate sits at FxPopup; we want on top)

        group               = cvGO.AddComponent<CanvasGroup>();
        group.alpha         = 0f;
        group.blocksRaycasts = false;
        group.interactable  = false;

        // ── root panel: top-right corner ──────────────────────────────────────
        float totalH = Pad + BarH + 2f + 18f + 4f + BadgeH + Pad;   // bar + name + badge + padding
        var panel = NewRect("Panel", cvGO.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot     = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-12f, -12f);
        panel.sizeDelta = new Vector2(W, totalH);

        // dark background
        var bg = NewImage("BG", panel, new Color(0.05f, 0.04f, 0.08f, 0.88f));
        StretchFull(bg.rectTransform);

        float y = -Pad;

        // ── HP bar track ──────────────────────────────────────────────────────
        var track = NewImage("HPTrack", panel, new Color(0.10f, 0.08f, 0.13f, 1f));
        PlaceTopLeft(track.rectTransform, new Vector2(Pad, y), new Vector2(W - Pad * 2f, BarH));

        fill = NewImage("HPFill", track.rectTransform, HpColor(1f));
        var frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.pivot     = new Vector2(0.5f, 0.5f);
        frt.offsetMin = new Vector2(1.5f, 1.5f); frt.offsetMax = new Vector2(-1.5f, -1.5f);
        fill.type         = Image.Type.Filled;
        fill.fillMethod   = Image.FillMethod.Horizontal;
        fill.fillOrigin   = (int)Image.OriginHorizontal.Left;
        fill.fillAmount   = 1f;

        // HP numbers centred inside the track
        hpLabel = NewText("HPLabel", track.rectTransform, 9, FontStyle.Normal,
                          new Color(0.85f, 0.85f, 0.85f));
        StretchFull(hpLabel.rectTransform);
        hpLabel.alignment = TextAlignmentOptions.Center;
        hpLabel.text      = "";

        y -= BarH + 2f;

        // ── name ──────────────────────────────────────────────────────────────
        nameLabel = NewText("Name", panel, 13, FontStyle.Bold, new Color(0.97f, 0.97f, 0.99f));
        PlaceTopLeft(nameLabel.rectTransform, new Vector2(Pad, y), new Vector2(W - Pad * 2f, 18f));
        nameLabel.alignment = TextAlignmentOptions.TopLeft;
        nameLabel.text      = "";

        y -= 20f;

        // ── element badge ─────────────────────────────────────────────────────
        elementBadge = NewImage("ElementBadge", panel, new Color(0.2f, 0.15f, 0.28f, 0.90f));
        PlaceTopLeft(elementBadge.rectTransform, new Vector2(Pad, y), new Vector2(BadgeW, BadgeH));

        elementLabel = NewText("ElementLabel", elementBadge.rectTransform, 9, FontStyle.Bold, Color.white);
        StretchFull(elementLabel.rectTransform);
        elementLabel.alignment = TextAlignmentOptions.Center;
        elementLabel.text      = "";

        elementBadge.gameObject.SetActive(false);   // hidden until a non-neutral mob is hovered
    }

    // ── shared UI helpers (no alloc, same as MobNameplate) ────────────────────

    static Sprite White => white != null ? white
        : (white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f)));

    static RectTransform NewRect(string name, Transform parent)
    {
        var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go  = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite       = White;
        img.color        = color;
        img.raycastTarget = false;
        return img;
    }

    static TMP_Text NewText(string name, Transform parent, int size, FontStyle style, Color color)
    {
        var t = UiKit.MakeText(name, parent, "", size, style, TextAnchor.UpperLeft, wrap: false);
        t.color = color;
        t.raycastTarget = false;
        return t;
    }

    static void PlaceTopLeft(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin       = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot           = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta       = size;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
