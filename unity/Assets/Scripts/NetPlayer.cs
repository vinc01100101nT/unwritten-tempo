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
    [SyncVar(hook = nameof(OnModelChanged))] public string modelName = "";

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

    // …and the pose it is HOLDING for as long as something takes: a jump frozen in the air, a dodge roll, a flinch,
    // a body lying dead. These are not beats that play once and finish — they last until the thing they are showing
    // is over — so they travel as a value that stands until it is replaced, not as a moment. `netHasDriven` false
    // means "nothing special", and the body falls back to walking or standing as its speed says.
    [SyncVar] bool netHasDriven;
    [SyncVar] StanceId netDriven;
    [SyncVar] float netDrivenWindow;
    [SyncVar] bool netDrivenRelease;

    // The standing pose UNDERNEATH all of that — guarding, casting, sitting. It outlives any one action, so it is
    // its own value rather than a second meaning for the one above.
    [SyncVar] bool netHasHeld;
    [SyncVar] StanceId netHeld;
    [SyncVar] float netHeldWindow;

    // WHICH TRAVEL GEAR they are in. Speed alone cannot tell a sprint from a hasted run — both are simply fast — so
    // the machine that owns the legs says which it is, and the body standing in for them on every other screen plays
    // the right one instead of guessing from a threshold.
    [SyncVar] bool netSprinting;

    // HOW FAR OFF THE FLOOR they are, and how fast their legs are churning. Both travel with the POSITION rather
    // than with the pose, because both change continuously while a pose does not — a jump's height moves every
    // frame it is in the air, and a cadence follows a Haste up and down.
    //
    // Neither can be worked out at the far end. A watched copy has no physics capsule, so it cannot tell real lift
    // from the height the capsule sits at when standing; and it has no controller, so it cannot fold this person's
    // gear, Slow and Haste into a cadence. Both were therefore missing entirely: a jumping player's copy never left
    // the ground, and its clips played at the bare baseline.
    [SyncVar] float netAboveGround;
    [SyncVar] float netGait;

    // A HOP, AS A CURVE RATHER THAN AS SAMPLES OF ONE. A jump's lift is scripted — a symmetric arc that is entirely
    // decided by the moment it began, how tall it goes and how long it lasts — so it is said ONCE and every machine
    // draws the identical curve on the clock they share.
    //
    // WHY IT CANNOT RIDE THE POSITION REPORT. That report goes ten times a second and a watched body then EASES
    // toward each value, and an arc this short survives neither. Replayed offline against the authored numbers, a
    // 730 ms hop reached only 95% of its height, peaked 135 ms late, and was still visibly off the ground a quarter
    // of a second after the jumper had landed — drawn, in total, from four reported values on the way up. None of
    // that is fixable by easing faster: the easing is what makes ordinary walking smooth.
    //
    // The SIDEWAYS half deliberately stays on the position report, because it is not scripted at all — it is steered
    // live by whatever keys are held each frame in the air.
    [SyncVar] double netHopAt;      // when the curve began, on the clock both machines share. 0 = no hop running
    [SyncVar] float netHopPeak;     // how high it goes at the top, in world units
    [SyncVar] float netHopSeconds;  // how long the whole curve lasts

    /// <summary>THE BODY THIS MACHINE DRAWS FOR THIS PERSON — the watched copy built for somebody else, and your own
    /// real avatar for yourself, because your own machine keeps no copy of you. Null before either exists.
    ///
    /// <para>Asked by everything that plays a picture on a person: a cast's effect, a cue, a floating number. One
    /// question, one answer, so nothing has to work out for itself which of the two it is looking at.</para></summary>
    public Transform DrawnBody
        => isOwned ? (Character.Instance != null ? Character.Instance.Body : null)
                   : (body != null ? body.transform : null);

    /// <summary>SERVER ONLY: the fighter this connection swings with — the thing that owns their stats, rolls
    /// their crits and takes their hits. Null on a client, which has no business holding anyone's authoritative
    /// stats, and null on the server until their character has been built.</summary>
    public ICombatUnit ServerCombatUnit { get; private set; }

    /// <summary>Server-only: give this connection the fighter the server will resolve its blows with.</summary>
    public void BindCombatUnitOnServer(ICombatUnit unit)
    {
        ServerCombatUnit = unit;
        BindMapToFighter();
    }

    /// <summary>THE ONE PLACE THIS CONNECTION'S MAP REACHES ITS FIGHTER, called from every moment either half can
    /// become known.
    ///
    /// <para>WHY IT IS CALLED FROM MORE THAN ONE PLACE: the two halves arrive in whichever order the wire and the
    /// account service settle in, and EITHER can be last. The fighter is built before the character server's answer
    /// is laid onto it, so at that moment there is no map to hand over; a person's own machine can equally name a
    /// map before the fighter exists. Whichever lands second has to complete the pair, so each of those moments
    /// calls THIS rather than carrying its own copy of the handover.</para>
    ///
    /// <para>WHAT IT COSTS TO CALL TOO OFTEN: nothing. Doing nothing until both halves exist is the first line, and
    /// the fighter ignores a map it is already in — so a repeat re-files nobody and re-runs nothing downstream.</para>
    ///
    /// <para>WHAT GOES WRONG WITHOUT IT: the fighter keeps no map of its own, and a blank one falls back to
    /// whichever map this process loaded last. On a server holding one map that is accidentally right; on a server
    /// that has brought a second map up it is somebody else's map, and that person is then filed under a place they
    /// have never been — hunted by nothing, and refused by anything that builds at their feet.</para></summary>
    void BindMapToFighter()
    {
        if (string.IsNullOrEmpty(mapName)) return;
        if (ServerCombatUnit is Character fighter && fighter != null) fighter.BindMap(mapName);
    }

    /// <summary>SERVER ONLY: which character this connection is playing. Held so that the name can be released
    /// again when they leave — a character being played is a fact about the server, not about the wire, so it is
    /// deliberately not a synced value and no client is ever told it.</summary>
    public string ServerCharacterId { get; set; }

    /// <summary>SERVER ONLY: where this person was sent, taken from the character the character server pushed —
    /// so the server knows which map they are in from the moment it hands them a body, instead of waiting to be
    /// told by their machine.
    ///
    /// <para>Waiting was the bug: for the first second or so of a session the server had no map for them, so a
    /// save in that window was refused to avoid erasing where they were standing — and walking through a door in
    /// that window aborted the move, because a move begins by writing them down. The server put them there; it
    /// does not need to be told.</para></summary>
    public void SetMapOnArrival(string map)
    {
        if (!string.IsNullOrWhiteSpace(map) && string.IsNullOrEmpty(mapName)) mapName = map;
        BindMapToFighter();
    }

    /// <summary>SERVER ONLY: whose account this character belongs to. Like the id above it is never told to any
    /// client; it is here because a one-time ticket is minted against an account.</summary>
    public string ServerAccountId { get; set; }



    /// <summary>SERVER ONLY: is the person on this connection trusted with the game's commands? Set from what the
    /// account service answered when their character was read, and false until then — so a connection whose
    /// account could not be read is refused rather than trusted.
    ///
    /// <para>Deliberately NOT a synced value: no client is told this, about themselves or anybody else. The game
    /// learns its own mark from the account service directly, and this copy is the only one that decides whether
    /// anything actually happens to the world.</para></summary>
    public bool ServerIsGm { get; set; }

    // ---- walking through a door into a zone another process holds -------------------------------------------

    /// <summary>SERVER ONLY: this person is on their way out and nothing they send may be acted on any more.
    ///
    /// <para>It goes up BEFORE they are written down, and never comes back down: from that instant their state
    /// on this process is finished, and anything accepted after it — a swing, a pick-up, a spent point — happens
    /// to a copy that is about to be thrown away, so the player would simply lose it.</para></summary>
    public bool Leaving { get; private set; }

    /// <summary>SERVER ONLY: this person's character now belongs to another connection — they were signed in
    /// again elsewhere, or they have walked into a zone another process holds. Nothing they send is acted on
    /// after this, and the write their disconnect would otherwise make is skipped, because it would land on top
    /// of the copy that is now live.</summary>
    public void MarkLeaving() => Leaving = true;

    /// <summary>A door. The player's own machine no longer travels by itself, because the map it is walking into
    /// may be held by a different process — and only the server can find out which, or refuse.</summary>
    [Command]
    public void CmdWalkThroughPortal(string map, string spawnId) => MoveToMap(map, spawnId);

    /// <summary>Server-only: take this person to <paramref name="map"/>. The one way anybody changes map — a door
    /// they walked through and a GM's <c>/tp</c> both come here, so a command cannot skip the save that has to
    /// land before they are handed to another process.</summary>
    public void MoveToMap(string map, string spawnId)
    {
        if (!isServer || Leaving || string.IsNullOrWhiteSpace(map)) return;

        // ALREADY HERE: nothing crosses the wire and nothing is written down. This is most doors, and it stays
        // exactly as instant as it has always been.
        if (MapHosting.Holding(map)) { TargetTravelHere(connectionToClient, map, spawnId); return; }

        MarkLeaving();
        MapHosting.Want(map);
        TargetMoveStep(connectionToClient, "Saving your character…");

        // WRITTEN DOWN, AND THE WRITE CONFIRMED, BEFORE ANYBODY ELSE IS TOLD ABOUT THEM. The process they are
        // going to is handed the stored character, so a move that started before the write landed would hand
        // over whoever they were fifteen seconds ago and lose everything since.
        SaveNow(ok =>
        {
            if (this == null) return;
            if (!ok) { Leaving = false; TargetMoveFailed(connectionToClient, "could not save your character"); return; }
            AskTheCharacterServerToMoveMe(map, spawnId);
        });
    }

    void AskTheCharacterServerToMoveMe(string map, string spawnId)
    {
        TargetMoveStep(connectionToClient, "Finding the map…");

        var body = JsonUtility.ToJson(new MoveAsk { map = map, accountId = ServerAccountId ?? "", isGm = ServerIsGm });
        ServerHttp.Send("POST", MapServerLink.CharacterServerUrl + "/server/characters/" + ServerCharacterId + "/move",
                        body, MapServerLink.ServerSecret, (ok, reply, error) =>
        {
            if (this == null) return;
            var where = ok ? JsonUtility.FromJson<MoveReply>(reply) : null;
            if (!ok || where == null || string.IsNullOrEmpty(where.ticket))
            {
                // THEY STAY WHERE THEY ARE. Nothing has been handed over, so putting the latch down leaves them
                // exactly as they were before they touched the door.
                Leaving = false;
                GameLog.Warn(LogCat.Server, "could not move " + playerName + " to '" + map + "' — " + error + " " + reply);
                TargetMoveFailed(connectionToClient, "that place would not take you just now");
                return;
            }

            GameLog.Info(LogCat.Server, playerName + " is moving to '" + map + "' at " + where.host + ":" + where.port);
            TargetMoveTo(connectionToClient, where.host, where.port, where.ticket, map, spawnId);
        });
    }

    [TargetRpc]
    void TargetTravelHere(NetworkConnectionToClient target, string map, string spawnId)
        => SceneTravel.ArriveLocally(map, spawnId);

    [TargetRpc]
    void TargetMoveStep(NetworkConnectionToClient target, string what) => LoadingScreen.SetStep(what);

    [TargetRpc]
    void TargetMoveTo(NetworkConnectionToClient target, string host, int port, string ticket, string map, string spawnId)
        => GameNetwork.MoveToAnotherServer(host, port, ticket, map, spawnId);

    [TargetRpc]
    void TargetMoveFailed(NetworkConnectionToClient target, string why)
    {
        LoadingScreen.Hide();
        BattleNotice.Warn(why);
    }

    [System.Serializable]
    class MoveAsk { public string map; public string accountId; public bool isGm; }

    [System.Serializable]
    class MoveReply { public string host; public int port; public string ticket; public string map; }

    // How often the owner tells the server where it is. Ten times a second is far below the frame rate and
    // still smoother than a person can walk; sending every frame would be bandwidth spent on nothing.
    const float ReportInterval = 0.1f;
    float nextReport;

    // Below these, a change is not worth a packet — a player standing perfectly still should cost nothing.
    const float MoveEpsilon = 0.01f;
    const float TurnEpsilon = 1f;      // degrees
    Vector3 lastReported;
    float lastReportedYaw;
    float lastReportedAbove;
    float lastReportedGait;

    // Below this a cadence has not really moved — it is a multiplier around 1, so a hundredth of it is already
    // finer than anything a leg could show.
    const float GaitEpsilon = 0.01f;

    // The visual that owns "how far off the floor am I". Held rather than looked up, because the report runs ten
    // times a second for the whole of a session; re-resolved after a door builds a new body.
    Character3DVisual playerVisual;

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
            InventoryStore.Instance?.Notify();
        }

        // …and the character sheet, held for the same reason.
        if (sheetWaiting && Character.Instance != null)
        {
            sheetWaiting = false;
            Character.Instance.SetSheetFromServer(waitingCurrent, waitingBaseline, waitingWithStates,
                                                  waitingRaiseCost, waitingJumpAt, waitingJumpGain);
        }

        // THREE possible askers, not two — and this is where the server used to fall through the crack. "Am I the
        // one steering this?" is false on a server for EVERY player, because the server steers nobody, so it took
        // the watcher's branch and built a visible body for each person who connected: a rigged model fetched,
        // assembled, and blended every frame on a machine with no screen to draw it on.
        //
        // The creature shells never had this problem because they ask a different question. So this asks it too:
        // a body is built only by a machine that is actually WATCHING.
        if (isServer) { PushMyHealth(); PushMySheet(); ForgetSpeedIfTheyStopped(); return; }
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
    /// anything, so another person's swing does not exist on your screen at all without it.
    ///
    /// <para><paramref name="beatSeconds"/> of ZERO means "however long the animation itself takes". A swing has a
    /// beat because the server owns the swing cadence; a skill's release swing does not, because how long it runs
    /// is a property of the art and a headless server has no business reading art. Each watching machine reads the
    /// length off its own copy of the animation, which is the one that has to look right.</para></summary>
    public void BeganAction(StanceId action, float beatSeconds)
    {
        if (!isServer || beatSeconds < 0f) return;
        netAction = action;
        netActionSeconds = beatSeconds;
        // Stamped by the server, never by the machine that asked: a moment a client writes is a moment a client
        // can move, and every watcher measures the action against it.
        netActionAt = NetworkTime.time;
    }

    // ---- the owner's half: say what their body is doing ---------------------------------------------------
    //
    // WHY THE OWNER AND NOT THE SERVER. Almost everything a person's body does is decided on their own machine and
    // nowhere else: they pressed jump, they rolled, they flinched, they sat down, their spell finished winding up
    // and threw. The server never sees any of it — it holds their stats, not their legs — so a pose it was not told
    // about is a pose no other player ever sees. The two exceptions stay the server's and are deliberately NOT sent
    // from here: a weapon swing (the server chooses it from the weapon IT holds) and plain walking (every machine
    // works that out from the speed it is already told). See StanceCatalog.WatchersMustBeTold.
    //
    // NOTHING HERE DECIDES ANYTHING. A pose is a picture; it moves no health, spends nothing and gates nothing, so
    // taking the owner's word for it costs the world nothing. The checks below are only to keep a broken or lying
    // machine from asking for a pose that does not exist or an animation stretched over an hour.

    // The longest a pose may be asked to last. Far above any real animation, low enough that a nonsense number
    // cannot leave somebody frozen mid-roll on everybody else's screen for the rest of the session.
    const float LongestPose = 60f;

    static bool RealStance(StanceId id) => StanceCatalog.Exists(id);

    static float SanePose(float seconds) => Mathf.Clamp(seconds, 0f, LongestPose);

    [Command]
    public void CmdActionBegan(StanceId id, float seconds)
    {
        if (Leaving || !RealStance(id)) return;
        BeganAction(id, SanePose(seconds));
    }

    [Command]
    public void CmdDrivenStance(bool has, StanceId id, float window, bool release)
    {
        if (Leaving) return;
        if (has && !RealStance(id)) return;
        netHasDriven = has;
        netDriven = id;
        netDrivenWindow = SanePose(window);
        netDrivenRelease = release;
    }

    [Command]
    public void CmdHeldStance(bool has, StanceId id, float window)
    {
        if (Leaving) return;
        if (has && !RealStance(id)) return;
        netHasHeld = has;
        netHeld = id;
        netHeldWindow = SanePose(window);
    }

    [Command]
    public void CmdHopBegan(float peak, float seconds)
    {
        if (Leaving || peak <= 0f || seconds <= 0f) return;
        netHopPeak = Mathf.Clamp(peak, 0f, HighestAnybodyGets);
        netHopSeconds = Mathf.Clamp(seconds, 0.05f, LongestHop);
        // STAMPED HERE, never by the machine that asked — the same rule a swing's moment already answers to. A
        // moment a client writes is a moment a client can move, and every watcher measures the curve against it.
        netHopAt = NetworkTime.time;
    }

    [Command]
    public void CmdHopEnded()
    {
        if (Leaving) return;
        netHopAt = 0d;
    }

    // The longest a scripted hop may claim to last. Far beyond any real one, and here only so a broken machine
    // cannot leave somebody arcing through the sky for a minute.
    const float LongestHop = 5f;

    [Command]
    public void CmdSprinting(bool on)
    {
        if (Leaving || netSprinting == on) return;
        netSprinting = on;
        // AND THE PUFF THAT GOES WITH IT, said from here so it reaches every screen — the person's own included.
        // Their machine no longer plays a cue it decided itself; it plays what the world tells it, exactly as it
        // already does for their own spells.
        if (on) VfxCue.Fire(VfxCueId.OnSprint, transform);
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

    // ---- the server's half: hand the person their character sheet ----------------------------------------
    //
    // A player's machine works out none of the numbers a character's stats produce, so the server derives the whole
    // sheet from the character it holds and sends it — but only when something on it moved. Checked twice a second
    // rather than on every beat: a sheet is read by a window, not by a swing, and it is three snapshots to build.

    const float SheetInterval = 0.5f;
    float nextSheetPush;
    bool sheetSent;
    DerivedSnapshot sentCurrent, sentBaseline, sentWithStates;
    readonly int[] sentRaiseCost = new int[6];
    readonly int[] sentJumpAt = new int[6];
    readonly int[] sentJumpGain = new int[6];

    void PushMySheet()
    {
        if (Time.time < nextSheetPush || connectionToClient == null) return;
        nextSheetPush = Time.time + SheetInterval;

        var person = ServerCombatUnit as Character;
        if (person == null) return;

        var current = person.CurrentDerived;
        var baseline = person.BaseDerived;
        var withStates = person.DerivedWithStates;
        bool changed = !sheetSent
                       || !DerivedSnapshot.Same(current, sentCurrent)
                       || !DerivedSnapshot.Same(baseline, sentBaseline)
                       || !DerivedSnapshot.Same(withStates, sentWithStates);
        for (int i = 0; i < 6; i++)
        {
            var stat = (PrimaryStat)i;
            int cost = person.CostToRaise(stat), jumpAt = person.NextJumpAt(stat), gain = person.JumpGainAt(stat);
            if (cost != sentRaiseCost[i] || jumpAt != sentJumpAt[i] || gain != sentJumpGain[i]) changed = true;
            sentRaiseCost[i] = cost;
            sentJumpAt[i] = jumpAt;
            sentJumpGain[i] = gain;
        }
        if (!changed) return;

        sheetSent = true;
        sentCurrent = current;
        sentBaseline = baseline;
        sentWithStates = withStates;
        // Copied only on a change, which is when a new sheet is worth a send at all.
        TargetSheet(connectionToClient, current, baseline, withStates,
                    (int[])sentRaiseCost.Clone(), (int[])sentJumpAt.Clone(), (int[])sentJumpGain.Clone());
    }

    [TargetRpc]
    void TargetSheet(NetworkConnectionToClient target, DerivedSnapshot current, DerivedSnapshot baseline,
                     DerivedSnapshot withStates, int[] raiseCost, int[] jumpAt, int[] jumpGain)
    {
        var me = Character.Instance;
        if (me != null) { me.SetSheetFromServer(current, baseline, withStates, raiseCost, jumpAt, jumpGain); return; }
        // ARRIVED BEFORE THERE WAS A CHARACTER TO GIVE IT TO. The server only sends a sheet when it changes, so a
        // dropped one would leave zeros on the window until something happened to move a stat. Held until there is.
        sheetWaiting = true;
        waitingCurrent = current;
        waitingBaseline = baseline;
        waitingWithStates = withStates;
        waitingRaiseCost = raiseCost;
        waitingJumpAt = jumpAt;
        waitingJumpGain = jumpGain;
    }

    bool sheetWaiting;
    DerivedSnapshot waitingCurrent, waitingBaseline, waitingWithStates;
    int[] waitingRaiseCost, waitingJumpAt, waitingJumpGain;

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
        float yaw = FacingYaw(me.transform);
        // BOTH READ FROM THEIR SINGLE OWNER, never re-derived here: the visual is what separates real lift from the
        // capsule's resting offset, and the stance choke point is what folds a cadence out of gear, Slow and Haste.
        // perf-ok: resolved once and held. A destroyed object reads as null, so walking through a door re-resolves
        // it against the new body — the same shape the turned part below uses, for the same reason.
        if (playerVisual == null && StanceController.Player != null)
            playerVisual = StanceController.Player.GetComponent<Character3DVisual>();
        float above = playerVisual != null ? playerVisual.HeightAboveGround : 0f;
        float gait = StanceController.Player != null ? StanceController.Player.TravelGait : 1f;

        // TURNING COUNTS AS A CHANGE, and it did not before. Only a step used to be worth a packet, so a person
        // who stood their ground and turned to face you never sent anything — they went on facing whichever way
        // they had last walked, on everybody else's screen, for as long as they stood there. Standing and turning
        // is exactly what a person does when somebody walks up to them, so this was the common case.
        // RISING OR FALLING COUNTS AS A CHANGE TOO. A jump straight up moves the feet barely at all on the flat, so
        // the two tests above would have held the whole hop back and everybody else would have seen nothing.
        if (hasReportedOnce
            && (p - lastReported).sqrMagnitude < MoveEpsilon * MoveEpsilon
            && Mathf.Abs(Mathf.DeltaAngle(lastReportedYaw, yaw)) < TurnEpsilon
            && Mathf.Abs(above - lastReportedAbove) < MoveEpsilon
            && Mathf.Abs(gait - lastReportedGait) < GaitEpsilon) return;

        lastReported = p;
        lastReportedYaw = yaw;
        lastReportedAbove = above;
        lastReportedGait = gait;
        hasReportedOnce = true;
        CmdReportPosition(p, yaw, above, gait);
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

    // THE DOCUMENT WAITING ITS TURN, and everybody who asked for a write while one was already out. Only ONE
    // document ever waits: each is a whole snapshot of the character, so a newer one already says everything an
    // older one said. The waiters, though, all stay — the newer document carries their state too, so telling
    // every one of them that it landed is the truth rather than a convenience.
    string queuedBody;
    Action<bool> queuedWaiters;

    // Who is waiting on the write that is out right now.
    Action<bool> inFlightWaiters;

    /// <summary>Server-only: write this character down NOW. <paramref name="whenDone"/> is told whether the state
    /// captured by THIS call reached the store — the disconnect path waits on it, because a character released
    /// before the write has finished is how the same items end up in two places.</summary>
    public void SaveNow(Action<bool> whenDone)
    {
        if (!isServer) { whenDone?.Invoke(false); return; }

        var c = ServerCharacter;
        if (c == null || string.IsNullOrEmpty(ServerCharacterId))
        {
            whenDone?.Invoke(false);
            return;
        }

        // REFUSED OUTRIGHT WHILE NOBODY KNOWS WHICH MAP THEY ARE IN. The character server does not merge — it
        // replaces the whole stored document with the one it is sent. Between somebody joining and their own
        // machine first saying where it is, this map name is still empty, so a write in that window stored an
        // empty map and the origin ON TOP of a real map and a real position. The next login then put them at the
        // front door of the default map, however far they had actually travelled, and nothing anywhere recorded
        // where they had been. Losing one write costs at most fifteen seconds of play and the next beat gets it;
        // losing where somebody was standing cannot be undone, so the write does not go at all.
        if (string.IsNullOrEmpty(mapName))
        {
            GameLog.Warn(LogCat.Server, "not writing " + playerName + " down — they have not said which map they "
                             + "are in yet, and a save without one would erase where they were standing.");
            needsSaving = true;
            nextSaveAt = Time.time + SecondsBetweenSaves;
            whenDone?.Invoke(false);
            return;
        }

        // WHAT GETS WRITTEN IS TAKEN NOW, before anything is sent and before this call can be made to wait. The
        // disconnect path depends on exactly that: it asks for a write and then tears the body down on the very
        // next line, so anything read later than this line would be read off a character that no longer exists.
        var save = c.CaptureSave();

        // AND WHERE THEY WERE STANDING. The character itself does not know — it has no map of its own and its
        // body is this object — so the two things that decide where somebody comes back to are filled in here.
        // Left out, every save wrote an empty map and the origin, so everybody logged in at the front door of
        // the default map however far they had travelled, and the character server could not route them either.
        save.sceneName = mapName;
        save.posX = netPosition.x;
        save.posY = netPosition.y;
        save.posZ = netPosition.z;

        needsSaving = false;
        var body = JsonUtility.ToJson(new SaveEnvelope { progress = save });

        // ONE AT A TIME, STILL. Two writes in flight are two whole documents racing, and whichever lands last
        // wins — so a slow first one arriving after a newer one silently puts the older state back.
        //
        // But a caller asked to WAIT is not the same as a caller refused. This used to answer "no" straight away
        // and leave the rest to the next beat, which was wrong twice over: the caller could not tell "the write
        // failed" from "somebody else was writing", and the person leaving had no next beat — their shell is
        // dismissed a moment later, so their last few seconds were simply gone. Now the document above is put in
        // the queue and sent the instant the current write returns, and this caller is told about THAT write.
        if (savingNow)
        {
            queuedBody = body;
            queuedWaiters += whenDone;
            return;
        }

        SendTheWrite(body, whenDone);
    }

    /// <summary>Server-only: put one already-captured document on the wire to the character server, which is the
    /// only thing that writes to the store. This machine has no database and never will — it asks, as a SERVER,
    /// with the secret the two of them share, and not as whoever happens to be playing.</summary>
    void SendTheWrite(string body, Action<bool> waiters)
    {
        savingNow = true;
        inFlightWaiters = waiters;

        ServerHttp.Send("PUT", MapServerLink.CharacterServerUrl + "/server/characters/" + ServerCharacterId
                        + "/progress", body, MapServerLink.ServerSecret,
            (ok, reply, error) =>
            {
                savingNow = false;
                var told = inFlightWaiters;
                inFlightWaiters = null;

                if (!ok)
                {
                    // SAID OUT LOUD, and the character is marked as still needing writing so the next beat tries
                    // again. A failed save that nobody hears about is a character quietly going stale.
                    GameLog.Warn(LogCat.Server, "could not write " + playerName + " down — " + error);
                    needsSaving = true;
                    nextSaveAt = Time.time + SecondsBetweenSaves;
                }

                // THE ONE THAT WAS WAITING GOES NOW, and it goes BEFORE anybody is told. A waiter is free to ask
                // for another save the moment it hears back, and starting the queued write first means such a
                // request queues behind it rather than racing it.
                if (queuedBody != null)
                {
                    var next = queuedBody;
                    var nextWaiters = queuedWaiters;
                    queuedBody = null;
                    queuedWaiters = null;
                    SendTheWrite(next, nextWaiters);
                }

                told?.Invoke(ok);
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
    // EVERY REQUEST BELOW REFUSES A PERSON ON THEIR WAY OUT. Their state on this process is finished the moment
    // a move starts and is about to be thrown away, so anything accepted after that is simply lost to the player.
    void CmdSpendStatPoint(int stat)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null) return;
        if (!System.Enum.IsDefined(typeof(PrimaryStat), stat)) return;
        if (!c.AddStat((PrimaryStat)stat)) return;   // no points left, or already at the ceiling
        SaveSoon();
        PushStatsToOwner();
    }

    /// <summary>Server-only: hand this connection its level, unspent points and primaries as they now stand.
    /// Called after every change the server makes to them — a point spent, or a GM command.</summary>
    public void PushStatsToOwner()
    {
        if (!isServer || connectionToClient == null) return;
        var c = ServerCharacter;
        if (c == null) return;
        TargetStatsChanged(connectionToClient, (int)c.Level, c.StatPoints, c.CaptureStatsForOwner());
        // The sheet those stats produce follows on the very next beat instead of waiting out its interval.
        nextSheetPush = 0f;
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
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null || bindings == null || bindings.Length > 64) return;
        c.Hotbar.Restore(bindings);
        // AND THE SKILLS BEHIND THE NEW BAR. What a character can use is built from four places — the bar, their
        // ticked passives, their weapon and their bag — and a skill only gets something to run it when that list is
        // rebuilt. Storing the bar without this left the server agreeing the skill was theirs and holding nothing to
        // cast it with, so the press was refused: their own machine spent the mana and the cooldown and the skill did
        // nothing. Their machine has always done this on the very same change (HotbarCommit.Now); this side never was.
        c.RefreshUsableSkills();
        SaveSoon();
    }

    [Command]
    void CmdSetPassives(string[] chosen)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null || chosen == null || chosen.Length > 64) return;
        c.PassiveSkills.Restore(chosen);
        c.RefreshUsableSkills();   // same rule as the bar above: what they can use changed, so the list is rebuilt
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
    /// the server makes to them, so there is exactly one way the client ever learns what it is holding.
    ///
    /// <para>Pass <paramref name="alsoTheBar"/> when the change was a WEAPON going on or coming off, which is the
    /// one thing that makes this side rewrite somebody's hotbar and clear the passives they had ticked. Their
    /// machine then draws the bar this one decided instead of working the same rule out again. Every other change
    /// leaves the bar out — see the note in <see cref="BagWire"/>.</para></summary>
    public void PushBagToOwner(bool alsoTheBar = false)
    {
        if (!isServer) return;
        if (ServerCombatUnit is not Character fighter || fighter == null) return;
        // WHAT THEY CAN USE CHANGED TOO. A consumable's effect is a skill, and the bag is one of the four places
        // that list is built from — so a potion picked up mid-session had nothing behind it here and drinking it
        // did nothing at all. This is the one place every change the server makes to a bag passes through, which
        // is why the rebuild sits here rather than at each of them.
        fighter.RefreshUsableSkills();
        TargetBagChanged(connectionToClient, BagWire.From(fighter, alsoTheBar));
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
        InventoryStore.Instance?.Notify();
    }

    /// <summary>Server-only: tell this connection that something has just arrived in their bag, so their chat can
    /// say so. Called from the ONE place a thing enters any bag, never from whatever caused it — a kill, a
    /// command, a thing picked up and anything written later all reach the player through this one line.
    ///
    /// <para>The ITEM'S NAME is not sent: what crosses is its id and the three numbers that decide how the line
    /// reads, and the other machine looks the rest up in the catalog it already has — the same way it builds the
    /// words for a thing lying on the ground.</para></summary>
    public void TellOwnerTheyGot(string itemId, int count, int rarity, int upgrade)
    {
        if (!isServer || connectionToClient == null) return;
        TargetGotItem(connectionToClient, itemId, count, rarity, upgrade);
    }

    [TargetRpc]
    void TargetGotItem(NetworkConnectionToClient target, string itemId, int count, int rarity, int upgrade)
    {
        var def = ItemCatalog.Resolve(itemId);
        if (def == null) return;
        ChatController.ItemArrived(def, count, (Rarity)rarity, upgrade);
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
            GameLog.Warn(LogCat.Server, "" + playerName + " asked about position " + index + " expecting '"
                             + expectedItemId + "' but it now holds '" + e.def.id + "' — ignored.");
            return false;
        }
        return true;
    }

    [Command]
    void CmdEquipFromBag(int index, string expectedItemId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (!StillTheSameStack(c, index, expectedItemId)) return;
        var e = c.Bag[index];
        if (e == null || !e.IsEquip || e.instance == null) return;

        // A WEAPON CHANGE IS ALSO A BAR CHANGE, so the bar this side is about to rewrite travels back with the
        // gear. Any other slot leaves the bar alone and must not send one.
        bool theirBarChanges = e.def.slot == EquipSlotType.Weapon;

        var prev = c.GetEquippedInstance(e.def.slot);
        c.SetEquipped(e.def.slot, e.instance);
        c.Bag.RemoveAt(index);
        // BY HAND ON PURPOSE. The piece being displaced is already theirs and is only moving from their body back
        // into their bag — putting it through the arrival door would tell them they had "obtained" a sword they
        // have been carrying all day, on every single equip.
        if (prev != null && prev.def != null)
            c.Bag.Add(new BagEntry { def = prev.def, count = 1, instance = prev });
        PushBagToOwner(theirBarChanges);
        SaveSoon();
    }

    [Command]
    void CmdUnequipToBag(int slot)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null) return;
        var worn = c.GetEquippedInstance((EquipSlotType)slot);
        if (worn == null) return;

        // Taking the weapon off empties the bar, for the same reason putting one on refills it.
        bool theirBarChanges = (EquipSlotType)slot == EquipSlotType.Weapon;

        c.SetEquipped((EquipSlotType)slot, (EquipmentInstance)null);
        // By hand, for the same reason as the swap above: taking your own gear off is not obtaining it.
        if (worn.def != null) c.Bag.Add(new BagEntry { def = worn.def, count = 1, instance = worn });
        PushBagToOwner(theirBarChanges);
        SaveSoon();
    }

    [Command]
    void CmdReorderBag(int from, int to, string expectedItemId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (!StillTheSameStack(c, from, expectedItemId) || from == to) return;

        var moving = c.Bag[from];
        c.Bag.RemoveAt(from);
        c.Bag.Insert(Mathf.Clamp(to, 0, c.Bag.Count), moving);
        PushBagToOwner();
        SaveSoon();
    }

    // WHAT A TENTH OF A SECOND OF RUNNING IS WORTH, added to the reach when the server decides whether somebody
    // was really standing over the thing they just reached for. A player's position arrives ten times a second,
    // so the place the server has them is always slightly behind the place they are — and without this the game
    // would refuse a pick-up it had just offered, to anybody who reached while still moving.
    const float ReachSlack = 0.6f;

    [Command]
    void CmdDropFromBag(int index, string expectedItemId, int howMany)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (!StillTheSameStack(c, index, expectedItemId)) return;
        var e = c.Bag[index];

        // THE TWO THINGS THAT ARE NEVER PUT DOWN, refused here and not only greyed out on the window that asked.
        // A screen can be got round; this is the answer that counts.
        if (e.def.category == ItemCategory.Quest) return;
        if (e.def.slot == EquipSlotType.Ring) return;

        int take = e.IsEquip ? 1 : Mathf.Clamp(howMany, 1, e.count);
        if (take <= 0) return;

        // AT YOUR FEET, or the nearest clear spot to them — two things in one place read as one thing, and the
        // one underneath cannot be seen or pointed at.
        var where = GroundDrop.FreeSpotNear(MapManager.InWorld(netPosition, mapName), mapName);
        var drop = GroundDrop.Place(where, e.def, take, e.instance, e.def.rarity, 0, mapName, 0);
        if (drop == null) return;

        // OUT OF THE BAG ONLY ONCE IT IS ON THE FLOOR. Taking it out first and failing to place it would be an
        // item that existed in neither place.
        if (e.IsEquip || take >= e.count) c.Bag.RemoveAt(index);
        else e.count -= take;

        GameNetwork.PublishDrop(drop);
        PushBagToOwner();
        SaveSoon();
    }

    [Command]
    void CmdPickUpDrop(uint dropNetId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null) return;
        if (!NetworkServer.spawned.TryGetValue(dropNetId, out var identity) || identity == null) return;

        var shell = identity.GetComponent<NetGroundDrop>();
        var drop = shell != null ? shell.ServerDrop : null;
        if (drop == null || drop.def == null) return;

        // IN THE SAME PLACE, AND WITHIN ARM'S LENGTH OF IT. Asked here rather than trusted from the machine that
        // asked, which is the whole reason picking up is a request at all.
        if (drop.mapName != mapName) return;
        var mine = MapManager.InWorld(netPosition, mapName);
        float dx = drop.transform.position.x - mine.x;
        float dz = drop.transform.position.z - mine.z;
        float reach = GroundDrop.Reach + ReachSlack;
        if (dx * dx + dz * dz > reach * reach) return;

        // FIRST COME. The shell is taken away the moment somebody wins it, so a second person asking for the same
        // thing a frame later finds nothing to be given — there is no way for one drop to be handed out twice.
        if (drop.instance != null) c.TakeIntoBag(drop.instance);
        else c.TakeIntoBag(drop.def, drop.count);

        NetworkServer.Destroy(shell.gameObject);
        PushBagToOwner();
        SaveSoon();
    }

    // ---- what a client asks for, from the bag window -----------------------------------------------------
    // Public so InventoryStore can send them without knowing anything about the wire.

    public void AskToEquip(int index, string itemId) => CmdEquipFromBag(index, itemId);
    public void AskToUnequip(EquipSlotType slot) => CmdUnequipToBag((int)slot);
    public void AskToReorder(int from, int to, string itemId) => CmdReorderBag(from, to, itemId);
    public void AskToDrop(int index, string itemId, int howMany) => CmdDropFromBag(index, itemId, howMany);
    public void AskToPickUp(uint dropNetId) => CmdPickUpDrop(dropNetId);

    [Command]
    void CmdReportMap(string map)
    {
        if (Leaving) return;
        // AN EMPTY NAME IS NOT A MAP, and taking one would rub out the only record this server has of where this
        // person is standing. Everything downstream reads it: the save refuses to write at all without it, the
        // body is filed into the scene it names, and who is told about whom is decided by comparing it. An honest
        // game never sends one — it says which map it is in only when that name CHANGES, and it starts out with
        // nothing to say — so an empty one is a broken game or a lying one, and neither may erase a known map.
        if (string.IsNullOrWhiteSpace(map))
        {
            GameLog.Warn(LogCat.Server, "" + playerName + " said they are in a map with no name — ignored, they "
                             + "stay filed under '" + mapName + "'.");
            return;
        }

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
            GameLog.Warn(LogCat.Server, "" + playerName + " says they are in '" + map
                             + "', which this server is not holding — they will be filed under '"
                             + gameObject.scene.name + "' and seen by the wrong people. Add it to the map list.");
        MapManager.PutInMap(gameObject, into);

        // AND THE FIGHTER IS TOLD TOO. It is the thing creatures actually look at, and it has no body in any map of
        // its own to be found in — so if this is not pushed across, every creature goes on hunting the whole world.
        BindMapToFighter();

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
            GameLog.Warn(LogCat.Server, "" + playerName + " changed map again after only "
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

    /// <summary>Ask the server to start, or stop, a conversation with a townsperson. Sent from THIS person's own
    /// machine, on the press of F and again when the dialog box closes.
    ///
    /// <para>It goes through this object rather than straight to the townsperson because this is the one thing on
    /// the wire this connection owns — which is what makes "who is talking" the server's answer rather than
    /// something the message claims about itself.</para></summary>
    public void AskToTalkTo(uint npcNetId, bool talking)
    {
        if (!isOwned) return;
        CmdTalkToNpc(npcNetId, talking);
    }

    // Asked BY a player, carried out HERE. Who is doing the talking is the connection it arrived on — this shell,
    // which on the server stands exactly where that person stands — never anything the message said about itself.
    [Command]
    void CmdTalkToNpc(uint npcNetId, bool talking)
    {
        if (Leaving) return;
        GameNetwork.SetNpcTalkedTo(npcNetId, talking ? transform : null);
    }

    [Command]
    void CmdReportPosition(Vector3 p, float yaw, float aboveGround, float gait)
    {
        if (Leaving) return;
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
        // TWO PICTURES, KEPT SANE RATHER THAN TRUSTED. Neither moves anything the server decides — one lifts a model
        // and the other paces a clip — so the only thing worth guarding is that a broken or lying machine cannot
        // leave somebody hanging in the sky or twitching at fifty times speed on everybody else's screen.
        netAboveGround = Mathf.Clamp(aboveGround, 0f, HighestAnybodyGets);
        netGait = Mathf.Clamp(gait, 0.05f, FastestLegsGet);

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

    // WHICH WAY THEY ARE ACTUALLY FACING — read off the part of them that TURNS.
    //
    // On a live-3D character that is the MODEL, and the root it hangs under is never rotated at all: turning is
    // done by the pose sink on its own transform, and nothing anywhere writes a rotation to the root. So reading
    // the root gave the angle it was built at, unchanged, for the whole of a session — and since that is the only
    // thing said about which way anybody is pointing, every other player on every other screen ran around
    // permanently facing one fixed direction while their legs moved.
    //
    // The far end turns the watched body's ROOT to this angle, and that body's own visual then turns its model to
    // match the root — so the model this reads and the model they see end up at the same angle.
    Transform turnedPart;   // resolved once, and again after a door builds a new body

    float FacingYaw(Transform root)
    {
        if (turnedPart == null)
        {
            // perf-ok: a static property read, at most ten times a second, and only until the body exists
            var stance = StanceController.Player;
            turnedPart = stance != null && stance.Pose is Component c ? c.transform : null;
        }
        return turnedPart != null ? turnedPart.eulerAngles.y : root.eulerAngles.y;
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

    // The ceilings on the two pictures a person's machine paints of its own body. Both are far above anything the
    // game produces — a jump clears a fraction of a unit and the fastest legs run at a few times normal — and are
    // here only so that a nonsense number cannot leave somebody floating in the sky or blurring on every screen.
    const float HighestAnybodyGets = 20f;
    const float FastestLegsGet = 8f;

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
            GameLog.Warn(LogCat.Server, "" + playerName + " reported " + refusedInARow
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
            // …AND THE POSE THEY ARE HOLDING WHILE IT LASTS. A swing is a beat that plays once, which is what the
            // line above shows; a jump, a roll, a flinch or a death lasts until it is over, so it is handed over as
            // something to keep doing rather than something that happened. Told every frame and applied only when it
            // changed — the body's own driver holds it from there.
            bodyVisual.TellPose(netHasDriven, netDriven, netDrivenWindow, netDrivenRelease, netSprinting);
            // …AND THE TWO THINGS A COPY CANNOT WORK OUT FOR ITSELF: how far off the floor they are, which is what
            // makes a jump actually leave the ground here, and how fast their legs are churning, which is what every
            // movement-paced clip is played at.
            bodyVisual.TellHeightAboveGround(HeightRightNow());
            if (bodyStance != null) bodyStance.TellGait(netGait);
        }

        ShowTheirHeldStance();
        ShowTheirWindup();
    }

    /// <summary>How far off the floor this person is THIS FRAME.
    ///
    /// <para>While a scripted hop is running, this is the curve itself, worked out on the clock both machines share —
    /// so it is drawn every frame rather than eased toward a value that arrives ten times a second, and it is in step
    /// with the jumper rather than trailing them. The moment the curve is over, or was never running, the answer goes
    /// back to the height the position report carries — which is what covers a real FALL off a ledge, where there is
    /// no script and gravity is doing the work.</para></summary>
    float HeightRightNow()
    {
        if (netHopAt <= 0d || netHopSeconds <= 0f) return netAboveGround;

        float u = (float)((NetworkTime.time - netHopAt) / netHopSeconds);
        if (u <= 0f || u >= 1f) return netAboveGround;

        // The identical arc the jumper's own machine draws: nothing at both ends, the peak halfway through.
        return netHopPeak * 4f * u * (1f - u);
    }

    // The held pose last put on this body, so the push and the pop happen once each rather than every frame.
    bool showingHeld;
    StanceId shownHeld;

    /// <summary>The standing pose this person is holding — guarding, casting, sitting. Pushed onto the watched
    /// body's own stance choke point, which is the same layer their own machine holds it on, so the two resolve it
    /// against an action the identical way.</summary>
    void ShowTheirHeldStance()
    {
        if (bodyStance == null) return;

        // A DIFFERENT ONE MEANS THE OLD ONE COMES OFF. Held poses are pushed and popped by name, so swapping a
        // guard for a seat without popping the guard would leave a body holding both and the higher-ranking one
        // winning forever.
        if (showingHeld && (!netHasHeld || shownHeld != netHeld)) { bodyStance.Pop(shownHeld); showingHeld = false; }
        if (!netHasHeld) return;

        // Pushed every frame rather than once: it is idempotent, it refreshes the window a fitted pose is stretched
        // to, and it is what puts the pose on a body that finished loading after the pose had already started.
        bodyStance.Push(netHeld, netHeldWindow);
        showingHeld = true;
        shownHeld = netHeld;
    }

    // ---- somebody else's wind-up: their cast bar and the pose they hold while it runs --------------------
    //
    // A wind-up is the one thing a watcher has to draw for ITSELF over time rather than being told each frame:
    // the server says it began, how long it runs and when it ended, and everything in between is this machine's
    // own clock. Both halves are driven exactly as the caster's own machine drives them — the same bar and the
    // same held stance, refreshed every frame — so the two cannot come to look different.

    SkillDef windingUp;        // the skill they are casting, or null when they are not
    double windupStartedAt;    // on the clock both machines share, so a message that took a moment still lands right
    float windupSeconds;
    Vector3 windupAimPoint;    // where the spell is aimed, in this person's map — the ring on the ground sits there
    Vfx.EffectHandle windupRing;   // that ring, held so it comes off again however the wind-up ends
    StanceController bodyStance;   // the watched body's stance choke point, held from the moment it is built

    /// <summary>Start showing this person's wind-up. Called when the server says one began.
    ///
    /// <para>The POSE they hold while it runs is not started here — it travels with every other pose they take, on
    /// the one road that carries them. This is the two things a wind-up has that no other pose does: a bar counting
    /// down over their head, and a ring on the ground where the spell is going to land.</para></summary>
    public void ShowCastWindup(SkillDef def, double startedAt, float seconds, Vector3 aimPointInMap)
    {
        if (def == null || seconds <= 0f) return;
        StopCastWindup();          // a second wind-up replaces the first; never two rings on one body
        windingUp = def;
        windupStartedAt = startedAt;
        windupSeconds = seconds;
        windupAimPoint = aimPointInMap;
        ShowTheirCastRing();
    }

    /// <summary>Stop showing it — the cast finished, or was broken. Called when the server says so, and whenever
    /// this machine stops being able to draw them at all.</summary>
    public void StopCastWindup()
    {
        windingUp = null;
        if (body != null) CastBar.Hide(body.transform);
        // AND THE RING ON THE GROUND GOES WITH IT. Stopped wherever the wind-up ends — the server saying so, the
        // clock running out, or the body going away — so a broken cast cannot leave a circle glowing in an empty
        // field for the rest of the session.
        if (windupRing != null) { windupRing.Stop(); windupRing = null; }
    }

    // The ring that opens on the ground under a wind-up — the same one the caster sees, read off the same authored
    // skill, sized by the same authored radius. Attached to the BODY rather than dropped at a fixed spot, exactly as
    // the caster's own is, so a caster shoved mid-cast takes their ring with them.
    void ShowTheirCastRing()
    {
        if (body == null || windingUp == null || string.IsNullOrEmpty(windingUp.castCircleId)) return;
        Vector3 at = MapManager.InWorld(windupAimPoint, mapName);
        windupRing = Vfx.AttachGroundCircle(windingUp.castCircleId, body.transform, at - body.transform.position,
                                            windingUp.LargestAreaRadius, windingUp.castCircleTint);
    }

    void ShowTheirWindup()
    {
        if (windingUp == null) return;

        float elapsed = (float)(NetworkTime.time - windupStartedAt);
        // ITS OWN CLOCK RUNNING OUT ENDS IT TOO, not only the server saying so. A wind-up that completes normally
        // is announced as ended, but that message crosses a network and the bar would otherwise sit full for as
        // long as it took to arrive.
        if (elapsed >= windupSeconds) { StopCastWindup(); return; }

        CastBar.Set(body.transform, elapsed / windupSeconds, windingUp.displayName, windingUp.tint);

        // THE RING, IF THE BODY ONLY JUST ARRIVED. A person can walk into view part-way through a long spell, and
        // the message that started this had no body to hang anything on at the time.
        if (windupRing == null) ShowTheirCastRing();
    }

    /// <summary>Stop drawing this person. Two reasons to, so one description of it: they left, or this machine
    /// walked into another map.</summary>
    void DropTheBody()
    {
        // THE BAR GOES WITH THE BODY IT WAS FLOATING OVER. It is asked for by naming that body, so a body destroyed
        // mid-cast would leave one hanging in the air with nothing under it.
        StopCastWindup();
        // AND EVERY LASTING CUE RIDING THIS BODY. A glow is held by the name the server gave it, not by the thing it
        // is attached to, so a person who walked out of view while poisoned would otherwise leave their poison
        // hanging in the air where they were standing.
        VfxCue.ForgetAurasOn(netId);
        if (body != null) Destroy(body);
        body = null;
        bodyVisual = null;
        bodyStance = null;
        bodyLoading = false;
        // The pose bookkeeping belonged to the body that just went; a new one starts from nothing.
        showingHeld = false;
    }

    /// <summary>A GM's <c>/model</c> changed what this person wears. Their own machine dresses its avatar; everybody
    /// else drops the copy they drew, and the next frame builds it again in the new model. The name arriving for the
    /// first time is not a change — the body is built by the usual path then.</summary>
    void OnModelChanged(string was, string now)
    {
        if (string.IsNullOrEmpty(was) || was == now) return;
        if (isOwned) { Player3DBody.Wear(now); return; }
        DropTheBody();
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
            // A GM's /model landed while this was loading. The hook already dropped the body and started loading the
            // new one, so this stale load builds nothing and leaves that load's latch alone.
            if (wanted != modelName) return;
            if (model == null)
            {
                GameLog.Warn(LogCat.Net, "no character model called '" + wanted + "' — "
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
            // AND THE STANCE CHOKE POINT ON IT. Held here rather than looked up when it is wanted, because what
            // wants it is a per-frame drive; the builder puts one on the root of every character it makes.
            bodyStance = root.GetComponent<StanceController>();

            // The body belongs to the WORLD, not to the map this machine is standing in — same reason a creature's
            // does. Travelling unloads the map you left, and everything built into it goes with it.
            MapManager.KeepAcrossMaps(root);

            body = root;
            bodyLoading = false;
        });
    }

    /// <summary>THE HEAD THIS PERSON SPEAKS OVER — where a white speech bubble hangs for anything they say or call
    /// out. It is the watched copy this machine built for them; null on their OWN machine, which draws its real
    /// avatar and never builds a copy, and null before their body has finished loading. Either way a null simply
    /// means no bubble, never a missing log row.</summary>
    public IStancePose SpeechPose => bodyVisual != null ? bodyVisual.Pose : null;

    /// <summary>Show a blow the server decided landed on this person.
    ///
    /// <para>The SAME two jobs this class already splits by: on the machine of the person it happened to, it is the
    /// number, the grunt, the flash and the stagger, drawn exactly as the server decided them
    /// (<see cref="Character.ShowBlowFromServer"/>). On everybody else's machine it is a number over somebody's head,
    /// which is all a bystander can honestly know.</para></summary>
    public void ShowBlow(in DamageInfo info, in UnitMitigation.Outcome mitigated, bool tick = false)
    {
        if (isOwned)
        {
            var me = Character.Instance;
            if (me == null) return;
            me.ShowBlowFromServer(info, tick);

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
        Transform on = DrawnBody;
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
        // AND THIS WORLD KNOWS NOTHING ABOUT WHAT THIS BODY IS DOING. Poses are reported only when they CHANGE, and
        // a world we have only just arrived in starts out believing this body is doing nothing — so whatever it is
        // holding right now has to be said again, or it would be held on this screen and on no other until the next
        // time it happened to change. The one place that arrival is known is here.
        if (StanceController.Player != null) StanceController.Player.ForgetWhatWasSaid();
    }

    public override void OnStopClient()
    {
        // THE SAME TIDY-UP A BODY GOING AWAY GETS, because this is a body going away — the person left, or this
        // machine did. Written as the one call so a route out of the world cannot skip a step the other takes.
        DropTheBody();
        base.OnStopClient();
    }
}
