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

    Character character;
    bool initialized;
    bool loading;      // a load is in flight — the answer comes over a network, so it spans frames
    bool bodyToWear;   // the chosen body is waiting for the player to be switched on

    /// <summary>The last data read or written. Kept so the resume point and anything else that wants to know
    /// what the save SAID can ask, without going back to the network for it.</summary>
    public SaveData Loaded { get; private set; }

    /// <summary>True once the save has been read and applied to the character. Goes false again if the
    /// character it was applied to is replaced, which is what a lost connection does.</summary>
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
        // THE PERSON WE WERE SAVING IS GONE. Losing the connection puts the player back at the front door by
        // reloading the whole systems layer, which destroys the character and builds a fresh one — and this
        // object outlives that, so without noticing it would go on holding a destroyed character and never read
        // anybody's save again. Everything the character owns would then be blank for the rest of the session:
        // their level, their belongings, their money. Noticing here re-runs the load against the new one.
        if (initialized && (character == null || character != Character.Instance))
        {
            initialized = false;
            loading = false;
            character = null;
            Loaded = null;
            // AND WHERE THEY WERE GOING BACK TO goes with them, or the next character would be stood on that spot.
            if (walkBackOwed != null && string.IsNullOrEmpty(walkBackOwed.arriveAtSpawnId)) SceneTravel.ForgetResume();
            walkBackOwed = null;
            return;
        }

        // THE CHOSEN BODY GOES ON ONCE THERE IS A PLAYER TO WEAR IT. The save is read the moment a character is
        // chosen, while the persistent layer is still switched off waiting for the server to let them in — and a
        // body put on then found no player to dress, so everybody entered wearing the default body. Reading the
        // save later instead is not the answer: that read takes the character over, so it would throw out the
        // connection that had just been let in.
        if (bodyToWear && character != null && character.isActiveAndEnabled)
        {
            bodyToWear = false;
            if (!string.IsNullOrEmpty(AccountSession.ModelName)) Player3DBody.Wear(AccountSession.ModelName);
        }

        if (!initialized)
        {
            // Nothing to do until someone is playing a character AND the Systems scene is up.
            if (!loading && AccountSession.IsActive && Character.Instance != null) BeginLoad();
            return;
        }

        if (walkBackOwed != null) TryWalkBack();
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

            // THE CHARACTER THIS WAS FOR IS GONE. A refused entry reloads the persistent layer while the read is
            // still out, so the answer arrives for a destroyed character. Laying it on threw, and `loading` stayed
            // up for good — the next character chosen in that session was never loaded at all. Start over instead.
            if (character == null || character != Character.Instance)
            {
                loading = false;
                character = null;
                return;
            }

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
                // AND THE BAG WINDOW IS TOLD. Filling the list does not repaint anything by itself, and the
                // window is built before the save arrives — so without this it shows the empty bag the
                // character had a moment ago until something unrelated happens to refresh it.
                InventoryStore.Instance?.Notify();
            }
            else if (GameNetwork.MyPlayer == null)
            {
                // NOBODY HAS PLAYED THIS CHARACTER YET, and there is no world — so their starter goods are given
                // here. IN a world the server does this instead, and doing it here as well was a race: whichever
                // landed last won, and half the time a new player saw a bag the server had never heard of.
                character.GiveStarterKit();
                InventoryStore.Instance?.Notify();
            }

            // Only the recolour is still listened for. Levelling, reviving and spending a point used to be
            // listened for too, purely to trigger a write from this machine; the server writes all three.
            character.OnAppearanceChanged += SaveAppearance;

            // AND NOW THIS MACHINE KNOWS WHICH SKILLS THIS PERSON HAS. A character carries only the skills it can
            // USE — its bar, its learned passives, its bag — and none of that existed when the body first
            // asked, because the save had not arrived. Without this the bar draws empty slots and nothing casts.
            character.RefreshUsableSkills();
            HotbarPanel.RebuildRequested?.Invoke();

            initialized  = true;
            loading      = false;
        });
    }

    // Wear the body this character was created with and its chosen recolour.
    void ApplyAppearance()
    {
        bodyToWear = true;
        character.SetDye(AccountSession.DyeSources, AccountSession.DyeTargets, AccountSession.ModelName);
        // Always the whole value, empty included — or the last character's hair stays on this one.
        character.SetHair(AccountSession.HairStyle, AccountSession.HairColour);
    }

    /// <summary>Push a recolour to the character record. Appearance is not progress, so it never waits for the
    /// autosave timer and never rides along in the save document.</summary>
    void SaveAppearance()
    {
        if (character == null) return;
        ProfileStore.Current.SaveAppearance(character.DyeModel, character.DyeSources, character.DyeTargets);
    }

    // The save whose place the player has not been put back to yet, because no map was up when it arrived.
    SaveData walkBackOwed;

    /// <summary>Stand the player where their save says they were.
    ///
    /// <para><b>The save can arrive BEFORE the map.</b> It is read the moment a character is chosen, and the map
    /// only loads once the server has let them in — so this cannot act on whatever map name is lying around. After
    /// Select character the last map's name is still the current one while nothing is loaded under it: acting on
    /// it put them "back" onto empty space, used up the spot, and the real arrival a moment later stood them at
    /// the map's arrival point. Sending them to the saved map instead loaded it a second time beside the one the
    /// sign-in was already loading, and the second arrival did the same.</para>
    ///
    /// <para>So the exact spot is handed to the arrival, which stands them on it as the map lands; anything still
    /// to do waits here until a map is actually up.</para></summary>
    void ReturnToWhereWeLeftOff(SaveData data)
    {
        if (data == null || string.IsNullOrEmpty(data.sceneName)) return;   // a save older than this, or a fresh character

        // AN ARRIVAL POINT RATHER THAN A POSITION has nothing to hand over: every map load names its own arrival
        // point, which would overwrite this one. It waits for the map instead.
        if (string.IsNullOrEmpty(data.arriveAtSpawnId))
        {
            SceneTravel.ResumeScene = data.sceneName;
            SceneTravel.ResumePosition = new Vector3(data.posX, data.posY, data.posZ);
        }
        walkBackOwed = data;
        TryWalkBack();
    }

    // A map is loaded, current, and nothing else is on its way.
    static bool MapIsUp =>
        !string.IsNullOrEmpty(MapManager.CurrentMap) && MapManager.CurrentScene.isLoaded && MapManager.ArrivingAt == null;

    void TryWalkBack()
    {
        if (walkBackOwed == null || !MapIsUp) return;
        var data = walkBackOwed;
        walkBackOwed = null;

        // AN ARRIVAL POINT RATHER THAN A POSITION — somebody who left while dead with no save point of their own.
        if (!string.IsNullOrEmpty(data.arriveAtSpawnId))
        {
            if (data.sceneName == MapManager.CurrentMap)
            {
                SceneTravel.Target = data.arriveAtSpawnId;
                if (PersistentPlayer3D.Instance != null) PersistentPlayer3D.Instance.ReturnToSavedPlace();
            }
            else
            {
                MapManager.Travel(data.sceneName, data.arriveAtSpawnId);
            }
            return;
        }

        // The arrival already stood them on it.
        if (!SceneTravel.HasResume) return;

        if (SceneTravel.ResumeScene == MapManager.CurrentMap)
        {
            if (PersistentPlayer3D.Instance != null) PersistentPlayer3D.Instance.ReturnToSavedPlace();
        }
        else
        {
            MapManager.Travel(SceneTravel.ResumeScene, null);   // the arrival reads the resume point and clears it
        }
    }

    // NO SAVE FROM HERE, AND THERE IS NO CONDITION UNDER WHICH THERE COULD BE. A character is owned by the
    // server that is running the world it is in, and that server writes it down itself. This program is a
    // client for its whole life; a client never learns what actually happened in a fight, so anything it wrote
    // would be older than the truth and would land on top of it. The write used to live here, refused only
    // while a body happened to exist, which left every moment between bodies open.
    //
    // Reading a character back is unaffected and still happens here. Appearance is not part of a character's
    // progress and has always gone its own way, which is why it survives below.

    void OnDestroy()
    {
        if (character == null) return;
        character.OnAppearanceChanged -= SaveAppearance;
    }
}
