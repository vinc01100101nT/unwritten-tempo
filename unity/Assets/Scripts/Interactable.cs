using UnityEngine;

/// <summary>
/// Marks an object you can interact with — an NPC, a sign, later a portal. The
/// player's <see cref="PlayerInteractor"/> finds the nearest one in reach and
/// calls <see cref="Interact"/> when you press the interact key. For now,
/// interacting opens a dialogue.
/// </summary>
public class Interactable : MonoBehaviour
{
    [Tooltip("Verb shown in the floating prompt — e.g. Talk / Open / Enter.")]
    public string prompt = "Talk";

    [Tooltip("Name shown above the dialogue line.")]
    public string speaker = "Villager";

    [TextArea]
    [Tooltip("Lines shown one at a time as you press the interact key.")]
    public string[] lines = { "Hello, traveler." };

    public void Interact()
    {
        if (DialogueUI.Instance != null)
            DialogueUI.Instance.Begin(speaker, lines);
    }
}
