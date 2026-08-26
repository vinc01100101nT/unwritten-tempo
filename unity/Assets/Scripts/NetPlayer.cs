using Mirror;
using UnityEngine;

/// <summary>
/// One connected player, as everybody else sees them. The server spawns exactly one of these per connection
/// and it exists on every client, so a player who is standing in your town has a body you can watch.
///
/// <para><b>Two very different jobs, decided by <see cref="NetworkBehaviour.isOwned"/>:</b></para>
/// <list type="bullet">
/// <item>On the machine that OWNS it, this object draws nothing. That player already has their real avatar —
/// the one they steer — so a second body would be a twin standing inside them. It only REPORTS where that
/// avatar is.</item>
/// <item>On everybody else's machine, it BUILDS a body and moves it to whatever the server last said. That
/// body is made by the shared character recipe (<see cref="Character3DBody"/>) — the same one townsfolk and
/// the player are built from — so a remote player is not a third kind of character with its own drift.</item>
/// </list>
///
/// <para><b>Honest about what this is NOT yet.</b> The server currently RELAYS the position its owner reports
/// rather than deciding it. That is enough for players to see each other, and it is the same shape the real
/// thing takes, but a client is still trusted about where it stands. Making the server decide movement needs
/// prediction and correction on the client, which is a whole piece of work of its own — see the migration
/// plan. Nothing here should be read as "movement is validated", because it is not.</para>
/// </summary>
public class NetPlayer : NetworkBehaviour
{
    // ---- what the server holds and every client receives ------------------------------------------------
    // Identity is set ONCE when the body is spawned and never changes; position changes constantly. They are
    // separate so a body that is standing still costs nothing to keep in sync.

    [SyncVar] public string playerName = "";
    [SyncVar] public string modelName = "";

    [SyncVar] Vector3 netPosition;
    [SyncVar] float netYaw;

    // The visual body, built once on the machines that need to SEE this player. Null on the owner's own
    // machine, which draws its real avatar instead.
    GameObject body;
    bool bodyRequested;

    /// <summary>SERVER ONLY: the fighter this connection swings with — the thing that owns their stats, rolls
    /// their crits and takes their hits. Null on a client, which has no business holding anyone's authoritative
    /// stats, and null on the server until their character has been built.</summary>
    public ICombatUnit ServerCombatUnit { get; private set; }

    /// <summary>Server-only: give this connection the fighter the server will resolve its blows with.</summary>
    public void BindCombatUnitOnServer(ICombatUnit unit) => ServerCombatUnit = unit;

    // How often the owner tells the server where it is. Ten times a second is far below the frame rate and
    // still smoother than a person can walk; sending every frame would be bandwidth spent on nothing.
    const float ReportInterval = 0.1f;
    float nextReport;

    // Below this, a step is not worth a packet — a player standing still should cost nothing.
    const float MoveEpsilon = 0.01f;
    Vector3 lastReported;

    // The FIRST report has to go out even if the player has not moved a millimetre. Without it a person who
    // spawns and stands still never sends anything, so the server's idea of where they are stays at the world
    // origin — and everyone else watches them standing in the far corner of the map instead of in town.
    bool hasReportedOnce;

    void Update()
    {
        if (isOwned) ReportMyPosition();
        else FollowTheServer();
    }

    // ---- the owner's half: say where I am ---------------------------------------------------------------

    void ReportMyPosition()
    {
        if (Time.time < nextReport) return;
        nextReport = Time.time + ReportInterval;

        var me = PersistentPlayer3D.Instance;
        if (me == null) return;   // the avatar is not up yet (still loading a map)

        Vector3 p = me.transform.position;
        float yaw = me.transform.eulerAngles.y;
        if (hasReportedOnce && (p - lastReported).sqrMagnitude < MoveEpsilon * MoveEpsilon) return;

        lastReported = p;
        hasReportedOnce = true;
        CmdReportPosition(p, yaw);
    }

    [Command]
    void CmdReportPosition(Vector3 p, float yaw)
    {
        // The server writes it down and every other client receives it. It does NOT yet check whether the
        // move was possible — see the class summary.
        netPosition = p;
        netYaw = yaw;
    }

    // ---- everybody else's half: show them ---------------------------------------------------------------

    void FollowTheServer()
    {
        if (body == null) { EnsureBody(); return; }

        // Glide rather than snap. Updates arrive ten times a second; moving straight to each one would read
        // as a stutter, so the body walks toward the latest known position instead.
        body.transform.position = Vector3.Lerp(body.transform.position, netPosition, 12f * Time.deltaTime);
        body.transform.rotation = Quaternion.Slerp(body.transform.rotation,
                                                   Quaternion.Euler(0f, netYaw, 0f), 12f * Time.deltaTime);
    }

    void EnsureBody()
    {
        if (bodyRequested) return;                      // the model is on its way; do not ask twice
        if (string.IsNullOrEmpty(modelName)) return;    // identity has not arrived yet — it will, next tick
        bodyRequested = true;

        string wanted = modelName;
        CharacterModels.Load(wanted, model =>
        {
            if (this == null) return;                   // player left while their model was loading
            if (model == null)
            {
                Debug.LogWarning("[net] no character model called '" + wanted + "' — "
                                 + playerName + " will be invisible.");
                return;
            }

            var spec = CharacterSpec.Active;
            var root = new GameObject("Player_" + playerName) { hideFlags = HideFlags.DontSaveInEditor };
            root.transform.position = netPosition;

            // The SHARED character body — the same recipe behind townsfolk and your own avatar. A remote
            // player must not be a fourth way of making a character, or the four drift apart.
            Character3DBody.Build(root, model, spec != null ? spec.animatorController : null,
                                  spec != null ? spec.bodyHeight : 0.327f);
            body = root;
        });
    }

    public override void OnStopClient()
    {
        if (body != null) Destroy(body);
        base.OnStopClient();
    }
}
