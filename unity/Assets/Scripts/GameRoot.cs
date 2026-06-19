using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The boot component of the persistent <c>Systems</c> scene. Once the persistent layer
/// (HUD / cursor / character / player / camera) is up, it brings the first gameplay map in
/// underneath it via <see cref="MapManager.LoadMap"/>. After that, portals swap maps and
/// Systems just stays loaded.
///
/// In a build, scene index 0 is Systems and it loads alone, so this loads <see cref="firstMap"/>.
/// In the editor we always enter Play from Systems (see SystemsPlayMode), and the map you had
/// open is remembered and loaded instead — so "press Play in Town" still tests Town.
/// </summary>
public class GameRoot : MonoBehaviour
{
    [Tooltip("Map to load on Play when none is open (the build's starting map). In the editor, " +
             "whichever map you had open is remembered and used instead.")]
    public string firstMap = "Town";

    void Start()
    {
        // If a gameplay map is somehow already loaded alongside Systems, adopt it instead of
        // loading a second one.
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
