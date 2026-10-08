using System.Collections.Generic;

/// <summary>One copy a character holds, as a quest step sees it.</summary>
public struct QuestHeld
{
    public string itemId;
    public int folds;
    public Quality quality;
    public bool worn;
    /// <summary>Which copy this is — what tells a piece made during a replay from one held before it.</summary>
    public string copyId;
}

/// <summary>Why the step being followed cannot be done right now — what the guide says instead of lighting a button.
/// NoneLeft: a Health step's item, which is used up, is not in the bag.</summary>
public enum QuestBlock { None = 0, CannotPay = 1, TakeItOff = 2, NotCarried = 3, NoneLeft = 4 }

/// <summary>
/// THE SUMS BEHIND A QUEST STEP — plain functions over what a character holds, so each can be checked by calling it
/// with numbers. Server only: a player's machine is told the answers.
/// </summary>
public static class QuestMath
{
    /// <summary>Is a held-things step finished? Have counts the bag and what is worn; Folded To counts any copy with
    /// that many folds, or of any grade above Raw (a quench spends the folds it was given); Wear counts only what is
    /// worn. A talk step is a moment, never a holding, so it is never met here.</summary>
    public static bool Met(QuestObjectiveKind kind, string itemId, int folds, List<QuestHeld> held)
    {
        if (kind == QuestObjectiveKind.TalkTo || string.IsNullOrEmpty(itemId) || held == null) return false;
        for (int i = 0; i < held.Count; i++)
        {
            var h = held[i];
            if (h.itemId != itemId) continue;
            switch (kind)
            {
                case QuestObjectiveKind.Have: return true;
                case QuestObjectiveKind.FoldedTo: if (h.folds >= folds || h.quality > Quality.Raw) return true; break;
                case QuestObjectiveKind.Wear: if (h.worn) return true; break;
            }
        }
        return false;
    }

    /// <summary>The most folds on any copy of <paramref name="itemId"/> held, for "1 / 2" beside a fold step.</summary>
    public static int BestFolds(string itemId, List<QuestHeld> held)
    {
        int best = 0;
        if (held == null) return 0;
        for (int i = 0; i < held.Count; i++)
            if (held[i].itemId == itemId && held[i].folds > best) best = held[i].folds;
        return best;
    }

    /// <summary>Is there a copy in the BAG (not worn) — the only place a station works on a piece.</summary>
    public static bool InBag(string itemId, List<QuestHeld> held)
    {
        if (held == null) return false;
        for (int i = 0; i < held.Count; i++) if (held[i].itemId == itemId && !held[i].worn) return true;
        return false;
    }

    /// <summary>Add one piece a quest has you forge and fold to its bill: the forge's material and gold once, then each
    /// fold's.</summary>
    public static void AddForgedPiece(int forgeMaterial, int forgeGold, int folds, int foldMaterialEach, int foldGoldEach,
                                      ref int material, ref int gold)
    {
        material += forgeMaterial + folds * foldMaterialEach;
        gold += forgeGold + folds * foldGoldEach;
    }

    /// <summary>Is a Health step met: <paramref name="current"/> of <paramref name="max"/> health is at least
    /// <paramref name="percent"/> percent of it. Whole-number maths, so 80% of 76 is met at 61, never at 60.8.</summary>
    public static bool HealthMet(int current, int max, int percent)
        => max > 0 && (long)current * 100 >= (long)percent * max;

    /// <summary>The least health that meets a Health step of <paramref name="percent"/> percent of <paramref name="max"/>
    /// — what the Quests window shows beside the step.</summary>
    public static int HealthNeeded(int max, int percent)
        => max <= 0 ? 0 : (int)(((long)percent * max + 99) / 100);

    /// <summary>How many uses of something that heals <paramref name="healPerUse"/> it takes to bring
    /// <paramref name="hp"/> of <paramref name="max"/> up to <paramref name="percent"/> percent — 0 when already there,
    /// or when it heals nothing.</summary>
    public static int UsesToReach(int hp, int max, int percent, int healPerUse)
    {
        int missing = HealthNeeded(max, percent) - hp;
        if (missing <= 0 || healPerUse <= 0) return 0;
        return (missing + healPerUse - 1) / healPerUse;
    }

    /// <summary>What a Buy step still costs: <paramref name="count"/> to buy at <paramref name="price"/> each, less the
    /// <paramref name="bought"/> already bought for it and the <paramref name="free"/> a first run still gives.</summary>
    public static int BuyBill(int price, int count, int bought, int free)
        => price * System.Math.Max(0, count - bought - free);

    /// <summary>Add a Buy step's <see cref="BuyBill"/> to a tutorial's running gold bill.</summary>
    public static void AddBought(int price, int count, int bought, int free, ref int gold)
        => gold += BuyBill(price, count, bought, free);

    /// <summary>What one use of an item heals: the sum of its Heal effects' written amounts, rounded, never below 0.</summary>
    public static int HealOfOneUse(List<float> heals)
    {
        float total = 0f;
        if (heals != null) foreach (var h in heals) total += h;
        return System.Math.Max(0, (int)System.Math.Round(total, System.MidpointRounding.AwayFromZero));
    }

    /// <summary>Can a bill of <paramref name="needMaterial"/> material and <paramref name="needGold"/> gold be paid.</summary>
    public static bool CanPay(int haveMaterial, int needMaterial, int haveGold, int needGold)
        => haveMaterial >= needMaterial && haveGold >= needGold;
}
