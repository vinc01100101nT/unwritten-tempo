/// <summary>
/// WHAT THE SERVER SAYS A PIECE COSTS AND WHAT IT WOULD DO — the whole of what a station window draws. Every
/// number in it is finished: the window shows these and works nothing out, so what a player is quoted and what
/// the station actually charges can never drift apart.
/// </summary>
public struct RecycleBatchQuote
{
    /// <summary>Which ask this answers — the window drops an answer to an ask it has since replaced.</summary>
    public int ask;
    /// <summary>What recycling every picked piece together gives back: item ids and counts, parallel, and gold.</summary>
    public string[] ids;
    public int[] counts;
    public int gold;
    /// <summary>How many pieces that is.</summary>
    public int pieces;
    /// <summary>Why the batch would be refused, as the server would say it; empty when it would go through.</summary>
    public string refusal;
}

public struct CraftStationOffer
{
    /// <summary>Which bag position this is about, so a reply that arrives late can be matched to what is shown.
    /// -1 when the piece is worn (<see cref="wornSlot"/>).</summary>
    public int bagIndex;
    /// <summary>The slot the piece is worn in (an <see cref="EquipSlotType"/>), or 0 (None) when it is in the bag.</summary>
    public int wornSlot;
    /// <summary>The piece's grade and folds right now.</summary>
    public int quality, folds;
    /// <summary>The most folds this grade allows, and how many stats it can carry.</summary>
    public int foldCap, inkSlots;

    /// <summary>What ONE fold costs: the material, how many, and the gold beside it.</summary>
    public string foldMaterialId;
    public int foldMaterialCost, foldGoldCost;
    /// <summary>True when the next fold costs nothing — a tutorial's first run gives it free.</summary>
    public bool foldFree;
    /// <summary>What ONE quench costs: the material, how many, and the gold beside it.</summary>
    public string quenchMaterialId;
    public int quenchMaterialCost, quenchGoldCost;
    /// <summary>How much of the fold's material and of the quench's material the player carries right now, counted on
    /// the server over every stack in the bag.</summary>
    public int foldMaterialHeld, quenchMaterialHeld;

    /// <summary>The chance, in whole percent, that the next fold succeeds, and the folds the piece stands at if it
    /// fails. 100 and the same folds while a fold cannot fail.</summary>
    public int foldChance, foldsIfFoldFails;

    /// <summary>The chance, in percent (up to three decimals), that the quench AS IT WOULD BE MADE goes up a grade.
    /// Already includes whatever Salt the player has chosen to put in, so the window shows the odds it is about to
    /// roll rather than a different set.</summary>
    public float upChancePercent;
    /// <summary>Where a failed quench would leave the piece: its grade and its folds.</summary>
    public int qualityIfQuenchFails, foldsIfQuenchFails;

    /// <summary>The Salt those odds include: 0 none, 1 Coarse, 2 Fine, 3 Pure.</summary>
    public int saltTier;
    /// <summary>What that Salt is called, for the window to name. Empty when none is being used.</summary>
    public string saltName;

    /// <summary>True when the piece may be quenched RIGHT NOW — below A, and folded to its grade's cap.</summary>
    public bool canQuench;
    /// <summary>True when the piece is already A, which is the top of the ladder and never quenched. Separate from
    /// <see cref="canQuench"/> so the window can tell "not folded far enough yet" from "there is nothing above
    /// this" — two refusals that read nothing alike.</summary>
    public bool atTheTop;
    /// <summary>False when the piece is already at its grade's fold cap — the point at which only a quench moves it on.</summary>
    public bool canFold;

    /// <summary>Card slots: how many the piece has, the most it can have, the chance one more lands (whole percent),
    /// and what a press costs — all the server's numbers. canSlot is false at the cap.</summary>
    public int cardSlots, slotCap, slotChance, slotMaterialCost, slotGoldCost;
    public bool canSlot;

    /// <summary>What recycling this piece gives back, worked out on the server: item ids and counts, parallel, and
    /// gold — half of what the piece provably consumed (<see cref="CraftingMath.RecycleYield"/>, <see cref="CraftingMath.RecycleInks"/>).</summary>
    public string[] recycleIds;
    public int[] recycleCounts;
    public int recycleGold;

    /// <summary>The piece's own numbers now, and after one more fold — both the finished numbers the server writes on
    /// a copy (<see cref="CraftingMath.PieceNumbers"/>), so the Fold tab can show what the press buys without working
    /// anything out. The same as now when the piece cannot be folded further.</summary>
    public CraftingMath.PieceStats pieceNow, pieceNext;

    /// <summary>THE INK THE INK TAB WRITES WITH — the strongest one carried (<see cref="CraftStations.StrongestOfKind"/>)
    /// — by name, and what it writes for each stat, in <see cref="CraftingMath.All"/>'s order. Which of them lands is
    /// rolled at the press. Empty name and no numbers when no Ink is carried.</summary>
    public string inkName;
    public float[] inkWrites;
}
