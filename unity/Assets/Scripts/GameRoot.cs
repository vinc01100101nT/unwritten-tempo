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
        GameNetwork.OnJoinedWorld += OnJoinedWorld;
        GameNetwork.OnLostWorld += OnLostWorld;
        Quiet();                  // asleep until somebody is actually playing
        OnSessionChanged();       // …unless a character was already chosen before this scene loaded
    }

    void OnDestroy()
    {
        AccountSession.OnChanged -= OnSessionChanged;
        GameNetwork.OnJoinedWorld -= OnJoinedWorld;
        GameNetwork.OnLostWorld -= OnLostWorld;
    }

    // THERE IS NO OFFLINE WORLD. A server owns a world and so may bring one up the moment it boots; a player
    // does not own one and may only be IN one, which means being joined to a server. Signing in is no longer
    // enough to be somewhere — it only earns the right to try.
    static bool MayTryToEnter => AccountSession.IsActive || GameServer.IsServer;

    void OnSessionChanged()
    {
        if (worldUp || !MayTryToEnter) return;

        // The wire FIRST, the world second. One call site for both halves, so the two can never be started by
        // different rules — a server begins listening, a player dials.
        GameNetwork.Begin();

        // A server IS the world and waits for nobody. A player waits to be let in (see OnJoinedWorld).
        if (GameServer.IsServer) BringTheWorldUp();
        // SOMETHING IS HAPPENING AND IT IS NOT NOTHING — but only while we are still trying. Asking to enter
        // can fail before the line above has even returned: with no character server to ask, the answer comes
        // back on the spot, and it signs the player out and puts the front door back up. Saying "reconnecting"
        // after that would be a veil over a door nobody is knocking on.
        else if (AccountSession.IsActive) ReconnectNotice.Show();
    }

    // The wire came up. THIS is the moment a player is allowed to be somewhere.
    void OnJoinedWorld()
    {
        ReconnectNotice.Hide();
        if (worldUp) return;           // a reconnect, not a first arrival — the world is already here
        BringTheWorldUp();
    }

    // Every retry spent. Nobody may be in a world they are not joined to, so the world goes and the player is
    // put back at the front door with a line saying why — not left standing in a town nobody else can see.
    void OnLostWorld()
    {
        ReconnectNotice.Hide();
        GameNetwork.Forget();
        AccountSession.SignOut();
        SceneManager.LoadScene(MapManager.SystemsScene, LoadSceneMode.Single);
        SignInScreen.OpenSaying("Lost the connection to the world.");
    }

    void BringTheWorldUp()
    {
        worldUp = true;
        Wake();
        BringUpMap();
        // A server writes to a log file, not to a screen, so the only way anyone knows it came up is if it
        // says so. Server-only: a player's console is not the place to announce this.
        if (GameServer.IsServer) GameLog.Info(LogCat.Server, "world up — loading map '" + MapToLoad() + "'");
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
        string first = MapToLoad();

        // If a gameplay map is somehow already loaded alongside Systems, adopt it instead of loading a second one.
        bool adopted = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.isLoaded && s.name != MapManager.SystemsScene)
            {
                MapManager.Activate(s);
                first = s.name;   // the adopted map IS the first one; nothing below may load it a second time
                adopted = true;
                break;
            }
        }
        if (!adopted) MapManager.LoadMap(first, null);

        // A PLAYER IS IN ONE MAP. A SERVER HOLDS SEVERAL AT ONCE, and that difference is the whole reason a
        // world has creatures in it when nobody is looking: a field the server never opened has no spawner,
        // so it makes nothing, and a player walking in finds it empty. Which maps to hold is authored above.
        if (!GameServer.IsServer) return;

        // THE CARETAKER FOR THE MAPS THIS PROCESS HOLDS. It lets a map go once nobody has been in it for a while
        // and brings one up when the character server says somebody is coming, so a process ends up holding the
        // zones people are actually in rather than a list decided before anyone signed in.
        if (MapHosting.Instance == null) gameObject.AddComponent<MapHosting>();

    }

    // NO LIST OF MAPS ANY MORE. A server used to be handed the zones it would hold before anybody had signed in,
    // which meant every process held the same starting map and fought over it, and a quiet field was held all
    // night for nobody. Now no process is given anything: the character server decides which process a zone goes
    // to, at the moment somebody wants it, weighing how busy each one already is — and MapHosting lets a map go
    // again once nobody has been in it for a while. The one map that comes up above is only somewhere to stand
    // while that is being decided.

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
