/// <summary>
/// WHAT THE SERVER SAYS A PIECE COSTS AND WHAT IT WOULD DO — the whole of what a station window draws. Every
/// number in it is finished: the window shows these and works nothing out, so what a player is quoted and what
/// the forge actually charges can never drift apart.
/// </summary>
public struct CraftStationOffer
{
    /// <summary>Which bag position this is about, so a reply that arrives late can be matched to what is shown.</summary>
    public int bagIndex;
    /// <summary>The piece's grade and folds right now.</summary>
    public int quality, folds;
    /// <summary>The most folds this grade allows, and how many stats it can carry.</summary>
    public int foldCap, inkSlots;

    /// <summary>What ONE fold costs: the material, how many, and the gold beside it.</summary>
    public string foldMaterialId;
    public int foldMaterialCost, foldGoldCost;
    /// <summary>What ONE quench costs, in the same material.</summary>
    public int quenchMaterialCost, quenchGoldCost;

    /// <summary>The three chances of the quench AS IT WOULD BE MADE, in whole percent: up a grade, unchanged,
    /// down a grade. Already includes whatever Salt the player has chosen to put in, so the window shows the odds
    /// it is about to roll rather than a different set.</summary>
    public int up, same, down;

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

    /// <summary>What recycling this piece gives back, worked out on the server: item ids and counts, parallel, and
    /// gold — half of what the piece provably consumed (<see cref="CraftingMath.RecycleYield"/>).</summary>
    public string[] recycleIds;
    public int[] recycleCounts;
    public int recycleGold;
}
