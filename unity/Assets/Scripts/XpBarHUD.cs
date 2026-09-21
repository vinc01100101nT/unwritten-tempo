using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The progression bars pinned to the very bottom of the screen: CHARACTER level + XP on the left,
/// RING level + XP on the right (the ring's own track — see <see cref="RingProgress"/>), and the WEAPON MASTERY of
/// the weapon in hand just above the ring's, shown only while a weapon is held. Lives on the
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
    const float RowGap = 2f;       // gap between the ring bar and the mastery bar stacked above it

    Character ch;
    RectTransform charFill, ringFill, masteryFill;
    TMP_Text charLabel, ringLabel, masteryLabel;
    GameObject masteryBar;
    Image masteryFillImage;
    bool built;

    // change-detection so the text is only rewritten (alloc + canvas rebuild) when a number actually moves
    int lastCharLvl = -1, lastCharXp = -1, lastRingLvl = -1, lastRingXp = -1;
    WeaponType lastMasteryWeapon = (WeaponType)(-1);
    int lastMasteryLvl = -1;
    float lastMasteryInto = -1f, lastMasterySize = -1f;
    bool lastMasteryCapped, lastMasteryKnown;

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
        masteryFill = BuildBar(false, RingFill, out masteryLabel, 1); // right half, one row above the ring's
        masteryBar = masteryFill.parent.gameObject;
        masteryFillImage = masteryFill.GetComponent<Image>();
        masteryBar.SetActive(false);

        Bind();
        built = true;
        Refresh();
    }

    // One labelled bar filling half the screen width along the very bottom: the left bar runs from the left
    // edge to just short of centre, the right bar from just past centre to the right edge — so they meet in
    // the middle. Returns the fill RectTransform (width scaled to the XP fraction in Refresh).
    RectTransform BuildBar(bool leftSide, Color fillColor, out TMP_Text label, int row = 0)
    {
        var bg = NewImage("XpBar", transform, BarBg);
        float y = BottomY + row * (BarH + RowGap);
        var brt = bg.rectTransform;
        if (leftSide) { brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(0.5f, 0f); }
        else          { brt.anchorMin = new Vector2(0.5f, 0f); brt.anchorMax = new Vector2(1f, 0f); }
        brt.pivot = new Vector2(0.5f, 0f);
        brt.offsetMin = new Vector2(leftSide ? EdgeInset : CenterGap, y);
        brt.offsetMax = new Vector2(leftSide ? -CenterGap : -EdgeInset, y + BarH);
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
        ch.Mastery.OnChanged += Refresh;  // weapon mastery, as the server sent it
    }

    void OnDestroy()
    {
        if (ch == null) return;
        ch.OnStatsChanged -= Refresh;
        ch.Ring.OnChanged -= Refresh;
        ch.Mastery.OnChanged -= Refresh;
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

        RefreshMastery();
    }

    // The weapon in hand's mastery, drawn from the level and progress the server sent — nothing worked out here.
    void RefreshMastery()
    {
        var weapon = ch.HeldWeapon;
        int level = ch.Mastery.SentLevel(weapon);
        bool known = ch.Mastery.SentProgress(weapon, out float into, out float size, out bool capped);
        if (weapon == lastMasteryWeapon && level == lastMasteryLvl && known == lastMasteryKnown
            && Mathf.Approximately(into, lastMasteryInto) && Mathf.Approximately(size, lastMasterySize)
            && capped == lastMasteryCapped) return;
        lastMasteryWeapon = weapon; lastMasteryLvl = level; lastMasteryKnown = known;
        lastMasteryInto = into; lastMasterySize = size; lastMasteryCapped = capped;

        bool show = weapon != WeaponType.None && known;
        masteryBar.SetActive(show);
        if (!show) return;

        // The weapon's own colour, the one its skills are drawn in.
        masteryFillImage.color = WeaponColors.Load()?.Lookup(weapon) ?? RingFill;
        if (capped)
        {
            SetFill(masteryFill, 1f);
            masteryLabel.text = $"{weapon} Mastery Lv {level}    capped by this weapon's grade";   // perf-ok: gated by the early return above
        }
        else
        {
            SetFill(masteryFill, size > 0f ? into / size : 0f);
            masteryLabel.text = $"{weapon} Mastery Lv {level}    {into:0.#} / {size:0.#}";   // perf-ok: gated by the early return above
        }
    }

    static void SetFill(RectTransform fill, int xp, int toNext)
    {
        float frac = toNext > 0 ? Mathf.Clamp01(xp / (float)toNext) : 0f;
        fill.anchorMax = new Vector2(frac, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
    }

    static void SetFill(RectTransform fill, float frac)
    {
        fill.anchorMax = new Vector2(Mathf.Clamp01(frac), 1f);
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
