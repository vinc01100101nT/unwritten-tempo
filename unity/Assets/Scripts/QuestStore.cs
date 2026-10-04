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

    /// <summary>Where the followed quest's step happens and whether it can be done now.</summary>
    public static QuestTargetWire Target { get; private set; }

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

    public static void Receive(QuestOpenWire[] open, QuestTargetWire target, string[] offers, string[] ongoingAt)
    {
        Open = open ?? Array.Empty<QuestOpenWire>();
        Target = target;
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
/// WHICH MARK FLOATS OVER WHICH TOWNSPERSON — read off the server's lists, never worked out. "!" over somebody who offers
/// a quest this character can accept; "?" over somebody where one of its open quests' current step happens. When both
/// are true, "?" wins: the quest already under way is the one to finish first.
/// </summary>
public static class QuestMarks
{
    public const string Offer = "!";
    public const string Ongoing = "?";

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
