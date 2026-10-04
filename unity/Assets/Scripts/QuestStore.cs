using System;
using UnityEngine;

/// <summary>
/// A PLAYER'S MACHINE'S COPY OF ITS OWN QUESTS — exactly what the server last said, nothing worked out. Static, so a
/// list that arrives before any window exists is still here when one opens: nothing is ever dropped for arriving early.
/// </summary>
public static class QuestStore
{
    /// <summary>The open quests, as the server sent them.</summary>
    public static QuestOpenWire[] Open { get; private set; } = Array.Empty<QuestOpenWire>();

    /// <summary>Where the followed quest's step happens — what the arrows and the maps' route lead to.</summary>
    public static QuestTargetWire Target { get; private set; }

    /// <summary>The quest the guide teaches, its step and whether it can be done now — the playing tutorial, or
    /// nothing. The server's answer.</summary>
    public static QuestTargetWire Guide { get; private set; }

    /// <summary>What each open tutorial still needs before Play starts it, and whether the character has it — the
    /// server's finished numbers.</summary>
    public static QuestNeedWire[] Needs { get; private set; } = Array.Empty<QuestNeedWire>();

    /// <summary>Quests somebody offers that this character can accept, by id — the server's answer.</summary>
    public static string[] Offers { get; private set; } = Array.Empty<string>();

    /// <summary>The townspeople where one of this character's open quests' current step happens, by id.</summary>
    public static string[] OngoingAt { get; private set; } = Array.Empty<string>();

    /// <summary>Goes up on every arrival; a window redraws when it moved.</summary>
    public static int Version { get; private set; }

    public static event Action Changed;

    // THE GUIDE WAITS FOR THE SERVER after its lit spot is pressed, so a quick second press cannot spend the starter
    // kit twice (two forges, two folds) before the answer comes back. Cleared by the next list, or after a moment in
    // case the press changed nothing the server needed to say.
    static float awaitingUntil;
    const float LongestWait = 2f;

    public static bool AwaitingAnswer => Time.unscaledTime < awaitingUntil;

    public static void PressedLitSpot() => awaitingUntil = Time.unscaledTime + LongestWait;

    public static void Receive(QuestOpenWire[] open, QuestTargetWire target, QuestTargetWire guide, QuestNeedWire[] needs,
                               string[] offers, string[] ongoingAt)
    {
        Open = open ?? Array.Empty<QuestOpenWire>();
        Target = target;
        Guide = guide;
        Needs = needs ?? Array.Empty<QuestNeedWire>();
        Offers = offers ?? Array.Empty<string>();
        OngoingAt = ongoingAt ?? Array.Empty<string>();
        awaitingUntil = 0f;
        Version++;
        Changed?.Invoke();
    }

    /// <summary>The open quest with this id, if the server listed it.</summary>
    public static bool TryGet(string questId, out QuestOpenWire found)
    {
        foreach (var q in Open) if (q.questId == questId) { found = q; return true; }
        found = default;
        return false;
    }
}

/// <summary>
/// A KILL THAT COUNTED, said over the player's head in the same stacking lines as "No MP": the creature's name and the
/// step's count, exactly as the server sent them — "Plopling 3 / 10". One line per quest per message.
/// </summary>
public static class QuestKillNotice
{
    public static void Show(QuestKillWire[] kills)
    {
        if (kills == null) return;
        foreach (var k in kills)
        {
            var creature = MonsterCatalog.Get(k.creatureId);
            string name = creature != null && !string.IsNullOrEmpty(creature.displayName) ? creature.displayName : k.creatureId;
            BattleNotice.Warn(name + " " + k.have + " / " + k.need, QuestMarks.Color);
        }
    }
}

/// <summary>
/// WHICH MARK FLOATS OVER WHICH TOWNSPERSON — read off the server's lists, never worked out. "!" over somebody who offers
/// a quest this character can accept; "?" over somebody where one of its open quests' current step happens. When both
/// are true, "?" wins: the quest already under way is the one to finish first.
/// </summary>
public static class QuestMarks
{
    public const string Offer = "!";
    public const string Ongoing = "?";

    /// <summary>The colour of everything that is about a quest on the world: the marks over townspeople and the kill
    /// counts over the player. The same yellow the health bar passes through — it reads as "look here", in a colour
    /// the plate already uses.</summary>
    public static readonly Color Color = new Color(0.95f, 0.80f, 0.20f);

    static readonly System.Collections.Generic.Dictionary<string, string> marks =
        new System.Collections.Generic.Dictionary<string, string>();
    static int builtFor = -1;

    /// <summary>The mark over the townsperson with this id, or empty.</summary>
    public static string For(string personId)
    {
        if (string.IsNullOrEmpty(personId)) return "";
        if (builtFor != QuestStore.Version) Rebuild();
        return marks.TryGetValue(personId, out var m) ? m : "";
    }

    static void Rebuild()
    {
        builtFor = QuestStore.Version;
        marks.Clear();
        foreach (var questId in QuestStore.Offers)
        {
            var def = QuestCatalog.Get(questId);
            if (def != null && !string.IsNullOrEmpty(def.offeredBy)) marks[def.offeredBy] = Offer;
        }
        foreach (var personId in QuestStore.OngoingAt)
            if (!string.IsNullOrEmpty(personId)) marks[personId] = Ongoing;
    }
}
