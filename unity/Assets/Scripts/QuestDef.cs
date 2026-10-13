using System;
using UnityEngine;

/// <summary>Which tab of the Quests window a quest is listed under.</summary>
public enum QuestCategory { Tutorial = 0, MainStory = 1, SideQuest = 2 }

/// <summary>What finishes a step. Talking is a moment; Have, Folded To and Wear are judged on what the character HOLDS,
/// so a step already true when it is reached finishes at once (decided by the user 2026-10-04); Kill counts the
/// creatures defeated while the step is the current one; Buy counts the items bought while the step is the current one;
/// Health is judged on the health the character has right now.</summary>
public enum QuestObjectiveKind { TalkTo = 0, Have = 1, FoldedTo = 2, Wear = 3, Kill = 4, Health = 5, Buy = 6 }

/// <summary>THE UNLOCK CONDITION — the moment a quest is given. A tutorial is listed in the Quests window from the start
/// with its unlock condition shown, and plays the moment it unlocks. None: given no way but being offered by somebody.</summary>
public enum QuestUnlock { None = 0, EnterWorld = 1, ReturnToSavePoint = 2 }

/// <summary>What a tutorial needs before Play starts it, as its owner is told it: the gold and the items its steps cost
/// (worked out from the items, never typed), and the level the quest names.</summary>
public enum QuestRequirementKind { Gold = 0, Item = 1, Level = 2 }

/// <summary>Which part of the screen a guide hint points at. The station kinds always mean the station that works the
/// step's item, and the item ones always mean the step's item — neither is typed twice.</summary>
public enum GuideSpot
{
    None = 0,
    StationFoldPage = 2,
    StationBack = 3,
    StationPieceRow = 5,
    StationFoldButton = 6,
    BagRow = 7,
    /// <summary>The health bar over your own character's head.</summary>
    OwnHealthBar = 8,
    /// <summary>"Buy 1" on the step's item, in a shop window.</summary>
    ShopBuyButton = 9,
}

/// <summary>
/// ONE QUEST, as a designer writes it: a title, the tab it sits under, and its steps in order. Each step names what
/// finishes it and, for the click guide, the parts of the screen to light on the way. A new quest is a new asset in
/// <c>Resources/Quests</c> — no code — as long as every spot it lights is already marked on the screen
/// (<see cref="GuideMarker"/>). Checked by <c>tools/check-quest-data.py</c>.
/// </summary>
[CreateAssetMenu(fileName = "Quest", menuName = "unwritten/Quest", order = 4)]
public class QuestDef : ScriptableObject
{
    [Serializable]
    public class Objective
    {
        public QuestObjectiveKind kind;
        [Tooltip("Talk To: the townsperson's id in the townsfolk list (WorldTownsfolk).")]
        public string townspersonId = "";
        [Tooltip("Have / Folded To / Wear / Buy: the item's id, e.g. 'item.sword'. Health: the item the guide points at " +
                 "in the bag to get there, e.g. 'item.minor-healing-potion'.")]
        public string itemId = "";
        [Tooltip("Folded To: how many folds. A piece of any grade above Raw counts too, because a quench spends its folds.")]
        [Min(1)] public int folds = 1;
        [Tooltip("Kill: the creature's id in the monster catalog, e.g. 'mob.plopling'.")]
        public string creatureId = "";
        [Tooltip("Kill: how many to defeat. Buy: how many to buy. Only kills or purchases made while this is the current " +
                 "step count.")]
        [Min(1)] public int count = 1;
        [Tooltip("Kill: the map the arrows lead to for this hunt, e.g. 'lst_fild01'. Empty = the nearest map, by doors, " +
                 "where the world spawn list places this creature. Either way the creature must be placed there.")]
        public string map = "";
        [Tooltip("Health: the share of their full health, in whole percent, the character must be at or above.")]
        [Range(1, 100)] public int percent = 80;
    }

    [Serializable]
    public class RewardItem
    {
        public string itemId = "";
        [Min(1)] public int count = 1;
    }

    [Serializable]
    public class Reward
    {
        [Min(0)] public int gold;
        [Tooltip("Paid through the same door a kill's experience is, so the weapon in hand earns its share of mastery too.")]
        [Min(0)] public int experience;
        public RewardItem[] items = Array.Empty<RewardItem>();

        public bool Any => gold > 0 || experience > 0 || (items != null && items.Length > 0);
    }

    [Serializable]
    public class Hint
    {
        [Tooltip("The part of the screen this hint lights. None = a sentence only, shown when nothing later in the list is on screen.")]
        public GuideSpot spot;
        [TextArea] public string text = "";
    }

    [Serializable]
    public class Step
    {
        [Tooltip("Stable id, saved with every character on this step. Never reuse or rename one in a live quest; add new ids instead.")]
        public string id = "";
        [Tooltip("The line the Quests window shows for this step.")]
        public string text = "";
        public Objective objective = new Objective();
        [Tooltip("Tutorials only. In order. The guide shows the LAST one whose spot is on screen right now.")]
        public Hint[] hints = Array.Empty<Hint>();
    }

    [Tooltip("Stable id, saved with every character. Never reuse or rename one — a renamed quest is given again.")]
    public string id = "";
    public string title = "";
    public QuestCategory category;
    [TextArea] public string description = "";
    [Tooltip("The UNLOCK CONDITION — the moment this quest is given to a character that has never had it. Enter World: " +
             "the moment they enter the world. Return To Save Point: the moment they come back from dying through " +
             "\"Return to last saved point\" (or sign in again after leaving while dead). A tutorial is followed the " +
             "moment it unlocks, taking over from the one followed until then, which is followed again when it is done; " +
             "any other quest is followed only when nothing else is. None = given only by being offered by somebody — a " +
             "quest is given one way.")]
    public QuestUnlock unlockCondition;
    [Tooltip("The townsperson who offers this quest (their id in the townsfolk list): pressing F on them offers it, and " +
             "they carry a '!' until it is accepted. Empty = nobody offers it. Never a station worker or a shopkeeper — " +
             "those open a window, not a conversation.")]
    public string offeredBy = "";
    [Tooltip("What the giver says when offering it — one conversation line per paragraph.")]
    [TextArea] public string offerText = "";
    [Tooltip("Paid once, the moment the last step is done.")]
    public Reward reward = new Reward();
    [Tooltip("Tutorials only: the character level Play needs. 0 = none. The gold and items it needs are never typed: " +
             "they are what its steps cost, worked out from the items — nothing on a character's first run, which " +
             "folds each of its pieces free, and the whole bill on a replay.")]
    [Min(0)] public int levelNeeded;
    public Step[] steps = Array.Empty<Step>();

    /// <summary>The position of <paramref name="stepId"/> in this quest, or -1.</summary>
    public int IndexOf(string stepId)
    {
        if (string.IsNullOrEmpty(stepId) || steps == null) return -1;
        for (int i = 0; i < steps.Length; i++) if (steps[i] != null && steps[i].id == stepId) return i;
        return -1;
    }
}
