using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The slim horizontal <b>stamina gauge</b> — the sprint pool. Sits above the hotbar with a plain WHITE fill that
/// empties left→right as V-sprint drains <see cref="Character.Stamina"/> and refills as it recharges. Always
/// visible (stamina always exists).
///
/// <para>Self-builds its holder on the persistent GameUICanvas (added by <c>HotbarPanel</c>, exactly like
/// <see cref="BuffBarPanel"/>); event-driven off <see cref="Character.OnStaminaChanged"/>.
/// The fill is written only on a stamina change — no per-frame or per-hit allocation.</para>
/// </summary>
public class StaminaBar : MonoBehaviour
{
    const float BarWidth = 300f, BarHeight = 12f, YAboveUlt = 124f;
    static readonly Color TrackColor = new Color(0.06f, 0.05f, 0.08f, 0.92f);
    static readonly Color FillColor  = Color.white;

    Canvas canvas;
    RectTransform holder;
    Image fill;
    Sprite whiteSprite;
    bool built;

    Character subscribed;
    bool dirty;
    float shownFill = -1f;

    void Update()
    {
        if (!built) { TryBuild(); return; }
        var ch = Character.Instance;
        EnsureSubscribed(ch);
        Refresh(ch);
    }

    void EnsureSubscribed(Character ch)
    {
        if (subscribed == ch) return;
        if (subscribed != null) subscribed.OnStaminaChanged -= MarkDirty;
        subscribed = ch;
        if (subscribed != null) subscribed.OnStaminaChanged += MarkDirty;
        dirty = true;
    }

    void MarkDirty() => dirty = true;
    void OnDestroy() { if (subscribed != null) subscribed.OnStaminaChanged -= MarkDirty; }

    void TryBuild()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        var go = new GameObject("HUD_StaminaBar", typeof(RectTransform));
        holder = (RectTransform)go.transform;
        holder.SetParent(canvas.transform, false);
        holder.anchorMin = holder.anchorMax = holder.pivot = new Vector2(0.5f, 0f);
        holder.anchoredPosition = new Vector2(0f, YAboveUlt);
        holder.sizeDelta = new Vector2(BarWidth, BarHeight);

        var track = UiKit.Img("Track", holder, TrackColor);
        UiKit.Fill(track.rectTransform, Vector2.zero, Vector2.zero);
        track.raycastTarget = false;

        fill = UiKit.Img("Fill", track.transform, FillColor);
        fill.sprite = whiteSprite;
        UiKit.Fill(fill.rectTransform, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;
        fill.raycastTarget = false;

        built = true;
        dirty = true;
    }

    void Refresh(Character ch)
    {
        if (!dirty) return;
        dirty = false;
        float frac = ch != null ? ch.Stamina01 : 0f;
        if (!Mathf.Approximately(frac, shownFill)) { fill.fillAmount = frac; shownFill = frac; }
    }
}
