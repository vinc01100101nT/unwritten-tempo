using Mirror;
using UnityEngine;

/// <summary>
/// One mob, as every player sees it. The SERVER owns the real creature — its brain, its pathing, its health —
/// and this object is the thin thing that crosses the wire: which kind of mob it is, where it is, and how
/// hurt it is. Every client builds its own copy of the body from that, exactly the way
/// <see cref="NetPlayer"/> builds a body for a remote player.
///
/// <para><b>Why not send the mob itself.</b> A mob's body is a baked asset fetched on demand, and there are
/// many kinds. Sending an id and letting each machine fetch its own copy means the wire carries a few dozen
/// bytes per mob instead of a model, and it reuses the spawn path the game already has rather than inventing
/// a second one.</para>
///
/// <para><b>The client's copy does not think.</b> Its brain, its pathing and its respawn timer are switched
/// off on arrival — those are the server's job now. A client that kept its own brain would have a mob that
/// disagreed with everyone else's about where it stood and whether it was alive, which is exactly the
/// disagreement this whole layer exists to prevent.</para>
/// </summary>
public class NetMob : NetworkBehaviour
{
    /// <summary>Every mob shell alive on this machine, found by the BODY it drew. A dictionary rather than a
    /// list because this is read on the per-hit path — every swing at a mob asks it — and PERFORMANCE.md's
    /// rule is that a per-hit lookup does not walk a collection.</summary>
    static readonly System.Collections.Generic.Dictionary<GameObject, NetMob> byView =
        new System.Collections.Generic.Dictionary<GameObject, NetMob>();

    /// <summary>The shell that speaks for <paramref name="viewObject"/>, or null if that object is not a mob
    /// this machine received from a server. Walks up from a child (a hit may land on a body part) but stops
    /// as soon as it finds one, so the usual case is a single dictionary probe.</summary>
    public static NetMob ShellForView(GameObject viewObject)
    {
        for (var t = viewObject != null ? viewObject.transform : null; t != null; t = t.parent)
            if (byView.TryGetValue(t.gameObject, out var shell) && shell != null) return shell;
        return null;
    }

    /// <summary>Which creature this is, by catalog id. Set once by the server before it spawns.</summary>
    [SyncVar] public string defId = "";

    [SyncVar] Vector3 netPosition;
    [SyncVar] float netYaw;
    [SyncVar] int netHealth;
    [SyncVar] int netMaxHealth = 1;

    /// <summary>The real creature. Only ever set on the SERVER — this is the thing that actually thinks,
    /// walks and takes damage; everything below just copies its state onto the wire.</summary>
    public GameObject ServerMob { get; private set; }

    Health serverHealth;

    // The client's own copy of the body: seen, never simulated.
    GameObject view;
    bool viewRequested;

    const float PushInterval = 0.1f;   // ten times a second, same cadence as a player's position
    float nextPush;

    // Close enough to count as arrived. Below these, blending toward the target is work nobody can see.
    const float SettledDistance = 0.005f;
    const float SettledAngle = 0.5f;

    /// <summary>Server-only: hand this shell the creature it speaks for.</summary>
    public void BindOnServer(GameObject mob, string id)
    {
        ServerMob = mob;
        defId = id;
        serverHealth = mob != null ? mob.GetComponent<Health>() : null;
        if (serverHealth != null) { netMaxHealth = serverHealth.maxHealth; netHealth = serverHealth.CurrentHP; }
        if (mob != null) { netPosition = mob.transform.position; netYaw = mob.transform.eulerAngles.y; }
    }

    void Update()
    {
        if (isServer) PushFromTheRealMob();
        else FollowTheServer();
    }

    // ---- the server's half: publish what the real creature is doing --------------------------------------

    void PushFromTheRealMob()
    {
        // GONE, or BANKED. A killed mob is not destroyed here — it is switched off and kept for reuse, so
        // "still exists" is not the same as "still alive". Checking only for null would leave this shell
        // pushing the frozen position of a corpse forever, and every player would watch a mob that is not
        // there. Either way the shell goes, and a fresh one is published when the mob is next brought back.
        if (ServerMob == null || !ServerMob.activeInHierarchy)
        {
            NetworkServer.Destroy(gameObject);
            return;
        }

        if (Time.time < nextPush) return;
        nextPush = Time.time + PushInterval;

        netPosition = ServerMob.transform.position;
        netYaw = ServerMob.transform.eulerAngles.y;
        if (serverHealth != null) netHealth = serverHealth.CurrentHP;
    }

    // ---- everybody else's half: show it ------------------------------------------------------------------

    // The view's own Health, cached at build time. Looked up once rather than per frame, because this runs on
    // every mob on screen every frame and a per-frame component lookup at that rate is exactly what
    // PERFORMANCE.md bans.
    Health viewHealth;
    int lastShownHealth = -1;

    void FollowTheServer()
    {
        if (view == null) { EnsureView(); return; }

        // A mob standing still costs nothing. Without this, every mob on screen pays for a position blend and
        // a rotation blend every frame forever — and a rotation blend is the expensive one. Most mobs in a
        // town or a quiet field are idle most of the time, so this is the common case, not the rare one.
        var t = view.transform;
        Vector3 here = t.position;
        if ((here - netPosition).sqrMagnitude > SettledDistance * SettledDistance)
            t.position = Vector3.Lerp(here, netPosition, 12f * Time.deltaTime);

        if (Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, netYaw)) > SettledAngle)
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0f, netYaw, 0f), 12f * Time.deltaTime);

        // Only when the number actually changed — a mob at steady health should cost nothing, and the
        // nameplate below only redraws when it sees a different value anyway.
        if (viewHealth != null && netHealth != lastShownHealth)
        {
            lastShownHealth = netHealth;
            viewHealth.SetHealthFromServer(netHealth, netMaxHealth);
        }
    }

    void EnsureView()
    {
        if (viewRequested || string.IsNullOrEmpty(defId)) return;
        viewRequested = true;

        var def = MonsterCatalog.Get(defId);
        if (def == null)
        {
            Debug.LogWarning("[net] no mob called '" + defId + "' in the catalog — it will be invisible.");
            return;
        }

        MobSpawner.Spawn(def, netPosition, go =>
        {
            if (this == null || go == null) return;
            SilenceTheBrain(go);
            view = go;
            viewHealth = go.GetComponent<Health>();   // cached once; never looked up per frame
            byView[go] = this;                        // so a swing at this body finds the shared identity
        });
    }

    // A watched mob must not also be a thinking mob. What remains is a body that follows what the server says.
    //
    // NOTE THE ASYMMETRY, it is deliberate: the brain is told to stop DECIDING but is left switched ON, because
    // switching it off would drop this mob out of the registry the game uses to find something to attack, to
    // pick area-effect targets and to draw minimap blips — leaving a mob standing there that nobody could click
    // or hit. The mover and the respawn timer have no such role, so those are simply switched off.
    static void SilenceTheBrain(GameObject mob)
    {
        var ai = mob.GetComponent<MonsterAI3D>();
        if (ai != null) ai.ThinksForItself = false;

        var agent = mob.GetComponent<PathAgent3D>();
        if (agent != null) agent.enabled = false;

        var respawn = mob.GetComponent<MobRespawn>();
        if (respawn != null) respawn.enabled = false;
    }

    public override void OnStopClient()
    {
        if (view != null)
        {
            byView.Remove(view);   // before the object goes, or the table keeps a dead key forever
            Destroy(view);
        }
        base.OnStopClient();
    }
}
