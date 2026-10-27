using UnityEngine;

/// <summary>
/// PLANTS LEAN AWAY FROM YOU. Tells the Idyllic Fantasy Nature plant shader where your own body stands, so the grass and
/// flowers around it bend; how far and how much is each plant material's own Bend Strength and Start Bend Range. It sets
/// one shader value for everything (no material is written), only on your screen, and only when you have moved.
///
/// <para><b>Display only.</b> Nothing here touches a game number. The server drops it (it draws nothing).</para>
///
/// <para><b>Cost.</b> One body lookup a second while you have no body yet, then one position compare a frame and one
/// shader write on the frames you move.</para>
/// </summary>
public class PlantBend : MonoBehaviour
{
    static readonly int PlayerPositionID = Shader.PropertyToID("_Player_Position");

    Transform body;
    Vector3 last = new Vector3(float.NaN, 0f, 0f);
    float nextLook;

    void Awake()
    {
        GameServer.RemovesPresentation(this);
    }

    void Update()
    {
        if (body == null)
        {
            if (Time.unscaledTime < nextLook) return;
            nextLook = Time.unscaledTime + 1f;
            body = Character.Instance != null ? Character.Instance.Body : null;
            if (body == null) return;
        }
        var at = body.position;
        if (at == last) return;
        last = at;
        Shader.SetGlobalVector(PlayerPositionID, at);
    }
}
