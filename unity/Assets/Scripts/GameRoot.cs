using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// The boot component of the persistent <c>Systems</c> scene, and THE gate on the world existing at all.
///
/// <para><b>Nothing of the world runs until a character is chosen.</b> Before that, no map is loaded, the HUD is
/// switched off and the player is switched off — so there is no town simulating behind the sign-in screen, no
/// weather falling, no clock running and no mob thinking. Hiding the world behind an opaque panel is NOT the
/// same thing and was the wrong fix: a game that is already running while you are still signing in is spending
/// frames on a session that hasn't begun, and would happily let a stray input reach it.</para>
///
/// <para>The moment <see cref="AccountSession"/> says somebody is playing, the persistent layer wakes up and the
/// first map comes in underneath it. After that, portals swap maps and Systems just stays loaded.</para>
///
/// In a build, scene index 0 is Systems and it loads alone. In the editor we always enter Play from Systems
/// (see SystemsPlayMode), and the map you had open is remembered and loaded instead — so "press Play in Town"
/// still tests Town, once you are through the door.
/// </summary>
public class GameRoot : MonoBehaviour
{
    [Tooltip("Map to load when a character enters the world (the build's starting map). In the editor, " +
             "whichever map you had open is remembered and used instead.")]
    public string firstMap = "town_0";

    bool worldUp;

    void Start()
    {
        AccountSession.OnChanged += OnSessionChanged;
        Quiet();                  // asleep until somebody is actually playing
        OnSessionChanged();       // …unless a character was already chosen before this scene loaded
    }

    void OnDestroy() => AccountSession.OnChanged -= OnSessionChanged;

    // The SERVER owns the world rather than watching one, so it has nobody to sign in and nothing to wait for:
    // it brings the map up the moment it boots. The gate below is the only thing that knows the difference —
    // everything downstream (waking the scene, loading the map) is identical either way.
    static bool WorldMayRun => AccountSession.IsActive || GameServer.IsServer;

    void OnSessionChanged()
    {
        if (worldUp || !WorldMayRun) return;
        worldUp = true;
        Wake();
        BringUpMap();
        // A server writes to a log file, not to a screen, so the only way anyone knows it came up is if it
        // says so. Server-only: a player's console is not the place to announce this.
        if (GameServer.IsServer) Debug.Log("[server] world up — loading map '" + MapToLoad() + "'");

        // Joining the wire happens at the same moment the world starts existing, for BOTH sides — a server
        // begins listening as it boots, a client dials once it is through the door. One call site, so the
        // two halves can never be started by different rules.
        GameNetwork.Begin();
    }

    // The persistent layer exists from the first frame — it IS this scene — so it gets switched off rather than
    // not built. Everything that belongs to a MAP simply isn't loaded yet, which is why there is no list of mobs
    // or weather to silence here: they do not exist.
    //
    // Everything in the scene is silenced rather than a hand-written list of the things that tick. A list would
    // be wrong the first time somebody adds a director to this scene and forgets to add it here, and that kind
    // of miss is invisible — it just quietly runs behind the sign-in screen.
    readonly System.Collections.Generic.List<GameObject> quieted = new System.Collections.Generic.List<GameObject>();

    void Quiet()
    {
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            if (root == gameObject) continue;                                    // the gate itself has to keep running
            if (root.GetComponentInChildren<EventSystem>(true) != null) continue; // the sign-in screen needs input to work
            if (!root.activeSelf) continue;

            root.SetActive(false);
            quieted.Add(root);
        }
    }

    void Wake()
    {
        // Exactly what was switched off, and nothing else — so anything an author deliberately left disabled in
        // the scene stays that way.
        foreach (var root in quieted) if (root != null) root.SetActive(true);
        quieted.Clear();
    }

    void BringUpMap()
    {
        // If a gameplay map is somehow already loaded alongside Systems, adopt it instead of loading a second one.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.isLoaded && s.name != MapManager.SystemsScene)
            {
                MapManager.Activate(s);
                return;
            }
        }
        MapManager.LoadMap(MapToLoad(), null);
    }

    string MapToLoad()
    {
        string map = firstMap;
#if UNITY_EDITOR
        string remembered = UnityEditor.SessionState.GetString("unwritten.bootMap", "");
        if (!string.IsNullOrEmpty(remembered)) map = remembered;
#endif
        return map;
    }
}
