using System.Collections.Generic;

/// <summary>One copy a character holds, as a quest step sees it.</summary>
public struct QuestHeld
{
    public string itemId;
    public int folds;
    public Quality quality;
    public bool worn;
}

/// <summary>Why the step being followed cannot be done right now — what the guide says instead of lighting a button.</summary>
public enum QuestBlock { None = 0, CannotPay = 1, TakeItOff = 2, NotCarried = 3 }

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

    /// <summary>Can a bill of <paramref name="needMaterial"/> material and <paramref name="needGold"/> gold be paid.</summary>
    public static bool CanPay(int haveMaterial, int needMaterial, int haveGold, int needGold)
        => haveMaterial >= needMaterial && haveGold >= needGold;
}
