using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Phase G Skills (K) panel — the Trio-Ring passive draft, presented as <b>bankable
/// points</b> (doc §11 #4). Lives on the persistent <c>GameUICanvas</c> and drives the
/// panel <see cref="UIShellBuilder"/> built as an empty placeholder ("SkillsPanel/Content").
///
/// It builds its whole UI at runtime (like <see cref="DeathScreen"/>), so the only editor
/// change needed is one <c>AddComponent</c> in the shell builder. Each level-up banks a
/// point (<see cref="Character.AddXP"/>); the player opens K when they like and spends each
/// point on a 4-card draft (3-known Trio + 1 wildcard; 10 stats at the level-1 opening).
/// </summary>
public class SkillDraftPanel : MonoBehaviour
{
    RectTransform content;          // the panel's "Content" node we build into
    Font font;
    DraftService.Draft? pending;    // the draft currently shown (cached so the wildcard is stable between renders)
    bool bound;

    static readonly Color CardBg     = new Color(0.16f, 0.16f, 0.20f, 0.95f);
    static readonly Color LeaderEdge = new Color(1f, 0.92f, 0.5f);     // gold = the center "stack" card
    static readonly Color WildEdge   = new Color(0.85f, 0.55f, 1f);    // purple = the gamble
    static readonly Color RowDim     = new Color(1f, 1f, 1f, 0.5f);

    void Start() => TryBind();
    void Update() { if (!bound) TryBind(); }   // Character/UI may not be ready on the very first frame

    void TryBind()
    {
        if (Character.Instance == null) return;
        content = FindContent();
        if (content == null) return;

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var vlg = content.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        Character.Instance.Passives.OnChanged += Render;
        Character.Instance.OnLeveledUp += ShowLevelToast;
        bound = true;
        Render();
    }

    // The panel the shell builder created is named "SkillsPanel" with a "Content" child;
    // it starts inactive (PanelToggle shows it on K), so search includes inactive objects.
    RectTransform FindContent()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "SkillsPanel")
                return t.Find("Content") as RectTransform;
        return null;
    }

    void OnDestroy()
    {
        if (Character.Instance == null) return;
        Character.Instance.Passives.OnChanged -= Render;
        Character.Instance.OnLeveledUp -= ShowLevelToast;
    }

    // -------------------------------------------------------------------------

    void Render()
    {
        if (content == null) return;
        var p = Character.Instance.Passives;

        // Cache the draft so re-renders (e.g. banking another point) don't re-roll the
        // wildcard; rebuild only when there's a point to spend and no draft is showing.
        if (p.BankedPoints > 0) { if (pending == null) pending = DraftService.BuildDraft(p); }
        else pending = null;

        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        // Header — banked points.
        var header = NewText(content, p.BankedPoints > 0
                ? $"<b>{p.BankedPoints}</b> skill point{(p.BankedPoints == 1 ? "" : "s")} to spend"
                : "No points to spend — level up to earn one.",
            17, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetHeight(header.gameObject, 26);

        // Draft cards.
        if (pending.HasValue)
        {
            var draft = pending.Value;
            NewLabel(content, draft.isOpening ? "Choose your starting passive:" : "Choose one:");
            BuildCardRow(draft);
        }

        // Divider + the ring view (current ranks / %s in ring order).
        SetHeight(NewImage(content, new Color(1, 1, 1, 0.12f)).gameObject, 2);
        NewLabel(content, "Your ring");
        BuildRingView(p);
    }

    void BuildCardRow(DraftService.Draft draft)
    {
        var rowGO = NewImage(content, new Color(0, 0, 0, 0)).gameObject;
        int cols = draft.isOpening ? 5 : draft.options.Count;   // opening: 10 stats wrap to 5×2
        SetHeight(rowGO, draft.isOpening ? 132 : 84);
        var grid = rowGO.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = cols;
        grid.spacing = new Vector2(6, 6);
        grid.cellSize = new Vector2(draft.isOpening ? 86 : 108, 60);
        grid.childAlignment = TextAnchor.MiddleCenter;

        foreach (var opt in draft.options)
            BuildCard(rowGO.transform, opt);
    }

    void BuildCard(Transform parent, DraftService.Option opt)
    {
        var p = Character.Instance.Passives;
        var card = NewImage(parent, CardBg);
        card.raycastTarget = true;

        // A colored edge tells the player the card's role: gold = stack the leader,
        // purple = the across-the-ring wildcard, else the stat's ring-region tint.
        var edge = NewImage(card.transform,
            opt.isLeader ? LeaderEdge : opt.isWildcard ? WildEdge : SkillRing.TintFor(opt.stat));
        var ert = edge.rectTransform;
        ert.anchorMin = Vector2.zero; ert.anchorMax = Vector2.one;
        ert.offsetMin = Vector2.zero; ert.offsetMax = Vector2.zero;
        // inset the dark face so the edge reads as a 2px border
        var face = NewImage(edge.transform, CardBg);
        var frt = face.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(2, 2); frt.offsetMax = new Vector2(-2, -2);
        face.raycastTarget = false;

        int cur = p.RanksIn(opt.stat);
        float next = SkillRing.Percent(opt.stat, cur + 1);   // the total this stat would reach after the pick
        string tag = opt.isLeader ? "<color=#FFE680>STACK</color>"
                   : opt.isWildcard ? "<color=#D98CFF>WILD</color>"
                   : "";
        var label = NewText(face.transform,
            $"<b>{SkillRing.ShortName(opt.stat)}</b>\n+{Mathf.RoundToInt(next * 100)}%\n{tag}",
            14, FontStyle.Normal, TextAnchor.MiddleCenter);
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        label.raycastTarget = false;

        var btn = card.gameObject.AddComponent<Button>();
        btn.targetGraphic = card;
        var stat = opt.stat;
        btn.onClick.AddListener(() =>
        {
            pending = null;                 // force a fresh draft (new wildcard) for the next point
            Character.Instance.Passives.Pick(stat);   // fires OnChanged → Render()
            UISounds.PlayClick();
        });
    }

    void BuildRingView(PassiveRanks p)
    {
        foreach (var stat in SkillRing.Ring)
        {
            int r = p.RanksIn(stat);
            bool isLeader = p.HasLeader && stat == p.Leader;
            bool inTrio = p.HasLeader && SkillRing.InTrio(p.Leader, stat);

            var row = NewText(content,
                $"{(isLeader ? "▶ " : "  ")}{SkillRing.ShortName(stat),-6}  ×{r,-2}   +{Mathf.RoundToInt(p.Percent(stat) * 100)}%",
                15, isLeader ? FontStyle.Bold : FontStyle.Normal, TextAnchor.MiddleLeft);
            row.supportRichText = false;
            row.color = isLeader ? LeaderEdge : inTrio ? Color.white : RowDim;
            SetHeight(row.gameObject, 18);
        }
    }

    // ---- level-up toast -------------------------------------------------------

    void ShowLevelToast(int level)
    {
        StopAllCoroutines();
        StartCoroutine(ToastRoutine(level));
    }

    IEnumerator ToastRoutine(int level)
    {
        var go = new GameObject("LevelToast", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.78f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(520, 40);
        var t = go.GetComponent<Text>();
        t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 22; t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.text = $"LEVEL {level}!  +1 skill point  —  press K";
        t.raycastTarget = false;
        var sh = go.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.8f); sh.effectDistance = new Vector2(1, -1);

        const float hold = 1.4f, fade = 0.8f;
        var gold = new Color(1f, 0.92f, 0.5f);
        for (float e = 0; e < hold + fade; e += Time.deltaTime)
        {
            float a = e < hold ? 1f : 1f - (e - hold) / fade;
            t.color = new Color(gold.r, gold.g, gold.b, a);
            rt.anchoredPosition = new Vector2(0, Mathf.Lerp(0, 18, e / (hold + fade)));
            yield return null;
        }
        Destroy(go);
    }

    // ---- tiny UI helpers ------------------------------------------------------

    Text NewLabel(Transform parent, string str)
    {
        var t = NewText(parent, str, 14, FontStyle.Italic, TextAnchor.MiddleCenter);
        t.color = new Color(1, 1, 1, 0.75f);
        SetHeight(t.gameObject, 20);
        return t;
    }

    Text NewText(Transform parent, string str, int size, FontStyle style, TextAnchor anchor)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size; t.fontStyle = style; t.alignment = anchor;
        t.color = Color.white; t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = str;
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
