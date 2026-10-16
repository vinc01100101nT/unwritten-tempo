using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// THE STATION WINDOW — what opens when you press F on a smith, a loom or a tannery. Four tabs (Fold, Quench, Ink,
/// Recycle) over the bag's own grid of what this station works on — what you carry, then what you wear, each worn piece
/// with its slot's word on its box. Picking a box lights it and fills the section under the grid with what the tab's work
/// costs and does; the one button for that work sits fixed at the bottom of the window.
///
/// <para><b>It works nothing out.</b> Every number on it — the costs, the odds, the fold cap, the piece's numbers after
/// one more fold — arrives from the server in one <see cref="CraftStationOffer"/>, and the button only asks. Which stat an
/// Ink writes, which way a quench goes and what anything costs are all the server's answers. What a press did is said
/// over your head (<see cref="OverheadNotice"/>) and in chat, not in the window.</para>
///
/// <para><b>Nothing runs per frame.</b> The window redraws when it opens, when a tab or a box is pressed, and when the
/// server answers — and the grid only re-points its thirty boxes.</para>
/// </summary>
public class CraftStationPanel : MonoBehaviour
{
    public static CraftStationPanel Instance { get; private set; }

    // The window's measures. The body is exactly the grid's width with its scroll lane, so the grid fills it edge to edge.
    const float TabHeight = 20f, Gap = 6f, DetailHeight = 132f, ActionHeight = 30f, ChipHeight = 22f, ChipGap = 3f;
    static readonly Vector2 Size = new Vector2(ItemGrid.WidthWithLane + 32f,
                                               UiKit.TitleBarHeight + 8f + TabHeight + Gap + ItemGrid.Height + 8f
                                               + DetailHeight + Gap + ActionHeight + 24f);

    GameObject root;
    RectTransform body;
    CraftStation station = CraftStation.None;
    int picked = -1;                 // bag position of the piece being worked on, or -1
    // The slot of the WORN piece being worked on, or None when the piece is in the bag (or none is picked).
    EquipSlotType pickedWorn = EquipSlotType.None;
    int saltPicked = -1;             // bag position of the Salt to put in the next quench, or -1 for none
    int inkSlotPicked = -1;          // which stat slot the next Ink writes, or -1 for "the first one offered"
    CraftStationOffer offer;
    // THE RECYCLE PAGE PICKS SEVERAL: the copies of the carried pieces ticked there, and the server's total for them.
    // Worn pieces are never ticked — one of those is still picked alone, as on every other page.
    readonly List<string> ticked = new List<string>(16);
    RecycleBatchQuote batchQuote;
    bool haveBatchQuote;
    int batchAsk;
    bool haveOffer;

    enum Page { Fold, Quench, Ink, Recycle }
    Page page = Page.Fold;

    readonly Button[] tabs = new Button[4];
    readonly ItemGrid grid = new ItemGrid();
    PieceBoxes boxes;
    TextMeshProUGUI head, lines, after;
    RectTransform chipRow;
    readonly List<Button> chips = new List<Button>(6);
    Toggle salt;
    TextMeshProUGUI saltWords;
    Button action;
    TextMeshProUGUI actionWord;
    readonly StringBuilder sb = new StringBuilder(256);

    // What the grid shows, rebuilt when the bag or what is worn changes: carried pieces first, then worn ones.
    readonly List<BagEntry> shown = new List<BagEntry>();
    readonly List<int> shownBagIndex = new List<int>();
    readonly List<EquipSlotType> shownWorn = new List<EquipSlotType>();

    static readonly string DimTag = "<color=" + UiKit.Hex(UiKit.TextDim) + ">";
    static readonly string GoodTag = "<color=" + UiKit.Hex(UiKit.Good) + ">";
    static readonly string BadTag = "<color=" + UiKit.Hex(UiKit.Bad) + ">";
    static readonly string NumTag = "<color=" + UiKit.Hex(UiKit.TitleText) + ">";

    void Awake()
    {
        Instance = this;
        boxes = new PieceBoxes(this);
        // THE QUESTS WINDOW, ITS GUIDE AND ITS ARROWS live on this same canvas; a HUD built before they existed is given
        // them here, so no shell rebuild is needed (the same move the shop window made).
        if (GetComponent<QuestsWindow>() == null) gameObject.AddComponent<QuestsWindow>();
        if (GetComponent<GuideOverlay>() == null) gameObject.AddComponent<GuideOverlay>();
        if (GetComponent<QuestArrows>() == null) gameObject.AddComponent<QuestArrows>();
        // What a first run makes free arrives with the quests, a moment after the piece the press made.
        QuestStore.Changed += OnQuestsChanged;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        QuestStore.Changed -= OnQuestsChanged;
        StopListening();
    }

    void OnQuestsChanged()
    {
        if (IsOpen) Rebuild();
    }

    // THE BAG AND WHAT IS WORN ARRIVE FROM THE SERVER after every press — a piece's new folds, grade and name, a material
    // used up — so the grid and the name redraw whenever they do, not only when the offer lands.
    bool listening;
    void StartListening()
    {
        var store = InventoryStore.Instance;
        if (listening || store == null) return;
        store.OnChanged += OnQuestsChanged;
        listening = true;
    }

    void StopListening()
    {
        if (!listening) return;
        var store = InventoryStore.Instance;
        if (store != null) store.OnChanged -= OnQuestsChanged;
        listening = false;
    }

    /// <summary>Open the window for <paramref name="which"/> station. Called when a player reaches a townsperson
    /// who works at one.</summary>
    public static void OpenFor(CraftStation which)
    {
        if (Instance == null || which == CraftStation.None) return;
        Instance.Show(which);
    }

    /// <summary>Close it, whoever asked. Walking away from the station closes it.</summary>
    public static void CloseIt()
    {
        if (Instance == null || Instance.root == null) return;
        UiAnim.Close(Instance.root);
        Instance.StopListening();
    }

    /// <summary>True while it is on screen.</summary>
    public static bool IsOpen => Instance != null && Instance.root != null && UiAnim.IsOpen(Instance.root);

    /// <summary>Which station's window is on screen, or None — what the tutorial guide checks before lighting a row.</summary>
    public static CraftStation OpenStation => IsOpen ? Instance.station : CraftStation.None;

    /// <summary>The server finished or refused a press. The sentence itself is said over the player's head and in chat
    /// (<see cref="NetPlayer"/>); the window only lets go of a piece that no longer exists, and redraws.</summary>
    public static void ShowResult(string line, bool ok, bool pieceGone = false)
    {
        if (Instance == null) return;
        // The piece it was showing no longer exists; pointing at its old position would show whatever slid in.
        if (pieceGone)
        {
            Instance.picked = -1; Instance.pickedWorn = EquipSlotType.None; Instance.haveOffer = false;
            Instance.ticked.Clear(); Instance.haveBatchQuote = false;
        }
        if (IsOpen) Instance.Rebuild();
    }

    /// <summary>THE SERVER'S TOTAL for the ticked pieces. An answer to an ask since replaced is dropped.</summary>
    public static void TakeRecycleQuote(RecycleBatchQuote q)
    {
        if (Instance == null || q.ask != Instance.batchAsk) return;
        Instance.batchQuote = q;
        Instance.haveBatchQuote = true;
        Instance.Rebuild();
    }

    /// <summary>THE SERVER'S ANSWER about one piece. Kept and drawn; nothing here is worked out from it.</summary>
    public static void TakeOffer(CraftStationOffer o)
    {
        if (Instance == null || (o.bagIndex < 0 && o.wornSlot == (int)EquipSlotType.None)) return;
        Instance.offer = o;
        Instance.haveOffer = true;
        // THE SERVER SAYS WHERE THE PIECE IS NOW. Paying for a fold empties material stacks out of the bag, so the
        // piece can move; the offer carries the position it moved to and the window follows it. A worn piece stays
        // in its slot.
        Instance.picked = o.bagIndex;
        Instance.pickedWorn = (EquipSlotType)o.wornSlot;
        // A SALT THE SERVER NO LONGER SEES IS NO LONGER CHOSEN. A quench spends the one that was in, so the position
        // remembered here can now hold anything.
        if (o.saltTier <= 0) Instance.saltPicked = -1;
        Instance.Rebuild();
    }

    void Show(CraftStation which)
    {
        station = which;
        picked = -1;
        pickedWorn = EquipSlotType.None;
        haveOffer = false;
        ticked.Clear();
        haveBatchQuote = false;
        page = Page.Fold;
        Build();
        if (root == null) return;
        GuideMarker.Put(tabs[(int)Page.Fold].gameObject, GuideSpot.StationFoldPage, "", station);
        UiAnim.Open(root);
        UISounds.PlayOpen();
        StartListening();
        grid.ToTop();
        Rebuild();
    }

    // ESCAPE, WALKING AWAY AND THE X ARE NOT DECIDED HERE. Opening a station is an interaction with a townsperson,
    // exactly as a conversation is, and PlayerInteractor owns all of that for both kinds. It closes this window when the
    // interaction ends and ends the interaction when this window closes, so the two can never disagree about which is open.

    // ---- building, once --------------------------------------------------------------------------------------------

    void Build()
    {
        if (root != null) return;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        body = UiKit.Window(canvas.transform, "Station", Size, Vector2.zero, out root);

        var row = UiKit.TabRow(body, TabHeight);
        for (int i = 0; i < tabs.Length; i++)
        {
            var p = (Page)i;
            tabs[i] = UiKit.Btn(p.ToString(), row, () => Open(p));
            var word = tabs[i].GetComponentInChildren<TMP_Text>();
            if (word != null) word.fontSize = 11;
        }

        float y = -(TabHeight + Gap);
        grid.Build(body, new Vector2(0f, y), boxes);
        y -= ItemGrid.Height + 8f;

        var rule = UiKit.Img("Rule", body, UiKit.Rule);
        rule.raycastTarget = false;
        TopLeft(rule.rectTransform, new Vector2(0f, y), new Vector2(ItemGrid.Width, 1f));
        y -= 6f;

        // THE SECTION UNDER THE GRID: a fixed height that never scrolls. Its parts stack top to bottom, each only as tall
        // as what it holds, and the tallest page (Recycle, a two-line name and three lines of fine print) fits.
        var detail = new GameObject("Detail", typeof(RectTransform)).GetComponent<RectTransform>();
        detail.SetParent(body, false);
        TopLeft(detail, new Vector2(0f, y), new Vector2(ItemGrid.Width, DetailHeight));
        var v = detail.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 3f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        detail.gameObject.AddComponent<RectMask2D>();

        head = Text("Piece", detail, 15);
        lines = Text("Lines", detail, 12);
        after = Text("After", detail, 13);

        // THE SALT: the game's own checkbox (the Settings window's), its words beside it.
        var saltRow = new GameObject("Salt", typeof(RectTransform)).GetComponent<RectTransform>();
        saltRow.SetParent(detail, false);
        saltRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
        salt = UiKit.Checkbox(saltRow, false);
        var srt = (RectTransform)salt.transform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 0.5f);
        srt.anchoredPosition = Vector2.zero;
        salt.onValueChanged.AddListener(SaltToggled);
        saltWords = UiKit.MakeText("Words", saltRow, "", 12, FontStyle.Normal, TextAnchor.MiddleLeft, null, false);
        UiKit.Fill(saltWords.rectTransform, new Vector2(26f, 0f), Vector2.zero);
        saltWords.raycastTarget = false;

        // THE INK SLOTS: one small button per slot, three to a row — six slots, the most any grade has, are two rows.
        chipRow = new GameObject("Slots", typeof(RectTransform)).GetComponent<RectTransform>();
        chipRow.SetParent(detail, false);
        var g = chipRow.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2((ItemGrid.Width - 2 * ChipGap) / 3f, ChipHeight);
        g.spacing = new Vector2(ChipGap, ChipGap);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = 3;
        chipRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 2 * ChipHeight + ChipGap;
        for (int i = 0; i < 6; i++)
        {
            int slot = i;
            var chip = UiKit.Btn("Slot" + i, chipRow, () => { inkSlotPicked = slot; UISounds.PlayClick(); Rebuild(); });
            var word = chip.GetComponentInChildren<TMP_Text>();
            if (word != null) word.fontSize = 11;
            chips.Add(chip);
        }

        // THE ONE BUTTON, fixed to the bottom whatever the section above holds. Its word is the work, nothing else:
        // every cost and number sits in the section, where long text wraps instead of running off the button.
        action = UiKit.Btn("Action", body, Press);
        var art = (RectTransform)action.transform;
        art.anchorMin = new Vector2(0f, 0f);
        art.anchorMax = new Vector2(1f, 0f);
        art.pivot = new Vector2(0.5f, 0f);
        art.offsetMin = new Vector2(0f, 0f);
        art.offsetMax = new Vector2(-(ItemGrid.LaneGap + ItemGrid.BarWidth), ActionHeight);
        actionWord = action.GetComponentInChildren<TextMeshProUGUI>();
        if (actionWord != null) actionWord.fontStyle = FontStyles.Bold;
    }

    static void TopLeft(RectTransform rt, Vector2 at, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = size;
        rt.anchoredPosition = at;
    }

    static TextMeshProUGUI Text(string name, Transform parent, float size)
    {
        var t = UiKit.MakeText(name, parent, "", size, FontStyle.Normal, TextAnchor.UpperLeft, null, true);
        t.raycastTarget = false;
        t.richText = true;
        return t;
    }

    // ---- drawing, on change only -----------------------------------------------------------------------------------

    void Open(Page which)
    {
        // Into Recycle, a piece picked in the bag becomes the first ticked one; out of it, the ticks are let go.
        if (which == Page.Recycle && page != Page.Recycle && picked >= 0 && pickedWorn == EquipSlotType.None)
        {
            var c = Character.Instance;
            string copy = c != null && picked < c.Bag.Count && c.Bag[picked].instance != null ? c.Bag[picked].instance.copyId : null;
            if (!string.IsNullOrEmpty(copy)) { ticked.Clear(); ticked.Add(copy); picked = -1; haveOffer = false; AskBatchQuote(); }
        }
        else if (which != Page.Recycle) { ticked.Clear(); haveBatchQuote = false; }
        page = which;
        inkSlotPicked = -1;
        UISounds.PlayClick();
        Rebuild();
    }

    void Rebuild()
    {
        if (root == null) return;
        for (int i = 0; i < tabs.Length; i++) UiKit.MarkTab(tabs[i], (int)page == i);
        CollectPieces();
        grid.Refresh();
        DrawDetail();
    }

    // What this station works on: every carried piece of its kind, then every worn one.
    void CollectPieces()
    {
        shown.Clear(); shownBagIndex.Clear(); shownWorn.Clear();
        var c = Character.Instance;
        if (c == null) return;
        for (int i = 0; i < c.Bag.Count; i++)
        {
            var e = c.Bag[i];
            if (e == null || e.instance == null || e.def == null || CraftStations.For(e.def) != station) continue;
            shown.Add(e); shownBagIndex.Add(i); shownWorn.Add(EquipSlotType.None);
        }
        foreach (var kv in c.Gear.Worn)
        {
            if (kv.Value == null || kv.Value.def == null || CraftStations.For(kv.Value.def) != station) continue;
            // perf-ok: a worn piece needs a bag-shaped entry for its box; made only when the window redraws, never per frame
            shown.Add(new BagEntry { def = kv.Value.def, count = 1, instance = kv.Value, worn = true, wornSlot = kv.Key });
            shownBagIndex.Add(-1); shownWorn.Add(kv.Key);
        }
        // A ticked piece no longer carried is let go, and the total asked for again.
        bool pruned = false;
        for (int t = ticked.Count - 1; t >= 0; t--)
            if (ShownBagIndexOf(ticked[t]) < 0) { ticked.RemoveAt(t); pruned = true; }
        if (pruned) AskBatchQuote();
    }

    // Where in the grid the carried piece with this copy is, or -1.
    int ShownBagIndexOf(string copyId)
    {
        for (int i = 0; i < shown.Count; i++)
            if (shownWorn[i] == EquipSlotType.None && shown[i].instance != null && shown[i].instance.copyId == copyId) return i;
        return -1;
    }

    static string CopyOf(BagEntry e) => e != null && e.instance != null ? e.instance.copyId : null;

    bool IsPicked(int i) =>
        page == Page.Recycle && ticked.Count > 0
            ? shownWorn[i] == EquipSlotType.None && ticked.Contains(CopyOf(shown[i]))
            : shownWorn[i] != EquipSlotType.None ? shownWorn[i] == pickedWorn : pickedWorn == EquipSlotType.None && shownBagIndex[i] == picked;

    void DrawDetail()
    {
        var c = Character.Instance;
        SetSaltShown(false);
        SetChipsShown(0);
        Write(after, "");
        SetAction(null);

        if (c == null) { Write(head, ""); Write(lines, DimTag + "Nobody here.</color>"); return; }
        if (shown.Count == 0)
        {
            Write(head, "");
            Write(lines, DimTag + "You are carrying nothing this station works on.</color>");
            return;
        }
        if (page == Page.Recycle && ticked.Count > 0) { DrawBatchRecycle(); return; }
        var inst = PickedPiece(c);
        if (inst == null || !haveOffer)
        {
            // Nothing picked says so; a piece picked a moment ago is waiting for the server's answer, and says nothing.
            Write(head, "");
            Write(lines, picked < 0 && pickedWorn == EquipSlotType.None ? DimTag + "Pick a piece.</color>" : "");
            return;
        }

        sb.Clear();
        sb.Append("<b>").Append(Rarities.Tag(Rarities.Of(inst.def, inst))).Append(ItemTooltip.CopyTitle(inst)).Append("</color></b>")
          .Append(" <size=11>").Append(DimTag).Append(offer.folds).Append(" of ").Append(offer.foldCap).Append(" folds</color></size>");
        Write(head, sb.ToString());

        switch (page)
        {
            case Page.Fold:    DrawFold(c, inst); break;
            case Page.Quench:  DrawQuench(c); break;
            case Page.Ink:     DrawInk(c, inst); break;
            case Page.Recycle: DrawRecycle(); break;
        }
    }

    void DrawFold(Character c, EquipmentInstance inst)
    {
        if (!offer.canFold)
        {
            Write(lines, DimTag + "Folded as far as this grade allows. Quench it to go further.</color>");
            return;
        }
        sb.Clear();
        if (offer.foldFree) sb.Append(DimTag).Append("Costs</color> <b>").Append(NumTag).Append("nothing</color></b> — your first time");
        else Costs(offer.foldMaterialCost, offer.foldMaterialId, offer.foldGoldCost);
        YouHave(c, offer.foldMaterialId, offer.foldMaterialHeld);
        // A fold that can fail says how likely it is to land and where a failure leaves the piece, both as the server sent them.
        if (offer.foldChance < 100)
            sb.Append("\n<size=13>").Append(DimTag).Append("Chance:</color> <b>").Append(NumTag).Append(offer.foldChance)
              .Append("%</color></b>  ").Append(DimTag).Append("· If it fails: back to +").Append(offer.foldsIfFoldFails)
              .Append("</color></size>");
        Write(lines, sb.ToString());

        // WHAT ONE MORE FOLD MAKES OF IT, row by row: the piece's own numbers now and after, both the server's.
        sb.Clear();
        var now = offer.pieceNow;
        var next = offer.pieceNext;
        AfterRow("ATK", now.attack, next.attack);
        AfterRow("M.ATK", now.magicAttack, next.magicAttack);
        AfterRow("DEF", now.defense, next.defense);
        AfterRow("M.DEF", now.magicDefense, next.magicDefense);
        AfterRow("Max HP", now.hp, next.hp);
        AfterRow("Max MP", now.mp, next.mp);
        AfterRow("EVA", now.evasion, next.evasion);
        AfterRow("ASPD", now.attackSpeedPercent, next.attackSpeedPercent, "%");
        Write(after, sb.ToString());
        SetAction("Fold", GuideSpot.StationFoldButton, inst.def.id);
    }

    void AfterRow(string label, float now, float next, string suffix = "")
    {
        if (Mathf.Approximately(now, 0f) && Mathf.Approximately(next, 0f)) return;
        if (sb.Length > 0) sb.Append('\n');
        bool up = next > now + 0.001f;
        sb.Append(DimTag).Append(label).Append("</color><pos=64>").Append(now.ToString("0.##")).Append(suffix)
          .Append("<pos=104>").Append(DimTag).Append("→</color><pos=124><b>").Append(up ? GoodTag : NumTag)
          .Append(next.ToString("0.##")).Append(suffix).Append("</color></b>");
    }

    void DrawQuench(Character c)
    {
        sb.Clear();
        if (offer.atTheTop) { Write(lines, DimTag + "This is the best grade there is.</color>"); return; }
        if (!offer.canQuench)
        {
            // THE ODDS STILL SHOW while folding toward the gate: they are what the work is FOR, and the server sent them.
            sb.Append(DimTag).Append("Fold it to +").Append(offer.foldCap).Append(" first — a quench spends the folds, and this one is at +")
              .Append(offer.folds).Append(".</color>\n");
            Odds();
            Write(lines, sb.ToString());
            return;
        }
        Costs(offer.quenchMaterialCost, offer.quenchMaterialId, offer.quenchGoldCost);
        YouHave(c, offer.quenchMaterialId, offer.quenchMaterialHeld);
        sb.Append('\n');
        Odds();
        Write(lines, sb.ToString());

        // THE SALT IS A CHOICE, spent whatever the quench does. Ticking it asks the server again; the odds above are the
        // ones it sends back, with that Salt in.
        int best = BestOfKind(c, MaterialKind.Salt);
        if (best >= 0)
        {
            SetSaltShown(true);
            salt.SetIsOnWithoutNotify(saltPicked >= 0);
            Write(saltWords, "Add " + (saltPicked >= 0 && !string.IsNullOrEmpty(offer.saltName) ? offer.saltName : c.Bag[best].def.displayName)
                             + " <size=11>" + DimTag + "(spent either way)</color></size>");
        }
        SetAction("Quench");
    }

    void Odds()
    {
        var failGrade = (Quality)offer.qualityIfQuenchFails;
        sb.Append("<size=13>").Append(DimTag).Append("Chance to rise:</color> <b>").Append(GoodTag)
          .Append(offer.upChancePercent.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
          .Append("%</color></b>  ").Append(DimTag).Append("· If it fails: ");
        if (offer.qualityIfQuenchFails < offer.quality)
            sb.Append("</color><b>").Append(BadTag).Append("drops to ").Append(NetPlayer.GradeWord(failGrade)).Append(" +")
              .Append(offer.foldsIfQuenchFails).Append("</color></b>");
        else
            sb.Append("stays ").Append(NetPlayer.GradeWord(failGrade)).Append(", back to +").Append(offer.foldsIfQuenchFails)
              .Append("</color>");
        sb.Append("</size>");
    }

    void DrawInk(Character c, EquipmentInstance inst)
    {
        if (offer.inkSlots <= 0)
        {
            Write(lines, DimTag + "A " + (inst.quality == Quality.Raw ? "Raw" : inst.quality.ToString())
                         + " piece has no stat slot. Quench it first.</color>");
            return;
        }
        int ink = BestOfKind(c, MaterialKind.Ink);
        if (ink < 0) { Write(lines, DimTag + "No Ink to write with.</color>"); return; }
        Write(lines, DimTag + "Using</color> <b>" + NumTag + c.Bag[ink].def.displayName + "</color></b>");

        // THE PLAYER PICKS THE SLOT: one button per stat already written (its line, as the item card writes it), and one
        // for the next empty slot while the grade still has room.
        int written = inst.inkStats != null && inst.inkValues != null ? Mathf.Min(inst.inkStats.Count, inst.inkValues.Count) : 0;
        written = Mathf.Min(written, offer.inkSlots);
        int count = written + (written < offer.inkSlots ? 1 : 0);
        if (inkSlotPicked < 0 || inkSlotPicked >= count) inkSlotPicked = count - 1;
        SetChipsShown(count);
        for (int i = 0; i < count; i++)
        {
            string word = i < written ? ItemTooltip.InkStatLine(inst.inkStats[i], inst.inkValues[i]) : "Slot " + (i + 1);
            var t = chips[i].GetComponentInChildren<TextMeshProUGUI>();
            UiKit.MarkTab(chips[i], i == inkSlotPicked);
            if (t != null) Write(t, word);
        }
        SetAction(inkSlotPicked < written ? "Replace" : "Write");
    }

    void DrawRecycle()
    {
        sb.Clear();
        GivesBack(offer.recycleIds, offer.recycleCounts, offer.recycleGold);
        RecycleFinePrint();
        Write(lines, sb.ToString());
        SetAction("Recycle");
    }

    // SEVERAL TICKED PIECES: how many, and the server's total for them — or its reason for refusing them.
    void DrawBatchRecycle()
    {
        int n = ticked.Count;
        Write(head, "<b>" + n + (n == 1 ? " piece" : " pieces") + "</b>");
        if (!haveBatchQuote) { Write(lines, ""); return; }
        sb.Clear();
        if (!string.IsNullOrEmpty(batchQuote.refusal))
        {
            sb.Append(BadTag).Append(batchQuote.refusal).Append("</color>");
            Write(lines, sb.ToString());
            return;
        }
        GivesBack(batchQuote.ids, batchQuote.counts, batchQuote.gold);
        RecycleFinePrint();
        Write(lines, sb.ToString());
        SetAction("Recycle " + n);
    }

    // "Gives back 5 Temper Dust, 420u", the amounts bold — as the server sent them.
    void GivesBack(string[] ids, int[] counts, int gold)
    {
        sb.Append(DimTag).Append("Gives back</color> ");
        int parts = 0;
        if (ids != null && counts != null)
            for (int i = 0; i < ids.Length && i < counts.Length; i++)
            {
                if (counts[i] <= 0) continue;
                if (parts++ > 0) sb.Append(", ");
                sb.Append("<b>").Append(NumTag).Append(counts[i]).Append("</color></b> ").Append(MaterialName(ids[i]));
            }
        if (gold > 0)
        {
            if (parts++ > 0) sb.Append(", ");
            sb.Append("<b>").Append(NumTag).Append(Wallet.Amount(gold)).Append("</color></b>");
        }
        if (parts == 0) sb.Append("nothing");
    }

    void RecycleFinePrint()
    {
        sb.Append("\n<size=11>").Append(BadTag).Append("Breaks the piece down.</color> ").Append(DimTag)
          .Append("You get back half the Temper Dust, Quench Oil and ").Append(Wallet.Name)
          .Append(" its folds and quenches took, and never less than ").Append(CraftingMath.RecycleLeastDust)
          .Append(" Temper Dust.</color></size>");
    }

    // "Costs 4 Temper Dust · 120u", the amounts bold.
    void Costs(int units, string materialId, int gold)
    {
        sb.Append(DimTag).Append("Costs</color> <b>").Append(NumTag).Append(units).Append("</color></b> ")
          .Append(MaterialName(materialId)).Append(" · <b>").Append(NumTag).Append(Wallet.Amount(gold)).Append("</color></b>");
    }

    // "You have 12 Temper Dust · 1,220u" on its own line under the price, both figures the server's.
    void YouHave(Character c, string materialId, int held)
    {
        sb.Append("\n<size=12>").Append(Wallet.YouHave(c != null ? c.Gold : 0, MaterialName(materialId), held)).Append("</size>");
    }

    // The name of the material a price is in, by the id the server sent.
    static string MaterialName(string id)
    {
        var material = ItemCatalog.Resolve(id);
        return material != null ? material.displayName : id;
    }

    void SetAction(string word, GuideSpot spot = GuideSpot.None, string itemId = "")
    {
        bool on = word != null;
        if (action.gameObject.activeSelf != on) action.gameObject.SetActive(on);
        if (!on) return;
        if (actionWord != null) Write(actionWord, word);
        var marker = action.GetComponent<GuideMarker>();
        if (spot != GuideSpot.None) GuideMarker.Put(action.gameObject, spot, itemId, station);
        else if (marker != null) marker.spot = GuideSpot.None;
    }

    void SetSaltShown(bool on)
    {
        var go = salt.transform.parent.gameObject;
        if (go.activeSelf != on) go.SetActive(on);
    }

    void SetChipsShown(int count)
    {
        bool any = count > 0;
        if (chipRow.gameObject.activeSelf != any) chipRow.gameObject.SetActive(any);
        for (int i = 0; i < chips.Count; i++)
            if (chips[i].gameObject.activeSelf != (i < count)) chips[i].gameObject.SetActive(i < count);
    }

    // A text write rebuilds its mesh, and an empty one hides its part so the section closes up.
    static void Write(TMP_Text t, string s)
    {
        if (t == null) return;
        if (t.text != s) t.text = s;
        bool on = !string.IsNullOrEmpty(s);
        if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
    }

    EquipmentInstance PickedPiece(Character c)
    {
        if (pickedWorn != EquipSlotType.None)
            return offer.wornSlot == (int)pickedWorn ? c.GetEquippedInstance(pickedWorn) : null;
        if (picked < 0 || offer.bagIndex != picked || picked >= c.Bag.Count) return null;
        return c.Bag[picked].instance;
    }

    // ---- the presses: every one only asks --------------------------------------------------------------------------

    void Press()
    {
        var c = Character.Instance;
        var me = GameNetwork.MyPlayer;
        if (page == Page.Recycle && ticked.Count > 0)
        {
            if (me == null || !haveBatchQuote || !string.IsNullOrEmpty(batchQuote.refusal)) return;
            UISounds.PlayClick();
            me.AskToRecycleMany(ticked.ToArray());
            return;
        }
        var inst = c != null ? PickedPiece(c) : null;
        if (me == null || inst == null) return;
        string itemId = inst.def.id;
        bool worn = pickedWorn != EquipSlotType.None;
        UISounds.PlayClick();
        switch (page)
        {
            case Page.Fold:
                if (worn) me.AskToFoldWorn(pickedWorn, itemId); else me.AskToFold(picked, itemId);
                break;
            case Page.Quench:
                if (worn) me.AskToQuenchWorn(pickedWorn, itemId, saltPicked); else me.AskToQuench(picked, itemId, saltPicked);
                break;
            case Page.Ink:
            {
                int ink = BestOfKind(c, MaterialKind.Ink);
                if (ink < 0) return;
                if (worn) me.AskToInkWorn(pickedWorn, itemId, ink, inkSlotPicked); else me.AskToInk(picked, itemId, ink, inkSlotPicked);
                break;
            }
            case Page.Recycle:
                if (worn) me.AskToRecycleWorn(pickedWorn, itemId); else me.AskToRecycle(picked, itemId);
                break;
        }
    }

    void SaltToggled(bool on)
    {
        var c = Character.Instance;
        saltPicked = on && c != null ? BestOfKind(c, MaterialKind.Salt) : -1;
        AskForOffer();
    }

    void Pick(int index)
    {
        if (index < 0 || index >= shown.Count) return;
        // ON THE RECYCLE PAGE a carried piece is ticked or unticked; a worn one is picked alone, letting the ticks go.
        if (page == Page.Recycle)
        {
            string copy = shownWorn[index] == EquipSlotType.None ? CopyOf(shown[index]) : null;
            if (!string.IsNullOrEmpty(copy))
            {
                if (!ticked.Remove(copy)) ticked.Add(copy);
                picked = -1;
                pickedWorn = EquipSlotType.None;
                haveOffer = false;
                UISounds.PlayClick();
                AskBatchQuote();
                Rebuild();
                return;
            }
            ticked.Clear();
            haveBatchQuote = false;
        }
        picked = shownBagIndex[index];
        pickedWorn = shownWorn[index];
        saltPicked = -1;
        inkSlotPicked = -1;
        haveOffer = false;
        UISounds.PlayClick();
        AskForOffer();
        Rebuild();
    }

    // Ask the server for the ticked pieces' total. Each ask is numbered, so only the newest answer is drawn.
    void AskBatchQuote()
    {
        batchAsk++;
        haveBatchQuote = false;
        var me = GameNetwork.MyPlayer;
        if (me != null && ticked.Count > 0) me.AskForRecycleQuote(batchAsk, ticked.ToArray());
    }

    // Ask the server what this piece costs and what a quench of it would do, with the Salt currently chosen.
    void AskForOffer()
    {
        var me = GameNetwork.MyPlayer;
        if (me == null) return;
        if (pickedWorn != EquipSlotType.None)
        {
            var worn = Character.Instance != null ? Character.Instance.GetEquippedInstance(pickedWorn) : null;
            if (worn != null && worn.def != null) me.AskForWornOffer(pickedWorn, worn.def.id, saltPicked);
        }
        else if (picked >= 0) me.AskForOffer(picked, saltPicked);
    }

    // The strongest Salt or Ink being carried, or -1 — what a quench puts in and what an Ink press writes with.
    static int BestOfKind(Character c, MaterialKind kind)
    {
        int best = -1, bestTier = 0;
        for (int i = 0; i < c.Bag.Count; i++)
        {
            var e = c.Bag[i];
            if (e == null || e.def == null || e.def.materialKind != kind) continue;
            if (e.def.materialStep > bestTier) { bestTier = e.def.materialStep; best = i; }
        }
        return best;
    }

    // THE GRID'S BOXES: the pieces this station works on, the picked one lit, a worn one with its slot's word on top.
    sealed class PieceBoxes : IItemBoxOwner
    {
        readonly CraftStationPanel panel;
        public PieceBoxes(CraftStationPanel owner) { panel = owner; }

        public int BoxCount => ItemGrid.Columns * ItemGrid.Rows > panel.shown.Count
            ? ItemGrid.Columns * ItemGrid.Rows
            : (panel.shown.Count + ItemGrid.Columns - 1) / ItemGrid.Columns * ItemGrid.Columns;

        public void Bind(int index, ItemBox box)
        {
            if (index >= panel.shown.Count) { box.ShowEmpty(); box.SetCorner("", UiKit.TextDim); return; }
            var e = panel.shown[index];
            var slot = panel.shownWorn[index];
            box.Show(e, slot != EquipSlotType.None ? ItemBoxSource.Worn : ItemBoxSource.Bag);
            box.SetCorner(slot != EquipSlotType.None ? EquipSlots.Label(slot) : "", UiKit.TextDim);
            box.Light(panel.IsPicked(index));
            GuideMarker.Put(box.gameObject, GuideSpot.StationPieceRow, e.def.id, panel.station);
        }

        public void Clicked(ItemBox box, PointerEventData e)
        {
            if (box.Entry == null || e.button != PointerEventData.InputButton.Left || box.index < 0) return;
            if (Hotkeys.ShiftHeld) { ChatController.InsertItemLink(box.Entry); return; }
            panel.Pick(box.index);
        }

        // Nothing is dropped into a station: it works on what you already carry and wear.
        public bool Dropped(ItemBox onto, ItemSlot from) => false;
    }
}
