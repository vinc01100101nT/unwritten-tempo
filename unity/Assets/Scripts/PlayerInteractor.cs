using UnityEngine;

/// <summary>
/// Lets the player interact with the nearest <see cref="Interactable"/> within
/// reach by pressing the interact key (default E). Every frame it also parks the
/// shared <see cref="InteractPrompt"/> over that nearest target. While a dialogue
/// is open, the key advances it and the prompt hides.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [Tooltip("How close (world units) you must be to interact.")]
    public float reach = 1.2f;

    public KeyCode interactKey = KeyCode.E;

    void Update()
    {
        // While a conversation is open: the key advances it, and the prompt hides.
        if (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen)
        {
            if (InteractPrompt.Instance != null) InteractPrompt.Instance.Hide();
            if (Input.GetKeyDown(interactKey)) DialogueUI.Instance.Advance();
            return;
        }

        var target = FindNearest();

        // Live prompt floating over the nearest interactable.
        if (InteractPrompt.Instance != null)
        {
            if (target != null)
                InteractPrompt.Instance.ShowAbove(target.transform, $"▽ {target.prompt} · {interactKey}");
            else
                InteractPrompt.Instance.Hide();
        }

        if (target != null && Input.GetKeyDown(interactKey))
            target.Interact();
    }

    Interactable FindNearest()
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, reach);
        Interactable best = null;
        float bestSqr = float.MaxValue;
        foreach (var h in hits)
        {
            var it = h.GetComponent<Interactable>();
            if (it == null) continue;
            float sqr = ((Vector2)(it.transform.position - transform.position)).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = it; }
        }
        return best;
    }
}
