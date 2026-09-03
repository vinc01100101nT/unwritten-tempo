using System;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

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
/// <para><b>Honest about how far the server's say goes.</b> A person's own machine still MOVES them — that is
/// what keeps the controls answering instantly at this camera distance — but the server no longer believes
/// whatever it is told. Every reported position is checked against how far that person could possibly have
/// travelled since the last one the server accepted, and a stream of impossible ones puts them back where the
/// server has them (see <see cref="MoveWasPossible"/>).</para>
///
/// <para>So: CHECKED, not simulated. That covers the whole of what a moving player can lie about — travelling
/// further or faster than the game allows. It is deliberately NOT the other design, where the server walks the
/// body and the client merely predicts; that one needs full prediction and reconciliation and belongs with a
/// camera that can afford it. Nothing here should be read as "the server decides movement", because it does
/// not — it decides whether to believe it.</para>
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

    // HOW HURT THIS PERSON IS, as the server has it. Health travels the same way a creature's does — a plain
    // figure the server keeps writing — rather than being worked out again on each machine, because two machines
    // working it out separately is precisely how they come to disagree about whether somebody is still alive.
    [SyncVar] int netHealth;
    // The mana pool travels beside the health one, on the same beat and the same latch. It had to the moment the
    // server started running skills: it is the server that refuses a cast nobody can pay for, so the number the
    // person is shown must be the number that decision was made against.
    [SyncVar] int netMana;
    [SyncVar] int netMaxMana = 1;
    [SyncVar] int netMaxHealth = 1;

    // HAS THE SERVER ACTUALLY SAID ANYTHING YET. Without this the pair above start at their empty values — no
    // health, out of a pool of one — and the owner believes them the instant they arrive, so every player dropped
    // dead the moment they walked into the world. An empty figure is not a small figure; it is no answer at all.
    [SyncVar] bool healthKnown;

    // The visual body, built once on the machines that need to SEE this player. Null on the owner's own
    // machine, which draws its real avatar instead.
    GameObject body;
    bool bodyLoading;
    // This person's animation driver on THIS machine, cached when their body is built. Read every frame for every
    // person on screen, which is exactly where PERFORMANCE.md bans a component lookup.
    Agent3DVisual bodyVisual;

    /// <summary>How fast this person is travelling on the ground, in units per second — worked out by the SERVER
    /// from the position reports it accepted, so nobody has to measure the catch-up glide (which measures the
    /// glide, not the walk: see <see cref="Agent3DVisual.TellTravelSpeed"/>).</summary>
    [SyncVar] float netSpeed;

    // What this person's body is DOING: which action, the moment it began on the clock both machines share, and
    // the beat it has to finish inside. HELD as a value rather than shouted, so somebody who starts watching
    // half-way through an action picks it up instead of seeing a body stand still through it.
    [SyncVar] StanceId netAction;
    [SyncVar] double netActionAt;
    [SyncVar] float netActionSeconds;

    /// <summary>SERVER ONLY: the fighter this connection swings with — the thing that owns their stats, rolls
    /// their crits and takes their hits. Null on a client, which has no business holding anyone's authoritative
    /// stats, and null on the server until their character has been built.</summary>
    public ICombatUnit ServerCombatUnit { get; private set; }

    /// <summary>Server-only: give this connection the fighter the server will resolve its blows with.</summary>
    public void BindCombatUnitOnServer(ICombatUnit unit)
    {
        ServerCombatUnit = unit;

        // WHATEVER WE ALREADY KNOW, HANDED OVER AT ONCE. The two halves arrive in whichever order the wire and the
        // account service settle in: a person can say which map they are in before their fighter has been built, and
        // a report that landed first would otherwise be lost — leaving that one person's fighter with no map for as
        // long as they stay put, which is exactly as long as nothing would correct it.
        if (unit is Character fighter && fighter != null && !string.IsNullOrEmpty(mapName)) fighter.BindMap(mapName);
    }

    /// <summary>SERVER ONLY: which character this connection is playing. Held so that the name can be released
    /// again when they leave — a character being played is a fact about the server, not about the wire, so it is
    /// deliberately not a synced value and no client is ever told it.</summary>
    public string ServerCharacterId { get; set; }



    /// <summary>SERVER ONLY: is the person on this connection trusted with the game's commands? Set from what the
    /// account service answered when their character was read, and false until then — so a connection whose
    /// account could not be read is refused rather than trusted.
    ///
    /// <para>Deliberately NOT a synced value: no client is told this, about themselves or anybody else. The game
    /// learns its own mark from the account service directly, and this copy is the only one that decides whether
    /// anything actually happens to the world.</para></summary>
    public bool ServerIsGm { get; set; }

    // How often the owner tells the server where it is. Ten times a second is far below the frame rate and
    // still smoother than a person can walk; sending every frame would be bandwidth spent on nothing.
    const float ReportInterval = 0.1f;
    float nextReport;

    // Below these, a change is not worth a packet — a player standing perfectly still should cost nothing.
    const float MoveEpsilon = 0.01f;
    const float TurnEpsilon = 1f;      // degrees
    Vector3 lastReported;
    float lastReportedYaw;

    /// <summary>WHICH MAP this person is standing in, as they last said so. The server cannot work this out for
    /// itself — it holds every map at once and steers nobody — and everything that asks "who is near this creature"
    /// is meaningless without it, because every map shares one set of coordinates.</summary>
    ///
    /// <para>PRIVATE on purpose. Mirror syncs a value whether or not Unity can see it, but a PUBLIC field is also
    /// baked into the saved prefab — and this prefab is one of the two the server loads the moment it starts up.
    /// Kept private, the wire carries it and the stored prefab is left exactly as it was.</para></summary>
    [SyncVar] string mapName = "";

    // The map last SENT, so a person standing in one costs nothing. Separate from the synced value above because on
    // the owner's machine that value is only ever written by the server.
    string lastReportedMap = "";

    // The FIRST report has to go out even if the player has not moved a millimetre. Without it a person who
    // spawns and stands still never sends anything, so the server's idea of where they are stays at the world
    // origin — and everyone else watches them standing in the far corner of the map instead of in town.
    bool hasReportedOnce;

    // The synced facing, held as a rotation. Rebuilt only when the number changes, because the follow below runs
    // every frame on every person on screen and building a rotation from an angle there is a cost paid forever
    // for a value that moves ten times a second at most.
    Quaternion facing = Quaternion.identity;
    float facingBuiltFrom = float.NaN;

    void Update()
    {
        if (isServer) ServerSaveTick();

        // The bag that arrived too early, handed over the moment there is somebody to hand it to.
        if (bagWaitingForACharacter.HasValue && Character.Instance != null)
        {
            var waiting = bagWaitingForACharacter.Value;
            bagWaitingForACharacter = null;
            waiting.Onto(Character.Instance);
            InventoryStore.Instance?.Notify(alsoSave: false);
        }

        // THREE possible askers, not two — and this is where the server used to fall through the crack. "Am I the
        // one steering this?" is false on a server for EVERY player, because the server steers nobody, so it took
        // the watcher's branch and built a visible body for each person who connected: a rigged model fetched,
        // assembled, and blended every frame on a machine with no screen to draw it on.
        //
        // The creature shells never had this problem because they ask a different question. So this asks it too:
        // a body is built only by a machine that is actually WATCHING.
        if (isServer) { PushMyHealth(); ForgetSpeedIfTheyStopped(); return; }
        if (isOwned) { ReportMyPosition(); TakeTheServersWord(); return; }
        if (isClient) FollowTheServer();
    }

    // How fast the last accepted pair of positions says this person was travelling. Server-only working, published
    // as netSpeed the moment a report is believed.
    float acceptedGroundSpeed;

    /// <summary>SERVER ONLY: a person who has stopped walking stops REPORTING — only a step or a turn is worth a
    /// packet — so the speed worked out from their last two positions would otherwise stand at whatever they were
    /// doing when they stopped, and their body would walk on the spot on every other screen forever.
    ///
    /// <para>Silence for longer than two report intervals is the whole of the evidence that they are standing
    /// still, and it is enough: a person who is walking sends ten times a second.</para></summary>
    void ForgetSpeedIfTheyStopped()
    {
        if (netSpeed <= 0f) return;
        if (lastAcceptedAt <= 0f || Time.time - lastAcceptedAt > ReportInterval * 2f) netSpeed = 0f;
    }

    /// <summary>SERVER ONLY: say that this person just BEGAN an action, and the beat it has to finish inside. The
    /// mirror of a creature's, and it exists for the same reason: nothing else on the wire says that a body did
    /// anything, so another person's swing does not exist on your screen at all without it.</summary>
    public void BeganAction(StanceId action, float beatSeconds)
    {
        if (!isServer || beatSeconds <= 0f) return;
        netAction = action;
        netActionSeconds = beatSeconds;
        // Stamped by the server, never by the machine that asked: a moment a client writes is a moment a client
        // can move, and every watcher measures the action against it.
        netActionAt = NetworkTime.time;
    }

    // ---- the server's half: say how hurt this person is --------------------------------------------------

    float nextHealthPush;

    void PushMyHealth()
    {
        if (Time.time < nextHealthPush) return;
        nextHealthPush = Time.time + ReportInterval;

        var fighter = ServerCombatUnit;
        if (fighter == null) return;
        netHealth = fighter.CurrentHP;
        netMaxHealth = Mathf.Max(1, fighter.MaxHP);
        if (fighter is Character person && person != null)
        {
            netMana = person.CurrentMP;
            netMaxMana = Mathf.Max(1, person.MaxMP);
        }
        healthKnown = true;
    }

    // ---- the server's half: say what is running on them --------------------------------------------------

    // SERVER SIDE ONLY, and reused for the life of the process, so a push allocates nothing at all — this runs
    // whenever anything about a person's buffs changes, which during a fight is often, and the server does it for
    // every person signed in at once. It is filled and handed STRAIGHT to the send; it used to be copied into a
    // fresh array first, which threw that array away on every buff change of every player in the world. The
    // receiving side never touches this buffer — the reader hands that side its own list — so the two halves of
    // this exchange share nothing, and a host running both at once could not clear the buffer out from under a send.
    static readonly System.Collections.Generic.List<UnitStates.Mirrored> statesScratch =
        new System.Collections.Generic.List<UnitStates.Mirrored>(16);

    // The store we are listening to, held so the subscription can be dropped when the person leaves.
    UnitStates watchedStates;

    /// <summary>SERVER ONLY: from now on, tell this person about every change to what is running on them. Their
    /// skills are decided here, so their buffs, their charges and the windows they bought exist here — and a person
    /// whose machine was never told would cast a brace and watch nothing happen.</summary>
    public void ReportStatesOf(UnitStates states)
    {
        if (watchedStates != null) watchedStates.OnChanged -= PushMyStates;
        watchedStates = states;
        alreadyDescribed.Clear();   // a different store is a different person: nothing has been explained to them yet
        if (watchedStates != null) { watchedStates.OnChanged += PushMyStates; PushMyStates(); }
    }

    // WHAT THIS PERSON HAS ALREADY BEEN TOLD THE MEANING OF. A buff's hover sentence never changes while the buff
    // runs, so it is sent once, when the buff first appears, and left out of every push after — and there are many
    // pushes, because this fires on every change to every buff during a fight. Their machine keeps the sentence it
    // was given (see UnitStates.MirrorFromServer). Rebuilt to exactly what was just sent, so a buff that ends and
    // comes back is described again rather than arriving blank.
    readonly System.Collections.Generic.HashSet<(StateId, string)> alreadyDescribed =
        new System.Collections.Generic.HashSet<(StateId, string)>();
    readonly System.Collections.Generic.HashSet<(StateId, string)> describedThisPush =
        new System.Collections.Generic.HashSet<(StateId, string)>();

    void PushMyStates()
    {
        if (watchedStates == null || connectionToClient == null) return;
        watchedStates.SnapshotForOwner(statesScratch);

        describedThisPush.Clear();
        for (int i = 0; i < statesScratch.Count; i++)
        {
            var m = statesScratch[i];
            var key = (m.id, m.sourceSkillId ?? "");
            describedThisPush.Add(key);
            if (!alreadyDescribed.Contains(key)) continue;
            m.description = "";              // they have it already
            statesScratch[i] = m;
        }
        alreadyDescribed.Clear();
        foreach (var key in describedThisPush) alreadyDescribed.Add(key);

        TargetStates(connectionToClient, statesScratch);
    }

    /// <summary>…and only to the person it belongs to. Everybody else reads a fight off the bodies and the numbers;
    /// only you need to know how long your own window has left.</summary>
    [TargetRpc]
    void TargetStates(NetworkConnectionToClient target, System.Collections.Generic.List<UnitStates.Mirrored> states)
    {
        var me = Character.Instance;
        if (me == null || me.States == null) return;
        me.States.MirrorFromServer(states);
    }

    /// <summary>ONE way a skill moves a person, whichever way it does it. A blink, a glide and a shove differ only
    /// in how the body travels — never in the fact that the server decided it and the owner's machine performs
    /// it — so they cross the wire as one thing with a kind, rather than as three near-identical messages.</summary>
    public struct Motion
    {
        public enum Kind { Teleport, Glide, Shove }

        public Kind kind;
        /// <summary>Teleport / Glide: where they end up. Shove: the point they are pushed AWAY from.</summary>
        public Vector3 destination;
        /// <summary>Glide: what to face for the whole trip. Ignored unless <see cref="facing"/> is set.</summary>
        public Vector3 faceAt;
        public bool facing;
        /// <summary>Glide: how much faster than a run. Shove: the push's speed.</summary>
        public float speed;
        public bool easeIn, easeOut;
        /// <summary>Glide: the ease window. Shove: how long the push lasts.</summary>
        public float seconds;
        /// <summary>Shove: how long they are staggered for.</summary>
        public float stagger;
    }

    /// <summary>SERVER ONLY: hand this motion to the machine that owns the legs.</summary>
    public void MoveOnOwnersMachine(NetworkConnectionToClient owner, Motion motion)
    {
        // WE MOVED THEM, SO WE DO NOT THEN ACCUSE THEM OF IT. Every motion the server decides — a teleport, a
        // glide, a shove, and the GM commands that ride on them — lands this person somewhere the movement check
        // has no way to reach at walking pace, and the report that follows would look exactly like a lie.
        //
        // It is forgiven HERE rather than at each of the three kinds, because this is the one door they all pass
        // through: a fourth kind added later inherits it instead of quietly missing it.
        ForgiveNextMove();
        TargetMove(owner, motion);
    }

    [TargetRpc]
    void TargetMove(NetworkConnectionToClient target, Motion motion)
    {
        var me = Character.Instance;
        if (me == null) return;

        // Back into this machine's own copy of the map they are standing in.
        Vector3 dest = MapManager.InWorld(motion.destination, MapManager.CurrentMap);
        Vector3 face = MapManager.InWorld(motion.faceAt, MapManager.CurrentMap);
        switch (motion.kind)
        {
            case Motion.Kind.Teleport: me.TeleportTo(dest); break;
            case Motion.Kind.Glide:
                me.GlideTo(dest, motion.speed, motion.easeIn, motion.easeOut, motion.seconds,
                           motion.facing ? face : (Vector3?)null);
                break;
            case Motion.Kind.Shove: me.Shove(dest, motion.speed, motion.seconds, motion.stagger); break;
        }
    }

    // ---- the owner's half: believe it ---------------------------------------------------------------------

    int lastToldHealth = -1;
    int lastToldMaxHealth = -1;
    int lastToldMana = -1;
    int lastToldMaxMana = -1;

    void TakeTheServersWord()
    {
        var me = Character.Instance;
        if (me == null) return;   // the character has not been built yet; it will be, next tick

        // SAID ONCE, the moment there is a character to say it to: from here on this machine SHOWS what happens to
        // you and the server DECIDES it. Without this both halves subtract their own copy of every blow and the
        // person on screen dies at a different moment from the person in the world.
        if (!me.IsWatchedCopy) me.MarkAsWatchedCopy();

        if (!healthKnown) return;   // nothing has been said yet — see the flag

        // Only when a number actually changed — a person at steady health should cost nothing. Both are watched for
        // the same reason a creature's are: a bar is a pair of numbers, and either one moving is news.
        if (netMana != lastToldMana || netMaxMana != lastToldMaxMana)
        {
            lastToldMana = netMana;
            lastToldMaxMana = netMaxMana;
            me.SetManaFromServer(netMana, netMaxMana);
        }

        if (netHealth == lastToldHealth && netMaxHealth == lastToldMaxHealth) return;
        lastToldHealth = netHealth;
        lastToldMaxHealth = netMaxHealth;
        me.SetHealthFromServer(netHealth, netMaxHealth);
    }

    // ---- the owner's half: say where I am ---------------------------------------------------------------

    void ReportMyPosition()
    {
        if (Time.time < nextReport) return;
        nextReport = Time.time + ReportInterval;

        var me = PersistentPlayer3D.Instance;
        if (me == null) return;   // the avatar is not up yet (still loading a map)

        // WHICH MAP, sent on the same beat as the position but only when it CHANGES — a person walks through a door
        // a few times an hour, and the name is the one piece of this report that is not a number.
        // It goes FIRST: a position without a map is a position the server cannot place, and a creature reading the
        // pair mid-swap would briefly hunt somebody in the map they just left.
        string map = MapManager.CurrentMap;
        if (map != lastReportedMap)
        {
            lastReportedMap = map;
            CmdReportMap(map);
        }

        Vector3 p = me.transform.position;
        float yaw = me.transform.eulerAngles.y;

        // TURNING COUNTS AS A CHANGE, and it did not before. Only a step used to be worth a packet, so a person
        // who stood their ground and turned to face you never sent anything — they went on facing whichever way
        // they had last walked, on everybody else's screen, for as long as they stood there. Standing and turning
        // is exactly what a person does when somebody walks up to them, so this was the common case.
        if (hasReportedOnce
            && (p - lastReported).sqrMagnitude < MoveEpsilon * MoveEpsilon
            && Mathf.Abs(Mathf.DeltaAngle(lastReportedYaw, yaw)) < TurnEpsilon) return;

        lastReported = p;
        lastReportedYaw = yaw;
        hasReportedOnce = true;
        CmdReportPosition(p, yaw);
    }



    // ---- writing the character down ---------------------------------------------------------------------
    //
    // THE SERVER IS THE ONE THAT SAVES, now that it is the one that owns what is being saved. It used to be
    // the player's own machine, which meant the server decided what a fight was worth and the client decided
    // what was written down — authoritative during the fight and not authoritative about its result.
    //
    // Written on a beat rather than on every change: a bag change, a level and a coin all arrive within a
    // second of each other during a fight, and each is not worth its own trip to the database.

    bool needsSaving;
    float nextSaveAt;
    bool savingNow;

    // Long enough that a busy fight is one write, short enough that a machine losing power costs seconds.
    const float SecondsBetweenSaves = 15f;

    /// <summary>Server-only: something about this character changed and should be written down before long.</summary>
    public void SaveSoon()
    {
        if (!isServer) return;
        if (!needsSaving) nextSaveAt = Time.time + SecondsBetweenSaves;
        needsSaving = true;
    }

    void ServerSaveTick()
    {
        if (!needsSaving || savingNow || Time.time < nextSaveAt) return;
        SaveNow(null);
    }

    /// <summary>Server-only: write this character down NOW. <paramref name="whenDone"/> is told whether it
    /// landed — the disconnect path waits on it, because a character released before the write has finished is
    /// how the same items end up in two places.</summary>
    public void SaveNow(Action<bool> whenDone)
    {
        if (!isServer) { whenDone?.Invoke(false); return; }

        var c = ServerCharacter;
        if (c == null || string.IsNullOrEmpty(ServerCharacterId))
        {
            whenDone?.Invoke(false);
            return;
        }

        // ONE AT A TIME. Two writes in flight are two whole documents racing, and whichever lands last wins —
        // so a slow first one arriving after a newer one silently puts the older state back. A save asked for
        // while one is out is remembered instead and goes on the next beat.
        if (savingNow)
        {
            needsSaving = true;
            whenDone?.Invoke(false);
            return;
        }

        needsSaving = false;
        savingNow = true;

        // SENT TO THE CHARACTER SERVER, which is the only thing that writes to the store. This machine has no
        // database and never will — it asks, as a SERVER, with the secret the two of them share, and not as
        // whoever happens to be playing.
        var save = c.CaptureSave();

        // AND WHERE THEY WERE STANDING. The character itself does not know — it has no map of its own and its
        // body is this object — so the two things that decide where somebody comes back to are filled in here.
        // Left out, every save wrote an empty map and the origin, so everybody logged in at the front door of
        // the default map however far they had travelled, and the character server could not route them either.
        if (!string.IsNullOrEmpty(mapName))
        {
            save.sceneName = mapName;
            save.posX = netPosition.x;
            save.posY = netPosition.y;
            save.posZ = netPosition.z;
        }

        var body = JsonUtility.ToJson(new SaveEnvelope { progress = save });
        ServerHttp.Send("PUT", MapServerLink.CharacterServerUrl + "/server/characters/" + ServerCharacterId
                        + "/progress", body, MapServerLink.ServerSecret,
            (ok, reply, error) =>
            {
                savingNow = false;
                if (!ok)
                {
                    // SAID OUT LOUD, and the character is marked as still needing writing so the next beat tries
                    // again. A failed save that nobody hears about is a character quietly going stale.
                    Debug.LogWarning("[server] could not write " + playerName + " down — " + error);
                    needsSaving = true;
                    nextSaveAt = Time.time + SecondsBetweenSaves;
                }
                whenDone?.Invoke(ok);
            });
    }

    [Serializable]
    class SaveEnvelope { public SaveData progress; }


    // ---- the other things a person changes about themselves ---------------------------------------------
    //
    // Spending a point, arranging the hotbar and choosing passives all used to happen on the player's own
    // machine and be written down by it. The server writes now, from ITS copy — which had never heard of any of
    // them, so every one of these was quietly undone by the next write, within fifteen seconds.
    //
    // The point is real power and is DECIDED here. The hotbar and the passive choice are asked for and taken as
    // said: they are that person's own arrangement of their own things, and nothing about them can be worth
    // more than somebody else has.

    [Command]
    void CmdSpendStatPoint(int stat)
    {
        var c = ServerCharacter;
        if (c == null) return;
        if (!System.Enum.IsDefined(typeof(PrimaryStat), stat)) return;
        if (!c.AddStat((PrimaryStat)stat)) return;   // no points left, or already at the ceiling
        SaveSoon();
        TargetStatsChanged(connectionToClient, (int)c.Level, c.StatPoints, c.CaptureStatsForOwner());
    }

    [TargetRpc]
    void TargetStatsChanged(NetworkConnectionToClient target, int level, int pointsLeft, int[] primaries)
    {
        var me = Character.Instance;
        if (me == null) return;
        me.RestoreStatsFromServer(level, pointsLeft, primaries);
    }

    [Command]
    void CmdSetHotbar(string[] bindings)
    {
        var c = ServerCharacter;
        if (c == null || bindings == null || bindings.Length > 64) return;
        c.Hotbar.Restore(bindings);
        SaveSoon();
    }

    [Command]
    void CmdSetPassives(string[] chosen)
    {
        var c = ServerCharacter;
        if (c == null || chosen == null || chosen.Length > 64) return;
        c.PassiveSkills.Restore(chosen);
        SaveSoon();
    }

    /// <summary>Client-side: tell the server this person spent a point / rearranged their bar / changed which
    /// passives they are running. Silently does nothing with no world, where their own machine already is the
    /// only thing that knows.</summary>
    public void AskToSpendStatPoint(PrimaryStat stat) => CmdSpendStatPoint((int)stat);
    public void AskToSetHotbar(string[] bindings) => CmdSetHotbar(bindings);
    public void AskToSetPassives(string[] chosen) => CmdSetPassives(chosen);

    // ---- the bag: the server holds it, this machine shows it ---------------------------------------------
    //
    // WHO OWNS WHAT A PERSON IS CARRYING. It is the server, now, for the same reason it owns their health: the
    // alternative is a machine the player controls deciding what they own, and then being asked to write it
    // down. Every change is ASKED for here and DECIDED there; what comes back is the whole bag, and this
    // machine simply wears it.
    //
    // The asking is by POSITION IN THE BAG, not by naming an item: the two copies are the same list in the same
    // order, so a position is unambiguous, and a request naming an item could be answered with the wrong copy
    // of two identical swords carrying different upgrades.

    /// <summary>Server-only: hand this connection its belongings as they now stand. Called after every change
    /// the server makes to them, so there is exactly one way the client ever learns what it is holding.</summary>
    public void PushBagToOwner()
    {
        if (!isServer) return;
        if (ServerCombatUnit is not Character fighter || fighter == null) return;
        TargetBagChanged(connectionToClient, BagWire.From(fighter));
    }

    // WHAT ARRIVED BEFORE THERE WAS ANYBODY TO GIVE IT TO. The bag can land before this machine has built its
    // character, and dropping it left the person with nothing until something else happened to change it —
    // with no way to ask again.
    BagWire? bagWaitingForACharacter;

    [TargetRpc]
    void TargetBagChanged(NetworkConnectionToClient target, BagWire bag)
    {
        var me = Character.Instance;
        if (me == null) { bagWaitingForACharacter = bag; return; }
        bag.Onto(me);
        // The window redraws from what just arrived. NOT marked as needing saving: this IS what the server has,
        // so writing it back would be this machine telling the server what the server just told it.
        InventoryStore.Instance?.Notify(alsoSave: false);
    }

    /// <summary>The server's copy of whoever is playing here, or null when this connection has no fighter
    /// yet. Every bag command below goes through it, so "is there anybody to do this to" is asked once.</summary>
    Character ServerCharacter => ServerCombatUnit as Character;

    /// <summary>Is the thing at <paramref name="index"/> still the thing the asker was looking at? The two
    /// copies are the same list in the same order, so a position is enough to name a stack — right up until the
    /// list moves between the click and the request arriving, which two quick clicks are enough to do. The
    /// asker therefore says WHAT it believed was there, and a request that no longer matches is dropped rather
    /// than carried out on whatever slid into that place.</summary>
    bool StillTheSameStack(Character c, int index, string expectedItemId)
    {
        if (c == null || index < 0 || index >= c.Bag.Count) return false;
        var e = c.Bag[index];
        if (e == null || e.def == null) return false;
        if (!string.IsNullOrEmpty(expectedItemId) && e.def.id != expectedItemId)
        {
            Debug.LogWarning("[server] " + playerName + " asked about position " + index + " expecting '"
                             + expectedItemId + "' but it now holds '" + e.def.id + "' — ignored.");
            return false;
        }
        return true;
    }

    [Command]
    void CmdEquipFromBag(int index, string expectedItemId)
    {
        var c = ServerCharacter;
        if (!StillTheSameStack(c, index, expectedItemId)) return;
        var e = c.Bag[index];
        if (e == null || !e.IsEquip || e.instance == null) return;

        var prev = c.GetEquippedInstance(e.def.slot);
        c.SetEquipped(e.def.slot, e.instance);
        c.Bag.RemoveAt(index);
        if (prev != null && prev.def != null)
            c.Bag.Add(new BagEntry { def = prev.def, count = 1, instance = prev });
        PushBagToOwner();
        SaveSoon();
    }

    [Command]
    void CmdUnequipToBag(int slot)
    {
        var c = ServerCharacter;
        if (c == null) return;
        var worn = c.GetEquippedInstance((EquipSlotType)slot);
        if (worn == null) return;

        c.SetEquipped((EquipSlotType)slot, (EquipmentInstance)null);
        if (worn.def != null) c.Bag.Add(new BagEntry { def = worn.def, count = 1, instance = worn });
        PushBagToOwner();
        SaveSoon();
    }

    [Command]
    void CmdReorderBag(int from, int to, string expectedItemId)
    {
        var c = ServerCharacter;
        if (!StillTheSameStack(c, from, expectedItemId) || from == to) return;

        var moving = c.Bag[from];
        c.Bag.RemoveAt(from);
        c.Bag.Insert(Mathf.Clamp(to, 0, c.Bag.Count), moving);
        PushBagToOwner();
        SaveSoon();
    }

    // ---- what a client asks for, from the bag window -----------------------------------------------------
    // Public so InventoryStore can send them without knowing anything about the wire.

    public void AskToEquip(int index, string itemId) => CmdEquipFromBag(index, itemId);
    public void AskToUnequip(EquipSlotType slot) => CmdUnequipToBag((int)slot);
    public void AskToReorder(int from, int to, string itemId) => CmdReorderBag(from, to, itemId);

    // ---- loot: asked for, granted once -------------------------------------------------------------------

    /// <summary>WHAT WE ASKED FOR, held only until the answer comes back, so the reward can be shown with the
    /// drop's own sound. A person can only be reaching for one thing at a time.</summary>
    WorldPickup askedFor;

    /// <summary>Client-only: ask the server for a drop. Goes through <see cref="GameNetwork.SendPickupToServer"/>,
    /// which is the one place that decides whether a drop is ours to give or the server's.</summary>
    public void AskForLoot(NetLoot shell, WorldPickup body)
    {
        if (shell == null) return;
        askedFor = body;
        CmdClaimLoot(shell.netId);
    }

    /// <summary>The server decides. A drop pays out ONCE however many people reach for it in the same breath —
    /// the loser is simply told nothing, and watches it vanish like everybody else.</summary>
    [Command]
    void CmdClaimLoot(uint shellId)
    {
        if (!NetworkServer.spawned.TryGetValue(shellId, out var identity) || identity == null) return;

        var loot = identity.GetComponent<NetLoot>();
        if (loot == null || !loot.ClaimOnServer()) return;

        // PAID INTO THE BAG THE SERVER HOLDS. It used to be paid on the winner's own machine, which was the only
        // place a bag existed; now the bag is here, and a coin added on their machine alone would be rubbed out
        // by the next thing the server said they were carrying.
        var c = ServerCharacter;
        if (c != null)
        {
            var def = ItemCatalog.Resolve(loot.ItemId);
            if (def != null)
            {
                if (def.slot != EquipSlotType.None)
                    c.Bag.Add(new BagEntry { def = def, count = 1, isNew = true,
                                             instance = EquipmentInstance.CreateRolled(def, null) });
                else
                {
                    var stack = c.Bag.Find(x => x.def == def && !x.IsEquip);
                    if (stack != null) stack.count += 1;
                    else c.Bag.Add(new BagEntry { def = def, count = 1, isNew = true });
                }
            }
            if (loot.Gold > 0) c.AddGold(loot.Gold);
            PushBagToOwner();
            SaveSoon();
        }

        // The winner is told BEFORE the drop is taken away, so the two arrive in that order. What they are told
        // now is only that they got it — the reward itself already happened, up here.
        TargetLootGranted(connectionToClient, loot.ItemId, loot.Gold);
        NetworkServer.Destroy(loot.gameObject);
    }

    /// <summary>…and only the person who won it is told, so their own machine can play the sound and the
    /// reaching gesture. It adds NOTHING: what they own arrived with the bag.</summary>
    [TargetRpc]
    void TargetLootGranted(NetworkConnectionToClient target, string itemId, int gold)
    {
        WorldPickup.Celebrate(askedFor != null ? askedFor.collectSfx : null);
        askedFor = null;
    }

    [Command]
    void CmdReportMap(string map)
    {
        // NOTHING TO DO IF THEY ARE STILL WHERE THEY WERE, and saying so matters far more than the work it saves.
        // The end of this method forgives the next move, because walking through a door is a jump no checker could
        // tell from a lie. A client that simply repeats its map before every step would therefore be forgiven
        // before every step — which is the whole movement check switched off, by a client, for free.
        if (map == mapName) return;

        mapName = map;

        // AND THIS BODY MOVES HOUSE WITH THEM. The server decides who is told about whom by asking which map
        // each is in before it measures any distance, and the answer it reads is the scene the object actually
        // sits in — not the name on the wire. A person whose body stayed in the map they started in would walk
        // into a town nobody there could see them arrive in, and go on being visible to the map they left.
        // A MAP THIS SERVER IS NOT HOLDING IS SAID OUT LOUD, because the alternative is the quietest failure in
        // this whole layer. Putting a body in a map only works for a map that is actually open here; for any other
        // it does nothing, with no error — and the body is then left filed under the map they LEFT, at the new
        // map's coordinates. The watcher lists are built map first, so that person and the people they walked away
        // from go on seeing each other, at positions that mean nothing, indefinitely.
        var into = SceneManager.GetSceneByName(map);
        if (!into.IsValid() || !into.isLoaded)
            Debug.LogWarning("[server] " + playerName + " says they are in '" + map
                             + "', which this server is not holding — they will be filed under '"
                             + gameObject.scene.name + "' and seen by the wrong people. Add it to the map list.");
        MapManager.PutInMap(gameObject, into);

        // AND THE FIGHTER IS TOLD TOO. It is the thing creatures actually look at, and it has no body in any map of
        // its own to be found in — so if this is not pushed across, every creature goes on hunting the whole world.
        if (ServerCombatUnit is Character fighter && fighter != null) fighter.BindMap(map);

        // WORTH WRITING DOWN, because where somebody is standing is part of their character — coming back to
        // the wrong map is the one save mistake a player notices immediately.
        SaveSoon();

        // A DOOR IS NOT A LIE. Walking into another map puts this person somewhere with no relation at all to where
        // they were standing a moment ago, and the movement checker would read that as the largest teleport it has
        // ever seen. The map is reported before the position that follows it, so forgiving here covers the jump.
        //
        // RATIONED, because this is a client saying it. A person walks through a door a few times a minute; a
        // client that wanted free teleports would flip between two real maps as fast as it could and be forgiven
        // each time. A second between forgivenesses costs an honest player nothing and takes that down to one
        // jump a second — which is not nothing, and is why the real answer is for the SERVER to decide that
        // somebody changed map rather than being told. That belongs with the character server and its map
        // registry; until then this is the narrow version.
        if (Time.time - lastForgivenMapChangeAt < ForgivenMapChangeGap)
        {
            Debug.LogWarning("[server] " + playerName + " changed map again after only "
                             + (Time.time - lastForgivenMapChangeAt).ToString("0.00")
                             + "s — not forgiving the jump this time.");
            return;
        }

        lastForgivenMapChangeAt = Time.time;
        ForgiveNextMove();
    }

    // When the last map change was let off the movement check, and how long until another one can be. Server-only.
    float lastForgivenMapChangeAt = -999f;
    const float ForgivenMapChangeGap = 1f;

    [Command]
    void CmdReportPosition(Vector3 p, float yaw)
    {
        // COULD THEY ACTUALLY HAVE GOT THERE. A person's own machine works out where they are standing and then
        // says so, which is the one thing in the world a player is still trusted about — so this is where that
        // trust is checked. A report that could not be true is DROPPED: nothing is written, so the server goes on
        // holding the last place this person could actually have reached, and that is what everybody else is told.
        if (!MoveWasPossible(p))
        {
            // PUT BACK only once the refusals have piled up. A single one is the game's own doing far more often
            // than it is a lie — and dropping it already cost the liar the move, so there is no hurry.
            //
            // AND ONLY IF THERE IS SOMEWHERE TO PUT THEM. Until one report has been believed, the position the
            // server holds is the empty one every object starts at, and "putting them back" would mean dragging
            // somebody who has never successfully said where they are to the corner of their map.
            if (haveAcceptedAPosition && refusedInARow >= RefusalsBeforeCorrecting)
            {
                TargetReturnTo(connectionToClient, netPosition);
                // They are being moved by us now, so the next thing they say is measured from there and not
                // read as one more lie.
                ForgiveNextMove();
            }
            return;
        }

        netPosition = p;
        netYaw = yaw;
        netSpeed = acceptedGroundSpeed;

        // AND THE SERVER'S OWN COPY ACTUALLY STANDS THERE. Writing only the figure above left this object sitting
        // at the world origin on the server for the whole of a player's life — which was harmless while nothing on
        // the server cared where anybody was, and is not any more: this transform IS where that person is, as far as
        // the character the server holds for them is concerned. Left at the origin, every one of their blows is
        // measured from the corner of the map and refused as out of reach, and every creature hunts the origin.
        //
        // Server-local: nothing here is a networked transform, so this moves the server's copy and sends nothing.
        // WHERE THIS MACHINE KEEPS THAT MAP. What arrives is where the person is inside their own map; a
        // server sets each map it holds down somewhere of its own, so the two are not the same number here.
        transform.SetPositionAndRotation(MapManager.InWorld(p, mapName), Quaternion.Euler(0f, yaw, 0f));
    }

    // ---- is that where they could be? -------------------------------------------------------------------
    //
    // THE ONE PLACE A PLAYER IS STILL TRUSTED, and so the one place worth checking. Their machine steers their
    // own body and reports where it ended up; nothing else about them is taken on trust any more.
    //
    // WHY THIS IS FORGIVING ON PURPOSE. A wrong refusal is worse than a missed cheat: it drags an honest player
    // backwards for something the game itself did — a door, a respawn, a shove, a frame the machine spent
    // loading something. So the ceiling is several times the fastest anybody can legitimately travel, a whole
    // step of slack is given away on top, and one bad report is never acted on at all. A speed or teleport hack
    // does not produce ONE impossible report, it produces every report — so counting them in a row separates the
    // two cleanly, and still corrects within about half a second.

    // Server-only, so no [SyncVar]: a client has no business knowing what it is being measured against.
    float lastAcceptedAt;
    int refusedInARow;

    // Has ONE report ever been believed. Not the same question as lastAcceptedAt, which is deliberately cleared
    // every time the server moves somebody itself — this one is asked to find out whether there is a real place to
    // put a person back to, and once it is true it stays true for the life of the connection.
    bool haveAcceptedAPosition;

    // How many impossible reports in a row before the person is actually moved back. They arrive ten times a
    // second, so this is a fifth of a second of sustained impossibility — far past any hitch, far short of
    // anything a player would feel.
    const int RefusalsBeforeCorrecting = 4;

    // How many times the fastest legitimate travel speed still counts as possible. Generous by design; it is
    // catching "across the map instantly", not "half a metre quick".
    const float SpeedForgiveness = 3f;

    // Free movement allowed in every report regardless of the clock, in world units. Covers the frame a machine
    // spends loading something, and the small shove a crowd or a slope can give.
    const float StepSlack = 1.5f;

    // Nothing legitimate crosses this in one report, whatever the clock says. The backstop for a report that
    // arrives after a long silence, where elapsed time alone would justify almost any distance.
    const float NeverThisFarInOneStep = 40f;

    /// <summary>Server-only: accept whatever position comes next without question. Called when the server ITSELF
    /// moved this person — walking through a door is a jump the checker must not read as a lie.</summary>
    void ForgiveNextMove()
    {
        lastAcceptedAt = 0f;
        refusedInARow = 0;
    }

    /// <summary>SERVER ONLY, for the places outside this object that move somebody — coming back from the dead is
    /// granted by the world, not by this body, and the jump to a spawn point has to be forgiven the same way a
    /// teleport is. Deliberately NOT reachable from a client: it is the one thing that turns the check off.</summary>
    public void ForgiveNextMoveAfterServerMove() => ForgiveNextMove();

    bool MoveWasPossible(Vector3 p)
    {
        // A number that is not a number. It would poison every comparison after it and settle nowhere, so it is
        // refused before anything else looks at it. It still COUNTS as a refusal: a stream of these has to end in
        // the person being put back like any other, and counting it also means the very first report of a life
        // cannot drag somebody to the map's origin — there is nowhere sensible to put them back to yet, and the
        // count has not been reached.
        if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) ||
            float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z))
        {
            refusedInARow++;
            return false;
        }

        float now = Time.time;

        // The first report of a life, or the first after the server moved them itself. There is nothing to
        // measure against, so it is taken as read and becomes the thing the next one is measured against.
        if (lastAcceptedAt <= 0f)
        {
            lastAcceptedAt = now;
            refusedInARow = 0;
            haveAcceptedAPosition = true;
            // Nothing to measure a speed against, and a jump the server itself ordered is not travel.
            acceptedGroundSpeed = 0f;
            return true;
        }

        float elapsed = now - lastAcceptedAt;

        // HOW FAST COULD THIS PERSON POSSIBLY BE GOING. The sprint gear is the fastest anything a player owns
        // travels, and a Haste multiplies it — both read from the same places the mover itself reads, so the
        // ceiling cannot drift away from the speed it is meant to describe.
        var spec = CharacterSpec.Active;
        float top = spec != null ? spec.moveSpeed * UnitScale.ForPlayer * spec.gaitSpeed : 8f;
        var unit = ServerCombatUnit;
        if (unit != null && unit.States != null) top *= Mathf.Max(1f, unit.States.MoveMult);

        float allowed = top * SpeedForgiveness * elapsed + StepSlack;

        // MEASURED FLAT. Falling is not travelling: gravity can move somebody down faster than they can ever walk,
        // and a checker that counted it would refuse honest players stepping off ledges. The height is covered by
        // the outright cap below instead, which nothing legitimate reaches in either direction.
        Vector3 was = netPosition;
        float dx = p.x - was.x, dz = p.z - was.z;
        float movedSqr = dx * dx + dz * dz;

        bool possible = movedSqr <= allowed * allowed
                        && (p - was).sqrMagnitude <= NeverThisFarInOneStep * NeverThisFarInOneStep;

        if (possible)
        {
            // HOW FAST THEY ACTUALLY WENT, from the two positions this machine believed — measured here because
            // this is the only place that holds both the distance and the time it took. Flat, like the check
            // above: falling is not travelling, and a body does not break into a run for stepping off a ledge.
            acceptedGroundSpeed = elapsed > 0f ? Mathf.Sqrt(movedSqr) / elapsed : 0f;
            lastAcceptedAt = now;
            refusedInARow = 0;
            haveAcceptedAPosition = true;
            return true;
        }

        // IMPOSSIBLE — so it is not written down, whichever number this is in the run. An earlier version of this
        // counted the first few and let them through, which is a different thing entirely and a much worse one:
        // the impossible place became the place the server held, the NEXT report was a small honest-looking step
        // away from it, and the count reset. A liar who jumped every other report was never once corrected, and
        // every jump landed. Refusing costs a cheat the move immediately; the count below only decides when the
        // person is additionally dragged back, which is the part worth being slow about.
        //
        // The clock still advances. The next report is measured from now rather than from whenever the last
        // believable one was, or one stalled frame would make everything after it look impossible too.
        lastAcceptedAt = now;
        refusedInARow++;

        // Said once per run of refusals rather than per report — these arrive ten times a second, and a line each
        // would bury the log the moment one turned up.
        if (refusedInARow == RefusalsBeforeCorrecting)
            Debug.LogWarning("[server] " + playerName + " reported " + refusedInARow
                             + " impossible moves in a row — putting them back.");
        return false;
    }

    /// <summary>Told only to the person who has to move: everybody else is already being sent the position the
    /// server kept, so they never saw the refused one and have nothing to undo.</summary>
    [TargetRpc]
    void TargetReturnTo(NetworkConnectionToClient target, Vector3 whereTheServerHasYou)
    {
        var me = Character.Instance;
        if (me == null) return;

        // THE SAME DOOR EVERY OTHER REPOSITION GOES THROUGH. Writing the transform directly is not the same thing:
        // this body carries a CharacterController, which fights a bare position write, and whatever the legs were
        // already doing — a route, a dash, a glide — would carry straight on from the new spot. TeleportTo is the
        // one place that knows how to put somebody somewhere and have them actually be there, and the server's own
        // teleport already uses it (see TargetMove).
        me.TeleportTo(MapManager.InWorld(whereTheServerHasYou, MapManager.CurrentMap));
    }

    // ---- everybody else's half: show them ---------------------------------------------------------------

    // The map this machine was standing in when we last decided whether to draw this person. An INTEGER, not the
    // name: this runs on every watched person every frame, and a string compare at that rate is the cost
    // PERFORMANCE.md bans. It only ever moves when somebody walks through a door.
    int seenMapVersion = -1;
    bool inMyMap;

    void FollowTheServer()
    {
        // AM I EVEN IN THE PLACE THIS PERSON IS? The creature half has always asked this; the person half never
        // did, and got away with it only because two people in maps set far apart fall outside each other's view
        // distance — an accident of an unrelated bandwidth rule, not a rule about maps. Two people in maps that
        // overlap would have watched each other walk through the scenery.
        //
        // The second half is not belt and braces: a person's map arrives as its own value and can land a frame
        // after their position, so a single answer taken on an empty name would be wrong for the rest of their
        // session.
        bool mapKnown = !string.IsNullOrEmpty(mapName);
        if (seenMapVersion != MapManager.MapVersion || !mapKnown)
        {
            seenMapVersion = mapKnown ? MapManager.MapVersion : -1;
            inMyMap = mapKnown && mapName == MapManager.CurrentMap;
            if (!inMyMap) DropTheBody();
        }

        if (!inMyMap) return;

        if (body == null) { EnsureBody(); return; }

        // Glide rather than snap. Updates arrive ten times a second; moving straight to each one would read as a
        // stutter, so the body walks toward the latest known position instead. The SHARED way a watched body does
        // that — the identical one a watched creature uses, so the two can never drift into gliding differently.
        if (facingBuiltFrom != netYaw) { facing = Quaternion.Euler(0f, netYaw, 0f); facingBuiltFrom = netYaw; }
        WatchedBody.Follow(body.transform, MapManager.InWorld(netPosition, mapName), facing);

        // WHAT THIS PERSON IS DOING, handed over rather than worked out from where their body has been placed.
        // Without it their character walked in a fixed standing pose whenever the glide flickered, and never swung
        // at all — their damage numbers climbed over somebody standing perfectly still.
        if (bodyVisual != null)
        {
            bodyVisual.TellTravelSpeed(netSpeed);
            if (netActionAt > 0d) bodyVisual.TellAction(netAction, netActionAt, netActionSeconds);
        }
    }

    /// <summary>Stop drawing this person. Two reasons to, so one description of it: they left, or this machine
    /// walked into another map.</summary>
    void DropTheBody()
    {
        if (body != null) Destroy(body);
        body = null;
        bodyVisual = null;
        bodyLoading = false;
    }

    void EnsureBody()
    {
        // A body is on its way — not "one was asked for once". The same latch on a creature's shell meant a body
        // destroyed under us could never be drawn again for the rest of that person's session; both halves of the
        // pair get the same rule, or the one left behind is the bug nobody remembers to look for.
        if (bodyLoading || body != null) return;
        if (string.IsNullOrEmpty(modelName)) return;    // identity has not arrived yet — it will, next tick
        bodyLoading = true;

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

            // Placed AND turned, not just placed. The body appears already standing where this person is and
            // already looking the way they are looking; setting only the position meant somebody walked into view
            // facing due north and then swung round to their real heading in front of you.
            root.transform.position = MapManager.InWorld(netPosition, mapName);
            root.transform.rotation = Quaternion.Euler(0f, netYaw, 0f);

            // The SHARED character body — the same recipe behind townsfolk and your own avatar. A remote
            // player must not be a fourth way of making a character, or the four drift apart.
            var pose = Character3DBody.Build(root, model, spec != null ? spec.animatorController : null,
                                             spec != null ? spec.bodyHeight : 0.327f);

            // …AND SOMETHING TO DRIVE IT. The builder hands the body's stance over to a driver and stops there, so
            // without this the person stood in the authored Idle pose forever and slid around town without ever
            // taking a step. The driver is the shared one every AI character already uses — walking, the
            // walk-or-stand decision, and seating the feet on the ground are not written a second time here.
            var visual = root.AddComponent<RemotePlayer3DVisual>();
            visual.BindBody(pose);
            // Held so the per-frame follow below can hand this body the server's word without looking a component
            // up on every frame of every person on screen.
            bodyVisual = visual;

            // The body belongs to the WORLD, not to the map this machine is standing in — same reason a creature's
            // does. Travelling unloads the map you left, and everything built into it goes with it.
            MapManager.KeepAcrossMaps(root);

            body = root;
            bodyLoading = false;
        });
    }

    /// <summary>Show a blow the server decided landed on this person.
    ///
    /// <para>The SAME two jobs this class already splits by: on the machine of the person it happened to, the blow
    /// goes through the ordinary door every hit has always gone through — the number, the flash, the stagger, the
    /// passives that listen for being hurt — with only the health left for the server to say. On everybody else's
    /// machine it is a number over somebody's head, which is all a bystander can honestly know.</para></summary>
    public void ShowBlow(in DamageInfo info, in UnitMitigation.Outcome mitigated)
    {
        if (isOwned)
        {
            var me = Character.Instance;
            if (me == null) return;
            CombatAuthority.ApplyFromServer(me, info);

            // WHAT THE BLOW RAN INTO, drawn here because this is the first machine in the chain with a screen. The
            // server decided all of it and suppresses every floating number it makes, and none of these survives in
            // the figure above — a fully braced or parried blow arrives as a plain zero. Over the body, beside the
            // white number, exactly where each of them used to appear.
            if (!mitigated.Nothing)
            {
                var av = me.Body;
                if (av != null) UnitMitigation.Show(mitigated, UnitAnchor.Overhead(av));
            }
            return;
        }

        if (body == null || info.amount <= 0 || info.missed) return;
        // The SAME two colours the person it happened to sees over their own body — one owner, read here.
        DamagePopup.Spawn(UnitAnchor.Overhead(body.transform), info.amount,
                          info.isMagic ? Character.MonsterMagicColor : Character.MonsterHitColor);
    }

    /// <summary>Show this person casting a skill, on the machines watching them. The EFFECT and nothing else — no
    /// damage, no buff, no charge, no cost. Everything the skill actually did was decided on the server against the
    /// character it holds for them, and reaches this machine through the doors that already announce a blow, a heal
    /// or a death.
    ///
    /// <para>The sequence is played directly rather than through a skill runner, deliberately: a runner is a thing
    /// that CASTS, and one standing on somebody else's body would read its costs, gates and piles off whoever is at
    /// this keyboard. A watcher needs a picture, and a picture is all this builds.</para></summary>
    public void ShowCast(SkillDef def, Transform aimedAt, Vector3 aimPointInMap, int seed)
    {
        if (def == null || def.vfx == null) return;

        // WHOSE BODY TO PLAY IT ON. Your own machine keeps no watched copy of you — it draws your real avatar — so
        // your own casts play there, and everybody else's play on the copy built for them.
        Transform on = isOwned ? (Character.Instance != null ? Character.Instance.Body : null)
                               : (body != null ? body.transform : null);
        if (on == null) return;

        Vector3 point = MapManager.InWorld(aimPointInMap, isOwned ? MapManager.CurrentMap : mapName);
        Vector3 target = aimedAt != null ? aimedAt.position : point;
        showing = def;
        VfxDirector.Play(def.vfx, new VfxContext
        {
            origin = on.position,
            target = target,
            effectPoint = point,
            follow = on,
            targetUnit = aimedAt,
            scaleMult = 1f,
            // THE SERVER'S OWN NUMBER, so this picture scatters into exactly the places the server measured its
            // damage from. Without it every screen drew a different shower.
            seed = seed,
            // …and the two answers that decide how much there is to look at: how long a looping step stays, and how
            // many copies of a carrier fly. Read off the SAME authored skill the server read them off, so a watcher
            // cannot draw one bolt where five were fired.
            stayForTick = stayForShown,
            fanForTick = fanForShown,
        });
    }

    // The skill currently being drawn, and the two resolvers that read its per-step answers. Fields rather than
    // fresh lambdas per cast, so drawing somebody's flurry allocates nothing per beat.
    SkillDef showing;
    System.Func<string, float> stayForShown;
    System.Func<string, ProjectileFan> fanForShown;

    void Awake()
    {
        stayForShown = tid => showing != null ? showing.StayForTick(tid) : 0f;
        fanForShown = tid => showing != null ? showing.FanForTick(tid) : ProjectileFan.Single;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        MapManager.KeepAcrossMaps(gameObject);   // survives a portal, exactly as a creature's shell does
    }

    /// <summary>THIS IS THE PROOF that this person was let in. The server hands out a body only to a connection
    /// it accepted, so this arriving on the machine that owns it is the one thing a refused connection never
    /// gets — which is why the retry count and the waking of the world both hang off it rather than off the wire
    /// coming up, which the refused get too.</summary>
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        GameNetwork.ConfirmedInTheWorld();
    }

    public override void OnStopClient()
    {
        if (body != null) Destroy(body);
        base.OnStopClient();
    }
}
