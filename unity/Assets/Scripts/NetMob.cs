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
public class NetMob : NetworkBehaviour, IWatchedShell
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

    /// <summary>Every mob shell on this WATCHING machine, found by the shared id a blow report names its striker
    /// with. A dictionary for the same reason as <see cref="byView"/>: it is asked on every blow that lands on this
    /// machine's player.</summary>
    static readonly System.Collections.Generic.Dictionary<uint, NetMob> byNetId =
        new System.Collections.Generic.Dictionary<uint, NetMob>();

    /// <summary>The mob shell the server calls <paramref name="netId"/>, or null when that id is not a mob this
    /// machine has been told about (a person, nobody, or a creature out of range).</summary>
    public static NetMob ShellForNetId(uint netId)
        => netId != 0u && byNetId.TryGetValue(netId, out var shell) ? shell : null;

    /// <summary>Every shell on the SERVER, found by the REAL creature it speaks for — the mirror of
    /// <see cref="byView"/>, which answers the same question on a watching machine. A dictionary for the same
    /// reason: the server has to name the creature a blow landed on in order to announce that blow, which is the
    /// per-hit path, and PERFORMANCE.md's rule is that a per-hit lookup does not walk a collection.</summary>
    static readonly System.Collections.Generic.Dictionary<GameObject, NetMob> byServerMob =
        new System.Collections.Generic.Dictionary<GameObject, NetMob>();

    /// <summary>SERVER ONLY: the shell that speaks for the real creature <paramref name="mobObject"/> belongs to,
    /// or null when that object is not a creature this server published. Walks up from a child for the same reason
    /// <see cref="ShellForView"/> does — a blow may land on a body part rather than on the root.</summary>
    public static NetMob ShellForServerMob(GameObject mobObject)
    {
        for (var t = mobObject != null ? mobObject.transform : null; t != null; t = t.parent)
            if (byServerMob.TryGetValue(t.gameObject, out var shell) && shell != null) return shell;
        return null;
    }

    /// <summary>Which creature this is, by catalog id. Set once by the server before it spawns.</summary>
    [SyncVar] public string defId = "";

    /// <summary>WHICH MAP this creature lives in, by scene name. Set by the server from the scene the real creature
    /// actually stands in.
    ///
    /// <para><b>Why a watcher needs it.</b> The world is several maps at once and they share one set of
    /// coordinates, so a creature in the field and a person in a town can be a metre apart by the numbers. Without
    /// this, every machine drew every creature in the world into whichever map its player was standing in: a town
    /// full of the field's monsters, standing on nothing, that walked at you and could never reach you.</para></summary>
    ///
    /// <para>PRIVATE on purpose, unlike the id above it. Mirror syncs a value whether or not Unity can see it, but
    /// a PUBLIC field is also baked into the saved prefab — and this prefab is one of the two the server loads at
    /// the very moment it starts up. Kept private, the wire carries it and the stored prefab is left exactly as it
    /// was.</para></summary>
    [SyncVar] string mapName = "";

    /// <summary>Whether this creature wears a live-3D body instead of a flat sprite. Set once by the server
    /// before it spawns, from the creature itself — the kind of thing it is does not say, because the same kind
    /// can be summoned either way. Without this on the wire the choice lived only on the machine that made it,
    /// and every watcher rebuilt the creature from its kind alone, which is always the sprite.</summary>
    [SyncVar] public bool isShadow;

    /// <summary>WHICH KIND OF CREATURE this is, read off the real creature by the server. PRIVATE for the same reason
    /// the map name is: the wire carries it, the saved prefab does not.</summary>
    [SyncVar] CreatureKind netKind;

    /// <summary>The creature's CURRENT level as the server holds it — re-sent on every push, because a summon
    /// can be re-statted to any level, so its creature data's level is not the answer.</summary>
    [SyncVar] int netLevel;

    public CreatureKind Kind => netKind;

    /// <summary>THE CHARACTER THIS CREATURE TURNED BACK INTO, by id — empty for an ordinary creature (see
    /// <see cref="SavedCharacters"/>). A watcher draws the body in that character's picture. PRIVATE for the same reason
    /// the map name is: the wire carries it, the saved prefab does not.</summary>
    [SyncVar] string savedCharacter = "";

    /// <summary>WHICH SIDE this creature fights on, as the server holds it — what the watching player's cursor asks before
    /// it will pick the creature. Re-sent when it changes: a character that says goodbye stops fighting.</summary>
    [SyncVar(hook = nameof(OnSideArrived))] Side netSide;

    // THE LAST THING THIS CREATURE SAID and the moment it said it, on the shared clock — held as a value, like an action,
    // so somebody who walks up a second later still reads the line.
    [SyncVar] string netLine = "";
    [SyncVar] double netLineAt;
    double lineShownAt;

    /// <summary>When this creature turned back into its character, on the shared clock — so a watcher who sees it within
    /// its first moments plays the aura that marks it, and one who arrives later does not.</summary>
    [SyncVar] double netSavedAt;

    // The last action this body was shown as a pose (a flat character's swing), so each is shown once.
    double poseShownAt;

    void OnSideArrived(Side _, Side now)
    {
        if (viewHealth != null) viewHealth.SetSide(now);
    }

    /// <summary>SERVER ONLY: this creature says <paramref name="line"/> — shown over its head on every watching machine.</summary>
    public void Say(string line)
    {
        if (!isServer || string.IsNullOrEmpty(line)) return;
        netLine = line;
        netLineAt = NetworkTime.time;
    }

    public int Level => netLevel;

    /// <summary>WHICH BODY a live-3D creature wears — its creature entry's fixed look, taken by this server before the
    /// creature is announced. Empty on a flat sprite creature, which wears none.
    ///
    /// <para>On the wire because it is this creature's IDENTITY, not a picture each machine may invent. It is set
    /// once, before anybody is told about the creature at all.</para>
    ///
    /// <para>Carries a change hook because the body a watcher builds and this name can land in either order: the
    /// hook covers the name arriving second, and the build covers it arriving first.</para></summary>
    [SyncVar(hook = nameof(OnShadowModelArrived))] string shadowModel = "";

    // The name landed after this machine had already built the creature's body. Told straight away; a body that has
    // not been built yet is told by the build instead, so neither order can leave it wearing nothing.
    void OnShadowModelArrived(string _, string now) => (viewVisual as MobShadow3DVisual)?.WearModel(now);

    /// <summary>WHAT THIS CREATURE IS WEARING and the chance each piece is given up, written by the server
    /// (<see cref="WornList"/>). A watcher dresses its copy in it and draws it on the creature's health bar, and works
    /// none of it out. PRIVATE for the same reason the map name is: the wire carries it, the saved prefab does not.</summary>
    [SyncVar(hook = nameof(OnWornArrived))] string worn = "";

    /// <summary>The states of the creature this shell speaks for, as the SERVER holds them — the real store its
    /// buffs are decided against. Null on a watching machine, which owns no creature.</summary>
    public UnitStates ServerStates => serverHealth != null ? serverHealth.States : null;

    /// <summary>The store on the body THIS machine drew for this creature, which is where a watcher's copy of its
    /// buffs is kept and where every readout already looks. Null until that body has arrived.</summary>
    public UnitStates WatchedStates => viewHealth != null ? viewHealth.States : null;

    /// <summary>What this creature wears, as the server wrote it (<see cref="WornList"/>) — read by the hover and target
    /// panel. The same string object until the server sends a different one, so a reader can notice a change by reference.</summary>
    public string WornText => worn;

    // It landed after this machine had built the body. A body built later is dressed by AdoptView instead.
    void OnWornArrived(string _, string now)
    {
        if (view != null) ApplyWornToView();
    }

    [SyncVar] Vector3 netPosition;
    [SyncVar] float netYaw;
    [SyncVar] int netHealth;
    [SyncVar] int netMaxHealth = 1;

    // Mana, on the same footing as health: a creature's pool is the server's, and a watching machine works none of it
    // out. 0 and 0 = no pool, which is what hides the bar.
    [SyncVar] int netMana;
    [SyncVar] int netMaxMana;

    /// <summary>How fast this creature is actually travelling on the ground, in units per second — 0 while it
    /// stands. TOLD rather than left for each watcher to work out, because a watcher can only measure the
    /// catch-up glide toward the positions it received, not the walk: see <see cref="Agent3DVisual.TellTravelSpeed"/>
    /// for the arithmetic. It only changes when the creature changes gear, and a SyncVar written the value it
    /// already holds sends nothing.</summary>
    [SyncVar] float netSpeed;

    /// <summary>Which way this creature last travelled on the ground, in world terms (x, z). Only a flat-picture
    /// creature needs it: each watcher turns it into the up / down / left / right picture for its own camera. A live-3D
    /// creature's direction already rides on <see cref="netYaw"/>. Left alone while it stands, so it keeps facing the
    /// way it last walked.</summary>
    [SyncVar] Vector2 netHeading = Vector2.down;

    // ---- what this creature is DOING ---------------------------------------------------------------------
    // The action beat: which action, the moment it began on the clock both machines share, and how long it runs.
    // HELD AS A VALUE rather than shouted once, which is what lets somebody who only just started watching pick
    // up an action already in progress — a shout reaches whoever was already there and nobody else.
    //
    // Three plain fields rather than one struct on purpose: Mirror batches a behaviour's dirty values into one
    // update, so they arrive together, and the watching side reads them in its own per-frame follow instead of
    // depending on the order three separate change-hooks would fire in.

    // THE POSE IT IS HOLDING — the casting pose through a wind-up. Separate from the action above for the same reason a
    // person's is: an action plays once, a held pose lasts until it is let go.
    [SyncVar] bool netHasHeld;
    [SyncVar] StanceId netHeld;
    [SyncVar] float netHeldWindow;

    [SyncVar] StanceId netAction;
    [SyncVar] double netActionAt;
    [SyncVar] float netActionSeconds;
    // Who the action was aimed at, by their shared name (0 = nobody in particular) — so a flat creature's bite can be
    // drawn leaning into whoever it bit.
    [SyncVar] uint netActionAimedAt;
    // When the action's blow connects, on the shared clock — the moment the server lands a melee swing's damage, and
    // the moment a flat creature's bite is drawn slamming in. 0 = no blow lands with this action.
    [SyncVar] double netActionConnectsAt;

    // A scripted move — a glide, a blink, a knockback — said once as it begins, and drawn here (see MoveSaid).
    [SyncVar] MoveSaid netMove;

    /// <summary>Where this creature's weapon is — on its body, coming out, in hand, going back — as the server holds
    /// it. A watcher draws it and decides nothing.</summary>
    [SyncVar(hook = nameof(OnDrawArrived))] DrawSaid netDraw;
    int drawVersionSent = -1;
    void OnDrawArrived(DrawSaid _, DrawSaid now) => (viewVisual as MobShadow3DVisual)?.ShowDraw(now);
    readonly WatchedMove watchedMove = new WatchedMove();

    /// <summary>The real creature. Only ever set on the SERVER — this is the thing that actually thinks,
    /// walks and takes damage; everything below just copies its state onto the wire.</summary>
    public GameObject ServerMob { get; private set; }

    Health serverHealth;
    UnitStatBlock serverBlock;

    // What the real creature wears, and which version of it this shell last put on the wire. The push beat compares
    // one number, so a creature whose gear has not changed costs nothing.
    UnitEquipment serverGear;
    int wornVersionSent = int.MinValue;

    // SERVER: put what the creature wears on the wire.
    void PublishWorn()
    {
        wornVersionSent = serverGear != null ? serverGear.Version : int.MinValue;
        worn = WornList.Encode(serverGear);
    }
    // The real creature's mover, cached when the shell is bound. It is the one thing on the server that knows
    // whether this creature is travelling and how fast, and this is read on the push beat — never looked up there.
    PathAgent3D serverAgent;

    // The client's own copy of the body: seen, never simulated.
    GameObject view;
    // A body is on its way. NOT "one was asked for once" — a body can be destroyed under us, and a latch that only
    // ever went true meant such a creature could never be drawn again for as long as it lived.
    bool viewLoading;

    /// <summary>WATCHING MACHINES ONLY: the copy of this creature that a server-decided blow is shown on. Cached
    /// when the copy is built, never looked up per hit — the door that uses it runs on every blow.</summary>
    public IDamageable WatchedTarget { get; private set; }

    /// <summary>THE BODY THIS MACHINE DRAWS FOR THIS CREATURE — the watched copy, or null before it is built. The
    /// creature half of the same question a person's shell answers, so anything that plays a picture on a unit asks
    /// one question rather than one per kind of unit.</summary>
    public Transform DrawnBody => view != null ? view.transform : null;

    /// <summary>WATCHING MACHINES ONLY: the name this creature is shown under — its nameplate's, read once when its body
    /// is built, so the combat log names it on every blow without a lookup. Null before it has a body.</summary>
    public string ShownName { get; private set; }

    const float PushInterval = 0.1f;   // ten times a second, same cadence as a player's position

    float nextPush;

    // The synced facing, held as a rotation. Rebuilt only when the number actually changes — the follow below runs
    // every frame on every creature on screen, and building a rotation from an angle there would be that cost times
    // every mob in the field, forever, for a value that changes ten times a second at most.
    Quaternion facing = Quaternion.identity;
    float facingBuiltFrom = float.NaN;

    /// <summary>Server-only: hand this shell the creature it speaks for.</summary>
    public void BindOnServer(GameObject mob, string id)
    {
        ServerMob = mob;
        // FILED UNDER THE REAL CREATURE, so a blow the server lands on it can find the shell that has to announce
        // that blow. The watching side files its own body the same way when it builds one (see AdoptView).
        if (mob != null) byServerMob[mob] = this;
        defId = id;
        mapName = mob != null ? mob.gameObject.scene.name : "";
        var shadow = mob != null ? mob.GetComponent<MobShadow3DVisual>() : null;
        isShadow = shadow != null;
        // AND THE BODY IT WEARS, read off the creature at the same moment its kind is. It took its fixed look from its
        // creature entry as it was built, on the line above the one that announces it, so there is an answer here.
        shadowModel = shadow != null ? (shadow.ModelName ?? "") : "";
        serverHealth = mob != null ? mob.GetComponent<Health>() : null;
        serverAgent = mob != null ? mob.GetComponent<PathAgent3D>() : null;
        if (serverHealth != null) { netMaxHealth = serverHealth.maxHealth; netHealth = serverHealth.CurrentHP; }
        serverBlock = mob != null ? mob.GetComponent<UnitStatBlock>() : null;
        if (serverBlock != null) { netMaxMana = serverBlock.MaxMP; netMana = serverBlock.CurrentMP; netLevel = serverBlock.Level; }
        if (serverHealth != null) netKind = serverHealth.kind;
        // AND WHICH CHARACTER IT IS, and whose side it is on, so the very first message about it already says so.
        savedCharacter = serverHealth != null && serverHealth.savedAs != null ? serverHealth.savedAs.id : "";
        netSavedAt = savedCharacter.Length > 0 ? NetworkTime.time : 0d;
        netSide = serverHealth != null ? serverHealth.side : Side.Creatures;
        serverGear = mob != null ? mob.GetComponent<UnitEquipment>() : null;
        // AND WHAT IT WEARS, so the very first message about this creature already says so.
        PublishWorn();
        if (mob != null)
        {
            // WHERE IT IS INSIDE ITS MAP, not where this machine happens to keep that map — see MapManager's
            // parking. A watcher adds back its own answer for where the map is, which for a player is nowhere.
            netPosition = MapManager.InMap(mob.transform.position, mapName);
            netYaw = mob.transform.eulerAngles.y;
            // AND THE SHELL ITSELF STANDS THERE TOO — see StandWhereTheMobStands.
            transform.position = mob.transform.position;
            // AND IN THE CREATURE'S OWN MAP, not in whichever one the server happened to load last. Anything
            // built while the game runs lands in the map that came up most recently, which for a shell made on
            // behalf of a creature somewhere else is the wrong one — and the watcher lists are now built map
            // first, so a shell filed under the wrong map is one no one in the right map is ever told about.
            MapManager.PutInMap(gameObject, mob.gameObject.scene);
        }
    }

    /// <summary>Put this shell where the creature it speaks for actually is.
    ///
    /// <para>The position on the wire is a value this object CARRIES; the position of the object is what the
    /// server measures to decide WHO IS TOLD about it, and those were two different numbers. Every shell was
    /// built at the world origin and left there, so "is this creature near enough to hear about" was really
    /// asking "is this player near the origin" — one answer for every creature in the world at once. A player
    /// who walked away lost the lot, a player who stayed was told about creatures in maps they were nowhere
    /// near, and nothing changed as they travelled, so nothing was ever re-sent.</para></summary>
    void StandWhereTheMobStands() => transform.position = ServerMob.transform.position;

    void Update()
    {
        if (isServer) { PushFromTheRealMob(); return; }
        if (isClient) FollowTheServer();
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

        // ITS WEAPON, the moment the draw state changes rather than on the beat: a draw is shorter than a few beats.
        var drawHost = serverHealth != null ? serverHealth.StatusHost : null;
        if (drawHost != null && drawHost.Draw.Version != drawVersionSent)
        {
            drawVersionSent = drawHost.Draw.Version;
            netDraw = new DrawSaid
            {
                phase = (byte)drawHost.Draw.Phase,
                at = NetworkTime.time - (Time.time - drawHost.Draw.PhaseStart),
                window = drawHost.Draw.Window,
            };
        }

        if (Time.time < nextPush) return;
        nextPush = Time.time + PushInterval;

        netPosition = MapManager.InMap(ServerMob.transform.position, mapName);
        netYaw = ServerMob.transform.eulerAngles.y;
        StandWhereTheMobStands();

        // WHAT GEAR IT IS IN, as a speed. Read off the mover's own live figure, which already has this creature's
        // Slow or Haste folded into it — so a watcher picks the right gait AND churns the legs at the right
        // cadence for a slowed creature, neither of which a plain walking/not-walking flag could say.
        netSpeed = serverAgent != null && serverAgent.Moving ? serverAgent.moveSpeed : 0f;
        if (!isShadow && netSpeed > 0f) netHeading = serverAgent.Heading;

        // WHAT IT WEARS, re-sent only when the worn set has changed — a new life's fresh gear, above all.
        if (serverGear != null && serverGear.Version != wornVersionSent) PublishWorn();

        // BOTH numbers, every push — not just the current one. A creature's maximum is not fixed for life: a
        // re-stat or fresh gear re-derives its whole stat block including how much health it can hold. Sending
        // only the current figure meant every watching player measured a grown creature's bar against the maximum
        // it was BORN with, so a full-health veteran read as overflowing and a hurt one read as healthier than it
        // was. Both are cheap, and a SyncVar that is written the same value it already holds sends nothing.
        if (serverHealth != null)
        {
            netHealth = serverHealth.CurrentHP;
            netMaxHealth = serverHealth.maxHealth;
            netSide = serverHealth.side;
        }
        if (serverBlock != null)
        {
            netMana = serverBlock.CurrentMP;
            netMaxMana = serverBlock.MaxMP;
            netLevel = serverBlock.Level;
        }
    }

    /// <summary>SERVER ONLY: say that this creature just BEGAN an action, and how long it runs. Called at the
    /// moment it happens — never polled for, because the push beat below runs ten times a second and a swing
    /// shorter than that would simply never be seen.
    ///
    /// <para><paramref name="seconds"/> is a number the SERVER owns. It must never be an animation clip's length:
    /// this machine is headless and has no business reading art to decide gameplay timing.</para></summary>
    public void BeganAction(StanceId action, float seconds, uint aimedAt, float connectsAfter)
    {
        if (!isServer || seconds < 0f) return;
        netAction = action;
        netActionSeconds = seconds;
        netActionAimedAt = aimedAt;
        netActionConnectsAt = connectsAfter > 0f ? NetworkTime.time + connectsAfter : 0d;
        // Stamped LAST, and stamped HERE rather than by whoever asked: the moment is the thing every watcher
        // measures the action against, so it is the server's word or it is worth nothing.
        netActionAt = NetworkTime.time;
    }

    /// <summary>SERVER ONLY: this creature began a scripted move. Said once, for the same reason a person's is
    /// (<see cref="MoveSaid"/>): the ten-a-second position push cannot show a move this short. The end point arrives
    /// in world coordinates and is filed in the creature's map coordinates, the same as its position.</summary>
    public void BeganMove(MoveSaid said)
    {
        if (!isServer) return;
        said.to = MapManager.InMap(said.to, mapName);
        said.at = NetworkTime.time;
        netMove = said;
    }

    /// <summary>SERVER ONLY: this creature started or stopped holding a pose.</summary>
    public void HoldPose(bool has, StanceId pose, float window)
    {
        if (!isServer) return;
        netHeld = pose;
        netHeldWindow = Mathf.Max(0f, window);
        netHasHeld = has;
    }

    // ---- everybody else's half: show it ------------------------------------------------------------------

    // The view's own Health, cached at build time. Looked up once rather than per frame, because this runs on
    // every mob on screen every frame and a per-frame component lookup at that rate is exactly what
    // PERFORMANCE.md bans.
    Health viewHealth;
    UnitStatBlock viewBlock;
    // The copy's animation driver, cached at build time for the same reason its health is: this is read every
    // frame on every creature on screen, and a per-frame component lookup at that rate is exactly what
    // PERFORMANCE.md bans. Null on a flat sprite creature, which has no such driver and needs none.
    Agent3DVisual viewVisual;
    // The flat-picture animator, cached at build time for the same reason. Null on a live-3D creature, which
    // destroys its own.
    BillboardAnimator3D viewBillboard;
    // The copy's worn set, cached at build time: dressed from what the server wrote, never worked out.
    UnitEquipment viewGear;
    readonly System.Collections.Generic.List<WornList.Entry> wornEntries =
        new System.Collections.Generic.List<WornList.Entry>(5);
    int lastShownHealth = -1;
    int lastShownMaxHealth = -1;
    int lastShownMana = -1;
    int lastShownMaxMana = -1;

    // The map this machine was standing in when we last decided whether to draw this creature. An INTEGER, not the
    // name: this runs on every creature in the world every frame, and PERFORMANCE.md bans a string compare at that
    // rate. It only ever moves when a person walks through a door.
    int seenMapVersion = -1;
    bool inMyMap;

    // ---- seen through scenery while it and this machine's player are fighting ---------------------------------
    // A creature hidden behind scenery (tall grass, a tree, a wall) is drawn as the same flat shape the player is
    // (OccludedSilhouetteTarget), on THIS machine only, from the moment a blow passes between it and this machine's
    // player until this long after the last one. Chosen here from blows the server reported; nothing is worked out.
    const float FightShownSeconds = 10f;
    // When the shape stops, on this machine's own clock; 0 while it is not being drawn.
    float fightShownUntil;
    // The body's mark, added the first time it is needed and kept with the body through the pool.
    OccludedSilhouetteTarget viewSilhouette;

    /// <summary>A PLAYER'S MACHINE: the server reported a blow between this creature and this machine's player — its
    /// swing at them, hit or miss, or their hit on it. Restarts the time it is drawn through scenery.</summary>
    public void TradedABlowWithMe()
    {
        fightShownUntil = Time.time + FightShownSeconds;
        ShowThroughScenery(true);
    }

    void ShowThroughScenery(bool on)
    {
        if (view == null) return;
        if (viewSilhouette == null)
        {
            if (!on) return;
            // perf-ok: once per body, the first time a creature wearing it fights this machine's player
            viewSilhouette = view.AddComponent<OccludedSilhouetteTarget>();
            viewSilhouette.Refresh();
            return;
        }
        viewSilhouette.Show(on);
    }

    void FollowTheServer()
    {
        // AM I EVEN IN THE PLACE THIS CREATURE IS? Re-decided when this machine changes map, and while this
        // creature has not said which map it is in yet — never once per frame in the settled case.
        //
        // THE SECOND HALF IS NOT BELT AND BRACES. A creature's map arrives as its own value on the wire and can
        // land a frame or two after the creature itself, exactly as its kind does. Deciding once on an empty name
        // and remembering that answer would show a creature from somewhere else for the rest of its life, and the
        // map it belongs to would never be read at all — the bug this whole change exists to remove, restored by
        // the very check meant to fix it.
        bool mapKnown = !string.IsNullOrEmpty(mapName);
        if (seenMapVersion != MapManager.MapVersion || !mapKnown)
        {
            // Latched ONLY once the creature has actually said where it is; until then this stays open so the
            // question is asked again next frame.
            seenMapVersion = mapKnown ? MapManager.MapVersion : -1;
            inMyMap = mapKnown && mapName == MapManager.CurrentMap;
            // GONE THE MOMENT YOU LEAVE, not left standing in the map you walked into. Its body is handed back for
            // reuse exactly as a creature that walked out of range is, so returning costs nothing.
            if (!inMyMap && view != null) DropTheView();
        }

        if (!inMyMap) return;

        if (view == null) { EnsureView(); return; }

        if (fightShownUntil > 0f && Time.time >= fightShownUntil)
        {
            fightShownUntil = 0f;
            ShowThroughScenery(false);
        }

        // The SHARED way a watched body catches up — the same one a watched person uses, so a creature and a
        // player can never drift into gliding differently. It also leaves a body that has arrived completely
        // alone, which is the common case in a town or a quiet field, not the rare one.
        if (facingBuiltFrom != netYaw) { facing = Quaternion.Euler(0f, netYaw, 0f); facingBuiltFrom = netYaw; }
        if (watchedMove.IsNew(netMove)) watchedMove.Tell(netMove, MapManager.InWorld(netMove.to, mapName), view.transform);
        WatchedBody.Follow(view.transform, MapManager.InWorld(netPosition, mapName), facing, watchedMove);

        // A WIND-UP IN PROGRESS: its bar over the body and its ring on the ground, drawn from the shared clock.
        watchedCast.Tick(view.transform, mapName);

        // THE LOOK ITS MULTIPLIER GIVES IT, read from this machine's own copy of the creature list.
        difficultyLook.Show(Multiplier, view.transform);

        // WHAT THE BODY IS DOING, handed over rather than guessed at. Its mover and its brain are switched off on
        // this machine, so the two things its animation used to read from both answer "nothing" forever — which is
        // why a watched creature slid about in its standing pose and never swung at anybody.
        if (viewVisual != null)
        {
            viewVisual.TellTravelSpeed(netSpeed);
            // A moment of zero means this creature has not done anything yet, not that it acted at the dawn of the
            // clock. Both calls are cheap and self-guarding: one beat plays once, however often it is offered.
            if (netActionAt > 0d) viewVisual.TellAction(netAction, netActionAt, netActionSeconds);
            viewVisual.TellHeld(netHasHeld, netHeld, netHeldWindow);
        }
        if (viewBillboard != null)
        {
            viewBillboard.TellTravel(netSpeed > 0f, netHeading);
            // A FLAT CHARACTER'S POSES, from the same beats a live-3D body plays: its casting picture while a wind-up is
            // held, and its attack picture for a swing — each swing once, and only while it is fresh.
            viewBillboard.HoldCast(netHasHeld && netHeld == StanceId.Casting);
            if (netActionAt > 0d && netActionAt != poseShownAt)
            {
                poseShownAt = netActionAt;
                if (NetworkTime.time - netActionAt < netActionSeconds + 0.1d && IsSwing(netAction))
                    viewBillboard.ShowAttack(GameNetwork.DrawnBodyOf(netActionAimedAt),
                                             netActionConnectsAt > 0d ? (float)(netActionConnectsAt - NetworkTime.time) : 0f);
            }
        }

        // A LINE IT SAID, shown once — and only while it is still fresh, so walking up to a creature later does not
        // replay something it said a minute ago. Only a saved character speaks, so the line goes in the chat log too,
        // under the name that character is shown by.
        if (netLineAt > 0d && netLineAt != lineShownAt)
        {
            lineShownAt = netLineAt;
            if (NetworkTime.time - netLineAt < LineFreshSeconds)
            {
                var saved = SavedCharacterCatalog.Get(savedCharacter);
                if (saved != null)
                    ChatController.CreatureSaid(saved.displayName, view != null ? view.transform : null,
                                                viewVisual != null ? viewVisual.Pose : null, netLine);
                else ShowCreatureCall(netLine);
            }
        }

        // Only when a number actually changed — a mob at steady health should cost nothing, and the
        // nameplate below only redraws when it sees a different value anyway.
        //
        // BOTH numbers are watched, not just the current one. Watching only the current figure would have made the
        // maximum's delivery depend on the current one happening to move in the same breath — true when a re-stat
        // heals to full, and quietly false the day anything raises a creature's ceiling without touching what it is
        // holding right now. A bar is a pair of numbers; either one moving is news.
        if (viewHealth != null && (netHealth != lastShownHealth || netMaxHealth != lastShownMaxHealth))
        {
            bool wasDown = lastShownHealth == 0;
            lastShownHealth = netHealth;
            lastShownMaxHealth = netMaxHealth;

            // BACK ON ITS FEET. A creature that dies where it stands is not replaced — the server revives the very
            // same one, so no new shell is ever announced and this copy is never rebuilt. It had played its death
            // for real (faded out, colliders off) and nothing ever told it otherwise, so from every watching seat
            // the creature died once and never came back while the server had it walking around.
            //
            // Restored through the SAME function the server revives its own bodies with, then re-silenced, exactly
            // as a body handed back by the pool is — so a creature that got up can never keep a brain of its own.
            if (wasDown && netHealth > 0) ReviveTheWatchedCopy();

            viewHealth.SetHealthFromServer(netHealth, netMaxHealth);
        }

        // THE MANA BAR, the same pair of numbers watched the same way.
        if (viewBlock != null && (netMana != lastShownMana || netMaxMana != lastShownMaxMana))
        {
            lastShownMana = netMana;
            lastShownMaxMana = netMaxMana;
            viewBlock.SetManaFromServer(netMana, netMaxMana);
        }
    }

    // This creature's casts, as a watcher sees them — drawn by the same code a person's shell draws theirs with.
    readonly WatchedCast watchedCast = new WatchedCast();

    readonly DifficultyLook difficultyLook = new DifficultyLook();

    // This creature's multiplier, read from the creature list once per creature this shell speaks for.
    string multiplierOf;
    float multiplier = CreaturePay.LeastMultiplier;
    float Multiplier
    {
        get
        {
            if (!ReferenceEquals(multiplierOf, defId))
            {
                multiplierOf = defId;
                var def = MonsterCatalog.Get(defId);
                multiplier = def != null ? def.multiplier : CreaturePay.LeastMultiplier;
            }
            return multiplier;
        }
    }

    /// <summary>Play a skill this creature cast, on the body this machine draws for it. Called when the server says so.</summary>
    public void ShowCast(SkillDef def, Transform aimedAt, Vector3 aimPointInMap, int seed)
    {
        if (view == null) return;
        // A flat character lets the skill go in a picture of its own — see BillboardAnimator3D.ShowRelease.
        if (viewBillboard != null) viewBillboard.ShowRelease();
        watchedCast.Play(def, view.transform, aimedAt, MapManager.InWorld(aimPointInMap, mapName), seed);
    }

    /// <summary>Start showing this creature's wind-up — the bar over its head and the ring where the spell will land.</summary>
    public void ShowCastWindup(SkillDef def, double startedAt, float seconds, Vector3 aimPointInMap)
        => watchedCast.BeginWindup(def, startedAt, seconds, aimPointInMap, view != null ? view.transform : null, mapName);

    /// <summary>Stop showing it — finished, broken, or the body is going away.</summary>
    public void StopCastWindup() => watchedCast.StopWindup(view != null ? view.transform : null);

    // A basic swing, of any weapon — what a flat character shows its attack picture for.
    static bool IsSwing(StanceId action) => action == StanceId.Attack || WeaponStances.IsBasicAttack(action);

    // How long after it was said a line is still worth showing to somebody who only just started watching.
    const double LineFreshSeconds = 3d;

    /// <summary>This creature calling out a skill: the name in a bubble over its head.</summary>
    public void ShowCreatureCall(string skillName)
        => ChatController.CreatureCall(view != null ? view.transform : null, viewVisual != null ? viewVisual.Pose : null, skillName);

    void ReviveTheWatchedCopy()
    {
        MobRespawn.Reactivate(view, MapManager.InWorld(netPosition, mapName));
        SilenceTheBrain(view);
        // Reviving put its authored gear back on; what it actually wears is what the server says.
        ApplyWornToView();
        (viewVisual as MobShadow3DVisual)?.ShowDraw(netDraw);
    }

    // DRESS THE WATCHED COPY in what the server says this creature wears — so its swing pose and reach follow the weapon
    // it really holds. Only on arrival or change.
    void ApplyWornToView()
    {
        WornList.Decode(worn, wornEntries);
        if (viewGear != null)
        {
            viewGear.Clear();
            for (int i = 0; i < wornEntries.Count; i++)
            {
                var e = wornEntries[i];
                viewGear.Equip(e.def.slot, new EquipmentInstance(e.def, e.folds) { quality = e.quality });
            }
        }
    }

    void EnsureView()
    {
        if (viewLoading || view != null || string.IsNullOrEmpty(defId)) return;
        viewLoading = true;

        // A body of this kind we already have, standing by. Reusing it costs nothing; building one costs a model
        // fetch and a rig assembled from scratch, and a field of respawning creatures asks for that every few
        // seconds. It comes back as a FRESH creature — the same treatment the server gives its own pooled bodies,
        // through the same function, so a returning body can never be a corpse or keep the last one's wounds.
        //
        // SWITCHING IT ON IS THAT FUNCTION'S JOB, not this one's — the order of the switch-on against the step
        // clamp it does next is exactly what has to stay in one place (see MobRespawn.Reactivate).
        var reused = TakeSpareBody(BodyKind);
        if (reused != null)
        {
            MobRespawn.Reactivate(reused, MapManager.InWorld(netPosition, mapName));
            AdoptView(reused);
            return;
        }

        var def = MonsterCatalog.Get(defId);
        if (def == null)
        {
            GameLog.Warn(LogCat.Net, "no mob called '" + defId + "' in the catalog — it will be invisible.");
            return;
        }

        // NO MAP ON PURPOSE. This copy is not a creature living in a map — it is what a watching machine draws
        // for one the server owns, and it belongs to the world so a door cannot destroy it (see AdoptView).
        MobSpawner.Spawn(def, MapManager.InWorld(netPosition, mapName), default, go =>
        {
            if (go == null) return;
            // NOBODY LEFT TO SPEAK FOR IT. A shell can be taken away while its body is still being fetched — which
            // is now an ordinary event rather than a rare one, because a creature that walks out of range is
            // dismissed and taken back again. Left standing, this body would keep its own brain and its own
            // respawn timer: a monster only this machine can see, hunting people nobody else can watch it hunt.
            if (this == null) { MapManager.DeleteWhenNoMapIsMoving(go); return; }
            AdoptView(go);
        });
    }

    // Everything true of a body this shell is now speaking for, whether it was just built or handed back by the
    // pool. ONE place, so a reused body can never quietly skip a step a fresh one gets.
    void AdoptView(GameObject go)
    {
        SilenceTheBrain(go);
        view = go;
        viewLoading = false;
        viewHealth = go.GetComponent<Health>();   // cached once; never looked up per frame
        viewBlock = go.GetComponent<UnitStatBlock>();
        WatchedTarget = viewHealth;               // cached once; the per-hit door reads it
        byView[go] = this;                        // so a swing at this body finds the shared identity

        // The body belongs to the WORLD, not to the map this machine happens to be standing in when it arrives.
        MapManager.KeepAcrossMaps(go);

        // The live-3D body, if the server said this creature wears one. A body only ever comes back from the pool
        // as what it was built as (see BodyKind), so this either adds it to a fresh body or finds it already there.
        if (isShadow && go.GetComponent<MobShadow3DVisual>() == null) go.AddComponent<MobShadow3DVisual>();

        // Resolved AFTER the line above, or a body being given its live-3D driver for the first time would be
        // filed as having none and would never be told anything for the whole of its life.
        viewVisual = go.GetComponent<Agent3DVisual>();
        // A live-3D creature destroys its flat-picture animator at the end of this frame, so it is never cached.
        viewBillboard = isShadow ? null : go.GetComponent<BillboardAnimator3D>();
        // A banked body keeps its mark, switched off when it was put away; it is on again only while THIS creature's
        // fight with this machine's player is still running (a body dropped by a map change and taken back).
        viewSilhouette = go.GetComponent<OccludedSilhouetteTarget>();
        ShowThroughScenery(fightShownUntil > Time.time);

        // AND WHICH BODY THIS CREATURE WEARS, which is the server's to choose and never this machine's. Said even
        // when the name has not arrived yet: an empty one tells the creature to WAIT for it rather than draw one of
        // its own, and the change hook above finishes the job the moment it lands.
        if (isShadow) (viewVisual as MobShadow3DVisual)?.WearModel(shadowModel);
        // And where its weapon is — a reused body still shows the last creature's.
        if (isShadow) (viewVisual as MobShadow3DVisual)?.ShowDraw(netDraw);

        // A REUSED BODY MUST BE TOLD ITS HEALTH AGAIN. These remember what was last drawn so a creature at steady
        // health costs nothing; left as they were, a recycled body would keep showing the last creature's wounds
        // until its health happened to change.
        lastShownHealth = -1;
        lastShownMaxHealth = -1;
        lastShownMana = -1;
        lastShownMaxMana = -1;

        // This body does not get to decide it died. It shows a blow — the number — but the moment
        // it is DEAD is a fact that arrives from the server like every other fact about it. Without this, a
        // watching machine's own arithmetic could reach zero first, fire a kill nobody else fired, and tear
        // the body down underneath the shell that speaks for it.
        if (viewHealth != null) viewHealth.MarkAsWatchedCopy();

        // WHICH SIDE IT FIGHTS ON and, for a creature that turned back, WHO IT IS — its picture, its size and its name.
        // Every body is told, because a banked body keeps whatever the last creature it drew was told.
        if (viewHealth != null) viewHealth.SetSide(netSide);
        var saved = SavedCharacterCatalog.Get(savedCharacter);
        if (saved != null)
        {
            if (viewHealth != null) viewHealth.savedAs = saved;
            SavedCharacters.Wear(go, saved, netKind);
            // AND THE AURA THAT MARKS IT AS IT APPEARS, for whatever is left of its time — nothing for somebody who
            // only arrives afterwards.
            float auraLeft = SavedCharacterRules.AppearAuraSeconds - (float)(NetworkTime.time - netSavedAt);
            if (netSavedAt > 0d && auraLeft > 0.1f)
                Vfx.Attach(SavedCharacters.AppearAuraFor(saved.behaviour), go.transform,
                           scaleMult: SavedCharacterRules.AppearAuraScale, lifetime: auraLeft);
        }

        // AND WHAT IT WEARS. A reused body still wears the last creature's gear until told.
        viewGear = go.GetComponent<UnitEquipment>();
        ApplyWornToView();

        // ITS NAME, after a turned-back character has put its own on, for every line of the combat log about it.
        var plate = go.GetComponent<MobNameplate>();
        ShownName = plate != null ? plate.displayName : null;
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

        // THE DISTANCE SWITCH LETS GO FIRST, WITH EVERY PART IT HOLDS SWITCHED ON. A watched body lives in the
        // always-loaded Systems scene, where the switch finds nobody near, so a revive or a body handed back from the
        // pool — both re-evaluate the distance on the way in — switched the animator and the body driver OFF, and
        // merely suspending the switch then kept them off: a 3D creature that came back stood static with no pose.
        // Released before the mover is switched off below, because the mover is one of the parts it hands back.
        var lod = mob.GetComponent<SimulationLod>();
        if (lod != null) lod.Release();

        var agent = mob.GetComponent<PathAgent3D>();
        if (agent != null) agent.enabled = false;

        var respawn = mob.GetComponent<MobRespawn>();
        if (respawn != null) respawn.enabled = false;

    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Out of the map this machine is standing in, before it can be caught by a portal. Mirror drops an arriving
        // object in whichever map is current, and travelling unloads that map — so every creature the server had
        // already announced was destroyed on the way through the door and never mentioned again.
        MapManager.KeepAcrossMaps(gameObject);
        byNetId[netId] = this;
    }

    /// <summary>Let this creature's body go and bank it for reuse. The ONE description of "stop drawing this",
    /// because there are now two reasons to — the shell going away, and this machine walking into another map — and
    /// a second copy of these three lines is how one of them ends up keeping a dead key in the lookup table.</summary>
    void DropTheView()
    {
        if (view == null) return;
        // A wind-up's bar and ring are hung on this body, which is about to be banked for another creature — and so is
        // any pose it was told to hold.
        StopCastWindup();
        if (viewVisual != null) viewVisual.TellHeld(false, StanceId.Casting, 0f);
        // EVERY LASTING CUE RIDING THIS BODY COMES OFF FIRST. A glow is held by the name the server gave it, not by
        // the body it is attached to, and this body is about to be banked for the next creature to use — so a
        // poisoned creature you walked away from would hand its glow to whatever came back in its place.
        VfxCue.ForgetAurasOn(netId);
        difficultyLook.Stop();
        // …and a move being drawn on it, with any see-through fade that move put on it.
        watchedMove.Stop();
        // …and the shape it was drawn through scenery with: the next creature to wear this body has fought nobody.
        ShowThroughScenery(false);
        viewSilhouette = null;
        byView.Remove(view);   // before the object goes, or the table keeps a dead key forever
        PutBodyBack(BodyKind, view);
        view = null;
        viewHealth = null;
        viewVisual = null;
        viewBillboard = null;
        viewGear = null;
        WatchedTarget = null;
        viewLoading = false;
    }

    public override void OnStopClient()
    {
        DropTheView();
        // Only while the entry is still THIS shell's — the same guard as the server's table below.
        if (byNetId.TryGetValue(netId, out var held) && held == this) byNetId.Remove(netId);
        base.OnStopClient();
    }

    /// <summary>The shell is going away, so the creature it spoke for goes out of the table with it — otherwise a
    /// dead key sits there forever, exactly as it would on the watching side.
    ///
    /// <para>Removed only while the entry is still THIS shell's. A creature's body is switched off and kept for
    /// reuse rather than destroyed, so a newer shell may already have claimed the same key, and a blind remove
    /// would take the living shell out along with the dead one.</para></summary>
    public override void OnStopServer()
    {
        if (ServerMob != null && byServerMob.TryGetValue(ServerMob, out var held) && held == this)
            byServerMob.Remove(ServerMob);
        base.OnStopServer();
    }

    // ---- the watching side's pool of bodies -------------------------------------------------------------
    // A creature that walks out of range, dies, or is respawned by the server arrives and leaves constantly, and
    // each arrival used to BUILD a whole rigged body and each departure THREW IT AWAY. In a field of respawning
    // monsters that is a model loaded, assembled and discarded every few seconds, per player, forever — the exact
    // churn the server already avoids with a pool of its own. This is that pool for the machines that only watch.
    //
    // Kept by KIND, because a body is only reusable as the same creature it was built as.

    static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.Stack<GameObject>> spare =
        new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Stack<GameObject>>();

    /// <summary>What a banked body IS, not merely which creature it belongs to. A flat one and a live-3D one of the
    /// same creature are not interchangeable — the 3D one hides the sprite and builds a rig it keeps for life — so
    /// handing one back for the other would either strip a body that cannot be rebuilt or leave an invisible mob.
    ///
    /// <para>A LIVE-3D BODY IS FILED UNDER ITS MODEL TOO. A shadow builds its rig once and keeps it for life, so a
    /// body handed back for a creature wearing a different model would come back wearing the wrong one — which is
    /// the very thing putting the model on the wire exists to stop. Bodies of one model still pool among themselves,
    /// which is the case that matters: a field of the same creature reuses freely.</para></summary>
    string BodyKind => isShadow ? defId + " 3d " + shadowModel
                     : string.IsNullOrEmpty(savedCharacter) ? defId
                     : defId + " as " + savedCharacter;

    /// <summary>How many bodies of one kind are worth keeping. Past this they are let go — a player who walks
    /// through a whole map should not still be holding a body for every creature they passed.</summary>
    const int SpareBodiesPerKind = 8;

    static GameObject TakeSpareBody(string kind)
    {
        if (string.IsNullOrEmpty(kind) || !spare.TryGetValue(kind, out var stack)) return null;

        // A body destroyed under us (a scene change, or its creature let go of after two maps unseen) leaves a hole in
        // the stack, so keep taking until one is real.
        while (stack.Count > 0)
        {
            var body = stack.Pop();
            if (body != null && AddressablePrefabs.Unpark(body)) return body;
        }
        return null;
    }

    static void PutBodyBack(string kind, GameObject body)
    {
        if (body == null) return;
        if (string.IsNullOrEmpty(kind)) { MapManager.DeleteWhenNoMapIsMoving(body); return; }

        if (!spare.TryGetValue(kind, out var stack)) { spare[kind] = stack = new System.Collections.Generic.Stack<GameObject>(); }
        if (stack.Count >= SpareBodiesPerKind) { MapManager.DeleteWhenNoMapIsMoving(body); return; }

        body.SetActive(false);
        // A body banked while its blade was in the air would hand the next creature of its kind an empty hand forever.
        var socket = body.GetComponentInChildren<WeaponSocket>(true);   // perf-ok: once per body banked
        if (socket != null) socket.ResetFlights();
        // Waiting here is not being used: it must not keep its creature loaded once nobody has seen one for two maps.
        AddressablePrefabs.Park(body);
        stack.Push(body);
    }
}
