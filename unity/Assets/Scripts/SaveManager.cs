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

    /// <summary>The last data read/written — the bag InventoryStore restores from, and the fallback bag we
    /// carry forward on a save taken before the bag window was ever opened this session.</summary>
    public SaveData Loaded { get; private set; }

    /// <summary>True once the save file has been read and applied — InventoryStore waits for this before
    /// deciding restore-vs-seed, so it never seeds over an existing save due to a startup race.</summary>
    public bool Initialized => initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("SaveManager") { hideFlags = HideFlags.DontSaveInEditor };
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
        // and the first checkpoint below seeds the file. Read through the profile store (local disk today,
        // a server backend later) so the source of truth is a one-line swap.
        Loaded = ProfileStore.Current.Read();
        if (Loaded != null)
        {
            character.RestoreSave(Loaded);
            // The persistent HUD panels may have already built from the fresh (empty) character earlier this
            // frame — they only reflect saved layout when told to rebuild, so nudge the ones that mirror it.
            // The data restored correctly; this is purely the post-load visual refresh (else the hotbar reads
            // as empty until the next manual bind).
            HotbarPanel.RebuildRequested?.Invoke();
            ComboBarPanel.RebuildRequested?.Invoke();
        }

        // Subscribe AFTER restoring, so loading doesn't immediately mark us dirty.
        character.OnLeveledUp       += OnLeveledUp;         // biggest checkpoints
        character.OnRevived         += SaveNow;             // persist the post-death respawn
        character.OnStatsChanged    += MarkDirty;           // spent a stat/skill point, gear, etc.
        character.OnAppearanceChanged += MarkDirty;         // recolored an outfit part

        nextAutosave = Time.unscaledTime + autosaveInterval;
        initialized  = true;
    }

    void OnLeveledUp(int newLevel) => SaveNow();

    /// <summary>Flag a pending autosave. Public so the bag (InventoryStore) can mark itself dirty on a
    /// bag-only change (give/consume/reorder) that doesn't touch Character's stat events.</summary>
    public void MarkDirty() => dirty = true;

    /// <summary>Write the live character + bag now and reset the autosave timer.</summary>
    public void SaveNow()
    {
        if (character == null) return;
        var data = character.CaptureSave();
        // The bag lives on InventoryStore (canvas-scoped). If it's up, snapshot it; if the window was
        // never opened this session, carry forward the bag we loaded so an autosave doesn't wipe it.
        if (InventoryStore.Instance != null) InventoryStore.Instance.CaptureInto(data);
        else CarryForwardBag(data);
        ProfileStore.Current.Write(data);
        Loaded = data;
        dirty = false;
        nextAutosave = Time.unscaledTime + autosaveInterval;
    }

    void CarryForwardBag(SaveData data)
    {
        if (Loaded == null) return;
        data.bagItemIds = Loaded.bagItemIds;
        data.bagCounts  = Loaded.bagCounts;
        data.bagRefines = Loaded.bagRefines;
        data.bagCards   = Loaded.bagCards;
    }

    void OnApplicationPause(bool paused) { if (paused && initialized) SaveNow(); }  // mobile/web background
    void OnApplicationQuit()             { if (initialized) SaveNow(); }            // editor Stop + standalone quit

    void OnDestroy()
    {
        if (character == null) return;
        character.OnLeveledUp       -= OnLeveledUp;
        character.OnRevived         -= SaveNow;
        character.OnStatsChanged    -= MarkDirty;
        character.OnAppearanceChanged -= MarkDirty;
    }
}
