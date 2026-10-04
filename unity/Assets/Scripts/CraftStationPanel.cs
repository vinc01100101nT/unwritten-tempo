using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE STATION WINDOW — what opens when you press F on a smith, a loom or a tannery. It lists what that station
/// can forge, and what you are carrying that it will work on; picking a piece shows what a fold and a quench
/// would cost and what a quench would do.
///
/// <para><b>It works nothing out.</b> Every number on it — the costs, the odds, the fold cap — arrives from the
/// server in one <see cref="CraftStationOffer"/>, and the four buttons only ask. Which stat an Ink writes, which
/// way a quench goes and what anything costs are all the server's answers.</para>
/// </summary>
public class CraftStationPanel : MonoBehaviour
{
    public static CraftStationPanel Instance { get; private set; }

    GameObject root;
    RectTransform content;
    CraftStation station = CraftStation.None;
    int picked = -1;                 // bag position of the piece being worked on, or -1
    int saltPicked = -1;             // bag position of the Salt to put in the next quench, or -1 for none
    CraftStationOffer offer;
    bool haveOffer;
    // The last thing the server said about a craft, drawn under the lists — red when it was a refusal.
    string lastResult;
    bool lastResultOk;

    // WHICH PAGE IS OPEN. The window opens on a menu of the five things a station does, and each opens its own
    // page in the same window with a Back row — one thing on screen at a time, instead of every list at once.
    enum Page { Menu, Forge, Fold, Quench, Ink, Recycle }
    Page page = Page.Menu;

    static readonly Color Sub = UiKit.TextDim;

    void Awake()
    {
        Instance = this;
        // THE QUESTS WINDOW, ITS GUIDE AND ITS ARROWS live on this same canvas; a HUD built before they existed is given
        // them here, so no shell rebuild is needed (the same move the shop window made).
        if (GetComponent<QuestsWindow>() == null) gameObject.AddComponent<QuestsWindow>();
        if (GetComponent<GuideOverlay>() == null) gameObject.AddComponent<GuideOverlay>();
        if (GetComponent<QuestArrows>() == null) gameObject.AddComponent<QuestArrows>();
    }
    void OnDestroy() { if (Instance == this) Instance = null; }

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
    }

    /// <summary>True while it is on screen.</summary>
    public static bool IsOpen => Instance != null && Instance.root != null && UiAnim.IsOpen(Instance.root);

    /// <summary>Which station's window is on screen, or None — what the tutorial guide checks before lighting a row.</summary>
    public static CraftStation OpenStation => IsOpen ? Instance.station : CraftStation.None;

    /// <summary>What the server said a craft did, or why it refused. Shown until the next one, or until the window
    /// is next opened. The chat carries the same sentence, so closing the window loses nothing.</summary>
    public static void ShowResult(string line, bool ok, bool pieceGone = false)
    {
        if (Instance == null) return;
        Instance.lastResult = line;
        Instance.lastResultOk = ok;
        // The piece it was showing no longer exists; pointing at its old position would show whatever slid in.
        if (pieceGone) { Instance.picked = -1; Instance.haveOffer = false; }
        if (IsOpen) Instance.Rebuild();
    }

    /// <summary>THE SERVER'S ANSWER about one piece. Kept and drawn; nothing here is worked out from it.</summary>
    public static void TakeOffer(CraftStationOffer o)
    {
        if (Instance == null || o.bagIndex < 0) return;
        Instance.offer = o;
        Instance.haveOffer = true;
        // THE SERVER SAYS WHERE THE PIECE IS NOW. Paying for a fold empties material stacks out of the bag, so the
        // piece can move; the offer carries the position it moved to and the window follows it.
        Instance.picked = o.bagIndex;
        // A SALT THE SERVER NO LONGER SEES IS NO LONGER CHOSEN. A quench spends the one that was in, so the position
        // remembered here can now hold anything; left as it was, the window read "Using — press to leave it out".
        if (o.saltTier <= 0) Instance.saltPicked = -1;
        Instance.Rebuild();
    }

    void Show(CraftStation which)
    {
        station = which;
        picked = -1;
        haveOffer = false;
        lastResult = null;
        page = Page.Menu;
        Build();
        if (root == null) return;
        UiAnim.Open(root);
        UISounds.PlayOpen();
        Rebuild();
    }

    void Build()
    {
        if (root != null) return;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var viewport = UiKit.Window(canvas.transform, "Station", new Vector2(420, 460), Vector2.zero, out root);
        // A LONG LIST SCROLLS. The rows are built into the same scroll column the Skills window uses, so thirty swords
        // in the bag are a wheel away rather than off the bottom of the window.
        content = UiKit.ScrollColumn(viewport);
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 5;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;
    }

    // ESCAPE, WALKING AWAY AND THE X ARE NOT DECIDED HERE. Opening a station is an interaction with a townsperson,
    // exactly as a conversation is — the player is held still, the camera frames the two of them, the townsperson
    // halts — and PlayerInteractor owns all of that for both kinds. It closes this window when the interaction ends
    // and ends the interaction when this window closes, so the two can never disagree about which is open.

    void Rebuild()
    {
        if (content == null) return;
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        RebuildLists();
        // WHAT THE LAST PRESS DID, last, whatever the lists above stopped at.
        if (!string.IsNullOrEmpty(lastResult)) Line(lastResult, 12, lastResultOk ? UiKit.TextMain : UiKit.Bad, 3);
    }

    void RebuildLists()
    {
        var c = Character.Instance;
        if (c == null) { Line("Nobody here.", 13, Sub); return; }

        if (page == Page.Menu)
        {
            Line(CraftStations.Label(station), 18, UiKit.TitleText);
            Row("Forge", () => Open(Page.Forge), GuideSpot.StationForgePage);
            Row("Fold", () => Open(Page.Fold), GuideSpot.StationFoldPage);
            Row("Quench", () => Open(Page.Quench));
            Row("Ink", () => Open(Page.Ink));
            Row("Recycle", () => Open(Page.Recycle));
            return;
        }

        Line(CraftStations.Label(station) + "  ·  " + PageWord(page), 18, UiKit.TitleText);
        Row("← Back", () => Open(Page.Menu), GuideSpot.StationBack);

        if (page == Page.Forge) { ForgePage(); return; }

        // ---- every other page starts with the pieces this station works on ---------------------------
        int workable = 0;
        for (int i = 0; i < c.Bag.Count; i++)
        {
            var e = c.Bag[i];
            if (e == null || e.instance == null || e.def == null) continue;
            if (CraftStations.For(e.def) != station) continue;
            workable++;
            int at = i;
            Row((at == picked ? "> " : "") + ItemTooltip.CopyTitle(e.instance), () => Pick(at), GuideSpot.StationPieceRow, e.def.id);
        }
        if (workable == 0) { Line("You are carrying nothing this station works on.", 12, Sub); return; }

        if (picked < 0 || !haveOffer || offer.bagIndex != picked) return;
        var inst = picked < c.Bag.Count ? c.Bag[picked].instance : null;
        if (inst == null || inst.def == null) return;

        Line(ItemTooltip.CopyTitle(inst) + "   " + offer.folds + " of " + offer.foldCap + " folds", 14, UiKit.TextMain);
        switch (page)
        {
            case Page.Fold:    FoldControls(inst); break;
            case Page.Quench:  QuenchControls(c, inst); break;
            case Page.Ink:     InkControls(c, inst); break;
            case Page.Recycle: RecycleControls(inst); break;
        }
    }

    void Open(Page which)
    {
        page = which;
        // The picked piece and its offer carry across pages: the same piece can be folded, then quenched, without
        // being picked twice. Only the result line is page-bound — it was about the last press, not this page.
        lastResult = null;
        UISounds.PlayClick();
        Rebuild();
    }

    static string PageWord(Page p)
    {
        switch (p)
        {
            case Page.Forge:   return "Forge";
            case Page.Fold:    return "Fold";
            case Page.Quench:  return "Quench";
            case Page.Ink:     return "Ink";
            case Page.Recycle: return "Recycle";
            default:           return "";
        }
    }

    // ---- what this station can make -----------------------------------------------------------------
    void ForgePage()
    {
        int forgeable = 0;
        foreach (var def in ItemCatalog.All)
        {
            if (def == null || def.forgeMaterialCost <= 0) continue;
            if (CraftStations.For(def) != station) continue;
            forgeable++;
            var d = def;
            var band1 = MaterialCatalog.Band(d.madeOf, 1);
            string forgeMaterial = band1 != null ? band1.displayName : "material";
            Row(d.displayName + "   " + d.forgeMaterialCost + " " + forgeMaterial + " · " + d.forgeGoldCost + "g",
                () => { var me = GameNetwork.MyPlayer; if (me != null) me.AskToForge(d.id); },
                GuideSpot.StationForgeRow, d.id);
        }
        if (forgeable == 0) Line("Nothing here is made of this.", 12, Sub);
    }

    string BandMaterialName()
    {
        var material = ItemCatalog.Resolve(offer.foldMaterialId);
        return material != null ? material.displayName : offer.foldMaterialId;
    }

    void FoldControls(EquipmentInstance inst)
    {
        string itemId = inst.def.id;
        if (offer.canFold)
            Row("Fold   " + offer.foldMaterialCost + " " + BandMaterialName() + " · " + offer.foldGoldCost + "g",
                () => { var me = GameNetwork.MyPlayer; if (me != null) me.AskToFold(picked, itemId); },
                GuideSpot.StationFoldButton, itemId);
        else
            Line("Folded as far as this grade allows. Quench it to go further.", 12, Sub);
    }

    void QuenchControls(Character c, EquipmentInstance inst)
    {
        string itemId = inst.def.id;
        if (offer.atTheTop) { Line("This is the best grade there is.", 12, Sub); return; }
        if (!offer.canQuench)
        {
            Line("Fold it to +" + offer.foldCap + " first — a quench spends the folds, and this one is at +"
                 + offer.folds + ".", 12, Sub, 2);
            // THE ODDS STILL SHOW while folding toward the gate: they are what the work is FOR, and the server
            // already sent them. Hiding them until the last fold would make the decision invisible until it
            // is already paid for.
            Line("When it is ready:  up " + offer.up + "%   ·   unchanged " + offer.same + "%   ·   down "
                 + offer.down + "%", 12, Sub);
            return;
        }

        Row("Quench   " + offer.quenchMaterialCost + " " + BandMaterialName() + " · " + offer.quenchGoldCost + "g",
            () => { var me = GameNetwork.MyPlayer; if (me != null) me.AskToQuench(picked, itemId, saltPicked); });
        Line("Up " + offer.up + "%   ·   unchanged " + offer.same + "%   ·   down " + offer.down + "%", 12, Sub);
        Line("A quench spends every fold, whichever way it goes.", 11, Sub);

        // THE SALT IS A CHOICE, and it is spent whatever the quench does — so it is named, and turning it off
        // is one press. The odds above already include whichever is chosen: the server sent them that way.
        int best = BestSaltInBag(c);
        if (best >= 0)
            Row(saltPicked >= 0 ? "Using " + offer.saltName + " — press to leave it out"
                                : "Add " + c.Bag[best].def.displayName + " (it is spent either way)",
                () => { saltPicked = saltPicked >= 0 ? -1 : best; AskForOffer(); });
        else Line("No Salt to steady it with.", 11, Sub);
    }

    void InkControls(Character c, EquipmentInstance inst)
    {
        string itemId = inst.def.id;
        if (offer.inkSlots <= 0) { Line("A " + (inst.quality == Quality.Raw ? "Raw" : inst.quality.ToString()) + " piece has no stat slot. Quench it first.", 12, Sub); return; }
        int ink = InkInBag(c);
        if (ink < 0) { Line("No Ink to write with.", 12, Sub); return; }

        // THE PLAYER PICKS THE SLOT: one button per stat already written (pressing it replaces that stat),
        // and one for the next empty slot while the grade still has room. The written stats are a list
        // with no holes, so "the next empty slot" is the only empty one there is.
        string inkName = c.Bag[ink].def.displayName;
        int written = inst.inkStats != null ? inst.inkStats.Count : 0;
        for (int i = 0; i < written && i < offer.inkSlots; i++)
        {
            int slot = i;
            Row("Replace " + ItemTooltip.InkStatLabel(inst.inkStats[i]) + " (slot " + (i + 1) + ") with " + inkName,
                () => { var me = GameNetwork.MyPlayer; if (me != null) me.AskToInk(picked, itemId, ink, slot); });
        }
        if (written < offer.inkSlots)
        {
            int slot = written;
            Row("Write slot " + (written + 1) + " with " + inkName,
                () => { var me = GameNetwork.MyPlayer; if (me != null) me.AskToInk(picked, itemId, ink, slot); });
        }
    }

    // The piece is destroyed, half of what it provably consumed comes back.
    void RecycleControls(EquipmentInstance inst)
    {
        string itemId = inst.def.id;
        var back = new System.Text.StringBuilder();
        if (offer.recycleIds != null && offer.recycleCounts != null)
            for (int i = 0; i < offer.recycleIds.Length && i < offer.recycleCounts.Length; i++)
            {
                var m = ItemCatalog.Resolve(offer.recycleIds[i]);
                if (m == null || offer.recycleCounts[i] <= 0) continue;
                if (back.Length > 0) back.Append(", ");
                back.Append(offer.recycleCounts[i]).Append(' ').Append(m.displayName);
            }
        if (offer.recycleGold > 0) { if (back.Length > 0) back.Append(", "); back.Append(offer.recycleGold).Append(" gold"); }
        Row("Recycle   → " + (back.Length > 0 ? back.ToString() : "nothing"),
            () => { var me = GameNetwork.MyPlayer; if (me != null) me.AskToRecycle(picked, itemId); });
        Line("Recycling destroys the piece. What comes back is half of the forge, the quenches it climbed, the folds on it "
             + "and the stats written on it — failed quenches are not counted.", 11, Sub, 2);
    }

    void Pick(int bagIndex)
    {
        picked = bagIndex;
        saltPicked = -1;
        haveOffer = false;
        AskForOffer();
    }

    // Ask the server what this piece costs and what a quench of it would do, with the Salt currently chosen.
    void AskForOffer()
    {
        var me = GameNetwork.MyPlayer;
        if (me != null && picked >= 0) me.AskForOffer(picked, saltPicked);
    }

    // The best Salt being carried, or -1 — what the "add Salt" press offers. Choosing a cheaper one is not
    // offered yet; leaving it out entirely is.
    static int BestSaltInBag(Character c)
    {
        int best = -1, bestTier = 0;
        for (int i = 0; i < c.Bag.Count; i++)
        {
            var e = c.Bag[i];
            if (e == null || e.def == null || e.def.materialKind != MaterialKind.Salt) continue;
            if (e.def.materialStep > bestTier) { bestTier = e.def.materialStep; best = i; }
        }
        return best;
    }

    // The best Ink being carried, by the same rule.
    static int InkInBag(Character c)
    {
        int best = -1, bestTier = 0;
        for (int i = 0; i < c.Bag.Count; i++)
        {
            var e = c.Bag[i];
            if (e == null || e.def == null || e.def.materialKind != MaterialKind.Ink) continue;
            if (e.def.materialStep > bestTier) { bestTier = e.def.materialStep; best = i; }
        }
        return best;
    }

    // One button line. Under this window's vertical layout a control with no stated height is given none, so
    // every button was drawn at zero height on top of the next — the 2026-09-21 screenshot. The same shape the
    // System window's entries use.
    void Row(string label, System.Action onClick, GuideSpot spot = GuideSpot.None, string itemId = "")
    {
        var btn = UiKit.Btn(label, content, onClick);
        btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
        // What the tutorial guide may point at, named for it here where the row is made (see GuideMarker).
        GuideMarker.Put(btn.gameObject, spot, itemId, station);
    }

    // One line of text, `rows` lines tall — a sentence from the server needs room to wrap.
    void Line(string s, int size, Color color, int rows = 1)
    {
        var t = UiKit.Label("Line", content, s, size, FontStyle.Normal, rows > 1 ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
        t.color = color;
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = (size + 4) * rows + 4;
    }
}
