using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The Ring of Progression — the Trio-Ring passive draft, presented as <b>bankable
/// points</b> (doc §11 #4). Lives on the persistent <c>GameUICanvas</c> and draws into the right-hand
/// column of the Character window (C), <see cref="ProgressionWindows.RingColumn"/>.
///
/// The ring is drawn AS a ring: its ten stats sit round a circle, the leader always at the top with its two
/// neighbours either side. While a point is banked, the stats it can be spent on carry a coloured edge and take
/// the click — there is no separate row of cards and no separate list. A pick that moves the leader TURNS the
/// ring the short way round until the new leader is at the top (doc §2 — "the draft is a steering wheel").
/// </summary>
public class SkillDraftPanel : MonoBehaviour
{
    // the Character window (C) the ring is drawn inside
    GameObject panelRoot;
    // the draft currently shown — its wildcard is the one the server rolled and sent
    DraftService.Draft? pending;

    // A pick ASKED of the server and not answered yet. The server spends the point; the ring only turns once
    // the walk it sends back shows that pick landed.
    DraftService.Option askedPick;
    int bankedWhenAsked;
    bool asking;
    bool bound, spinning;

    // Which ring position sits at the top, counted in stats. It differs from the leader's own position only
    // while the ring is turning.
    float turn;

    // Built once and re-texted in place, so a ring XP tick or a pick never rebuilds the panel.
    RectTransform dial, xpFill;
    TMP_Text levelText, xpText, bankText;
    readonly RectTransform[] nodeRect = new RectTransform[SkillRing.Count];
    readonly Image[] nodeEdge = new Image[SkillRing.Count];
    readonly TMP_Text[] nodeText = new TMP_Text[SkillRing.Count];
    readonly Button[] nodeButton = new Button[SkillRing.Count];
    readonly DraftService.Option[] nodeOption = new DraftService.Option[SkillRing.Count];
    readonly bool[] nodePickable = new bool[SkillRing.Count];

    const float SlideDur = 0.45f;
    const float DialWidth = 274f, DialHeight = 244f, NodeRadius = 106f, NodeWidth = 62f, NodeHeight = 32f;
    const float HubBarWidth = 112f, HubBarHeight = 12f;

    static readonly Color CardBg     = UiKit.PanelRaise;
    // the accent = the leader
    static readonly Color LeaderEdge = UiKit.Accent;
    // purple = the gamble
    static readonly Color WildEdge   = new Color(0.85f, 0.55f, 1f);
    static readonly Color RowDim     = UiKit.TextDim;

    bool wasOpen;

    void Start()
    {
        // The Skills (K) window is added the same way the buff bar is — so it appears without a Build UI Shell
        // rebuild. Idempotent: skips if the shell already added one.
        if (GetComponent<SkillsWindow>() == null) gameObject.AddComponent<SkillsWindow>();
        TryBind();
    }

    void Update()
    {
        // Character/UI may not be ready on the very first frame
        if (!bound) { TryBind(); return; }
        // The ring lives inside the Character window (C), which ProgressionWindows opens and closes. Ring XP only
        // redraws while it is on screen, so opening the window is when it catches up.
        bool open = panelRoot != null && UiAnim.IsOpen(panelRoot);
        if (open != wasOpen)
        {
            wasOpen = open;
            if (open) Refresh();
        }
    }

    void TryBind()
    {
        if (Character.Instance == null) return;
        var column = ProgressionWindows.RingColumn;
        if (column == null) return;
        panelRoot = ProgressionWindows.CharacterRoot;

        Build(column);

        Character.Instance.Passives.OnChanged += OnWalkChanged;
        Character.Instance.OnStatsChanged += OnStatsChanged;
        Character.Instance.Ring.OnChanged += OnRingChanged;
        bound = true;
        Refresh();
    }

    // Ring XP ticks every kill, so it is only re-texted while the window is actually open (you only see it then).
    void OnRingChanged() { if (panelRoot != null && UiAnim.IsOpen(panelRoot)) Refresh(); }

    // The ring being equipped/unequipped arrives as a stat change (SetEquipped fires OnStatsChanged).
    void OnStatsChanged() { if (panelRoot != null && UiAnim.IsOpen(panelRoot)) Refresh(); }

    void OnDisable()
    {
        // A turn cut short by the component switching off must not leave picks refused for good.
        spinning = false;
    }

    void OnDestroy()
    {
        if (Character.Instance == null) return;
        Character.Instance.Passives.OnChanged -= OnWalkChanged;
        Character.Instance.OnStatsChanged -= OnStatsChanged;
        Character.Instance.Ring.OnChanged -= OnRingChanged;
    }

    // ---- build, once ----------------------------------------------------------

    void Build(RectTransform column)
    {
        var holder = UiKit.Img("RingDial", column, new Color(0, 0, 0, 0));
        holder.raycastTarget = false;
        dial = holder.rectTransform;
        dial.anchorMin = dial.anchorMax = dial.pivot = new Vector2(0.5f, 0.5f);
        dial.sizeDelta = new Vector2(DialWidth, DialHeight);
        dial.anchoredPosition = Vector2.zero;

        CircleOutline.Build(dial, NodeRadius * 2f, 2f, UiKit.Rule);

        for (int i = 0; i < SkillRing.Count; i++) BuildNode(i);
        BuildHub();
    }

    // One stat on the ring: a 2px edge that carries its colour while a point can be spent on it, a face, two lines.
    void BuildNode(int i)
    {
        var edge = UiKit.Img("Node_" + SkillRing.Ring[i], dial, CardBg);
        var rt = edge.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(NodeWidth, NodeHeight);

        var face = UiKit.Img("Face", edge.transform, Color.white);
        UiKit.Fill(face.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        face.raycastTarget = false;

        var label = UiKit.MakeText("Text", face.transform, "", 11, FontStyle.Normal, TextAnchor.MiddleCenter, null, false);
        UiKit.Fill(label.rectTransform, Vector2.zero, Vector2.zero);
        label.raycastTarget = false;

        // The face is white and takes its colour from the button, so hover and press are the theme's own colours.
        var btn = edge.gameObject.AddComponent<Button>();
        btn.targetGraphic = face;
        var colors = btn.colors;
        colors.normalColor = colors.selectedColor = colors.disabledColor = CardBg;
        colors.highlightedColor = UiKit.Hover;
        colors.pressedColor = UiKit.Rule;
        btn.colors = colors;
        int index = i;
        btn.onClick.AddListener(() => OnPick(index));

        nodeRect[i] = rt; nodeEdge[i] = edge; nodeText[i] = label; nodeButton[i] = btn;
    }

    // The middle of the ring: its name, its own level, its XP bar, and the points banked — the ring's own
    // progression, separate from the character's level on the left of the window.
    void BuildHub()
    {
        var title = HubLine("Title", 24f, 12, FontStyle.Bold);
        title.text = "Ring of Progression";
        title.color = UiKit.TitleText;
        levelText = HubLine("Level", 9f, 12, FontStyle.Normal);

        var barBg = UiKit.Img("XpBar", dial, UiKit.Rule);
        barBg.raycastTarget = false;
        var brt = barBg.rectTransform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(HubBarWidth, HubBarHeight);
        brt.anchoredPosition = new Vector2(0f, -7f);
        var fill = UiKit.Img("Fill", barBg.transform, UiKit.Accent);
        fill.raycastTarget = false;
        xpFill = fill.rectTransform;
        xpFill.anchorMin = Vector2.zero; xpFill.anchorMax = new Vector2(0f, 1f);
        xpFill.offsetMin = xpFill.offsetMax = Vector2.zero;
        xpText = UiKit.MakeText("Text", barBg.transform, "", 10, FontStyle.Bold, TextAnchor.MiddleCenter, null, false);
        UiKit.Fill(xpText.rectTransform, Vector2.zero, Vector2.zero);
        xpText.raycastTarget = false;

        bankText = HubLine("Banked", -24f, 12, FontStyle.Bold);
        bankText.color = UiKit.TitleText;
    }

    TMP_Text HubLine(string name, float y, int size, FontStyle style)
    {
        var t = UiKit.MakeText(name, dial, "", size, style, TextAnchor.MiddleCenter, null, false);
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(HubBarWidth + 16f, 15f);
        rt.anchoredPosition = new Vector2(0f, y);
        return t;
    }

    // ---- refresh, in place ----------------------------------------------------

    // Nothing is said when there is no point to spend: the count simply is not there.
    static string BankString(PassiveRanks p) => p.BankedPoints > 0
        ? $"{p.BankedPoints} ring point{(p.BankedPoints == 1 ? "" : "s")}"
        : "";

    void Refresh()
    {
        if (dial == null) return;
        var c = Character.Instance;
        var p = c.Passives;
        var r = c.Ring;

        pending = p.BankedPoints > 0 ? DraftService.BuildDraft(p) : (DraftService.Draft?)null;
        for (int i = 0; i < SkillRing.Count; i++) nodePickable[i] = false;
        if (pending.HasValue)
            foreach (var opt in pending.Value.options)
            {
                int i = SkillRing.IndexOf(opt.stat);
                nodeOption[i] = opt; nodePickable[i] = true;
            }

        for (int i = 0; i < SkillRing.Count; i++)
        {
            var stat = SkillRing.Ring[i];
            int ranks = p.RanksIn(stat);
            bool isLeader = p.HasLeader && stat == p.Leader;
            bool inTrio = p.HasLeader && SkillRing.InTrio(p.Leader, stat);
            bool pick = nodePickable[i];

            // A coloured edge tells the player a point can go here: purple = the across-the-ring wildcard;
            // every other stat (leader included) wears its native ring-region tint.
            nodeEdge[i].color = !pick ? CardBg : nodeOption[i].isWildcard ? WildEdge : SkillRing.TintFor(stat);
            nodeButton[i].interactable = pick;

            // A stat a point can go on shows what it is and what the pick would make it; the rest show what they are.
            int now = SkillRing.PercentPoints(stat, ranks);
            string value = pick ? $"{now}→{SkillRing.PercentPoints(stat, ranks + 1)}%" : $"+{now}%";
            nodeText[i].text = $"<b>{SkillRing.ShortName(stat)}</b> <size=9>×{ranks}</size>\n{value}";
            nodeText[i].fontStyle = isLeader ? FontStyles.Bold : FontStyles.Normal;
            nodeText[i].color = isLeader ? LeaderEdge : inTrio ? UiKit.TextMain : RowDim;
        }

        levelText.text = $"Lv {r.Level}";
        xpFill.anchorMax = new Vector2(Mathf.Clamp01((float)(r.XP / Mathf.Max(1, r.XPToNext))), 1f);
        xpText.text = $"XP {r.XP:0.00} / {r.XPToNext}";
        bankText.text = BankString(p);

        if (!spinning)
        {
            turn = p.HasLeader ? SkillRing.IndexOf(p.Leader) : 0;
            Place();
        }
    }

    // Stand every stat at its place round the circle for the current turn: clockwise from the top.
    void Place()
    {
        float step = 2f * Mathf.PI / SkillRing.Count;
        for (int i = 0; i < SkillRing.Count; i++)
        {
            float a = (i - turn) * step;
            nodeRect[i].anchoredPosition = new Vector2(Mathf.Sin(a) * NodeRadius, Mathf.Cos(a) * NodeRadius);
        }
    }

    // ---- the pick -------------------------------------------------------------

    void OnPick(int i)
    {
        if (spinning || asking || dial == null || !nodePickable[i]) return;
        var me = GameNetwork.MyPlayer;
        if (me == null) return;

        UISounds.PlayClick();
        askedPick = nodeOption[i]; bankedWhenAsked = Character.Instance.Passives.BankedPoints; asking = true;
        me.AskToPickRingStat(askedPick.stat);
    }

    // The walk changed — usually the server's answer to a pick. If it shows the asked pick landed (a point gone and
    // the leader now on the picked stat), the ring turns to it; otherwise (refused, or a change from elsewhere) it
    // is simply shown as it now is.
    void OnWalkChanged()
    {
        var p = Character.Instance.Passives;
        bool landed = asking && p.HasLeader && p.Leader == askedPick.stat && p.BankedPoints < bankedWhenAsked;
        asking = false;
        if (landed && dial != null && !spinning && isActiveAndEnabled)
        {
            // Raised first, so the refresh shows the new numbers and edges but leaves the turning to the coroutine.
            spinning = true;
            Refresh();
            StartCoroutine(Spin(SkillRing.IndexOf(p.Leader)));
            return;
        }
        Refresh();
    }

    // Turn the ring until `target` is at the top, the short way round. Stacking the leader is a turn of nothing.
    IEnumerator Spin(int target)
    {
        float from = turn, half = SkillRing.Count * 0.5f;
        float steps = Mathf.Repeat(target - from + half, SkillRing.Count) - half;
        if (Mathf.Approximately(steps, 0f)) { spinning = false; yield break; }
        for (float e = 0; e < SlideDur; e += Time.unscaledDeltaTime)
        {
            turn = from + steps * Ease(Mathf.Clamp01(e / SlideDur));
            Place();
            yield return null;
        }
        turn = target;
        Place();
        spinning = false;
    }

    // Smootherstep: eases gently IN and OUT (6t⁵−15t⁴+10t³). Zero velocity at both ends → no abrupt
    // start and a soft landing, which reads far smoother than a plain ease-out on a fast turn.
    static float Ease(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
}
