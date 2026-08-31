#if UNITY_ANDROID
using UnityEngine;

/// <summary>
/// Pumps <see cref="TouchControls"/> once per frame, before anything reads it. Self-bootstraps on Android the
/// same way the UI click shield does, so no scene and no prefab has to know the touch layer exists — which is
/// what keeps the desktop scenes byte-identical.
///
/// <para>The execution order is the point of this being its own component: gestures must be resolved before the
/// player controller, the camera and the targeters run, or every read would be a frame stale and a tap could be
/// answered after the frame it happened in.</para>
/// </summary>
[DefaultExecutionOrder(-1000)]
public class TouchDriver : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (GameServer.IsServer) return;   // a headless server has no screen and no fingers

        var go = new GameObject("TouchDriver") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);
        go.AddComponent<TouchDriver>();
        go.AddComponent<TouchHud>();
        go.AddComponent<TouchUiScale>();
    }

    void Update() => TouchControls.Tick();
}
#endif
