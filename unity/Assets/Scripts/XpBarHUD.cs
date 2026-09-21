using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The two progression bars pinned to the very bottom of the screen: CHARACTER level + XP on the left,
/// RING level + XP on the right (the ring's own track — see <see cref="RingProgress"/>). Lives on the
/// persistent <c>GameUICanvas</c> and self-builds (no editor rebake to appear), then refreshes only on the
/// change events — no per-frame work or string churn.
/// </summary>
public class XpBarHUD : MonoBehaviour
{
    static readonly Color BarBg     = new Color(0f, 0f, 0f, 0.55f);
    static readonly Color CharFill  = new Color(0.55f, 0.85f, 0.5f, 0.9f);    // green — the character track
    static readonly Color RingFill  = new Color(0.55f, 0.8f, 1f, 0.9f);       // blue — the ring track
    static readonly Color Gold      = new Color(1f, 0.86f, 0.5f);

    const float BarH = 16f;        // bar height
    const float EdgeInset = 8f;    // gap from the screen's left/right edge
    const float CenterGap = 3f;    // half-gap where the two bars meet in the middle
    const float BottomY = 4f;      // lift off the very bottom edge

    Character ch;
    RectTransform charFill, ringFill;
    TMP_Text charLabel, ringLabel;
    bool built;

    // change-detection so the text is only rewritten (alloc + canvas rebuild) when a number actually moves
    int lastCharLvl = -1, lastCharXp = -1, lastRingLvl = -1, lastRingXp = -1;

    void Update()
    {
        if (!built) { TryBuild(); return; }
        if (ch == null) { Bind(); return; }
        Refresh();
    }

    void TryBuild()
    {
        if (Character.Instance == null) return;

        charFill = BuildBar(true,  CharFill, out charLabel);    // left half
        ringFill = BuildBar(false, RingFill, out ringLabel);    // right half — meets the left bar at centre

        Bind();
        built = true;
        Refresh();
    }

    // One labelled bar filling half the screen width along the very bottom: the left bar runs from the left
    // edge to just short of centre, the right bar from just past centre to the right edge — so they meet in
    // the middle. Returns the fill RectTransform (width scaled to the XP fraction in Refresh).
    RectTransform BuildBar(bool leftSide, Color fillColor, out TMP_Text label)
    {
        var bg = NewImage("XpBar", transform, BarBg);
        var brt = bg.rectTransform;
        if (leftSide) { brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(0.5f, 0f); }
        else          { brt.anchorMin = new Vector2(0.5f, 0f); brt.anchorMax = new Vector2(1f, 0f); }
        brt.pivot = new Vector2(0.5f, 0f);
        brt.offsetMin = new Vector2(leftSide ? EdgeInset : CenterGap, BottomY);
        brt.offsetMax = new Vector2(leftSide ? -CenterGap : -EdgeInset, BottomY + BarH);
        bg.raycastTarget = false;

        var fill = NewImage("Fill", bg.transform, fillColor);
        fill.raycastTarget = false;
        var frt = fill.rectTransform;
        frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(0f, 1f);   // left-anchored; width set in Refresh
        frt.pivot = new Vector2(0f, 0.5f);
        frt.offsetMin = frt.offsetMax = Vector2.zero;

        label = NewText("Label", bg.transform, "", 11, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(6, 0); lrt.offsetMax = new Vector2(-6, 0);
        return frt;
    }

    void Bind()
    {
        if (ch != null) return;
        ch = Character.Instance;
        if (ch == null) return;
        ch.OnStatsChanged += Refresh;     // fired when the server's finished XP/level answer is taken
        ch.Ring.OnChanged += Refresh;     // ring XP/level
    }

    void OnDestroy()
    {
        if (ch == null) return;
        ch.OnStatsChanged -= Refresh;
        ch.Ring.OnChanged -= Refresh;
    }

    void Refresh()
    {
        if (ch == null || charFill == null) return;

        if (ch.Level != lastCharLvl || ch.XP != lastCharXp)
        {
            lastCharLvl = ch.Level; lastCharXp = ch.XP;
            SetFill(charFill, ch.XP, ch.XPToNext);
            charLabel.text = $"Lv {ch.Level}    {ch.XP} / {ch.XPToNext}";
        }

        var r = ch.Ring;
        if (r.Level != lastRingLvl || r.XP != lastRingXp)
        {
            lastRingLvl = r.Level; lastRingXp = r.XP;
            SetFill(ringFill, r.XP, r.XPToNext);
            ringLabel.text = $"Ring Lv {r.Level}    {r.XP} / {r.XPToNext}";
        }
    }

    static void SetFill(RectTransform fill, int xp, int toNext)
    {
        float frac = toNext > 0 ? Mathf.Clamp01(xp / (float)toNext) : 0f;
        fill.anchorMax = new Vector2(frac, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
    }

    // ---- tiny UI helpers ----
    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static TMP_Text NewText(string name, Transform parent, string str, int size, FontStyle style, TextAnchor anchor, Color color)
    {
        var t = UiKit.MakeText(name, parent, str, size, style, anchor, wrap: false);
        t.color = color; t.raycastTarget = false;
        return t;
    }
}
