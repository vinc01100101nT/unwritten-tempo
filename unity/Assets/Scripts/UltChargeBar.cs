using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The prominent, standalone <b>ultimate charge gauge</b> — a slim horizontal bar centered just ABOVE the
/// hotbar that fills left→right as the shared ult meter (<see cref="Character.UltCharge"/>) builds, and flips
/// to a bright "READY" state the instant it holds enough to fire the equipped ultimate. The dedicated ULT
/// hotbar cell already shows the same meter as a clearing radial shade; this is the big, readable version so
/// the player doesn't have to read a tiny cell to know when their ult is up.
///
/// <para>Fill is progress toward FIRING (charge ÷ the equipped ult's <c>chargeCost</c>), so 100% == usable —
/// not charge ÷ max. Hidden entirely when no ultimate is equipped. Self-builds its holder on the persistent
/// GameUICanvas (added by <c>HotbarPanel</c>, like <see cref="BuffBarPanel"/>); event-driven off
/// <see cref="Character.OnUltChargeChanged"/> plus a cheap per-frame equip/visibility check. The fill/colour
/// are written only on a meter change and the label string is rebuilt only when the shown percent (or the
/// ready state) changes — no per-frame or per-hit allocation.</para>
/// </summary>
public class UltChargeBar : MonoBehaviour
{
    const float BarWidth = 300f, BarHeight = 16f;
    static readonly Color TrackColor   = new Color(0.06f, 0.05f, 0.08f, 0.92f);
    static readonly Color FillCharging  = new Color(0.62f, 0.30f, 0.92f, 1f);   // building — deep arcane purple
    static readonly Color FillReady      = new Color(1f, 0.78f, 0.30f, 1f);      // charged — bright gold
    static readonly Color TextCharging   = new Color(0.82f, 0.74f, 0.95f);
    static readonly Color TextReady       = new Color(1f, 0.95f, 0.72f);

    // Pre-baked "N%" strings so the per-percent repaint never allocates (like HotbarPanel's second strings).
    static readonly string[] Pct = BuildPct();
    static string[] BuildPct() { var a = new string[101]; for (int i = 0; i < a.Length; i++) a[i] = "ULT  " + i + "%"; return a; }
    const string ReadyText = "ULTIMATE READY";

    Canvas canvas;
    RectTransform holder;
    Image fill;
    TMP_Text label;
    Sprite whiteSprite;
    bool built;

    Character subscribed;
    bool dirty;                 // meter changed → repaint next frame
    float shownFill = -1f;
    sbyte lastReady = -1;
    int lastPct = -1;

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
        if (subscribed != null) subscribed.OnUltChargeChanged -= MarkDirty;
        subscribed = ch;
        if (subscribed != null) subscribed.OnUltChargeChanged += MarkDirty;
        dirty = true;
    }

    void MarkDirty() => dirty = true;
    void OnDestroy() { if (subscribed != null) subscribed.OnUltChargeChanged -= MarkDirty; }

    void TryBuild()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        var go = new GameObject("HUD_UltChargeBar", typeof(RectTransform));
        holder = (RectTransform)go.transform;
        holder.SetParent(canvas.transform, false);
        // Bottom-centre, lifted to sit just above the hotbar (hotbar top ≈ 42 + 54 = 96 from the bottom).
        holder.anchorMin = holder.anchorMax = holder.pivot = new Vector2(0.5f, 0f);
        holder.anchoredPosition = new Vector2(0f, 104f);
        holder.sizeDelta = new Vector2(BarWidth, BarHeight);

        var track = UiKit.Img("Track", holder, TrackColor);
        UiKit.Fill(track.rectTransform, Vector2.zero, Vector2.zero);
        track.raycastTarget = false;

        fill = UiKit.Img("Fill", track.transform, FillCharging);
        fill.sprite = whiteSprite;
        UiKit.Fill(fill.rectTransform, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 0f;
        fill.raycastTarget = false;

        label = UiKit.MakeText("Label", holder, "", 11, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
        label.color = TextCharging;
        label.raycastTarget = false;
        UiKit.Fill(label.rectTransform, Vector2.zero, Vector2.zero);

        built = true;
        dirty = true;
    }

    void Refresh(Character ch)
    {
        // Progress toward FIRING: charge ÷ the equipped ultimate's cost. No ult equipped (or a free/zero-cost
        // ult) → nothing to gauge, so hide the whole bar.
        float cost = ch != null ? ch.EquippedUltCost : 0f;
        bool show = cost > 0f;
        if (holder.gameObject.activeSelf != show) holder.gameObject.SetActive(show);
        if (!show) { dirty = false; return; }

        if (!dirty) return;   // meter unchanged since last write — skip the repaint
        dirty = false;

        float frac = Mathf.Clamp01(ch.UltCharge / cost);
        if (!Mathf.Approximately(frac, shownFill)) { fill.fillAmount = frac; shownFill = frac; }

        sbyte ready = (sbyte)(frac >= 1f ? 1 : 0);
        if (ready != lastReady)
        {
            fill.color = ready == 1 ? FillReady : FillCharging;
            label.color = ready == 1 ? TextReady : TextCharging;
            lastReady = ready;
        }

        int pct = ready == 1 ? 100 : Mathf.Clamp(Mathf.FloorToInt(frac * 100f), 0, 99);
        if (pct != lastPct)
        {
            label.text = ready == 1 ? ReadyText : Pct[pct];
            lastPct = pct;
        }
    }
}
