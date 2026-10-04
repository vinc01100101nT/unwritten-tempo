using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE QUESTS WINDOW (J, or the Quests button in the status box). Three tabs — Tutorial, Main Story, Side Quests — the
/// open quests of the chosen tab down the left, and the chosen quest's steps on the right.
///
/// <para><b>It works nothing out.</b> Which quests are open, which step each is on and how far a fold step has got all
/// arrive from the server (<see cref="QuestStore"/>); the titles and step lines are read from the same quest data the
/// server judges against. Choosing a quest only ASKS to follow it — the ground arrows then lead to its step.</para>
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
        var content = UiKit.Window(canvas.transform, "Quests", new Vector2(520, 420), Vector2.zero, out root);

        var tabRow = UiKit.TabRow(content);
        for (int i = 0; i < TabNames.Length; i++)
        {
            int index = i;
            tabs.Add(UiKit.Btn(TabNames[i], tabRow, () => { tab = index; chosen = null; Redraw(); }));
        }

        var left = UiKit.Img("List", content, UiKit.SlotFill);
        var lrt = left.rectTransform;
        lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(0, 1); lrt.pivot = new Vector2(0, 1);
        lrt.offsetMin = new Vector2(0, 0); lrt.offsetMax = new Vector2(190, -34);
        listContent = Column(lrt, 2);

        var right = UiKit.Img("Detail", content, new Color(0, 0, 0, 0));
        var rrt = right.rectTransform;
        rrt.anchorMin = new Vector2(0, 0); rrt.anchorMax = new Vector2(1, 1); rrt.pivot = new Vector2(0, 1);
        rrt.offsetMin = new Vector2(196, 0); rrt.offsetMax = new Vector2(0, -34);
        detailContent = Column(rrt, 4);
    }

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

        // The open quests of this tab, as the server listed them. The followed one is chosen when nothing else is.
        var category = TabCategories[tab];
        QuestDef shown = null;
        QuestOpenWire shownWire = default;
        foreach (var open in QuestStore.Open)
        {
            var def = QuestCatalog.Get(open.questId);
            if (def == null || def.category != category) continue;
            if (chosen == null && open.tracked) chosen = def.id;
            var row = UiKit.Btn(def.title, listContent, () => Choose(def.id));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 26;
            UiKit.MarkTab(row, def.id == chosen);
            if (def.id == chosen) { shown = def; shownWire = open; }
        }
        if (listContent.childCount == 0) { Text(listContent, "No quests here yet.", 12, UiKit.TextDim); return; }
        if (shown == null) return;

        Text(detailContent, shown.title, 15, UiKit.TitleText, FontStyle.Bold);
        if (!string.IsNullOrEmpty(shown.description)) Text(detailContent, shown.description, 12, UiKit.TextMain, FontStyle.Normal, 4);
        string reward = RewardLine(shown.reward);
        if (reward != null) Text(detailContent, reward, 12, UiKit.TitleText, FontStyle.Bold, 2);
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
        // ONLY ASKED. The server decides whether it may be followed, and the arrows follow its answer.
        GameNetwork.MyPlayer?.AskToTrackQuest(questId);
        Redraw();
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
