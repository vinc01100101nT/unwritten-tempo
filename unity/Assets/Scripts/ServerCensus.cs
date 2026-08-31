using UnityEngine;

// SCAFFOLDING — temporary, to prove the default map set really brings its mobs up. DELETE after verifying.
public class ServerCensus : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!GameServer.IsServer) return;
        var go = new GameObject("ServerCensus") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);
        go.AddComponent<ServerCensus>();
    }

    float next;

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + 5f;
        Debug.Log("[census] maps=" + UnityEngine.SceneManagement.SceneManager.sceneCount
                  + " mobs=" + MonsterAI3D.Active.Count
                  + " netObjects=" + Mirror.NetworkServer.spawned.Count);
    }
}
