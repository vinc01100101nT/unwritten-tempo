using UnityEngine;

/// <summary>
/// Drives the M1 save slice: loads your character on launch and writes it back at the moments that
/// matter (level-up, respawn, quit) plus a throttled background autosave whenever something changed.
///
/// Self-bootstrapping — a <see cref="RuntimeInitializeOnLoadMethod"/> spawns one the first time any
/// scene loads, so saving "just works" on Play with zero editor setup. It holds no scene-visible state,
/// so a tiny DontDestroyOnLoad object is fine here (the player/HUD/camera still live in the Systems
/// scene as usual).
///
/// <see cref="Character"/> may not exist yet on frame one (the Systems scene loads additively), so we
/// poll cheaply until it appears, then initialize exactly once.
/// </summary>
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [Tooltip("Seconds between background autosaves. Only writes if something actually changed.")]
    public float autosaveInterval = 20f;

    Character character;
    bool initialized;
    bool dirty;
    float nextAutosave;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("SaveManager");
        DontDestroyOnLoad(go);
        go.AddComponent<SaveManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (!initialized)
        {
            if (Character.Instance != null) Initialize();   // Systems scene is up — load now
            return;
        }

        if (dirty && Time.unscaledTime >= nextAutosave) SaveNow();
    }

    void Initialize()
    {
        character = Character.Instance;

        // Continue an existing character if there's a save; otherwise the fresh level-1 character stands,
        // and the first checkpoint below seeds the file.
        var data = SaveSystem.Read();
        if (data != null) character.RestoreSave(data);

        // Subscribe AFTER restoring, so loading doesn't immediately mark us dirty.
        character.OnLeveledUp    += OnLeveledUp;            // biggest checkpoints
        character.OnRevived      += SaveNow;               // persist the post-death respawn
        character.OnStatsChanged += MarkDirty;             // spent a stat/skill point, gear, etc.

        nextAutosave = Time.unscaledTime + autosaveInterval;
        initialized  = true;
    }

    void OnLeveledUp(int newLevel) => SaveNow();
    void MarkDirty() => dirty = true;

    /// <summary>Write the live character to disk now and reset the autosave timer.</summary>
    public void SaveNow()
    {
        if (character == null) return;
        SaveSystem.Write(character.CaptureSave());
        dirty = false;
        nextAutosave = Time.unscaledTime + autosaveInterval;
    }

    void OnApplicationPause(bool paused) { if (paused && initialized) SaveNow(); }  // mobile/web background
    void OnApplicationQuit()             { if (initialized) SaveNow(); }            // editor Stop + standalone quit

    void OnDestroy()
    {
        if (character == null) return;
        character.OnLeveledUp    -= OnLeveledUp;
        character.OnRevived      -= SaveNow;
        character.OnStatsChanged -= MarkDirty;
    }
}
