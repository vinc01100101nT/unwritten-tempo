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

    /// <summary>How often a SERVER runs a frame. A server draws nothing, so every frame it spends is pure
    /// simulation — and it was spending them at the client's 120, four times more than the world needs, on a
    /// machine with no screen to show for it.
    ///
    /// <para>This is also the ceiling on how often anything can reach a player: the wire is pumped once per
    /// frame, so a send rate above this would be a promise the loop cannot keep. <see cref="GameNetwork"/>
    /// reads this same number rather than keeping its own, so the two can never drift apart.</para>
    ///
    /// <para><b>Why sixty and not something lower.</b> A frame is not only simulation here — it is also when the
    /// wire is emptied and refilled, and the transport is set up expecting to be seen roughly every ten
    /// milliseconds. Thirty was tried first and would have stretched that to thirty-three, making every lost
    /// packet take about three times as long to notice and resend, which is paid by every player in the world.
    /// Sixty still halves what a server was spending and keeps the wire close to the beat it asked for.</para></summary>
    public const int ServerFps = 60;

    // Apply the frame cap at startup — runs before the first scene loads, so it holds even if no GameSystems
    // component is present. If the component IS in the scene, its Awake (below) re-applies the inspector
    // values right after. A server takes ServerFps and ignores the inspector entirely: those knobs describe a
    // display, and a server has none.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BootFrameRate()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = GameServer.IsServer ? ServerFps : 120;
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
        // A SERVER IGNORES BOTH KNOBS, and it has to be said here as well as at boot: this runs from Awake,
        // so without it the component in the Systems scene would hand a headless server the display settings
        // back and undo the cap a few milliseconds after it was set.
        if (GameServer.IsServer)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = ServerFps;
            return;
        }

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
