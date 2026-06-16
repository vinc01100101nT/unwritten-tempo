using UnityEngine;

/// <summary>
/// Marks the game UI Canvas as the single HUD. It now lives in the persistent <c>Systems</c>
/// scene (never unloaded), so it no longer needs <c>DontDestroyOnLoad</c> or to spawn itself
/// from Resources — it's just present for the whole game. The singleton check stays as a guard:
/// if a map ever brings in its own stray copy of the Canvas, that copy destroys itself so the
/// HUD never doubles.
/// </summary>
public class PersistentUI : MonoBehaviour
{
    public static PersistentUI Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);   // a map loaded its own copy — drop it, keep the Systems one
            return;
        }
        Instance = this;
    }
}
