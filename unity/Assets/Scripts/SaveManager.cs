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
///
/// <para>A character's data lives in ONE place: the account service's database, reached through
/// <see cref="ProfileStore"/>. Loading therefore takes frames rather than returning immediately, and nothing
/// loads or saves until <see cref="AccountSession"/> says who is playing — before sign-in there is no
/// character to write, which is the correct state, not a failure.</para>
/// </summary>
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [Tooltip("Seconds between background autosaves. Only writes if something actually changed.")]
    public float autosaveInterval = 20f;

    Character character;
    bool initialized;
    bool loading;      // a load is in flight — the answer comes over a network, so it spans frames
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
            // Nothing to do until someone is playing a character AND the Systems scene is up.
            if (!loading && AccountSession.IsActive && Character.Instance != null) BeginLoad();
            return;
        }

        if (dirty && Time.unscaledTime >= nextAutosave) SaveNow();
    }

    void BeginLoad()
    {
        loading   = true;
        character = Character.Instance;

        // Appearance first, from what the character list already told us, so the body is right before the world
        // is looked at. The save carries the same values and will confirm them a moment later — the list reads
        // them out of that very document, so there is still only one thing being remembered.
        ApplyAppearance();

        ProfileStore.Current.Load(data =>
        {
            if (this == null) return;   // left the game while the request was out

            Loaded = data;
            if (Loaded != null)
            {
                character.RestoreSave(Loaded);
                // The persistent HUD panels may have already built from the fresh (empty) character — they
                // only reflect saved layout when told to rebuild, so nudge the ones that mirror it. The data
                // restored correctly; this is purely the post-load visual refresh (else the hotbar reads as
                // empty until the next manual bind).
                HotbarPanel.RebuildRequested?.Invoke();
                ReturnToWhereWeLeftOff(Loaded);
            }

            // Subscribe AFTER restoring, so loading doesn't immediately mark us dirty.
            character.OnLeveledUp         += OnLeveledUp;    // biggest checkpoints
            character.OnRevived           += SaveNow;        // persist the post-death respawn
            character.OnStatsChanged      += MarkDirty;      // spent a stat/skill point, gear, etc.
            character.OnAppearanceChanged += SaveAppearance; // recoloured — goes to the character record, not the save

            nextAutosave = Time.unscaledTime + autosaveInterval;
            initialized  = true;
            loading      = false;
        });
    }

    // Wear the body this character was created with and its chosen recolour.
    void ApplyAppearance()
    {
        if (!string.IsNullOrEmpty(AccountSession.ModelName)) Player3DBody.Wear(AccountSession.ModelName);
        character.SetDye(AccountSession.DyeSources, AccountSession.DyeTargets, AccountSession.ModelName);
    }

    /// <summary>Push a recolour to the character record. Appearance is not progress, so it never waits for the
    /// autosave timer and never rides along in the save document.</summary>
    void SaveAppearance()
    {
        if (character == null) return;
        ProfileStore.Current.SaveAppearance(character.DyeModel, character.DyeSources, character.DyeTargets);
    }

    void OnLeveledUp(int newLevel) => SaveNow();

    /// <summary>Flag a pending autosave. Public so the bag (InventoryStore) can mark itself dirty on a
    /// bag-only change (give/consume/reorder) that doesn't touch Character's stat events.</summary>
    public void MarkDirty() => dirty = true;

    /// <summary>Stand the player where their save says they were. The save always arrives AFTER the map has
    /// come up, so this either moves them within the map they are already in, or sends them to the one they
    /// logged out of and lets the arrival place them.</summary>
    static void ReturnToWhereWeLeftOff(SaveData data)
    {
        if (data == null || string.IsNullOrEmpty(data.sceneName)) return;   // a save older than this, or a fresh character

        SceneTravel.ResumeScene = data.sceneName;
        SceneTravel.ResumePosition = new Vector3(data.posX, data.posY, data.posZ);

        if (data.sceneName == MapManager.CurrentMap)
        {
            if (PersistentPlayer3D.Instance != null) PersistentPlayer3D.Instance.ReturnToSavedPlace();
        }
        else
        {
            MapManager.Travel(data.sceneName, null);   // the arrival reads the resume point and clears it
        }
    }

    /// <summary>Write the live character + bag now and reset the autosave timer.</summary>
    public void SaveNow()
    {
        if (character == null) return;
        var data = character.CaptureSave();
        // The bag lives on InventoryStore (canvas-scoped). If it's up, snapshot it; if the window was
        // never opened this session, carry forward the bag we loaded so an autosave doesn't wipe it.
        if (InventoryStore.Instance != null) InventoryStore.Instance.CaptureInto(data);
        else CarryForwardBag(data);

        // Where the player is standing, so signing back in puts them there instead of at the map's front door.
        var player = PersistentPlayer3D.Instance;
        if (player != null && !string.IsNullOrEmpty(MapManager.CurrentMap))
        {
            data.sceneName = MapManager.CurrentMap;
            var p = player.transform.position;
            data.posX = p.x; data.posY = p.y; data.posZ = p.z;
        }
        else if (Loaded != null)
        {
            // No map yet (the world has not finished coming up) — carry forward what we loaded rather than
            // recording nowhere, which would silently forget the spot on the very first autosave.
            data.sceneName = Loaded.sceneName;
            data.posX = Loaded.posX; data.posY = Loaded.posY; data.posZ = Loaded.posZ;
        }
        ProfileStore.Current.Save(data);
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
        character.OnLeveledUp         -= OnLeveledUp;
        character.OnRevived           -= SaveNow;
        character.OnStatsChanged      -= MarkDirty;
        character.OnAppearanceChanged -= SaveAppearance;
    }
}
