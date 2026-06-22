using UnityEngine;

/// <summary>
/// The persistent "systems layer" object — host for global game services that must outlive any
/// single map (today the hardware <see cref="GameCursor3D"/> sits alongside this component). It
/// lives in the persistent <c>Systems</c> scene, so it's simply always present: no
/// <c>DontDestroyOnLoad</c>, no spawning from Resources. New always-on systems (audio, save,
/// pause) belong on this object too.
///
/// The singleton check is a guard against a map carrying in a stray copy.
/// </summary>
public class GameSystems : MonoBehaviour
{
    public static GameSystems Instance { get; private set; }

    [Header("Frame rate (whole game)")]
    [Tooltip("Frames-per-second cap for the entire game. Default 120. VSync is turned OFF so this is " +
             "honoured even on a 60 Hz panel (may tear above the refresh rate). Tick 'lock to VSync' " +
             "instead to follow the monitor refresh cleanly with no tearing.")]
    public int targetFps = 120;

    [Tooltip("Lock to the monitor's refresh rate (no tearing) instead of the targetFps cap. " +
             "On a 60 Hz monitor this caps you at 60.")]
    public bool lockToVSync = false;

    // Apply a 120 FPS cap (VSync off) at startup — runs automatically before the first scene loads, so the
    // whole game is uncapped from the monitor's 60 Hz refresh even if no GameSystems component is present.
    // If the component IS in the scene, its Awake (below) re-applies the inspector values right after.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BootFrameRate()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 120;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);   // a map loaded its own copy — drop it, keep the Systems one
            return;
        }
        Instance = this;
        ApplyFrameRate();
    }

    // Global render cap. With VSync on, Application.targetFrameRate is IGNORED (you lock to the refresh
    // rate), so to actually exceed 60 on a 60 Hz panel we must turn VSync off and set the cap directly.
    public void ApplyFrameRate()
    {
        if (lockToVSync)
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;     // follow the display refresh
        }
        else
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.Max(30, targetFps);
        }
    }

    // Lets you drag targetFps / toggle VSync in the inspector at runtime and see it apply immediately.
    void OnValidate()
    {
        if (Application.isPlaying && Instance == this) ApplyFrameRate();
    }
}
