using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The RO-style stat-distribution UI for the Character (C) panel. Shows the six primary stats
/// (STR/AGI/VIT/INT/DEX/LUK) with a <b>+</b> button each, a "Stat Points" counter, and a live
/// readout of the derived combat stats they feed. Lives on the persistent GameUICanvas and builds
/// its widgets into the panel <see cref="UIShellBuilder"/> created (CharacterPanel/Content),
/// replacing the old static stats text.
///
/// It refreshes on <see cref="Character.OnStatsChanged"/>, <c>OnHealthChanged</c> AND
/// <c>OnManaChanged</c> — so the readout no longer goes stale while mana regenerates.
/// </summary>
public class StatPanel : MonoBehaviour
{
    static readonly PrimaryStat[] Order =
        { PrimaryStat.STR, PrimaryStat.AGI, PrimaryStat.VIT, PrimaryStat.INT, PrimaryStat.DEX, PrimaryStat.LUK };

    TMP_Text pointsHeader, derivedText;
    readonly TMP_Text[] valueTexts = new TMP_Text[6];
    readonly TMP_Text[] costTexts  = new TMP_Text[6];   // pre-renewal: the next-point cost shown per stat
    readonly Button[] addButtons = new Button[6];
    bool built;

    void Start() => TryBuild();
    void Update() { if (!built) TryBuild(); }

    void TryBuild()
    {
        if (Character.Instance == null) return;
        var content = FindCharacterContent();
        if (content == null) return;

        // Retire the old static "Stats" text — this panel owns the stat display now.
        var oldStats = content.Find("Stats");
        if (oldStats != null) oldStats.gameObject.SetActive(false);

        // A vertical column in the panel's left region.
        var col = NewImage(content, new Color(0, 0, 0, 0)).rectTransform;
        col.name = "StatColumn";
        col.anchorMin = new Vector2(0, 0); col.anchorMax = new Vector2(0.46f, 1);   // left region (Equipment owns the right)
        col.offsetMin = new Vector2(8, 8); col.offsetMax = new Vector2(-4, -8);
        var vlg = col.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4; vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;

        pointsHeader = NewText(col, "Stat Points: 0", 15, FontStyle.Bold, TextAnchor.MiddleLeft);
        SetHeight(pointsHeader.gameObject, 22);

        for (int i = 0; i < Order.Length; i++)
            BuildRow(col, i);

        SetHeight(NewImage(col, new Color(1, 1, 1, 0.12f)).gameObject, 2);   // divider
        derivedText = NewText(col, "", 13, FontStyle.Normal, TextAnchor.UpperLeft);
        derivedText.lineSpacing = 1.05f;

        Character.Instance.OnStatsChanged  += Refresh;
        Character.Instance.OnHealthChanged += Refresh;
        Character.Instance.OnManaChanged   += Refresh;
        built = true;
        Refresh();
    }

    void BuildRow(Transform parent, int i)
    {
        var row = NewImage(parent, new Color(1, 1, 1, 0.04f)).rectTransform;
        SetHeight(row.gameObject, 26);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6; h.padding = new RectOffset(4, 4, 0, 0);
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandHeight = true; h.childAlignment = TextAnchor.MiddleLeft;

        var name = NewText(row, Order[i].ToString(), 15, FontStyle.Bold, TextAnchor.MiddleLeft);
        name.gameObject.AddComponent<LayoutElement>().preferredWidth = 44;

        valueTexts[i] = NewText(row, "1", 15, FontStyle.Normal, TextAnchor.MiddleRight);
        var ve = valueTexts[i].gameObject.AddComponent<LayoutElement>();
        ve.flexibleWidth = 1;

        // Next-point cost (pre-renewal ⌊V/10⌋+2), or "MAX" at the cap. Greyed/red when unaffordable.
        costTexts[i] = NewText(row, "", 12, FontStyle.Normal, TextAnchor.MiddleRight);
        costTexts[i].color = new Color(0.7f, 0.75f, 0.8f);
        costTexts[i].gameObject.AddComponent<LayoutElement>().preferredWidth = 36;

        // The "+" button.
        var btnImg = NewImage(row, new Color(0.25f, 0.55f, 0.35f));
        btnImg.gameObject.AddComponent<LayoutElement>().preferredWidth = 28;
        var plus = NewText(btnImg.transform, "+", 18, FontStyle.Bold, TextAnchor.MiddleCenter);
        plus.raycastTarget = false;
        var prt = plus.rectTransform; prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        var btn = btnImg.gameObject.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        var stat = Order[i];
        // Click adds 1; press-and-hold adds 1, waits 0.5s, then keeps adding until released or out of
        // points. AddStat returns false when StatPoints hit 0, which ends the hold automatically.
        var hold = btnImg.gameObject.AddComponent<HoldRepeatButton>();
        hold.SetAction(() =>
        {
            if (!Character.Instance.AddStat(stat)) return false;
            UISounds.PlayClick();
            return true;
        });
        addButtons[i] = btn;
    }

    void Refresh()
    {
        var c = Character.Instance;
        if (c == null || !built) return;

        pointsHeader.text = $"Stat Points: {c.StatPoints}";
        for (int i = 0; i < Order.Length; i++)
        {
            var stat = Order[i];
            valueTexts[i].text = c.GetStat(stat).ToString();

            int cost = c.CostToRaise(stat);      // -1 once the stat hits the cap
            bool capped = cost < 0;
            bool can = c.CanRaise(stat);         // below cap AND the (escalating) next point is affordable
            costTexts[i].text  = capped ? "MAX" : $"({cost})";
            costTexts[i].color = capped ? new Color(0.85f, 0.80f, 0.45f)   // gold at the cap
                               : can    ? new Color(0.65f, 0.85f, 0.65f)   // green when you can afford it
                                        : new Color(0.60f, 0.45f, 0.45f);  // red-grey when you can't

            addButtons[i].interactable = can;
            ((Image)addButtons[i].targetGraphic).color = can
                ? new Color(0.25f, 0.55f, 0.35f) : new Color(0.3f, 0.3f, 0.3f);
        }

        derivedText.text =
            $"ATK   {c.MinAttack}-{c.MaxAttack}  M.ATK {c.MinMagicAttack}-{c.MaxMagicAttack}\n" +
            $"DEF   {c.CurrentDerived.hardDef / 10f:0.0}%+{c.SoftDefense}  M.DEF {c.CurrentDerived.hardMdef / 10f:0.0}%+{c.SoftMagicDefense}\n" +
            $"HP    {c.CurrentHP}/{c.MaxHP}\n" +
            $"MP    {c.CurrentMP}/{c.MaxMP}\n" +
            $"ASPD  {c.CurrentDerived.aspd} ({UnitStatMath.MaxAspd})   " +
            $"CAST ×{c.CastTimeMult:0.00}\n" +
            $"CRIT  {Mathf.RoundToInt(c.CritChance01 * 100)}% ×{c.CritMultiplier:0.0}\n" +
            $"HIT   {c.Hit,-4}  FLEE {c.Flee}\n" +
            $"MOVE  {c.DerivedWithStates.moveSpeed}% ({Mathf.RoundToInt(UnitStatMath.MinMoveMult * 100)}–{Mathf.RoundToInt(UnitStatMath.MaxMoveMult * 100)}%)";
    }

    RectTransform FindCharacterContent()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "CharacterPanel")
                return t.Find("Content") as RectTransform;
        return null;
    }

    void OnDestroy()
    {
        if (Character.Instance == null) return;
        Character.Instance.OnStatsChanged  -= Refresh;
        Character.Instance.OnHealthChanged -= Refresh;
        Character.Instance.OnManaChanged   -= Refresh;
    }

    // ---- helpers ----
    TMP_Text NewText(Transform parent, string str, int size, FontStyle style, TextAnchor anchor)
    {
        var t = UiKit.MakeText("Text", parent, str, size, style, anchor, wrap: false);
        t.color = Color.white;
        return t;
    }

    Image NewImage(Transform parent, Color color)
    {
        var go = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static void SetHeight(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = h;
    }
}
