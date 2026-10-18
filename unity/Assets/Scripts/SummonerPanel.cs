using System.Collections.Generic;
using UnityEngine;

/// <summary>One boss the Elite Summoner can call, as the server sent it: the boss, the materials it costs and how many
/// of each the asker holds. Lists travel as comma-joined text so the message stays one flat record.</summary>
public struct SummonLine
{
    public string bossId;
    public string needIds;     // item ids, comma-joined
    public string needCounts;  // how many of each, comma-joined
    public string heldCounts;  // how many of each the asker holds, comma-joined
    public bool canPay;
}

/// <summary>
/// THE ELITE SUMMONER'S WINDOW — what opens when you press F on a town's Elite Summoner: every boss that can be called,
/// what it costs, and a Summon press. <b>It works nothing out</b>: the list, the counts held and whether each can be paid
/// arrive from the server, and a press only names the boss. The server checks again and decides.
/// </summary>
public class SummonerPanel : MonoBehaviour, UiListView.ISource
{
    public static SummonerPanel Instance { get; private set; }

    static readonly Vector2 Size = new Vector2(440, 480);

    GameObject root;
    UiListView view;
    string summoner;
    SummonLine[] offer;
    bool spotTaken;
    string lastResult;
    bool lastResultOk;
    bool linesOwed;

    enum Kind { Title, Note, Boss, Cost, Result }
    struct Line { public Kind kind; public int index; }
    readonly List<Line> lines = new List<Line>(64);

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Open the window, kept by <paramref name="title"/> — their name and what they are.</summary>
    public static void OpenFor(string title)
    {
        if (Instance == null) return;
        Instance.Show(title);
    }

    public static void CloseIt()
    {
        if (Instance == null || Instance.root == null) return;
        UiAnim.Close(Instance.root);
    }

    public static bool IsOpen => Instance != null && Instance.root != null && UiAnim.IsOpen(Instance.root);

    /// <summary>What the server says can be summoned here, and whether a boss is already out.</summary>
    public static void TakeOffer(SummonLine[] sent, bool taken)
    {
        if (Instance == null) return;
        Instance.offer = sent ?? new SummonLine[0];
        Instance.spotTaken = taken;
        Instance.linesOwed = true;
    }

    /// <summary>What the last press did, or why it was refused.</summary>
    public static void ShowResult(string line, bool ok)
    {
        if (Instance == null) return;
        Instance.lastResult = line;
        Instance.lastResultOk = ok;
        Instance.linesOwed = true;
    }

    void Show(string title)
    {
        summoner = title;
        lastResult = null;
        offer = null;
        Build();
        if (root == null) return;
        UiAnim.Open(root);
        UISounds.PlayOpen();
        linesOwed = true;
        view.ToTop();
        var me = GameNetwork.MyPlayer;
        if (me != null) me.AskSummonOffer();
    }

    void Build()
    {
        if (root != null) return;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var body = UiKit.Window(canvas.transform, "Summoner", Size, Vector2.zero, out root);
        view = new UiListView(body, this);
    }

    void LateUpdate()
    {
        if (view == null) return;
        if (linesOwed && IsOpen)
        {
            linesOwed = false;
            LayOut();
            view.Changed();
        }
        view.RedrawIfNeeded();
    }

    void LayOut()
    {
        lines.Clear();
        Add(Kind.Title, 0);
        if (offer == null) { Add(Kind.Note, 0); }
        else
        {
            if (spotTaken) Add(Kind.Note, 1);
            for (int i = 0; i < offer.Length; i++) { Add(Kind.Boss, i); Add(Kind.Cost, i); }
        }
        if (!string.IsNullOrEmpty(lastResult)) Add(Kind.Result, 0);
    }

    void Add(Kind kind, int index) => lines.Add(new Line { kind = kind, index = index });

    public int Count => lines.Count;

    public float HeightOf(int i)
    {
        switch (lines[i].kind)
        {
            case Kind.Title:  return UiListView.LineHeight(18);
            case Kind.Note:   return UiListView.LineHeight(12);
            case Kind.Cost:   return UiListView.LineHeight(11, 2);
            case Kind.Result: return UiListView.LineHeight(12, 2);
            default:          return UiListView.RowHeight;
        }
    }

    public void Bind(int i, UiListView.Slot slot)
    {
        var line = lines[i];
        switch (line.kind)
        {
            case Kind.Title:
                slot.ShowLine(summoner, 18, UiKit.TitleText);
                break;
            case Kind.Note:
                slot.ShowLine(line.index == 0 ? "Asking the Summoner…" : "A boss is already out here.", 12, UiKit.TextDim);
                break;
            case Kind.Boss:
            {
                var o = offer[line.index];
                var def = MonsterCatalog.ById(o.bossId);
                string name = def != null && !string.IsNullOrEmpty(def.displayName) ? def.displayName : o.bossId;
                // dim when the server said it cannot be paid, or the spot is taken — the press still asks
                slot.ShowButton("Summon " + name, !o.canPay || spotTaken);
                break;
            }
            case Kind.Cost:
                slot.ShowLine(CostText(offer[line.index]), 11, UiKit.TextDim, 2);
                break;
            case Kind.Result:
                slot.ShowLine(lastResult ?? "", 12, lastResultOk ? UiKit.TextMain : UiKit.Bad, 2);
                break;
        }
    }

    static readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(128);

    // "Abyss Maw Heart 2/1 · Mossmaw Eye 0/1" — what is held over what is needed, both the server's numbers.
    static string CostText(in SummonLine o)
    {
        sb.Clear();
        var ids = (o.needIds ?? "").Split(',');
        var needs = (o.needCounts ?? "").Split(',');
        var held = (o.heldCounts ?? "").Split(',');
        for (int k = 0; k < ids.Length; k++)
        {
            if (string.IsNullOrEmpty(ids[k])) continue;
            var item = ItemCatalog.Resolve(ids[k]);
            if (sb.Length > 0) sb.Append("  ·  ");
            sb.Append(item != null ? item.displayName : ids[k]).Append(' ')
              .Append(k < held.Length ? held[k] : "0").Append('/').Append(k < needs.Length ? needs[k] : "?");
        }
        return sb.ToString();
    }

    public void Press(int i, int which)
    {
        var line = lines[i];
        if (line.kind != Kind.Boss || offer == null || line.index >= offer.Length) return;
        var me = GameNetwork.MyPlayer;
        if (me != null) me.AskToSummon(offer[line.index].bossId);
        UISounds.PlayClick();
    }
}
