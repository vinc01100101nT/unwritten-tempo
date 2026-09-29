/// <summary>
/// WHAT A SAVED CHARACTER SAYS, one set per moment and behaviour. The server picks one line at random and sends the text,
/// so everyone nearby reads the same words. The sets were agreed with the user on 2026-09-30; this is the only copy.
/// </summary>
public static class SavedCharacterLines
{
    /// <summary>A never-fights character, the moment it appears.</summary>
    public static readonly string[] NeverFightsAppears =
    {
        "Oh... I can see my own colours again.",
        "Thank you. I thought I'd be a smudge forever.",
        "You found me! I was lost in the ink.",
        "Is it over? Is it really me again?",
        "Thank you, traveller. Take this — it's all I have.",
        "My page... it's clean. Thank you.",
        "I remember my name now. Thank you!",
        "I was so scared. Thank you for coming.",
    };

    /// <summary>A never-fights character, as it leaves.</summary>
    public static readonly string[] NeverFightsGoodbye =
    {
        "I'm going home now. Goodbye!",
        "Keep going. There are others still lost.",
        "I'll tell everyone about you.",
        "Back to my page. Be safe!",
        "May your story be written kindly.",
        "Goodbye, and thank you again.",
    };

    /// <summary>An ally, the moment it appears.</summary>
    public static readonly string[] AllyAppears =
    {
        "I'm free! Let me help you — stay close!",
        "You saved me. My turn now!",
        "Thank you! Point me at the next one!",
        "Back to back — let's clear this page!",
        "I owe you. I'll fight for you a while.",
        "The ink's gone from my eyes. I'll cover you!",
        "Together, then! Quickly!",
        "No more smudges. Let's go!",
    };

    /// <summary>An ally, as it leaves.</summary>
    public static readonly string[] AllyGoodbye =
    {
        "That's all I can give. Thank you, friend!",
        "I have to go back now. Keep fighting!",
        "We make a good team. Farewell!",
        "My page is calling me. Good luck!",
        "Remember me when your story's written.",
        "Take care out there!",
    };

    /// <summary>A rebel who refuses the pen, the moment it appears.</summary>
    public static readonly string[] RebelRefusesThePen =
    {
        "Free at last — and no one writes me again.",
        "You carry his ink. That makes you my enemy.",
        "I watched what his fear did to the Fourteen. Never me.",
        "Cured, yes. Grateful? No.",
        "No more pages. No more pens. Draw your blade.",
        "I choose my own story now — and it starts with you falling.",
        "Leave the book alone!",
    };

    /// <summary>A rebel drawn as a villain, the moment it appears.</summary>
    public static readonly string[] RebelDrawnAsVillain =
    {
        "Ah, clean again! Now I can be properly wicked.",
        "Every story needs a villain. Here I am!",
        "Thanks for the rescue. Now run.",
        "He drew me to scare you. I'll do it right.",
        "I missed this part.",
        "A villain with free will. Aren't you lucky?",
    };

    /// <summary>A rebel left unfinished, the moment it appears.</summary>
    public static readonly string[] RebelLeftUnfinished =
    {
        "He never finished me. You won't either.",
        "Half-drawn, half-forgotten. All angry.",
        "Where was the pen when I needed it?",
        "You work for him. That's enough.",
        "I was one line away from whole.",
    };

    /// <summary>Any rebel, as it is beaten.</summary>
    public static readonly string[] RebelBeaten =
    {
        "Maybe... I was wrong about you.",
        "Tell him... I wanted to be finished.",
        "Fine. Write me, then.",
        "Not bad... for his ink.",
        "I only wanted to choose.",
        "Next page... I'll listen.",
    };

    /// <summary>The set a character of <paramref name="behaviour"/> from <paramref name="group"/> speaks from as it appears.</summary>
    public static string[] Appears(SavedBehaviour behaviour, CharacterGroup group)
    {
        if (behaviour == SavedBehaviour.NeverFights) return NeverFightsAppears;
        if (behaviour == SavedBehaviour.Ally) return AllyAppears;
        switch (SavedCharacterRules.ReasonOf(group))
        {
            case RebelReason.DrawnAsVillain: return RebelDrawnAsVillain;
            case RebelReason.LeftUnfinished: return RebelLeftUnfinished;
            default: return RebelRefusesThePen;
        }
    }

    /// <summary>The set a character of <paramref name="behaviour"/> speaks from as it leaves; empty for a rebel, which
    /// does not leave.</summary>
    public static string[] Goodbye(SavedBehaviour behaviour) =>
        behaviour == SavedBehaviour.NeverFights ? NeverFightsGoodbye
      : behaviour == SavedBehaviour.Ally ? AllyGoodbye
      : System.Array.Empty<string>();

    /// <summary>One line of <paramref name="set"/>, chosen by <paramref name="roll"/>; empty when the set is.</summary>
    public static string Pick(string[] set, int roll) =>
        set == null || set.Length == 0 ? "" : set[((roll % set.Length) + set.Length) % set.Length];
}
