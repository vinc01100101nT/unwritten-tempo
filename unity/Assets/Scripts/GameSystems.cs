using UnityEngine;

/// <summary>
/// The persistent "systems layer" object — host for global game services that must outlive any
/// single map (today the hardware <see cref="GameCursor"/> sits alongside this component). It
/// lives in the persistent <c>Systems</c> scene, so it's simply always present: no
/// <c>DontDestroyOnLoad</c>, no spawning from Resources. New always-on systems (audio, save,
/// pause) belong on this object too.
///
/// <see cref="Bootstrap"/> reads <see cref="Instance"/> to stand down when the Systems layer is
/// active. The singleton check is a guard against a map carrying in a stray copy.
/// </summary>
public class GameSystems : MonoBehaviour
{
    public static GameSystems Instance { get; private set; }

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
