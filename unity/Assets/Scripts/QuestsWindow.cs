using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE QUESTS WINDOW (J, or the Quests button in the status box). Three tabs — Tutorial, Main Story, Side Quests — the
/// open quests of the chosen tab down the left, and the chosen quest's steps on the right.
///
/// <para><b>It works nothing out.</b> Which quests are open, which step each is on and how far a fold step has got all
/// arrive from the server (<see cref="QuestStore"/>); the titles and step lines are read from the same quest data the
/// server judges against. Choosing a quest only shows its steps. A tutorial has Play — Stop while it plays — which ASKS
/// the server to start it (its guide and its path), and shows what it still needs, as the server worked it out. Any
/// other quest has "Show path", which only ASKS to follow it — or, pressed again, to follow nothing; it is there only
/// while the server says the quest's current step happens somewhere (or while it is followed, so it can be turned off).
/// One path at a time, whatever kind of quest it belongs to.</para>
/// </summary>
public class QuestsWindow : MonoBehaviour
{
    static readonly string[] TabNames = { "Tutorial", "Main Story", "Side Quests" };
    static readonly QuestCategory[] TabCategories = { QuestCategory.Tutorial, QuestCategory.MainStory, QuestCategory.SideQuest };

    GameObject root;
    RectTransform listContent, detailContent;
    readonly List<Button> tabs = new List<Button>();
    int tab;
    string chosen;
    int drawnVersion = -1;

    public static QuestsWindow Instance { get; private set; }

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    public bool IsOpen => root != null && UiAnim.IsOpen(root);

    public void Toggle()
    {
        if (root == null) return;
        bool open = UiAnim.Toggle(root);
        if (open) { UISounds.PlayOpen(); Redraw(); } else UISounds.PlayClose();
    }

    void Start()
    {
        // A server draws nothing.
        if (!GameServer.IsServer || GameNetwork.TheWorldIsElsewhere) Build();
        else enabled = false;
    }

    void Build()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { enabled = false; return; }
        var content = UiKit.Window(canvas.transform, "Quests", new Vector2(560, 420), Vector2.zero, out root);

        var tabRow = UiKit.TabRow(content);
        for (int i = 0; i < TabNames.Length; i++)
        {
            int index = i;
            tabs.Add(UiKit.Btn(TabNames[i], tabRow, () => { tab = index; chosen = null; Redraw(); }));
        }

        var left = UiKit.Img("List", content, UiKit.SlotFill);
        var lrt = left.rectTransform;
        lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(0, 1); lrt.pivot = new Vector2(0, 1);
        lrt.offsetMin = new Vector2(0, 0); lrt.offsetMax = new Vector2(ListWidth, -34);
        listContent = Column(lrt, 2);

        var right = UiKit.Img("Detail", content, new Color(0, 0, 0, 0));
        var rrt = right.rectTransform;
        rrt.anchorMin = new Vector2(0, 0); rrt.anchorMax = new Vector2(1, 1); rrt.pivot = new Vector2(0, 1);
        rrt.offsetMin = new Vector2(ListWidth + 6, 0); rrt.offsetMax = new Vector2(0, -34);
        detailContent = Column(rrt, 4);
    }

    const float ListWidth = 230f, ShowPathWidth = 78f, PlayWidth = 52f;

    static RectTransform Column(RectTransform viewport, int spacing)
    {
        var column = UiKit.ScrollColumn(viewport);
        var vlg = column.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = spacing;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        return column;
    }

    void Update()
    {
        if (root == null) return;
        if (Hotkeys.Down(GameKeys.Quests)) Toggle();
        else if (IsOpen && Hotkeys.CancelDown())
        {
            UiAnim.Close(root);
            UISounds.PlayClose();
            Hotkeys.MarkCancelHandled();
        }
        if (IsOpen && drawnVersion != QuestStore.Version) Redraw();
    }

    void Redraw()
    {
        if (listContent == null) return;
        drawnVersion = QuestStore.Version;
        for (int i = 0; i < tabs.Count; i++) UiKit.MarkTab(tabs[i], i == tab);
        Clear(listContent);
        Clear(detailContent);

        // The open quests of this tab, as the server listed them. With nothing chosen, the followed one is shown, or
        // else the first.
        var category = TabCategories[tab];
        if (chosen == null)
            foreach (var open in QuestStore.Open)
            {
                var def = QuestCatalog.Get(open.questId);
                if (def == null || def.category != category) continue;
                if (chosen == null || open.tracked) chosen = def.id;
                if (open.tracked) break;
            }
        QuestDef shown = null;
        QuestOpenWire shownWire = default;
        foreach (var open in QuestStore.Open)
        {
            var def = QuestCatalog.Get(open.questId);
            if (def == null || def.category != category) continue;
            var line = new GameObject("Quest", typeof(RectTransform));
            line.transform.SetParent(listContent, false);
            line.AddComponent<LayoutElement>().preferredHeight = 26;
            var row = UiKit.Btn(def.title, line.transform, () => Choose(def.id));
            var rowRt = (RectTransform)row.transform;
            rowRt.anchorMin = Vector2.zero; rowRt.anchorMax = Vector2.one;
            rowRt.offsetMin = Vector2.zero;
            bool tutorial = def.category == QuestCategory.Tutorial, showPath = !tutorial && (open.tracked || open.hasPath);
            rowRt.offsetMax = new Vector2(tutorial ? -PlayWidth : showPath ? -ShowPathWidth : 0f, 0f);
            UiKit.MarkTab(row, def.id == chosen);
            // EVERY open tutorial has Play or Stop, whatever step it is on — one with nothing to lead to still has a guide.
            if (tutorial) PlayButton(line.transform, def.id, open.tracked);
            else if (showPath) ShowPathButton(line.transform, def.id, open.tracked);
            if (def.id == chosen) { shown = def; shownWire = open; }
        }
        if (listContent.childCount == 0) { Text(listContent, "No quests here yet.", 12, UiKit.TextDim); return; }
        if (shown == null) return;

        Text(detailContent, shown.title, 15, UiKit.TitleText, FontStyle.Bold);
        if (!string.IsNullOrEmpty(shown.description)) Text(detailContent, shown.description, 12, UiKit.TextMain, FontStyle.Normal, 4);
        string reward = RewardLine(shown.reward);
        if (reward != null) Text(detailContent, reward, 12, UiKit.TitleText, FontStyle.Bold, 2);
        if (shown.category == QuestCategory.Tutorial) Requirements(shown.id);
        int now = shown.IndexOf(shownWire.stepId);
        for (int i = 0; i < shown.steps.Length; i++)
        {
            var s = shown.steps[i];
            bool done = i < now, current = i == now;
            string line = (done ? "✔  " : "     ") + s.text;
            if (current && shownWire.need > 0) line += "   " + shownWire.have + " / " + shownWire.need;
            Text(detailContent, line, 12, done ? UiKit.Good : current ? UiKit.TextMain : UiKit.TextDim,
                 current ? FontStyle.Bold : FontStyle.Normal);
        }
    }

    // WHAT THIS TUTORIAL STILL NEEDS before Play starts it — each line the server's number and the server's yes or no.
    void Requirements(string questId)
    {
        bool any = false;
        foreach (var n in QuestStore.Needs)
        {
            if (n.questId != questId) continue;
            if (!any) { Text(detailContent, "Requirements", 12, UiKit.TitleText, FontStyle.Bold); any = true; }
            Text(detailContent, (n.met ? "✔  " : "     ") + NeedText(n), 12, n.met ? UiKit.Good : UiKit.Bad);
        }
        if (!any) Text(detailContent, "Requirements: none", 12, UiKit.TitleText, FontStyle.Bold);
    }

    static string NeedText(QuestNeedWire n)
    {
        switch (n.kind)
        {
            case QuestRequirementKind.Gold: return n.amount.ToString("N0") + " gold";
            case QuestRequirementKind.Level: return "Level " + n.amount;
            default:
                var item = ItemCatalog.Resolve(n.itemId);
                return n.amount + " " + (item != null ? item.displayName : n.itemId);
        }
    }

    // Play, or Stop while this tutorial plays. Play only ASKS — the server checks what it needs and says so if it is
    // short; Stop asks to follow nothing, which ends its guide and its path.
    static void PlayButton(Transform line, string questId, bool playing)
    {
        var button = UiKit.Btn(playing ? "Stop" : "Play", line, () =>
        {
            UISounds.PlayClick();
            var me = GameNetwork.MyPlayer;
            if (me == null) return;
            if (playing) me.AskToTrackQuest(""); else me.AskToPlayTutorial(questId);
        });
        var rt = (RectTransform)button.transform;
        rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(PlayWidth - 4f, 0f);
        rt.anchoredPosition = Vector2.zero;
    }

    // "Reward: 25 experience, 300 gold, 5 Minor Healing Potion" — the quest data's own numbers, as written.
    static string RewardLine(QuestDef.Reward r)
    {
        if (r == null || !r.Any) return null;
        var parts = new List<string>();
        if (r.experience > 0) parts.Add(r.experience + " experience");
        if (r.gold > 0) parts.Add(r.gold + " gold");
        if (r.items != null)
            foreach (var it in r.items)
            {
                var item = it != null ? ItemCatalog.Resolve(it.itemId) : null;
                if (item != null) parts.Add(it.count + " " + item.displayName);
            }
        return "Reward: " + string.Join(", ", parts);
    }

    void Choose(string questId)
    {
        chosen = questId;
        UISounds.PlayClick();
        Redraw();
    }

    // "Show path" and its round button, at the right end of a quest's row. At most one is on: the quest the server says
    // is followed. Pressing it ASKS for the result wanted — follow this one, or follow nothing — and the list redraws
    // from the server's answer.
    static void ShowPathButton(Transform line, string questId, bool on)
    {
        var label = UiKit.Label("ShowPath", line, "Show path", 10, FontStyle.Normal, TextAnchor.MiddleRight);
        label.color = UiKit.TextDim;
        label.raycastTarget = false;
        var lrt = label.rectTransform;
        lrt.anchorMin = new Vector2(1f, 0f); lrt.anchorMax = new Vector2(1f, 1f); lrt.pivot = new Vector2(1f, 0.5f);
        lrt.sizeDelta = new Vector2(ShowPathWidth - 26f, 0f);
        lrt.anchoredPosition = new Vector2(-24f, 0f);

        var radio = UiKit.RadioButton(line, on);
        radio.onValueChanged.AddListener(want =>
        {
            UISounds.PlayClick();
            GameNetwork.MyPlayer?.AskToTrackQuest(want ? questId : "");
        });
    }

    static void Clear(RectTransform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
    }

    static void Text(RectTransform parent, string s, int size, Color color, FontStyle style = FontStyle.Normal, int lines = 1)
    {
        var t = UiKit.Label("Line", parent, s, size, style, lines > 1 ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
        t.color = color;
        t.textWrappingMode = TMPro.TextWrappingModes.Normal;
        t.gameObject.AddComponent<LayoutElement>().preferredHeight = (size + 6) * lines;
    }
}
