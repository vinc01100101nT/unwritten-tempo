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

    // A pick ASKED of the server and not answered yet. The server spends the point and always sends the walk back,
    // and the ring turns when that walk shows a new leader.
    bool asking;
    bool bound, spinning;
    Coroutine spin;
    int spinTarget = -1;

    // Which ring position sits at the top, counted in stats. It differs from the leader's own position only
    // while the ring is turning.
    float turn;
    // The next refresh puts the ring straight at its leader instead of turning to it: when it is first built, and
    // when the window opens on a leader that moved while it was shut.
    bool snapNext = true;

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

    static ThemeColor CardBg     => UiKit.PanelRaise;
    // the accent = the leader
    static ThemeColor LeaderEdge => UiKit.Accent;
    // purple = the gamble
    static readonly Color WildEdge   = new Color(0.85f, 0.55f, 1f);
    static ThemeColor RowDim     => UiKit.TextDim;

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
            if (open) { snapNext = true; Refresh(); }
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
        UiKit.ThemeChanged += OnThemeChanged;
        bound = true;
        Refresh();
    }

    // Ring XP ticks every kill, so it is only re-texted while the window is actually open (you only see it then).
    void OnRingChanged() { if (panelRoot != null && UiAnim.IsOpen(panelRoot)) Refresh(); }

    // The ring being equipped/unequipped arrives as a stat change (SetEquipped fires OnStatsChanged).
    void OnStatsChanged() { if (panelRoot != null && UiAnim.IsOpen(panelRoot)) Refresh(); }

    void OnDisable()
    {
        // A turn cut short by the component switching off must not leave picks refused for good; the next refresh
        // puts the ring where it belongs.
        spinning = false; spin = null; spinTarget = -1; snapNext = true;
    }

    void OnDestroy()
    {
        UiKit.ThemeChanged -= OnThemeChanged;
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
        TintNode(btn);
        int index = i;
        btn.onClick.AddListener(() => OnPick(index));

        nodeRect[i] = rt; nodeEdge[i] = edge; nodeText[i] = label; nodeButton[i] = btn;
    }

    // A node's face is white and the button tints it: the card colour at rest, the hover and press colours on use.
    static void TintNode(Button btn)
    {
        var colors = btn.colors;
        colors.normalColor = colors.selectedColor = colors.disabledColor = CardBg.Value;
        colors.highlightedColor = UiKit.Hover.Value;
        colors.pressedColor = UiKit.Rule.Value;
        btn.colors = colors;
    }

    void OnThemeChanged()
    {
        for (int i = 0; i < nodeButton.Length; i++) if (nodeButton[i] != null) TintNode(nodeButton[i]);
        if (panelRoot != null && UiAnim.IsOpen(panelRoot)) Refresh();
    }

    // The middle of the ring: its name, its own level, its XP bar, and the points banked — the ring's own
    // progression, separate from the character's level on the left of the window.
    void BuildHub()
    {
        var title = HubLine("Title", 24f, 12, FontStyle.Bold);
        title.text = "Ring of Progression";
        UiKit.Paint(title, UiKit.TitleText);
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
        UiKit.Paint(bankText, UiKit.TitleText);
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
            if (!pick) UiKit.Paint(nodeEdge[i], CardBg);
            else UiKit.Paint(nodeEdge[i], nodeOption[i].isWildcard ? WildEdge : SkillRing.TintFor(stat));
            nodeButton[i].interactable = pick;

            // A stat a point can go on shows what it is and what the pick would make it; the rest show what they are.
            int now = p.ShownPercent(stat);
            string value = pick ? $"{now}→{p.ShownNextPercent(stat)}%" : $"+{now}%";
            nodeText[i].text = $"<b>{SkillRing.ShortName(stat)}</b> <size=9>×{ranks}</size>\n{value}";
            nodeText[i].fontStyle = isLeader ? FontStyles.Bold : FontStyles.Normal;
            UiKit.Paint(nodeText[i], isLeader ? LeaderEdge : inTrio ? UiKit.TextMain : RowDim);
        }

        levelText.text = $"Lv {r.Level}";
        xpFill.anchorMax = new Vector2(Mathf.Clamp01((float)(r.XP / Mathf.Max(1, r.XPToNext))), 1f);
        xpText.text = $"XP {r.XP:0.00} / {r.XPToNext}";
        bankText.text = BankString(p);

        TurnTo(p.HasLeader ? SkillRing.IndexOf(p.Leader) : 0);
    }

    // THE ONE PLACE THE RING MOVES. Every refresh lands here, whichever event caused it — the walk arriving, the
    // stats it changes, the ring's XP — so the order those events fire in can never decide whether the ring turns.
    // A new leader while the window is on screen is turned to; otherwise the ring is simply put there.
    void TurnTo(int target)
    {
        bool open = panelRoot != null && UiAnim.IsOpen(panelRoot);
        if (snapNext || !open || !isActiveAndEnabled)
        {
            snapNext = false;
            if (spin != null) StopCoroutine(spin);
            spin = null; spinning = false; spinTarget = -1;
            turn = target;
            Place();
            return;
        }
        if (Mathf.Approximately(ShortWay(target - turn), 0f)) return;
        // Already turning there: the stat sheet and ring XP that follow a pick refresh the window mid-turn, and
        // restarting the ease from midway would stutter.
        if (spinning && spinTarget == target) return;
        // A turn already under way is redirected from wherever it has got to, never restarted from the old leader.
        if (spin != null) StopCoroutine(spin);
        spin = StartCoroutine(Spin(target));
    }

    static float ShortWay(float steps) => Mathf.Repeat(steps + SkillRing.Count * 0.5f, SkillRing.Count) - SkillRing.Count * 0.5f;

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
        asking = true;
        me.AskToPickRingStat(nodeOption[i].stat);
    }

    // The walk changed — the server's answer to a pick (it answers every pick, refused or not), or a point banked.
    void OnWalkChanged()
    {
        asking = false;
        Refresh();
    }

    // Turn the ring until `target` is at the top, the short way round, from wherever it is now.
    IEnumerator Spin(int target)
    {
        spinning = true;
        spinTarget = target;
        float from = turn, steps = ShortWay(target - turn);
        for (float e = 0; e < SlideDur; e += Time.unscaledDeltaTime)
        {
            turn = from + steps * Ease(Mathf.Clamp01(e / SlideDur));
            Place();
            yield return null;
        }
        turn = target;
        Place();
        spinning = false;
        spin = null;
        spinTarget = -1;
    }

    // Smootherstep: eases gently IN and OUT (6t⁵−15t⁴+10t³). Zero velocity at both ends → no abrupt
    // start and a soft landing, which reads far smoother than a plain ease-out on a fast turn.
    static float Ease(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
}
