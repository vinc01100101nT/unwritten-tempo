using UnityEngine;

/// <summary>
/// Starts each frame's one-shot button presses (<see cref="Hotkeys.BeginFrame"/>) before anything reads a key,
/// so an on-screen button that stands in for a key — the status box's window buttons — is answered for exactly one
/// frame. Without this, a press would never be cleared and would read as held forever.
/// </summary>
[DefaultExecutionOrder(-1001)]
public class VirtualPressClock : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (GameServer.IsServer) return;   // a headless server has no buttons to press
        var go = new GameObject("VirtualPressClock") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);
        go.AddComponent<VirtualPressClock>();
    }

    void Update() => Hotkeys.BeginFrame();
}
