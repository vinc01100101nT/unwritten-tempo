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
public partial class NetPlayer : NetworkBehaviour, IWatchedShell
{
    // ---- what the server holds and every client receives ------------------------------------------------
    // Identity is set ONCE when the body is spawned and never changes; position changes constantly. They are
    // separate so a body that is standing still costs nothing to keep in sync.

    [SyncVar] public string playerName = "";

    /// <summary>WHAT THIS PERSON IS WEARING, written by the server in the same words a creature's worn set travels in
    /// (<see cref="WornList"/>). A watching machine dresses the body it built for them out of it, so the weapon in
    /// their hand is the weapon the server has them holding.
    ///
    /// <para>It had to go on the wire at all because nothing else said it: the body every other machine draws for a
    /// person was built from their MODEL NAME and nothing more, so everybody stood empty-handed on everybody else's
    /// screen no matter what they were carrying.</para></summary>
    [SyncVar(hook = nameof(OnWornArrived))] string worn = "";

    // It landed after this machine had built the body. A body built later dresses itself from `worn` as it is made,
    // so neither order can leave somebody bare-handed.
    void OnWornArrived(string _, string now) { if (bodyGear != null) DressTheBody(); }

    /// <summary>THE COLOURS THIS PERSON CHOSE AT CREATION, written by the server from the character it was handed
    /// (<see cref="CharacterDye3D.PackLook"/>). Without it every other machine drew this person in their model's own
    /// colours, because the only thing that ever recoloured a body was this machine's own character.</summary>
    [SyncVar(hook = nameof(OnLookArrived))] string look = "";

    // Landed after the body was built; a body built later is recoloured from `look` as it is made.
    void OnLookArrived(string _, string now) { if (bodyDye != null) bodyDye.ShowLook(now); }
    CharacterDye3D bodyDye;

    /// <summary>THE HAIR THIS PERSON WEARS, worked out by the server from the character it was handed
    /// (<see cref="HeadPieces.Resolve"/>, packed by <see cref="HeadPieces.Pack"/>) — the chosen style, or for a
    /// character made before hairstyles the hair their body was made with, in the colour they gave it.</summary>
    [SyncVar(hook = nameof(OnHairArrived))] string hair = "";

    // Landed after the body was built; a body built later puts it on as it is made.
    void OnHairArrived(string _, string now) { if (bodyHead != null) bodyHead.ShowHair(now); }
    HeadSocket bodyHead;

    [SyncVar(hook = nameof(OnModelChanged))] public string modelName = "";

    /// <summary>Server-only: what this person looks like — the model and the colours — read from the character the
    /// character server handed over, never from anything their own machine said.</summary>
    public void DressOnServer(SaveData progress)
    {
        modelName = progress != null ? (progress.modelName ?? "") : "";
        look = progress != null ? CharacterDye3D.PackLook(progress.modelName, progress.dyeSources, progress.dyeTargets) : "";
        if (progress != null)
        {
            HeadPieces.Resolve(progress.modelName, progress.hairStyle, progress.hairColour,
                               progress.dyeSources, progress.dyeTargets, out var hairId, out var hairHex);
            hair = HeadPieces.Pack(hairId, hairHex);
        }
        else hair = "";
    }

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

    // THIS PERSON'S LEVEL, for every machine that draws them — their own sheet still gets it privately with their
    // points and primaries. Written on the same beat as health, so a kill, a load and a GM dial all reach it.
    [SyncVar] int netLevel;

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
    // Which beat of that pose it is. A new number while the same pose holds starts its clip over — a second hit
    // restarts the flinch on every screen, not only on the one it happened to.
    [SyncVar] int netDrivenBeat;

    // The standing pose UNDERNEATH all of that — guarding, casting, sitting. It outlives any one action, so it is
    // its own value rather than a second meaning for the one above.
    [SyncVar] bool netHasHeld;
    [SyncVar] StanceId netHeld;
    [SyncVar] float netHeldWindow;

    // WHICH TRAVEL GEAR they are in. Speed alone cannot tell a sprint from a hasted run — both are simply fast — so
    // the machine that owns the legs says which it is, and the body standing in for them on every other screen plays
    // the right one instead of guessing from a threshold.
    [SyncVar] bool netSprinting;

    // HOW FAR OFF THE FLOOR they are. It travels with the POSITION rather than with the pose, because it changes
    // continuously while a pose does not — a jump's height moves every frame it is in the air.
    //
    // It cannot be worked out at the far end: a watched copy has no physics capsule, so it cannot tell real lift
    // from the height the capsule sits at when standing. Without it a jumping player's copy never left the ground.
    // (Their legs need nothing told: a walk, run or sprint plays locked to the travel speed the copy already has.)
    [SyncVar] float netAboveGround;

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

    // A SCRIPTED MOVE — a skill glide, a shove, a blink — SAID ONCE AS IT BEGINS, for the same reason as the hop
    // above. A Dash hop is over in a few hundredths of a second; replayed offline against Thousand Petals' real
    // numbers, a watched copy following the ten-a-second position report reached at most 65% of the hop's speed and
    // missed each star point by up to 0.4 units. The owner's machine says the move and every other screen draws it
    // (WatchedMove), afterimage and fade included, read from its own copy of the skill.
    [SyncVar] MoveSaid netMove;
    readonly WatchedMove watchedMove = new WatchedMove();

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
        // The worn set this person's body is drawn from. Held here so the push beat compares one number rather than
        // walking the set, exactly as a creature's shell does.
        serverGear = unit is Character who && who != null ? who.Gear : null;
        wornVersionSent = int.MinValue;
        BindMapToFighter();
    }

    // What this person wears, and which version of it was last put on the wire.
    UnitEquipment serverGear;
    int wornVersionSent = int.MinValue;

    // SERVER: put what this person wears on the wire, and only when it has actually changed — a new piece equipped,
    // one taken off. A SyncVar written the value it already holds sends nothing, but building the text does not.
    void PushWorn()
    {
        if (serverGear == null || serverGear.Version == wornVersionSent) return;
        wornVersionSent = serverGear.Version;
        worn = WornList.Encode(serverGear);
        // A DIFFERENT WEAPON STARTS ON THE BODY: what was drawn was the old one.
        var weapon = serverGear.Get(EquipSlotType.Weapon);
        var weaponDef = weapon != null ? weapon.def : null;
        if (weaponDef != drawWeaponSeen)
        {
            drawWeaponSeen = weaponDef;
            var host = ServerCombatUnit != null ? ServerCombatUnit.StatusHost : null;
            if (host != null) host.WeaponSwapped();
        }
    }
    Item drawWeaponSeen;

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

    // THIS BODY IS FILED UNDER THE MAP ITS PERSON IS IN — the one way it happens, on arrival and on every map change.
    // The server decides who is told about whom by asking which map each is in before it measures any distance, and
    // the answer it reads is the scene the object actually sits in — not the name on the wire. A person whose body
    // stayed in another map would walk into a town nobody there could see, and see none of its townsfolk.
    // A MAP THIS SERVER IS NOT HOLDING IS SAID OUT LOUD, because the alternative is the quietest failure in this whole
    // layer. Putting a body in a map only works for a map that is actually open here; for any other it does nothing,
    // with no error — and the body is then left filed under the map it was in, at the new map's coordinates.
    void FileBodyInto(string map)
    {
        var into = SceneManager.GetSceneByName(map);
        if (!into.IsValid() || !into.isLoaded)
            GameLog.Warn(LogCat.Server, "" + playerName + " is in '" + map
                             + "', which this server is not holding — they will be filed under '"
                             + gameObject.scene.name + "' and seen by the wrong people. Add it to the map list.");
        MapManager.PutInMap(gameObject, into);
    }

    /// <summary>SERVER ONLY: which character this connection is playing. Held so that the name can be released
    /// again when they leave — a character being played is a fact about the server, not about the wire, so it is
    /// deliberately not a synced value and no client is ever told it.</summary>
    public string ServerCharacterId { get; set; }

    /// <summary>SERVER ONLY: which hand-over of this character this server holds — the number the character server
    /// stamped on it when it sent it here. Every save carries it back, and the character server refuses a save from
    /// an older hand-over, so a server that lost touch can never roll a player back once they are elsewhere. 0 when
    /// the character server sent none, which it then does not check.</summary>
    public int ServerHandover { get; set; }

    /// <summary>SERVER ONLY: where this person was sent, taken from the character the character server pushed —
    /// so the server knows which map they are in from the moment it hands them a body, instead of waiting to be
    /// told by their machine.
    ///
    /// <para>Waiting was the bug: for the first second or so of a session the server had no map for them, so a
    /// save in that window was refused to avoid erasing where they were standing — and walking through a door in
    /// that window aborted the move, because a move begins by writing them down. The server put them there; it
    /// does not need to be told.</para>
    ///
    /// <para>AND THE BODY IS FILED INTO THAT MAP, here, in the same breath. A new body is made in whichever map this
    /// server happened to make current last, and the move into the right one used to wait for the player's machine
    /// to name its map — which never moved it, because the name already matched. The body then stayed in the wrong
    /// map: once a field had been opened and let go, everybody arriving with a save was filed somewhere else, saw no
    /// townsfolk, and was seen by nobody in the town.</para>
    ///
    /// <para>THE MAP IS THE CHARACTER SERVER'S ANSWER, NOT THEIR MACHINE'S. <paramref name="announcedMap"/> is the map
    /// the character server sent them to — their save's, or this server's starting map for a character never played or
    /// saved in a map the game no longer has (that save carries no map at all). Their own machine is never asked: it
    /// may only confirm it (see <see cref="CmdReportMap"/>). And when the save says WHERE in that map, that is where
    /// their first report must land (see <see cref="ExpectArrival"/>).</para></summary>
    public void SetMapOnArrival(SaveData pushed, string announcedMap)
    {
        string map = !string.IsNullOrWhiteSpace(announcedMap) ? announcedMap
                   : pushed != null && !string.IsNullOrWhiteSpace(pushed.sceneName) ? pushed.sceneName
                   : MapServerLink.StartingMap;
        if (!string.IsNullOrWhiteSpace(map) && string.IsNullOrEmpty(mapName))
        {
            mapName = map;
            FileBodyInto(map);
        }
        BindMapToFighter();

        if (pushed == null) return;
        storedMap = pushed.sceneName;
        storedPosition = new Vector3(pushed.posX, pushed.posY, pushed.posZ);
        storedSpawnId = pushed.arriveAtSpawnId;

        // WHERE IN THAT MAP THEIR SAVE PUTS THEM, which is where their machine stands them as the map lands: a spot,
        // or an arrival point of this map. Only when the save's map IS the map they were sent to — a character sent
        // somewhere else is placed by that map, not by an old spot.
        if (pushed.sceneName != mapName) return;
        if (!string.IsNullOrEmpty(pushed.arriveAtSpawnId))
        {
            if (ArrivalPointIn(mapName, pushed.arriveAtSpawnId, out var at)) ExpectArrival(at, false);
        }
        else ExpectArrival(storedPosition, false);
    }

    // WHERE THE STORED CHARACTER STANDS, as it arrived. Written back unchanged until this person's own machine has
    // had one position believed: the map above is known from the first moment, the position is not, and a save in
    // that gap — the character being taken over the instant they connect, or them leaving at once — wrote the right
    // map with the origin in it, so they came back at that map's arrival point. MEASURED 2026-10-05: a character
    // saved at (-11.6, 5.7) was rewritten as (0, 0, 0) 150 ms after connecting, on every sign-in of the run.
    string storedMap;
    Vector3 storedPosition;
    string storedSpawnId;

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
    /// may be held by a different process — and only the server can find out which, or refuse.
    ///
    /// <para>ONLY A DOOR THAT IS THERE. The map named must be joined to the map they are in by a door in the world
    /// layout (<see cref="WorldAtlas.links"/>), and they must be standing at it — otherwise any map name sent here was a
    /// free teleport. Refused, logged, and the curtain their machine raised is lowered.</para></summary>
    [Command]
    public void CmdWalkThroughPortal(string map, string spawnId)
    {
        if (Leaving) return;
        bool atADoor = WorldRoutes.DoorWithin(mapName, map, netPosition.x, netPosition.z, KeeperMath.DoorReach + ReachSlack);
        // A GAME MASTER IS LET THROUGH ANY DOOR — the test maps join each other by doors the world layout does not list,
        // and a GM can already go anywhere with /tp. Said in the log, so nobody mistakes it for a door that exists.
        if (!atADoor && ServerIsGm)
        {
            GameLog.Info(LogCat.Server, "GM " + playerName + " walked through a door to '" + map + "' from '" + mapName
                         + "' with no door of the world layout in reach — let through");
            atADoor = true;
        }
        if (!atADoor)
        {
            GameLog.Warn(LogCat.Server, playerName + " asked to walk through a door to '" + map + "' from '" + mapName
                         + "' at " + Coords.Text(netPosition, "0.0") + " — there is no such door within "
                         + KeeperMath.DoorReach + " m of them. Refused.");
            TargetMoveFailed(connectionToClient, "There is no door here.");
            return;
        }
        // NOT WHILE AUTO-COMBAT IS ON: a hunt that wandered through a door would carry on in a map nobody chose. Their
        // own machine already keeps them from walking into one; this is the answer when it did anyway.
        if (autoCombatOn)
        {
            if (GameLog.On(LogCat.Server)) LogDid(ServerCharacter, "was refused a door to '" + map + "' — auto-combat is on");
            TargetMoveFailed(connectionToClient, "Switch auto-combat off to use a door.");
            return;
        }
        MoveToMap(map, spawnId);
    }

    /// <summary>Server-only: take this person to <paramref name="map"/>. The one way anybody changes map — a door
    /// they walked through and a GM's <c>/tp</c> both come here, so a command cannot skip the save that has to
    /// land before they are handed to another process.
    ///
    /// <para>With <paramref name="toSpot"/> they arrive at <paramref name="spot"/> — a position INSIDE the map, the
    /// same form a save's position is — instead of at an arrival point; a save point has no doorway to name.</para>
    ///
    /// <para><paramref name="onFailure"/> is told when the move does not happen — refused at once, the save before it
    /// failed, or the map would not take them — so whoever paid for the trip can be paid back.</para></summary>
    public void MoveToMap(string map, string spawnId, bool toSpot = false, Vector3 spot = default, Action onFailure = null)
    {
        if (!isServer || Leaving || string.IsNullOrWhiteSpace(map)) { onFailure?.Invoke(); return; }

        // EVERY TRIP ENDS AUTO-COMBAT — a wing home, a Keeper's teleport, a GM's /tp: the hunt was set up where they were.
        StopAutoCombat("Auto-combat stopped — you travelled.");

        // ALREADY HERE: nothing crosses the wire and nothing is written down. This is most doors, and it stays
        // exactly as instant as it has always been.
        // AND THE SERVER PUTS THEM THERE ITSELF, before their machine is told: the map is this server's to set, never
        // their machine's to report, and the spot they will appear at is where it holds them from this moment.
        if (MapHosting.Holding(map))
        {
            bool sameMap = map == mapName;
            Vector3 at = spot;
            bool known = toSpot || ArrivalPointIn(map, spawnId, out at);
            ChangeMapOnServer(map);
            if (known) ExpectArrival(at, sameMap);
            else
            {
                // AN ARRIVAL POINT THIS SERVER CANNOT NAME (none was given, so their machine picks the map's first) —
                // the one case still taken on trust: their next report is believed as it comes.
                ForgiveNextMove();
                GameLog.Warn(LogCat.Server, playerName + " was sent into '" + map + "' with no arrival point the server "
                             + "can name — their next position is taken as read");
            }
            TargetTravelHere(connectionToClient, map, spawnId, toSpot, spot);
            return;
        }

        MarkLeaving();
        MapHosting.Want(map);
        TargetMoveStage(connectionToClient, LoadingScreen.Stage.Asked);

        // WRITTEN DOWN, AND THE WRITE CONFIRMED, BEFORE ANYBODY ELSE IS TOLD ABOUT THEM. The process they are
        // going to is handed the stored character, so a move that started before the write landed would hand
        // over whoever they were fifteen seconds ago and lose everything since.
        // AND WRITTEN DOWN STANDING WHERE THEY ARE GOING — the destination map and spot (or arrival point) — so the
        // character server hands the other process a character already there, and a player who drops during the
        // loading screen comes back where they were sent, with whatever they paid for the trip spent on it. If the
        // move then fails they stay here, and the next save writes them back where they really are.
        var travel = toSpot || !string.IsNullOrEmpty(spawnId)
            ? new TravelTo { map = map, spawnId = spawnId ?? "", toSpot = toSpot, spot = spot } : null;
        SaveNow(ok =>
        {
            if (this == null) return;
            if (!ok)
            {
                Leaving = false;
                SaveNow(null);
                TargetMoveFailed(connectionToClient, "could not save your character");
                onFailure?.Invoke();
                return;
            }
            AskTheCharacterServerToMoveMe(map, spawnId, toSpot, spot, onFailure);
        }, travel);
    }

    // A document stands them in `map`: at `spot` when there is one, otherwise at the arrival point `spawnId`.
    static void WriteWhere(SaveData save, string map, string spawnId, bool atSpot, Vector3 spot)
    {
        save.sceneName = map;
        if (atSpot)
        {
            save.posX = spot.x; save.posY = spot.y; save.posZ = spot.z;
            save.arriveAtSpawnId = "";
        }
        else
        {
            save.posX = save.posY = save.posZ = 0f;
            save.arriveAtSpawnId = spawnId ?? "";
        }
    }

    /// <summary>Where a server-decided trip sets somebody down, written into the save that hands them over.</summary>
    public sealed class TravelTo
    {
        public string map, spawnId;
        public bool toSpot;
        public Vector3 spot;
    }

    // SERVER: this person is now in `map`, because the server put them there. The one place their map changes on this
    // process — everything that follows a map change follows from here.
    void ChangeMapOnServer(string map)
    {
        if (map == mapName) return;
        mapName = map;
        FileBodyInto(map);
        BindMapToFighter();
        // Where the followed quest's step happens is told from where they stand: a new map can mean a new next door,
        // and a different nearest smith to mark.
        SendQuests(true);
        // The copy of their storage this server held is let go; the next Keeper reads it fresh.
        serverStorage.Clear();
        nextStorageReadAt = 0f;
        SaveSoon();
    }

    // SERVER: where an arrival point of a map this server holds stands, inside that map — the same choice their machine
    // makes (PersistentPlayer3D.ResolveSpawn): the point named, or with none named in the default map, its default.
    // FALSE when it cannot be told: no point of that name, or none named elsewhere (their machine then takes whichever
    // the map lists first). Asked once per trip, never per frame.
    static bool ArrivalPointIn(string map, string spawnId, out Vector3 at)
    {
        at = default;
        string want = !string.IsNullOrEmpty(spawnId) ? spawnId : map == SavePoint.DefaultMap ? SavePoint.DefaultSpawnId : null;
        var scene = SceneManager.GetSceneByName(map);
        if (want == null || !scene.IsValid() || !scene.isLoaded) return false;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var point in root.GetComponentsInChildren<SpawnPoint>(true))   // perf-ok: once per trip
                if (point.id == want) { at = MapManager.InMap(point.transform.position, map); return true; }
        return false;
    }

    void AskTheCharacterServerToMoveMe(string map, string spawnId, bool toSpot, Vector3 spot, Action onFailure)
    {
        TargetMoveStage(connectionToClient, LoadingScreen.Stage.Saved);

        var body = JsonUtility.ToJson(new MoveAsk { map = map, accountId = ServerAccountId ?? "", isGm = ServerIsGm });
        ServerHttp.Send("POST", MapServerLink.CharacterServerUrl + "/server/characters/" + ServerCharacterId + "/move",
                        body, MapServerLink.ServerSecret, (ok, reply, error, _) =>
        {
            if (this == null) return;
            var where = ok ? JsonUtility.FromJson<MoveReply>(reply) : null;
            if (!ok || where == null || string.IsNullOrEmpty(where.ticket))
            {
                // THEY STAY WHERE THEY ARE. Nothing has been handed over, so putting the latch down leaves them
                // exactly as they were before they touched the door.
                Leaving = false;
                GameLog.Warn(LogCat.Server, "could not move " + playerName + " to '" + map + "' — " + error + " " + reply);
                // The save that went ahead of the move stood them at the destination; this one puts them back, now.
                SaveNow(null);
                TargetMoveFailed(connectionToClient, "that place would not take you just now");
                onFailure?.Invoke();
                return;
            }

            GameLog.Info(LogCat.Server, playerName + " is moving to '" + map + "' at " + where.host + ":" + where.port);
            TargetMoveTo(connectionToClient, where.host, where.port, where.ticket, map, spawnId, toSpot, spot);
        });
    }

    [TargetRpc]
    void TargetTravelHere(NetworkConnectionToClient target, string map, string spawnId, bool toSpot, Vector3 spot)
    {
        if (toSpot) SceneTravel.ArriveAtSpot(map, spot);
        SceneTravel.ArriveLocally(map, toSpot ? null : spawnId);
    }

    // The loading screen goes up at Asked — the server started this move, whether or not anybody pressed for it.
    [TargetRpc]
    void TargetMoveStage(NetworkConnectionToClient target, LoadingScreen.Stage stage)
    {
        if (stage == LoadingScreen.Stage.Asked) LoadingScreen.Show();
        else LoadingScreen.Reached(stage);
    }

    /// <summary>SERVER ONLY: a move this person's machine asked for will not happen — the loading screen it raised on
    /// the press comes down, and <paramref name="why"/> is said.</summary>
    public void RefuseTrip(string why)
    {
        if (isServer && connectionToClient != null) TargetMoveFailed(connectionToClient, why);
    }

    [TargetRpc]
    void TargetMoveTo(NetworkConnectionToClient target, string host, int port, string ticket, string map, string spawnId,
                      bool toSpot, Vector3 spot)
        => GameNetwork.MoveToAnotherServer(host, port, ticket, map, spawnId, toSpot, spot);

    /// <summary>SERVER ONLY: take this person to their save point — or, when they have never set one, to
    /// <see cref="SavePoint.DefaultMap"/>'s default arrival point. <paramref name="onFailure"/> is told when the trip
    /// does not happen (see <see cref="MoveToMap"/>).</summary>
    public void SendToSavePoint(Action onFailure = null)
    {
        SavePointTrip(ServerCharacter, out var map, out var spawnId, out bool toSpot, out var spot);
        MoveToMap(map, spawnId, toSpot, spot, onFailure);
    }

    /// <summary>Where <paramref name="c"/>'s save point puts them: a spot in a map, or an arrival point. A save point set
    /// at a Keeper names only the town, so the spot is worked out now from where that town's Keeper stands; a town with
    /// no Keeper any more, like a character with no save point, sends them to the default arrival point.</summary>
    static void SavePointTrip(Character c, out string map, out string spawnId, out bool toSpot, out Vector3 spot)
    {
        spawnId = null; toSpot = false; spot = default;
        if (c != null && c.HasSavePoint)
        {
            if (!c.SavePointAtKeeper)
            {
                map = c.SavePointMap; toSpot = true; spot = c.SavePointPosition;
                return;
            }
            var keeper = WorldTownsfolk.KeeperIn(c.SavePointMap);
            if (keeper != null)
            {
                map = c.SavePointMap; toSpot = true;
                spot = KeeperMath.ArrivalSpot(keeper.home, keeper.facing, KeeperMath.ArrivalAhead);
                return;
            }
        }
        map = SavePoint.DefaultMap;
        spawnId = SavePoint.DefaultSpawnId;
    }

    /// <summary>SERVER ONLY: make where this person is standing right now their save point — the GM's <c>/save</c>.
    /// FALSE when there is nowhere to record yet: no character, or no map they have said they are in. Written down
    /// on the next save beat like any other change.</summary>
    public bool MakeSavePointHere()
    {
        var c = ServerCharacter;
        if (!isServer || c == null || string.IsNullOrEmpty(mapName)) return false;
        c.SetSavePoint(mapName, netPosition);
        SaveSoon();
        return true;
    }

    /// <summary>The map this person is standing in, as the server has it.</summary>
    public string ServerMapName => mapName;
    /// <summary>Where inside <see cref="ServerMapName"/> they are standing, as the server last accepted it.</summary>
    public Vector3 ServerPositionInMap => netPosition;

    [TargetRpc]
    void TargetMoveFailed(NetworkConnectionToClient target, string why)
    {
        LoadingScreen.Hide();
        OverheadNotice.Fail(why);
    }

    [System.Serializable]
    class MoveAsk { public string map; public string accountId; public bool isGm; }

    [System.Serializable]
    class MoveReply { public string host; public int port; public string ticket; public string map; }

    // How often the owner tells the server where it is. Ten times a second is far below the frame rate and
    // still smoother than a person can walk; sending every frame would be bandwidth spent on nothing.
    public const float ReportInterval = 0.1f;
    float nextReport;

    // Below these, a change is not worth a packet — a player standing perfectly still should cost nothing.
    const float MoveEpsilon = 0.01f;
    const float TurnEpsilon = 1f;      // degrees
    Vector3 lastReported;
    float lastReportedYaw;
    float lastReportedAbove;

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

    /// <summary>THE PERSON AT THIS KEYBOARD, as their shell. Held so anything that needs to ask the server
    /// something on their behalf has one place to find them instead of searching the scene for the owned shell.
    /// Null on a server, which steers nobody, and between leaving one world and arriving in the next.</summary>
    public static NetPlayer Mine { get; private set; }

    /// <summary>EVERY OTHER PERSON'S BODY THIS MACHINE HAS BUILT, as the health copy that speaks for it. The same
    /// shape as the creature registry: a plain list, filed when a body is built and struck when it is dropped, so a
    /// cursor scan walks the people on screen without searching the scene. Never holds your own body.</summary>
    public static readonly System.Collections.Generic.List<Health> WatchedBodies =
        new System.Collections.Generic.List<Health>(8);

    // THE SHELL THAT SPEAKS FOR A BODY THIS MACHINE BUILT, found by that body. The exact mirror of the table
    // creatures already keep (NetMob.ShellForView) and for the same reason: a watched body is a separate object
    // from the shell that speaks for it, so there is no parent to walk up to.
    static readonly System.Collections.Generic.Dictionary<GameObject, NetPlayer> byBody =
        new System.Collections.Generic.Dictionary<GameObject, NetPlayer>();

    /// <summary>The network identity of whatever unit <paramref name="watchedBody"/> is the drawn copy of — a
    /// creature's or a person's. ONE answer for both, so anything pointing at a body has a single way to name the
    /// unit it belongs to when it asks the server about it.</summary>
    public static NetworkIdentity ShellIdentityFor(GameObject watchedBody)
    {
        if (watchedBody == null) return null;
        var mob = NetMob.ShellForView(watchedBody);
        if (mob != null) return mob.netIdentity;
        var person = ShellForBody(watchedBody);
        return person != null ? person.netIdentity : null;
    }

    /// <summary>The person whose body this machine drew as <paramref name="watchedBody"/>, or null when it is not
    /// another person's body — the mirror of <see cref="NetMob.ShellForView"/>.</summary>
    public static NetPlayer ShellForBody(GameObject watchedBody)
    {
        for (var t = watchedBody != null ? watchedBody.transform : null; t != null; t = t.parent)
            if (byBody.TryGetValue(t.gameObject, out var person) && person != null) return person;
        return null;
    }

    /// <summary>This person's level, as the server has it.</summary>
    public int Level => netLevel;

    /// <summary>What this person wears, in the server's words — the same text that dresses their body here.</summary>
    public string WornText => worn;

    void Update()
    {
        if (isServer)
        {
            ServerSaveTick();
            AutoCombatServerTick();
            EmblemServerTick();
            SendOwedBag();
            if (savePointTripOwed) TakeOwedTrip();
            if (arrivalExpected) StandThemWhereSentIfLate();
            if (questsPushOwed) { questsPushOwed = false; SendQuests(true); }
        }

        // The bag that arrived too early, handed over the moment there is somebody to hand it to.
        if (bagWaitingForACharacter.HasValue && Character.Instance != null)
        {
            var waiting = bagWaitingForACharacter.Value;
            bagWaitingForACharacter = null;
            waiting.Onto(Character.Instance);
            InventoryStore.Instance?.Notify();
        }

        // …and the mastery, held for the same reason — a server resends it only when it changes, so one parked and
        // never handed over would leave the Skills window empty until the next kill.
        GiveWaitingMasteryToTheCharacter();

        // …and the character sheet, held for the same reason.
        if (sheetWaiting && Character.Instance != null)
        {
            sheetWaiting = false;
            Character.Instance.SetSheetFromServer(waitingCurrent, waitingBaseline, waitingWithStates,
                                                  waitingRaiseCost, waitingGearStat);
        }

        // THREE possible askers, not two — and this is where the server used to fall through the crack. "Am I the
        // one steering this?" is false on a server for EVERY player, because the server steers nobody, so it took
        // the watcher's branch and built a visible body for each person who connected: a rigged model fetched,
        // assembled, and blended every frame on a machine with no screen to draw it on.
        //
        // The creature shells never had this problem because they ask a different question. So this asks it too:
        // a body is built only by a machine that is actually WATCHING.
        if (isServer) { PushMyHealth(); PushMySheet(); PushMyMastery(); PushMyRing(); PushWorn(); PushDraw(); ForgetSpeedIfTheyStopped(); return; }
        if (isOwned)
        {
            ReportMyPosition();
            TakeTheServersWord();
            // THE SMOKE AT THE LEVEL CAP, on the avatar this machine really draws for its own person.
            var mine = PersistentPlayer3D.Instance;
            levelAura.Show(netLevel, mine != null ? mine.transform : null);
            return;
        }
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

    // ---- "if equipped" lines for a piece in the bag (EquipPreview) ------------------------------------------
    //
    // The pointer resting on a piece asks; the server derives the sheet as if it were worn and sends the finished
    // lines back to this connection alone. At most a few a second per person — a hover asks once per piece until the
    // character next changes, so honest play never comes near it.
    const int MostPreviewsPerSecond = 8;
    int previewsThisSecond;
    float previewSecondStarted;

    /// <summary>PLAYER'S MACHINE: ask what wearing copy <paramref name="copyId"/> in <paramref name="slot"/> would change.</summary>
    public void AskEquipPreview(string copyId, EquipSlotType slot) => CmdAskEquipPreview(copyId, (int)slot);

    [Command]
    void CmdAskEquipPreview(string copyId, int slot)
    {
        if (Leaving) return;
        if (Time.time - previewSecondStarted >= 1f) { previewSecondStarted = Time.time; previewsThisSecond = 0; }
        if (++previewsThisSecond > MostPreviewsPerSecond) return;
        var c = ServerCharacter;
        if (c == null || string.IsNullOrEmpty(copyId) || copyId.Length > 64) return;
        if (!System.Enum.IsDefined(typeof(EquipSlotType), slot) || slot == (int)EquipSlotType.None) return;
        // Only a piece they carry in the bag: a worn piece has nothing to be compared with.
        if (!c.FindCopy(copyId, out int bagIndex, out bool worn) || worn) return;
        var inst = c.Bag[bagIndex].instance;
        if (inst == null || inst.def == null) return;
        TargetEquipPreview(connectionToClient, copyId, slot, EquipPreview.Text(c, inst, (EquipSlotType)slot));
    }

    [TargetRpc]
    void TargetEquipPreview(NetworkConnectionToClient target, string copyId, int slot, string text)
        => EquipPreview.TakeSent(copyId, (EquipSlotType)slot, text);

    [Command]
    public void CmdActionBegan(StanceId id, float seconds)
    {
        if (Leaving || !RealStance(id)) return;
        BeganAction(id, SanePose(seconds));
    }

    [Command]
    public void CmdDrivenStance(bool has, StanceId id, float window, bool release, int beat)
    {
        if (Leaving) return;
        if (has && !RealStance(id)) return;
        netHasDriven = has;
        netDriven = id;
        netDrivenWindow = SanePose(window);
        netDrivenRelease = release;
        netDrivenBeat = beat;
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

    // The authored effect behind the last move this server told the owner to make. A move's look is only ever passed
    // on as this one — so a machine cannot name some other skill's afterimage, or pile readings of its own choosing.
    RelocationLook lookSentToOwner;

    /// <summary>The owner's machine began a scripted move (a glide or a blink). A picture of a move it is making
    /// anyway — it decides nothing, so it is kept sane rather than trusted: no move outlasts <see cref="LongestHop"/>,
    /// for the same reason no hop does, and its look is the one this server sent, or none.</summary>
    [Command]
    public void CmdMoveBegan(MoveSaid said)
    {
        if (Leaving) return;
        if (said.kind == MoveSaid.Glide && (said.speed <= 0f || said.window <= 0f)) return;
        if (said.kind != MoveSaid.Glide && said.kind != MoveSaid.Blink) return;
        said.easeSeconds = Mathf.Clamp(said.easeSeconds, 0f, LongestHop);
        said.window = said.kind == MoveSaid.Glide ? Mathf.Clamp(said.window, 0.05f, LongestHop) : 0f;
        bool sameEffect = said.look.Has && said.look.skillId == lookSentToOwner.skillId
                       && said.look.binding == lookSentToOwner.binding && said.look.effect == lookSentToOwner.effect;
        said.look = sameEffect ? lookSentToOwner : RelocationLook.None;
        // STAMPED HERE, never by the machine that asked — the rule the hop and the swing already answer to.
        said.at = NetworkTime.time;
        netMove = said;
    }

    // ---- combat or peace: the asks this person's machine sends, and the server's word sent back ----------------

    /// <summary>Where this person's weapon is, said by the server to every machine — their own included.</summary>
    [SyncVar(hook = nameof(OnDrawArrived))] DrawSaid netDraw;
    /// <summary>Where this person's weapon is, as the server last said — what a body built later is shown.</summary>
    public DrawSaid CurrentDraw => netDraw;
    WeaponSocket bodySocket;
    int drawVersionSent = int.MinValue;
    float nextWeaponAskAt;
    int drawnTooFastReports;
    float orderedMoveUntil;   // a glide or shove this server ordered is fast on purpose — not a sprint, until then
    float orderedMoveSpeed;   // …and how fast it goes: the movement check allows this much until it ends

    void OnDrawArrived(DrawSaid _, DrawSaid now) => ShowDraw(now);

    // Every body this machine draws for this person shows the same word: their own avatar on their own machine, the
    // watched copy everywhere else. A body built later is shown it as it is made.
    void ShowDraw(DrawSaid said)
    {
        if (isOwned)
        {
            WeaponDrawClient.Told(said.Phase);
            if (WeaponSocket.OnMyAvatar != null) WeaponSocket.OnMyAvatar.ShowDraw(said);
            return;
        }
        if (bodySocket != null) bodySocket.ShowDraw(said);
    }

    // SERVER: put the draw state on the wire when it changes. The phase's start travels on the clock both machines
    // share, worked back from how long ago it began here.
    void PushDraw()
    {
        var host = ServerCombatUnit != null ? ServerCombatUnit.StatusHost : null;
        if (host == null || host.Draw.Version == drawVersionSent) return;
        drawVersionSent = host.Draw.Version;
        netDraw = new DrawSaid
        {
            phase = (byte)host.Draw.Phase,
            at = NetworkTime.time - (Time.time - host.Draw.PhaseStart),
            window = host.Draw.Window,
        };
    }

    /// <summary>This person asks for their weapon out (true) or away (false). The server decides; a dead person, a
    /// person leaving, and asks faster than a few a second are turned away.</summary>
    [Command]
    public void CmdWeapon(bool draw)
    {
        if (Leaving || Time.time < nextWeaponAskAt) return;
        nextWeaponAskAt = Time.time + 0.15f;
        var unit = ServerCombatUnit;
        var host = unit != null ? unit.StatusHost : null;
        if (host == null || unit.IsDead) return;
        if (draw) host.RequestDraw(); else host.RequestSheathe();
    }

    /// <summary>This person started or stopped walking. Walking puts the weapon away.</summary>
    [Command]
    public void CmdWalking(bool on)
    {
        if (Leaving || !on) return;
        var host = ServerCombatUnit != null ? ServerCombatUnit.StatusHost : null;
        if (host != null) host.RequestSheathe();
    }

    // SERVER: a drawn weapon cannot sprint. The sprint report is this person's own machine talking, so their measured
    // speed decides as well: three accepted steps in a row faster than a run puts the weapon away, said in the log.
    void PutAwayIfSprintingDrawn()
    {
        var unit = ServerCombatUnit;
        var host = unit != null ? unit.StatusHost : null;
        if (host == null || host.Draw.IsSheathed || Time.time < orderedMoveUntil) { drawnTooFastReports = 0; return; }
        var spec = CharacterSpec.Active;
        float moveMult = unit.States != null ? unit.States.MoveMult : 1f;
        if (spec == null || !WeaponDrawMath.FasterThanRunning(acceptedGroundSpeed, spec.RunGearSpeed, moveMult)) { drawnTooFastReports = 0; return; }
        if (++drawnTooFastReports < 3) return;
        drawnTooFastReports = 0;
        if (host.Draw.Phase == DrawPhase.Drawn || host.Draw.Phase == DrawPhase.Drawing)
        {
            host.RequestSheathe();
            GameLog.Info(LogCat.Server, string.Concat(SpawnIds.MapOf(unit), " ", playerName, " moved faster than a run with their weapon out (",
                acceptedGroundSpeed.ToString("0.##"), " u/s) — putting it away"));
        }
    }

    [Command]
    public void CmdSprinting(bool on)
    {
        if (Leaving || netSprinting == on) return;
        netSprinting = on;
        // Sprinting puts the weapon away.
        if (on && ServerCombatUnit != null && ServerCombatUnit.StatusHost != null) ServerCombatUnit.StatusHost.RequestSheathe();
        // AND THE PUFF THAT GOES WITH IT, said from here so it reaches every screen — the person's own included.
        // Their machine no longer plays a cue it decided itself; it plays what the world tells it, exactly as it
        // already does for their own spells.
        if (on) VfxCue.Fire(VfxCueId.OnSprint, transform);
    }

    // ---- the server's half: say how hurt this person is --------------------------------------------------

    float nextHealthPush;

    float nextRatesCheck;
    const float RatesCheckSeconds = 60f;

    void PushMyHealth()
    {
        if (Time.time < nextHealthPush) return;
        nextHealthPush = Time.time + ReportInterval;

        var fighter = ServerCombatUnit;
        if (fighter == null) return;
        // ONCE A MINUTE, their per-hour rates if they moved — they fall as the minutes pass with no kill, and the
        // player's machine may not lower them itself. Nothing is sent once they rest.
        if (Time.time >= nextRatesCheck && fighter is Character earner && earner != null)
        {
            nextRatesCheck = Time.time + RatesCheckSeconds;
            GameNetwork.TellOwnerTheirRatesIfMoved(earner);
        }
        netHealth = fighter.CurrentHP;
        netMaxHealth = Mathf.Max(1, fighter.MaxHP);
        if (fighter is Character person && person != null)
        {
            netMana = person.CurrentMP;
            netMaxMana = Mathf.Max(1, person.MaxMP);
            netLevel = person.Level;
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
    readonly int[] sentGearStat = new int[6];

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
            int cost = person.CostToRaise(stat);
            int gear = person.GearStat(stat);
            if (cost != sentRaiseCost[i] || gear != sentGearStat[i])
                changed = true;
            sentRaiseCost[i] = cost;
            sentGearStat[i] = gear;
        }
        if (!changed) return;

        sheetSent = true;
        sentCurrent = current;
        sentBaseline = baseline;
        sentWithStates = withStates;
        // Copied only on a change, which is when a new sheet is worth a send at all.
        TargetSheet(connectionToClient, current, baseline, withStates,
                    (int[])sentRaiseCost.Clone(), (int[])sentGearStat.Clone());
    }

    [TargetRpc]
    void TargetSheet(NetworkConnectionToClient target, DerivedSnapshot current, DerivedSnapshot baseline,
                     DerivedSnapshot withStates, int[] raiseCost, int[] gearStat)
    {
        // A new sheet may change what any piece would do if worn: the kept "if equipped" lines are asked for again.
        EquipPreview.ForgetSent();
        var me = Character.Instance;
        if (me != null) { me.SetSheetFromServer(current, baseline, withStates, raiseCost, gearStat); return; }
        // ARRIVED BEFORE THERE WAS A CHARACTER TO GIVE IT TO. The server only sends a sheet when it changes, so a
        // dropped one would leave zeros on the window until something happened to move a stat. Held until there is.
        sheetWaiting = true;
        waitingCurrent = current;
        waitingBaseline = baseline;
        waitingWithStates = withStates;
        waitingRaiseCost = raiseCost;
        waitingGearStat = gearStat;
    }

    bool sheetWaiting;
    DerivedSnapshot waitingCurrent, waitingBaseline, waitingWithStates;
    int[] waitingRaiseCost, waitingGearStat;

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

    // ---- WHAT IS RUNNING ON THE UNIT YOU HAVE SELECTED ------------------------------------------------------
    //
    // A person's own buffs go only to them (above), because only they need to know how long their own window has
    // left. But a watcher who has selected somebody has to be able to see what is on them, and a single answer sent
    // once would be a lie two seconds later. So this is a SUBSCRIPTION with a scope: while one unit is on your target
    // panel, the server tells you about that unit's buffs — once straight away, and again on every change — and the
    // moment the panel lets it go it stops. Nothing is sent about anybody nobody is looking at, so the cost is
    // one watcher times one unit, not everybody times everybody.
    //
    // It serves a creature and a person identically, because what is being watched is only ever "a unit".

    // THE UNIT THIS CONNECTION HAS SELECTED, and the store being listened to on its behalf. Server-side only.
    UnitStates pointedAtStates;
    NetworkIdentity pointedAt;

    /// <summary>The states of the unit this shell speaks for, as the SERVER holds them. This is a person's real
    /// store, the one their skills are decided against.</summary>
    public UnitStates ServerStates => watchedStates;

    /// <summary>The store on the body THIS machine drew for this person, which is where a watcher's copy of their
    /// buffs is kept. Null until their body has been built.</summary>
    public UnitStates WatchedStates => bodyHealth != null ? bodyHealth.States : null;

    // WHAT WAS LAST SAID TO THE SERVER, so pointing at the same unit for a hundred frames sends one message and
    // not a hundred. The server ignores a repeat as well; this simply stops it ever being asked.
    NetworkIdentity lastPointedSent;

    /// <summary>Tell the server which unit's details the person at this keyboard has up, named by the body this
    /// machine drew for it — or nothing at all. Nothing on screen calls this today. The server
    /// then keeps sending that unit's buffs and debuffs. Safe to call every frame: it sends only when the answer
    /// changes.
    ///
    /// <para>Takes a plain object rather than a network handle so the readout — a piece of screen furniture — does not
    /// have to know anything about the wire.</para></summary>
    public static void PointAt(GameObject watchedBody)
    {
        if (Mine == null) return;
        var shell = ShellIdentityFor(watchedBody);
        if (shell == Mine.lastPointedSent) return;
        Mine.lastPointedSent = shell;
        Mine.CmdPointAt(shell);
    }

    /// <summary>Point at a unit, or at nothing. Called by the target panel; the answer comes back on
    /// <see cref="TargetPointedAtStates"/> and keeps coming until this is called again.</summary>
    [Command]
    public void CmdPointAt(NetworkIdentity who)
    {
        if (who == pointedAt) return;

        if (pointedAtStates != null) pointedAtStates.OnChanged -= PushPointedAtStates;
        pointedAt = who;
        pointedAtStates = StatesHeldBy(who);

        if (pointedAtStates == null) return;
        pointedAtStates.OnChanged += PushPointedAtStates;
        PushPointedAtStates();
    }

    // Whichever kind of unit this identity speaks for, its real store on this machine. A shell that is neither —
    // a door, a dropped item — has none, and pointing at it simply subscribes to nothing.
    static UnitStates StatesHeldBy(NetworkIdentity who)
    {
        if (who == null) return null;
        if (who.TryGetComponent<NetMob>(out var mob)) return mob.ServerStates;
        if (who.TryGetComponent<NetPlayer>(out var person)) return person.ServerStates;
        return null;
    }

    void PushPointedAtStates()
    {
        if (pointedAtStates == null || pointedAt == null || connectionToClient == null) return;
        pointedAtStates.SnapshotForOwner(statesScratch);
        TargetPointedAtStates(connectionToClient, pointedAt, statesScratch);
    }

    /// <summary>What is running on the unit you have selected. Written onto the copy of that unit THIS machine drew,
    /// which is where every readout already looks.</summary>
    [TargetRpc]
    void TargetPointedAtStates(NetworkConnectionToClient target, NetworkIdentity who,
                               System.Collections.Generic.List<UnitStates.Mirrored> states)
    {
        var store = WatchedStatesOf(who);
        if (store != null) store.MirrorFromServer(states);
    }

    /// <summary>The store on the body this machine drew for that unit — a creature's or a person's.</summary>
    public static UnitStates WatchedStatesOf(NetworkIdentity who)
    {
        if (who == null) return null;
        if (who.TryGetComponent<NetMob>(out var mob)) return mob.WatchedStates;
        if (who.TryGetComponent<NetPlayer>(out var person)) return person.WatchedStates;
        return null;
    }

    // THE SUBSCRIPTION DIES WITH THE CONNECTION. Left attached, a store would keep a handler belonging to somebody
    // who has gone, and every change on that unit would try to speak to an empty seat.
    public override void OnStopServer()
    {
        if (pointedAtStates != null) pointedAtStates.OnChanged -= PushPointedAtStates;
        pointedAtStates = null;
        pointedAt = null;
        ReleaseEmblemOnServer();
        base.OnStopServer();
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
        /// <summary>Teleport / Glide: the authored effect behind it, so the owner's machine can read its afterimage
        /// and fade out of its own copy of the skill. <see cref="RelocationLook.None"/> otherwise.</summary>
        public RelocationLook look;
        /// <summary>Teleport: a wing's trip — the owner's screen fades to black, they are moved and the camera put on
        /// them while it is black, then it fades back.</summary>
        public bool fadeThroughBlack;
    }

    /// <summary>SERVER ONLY: hand this motion to the machine that owns the legs.</summary>
    public void MoveOnOwnersMachine(NetworkConnectionToClient owner, Motion motion)
    {
        // WE MOVED THEM, SO WE DO NOT THEN ACCUSE THEM OF IT. Every motion the server decides — a teleport, a
        // glide, a shove, and the GM commands that ride on them — lands this person somewhere the movement check
        // has no way to reach at walking pace, and the report that follows would look exactly like a lie.
        //
        // It is decided HERE rather than at each of the three kinds, because this is the one door they all pass
        // through: a fourth kind added later inherits it instead of quietly missing it.
        //
        // A TELEPORT IS NOT FORGIVEN — IT IS EXPECTED. The server knows exactly where it put them, so it holds them there
        // from this moment and believes no report until one lands there: a report already in the air from before the
        // jump is dropped rather than read as a lie, and a machine that answers with any other spot gets nowhere.
        // A glide and a shove travel over time to a place their own machine works out, so those are still forgiven.
        if (motion.kind == Motion.Kind.Teleport) ExpectArrival(motion.destination, true);
        else ForgiveNextMove();
        // And a move this server ordered is not a sprint either: how long it lasts, from its own distance and speed.
        var spec = CharacterSpec.Active;
        float runSpeed = spec != null ? spec.RunGearSpeed : 1f;
        float lasts = motion.kind == Motion.Kind.Glide
            ? WeaponDrawMath.OrderedMoveSeconds(Vector3.Distance(netPosition, motion.destination), runSpeed * motion.speed, motion.seconds, LongestHop)
            : motion.kind == Motion.Kind.Shove ? WeaponDrawMath.OrderedMoveSeconds(0f, 1f, motion.seconds, LongestHop) : 0f;
        orderedMoveUntil = Mathf.Max(orderedMoveUntil, Time.time + lasts);
        // A DASH IS FASTER THAN ANYTHING THE MOVEMENT CHECK EXPECTS — Thousand Petals glides at 8× the run gear, past
        // three sprints — so while it lasts, the check is told its speed. Forgiving only the first report was not
        // enough: a long dash sent four more, each refused, and the fourth dragged the dasher back to where they began.
        orderedMoveSpeed = WeaponDrawMath.OrderedMoveSpeed(runSpeed, motion.kind == Motion.Kind.Glide ? motion.speed : 0f);
        lookSentToOwner = motion.look;
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
            case Motion.Kind.Teleport:
                if (motion.fadeThroughBlack) TeleportThroughBlack(dest, motion.look);
                else me.TeleportTo(dest, motion.look);
                break;
            case Motion.Kind.Glide:
                me.GlideTo(dest, motion.speed, motion.easeIn, motion.easeOut, motion.seconds,
                           motion.facing ? face : (Vector3?)null, motion.look);
                break;
            case Motion.Kind.Shove: me.Shove(dest, motion.speed, motion.seconds, motion.stagger); break;
        }
    }

    // A WING'S TRIP: the screen goes black, the body is moved and the camera put straight on it — smooth follow would
    // otherwise pan the whole way there — and the screen comes back. The server already holds them at the landing
    // spot and drops reports from the old one, so moving a fade later loses nothing.
    static void TeleportThroughBlack(Vector3 dest, RelocationLook look)
    {
        string map = MapManager.CurrentMap;
        ScreenFade.Through(() =>
        {
            var me = Character.Instance;
            // Left the map while the screen went dark: that landing spot belongs to a map no longer here.
            if (me == null || MapManager.CurrentMap != map) return;
            me.TeleportTo(dest, look);
            if (CameraFollow3D.Main != null) CameraFollow3D.Main.SnapToTarget();
        });
    }

    /// <summary>SERVER ONLY: tell the owner's machine which creature a taunt has locked them onto.</summary>
    public void TauntOnOwnersMachine(NetworkConnectionToClient owner, uint taunterNetId) => TargetTaunted(owner, taunterNetId);

    [TargetRpc]
    void TargetTaunted(NetworkConnectionToClient target, uint taunterNetId)
    {
        var me = Character.Instance;
        if (me == null) return;
        if (!NetworkClient.spawned.TryGetValue(taunterNetId, out var identity) || identity == null) return;
        var shell = identity.GetComponent<NetMob>();
        var mob = (shell != null ? shell.WatchedTarget : identity.GetComponent<IDamageable>()) as Health;
        if (mob != null) me.LockOntoTaunter(mob);
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
        // NOT UNTIL THE BODY HAS ARRIVED. Before that it is still standing at the sign-in screen's origin or in the map
        // being left, and the server takes the first report after joining as read — so one sent early became the place
        // it held, and the real arrival a moment later was refused as an impossible jump and dragged back to it.
        if (me == null || !me.HasArrived) return;

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
        // READ FROM ITS SINGLE OWNER, never re-derived here: the visual is what separates real lift from the
        // capsule's resting offset.
        // perf-ok: resolved once and held. A destroyed object reads as null, so walking through a door re-resolves
        // it against the new body — the same shape the turned part below uses, for the same reason.
        if (playerVisual == null && StanceController.Player != null)
            playerVisual = StanceController.Player.GetComponent<Character3DVisual>();
        float above = playerVisual != null ? playerVisual.HeightAboveGround : 0f;

        // TURNING COUNTS AS A CHANGE, and it did not before. Only a step used to be worth a packet, so a person
        // who stood their ground and turned to face you never sent anything — they went on facing whichever way
        // they had last walked, on everybody else's screen, for as long as they stood there. Standing and turning
        // is exactly what a person does when somebody walks up to them, so this was the common case.
        // RISING OR FALLING COUNTS AS A CHANGE TOO. A jump straight up moves the feet barely at all on the flat, so
        // the two tests above would have held the whole hop back and everybody else would have seen nothing.
        if (hasReportedOnce
            && (p - lastReported).sqrMagnitude < MoveEpsilon * MoveEpsilon
            && Mathf.Abs(Mathf.DeltaAngle(lastReportedYaw, yaw)) < TurnEpsilon
            && Mathf.Abs(above - lastReportedAbove) < MoveEpsilon) return;

        lastReported = p;
        lastReportedYaw = yaw;
        lastReportedAbove = above;
        hasReportedOnce = true;
        CmdReportPosition(p, yaw, above);
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
        if (needsSaving && Time.time >= nextSaveAt) { SaveNow(null); return; }
        if (needsHotbarSaving && Time.time >= nextHotbarSaveAt) SaveHotbarNow(null);
    }

    // ---- the hotbar's own row --------------------------------------------------------------------------------
    // The bar is written ONLY when it changed, and on its own — a rearrangement never rewrites the whole character,
    // and the 15-second save never rewrites the bar. The same beat, so a player dragging slots about is one write.
    bool needsHotbarSaving;
    float nextHotbarSaveAt;
    int hotbarVersionWritten = -1;

    /// <summary>Server-only: the bar changed and should be written to its row before long.</summary>
    public void SaveHotbarSoon()
    {
        if (!isServer) return;
        if (!needsHotbarSaving) nextHotbarSaveAt = Time.time + SecondsBetweenSaves;
        needsHotbarSaving = true;
    }

    // Write the bar now if it changed since it was last written; answers yes at once when it did not.
    void SaveHotbarNow(Action<bool> whenDone)
    {
        needsHotbarSaving = false;
        var c = ServerCharacter;
        if (c == null || string.IsNullOrEmpty(ServerCharacterId)) { whenDone?.Invoke(true); return; }
        // UNCHANGED SINCE IT WAS LAST SENT — but that send may still be in the air, or waiting to be tried again. The
        // answer waits for it, because whoever takes the character over reads the bar's row as it stands.
        if (c.Hotbar.Version == hotbarVersionWritten) { SaveOutbox.FlushHotbar(ServerCharacterId, whenDone); return; }
        hotbarVersionWritten = c.Hotbar.Version;
        var body = JsonUtility.ToJson(new HotbarEnvelope { slots = c.Hotbar.Capture(), handover = ServerHandover });
        // Once a bar has landed in its own row, the old bar the save still carries (before v18) is never read again.
        SaveOutbox.SubmitHotbar(ServerCharacterId, body, ok =>
        {
            if (ok && c != null) c.LegacyHotbar = null;
            whenDone?.Invoke(ok);
        });
    }

    [Serializable]
    class HotbarEnvelope { public string[] slots; public int handover; }

    /// <summary>Server-only: the bar as it stands is what the store already holds — set when the bar arrives with the
    /// character, so an unchanged bar is never written back.</summary>
    public void HotbarAlreadyStored() { if (ServerCharacter != null) hotbarVersionWritten = ServerCharacter.Hotbar.Version; }

    /// <summary>Server-only: write this character down NOW. <paramref name="whenDone"/> is told whether the state
    /// captured by THIS call reached the store — the disconnect path waits on it, because a character released
    /// before the write has finished is how the same items end up in two places.
    ///
    /// <para>With <paramref name="travel"/> the document stands them where a trip the server decided is sending them —
    /// the save that hands them to another process (see <see cref="MoveToMap"/>).</para></summary>
    public void SaveNow(Action<bool> whenDone, TravelTo travel = null)
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
        // ONLY A PLACE THIS SERVER HAS ACTUALLY BELIEVED. Until one report has been, the position here is the empty
        // one every object starts with, so the place the character arrived with is written back instead.
        if (haveAcceptedAPosition)
        {
            save.sceneName = mapName;
            save.posX = netPosition.x;
            save.posY = netPosition.y;
            save.posZ = netPosition.z;
        }
        else
        {
            save.sceneName = storedMap;
            save.posX = storedPosition.x;
            save.posY = storedPosition.y;
            save.posZ = storedPosition.z;
            save.arriveAtSpawnId = storedSpawnId;
        }

        // WRITTEN DOWN WHILE DEAD — they chose another character, logged out, quit or dropped from the death window.
        // They come back exactly as "Return to last saved point" would have put them: at their save point, on a
        // tenth of their health, mana as it was, and nothing timed carried across the death.
        if (c.IsDead)
        {
            SavePointTrip(c, out var spMap, out var spSpawn, out bool spAtSpot, out var spAt);
            WriteWhere(save, spMap, spSpawn, spAtSpot, spAt);
            save.currentHP = SavePoint.RespawnHealth(c.MaxHP);
            save.timedEffects = null;
        }

        // A TRIP THE SERVER DECIDED: they are written down where it is sending them.
        if (travel != null) WriteWhere(save, travel.map, travel.spawnId, travel.toSpot, travel.spot);

        needsSaving = false;
        // WHICH HAND-OVER THIS COPY BELONGS TO, so the character server can refuse it if the character has been
        // sent to another server since — a late save from here must never roll that player back.
        // AND EVERY QUEST RECORD THAT CHANGED since the last save that landed — in this same document, so the character
        // server writes the bag and the quest that judged it in one transaction, together or not at all.
        var quests = c.Quests.CapturePending(out int questsUpTo);
        // AND A STORAGE MOVE STILL WAITING, in every document until the character server has answered it — a take also
        // puts the thing in this document's bag (see StorageMoves).
        var storageMove = StorageMoves.For(ServerCharacterId);
        StorageMoves.Compose(save, storageMove);
        var body = JsonUtility.ToJson(new ProgressEnvelope { progress = save, quests = quests, handover = ServerHandover,
                                                             storage = storageMove ?? new StorageMoveWire() });

        // HANDED TO THE SERVER'S ONE OUTBOX, which outlives this shell. One document on the wire per character, the
        // newest waiting behind it, and a save the character server cannot take right now is kept and tried again
        // until it lands — even after this person has logged out, or this server has restarted.
        // AND THE BAR AND THE AUTO-COMBAT SETTINGS, IF EITHER CHANGED, in the same breath, and the emblem's row if it is
        // still in the air — and the answer waits for all four, because a hand-over reads the rows together.
        bool allOk = true;
        int waiting = 4;
        void One(bool ok)
        {
            allOk &= ok;
            if (--waiting == 0) whenDone?.Invoke(allOk);
        }
        // Once this document is done with — on whichever attempt — the quest records it carried are no longer owed.
        var questLog = c.Quests;
        SaveOutbox.Submit(ServerCharacterId, body, One, () => questLog.Acknowledge(questsUpTo));
        SaveHotbarNow(One);
        SaveAutoCombatNow(One);
        SaveEmblemIfOwed(One);
    }



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
    // A RING POINT IS REAL POWER TOO, and is decided here like a stat point: the stat must be one of the cards the
    // server's own draft is showing, the wildcard being the one the server rolled. Whatever the answer, the walk as
    // it now stands goes straight back, so a refused pick redraws the window instead of leaving it waiting.
    void CmdPickRingStat(int stat)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null) return;
        if (System.Enum.IsDefined(typeof(StatId), stat) && c.Passives.MayPick((StatId)stat)
            && c.Passives.Pick((StatId)stat))
        {
            c.Passives.RollWildcard(null);
            GameLog.Info(LogCat.Server, playerName + " put a ring point in " + (StatId)stat
                         + " — " + c.Passives.BankedPoints + " left");
            SaveSoon();
            nextSheetPush = 0f;
        }
        else GameLog.Info(LogCat.Server, playerName + " was refused a ring point in " + stat);
        sentRingVersion = -1;
        PushMyRing();
    }

    public void AskToPickRingStat(StatId stat) => CmdPickRingStat((int)stat);

    // ---- the ring's walk: the server spends the points, this machine draws them -------------------------
    // Sent whenever it moved (a pick, a ring level banking a point, a load) and on arrival.
    int sentRingVersion = -1;

    void PushMyRing()
    {
        if (connectionToClient == null) return;
        if (ServerCombatUnit is not Character person || person == null) return;
        var p = person.Passives;
        if (p.HasLeader && !p.HasWildcard) p.RollWildcard(null);   // a walk loaded from a save has no wildcard yet

        if (p.Version == sentRingVersion) return;
        sentRingVersion = p.Version;

        var map = p.RankMap;
        var stats = new int[map.Count];
        var values = new int[map.Count];
        int i = 0;
        foreach (var kv in map) { stats[i] = (int)kv.Key; values[i] = kv.Value; i++; }
        // What each stat is worth now and what one more point would make it, in whole percent, indexed by StatId —
        // the window copies these and works out none of them.
        int count = System.Enum.GetValues(typeof(StatId)).Length;
        var percentNow = new int[count];
        var percentNext = new int[count];
        for (int s = 0; s < count; s++)
        {
            int ranks = p.RanksIn((StatId)s);
            percentNow[s] = SkillRing.PercentPoints((StatId)s, ranks);
            percentNext[s] = SkillRing.PercentPoints((StatId)s, ranks + 1);   // server-math: PushMyRing runs on the server only — it leaves at once with no client connection
        }
        TargetRing(connectionToClient, stats, values, (int)p.Leader, p.HasLeader, p.BankedPoints,
                   (int)p.Wildcard, p.HasWildcard, percentNow, percentNext);
    }

    [TargetRpc]
    void TargetRing(NetworkConnectionToClient target, int[] stats, int[] values, int leader, bool hasLeader,
                    int banked, int wildcard, bool hasWildcard, int[] percentNow, int[] percentNext)
    {
        ringWaitingForACharacter = new object[] { stats, values, leader, hasLeader, banked, wildcard, hasWildcard,
                                                  percentNow, percentNext };
        GiveWaitingRingToTheCharacter();
    }

    object[] ringWaitingForACharacter;

    void GiveWaitingRingToTheCharacter()
    {
        if (ringWaitingForACharacter == null || Character.Instance == null) return;
        var r = ringWaitingForACharacter;
        ringWaitingForACharacter = null;
        var ints = (int[])r[0];
        var stats = new StatId[ints.Length];
        for (int i = 0; i < ints.Length; i++) stats[i] = (StatId)ints[i];
        Character.Instance.Passives.TakeFromServer(stats, (int[])r[1], (StatId)(int)r[2], (bool)r[3], (int)r[4],
                                                   (StatId)(int)r[5], (bool)r[6], (int[])r[7], (int[])r[8]);
    }

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
        // So does the bag: how much they can carry moves with STR (BagRoom), and the bag message carries it.
        PushBagToOwner();
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
        // NOTHING THEY MAY NOT USE. A bar is the player's to arrange, but what may go on it is the server's
        // answer — and it is the SAME question a cast asks (Character.MayUse), so a skill that belongs to no
        // weapon, which nobody learns, can still be bound and still fires.
        if (bindings.Length != HotbarBindings.SlotCount) return;
        c.KeepOnlyBindable(bindings);
        if (GameLog.On(LogCat.Server)) LogHotbarChange(c, bindings);
        c.Hotbar.Restore(bindings);
        // AND THE BAR THE SERVER NOW HOLDS GOES BACK, so a bar the server sent while this was in the air (an item used
        // up) cannot leave the player's screen showing something the server no longer has.
        PushBagToOwner(alsoTheBar: true);
        // AND THE SKILLS BEHIND THE NEW BAR. What a character can use is built from three places — the bar, their
        // learned passives and their bag — and a skill only gets something to run it when that list is
        // rebuilt. Storing the bar without this left the server agreeing the skill was theirs and holding nothing to
        // cast it with, so the press was refused: their own machine spent the mana and the cooldown and the skill did
        // nothing. Their machine has always done this on the very same change (HotbarCommit.Now); this side never was.
        c.RefreshUsableSkills();
        SaveHotbarSoon();
    }

    // "set hotbar slot 3 to skill.lunge, cleared slot 5": only the slots that MOVED, so a bar sent whole on every change
    // reads as the one thing the player did. Written before the new bar is laid on, while the old one can be read.
    void LogHotbarChange(Character c, string[] bindings)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < bindings.Length; i++)
        {
            string was = c.Hotbar.Get(i) ?? "";
            string now = bindings[i] ?? "";
            if (was == now) continue;
            sb.Append(sb.Length > 0 ? ", " : "");
            if (now.Length == 0) sb.Append("cleared slot ").Append(i + 1);
            else sb.Append("set slot ").Append(i + 1).Append(" to ").Append(now);
        }
        if (sb.Length > 0) LogDid(c, "changed their hotbar: " + sb);
    }

    /// <summary>Client-side: tell the server this person spent a point / rearranged their bar. Silently does
    /// nothing with no world, where their own machine already is the only thing that knows.</summary>
    public void AskToSpendStatPoint(PrimaryStat stat) => CmdSpendStatPoint((int)stat);
    public void AskToSetHotbar(string[] bindings) => CmdSetHotbar(bindings);

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
    /// one thing that makes this side rewrite somebody's hotbar. Their
    /// machine then draws the bar this one decided instead of working the same rule out again. Every other change
    /// leaves the bar out — see the note in <see cref="BagWire"/>.</para>
    ///
    /// <para>Pass <paramref name="alsoTheBuyback"/> when what they can buy back changed — arriving, a sale, a buyback —
    /// for the same reason.</para></summary>
    public void PushBagToOwner(bool alsoTheBar = false, bool alsoTheBuyback = false)
    {
        if (!isServer) return;
        // OWED, AND SENT ONCE THIS FRAME. A purse change now asks for this by itself (PlayerFighter subscribes it to
        // the character's gold), so a kill that also pushes the bag the old way would otherwise send it twice.
        bagPushOwed = true;
        barPushOwed |= alsoTheBar;
        buybackPushOwed |= alsoTheBuyback;
    }

    bool bagPushOwed, barPushOwed, buybackPushOwed;

    void SendOwedBag()
    {
        if (!bagPushOwed) return;
        bool alsoTheBar = barPushOwed;
        bool alsoTheBuyback = buybackPushOwed;
        bagPushOwed = false;
        barPushOwed = false;
        buybackPushOwed = false;
        if (ServerCombatUnit is not Character fighter || fighter == null) return;
        // A PIECE GIVEN AWAY LEAVES THE BAR, on the same push that tells them it is gone. (An item used up stays.)
        if (fighter.PruneHotbar()) { alsoTheBar = true; SaveHotbarSoon(); }
        // WHAT THEY CAN USE CHANGED TOO. A consumable's effect is a skill, and the bag is one of the three places
        // that list is built from — so a potion picked up mid-session had nothing behind it here and drinking it
        // did nothing at all. This is the one place every change the server makes to a bag passes through, which
        // is why the rebuild sits here rather than at each of them.
        fighter.RefreshUsableSkills();
        TargetBagChanged(connectionToClient, BagWire.From(fighter, alsoTheBar, alsoTheBuyback));
        // AND EVERY QUEST STEP JUDGED ON WHAT THEY HOLD, here for the same reason: this is the one place every bag
        // change passes through. What they can pay for moves with the purse, so the followed step's answer is
        // re-sent whenever it changed even when no step moved.
        if (fighter.Quests.Advance(fighter)) QuestsMoved();
        else SendQuests(false);
    }

    // ---- what they have practised and learned ---------------------------------------------------------------
    //
    // A player's machine works out no mastery of its own: it is told what was earned, how many points each weapon
    // has left, the level bought in every skill, and — for the hover card — every growing number now and at the next
    // level. Checked on the same beat as the sheet, and sent only when it moved: a mastery level is thousands of kills
    // wide, and a skill level moves only when a point is spent.
    double sentPracticeTotal = -1.0;
    int sentLevelsVersion = -1;
    // The weapon in hand and its grade decide which progress is shown and whether it is capped, so a swap resends.
    WeaponType sentHeldWeapon = (WeaponType)(-1);
    Quality sentHeldGrade;
    // The card's numbers also carry the ring's investment and the trademark of the exact weapon in hand.
    int sentRingRanks = -1;
    string sentTrademark = "";

    void PushMyMastery()
    {
        if (connectionToClient == null) return;
        if (ServerCombatUnit is not Character person || person == null) return;

        person.Mastery.Capture(out var weapons, out var points, out var skillIds, out var levels);
        double total = 0.0;
        for (int i = 0; points != null && i < points.Length; i++) total += points[i];
        var held = person.HeldWeapon;
        var grade = person.HeldQuality;
        int version = person.Mastery.LevelsVersion;
        int ringRanks = person.Passives.TotalRanks();
        var heldPiece = person.GetEquippedInstance(EquipSlotType.Weapon);
        string trademark = heldPiece != null ? heldPiece.trademarkSkillId ?? "" : "";
        // The card's numbers move only with a level bought, the weapon in hand or a ring pick — never with a kill's
        // practice — so a kill resends the small arrays and leaves them out.
        bool withViews = version != sentLevelsVersion || held != sentHeldWeapon || grade != sentHeldGrade
                         || ringRanks != sentRingRanks || trademark != sentTrademark;
        if (System.Math.Abs(total - sentPracticeTotal) < 0.001 && !withViews) return;

        sentPracticeTotal = total;
        sentLevelsVersion = version;
        sentHeldWeapon = held;
        sentHeldGrade = grade;
        sentRingRanks = ringRanks;
        sentTrademark = trademark;
        person.Mastery.CaptureProgress(held, grade, out var progWeapons, out var into, out var size, out var capped);
        string[] viewIds = null; int[] counts = null; float[] now = null, next = null;
        if (withViews) person.Mastery.CaptureViews(person, out viewIds, out counts, out now, out next);
        TargetMastery(connectionToClient, weapons, points, skillIds, levels, person.Mastery.EarnedLevels(weapons),
                      person.Mastery.PointsLeft(weapons), progWeapons, into, size, capped,
                      withViews, viewIds, counts, now, next);
    }

    [TargetRpc]
    void TargetMastery(NetworkConnectionToClient target, int[] weapons, double[] points,
                       string[] skillIds, int[] skillLevels, int[] levels, int[] pointsLeft,
                       int[] progWeapons, double[] into, double[] size, bool[] capped,
                       bool withViews, string[] viewIds, int[] counts, float[] now, float[] next)
    {
        var p = new object[] { weapons, points, skillIds, skillLevels, levels, pointsLeft,
                               progWeapons, into, size, capped, withViews, viewIds, counts, now, next };
        // A delivery still parked keeps the card's numbers it carried when a later one arrives without any.
        if (!withViews && masteryWaitingForACharacter != null && (bool)masteryWaitingForACharacter[10])
            for (int i = 10; i < p.Length; i++) p[i] = masteryWaitingForACharacter[i];
        // THE NEWEST DELIVERY IS THE ONLY ONE THAT COUNTS. Whatever was parked is now older than this, so it is
        // dropped here — handed over later, it would put back levels and points that are no longer true.
        masteryWaitingForACharacter = null;
        if (Character.Instance == null) { masteryWaitingForACharacter = p; return; }
        TakeMastery(p);
    }

    // The same parking the sheet and the bag do: what arrives before this machine has built its character.
    object[] masteryWaitingForACharacter;

    void GiveWaitingMasteryToTheCharacter()
    {
        if (masteryWaitingForACharacter == null || Character.Instance == null) return;
        var p = masteryWaitingForACharacter;
        masteryWaitingForACharacter = null;
        TakeMastery(p);
    }

    // ONE way in for what the server sent, parked or not, so the two paths can never take it differently.
    static void TakeMastery(object[] p)
    {
        var me = Character.Instance;
        var weapons = (int[])p[0];
        me.Mastery.Restore(weapons, (double[])p[1], (string[])p[2], (int[])p[3]);
        me.Mastery.TakeSentLevels(weapons, (int[])p[4]);
        me.Mastery.TakeSentPointsLeft(weapons, (int[])p[5]);
        me.Mastery.TakeSentProgress((int[])p[6], (double[])p[7], (double[])p[8], (bool[])p[9]);
        if ((bool)p[10]) me.Mastery.TakeSentViews((string[])p[11], (int[])p[12], (float[])p[13], (float[])p[14]);
        me.RefreshUsableSkills();
    }

    /// <summary>Client-side: ask the server to spend one mastery point on <paramref name="skillId"/>. The server
    /// decides; what comes back is the whole mastery picture, or a line saying why not.</summary>
    public void AskToSpendSkillPoint(string skillId) => CmdSpendSkillPoint(skillId);

    float nextRefusalLine;

    [Command]
    void CmdSpendSkillPoint(string skillId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null || string.IsNullOrEmpty(skillId) || skillId.Length > 128) return;
        bool spent = c.Mastery.TrySpend(skillId, out string why);
        // A refusal is written as often as it is said to them — once a second at most — and a point spent every time.
        if (GameLog.On(LogCat.Server) && (spent || Time.unscaledTime >= nextRefusalLine))
            LogDid(c, spent ? "spent a skill point on " + skillId
                            : string.Concat("was refused a skill point on ", skillId, " — ", why));
        if (!spent)
        {
            // One reason a second at most: a client clicking a grey + in a loop must not turn into a chat flood.
            if (connectionToClient != null && !string.IsNullOrEmpty(why) && Time.unscaledTime >= nextRefusalLine)
            {
                nextRefusalLine = Time.unscaledTime + 1f;
                TargetTold(connectionToClient, why);
            }
            return;
        }
        // A skill bought for the first time may be a passive, which only runs once the list of what this character
        // can use is rebuilt — the same rebuild a bar change makes.
        c.RefreshUsableSkills();
        SaveSoon();
        PushMyMastery();
    }

    // WHAT ARRIVED BEFORE THERE WAS ANYBODY TO GIVE IT TO. The bag can land before this machine has built its
    // character, and dropping it left the person with nothing until something else happened to change it —
    // with no way to ask again.
    BagWire? bagWaitingForACharacter;

    [TargetRpc]
    void TargetBagChanged(NetworkConnectionToClient target, BagWire bag)
    {
        // A piece's folds, grade or place in the bag may have changed: the kept "if equipped" lines are asked for again.
        EquipPreview.ForgetSent();
        var me = Character.Instance;
        GiveWaitingMasteryToTheCharacter();
        GiveWaitingRingToTheCharacter();
        if (me == null)
        {
            // A LATER BAG REPLACES AN EARLIER ONE STILL WAITING — but the bar and the buy-back list ride only the pushes
            // that changed them (null otherwise), so one the earlier bag carried is kept rather than dropped by a later
            // push that carried nothing; nothing would ever send it again.
            if (bagWaitingForACharacter.HasValue)
            {
                var earlier = bagWaitingForACharacter.Value;
                if (bag.hotbarBindings == null) bag.hotbarBindings = earlier.hotbarBindings;
                if (bag.buyback == null) bag.buyback = earlier.buyback;
            }
            bagWaitingForACharacter = bag;
            return;
        }
        bag.Onto(me);
        // The window redraws from what just arrived. NOT marked as needing saving: this IS what the server has,
        // so writing it back would be this machine telling the server what the server just told it.
        InventoryStore.Instance?.Notify();
    }

    /// <summary>Server-only: tell this connection that something has just arrived in their bag, so their chat can
    /// say so. Called from the ONE place a thing enters any bag, never from whatever caused it — a kill, a
    /// command, a thing picked up and anything written later all reach the player through this one line.
    ///
    /// <para>The ITEM'S NAME is not sent: what crosses is its id and the two numbers that decide how the line
    /// reads, and the other machine looks the rest up in the catalog it already has — the same way it builds the
    /// words for a thing lying on the ground.</para></summary>
    public void TellOwnerTheyGot(string itemId, int count, int folds, Rarity rarity, int cardSlots)
    {
        if (!isServer || connectionToClient == null) return;
        TargetGotItem(connectionToClient, itemId, count, folds, (int)rarity, cardSlots);
    }

    /// <summary>Server-only: a sentence from the game to the person playing here, in their chat log, in red — why a
    /// press was refused, and anything else they would otherwise only find in the server's log.</summary>
    public void TellOwner(string line)
    {
        if (!isServer || connectionToClient == null || string.IsNullOrEmpty(line)) return;
        TargetTold(connectionToClient, line);
    }

    [TargetRpc]
    void TargetTold(NetworkConnectionToClient target, string line) => ChatController.GameSaid(line, false);

    /// <summary>SERVER ONLY: say in this person's chat that a thing could not go into their bag, and why. Said once per
    /// thing and reason for <paramref name="quietFor"/> seconds — a pickup held down asks every frame, and auto-loot
    /// meets the same full tab on every kill.</summary>
    public void TellNoRoom(Item def, BagRefusal why, string verb = "take", float quietFor = 1f)
    {
        if (!isServer || def == null || why == BagRefusal.None || ServerCombatUnit is not Character c) return;
        string key = def.id + (why == BagRefusal.SlotsFull ? "#slots" : "#weight");
        if (noRoomQuietUntil.TryGetValue(key, out float until) && Time.unscaledTime < until) return;
        noRoomQuietUntil[key] = Time.unscaledTime + quietFor;
        string line = c.RefusalLine(def, why, verb);
        if (GameLog.On(LogCat.Server)) LogDid(c, "had no room: " + line);
        if (connectionToClient != null) TargetTold(connectionToClient, line);
    }

    readonly System.Collections.Generic.Dictionary<string, float> noRoomQuietUntil =
        new System.Collections.Generic.Dictionary<string, float>();

    [TargetRpc]
    void TargetGotItem(NetworkConnectionToClient target, string itemId, int count, int folds, int rarity, int cardSlots)
    {
        var def = ItemCatalog.Resolve(itemId);
        if (def == null) return;
        ChatController.ItemArrived(def, count, folds, cardSlots);
        LootCards.Show(def, count, folds, (Rarity)rarity, cardSlots);
    }

    /// <summary>The server's copy of whoever is playing here, or null when this connection has no fighter
    /// yet. Every bag command below goes through it, so "is there anybody to do this to" is asked once.</summary>
    Character ServerCharacter => ServerCombatUnit as Character;

    /// <summary>Is the thing at <paramref name="index"/> still the thing the asker was looking at? The one check,
    /// shared with using an item: <see cref="BagStack"/>.</summary>
    bool StillTheSameStack(Character c, int index, string expectedItemId)
        => BagStack.StillTheSame(c, index, expectedItemId, playerName);

    [Command]
    void CmdEquipFromBag(int index, string expectedItemId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (!StillTheSameStack(c, index, expectedItemId)) return;
        EquipFromBagAt(c, index);
    }

    /// <summary>Client-side: equip exactly the piece of gear with this id — what a hotbar gear slot asks for. The server
    /// finds the piece by its id, wherever it sits in the bag now, so a second press can never put on another copy of
    /// the same item.</summary>
    public void AskToEquipCopy(string copyId) => CmdEquipCopy(copyId);

    [Command]
    void CmdEquipCopy(string copyId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null || string.IsNullOrEmpty(copyId) || copyId.Length > 64) return;
        if (!c.FindCopy(copyId, out int index, out bool worn) || worn) return;
        EquipFromBagAt(c, index);
    }

    // THE ONE EQUIP from the bag, whichever way the piece was named.
    void EquipFromBagAt(Character c, int index)
    {
        var e = c.Bag[index];
        if (e == null || !e.IsEquip || e.instance == null) return;

        var prev = c.GetEquippedInstance(e.def.slot);
        c.SetEquipped(e.def.slot, e.instance);
        c.Bag.RemoveAt(index);
        // BY HAND ON PURPOSE. The piece being displaced is already theirs and is only moving from their body back
        // into their bag — putting it through the arrival door would tell them they had "obtained" a sword they
        // have been carrying all day, on every single equip.
        if (prev != null && prev.def != null)
            c.Bag.Add(new BagEntry { def = prev.def, count = 1, instance = prev });
        PushBagToOwner();
        SaveSoon();
        // SAID IN CHAT, every time, so a person pressing a gear slot in a fight knows the swap really happened.
        if (connectionToClient != null) TargetTold(connectionToClient, "Equipped " + e.def.displayName + ".");
        if (GameLog.On(LogCat.Server))
            LogDid(c, string.Concat("equipped ", e.def.displayName, " (", e.def.slot.ToString(), ")",
                                    prev != null && prev.def != null ? " in place of " + prev.def.displayName : ""));
    }

    [Command]
    void CmdUnequipToBag(int slot)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null) return;
        var worn = c.GetEquippedInstance((EquipSlotType)slot);
        if (worn == null) return;

        // A FREE BOX FOR IT in its tab, or it stays on (BagRoom): what is worn takes no box, so taking it off needs one.
        var noRoom = c.RoomFor(worn.def, 1);
        if (noRoom != BagRefusal.None) { TellNoRoom(worn.def, noRoom, "take off"); return; }

        // Taking the weapon off empties the bar, for the same reason putting one on refills it.
        bool theirBarChanges = (EquipSlotType)slot == EquipSlotType.Weapon;

        c.SetEquipped((EquipSlotType)slot, (EquipmentInstance)null);
        // By hand, for the same reason as the swap above: taking your own gear off is not obtaining it.
        if (worn.def != null) c.Bag.Add(new BagEntry { def = worn.def, count = 1, instance = worn });
        PushBagToOwner(theirBarChanges);
        SaveSoon();
        if (GameLog.On(LogCat.Server) && worn.def != null)
            LogDid(c, string.Concat("took off ", worn.def.displayName, " (", ((EquipSlotType)slot).ToString(), ")"));
    }

    /// <summary>A pile of one Ink or Salt becomes one of the next tier up (CraftingMath.MergeCount). Deterministic — nothing is rolled and
    /// nothing can fail — so the only answers are "done" and "not enough", and the bag that comes back says
    /// which. The stack is named by position AND id, like every other bag command, so a bag that moved under
    /// the asker cannot merge the wrong pile.</summary>
    [Command]
    void CmdMergeMaterial(int index, string expectedItemId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (!StillTheSameStack(c, index, expectedItemId)) return;
        var e = c.Bag[index];
        if (e == null || e.def == null) return;
        if (!CraftingMath.Merges(e.def.materialKind) || e.def.mergesInto == null)
        { Refused(e.def.displayName + " does not merge."); return; }
        int need = CraftingMath.MergeCount(e.def.materialKind, e.def.materialStep);
        if (need <= 0) { Refused(e.def.displayName + " does not merge."); return; }
        if (e.count < need)
        { Refused("Merging needs " + need + " " + e.def.displayName + " — you have " + e.count + ".",
                  "Merging needs " + need + " — you have " + e.count); return; }

        var into = e.def.mergesInto;
        // The pile going out is counted off first: its weight, and its entry when it is the whole stack.
        int freedSlots = e.count == need && into.category == e.def.category ? -1 : 0;
        var noRoom = c.RoomFor(into, 1, freedSlots, -e.def.weight * need);
        if (noRoom != BagRefusal.None) { Refused(c.RefusalLine(into, noRoom, "make"), "No room in your bag"); return; }
        e.count -= need;
        if (e.count <= 0) c.Bag.RemoveAt(index);
        c.TakeIntoBag(into, 1);
        PushBagToOwner();
        Done("Merged " + need + " " + e.def.displayName + " into 1 " + into.displayName + ".", "Merged");
        SaveSoon();
    }

    // ---- what the player is told about every craft, success and refusal alike --------------------------
    //
    // EVERY OUTCOME IS SAID IN ONE SENTENCE, to the chat and to the station window, because from the player's
    // side a refused press and a broken button look the same (2026-09-21: a forge short of Copper did nothing
    // and said nothing). The reason carries its numbers — what was needed and what they have.

    // `notice` is the short form for the stacking notice (OverheadNotice.MostCharacters); the chat gets the whole sentence.
    // A refusal already short enough is its own short form.
    void Refused(string why, string notice = null) => TellOwnerAboutCraft(why, notice ?? why, false, false);
    void Done(string what, string notice, bool pieceGone = false) => TellOwnerAboutCraft(what, notice, true, pieceGone);
    // A try that went through and was paid for, but whose luck failed: logged as a craft, shown in the failure colour.
    void Failed(string what, string notice) => TellOwnerAboutCraft(what, notice, false, false, true);

    void TellOwnerAboutCraft(string line, string notice, bool ok, bool pieceGone, bool pressWentThrough = false)
    {
        // THE SAME SENTENCE IN THE LOG, refusals included — every craft press ends here, so what a tester tried at a
        // station, and what stopped them, is written once for all of them.
        if (isServer && !string.IsNullOrEmpty(line) && GameLog.On(LogCat.Server))
            LogDid(ServerCharacter, (ok || pressWentThrough ? "crafted: " : "was refused a craft: ") + line);
        if (!isServer || connectionToClient == null || string.IsNullOrEmpty(line)) return;
        TargetCraftResult(connectionToClient, line, notice, ok, pieceGone);
    }

    // `pieceGone` says the piece the window had picked no longer exists (it was recycled), so the window lets go
    // of it rather than pointing at whatever slid into its place.
    [TargetRpc]
    void TargetCraftResult(NetworkConnectionToClient target, string line, string notice, bool ok, bool pieceGone)
    {
        ChatController.GameSaid(line, ok);
        OverheadNotice.Result(notice, ok);
        CraftStationPanel.ShowResult(line, ok, pieceGone);
    }

    // "You are not at a smith." — the station a piece needs, named, when the asker is not standing at one.
    static string NotAtStation(Item item)
        => "You are not at a " + CraftStations.Label(CraftStations.For(item)).ToLowerInvariant() + ".";

    // ---- the craft stations: fold, quench, ink, recycle --------------------------------------------------
    //
    // EVERY ONE OF THESE HAPPENS HERE, on the server, and nowhere else. A station window asks; it never decides,
    // never rolls and never works out a cost — it draws the offer below and sends a press. The asker must be
    // standing at a station of the piece's own craft, which is checked here rather than trusted from the window.

    // How close a player must be to a townsperson to work at their station. The same reach the conversation uses,
    // with the same slack, because a person's position arrives ten times a second and is always slightly behind.
    const float StationReach = 3.5f;

    // THE NEAREST STATION, not the nearest person. A villager standing a hand's width closer than the smith
    // would otherwise refuse every press, silently, with the window still open.
    CraftStation StationInReach(CraftStation wanted)
    {
        if (wanted == CraftStation.None) return CraftStation.None;
        return TownNPC.NearestStation(transform.position, StationReach + ReachSlack, wanted) != null
               ? wanted : CraftStation.None;
    }

    // True when this asker is standing at a station that works on `item`.
    bool AtTheRightStation(Item item)
    {
        var needed = CraftStations.For(item);
        return needed != CraftStation.None && StationInReach(needed) == needed;
    }

    /// <summary>Fold a piece once: it rises by one, and its own numbers are worked out again. Refused at the
    /// grade's fold cap — past there the only way on is a quench.</summary>
    [Command]
    void CmdFold(int index, string expectedItemId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (!StillTheSameStack(c, index, expectedItemId)) return;
        Fold(c, c.Bag[index].instance);
    }

    /// <summary>The same fold, of the piece worn in <paramref name="slot"/> — it stays on while it is worked.</summary>
    [Command]
    void CmdFoldWorn(int slot, string expectedItemId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        Fold(c, WornPiece(c, slot, expectedItemId));
    }

    /// <summary>SERVER: the piece worn in <paramref name="slot"/>, when it is the item the asker named; null otherwise.</summary>
    static EquipmentInstance WornPiece(Character c, int slot, string expectedItemId)
    {
        if (c == null || slot == (int)EquipSlotType.None || !System.Enum.IsDefined(typeof(EquipSlotType), slot)) return null;
        var worn = c.GetEquippedInstance((EquipSlotType)slot);
        return worn != null && worn.def != null && worn.def.id == expectedItemId ? worn : null;
    }

    void Fold(Character c, EquipmentInstance inst)
    {
        if (c == null || inst == null || inst.def == null) return;
        if (!AtTheRightStation(inst.def)) { Refused(NotAtStation(inst.def)); return; }
        if (inst.upgrade >= CraftingMath.FoldCap(inst.quality))
        { Refused(ItemTooltip.CopyTitle(inst) + " is folded as far as " + GradeWord(inst.quality) + " allows (+" + CraftingMath.FoldCap(inst.quality) + "). Quench it to go further.",
                  "Folded as far as it goes"); return; }

        var dust = MaterialCatalog.TemperDust;
        if (dust == null) { Refused("No Temper Dust is authored."); return; }
        int units = CraftingMath.FoldMaterialCost(inst.quality);
        int gold = CraftingMath.FoldGoldCost(inst.quality);
        inst.EnsureCopyId();
        // A FREE FOLD IS ASKED ABOUT BEFORE THE ROLL AND USED UP ONLY ON A SUCCESS: a new character has no gold to pay
        // for the try again, so a failed free fold stays free.
        int before = inst.upgrade;
        bool free = c.Quests.FoldIsFree(inst.def.id, inst.copyId, inst.quality, before);
        if (!free && !Spend(c, dust, units, gold)) return;

        bool landed = CombatRng.Range(0, 100) < CraftingMath.FoldChancePct(inst.quality, before);
        if (!landed)
        {
            inst.upgrade = CraftingMath.FoldFailFolds(before);
            inst.Recompute();
            Failed("The fold failed — " + ItemTooltip.CopyTitle(inst) + " is now +" + inst.upgrade
                   + (free ? ". The free fold is still yours." : ", and the " + Paid(dust, units, gold) + " is spent."),
                   "Fold failed: +" + inst.upgrade);
            AfterWorkingOnGear(c, inst);
            return;
        }
        if (free) c.Quests.TakeFreeFold(inst.def.id, inst.copyId, inst.quality, before);
        c.Quests.NoteReplayFold(inst.def.id, inst.copyId, inst.quality, before);
        inst.upgrade = before + 1;
        inst.Recompute();
        Done("Folded to " + ItemTooltip.CopyTitle(inst) + (free ? " — free, your first time."
             : " for " + Paid(dust, units, gold) + "."), "Folded to +" + inst.upgrade);
        AfterWorkingOnGear(c, inst);
    }

    /// <summary>Add a card slot to a piece: one more on a success, the slots it had on a failure, never past the cap
    /// (a weapon 4, an armour piece 2). Paid in Quench Oil and gold, tripling for every slot already there.</summary>
    [Command]
    void CmdSlot(int index, string expectedItemId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (!StillTheSameStack(c, index, expectedItemId)) return;
        Slot(c, c.Bag[index].instance);
    }

    /// <summary>The same slotting, of the piece worn in <paramref name="slot"/>.</summary>
    [Command]
    void CmdSlotWorn(int slot, string expectedItemId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        Slot(c, WornPiece(c, slot, expectedItemId));
    }

    void Slot(Character c, EquipmentInstance inst)
    {
        if (c == null || inst == null || inst.def == null) return;
        if (!AtTheRightStation(inst.def)) { Refused(NotAtStation(inst.def)); return; }
        int cap = CraftingMath.CardSlotCap(inst.def.slot);
        if (inst.cardSlots >= cap)
        { Refused(ItemTooltip.CopyTitle(inst) + " already has every card slot it can (" + cap + ").", "Slots full"); return; }

        var oil = MaterialCatalog.QuenchOil;
        if (oil == null) { Refused("No Quench Oil is authored."); return; }
        int before = inst.cardSlots;
        int units = CraftingMath.SlotMaterialCost(inst.quality, before);
        int gold = CraftingMath.SlotGoldCost(inst.quality, before);
        if (!Spend(c, oil, units, gold)) return;

        bool landed = CombatRng.Range(0, 100) < CraftingMath.SlotChancePct(inst.def.slot, before);
        if (!landed)
        {
            Failed("Slotting failed — " + ItemTooltip.CopyTitle(inst) + " keeps its " + before + " slot(s), and the "
                   + Paid(oil, units, gold) + " is spent.", "Slotting failed — slots kept");
            AfterWorkingOnGear(c, inst);
            return;
        }
        inst.cardSlots = before + 1;
        Done("Slotted " + ItemTooltip.CopyTitle(inst) + " for " + Paid(oil, units, gold) + ".",
             "Slotted — " + inst.cardSlots + "/" + cap + " slots");
        AfterWorkingOnGear(c, inst);
    }

    /// <summary>Quench a piece at its cap: it goes up one grade to +0, or fails — and what a failure does is the Salt's
    /// answer (CraftingMath.QuenchFail). A Salt named by bag position is consumed whatever happens.</summary>
    [Command]
    void CmdQuench(int index, string expectedItemId, int saltIndex)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (!StillTheSameStack(c, index, expectedItemId)) return;
        Quench(c, c.Bag[index].instance, saltIndex);
    }

    /// <summary>The same quench, of the piece worn in <paramref name="slot"/> — it stays on while it is worked.</summary>
    [Command]
    void CmdQuenchWorn(int slot, string expectedItemId, int saltIndex)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        Quench(c, WornPiece(c, slot, expectedItemId), saltIndex);
    }

    void Quench(Character c, EquipmentInstance inst, int saltIndex)
    {
        if (c == null || inst == null || inst.def == null) return;
        if (!AtTheRightStation(inst.def)) { Refused(NotAtStation(inst.def)); return; }
        if (!CraftingMath.CanQuench(inst.quality))
        { Refused(ItemTooltip.CopyTitle(inst) + " is already " + GradeWord(CraftingMath.Best) + " — the best grade there is.",
                  "Already the best grade"); return; }
        // THE FOLD CAP IS THE GATE (V4 §2): the folds are what a quench spends, so a piece that has not been
        // folded to its grade's cap has not done the work the roll costs.
        if (!CraftingMath.ReadyToQuench(inst.quality, inst.upgrade))
        { Refused(ItemTooltip.CopyTitle(inst) + " must be folded to +" + CraftingMath.FoldCap(inst.quality)
                  + " before it can be quenched — it is at +" + inst.upgrade + ".",
                  "Fold to +" + CraftingMath.FoldCap(inst.quality) + " before quenching"); return; }

        // The Salt, if the player chose one: it is spent whatever the quench does.
        var salt = SaltAt(c, saltIndex);
        int saltTier = salt != null ? salt.def.materialStep : 0;
        string saltName = salt != null ? salt.def.displayName : null;

        var oil = MaterialCatalog.QuenchOil;
        if (oil == null) { Refused("No Quench Oil is authored."); return; }
        int units = CraftingMath.QuenchMaterialCost(inst.quality);
        int gold = CraftingMath.QuenchGoldCost(inst.quality);
        if (!Spend(c, oil, units, gold)) return;
        if (salt != null) TakeOne(c, salt);

        var before = inst.quality;
        string was = ItemTooltip.CopyTitle(inst);
        bool rose = CombatRng.Range(0, CraftingMath.QuenchChanceDen) < CraftingMath.QuenchUpChance(before, saltTier);
        if (rose) { inst.quality = (Quality)((int)before + 1); inst.upgrade = 0; }
        else
        {
            CraftingMath.QuenchFail(before, saltTier, out var grade, out int folds);
            inst.quality = grade;
            inst.upgrade = folds;
        }
        // A grade that fell takes the last stat written on the piece with it.
        string lost = null;
        if (inst.quality < before && inst.inkStats != null && inst.inkStats.Count > 0)
            lost = ItemTooltip.InkStatLabel(inst.inkStats[inst.inkStats.Count - 1]);
        if (inst.quality < before) inst.DropLastInk();
        inst.TrimInkToSlots();
        inst.Recompute();

        string paid = " for " + Paid(oil, units, gold) + (saltName != null ? " and 1 " + saltName : "");
        if (rose)
            Done("Quenched " + was + paid + ": it rose to " + ItemTooltip.CopyTitle(inst) + ".",
                 "Quench: up to " + GradeWord(inst.quality));
        else if (inst.quality < before)
            Failed("The quench failed — " + was + " fell to " + ItemTooltip.CopyTitle(inst)
                   + (lost != null ? ", and lost its " + lost + " stat" : "") + ". Spent" + paid.Substring(4) + ".",
                   "Quench failed: down to " + GradeWord(inst.quality));
        else
            Failed("The quench failed — " + ItemTooltip.CopyTitle(inst) + " stays " + GradeWord(inst.quality) + ", at +"
                   + inst.upgrade + ". Spent" + paid.Substring(4) + ".",
                   "Quench failed: stays " + GradeWord(inst.quality));
        AfterWorkingOnGear(c, inst);
    }

    /// <summary>Write one stat onto a piece with an Ink, at the slot the player picked. The stat is rolled here,
    /// from the eighteen, and the Ink is spent whatever it rolls.</summary>
    [Command]
    void CmdInk(int index, string expectedItemId, int inkIndex, int slot)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (!StillTheSameStack(c, index, expectedItemId)) return;
        Ink(c, c.Bag[index].instance, inkIndex, slot);
    }

    /// <summary>The same Ink, on the piece worn in <paramref name="wornSlot"/> — it stays on while it is written.</summary>
    [Command]
    void CmdInkWorn(int wornSlot, string expectedItemId, int inkIndex, int slot)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        Ink(c, WornPiece(c, wornSlot, expectedItemId), inkIndex, slot);
    }

    void Ink(Character c, EquipmentInstance inst, int inkIndex, int slot)
    {
        if (c == null || inst == null || inst.def == null) return;
        if (!AtTheRightStation(inst.def)) { Refused(NotAtStation(inst.def)); return; }
        if (inst.InkSlots <= 0)
        { Refused(ItemTooltip.CopyTitle(inst) + " has no stat slot — a " + GradeWord(inst.quality) + " piece takes no Ink. Quench it first.",
                  "No stat slot — quench it first"); return; }
        if (slot < 0 || slot >= inst.InkSlots)
        { Refused(ItemTooltip.CopyTitle(inst) + " has " + inst.InkSlots + " stat slot(s); there is no slot " + (slot + 1) + ".",
                  "No such stat slot"); return; }
        if (inkIndex < 0 || inkIndex >= c.Bag.Count) { Refused("No Ink at that bag position."); return; }

        var ink = c.Bag[inkIndex];
        if (ink == null || ink.def == null || ink.def.materialKind != MaterialKind.Ink)
        { Refused("That is not an Ink."); return; }

        // Which stat: rolled from what is not already on the piece. A piece carrying all eighteen cannot be inked.
        inkScratch.Clear();
        if (inst.inkStats != null)
            for (int i = 0; i < inst.inkStats.Count; i++)
                if (i != slot) inkScratch.Add(inst.inkStats[i]);
        int weight = CraftingMath.RemainingInkWeight(inkScratch);
        if (weight <= 0) { Refused(ItemTooltip.CopyTitle(inst) + " already carries every stat there is.", "Every stat is already on it"); return; }
        if (!CraftingMath.RollInkStat(CombatRng.Range(0, weight), inkScratch, out var stat)) return;

        string inkName = ink.def.displayName;
        string replaced = inst.inkStats != null && slot < inst.inkStats.Count ? ItemTooltip.InkStatLabel(inst.inkStats[slot]) : null;
        TakeOne(c, ink);
        inst.WriteInk(slot, stat, ink.def.materialStep);
        float value = inst.inkValues[Mathf.Min(slot, inst.inkValues.Count - 1)];
        Done("Wrote " + ItemTooltip.InkStatLine(stat, value)
             + " on " + ItemTooltip.CopyTitle(inst) + " (slot " + (slot + 1) + ") with 1 " + inkName
             + (replaced != null ? ", replacing " + replaced : "") + ".", "Wrote " + ItemTooltip.InkStatLine(stat, value));
        AfterWorkingOnGear(c, inst);
    }

    /// <summary>Recycle a piece: it is destroyed, and half of what it provably consumed comes back — the folds and
    /// the quench of every grade it climbed, the folds standing on it, and the Inks written on it
    /// (<see cref="CraftingMath.RecycleYield"/>). Failed quenches and the folds they spent are not recorded anywhere,
    /// so those come back as nothing.</summary>
    [Command]
    void CmdRecycle(int index, string expectedItemId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (!StillTheSameStack(c, index, expectedItemId)) return;
        Recycle(c, c.Bag[index].instance, index, EquipSlotType.None);
    }

    /// <summary>The same recycle, of the piece worn in <paramref name="wornSlot"/>: it comes off and is destroyed.</summary>
    [Command]
    void CmdRecycleWorn(int wornSlot, string expectedItemId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        var worn = WornPiece(c, wornSlot, expectedItemId);
        if (worn != null) Recycle(c, worn, -1, (EquipSlotType)wornSlot);
    }

    // Entries each bag tab has gained so far in one recycle, indexed by ItemCategory — reused, never rebuilt.
    readonly int[] recycleTabSlots = new int[System.Enum.GetValues(typeof(ItemCategory)).Length];

    // One thing a recycle gives back: does it fit on top of what the same recycle has already given? Refuses the
    // recycle out loud when it does not.
    bool RoomForRecycled(Character c, Item def, int count, ref int addedWeight)
    {
        if (def == null) return true;
        var noRoom = c.RoomFor(def, count, recycleTabSlots[(int)def.category], addedWeight);
        if (noRoom != BagRefusal.None) { Refused(c.RefusalLine(def, noRoom, "get back") + " Nothing was recycled.", "No room for what comes back"); return false; }
        bool held = false;
        for (int i = 0; i < c.Bag.Count; i++)
            if (c.Bag[i] != null && c.Bag[i].def == def && !c.Bag[i].IsEquip) { held = true; break; }
        recycleTabSlots[(int)def.category] += BagRoom.SlotsNeeded(false, count, held);
        addedWeight += def.weight * count;
        return true;
    }

    // The piece is at bag position `index`, or worn in `wornSlot` (index -1).
    void Recycle(Character c, EquipmentInstance inst, int index, EquipSlotType wornSlot)
    {
        if (c == null || inst == null || inst.def == null) return;
        if (!AtTheRightStation(inst.def)) { Refused(NotAtStation(inst.def)); return; }
        var owing = c.Quests.FirstRunOwing(inst.def.id);
        if (owing != null) { Refused(StillNeeded(inst.def, owing), "Still needed for a quest"); return; }

        string was = ItemTooltip.CopyTitle(inst);
        CraftingMath.RecycleYield(inst.quality, inst.upgrade, out int dust, out int oil, out int gold);
        CraftingMath.RecycleInks(inst.inkTiers, recycleTiers);
        var dustItem = MaterialCatalog.TemperDust;
        var oilItem = MaterialCatalog.QuenchOil;

        // ROOM FOR ALL OF WHAT COMES BACK, judged before the piece is destroyed: the piece's own weight and entry are
        // counted off, and each thing coming back is counted on before the next one is judged.
        System.Array.Clear(recycleTabSlots, 0, recycleTabSlots.Length);
        recycleTabSlots[(int)inst.def.category] = -1;
        int addedWeight = -inst.def.weight;
        if (dust > 0 && !RoomForRecycled(c, dustItem, dust, ref addedWeight)) return;
        if (oil > 0 && !RoomForRecycled(c, oilItem, oil, ref addedWeight)) return;
        for (int t = 1; t < recycleTiers.Length; t++)
            if (recycleTiers[t] > 0 && !RoomForRecycled(c, MaterialCatalog.Ink(t), recycleTiers[t], ref addedWeight)) return;

        // THE PIECE GOES FIRST, so what comes back can never stack beside the thing it came out of. A worn piece is
        // taken off first, exactly as taking it off at the bag does — a weapon's skills and bar go with it.
        if (wornSlot != EquipSlotType.None) c.SetEquipped(wornSlot, (EquipmentInstance)null);
        else c.Bag.RemoveAt(index);

        var got = new System.Text.StringBuilder();
        if (dust > 0 && dustItem != null) { c.TakeIntoBag(dustItem, dust); Join(got, dust + " " + dustItem.displayName); }
        if (oil > 0 && oilItem != null) { c.TakeIntoBag(oilItem, oil); Join(got, oil + " " + oilItem.displayName); }
        for (int t = 1; t < recycleTiers.Length; t++)
        {
            if (recycleTiers[t] <= 0) continue;
            var ink = MaterialCatalog.Ink(t);
            if (ink == null) continue;
            c.TakeIntoBag(ink, recycleTiers[t]);
            Join(got, recycleTiers[t] + " " + ink.displayName);
        }
        if (gold > 0) { c.AddGold(gold); Join(got, Wallet.Amount(gold)); }

        // THE BAG BEFORE THE SENTENCE: the window redraws on the sentence, and must redraw the bag as it now is. Taking
        // a weapon off empties the bar, so the bar goes too then (as CmdUnequipToBag sends it).
        PushBagToOwner(wornSlot == EquipSlotType.Weapon);
        Done("Recycled " + was + ": got back " + (got.Length > 0 ? got.ToString() : "nothing") + ".", "Recycled", pieceGone: true);
        SaveSoon();
    }

    // ---- recycling several carried pieces at once ----------------------------------------------------------
    // The Recycle page's ticked pieces, judged together by the SAME rules one recycle uses, and refused together:
    // nothing is half-done. Worn pieces are never in a batch, so a batch never strips what someone is wearing.

    /// <summary>The most pieces one batch may name — more than any bag holds, so it only stops a malformed ask.</summary>
    const int MostRecycledAtOnce = 200;

    // Reused by every batch: the bag positions named, and the totals of what comes back.
    readonly System.Collections.Generic.List<int> batchIndices = new System.Collections.Generic.List<int>(32);
    readonly int[] batchInkTiers = new int[6];
    int batchDust, batchOil, batchGold;

    // Find every named piece in the bag and total what recycling them gives back. Returns the refusal sentence, or
    // null when the batch may go through. Nothing is changed.
    string JudgeRecycleBatch(Character c, string[] copyIds)
    {
        batchIndices.Clear();
        batchDust = batchOil = batchGold = 0;
        System.Array.Clear(batchInkTiers, 0, batchInkTiers.Length);
        if (c == null || copyIds == null || copyIds.Length == 0) return "Pick a piece.";
        if (copyIds.Length > MostRecycledAtOnce) return "Too many pieces at once.";
        for (int i = 0; i < copyIds.Length; i++)
        {
            if (!c.FindCopy(copyIds[i], out int index, out bool worn) || worn || batchIndices.Contains(index))
                return "One of those pieces is no longer in your bag.";
            var inst = c.Bag[index].instance;
            if (inst == null || inst.def == null) return "One of those pieces is no longer in your bag.";
            if (!AtTheRightStation(inst.def)) return NotAtStation(inst.def);
            var owing = c.Quests.FirstRunOwing(inst.def.id);
            if (owing != null) return StillNeeded(inst.def, owing);
            batchIndices.Add(index);
            CraftingMath.AddRecycleYield(inst.quality, inst.upgrade, inst.inkTiers,
                                         ref batchDust, ref batchOil, ref batchGold, batchInkTiers);
        }
        return null;
    }

    /// <summary>What recycling the pieces named by <paramref name="copyIds"/> together would give back, or why it would
    /// be refused. Asked whenever the ticked pieces change.</summary>
    [Command]
    void CmdAskRecycleQuote(int ask, string[] copyIds)
    {
        if (Leaving || connectionToClient == null) return;
        var c = ServerCharacter;
        string refusal = JudgeRecycleBatch(c, copyIds);
        var ids = new System.Collections.Generic.List<string>(8);
        var counts = new System.Collections.Generic.List<int>(8);
        var dustItem = MaterialCatalog.TemperDust;
        var oilItem = MaterialCatalog.QuenchOil;
        if (refusal == null)
        {
            if (batchDust > 0 && dustItem != null) { ids.Add(dustItem.id); counts.Add(batchDust); }
            if (batchOil > 0 && oilItem != null) { ids.Add(oilItem.id); counts.Add(batchOil); }
            for (int t = 1; t < batchInkTiers.Length; t++)
            {
                var ink = batchInkTiers[t] > 0 ? MaterialCatalog.Ink(t) : null;
                if (ink != null) { ids.Add(ink.id); counts.Add(batchInkTiers[t]); }
            }
        }
        TargetRecycleQuote(connectionToClient, new RecycleBatchQuote
        {
            ask = ask, ids = ids.ToArray(), counts = counts.ToArray(), gold = refusal == null ? batchGold : 0,
            pieces = refusal == null ? batchIndices.Count : 0, refusal = refusal ?? "",
        });
    }

    [TargetRpc]
    void TargetRecycleQuote(NetworkConnectionToClient target, RecycleBatchQuote quote) => CraftStationPanel.TakeRecycleQuote(quote);

    /// <summary>Recycle every carried piece named by <paramref name="copyIds"/> at once — all of them, or none.</summary>
    [Command]
    void CmdRecycleMany(string[] copyIds)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        string refusal = JudgeRecycleBatch(c, copyIds);
        if (refusal != null) { Refused(refusal + " Nothing was recycled."); return; }

        var dustItem = MaterialCatalog.TemperDust;
        var oilItem = MaterialCatalog.QuenchOil;

        // ROOM FOR ALL OF WHAT COMES BACK, judged before anything is destroyed: every piece's own weight and entry are
        // counted off first, as one recycle counts off its one.
        System.Array.Clear(recycleTabSlots, 0, recycleTabSlots.Length);
        int addedWeight = 0;
        for (int i = 0; i < batchIndices.Count; i++)
        {
            var def = c.Bag[batchIndices[i]].def;
            recycleTabSlots[(int)def.category] -= 1;
            addedWeight -= def.weight;
        }
        if (batchDust > 0 && !RoomForRecycled(c, dustItem, batchDust, ref addedWeight)) return;
        if (batchOil > 0 && !RoomForRecycled(c, oilItem, batchOil, ref addedWeight)) return;
        for (int t = 1; t < batchInkTiers.Length; t++)
            if (batchInkTiers[t] > 0 && !RoomForRecycled(c, MaterialCatalog.Ink(t), batchInkTiers[t], ref addedWeight)) return;

        // THE PIECES GO FIRST, highest position first so each removal leaves the positions still to come in place.
        int count = batchIndices.Count;
        batchIndices.Sort();
        for (int i = batchIndices.Count - 1; i >= 0; i--) c.Bag.RemoveAt(batchIndices[i]);

        var got = new System.Text.StringBuilder();
        if (batchDust > 0 && dustItem != null) { c.TakeIntoBag(dustItem, batchDust); Join(got, batchDust + " " + dustItem.displayName); }
        if (batchOil > 0 && oilItem != null) { c.TakeIntoBag(oilItem, batchOil); Join(got, batchOil + " " + oilItem.displayName); }
        for (int t = 1; t < batchInkTiers.Length; t++)
        {
            if (batchInkTiers[t] <= 0) continue;
            var ink = MaterialCatalog.Ink(t);
            if (ink == null) continue;
            c.TakeIntoBag(ink, batchInkTiers[t]);
            Join(got, batchInkTiers[t] + " " + ink.displayName);
        }
        if (batchGold > 0) { c.AddGold(batchGold); Join(got, Wallet.Amount(batchGold)); }

        PushBagToOwner();
        Done("Recycled " + count + (count == 1 ? " piece" : " pieces") + ": got back "
             + (got.Length > 0 ? got.ToString() : "nothing") + ".", "Recycled " + count, pieceGone: true);
        SaveSoon();
    }

    // Reused across recycles; a craft is a cold path but there is no reason to allocate in it.
    static readonly int[] recycleTiers = new int[6];

    static void Join(System.Text.StringBuilder sb, string part)
    {
        if (sb.Length > 0) sb.Append(", ");
        sb.Append(part);
    }

    // The Salt at a bag position, or null when that position holds anything else — including when the player
    // chose no Salt at all, which is −1.
    static BagEntry SaltAt(Character c, int index)
    {
        if (c == null || index < 0 || index >= c.Bag.Count) return null;
        var e = c.Bag[index];
        return e != null && e.def != null && e.def.materialKind == MaterialKind.Salt ? e : null;
    }

    // Reused so a craft hands the GC nothing; a craft is a cold path, but there is no reason to allocate in it.
    static readonly System.Collections.Generic.List<InkStat> inkScratch = new System.Collections.Generic.List<InkStat>(6);

    // Spend `units` of `material` and `gold` from this character, or nothing at all. All-or-nothing on purpose:
    // a craft that took the metal and then found the purse short would leave the player worse off for pressing.
    // How many of `material` the bag holds, over every stack of it.
    static int Held(Character c, Item material)
    {
        int held = 0;
        if (c == null || material == null) return 0;
        for (int i = 0; i < c.Bag.Count; i++)
            if (c.Bag[i] != null && c.Bag[i].def == material) held += Mathf.Max(1, c.Bag[i].count);
        return held;
    }

    bool Spend(Character c, Item material, int units, int gold)
    {
        if (units > 0)
        {
            int held = Held(c, material);
            if (held < units)
            { Refused("Needs " + units + " " + material.displayName + " — you have " + held + "."); return false; }
        }
        if (gold > 0 && c.Gold < gold)
        { Refused("Needs " + Wallet.Amount(gold) + " — you have " + Wallet.Amount(c.Gold) + "."); return false; }

        int left = units;
        for (int i = c.Bag.Count - 1; i >= 0 && left > 0; i--)
        {
            var e = c.Bag[i];
            if (e == null || e.def != material) continue;
            int take = Mathf.Min(left, Mathf.Max(1, e.count));
            e.count -= take;
            left -= take;
            if (e.count <= 0) c.Bag.RemoveAt(i);
        }
        if (gold > 0) c.AddGold(-gold);
        return true;
    }

    // "Your Sword is still needed for Your First Gear — finish it first." Why recycling, selling or dropping a piece a
    // tutorial's first run still owes a step on is refused.
    static string StillNeeded(Item def, string questTitle)
        => "Your " + def.displayName + " is still needed for " + questTitle + " — finish it first.";

    // "3 Temper Dust and 150u" — what a craft cost, for the sentence that reports it.
    static string Paid(Item material, int units, int gold)
    {
        string m = units > 0 && material != null ? units + " " + material.displayName : null;
        string g = gold > 0 ? Wallet.Amount(gold) : null;
        return m != null && g != null ? m + " and " + g : m ?? g ?? "nothing";
    }

    /// <summary>A grade as the player reads it: "Raw", or its letter.</summary>
    public static string GradeWord(Quality q) => q == Quality.Raw ? "Raw" : q.ToString();

    // The stats an Ink writes as a percentage — the same four the tooltip draws with a % sign.

    static void TakeOne(Character c, BagEntry e)
    {
        e.count--;
        if (e.count <= 0) c.Bag.Remove(e);
    }

    // Every craft ends the same way: the bag goes back to its owner, the new offer for that piece goes with it,
    // and the character is written down.
    //
    // THE PIECE IS NAMED BY IDENTITY, NOT BY POSITION. Paying for the work empties material stacks out of the bag,
    // so the piece that was at position 6 when the press arrived can be at position 4 by the time it is done —
    // and an offer sent for position 6 would describe whatever moved into it.
    void AfterWorkingOnGear(Character c, EquipmentInstance worked, int saltIndex = -1)
    {
        // A WORN PIECE IS PUT BACK ON, the same piece in the same slot, so everything a change of worn gear updates —
        // the stats, the health cap, the skills a weapon brings, what everybody else sees on the body — sees its new
        // grade and folds. Worked out of the bag, nothing here differs from before.
        var wornSlot = WornSlotOf(c, worked);
        if (wornSlot != EquipSlotType.None) c.SetEquipped(wornSlot, worked);
        // THE BAG GOES FIRST, NOW — not at the end of the frame with the other owed pushes — so the piece's new folds and
        // name reach the window before the offer that describes them. Sent once either way: this clears what was owed.
        PushBagToOwner();
        SendOwedBag();
        if (wornSlot != EquipSlotType.None) SendOffer(c, worked, -1, wornSlot, saltIndex);
        else SendOffer(c, IndexOf(c, worked), saltIndex);
        SaveSoon();
    }

    static EquipSlotType WornSlotOf(Character c, EquipmentInstance inst)
    {
        if (c == null || inst == null) return EquipSlotType.None;
        foreach (var kv in c.Gear.Worn)
            if (ReferenceEquals(kv.Value, inst)) return kv.Key;
        return EquipSlotType.None;
    }

    // Whether the next fold of this copy is one a tutorial's first run gives free — what the offer says it costs.
    static bool FoldIsFree(Character c, EquipmentInstance inst)
    {
        inst.EnsureCopyId();
        return c.Quests.FoldIsFree(inst.def.id, inst.copyId, inst.quality, inst.upgrade);
    }

    static int IndexOf(Character c, EquipmentInstance inst)
    {
        for (int i = 0; i < c.Bag.Count; i++)
            if (c.Bag[i] != null && ReferenceEquals(c.Bag[i].instance, inst)) return i;
        return -1;
    }

    /// <summary>What a piece would cost to work on, and what a quench of it would do with the Salt the player has
    /// chosen. Asked when the window opens, when a different piece is picked, and when the Salt changes.</summary>
    [Command]
    void CmdAskOffer(int index, int saltIndex)
    {
        if (Leaving) return;
        SendOffer(ServerCharacter, index, saltIndex);
    }

    /// <summary>The same, about the piece worn in <paramref name="slot"/>.</summary>
    [Command]
    void CmdAskWornOffer(int slot, string expectedItemId, int saltIndex)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        var worn = WornPiece(c, slot, expectedItemId);
        if (worn != null) SendOffer(c, worn, -1, (EquipSlotType)slot, saltIndex);
    }

    void SendOffer(Character c, int index, int saltIndex = -1)
    {
        if (c == null || index < 0 || index >= c.Bag.Count) return;
        SendOffer(c, c.Bag[index].instance, index, EquipSlotType.None, saltIndex);
    }

    // About ONE piece, wherever it is: at bag position `index`, or worn in `wornSlot` (index -1).
    void SendOffer(Character c, EquipmentInstance inst, int index, EquipSlotType wornSlot, int saltIndex)
    {
        if (c == null || connectionToClient == null) return;
        if (inst == null || inst.def == null) return;

        var dustItem = MaterialCatalog.TemperDust;
        var oilItem = MaterialCatalog.QuenchOil;

        // THE ODDS INCLUDE THE SALT THE PLAYER PICKED, because those are the odds the press will roll. A Salt
        // named by a position that is not one is simply no Salt.
        var salt = SaltAt(c, saltIndex);
        int saltTier = salt != null ? salt.def.materialStep : 0;
        CraftingMath.QuenchFail(inst.quality, saltTier, out var failGrade, out int failFolds);
        int foldChance = CraftingMath.FoldChancePct(inst.quality, inst.upgrade);

        // WHAT RECYCLING WOULD GIVE BACK, as finished item ids and counts — the window only names them.
        CraftingMath.RecycleYield(inst.quality, inst.upgrade, out int recycleDust, out int recycleOil, out int recycleGold);
        CraftingMath.RecycleInks(inst.inkTiers, recycleTiers);
        var ids = new System.Collections.Generic.List<string>();
        var counts = new System.Collections.Generic.List<int>();
        if (recycleDust > 0 && dustItem != null) { ids.Add(dustItem.id); counts.Add(recycleDust); }
        if (recycleOil > 0 && oilItem != null) { ids.Add(oilItem.id); counts.Add(recycleOil); }
        for (int t = 1; t < recycleTiers.Length; t++)
        {
            var ink = recycleTiers[t] > 0 ? MaterialCatalog.Ink(t) : null;
            if (ink != null) { ids.Add(ink.id); counts.Add(recycleTiers[t]); }
        }

        // WHAT THE INK TAB'S INK WRITES, stat by stat: the window lists them and works none of them out.
        int inkAt = CraftStations.StrongestOfKind(c.Bag, MaterialKind.Ink);
        var inkDef = inkAt >= 0 ? c.Bag[inkAt].def : null;
        float[] inkWrites = null;
        if (inkDef != null)
        {
            inkWrites = new float[CraftingMath.All.Length];
            for (int s = 0; s < inkWrites.Length; s++) inkWrites[s] = CraftingMath.InkValue(CraftingMath.All[s], inst.quality, inkDef.materialStep);
        }

        TargetOffer(connectionToClient, new CraftStationOffer
        {
            inkName = inkDef != null ? inkDef.displayName : "",
            inkWrites = inkWrites ?? System.Array.Empty<float>(),
            recycleIds = ids.ToArray(),
            recycleCounts = counts.ToArray(),
            recycleGold = recycleGold,
            bagIndex = index,
            wornSlot = (int)wornSlot,
            quality = (int)inst.quality,
            folds = inst.upgrade,
            foldCap = CraftingMath.FoldCap(inst.quality),
            inkSlots = inst.InkSlots,
            foldMaterialId = dustItem != null ? dustItem.id : "",
            foldMaterialCost = CraftingMath.FoldMaterialCost(inst.quality),
            foldGoldCost = CraftingMath.FoldGoldCost(inst.quality),
            quenchMaterialId = oilItem != null ? oilItem.id : "",
            quenchMaterialCost = CraftingMath.QuenchMaterialCost(inst.quality),
            quenchGoldCost = CraftingMath.QuenchGoldCost(inst.quality),
            foldMaterialHeld = Held(c, dustItem),
            quenchMaterialHeld = Held(c, oilItem),
            foldChance = foldChance,
            foldsIfFoldFails = foldChance >= 100 ? inst.upgrade : CraftingMath.FoldFailFolds(inst.upgrade),
            upChancePercent = CraftingMath.QuenchUpChance(inst.quality, saltTier) * 100f / CraftingMath.QuenchChanceDen,
            qualityIfQuenchFails = (int)failGrade,
            foldsIfQuenchFails = failFolds,
            saltTier = saltTier,
            saltName = salt != null ? salt.def.displayName : "",
            canQuench = CraftingMath.ReadyToQuench(inst.quality, inst.upgrade),
            atTheTop = !CraftingMath.CanQuench(inst.quality),
            canFold = inst.upgrade < CraftingMath.FoldCap(inst.quality),
            foldFree = FoldIsFree(c, inst),
            cardSlots = inst.cardSlots,
            slotCap = CraftingMath.CardSlotCap(inst.def.slot),
            slotChance = CraftingMath.SlotChancePct(inst.def.slot, inst.cardSlots),
            slotMaterialCost = CraftingMath.SlotMaterialCost(inst.quality, inst.cardSlots),
            slotGoldCost = CraftingMath.SlotGoldCost(inst.quality, inst.cardSlots),
            canSlot = inst.cardSlots < CraftingMath.CardSlotCap(inst.def.slot),
            pieceNow = inst.piece,
            pieceNext = inst.upgrade < CraftingMath.FoldCap(inst.quality)
                ? inst.NumbersAt(inst.upgrade + 1, inst.quality)
                : inst.piece,
        });
    }

    [TargetRpc]
    void TargetOffer(NetworkConnectionToClient target, CraftStationOffer offer) => CraftStationPanel.TakeOffer(offer);

    public void AskToFold(int index, string itemId) => CmdFold(index, itemId);
    public void AskToSlot(int index, string itemId) => CmdSlot(index, itemId);
    public void AskToSlotWorn(EquipSlotType slot, string itemId) => CmdSlotWorn((int)slot, itemId);
    public void AskToQuench(int index, string itemId, int saltIndex) => CmdQuench(index, itemId, saltIndex);
    public void AskToInk(int index, string itemId, int inkIndex, int slot) => CmdInk(index, itemId, inkIndex, slot);
    public void AskForOffer(int index, int saltIndex) => CmdAskOffer(index, saltIndex);
    public void AskToFoldWorn(EquipSlotType slot, string itemId) => CmdFoldWorn((int)slot, itemId);
    public void AskToQuenchWorn(EquipSlotType slot, string itemId, int saltIndex) => CmdQuenchWorn((int)slot, itemId, saltIndex);
    public void AskForWornOffer(EquipSlotType slot, string itemId, int saltIndex) => CmdAskWornOffer((int)slot, itemId, saltIndex);
    public void AskToInkWorn(EquipSlotType slot, string itemId, int inkIndex, int inkSlot) => CmdInkWorn((int)slot, itemId, inkIndex, inkSlot);
    public void AskToRecycleWorn(EquipSlotType slot, string itemId) => CmdRecycleWorn((int)slot, itemId);
    public void AskToRecycle(int index, string itemId) => CmdRecycle(index, itemId);
    public void AskToRecycleMany(string[] copyIds) => CmdRecycleMany(copyIds);
    public void AskForRecycleQuote(int ask, string[] copyIds) => CmdAskRecycleQuote(ask, copyIds);

    /// <summary>Swap the two bag positions <paramref name="a"/> and <paramref name="b"/> — a box dropped on another box.
    /// Both are named by position AND item id, so a bag that moved under the asker swaps nothing.</summary>
    [Command]
    void CmdSwapBag(int a, int b, string expectedA, string expectedB)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (a == b || !StillTheSameStack(c, a, expectedA) || !StillTheSameStack(c, b, expectedB)) return;
        (c.Bag[a], c.Bag[b]) = (c.Bag[b], c.Bag[a]);
        PushBagToOwner();
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

    // ---- shops: buying and selling ----------------------------------------------------------------------
    //
    // BOTH HAPPEN HERE, on the server, and nowhere else. The shop window asks; the price of one is the item's own
    // number and the total is worked out here, never on the asker's machine. The asker must be standing at a keeper of
    // that very shop, which is checked here rather than trusted from the window.

    // True when this asker stands within reach of somebody who keeps `shopId`.
    bool AtTheShop(string shopId)
        => TownNPC.NearestShop(transform.position, StationReach + ReachSlack, shopId) != null;

    // ---- the shop: one cart, one trade --------------------------------------------------------------------------
    //
    // THE CART IS THE ONLY DOOR. The shop window keeps a list of intents (buy this, sell that position, buy that back) and
    // sends it whole: first to be quoted as it changes, then once, on Confirm, to be carried out. Both go through the one
    // check (ShopCart.Check), so the quote and the trade can never judge a cart two ways. The trade does all of it or
    // none of it, in this one call, with nothing awaited between the check and the change.

    // The last cart this player's trade carried out, by the window's own count — so a second Confirm sent before the
    // first answer arrived buys nothing twice.
    int lastTradeVersion = -1;

    /// <summary>Price a cart without carrying it out: the four finished numbers, or why it would be refused.</summary>
    [Command]
    void CmdQuoteCart(string shopId, CartLine[] lines, int version)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null || connectionToClient == null) return;
        var shop = WorldShops.Find(shopId);
        ShopCart.Plan plan = null;
        string why = shop == null || !AtTheShop(shopId) ? "You are not at that shop."
                   : ShopCart.Check(c, shop, lines, WorldClock.EpochSecondsNow, out plan);
        if (why != null) plan = null;
        TargetCartQuote(connectionToClient, new CartQuote
        {
            version = version,
            buying = plan != null ? plan.buying : 0,
            selling = plan != null ? plan.selling : 0,
            tax = plan != null ? plan.tax : 0,
            net = plan != null ? plan.net : 0,
            refusal = why ?? "",
        });
    }

    /// <summary>Carry a cart out — everything in it, or nothing.</summary>
    [Command]
    void CmdTrade(string shopId, CartLine[] lines, int version)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null) return;
        if (version == lastTradeVersion) return;
        var shop = WorldShops.Find(shopId);
        if (shop == null || !AtTheShop(shopId)) { ShopSaid("You are not at that shop.", false); TradeAnswered(version, false); return; }

        double now = WorldClock.EpochSecondsNow;
        string why = ShopCart.Check(c, shop, lines, now, out var plan);
        if (why != null)
        {
            // What the cart named may have ended or moved: the window gets the list as it now stands to fix the cart by.
            PushBagToOwner(alsoTheBuyback: true);
            // The reason in full goes to the chat and under the cart; the notice says only that it was turned down.
            ShopSaid(why, false, "Trade refused");
            TradeAnswered(version, false);
            return;
        }
        lastTradeVersion = version;

        // BUY-BACKS OFF THE LIST FIRST, so nothing sold in this same trade can merge into an entry being handed back.
        for (int i = 0; i < plan.backs.Count; i++) c.Buyback.Remove(plan.backs[i]);

        // SALES, highest bag position first, so taking one out never shifts the position the next one names.
        plan.sells.Sort((a, b) => b.bagIndex.CompareTo(a.bagIndex));
        bool emptiedAStack = false;
        for (int i = 0; i < plan.sells.Count; i++)
        {
            var s = plan.sells[i];
            var e = s.entry;
            var piece = e.instance;
            // The piece keeps its own id while it waits to be bought back, so it returns as itself.
            if (piece != null) piece.EnsureCopyId();
            if (s.count >= e.count) { c.Bag.RemoveAt(s.bagIndex); emptiedAStack = true; }
            else e.count -= s.count;
            c.Buyback.Keep(e.def, s.count, piece, s.pay, now, out var letGo);
            GameLog.Info(LogCat.Server, playerName + " sold " + s.count + " " + e.def.id + " to '" + shopId + "' for " + s.pay + " gold");
            if (letGo != null)
                GameLog.Info(LogCat.Server, playerName + "'s oldest kept sale, " + letGo.count + " " + letGo.def.id
                             + (letGo.instance != null ? " (copy " + letGo.CopyId + ")" : "") + ", is gone for good: more than "
                             + ShopMath.BuybackMostEntries + " kept");
        }

        // THE PURSE MOVES ONCE, by the whole of what changes hands.
        c.AddGold(-plan.net);

        bool questsMoved = false;
        for (int i = 0; i < plan.buys.Count; i++)
        {
            var b = plan.buys[i];
            if (b.free > 0) c.Quests.TakeFreeBuys(b.def.id, b.free);
            c.TakeIntoBag(b.def, b.count);
            long cost = ShopMath.BuyCost(b.price, b.count - b.free, b.taxed);
            GameLog.Info(LogCat.Server, playerName + " bought " + b.count + " " + b.def.id + " from '" + shopId + "' for " + cost + " gold"
                         + (b.free > 0 ? " (" + b.free + " free, a tutorial's first run)" : ""));
            // A STEP TO BUY IT counts the purchase; the bag push below judges every step on what they now hold.
            if (c.Quests.CountBought(b.def.id, b.count)) questsMoved = true;
        }
        for (int i = 0; i < plan.backs.Count; i++)
        {
            var k = plan.backs[i];
            // A piece of gear returns as the very copy that was sold.
            if (k.instance != null) c.TakeIntoBag(k.instance);
            else c.TakeIntoBag(k.def, k.count);
            GameLog.Info(LogCat.Server, playerName + " bought back " + k.count + " " + k.def.id
                         + (k.instance != null ? " (copy " + k.CopyId + ")" : "") + " at '" + shopId + "' for "
                         + ShopMath.BuybackPrice(k.paid, k.count) + " gold");
        }
        if (questsMoved) QuestsMoved();

        // The bar too: a stack sold to its last one leaves nothing for its hotbar slot to use.
        PushBagToOwner(alsoTheBar: emptiedAStack, alsoTheBuyback: true);
        SaveSoon();
        ShopSaid(TradeSentence(plan), true, plan.net > 0 ? "Trade done — paid " + Wallet.Amount(plan.net)
                                          : plan.net < 0 ? "Trade done — got " + Wallet.Amount(-plan.net) : "Trade done");
        TradeAnswered(version, true);
    }

    // "Bought 10 Healing Potion, sold 23 Plopling Jelly — you got 6,316u." One sentence for the whole cart.
    static string TradeSentence(ShopCart.Plan plan)
    {
        var sb = new System.Text.StringBuilder(128);
        for (int i = 0; i < plan.buys.Count; i++)
        {
            var b = plan.buys[i];
            sb.Append(sb.Length == 0 ? "Bought " : ", bought ").Append(b.count).Append(' ').Append(b.def.displayName);
            if (b.free > 0) sb.Append(" (").Append(b.free).Append(" free)");
        }
        for (int i = 0; i < plan.sells.Count; i++)
        {
            var e = plan.sells[i].entry;
            sb.Append(sb.Length == 0 ? "Sold " : ", sold ")
              .Append(e.instance != null ? ItemTooltip.CopyTitle(e.instance) : plan.sells[i].count + " " + e.def.displayName);
        }
        for (int i = 0; i < plan.backs.Count; i++)
        {
            var k = plan.backs[i];
            sb.Append(sb.Length == 0 ? "Bought back " : ", bought back ")
              .Append(k.instance != null ? ItemTooltip.CopyTitle(k.instance) : k.count + " " + k.def.displayName);
        }
        if (plan.net > 0) sb.Append(" — you paid ").Append(Wallet.Amount(plan.net)).Append('.');
        else if (plan.net < 0) sb.Append(" — you got ").Append(Wallet.Amount(-plan.net)).Append('.');
        else sb.Append(" — nothing changed hands.");
        return sb.ToString();
    }

    void TradeAnswered(int version, bool done)
    {
        if (connectionToClient != null) TargetTradeAnswered(connectionToClient, version, done);
    }

    [TargetRpc]
    void TargetCartQuote(NetworkConnectionToClient target, CartQuote quote) => ShopPanel.TakeQuote(quote);

    [TargetRpc]
    void TargetTradeAnswered(NetworkConnectionToClient target, int version, bool done) => ShopPanel.TradeAnswered(version, done);

    void ShopSaid(string line, bool ok, string notice = null)
    {
        if (!isServer || connectionToClient == null || string.IsNullOrEmpty(line)) return;
        TargetShopResult(connectionToClient, line, notice ?? line, ok);
    }

    [TargetRpc]
    void TargetShopResult(NetworkConnectionToClient target, string line, string notice, bool ok)
    {
        ChatController.GameSaid(line, ok);
        OverheadNotice.Result(notice, ok);
    }

    public void AskForCartQuote(string shopId, CartLine[] lines, int version) => CmdQuoteCart(shopId, lines, version);
    public void AskToTrade(string shopId, CartLine[] lines, int version) => CmdTrade(shopId, lines, version);

    // ---- the Keeper: save point, storage, teleport ---------------------------------------------------------
    //
    // EVERY SERVICE IS DECIDED HERE. The Keeper window only asks — which service, which town, which bag position and
    // which stored thing — and the asker must be standing at a Keeper, checked here on the server's own positions.
    // Prices are worked out here and sent finished; what storage holds is the character server's table.

    // True when this asker stands within reach of a Keeper.
    bool AtAKeeper() => TownNPC.NearestKeeper(transform.position, StationReach + ReachSlack) != null;

    // What storage holds for this character's account, as this server last read it and kept up to date since.
    readonly StorageList serverStorage = new StorageList();
    float nextStorageReadAt;

    // How many stored rows one message carries: a full storage list goes in several, never in one large message.
    const int StorageChunk = 100;

    // Why a Keeper service is refused before anything else is looked at, or null when it may go ahead.
    string KeeperRefusal(Character c)
    {
        if (c == null) return "";
        if (c.IsDead) return "You cannot do that while dead.";
        if (!AtAKeeper()) return "You are not at a Keeper.";
        return null;
    }

    bool RefusedByKeeper(Character c)
    {
        string why = KeeperRefusal(c);
        if (why == null) return false;
        if (why.Length > 0) KeeperSaid(why, false);
        return true;
    }

    /// <summary>Make where the asker stands, at the Keeper, their save point.</summary>
    [Command]
    void CmdKeeperSavePoint()
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (RefusedByKeeper(c)) return;
        if (string.IsNullOrEmpty(mapName)) { KeeperSaid("You are not standing in a map yet.", false); return; }
        // THE TOWN, NOT A SPOT. Where they stand back up is worked out each time from where this town's Keeper stands
        // (see SavePointTrip), so a town that is re-laid never leaves anybody saved inside a wall.
        c.SetSavePointAtKeeper(mapName);
        SaveSoon();
        GameLog.Info(LogCat.Server, playerName + " set their save point at the Keeper in '" + mapName + "'");
        KeeperSaid("Save point set: " + TownName(mapName) + ".", true);
    }

    /// <summary>Send the asker every town this Keeper can send them to, each with its finished price.</summary>
    [Command]
    void CmdKeeperAskTrips()
    {
        if (Leaving) return;
        if (RefusedByKeeper(ServerCharacter)) return;
        TargetKeeperTrips(connectionToClient, TripsFrom(mapName, ServerCharacter.Level));
    }

    // Every other town with a Keeper that a way of doors reaches, cheapest first, then by name — priced for a
    // traveller of `level`.
    static KeeperTrip[] TripsFrom(string from, int level)
    {
        var atlas = WorldRoutes.Atlas;
        var trips = new System.Collections.Generic.List<KeeperTrip>();
        if (atlas != null)
            foreach (var m in atlas.maps)
            {
                if (m == null || m.scene == from || WorldTownsfolk.KeeperIn(m.scene) == null) continue;
                WorldRoutes.NextMap(from, m.scene, out int doors);
                if (doors <= 0) continue;
                trips.Add(new KeeperTrip { map = m.scene, name = atlas.NameOf(m.scene), price = KeeperMath.TeleportPrice(doors, level) });
            }
        trips.Sort((a, b) => a.price != b.price ? a.price.CompareTo(b.price) : string.CompareOrdinal(a.name, b.name));
        return trips.ToArray();
    }

    static string TownName(string map)
    {
        var atlas = WorldRoutes.Atlas;
        return atlas != null ? atlas.NameOf(map) : map;
    }

    /// <summary>Send the asker to the Keeper of <paramref name="map"/>, for the price of the doors between. The gold is
    /// taken first and handed back if the trip does not happen.</summary>
    [Command]
    void CmdKeeperTeleport(string map)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (RefusedByKeeper(c)) return;
        var keeper = WorldTownsfolk.KeeperIn(map);
        int doors = -1;
        if (keeper != null && map != mapName) WorldRoutes.NextMap(mapName, map, out doors);
        if (doors <= 0) { KeeperSaid("There is no way there.", false); return; }
        long price = KeeperMath.TeleportPrice(doors, c.Level);
        if (price > c.Gold) { KeeperSaid("Needs " + Wallet.Amount(price) + " — you have " + Wallet.Amount(c.Gold) + ".", false); return; }

        c.AddGold(-price);
        // The purse reaches their machine with the bag, so it is sent now: a town this same server holds is entered
        // without a fresh copy of the character.
        PushBagToOwner();
        SaveSoon();
        GameLog.Info(LogCat.Server, playerName + " paid " + price + " gold for a Keeper's trip from '" + mapName + "' to '"
                     + map + "' (" + doors + " doors)");
        KeeperSaid("Off to " + TownName(map) + " for " + Wallet.Amount(price) + ".", true);

        var spot = KeeperMath.ArrivalSpot(keeper.home, keeper.facing, KeeperMath.ArrivalAhead);
        MoveToMap(map, null, true, spot, () =>
        {
            if (this == null) return;
            c.AddGold(price);
            PushBagToOwner();
            SaveSoon();
            GameLog.Warn(LogCat.Server, playerName + "'s Keeper trip to '" + map + "' did not happen — " + price
                         + " gold handed back");
            KeeperSaid("The trip could not be made — your " + Wallet.Amount(price) + " is back.", false,
                       "Trip failed — " + Wallet.Amount(price) + " back");
        });
    }

    /// <summary>Send the asker what their account keeps in storage — read fresh from the character server, at most once
    /// a second; asked more often, the copy already held is sent.</summary>
    [Command]
    void CmdKeeperOpenStorage()
    {
        if (Leaving) return;
        if (RefusedByKeeper(ServerCharacter)) return;
        // ASKED AGAIN WITHIN THE GAP: nothing is sent. Their machine keeps the list it was last sent, and every move since
        // reached it as the one row that changed.
        if (Time.time < nextStorageReadAt) return;
        nextStorageReadAt = Time.time + KeeperMath.StorageReadGap;
        ReadStorage();
    }

    void ReadStorage()
    {
        if (string.IsNullOrEmpty(ServerCharacterId)) return;
        ServerHttp.Send("GET", MapServerLink.CharacterServerUrl + "/server/characters/" + ServerCharacterId + "/storage",
                        null, MapServerLink.ServerSecret, (ok, reply, error, _) =>
        {
            if (this == null) return;
            StorageReply r = null;
            if (ok) { try { r = JsonUtility.FromJson<StorageReply>(reply); } catch { r = null; } }
            if (r == null)
            {
                GameLog.Warn(LogCat.Server, "could not read " + playerName + "'s storage — " + error + " " + reply);
                KeeperSaid("Storage could not be opened just now.", false);
                return;
            }
            serverStorage.Take(r.rows, 0);
            SendStorage();
        });
    }

    [Serializable]
    class StorageReply { public long revision; public StoredLine[] rows; }

    void SendStorage()
    {
        if (connectionToClient == null) return;
        var all = serverStorage.Entries;
        int n = all.Count;
        for (int start = 0; start == 0 || start < n; start += StorageChunk)
        {
            int len = Mathf.Max(0, Mathf.Min(StorageChunk, n - start));
            var lines = new StoredLine[len];
            for (int i = 0; i < len; i++) lines[i] = StorageList.LineOf(all[start + i]);
            TargetStorage(connectionToClient, lines, start, serverStorage.Rows, KeeperMath.StorageLimit);
        }
    }

    /// <summary>SERVER ONLY: a storage move landed — keep this server's copy of the list in step and send the row that
    /// changed. <paramref name="stored"/> is true for something put in, false for something taken out.</summary>
    public void StorageChanged(Item def, int count, EquipmentInstance piece, bool stored)
    {
        if (!serverStorage.Known || def == null) return;
        string copy = piece != null ? piece.copyId ?? "" : "";
        var held = serverStorage.Find(def.id, copy);
        int now = stored ? (piece != null ? 1 : (held != null ? held.count : 0) + count)
                         : (piece != null ? 0 : (held != null ? held.count : 0) - count);
        var line = new StoredLine { itemId = def.id, count = Mathf.Max(0, now), cards = "", roll = "", copyId = "" };
        if (piece != null) piece.ToSaved(out line.refine, out line.cards, out line.roll, out line.copyId);
        if (piece != null && !stored) line.count = 0;
        serverStorage.TakeRow(line);
        if (connectionToClient != null) TargetStorageRow(connectionToClient, line, serverStorage.Rows);
    }

    /// <summary>SERVER ONLY: this server's copy of the storage list may be wrong (a move was refused) — read it again and
    /// send it.</summary>
    public void ForgetStorageCopy()
    {
        serverStorage.Clear();
        nextStorageReadAt = Time.time + KeeperMath.StorageReadGap;
        ReadStorage();
    }

    /// <summary>Put the stack at bag position <paramref name="index"/> into storage — one of it, or all of it. A piece of
    /// gear goes as itself. The thing leaves the bag at once and the save that writes it into storage goes now.</summary>
    [Command]
    void CmdKeeperStore(int index, string expectedItemId, string expectedCopyId, bool all)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (!StillTheSameStack(c, index, expectedItemId)) return;
        if (RefusedByKeeper(c)) return;
        var e = c.Bag[index];
        if (e.instance != null && (e.instance.copyId ?? "") != (expectedCopyId ?? "")) return;
        if (!e.def.CanBeStored) { KeeperSaid(Item.RefusedLine(Item.CannotBeStoredText) + ".", false, Item.RefusedLine(Item.CannotBeStoredText)); return; }
        if (StorageMoves.InFlight(ServerCharacterId)) { KeeperSaid("Your last storage move is still being written down.", false, "Still saving your last move"); return; }
        if (!serverStorage.Known) { KeeperSaid("Storage is not open.", false); return; }
        var piece = e.instance;
        if (piece != null) piece.EnsureCopyId();
        string copy = piece != null ? piece.copyId ?? "" : "";
        if (!KeeperMath.Fits(serverStorage.Rows, KeeperMath.StorageLimit, serverStorage.NeedsNewRow(e.def, copy)))
        { KeeperSaid("Storage is full.", false); return; }

        int count = piece != null ? 1 : (all ? e.count : 1);
        var def = e.def;
        var move = new StorageMoveWire { moveId = Guid.NewGuid().ToString("D"), kind = "store", itemId = def.id, count = count,
                                         limit = KeeperMath.StorageLimit };
        if (piece != null) piece.ToSaved(out move.refine, out move.cards, out move.roll, out move.copyId);
        // OUT OF THE BAG NOW, so nothing can be done with it while the save is in the air; put back if it is refused.
        if (count >= e.count) c.Bag.RemoveAt(index);
        else e.count -= count;
        StorageMoves.Begin(ServerCharacterId, this, move, def, piece);
        GameLog.Info(LogCat.Server, playerName + " is storing " + count + " " + def.id
                     + (copy.Length > 0 ? " (copy " + copy + ")" : "") + " — move " + move.moveId);
        PushBagToOwner(alsoTheBar: true);
        SaveNow(null);
    }

    /// <summary>Take a stored thing — named by its item id and, for gear, the piece's own id — out of storage: one of it,
    /// or all of it. It arrives in the bag once the save that takes it out has landed.</summary>
    [Command]
    void CmdKeeperTake(string itemId, string copyId, bool all)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (RefusedByKeeper(c)) return;
        if (StorageMoves.InFlight(ServerCharacterId)) { KeeperSaid("Your last storage move is still being written down.", false, "Still saving your last move"); return; }
        var held = serverStorage.Known ? serverStorage.Find(itemId, copyId ?? "") : null;
        if (held == null) { KeeperSaid("That is no longer in storage.", false); return; }

        int wanted = held.instance != null ? 1 : (all ? held.count : 1);
        // ROOM FOR IT IN THE BAG, asked before the move starts. "All" of a pile too heavy to carry takes the most that
        // fits; nothing fitting leaves it in storage, and either way they are told.
        int count = c.MostThatFits(held.def, wanted);
        if (count <= 0) { KeeperSaid(c.RefusalLine(held.def, c.RoomFor(held.def, wanted)), false); return; }
        if (count < wanted)
            KeeperSaid("Took " + count + " of " + wanted + " " + held.def.displayName + " — that is all you can carry.", true,
                       "Took " + count + " of " + wanted + " — bag full");
        var move = new StorageMoveWire { moveId = Guid.NewGuid().ToString("D"), kind = "take", itemId = held.def.id,
                                         count = count, limit = KeeperMath.StorageLimit };
        if (held.instance != null) held.instance.ToSaved(out move.refine, out move.cards, out move.roll, out move.copyId);
        StorageMoves.Begin(ServerCharacterId, this, move, held.def, held.instance);
        GameLog.Info(LogCat.Server, playerName + " is taking " + count + " " + held.def.id
                     + (move.copyId.Length > 0 ? " (copy " + move.copyId + ")" : "") + " out of storage — move " + move.moveId);
        SaveNow(null);
    }

    /// <summary>SERVER ONLY: tell this person what a Keeper did, or why it refused — in the window and in the chat.</summary>
    public void KeeperSaid(string line, bool ok, string notice = null)
    {
        if (!isServer || connectionToClient == null || string.IsNullOrEmpty(line)) return;
        TargetKeeperSaid(connectionToClient, line, notice ?? line, ok);
    }

    [TargetRpc]
    void TargetKeeperSaid(NetworkConnectionToClient target, string line, string notice, bool ok)
    {
        ChatController.GameSaid(line, ok);
        OverheadNotice.Result(notice, ok);
        KeeperPanel.ShowResult(line, ok);
    }

    [TargetRpc]
    void TargetKeeperTrips(NetworkConnectionToClient target, KeeperTrip[] trips) => KeeperPanel.TakeTrips(trips);

    [TargetRpc]
    void TargetStorage(NetworkConnectionToClient target, StoredLine[] lines, int startsAt, int rows, int limit)
        => KeeperPanel.TakeStorage(lines, startsAt, rows, limit);

    [TargetRpc]
    void TargetStorageRow(NetworkConnectionToClient target, StoredLine line, int rows) => KeeperPanel.TakeStorageRow(line, rows);

    // ---- the Elite Summoner ------------------------------------------------------------------------------------
    // The window asks for its list; a press names one boss. The server checks reach, the recipe, the spot and the
    // materials, holds the spot BEFORE taking anything, takes the materials only once a body has arrived, and gives
    // the spot back when no body comes — so two presses never make two bosses, and a boss that cannot be made costs nothing.

    [Command]
    void CmdAskSummonOffer()
    {
        if (Leaving) return;
        var c = ServerCharacter;
        var npc = TownNPC.NearestSummoner(transform.position, StationReach + ReachSlack);
        if (c == null || npc == null) return;
        TargetSummonOffer(connectionToClient, SummonLinesFor(c, npc.Summons), SummonedBoss.Taken(npc.GetInstanceID()));
    }

    // The one boss this summoner calls, as an offer line.
    static SummonLine[] SummonLinesFor(Character c, string bossId)
    {
        var recipe = SummonRecipes.Find(bossId);
        if (recipe == null) return new SummonLine[0];
        var sbIds = new System.Text.StringBuilder();
        var sbNeed = new System.Text.StringBuilder();
        var sbHeld = new System.Text.StringBuilder();
        bool canPay = true;
        for (int k = 0; k < recipe.needs.Count; k++)
        {
            var need = recipe.needs[k];
            var item = ItemCatalog.Resolve(need.itemId);
            int held = Held(c, item);
            if (item == null || held < need.count) canPay = false;
            if (k > 0) { sbIds.Append(','); sbNeed.Append(','); sbHeld.Append(','); }
            sbIds.Append(need.itemId); sbNeed.Append(need.count); sbHeld.Append(held);
        }
        var lines = new[]
        {
            new SummonLine
            {
                bossId = recipe.bossId, needIds = sbIds.ToString(), needCounts = sbNeed.ToString(),
                heldCounts = sbHeld.ToString(), canPay = canPay,
            },
        };
        return lines;
    }

    [Command]
    void CmdSummon(string bossId)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null) return;
        var npc = TownNPC.NearestSummoner(transform.position, StationReach + ReachSlack);
        if (npc == null) { SummonSaid("You need to stand at a summoner.", "Not at a summoner", false); return; }
        // a summoner calls their own boss and nobody else
        var recipe = bossId == npc.Summons ? SummonRecipes.Find(bossId) : null;
        var def = recipe != null ? MonsterCatalog.ById(bossId) : null;
        if (def == null) { SummonSaid("Nobody by that name can be summoned.", "Cannot summon that", false); return; }
        int spot = npc.GetInstanceID();
        if (SummonedBoss.Taken(spot)) { SummonSaid("A boss is already out here.", "A boss is already out", false); return; }
        if (!HoldsRecipe(c, recipe)) { SummonSaid("You do not have every material " + def.displayName + " needs.", "Not enough materials", false); return; }
        var map = npc.gameObject.scene;
        if (!WorldTownsfolk.SummonSpot(npc.PersonId, map, out var at))
        { SummonSaid("This Summoner has nowhere to call a boss.", "Nowhere to summon", false); return; }

        SummonedBoss.Hold(spot);
        var me = this;
        MobSpawner.Spawn(def, at, map, go =>
        {
            // NO BODY, NO PRICE: the spot goes back and nothing is taken.
            if (go == null) { SummonedBoss.Free(spot); me.SummonSaid(def.displayName + " could not be called.", "Summon failed", false); return; }
            var payer = me != null ? me.ServerCharacter : null;
            // the materials may have left the bag while the body loaded
            if (payer == null || !HoldsRecipe(payer, recipe))
            {
                SummonedBoss.Free(spot);
                Destroy(go);
                if (me != null) me.SummonSaid("The materials were gone before " + def.displayName + " arrived.", "Not enough materials", false);
                return;
            }
            for (int k = 0; k < recipe.needs.Count; k++)
                me.Spend(payer, ItemCatalog.Resolve(recipe.needs[k].itemId), recipe.needs[k].count, 0);
            var comesBack = go.GetComponent<MobRespawn>();
            if (comesBack != null) Destroy(comesBack);
            var brain = go.GetComponent<MonsterAI3D>();
            if (brain != null) { brain.SetHome(at); brain.tetherRadius = SummonMath.TetherMetres; }
            go.AddComponent<SummonedBoss>().Begin(spot);
            GameNetwork.PublishMob(go, def.id);
            me.PushBagToOwner();
            me.SendOwedBag();
            me.SaveSoon();
            if (GameLog.On(LogCat.Server)) me.LogDid(payer, "summoned " + def.id + " at " + map.name);
            me.SummonSaid(def.displayName + " has been summoned!", "Summoned " + def.displayName, true);
        });
    }

    static bool HoldsRecipe(Character c, SummonRecipes.Recipe recipe)
    {
        if (c == null || recipe == null) return false;
        for (int k = 0; k < recipe.needs.Count; k++)
        {
            var item = ItemCatalog.Resolve(recipe.needs[k].itemId);
            if (item == null || Held(c, item) < recipe.needs[k].count) return false;
        }
        return true;
    }

    void SummonSaid(string line, string notice, bool ok)
    {
        if (connectionToClient == null) return;
        TargetSummonSaid(connectionToClient, line, notice, ok);
        var c = ServerCharacter;
        var npc = TownNPC.NearestSummoner(transform.position, StationReach + ReachSlack);
        if (c != null && npc != null)
            TargetSummonOffer(connectionToClient, SummonLinesFor(c, npc.Summons), SummonedBoss.Taken(npc.GetInstanceID()));
    }

    [TargetRpc]
    void TargetSummonSaid(NetworkConnectionToClient target, string line, string notice, bool ok)
    {
        ChatController.GameSaid(line, ok);
        OverheadNotice.Result(notice, ok);
        SummonerPanel.ShowResult(line, ok);
    }

    [TargetRpc]
    void TargetSummonOffer(NetworkConnectionToClient target, SummonLine[] lines, bool taken) => SummonerPanel.TakeOffer(lines, taken);

    public void AskSummonOffer() => CmdAskSummonOffer();
    public void AskToSummon(string bossId) => CmdSummon(bossId ?? "");

    public void AskKeeperSavePoint() => CmdKeeperSavePoint();
    public void AskKeeperTrips() => CmdKeeperAskTrips();
    public void AskKeeperTeleport(string map) => CmdKeeperTeleport(map ?? "");
    public void AskKeeperStorage() => CmdKeeperOpenStorage();
    public void AskToStore(int index, string itemId, string copyId, bool all) => CmdKeeperStore(index, itemId, copyId ?? "", all);
    public void AskToTake(string itemId, string copyId, bool all) => CmdKeeperTake(itemId, copyId ?? "", all);

    // ---- a Butterfly Wing's trip home ------------------------------------------------------------------------
    // OWED, NOT TAKEN AT ONCE: the wing is spent after its skill fires, and a trip to a map another server holds saves
    // the character the moment it starts — taken inside the skill, that save would still carry the wing.
    bool savePointTripOwed;
    // The item that paid for the owed trip, given back if the trip does not happen; null when nothing paid (the skill
    // was not run by an item).
    Item savePointTripPaidWith;

    /// <summary>SERVER ONLY: send this person to their save point on this shell's next server frame.
    /// <paramref name="paidWith"/> is the item that paid for it, given back if the trip does not happen.</summary>
    public void OweTripToSavePoint(Item paidWith)
    {
        if (!isServer) return;
        savePointTripOwed = true;
        savePointTripPaidWith = paidWith;
    }

    void TakeOwedTrip()
    {
        savePointTripOwed = false;
        var paid = savePointTripPaidWith;
        savePointTripPaidWith = null;
        var c = ServerCharacter;
        if (Leaving || c == null || c.IsDead)
        {
            GiveBackWhatPaidForTrip(paid, c != null && c.IsDead ? "You fell before the wing could carry you."
                                                                : "The wing could not carry you just now.");
            return;
        }
        GameLog.Info(LogCat.Server, playerName + " is going back to their save point"
                     + (paid != null ? " (" + paid.id + ")" : ""));
        SendToSavePoint(() => { if (this != null) GiveBackWhatPaidForTrip(paid, "The wing could not carry you just now."); });
    }

    // The item a trip was paid with goes back into the bag it came out of — the same thing, not a new arrival — and the
    // player is told why.
    void GiveBackWhatPaidForTrip(Item paid, string why)
    {
        var c = ServerCharacter;
        if (paid == null || c == null) return;
        StorageMoves.PutInLiveBag(c, paid, 1, null);
        PushBagToOwner(alsoTheBar: true);
        SaveSoon();
        GameLog.Info(LogCat.Server, playerName + "'s trip did not happen — " + paid.id + " given back");
        TellOwner(why + " Your " + paid.displayName + " is back in your bag.");
    }

    [Command]
    void CmdDropFromBag(int index, string expectedItemId, int howMany)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (!StillTheSameStack(c, index, expectedItemId)) return;
        var e = c.Bag[index];

        // AN UNDROPPABLE ITEM IS NEVER PUT DOWN, refused here and not only greyed out on the window that asked.
        // A screen can be got round; this is the answer that counts.
        if (!e.def.CanBeDropped) return;
        var owing = c.Quests.FirstRunOwing(e.def.id);
        if (owing != null) { TellOwner(StillNeeded(e.def, owing)); return; }

        int take = e.IsEquip ? 1 : Mathf.Clamp(howMany, 1, e.count);
        if (take <= 0) return;

        // AT YOUR FEET, or the nearest clear spot to them — two things in one place read as one thing, and the
        // one underneath cannot be seen or pointed at.
        var where = GroundDrop.FreeSpotNear(MapManager.InWorld(netPosition, mapName), mapName);
        var drop = GroundDrop.Place(where, e.def, take, e.instance, Quality.Raw, 0, mapName, 0);
        if (drop == null) return;
        drop.ThrownBy(netId, MapManager.InWorld(netPosition, mapName));

        // OUT OF THE BAG ONLY ONCE IT IS ON THE FLOOR. Taking it out first and failing to place it would be an
        // item that existed in neither place.
        if (e.IsEquip || take >= e.count) c.Bag.RemoveAt(index);
        else e.count -= take;
        if (GameLog.On(LogCat.Server)) LogDid(c, "put down " + drop.Text);

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
        if (dx * dx + dz * dz > reach * reach)
        {
            LogPickUpRefused(drop, string.Concat("it is ", Mathf.Sqrt(dx * dx + dz * dz).ToString("0.0"),
                                                 " away and the reach is ", reach.ToString("0.0")));
            return;
        }

        // NOT YOURS YET. What falls from a kill is kept for whoever earned it for a few seconds before anybody else may
        // have it (GroundDrop.KillerOnlySeconds).
        if (!drop.MayBeTakenBy(c))
        {
            LogPickUpRefused(drop, "it is still kept for " + drop.reservedForName);
            return;
        }

        // ROOM FOR IT, or it stays where it lies and they are told why (BagRoom).
        var noRoom = c.RoomFor(drop.def, drop.instance != null ? 1 : drop.count);
        if (noRoom != BagRefusal.None) { TellNoRoom(drop.def, noRoom, "pick up"); return; }

        // FIRST COME. The shell is taken away the moment somebody wins it, so a second person asking for the same
        // thing a frame later finds nothing to be given — there is no way for one drop to be handed out twice.
        if (drop.instance != null) c.TakeIntoBag(drop.instance);
        else c.TakeIntoBag(drop.def, drop.count);

        PlaySession.PickedUp(c);
        if (GameLog.On(LogCat.Server))
            LogDid(c, string.Concat("picked up ", drop.Text, " — it had lain ",
                                    Mathf.RoundToInt(Time.time - drop.placedAt).ToString(), "s",
                                    !string.IsNullOrEmpty(drop.reservedForId) && drop.reservedFor != c
                                        ? " and fell for " + drop.reservedForName : ""));

        NetworkServer.Destroy(shell.gameObject);
        PushBagToOwner();
        SaveSoon();
    }

    // A pick-up the server turned down, said once a second at most — a player pressing F over and over at something
    // out of reach must not bury the log, but the first refusal is exactly what a play-test needs to see.
    float nextPickUpRefusalLine;

    void LogPickUpRefused(GroundDrop drop, string why)
    {
        if (!GameLog.On(LogCat.Server) || Time.unscaledTime < nextPickUpRefusalLine) return;
        nextPickUpRefusalLine = Time.unscaledTime + 1f;
        LogDid(ServerCharacter, string.Concat("could not pick up ", drop.Text, " — ", why));
    }

    /// <summary>SERVER: one line about something this player chose to do — "lst_fild01 patches #201 equipped Knife".
    /// Every line about a player's own choices is written through this one shape, under [server], which is on by
    /// default: read in order they are the story of a play-test. Builds a string, so callers put it behind
    /// <see cref="GameLog.On"/>.</summary>
    void LogDid(Character c, string what)
    {
        if (c == null) { GameLog.Info(LogCat.Server, string.Concat(playerName, " ", what)); return; }
        GameLog.Info(LogCat.Server, string.Concat(SpawnIds.MapOf(c), " ", SpawnIds.Label(c), " ", what));
    }

    // ---- what a client asks for, from the bag window -----------------------------------------------------
    // Public so InventoryStore can send them without knowing anything about the wire.

    public void AskToEquip(int index, string itemId) => CmdEquipFromBag(index, itemId);
    public void AskToUnequip(EquipSlotType slot) => CmdUnequipToBag((int)slot);
    public void AskToReorder(int from, int to, string itemId) => CmdReorderBag(from, to, itemId);
    public void AskToSwap(int a, int b, string idA, string idB) => CmdSwapBag(a, b, idA, idB);
    public void AskToDrop(int index, string itemId, int howMany) => CmdDropFromBag(index, itemId, howMany);
    public void AskToPickUp(uint dropNetId) => CmdPickUpDrop(dropNetId);
    public void AskToMerge(int index, string itemId) => CmdMergeMaterial(index, itemId);

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

        // THE SERVER DECIDES WHICH MAP THEY ARE IN — it put them there: the map the character server sent them to on
        // arrival, or the map of a door, a save point or a Keeper's trip it carried out (see ChangeMapOnServer). Their
        // machine only CONFIRMS it. Any other name is refused and said in the log: taking it was a free teleport past
        // every door check, a Keeper's price read from a map they were not in, and a save point set anywhere.
        if (map != mapName)
        {
            GameLog.Warn(LogCat.Server, "" + playerName + " said they are in '" + map + "' but the server has them in '"
                             + mapName + "' — refused.");
            return;
        }

        // THE BODY IS FILED, if it is not in that map yet: a map that was not open here the moment they arrived is open
        // by the time their machine says where it is.
        if (gameObject.scene.name != map) FileBodyInto(map);

        // AND THEIR MACHINE NOW HAS THE MAP it was sent into, so the wait for them to appear where they were sent starts.
        if (arrivalExpected && !arrivalConfirmed)
        {
            arrivalConfirmed = true;
            arrivalDue = Time.time + ArrivalWait;
        }
    }

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
        if (talking) QuestTalkedTo(npcNetId);
        if (GameLog.On(LogCat.Server)) LogDid(ServerCharacter, (talking ? "started talking to " : "stopped talking to ") + NpcName(npcNetId));
    }

    // The townsperson's own name for a log line, or the object's id when it cannot be found.
    static string NpcName(uint npcNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(npcNetId, out var identity) || identity == null) return "townsperson " + npcNetId;
        var shell = identity.GetComponent<NetNpc>();
        var npc = shell != null && shell.ServerNpc != null ? shell.ServerNpc.GetComponent<TownNPC>() : null;
        if (npc == null) return "townsperson " + npcNetId;
        return string.IsNullOrEmpty(npc.PersonId) ? npc.name : npc.PersonId;
    }

    // A CONVERSATION THAT COUNTS FOR A QUEST is one held within reach — measured here, on the server's own positions,
    // the same reach a station or a shop is held to. Who they spoke to is the townsperson's id, never a name.
    void QuestTalkedTo(uint npcNetId)
    {
        if (ServerCombatUnit is not Character c || c == null) return;
        var npc = TownspersonWithinReach(npcNetId);
        if (npc == null || string.IsNullOrEmpty(npc.PersonId)) return;
        if (c.Quests.Advance(c, npc.PersonId)) QuestsMoved();
    }

    // The townsperson a player asked about, on the SERVER, when they are within reach of the asker — or null. The same
    // reach a conversation, a station or a shop is held to.
    TownNPC TownspersonWithinReach(uint npcNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(npcNetId, out var identity) || identity == null) return null;
        var shell = identity.GetComponent<NetNpc>();
        var npc = shell != null && shell.ServerNpc != null ? shell.ServerNpc.GetComponent<TownNPC>() : null;
        if (npc == null) return null;
        var d = npc.transform.position - transform.position;
        d.y = 0f;
        float reach = StationReach + ReachSlack;   // server-math: a helper only [Command] bodies call, run only on the server
        return d.sqrMagnitude <= reach * reach ? npc : null;
    }

    /// <summary>Server-only: something moved this character's quests. Pays every quest that just finished — in this
    /// same frame, so the reward and the finished record go out in the same save — then saves soon and tells them.</summary>
    public void QuestsMoved()
    {
        if (ServerCombatUnit is not Character c || c == null) return;
        if (c.Quests.TakeJustFinished(finishedScratch) > 0)
            foreach (var def in finishedScratch) QuestRewards.Pay(c, def, this);
        SaveSoon();
        // SENT ONCE A FRAME, however many kills counted in it — an area blow that fells seven is one message.
        questsPushOwed = true;
    }

    bool questsPushOwed;

    /// <summary>Server-only: this person's health moved. Only a quest step judged on health cares, and only while one is
    /// the current step of a running quest; then the step is judged, and the list — which shows their health beside the
    /// step — is sent again this frame.</summary>
    public void HealthMoved()
    {
        if (this == null || ServerCombatUnit is not Character c || c == null || !c.Quests.WatchesHealth) return;
        if (c.Quests.Advance(c)) QuestsMoved();
        else questsPushOwed = true;
    }

    readonly System.Collections.Generic.List<QuestDef> finishedScratch = new System.Collections.Generic.List<QuestDef>(2);

    // Asked BY a player: take the quest the townsperson in front of them offers. Only a quest that person offers, only
    // within reach of them, only one this character has never had — anything else is ignored.
    [Command]
    void CmdAcceptQuest(string questId, uint npcNetId)
    {
        if (Leaving || ServerCombatUnit is not Character c || c == null) return;
        var npc = TownspersonWithinReach(npcNetId);
        if (npc == null || string.IsNullOrEmpty(npc.PersonId)) return;
        if (!c.Quests.Accept(questId, npc.PersonId)) return;
        if (GameLog.On(LogCat.Server)) LogDid(c, string.Concat("accepted quest ", questId, " from ", npc.PersonId));
        var def = QuestCatalog.Get(questId);
        string title = def != null ? def.title : questId;
        TellQuestLine(string.Concat("Quest accepted: ", title, "."), string.Concat("Accepted: ", title));
        c.Quests.Advance(c);
        QuestsMoved();
    }

    public void AskToAcceptQuest(string questId, uint npcNetId) { if (isOwned) CmdAcceptQuest(questId, npcNetId); }

    /// <summary>Server-only: a line for this player's chat about their quests — a reward paid, say.</summary>
    public void TellQuestLine(string line, string notice) { if (isServer && connectionToClient != null) TargetQuestLine(connectionToClient, line, notice); }

    // In the chat, and — since handing a quest in is a conversation, with the chat hidden behind it — in the open
    // conversation box too.
    [TargetRpc]
    void TargetQuestLine(NetworkConnection _, string line, string notice)
    {
        ChatController.GameSaid(line, true);
        OverheadNotice.Result(notice, true);
        DialogBox.Interject(line);
    }

    // ---- quests: what the owner is told, and the one thing they may ask ---------------------------------------------

    int questsVersionSent = -1;
    QuestTargetWire questTargetSent;
    int questNeedsSent;

    /// <summary>Server-only: send the owner their open quests and where the followed one's step happens. Sent whole on
    /// arrival and whenever either changed; open quests only — a finished one is never shown again.</summary>
    public void PushQuestsToOwner() => SendQuests(true);

    void SendQuests(bool always)
    {
        if (!isServer || connectionToClient == null) return;
        if (ServerCombatUnit is not Character c || c == null) return;
        var target = c.Quests.Target(c, mapName);
        // What each tutorial still needs moves with the purse and the bag, not with the quests — so it is part of the test.
        int needs = c.Quests.CollectNeeds(c);
        if (!always && c.Quests.Version == questsVersionSent && SameTarget(target, questTargetSent)
            && needs == questNeedsSent) return;
        questsVersionSent = c.Quests.Version;
        questTargetSent = target;
        questNeedsSent = needs;
        // The kills counted since the last list ride IN it — one message a frame however many fell, one line per quest.
        c.Quests.FreeBuys(freeBuyIds, freeBuyCounts);
        TargetQuestsChanged(connectionToClient, c.Quests.Snapshot(c, mapName), target, QuestLog.GuideOf(target),
                            c.Quests.NeedsSnapshot(), c.Quests.Offers(mapName), c.Quests.OngoingAt(mapName),
                            c.Quests.TakeKills(), freeBuyIds.ToArray(), freeBuyCounts.ToArray());
    }

    readonly System.Collections.Generic.List<string> freeBuyIds = new System.Collections.Generic.List<string>(2);
    readonly System.Collections.Generic.List<int> freeBuyCounts = new System.Collections.Generic.List<int>(2);

    static bool SameTarget(QuestTargetWire a, QuestTargetWire b) =>
        a.any == b.any && a.questId == b.questId && a.stepId == b.stepId && a.map == b.map && a.at == b.at
        && a.nextMap == b.nextMap && a.station == b.station && a.block == b.block;

    // Asked BY a player: follow one of their OWN open quests, or — with no id — follow nothing. Anything else — a quest
    // they do not hold, a finished one, the one already followed — is ignored, so a made-up id learns nothing about
    // where a quest it never had happens. The answer goes with the next once-a-frame list, so asking many times in a
    // frame costs one list.
    [Command]
    void CmdTrackQuest(string questId)
    {
        if (Leaving || ServerCombatUnit is not Character c || c == null) return;
        if (c.Quests.Track(questId))
        {
            SaveSoon();
            questsPushOwed = true;
            if (GameLog.On(LogCat.Server))
                LogDid(c, string.IsNullOrEmpty(questId) ? "stopped following any quest" : "started following quest " + questId);
        }
    }

    public void AskToTrackQuest(string questId) { if (isOwned) CmdTrackQuest(questId); }

    /// <summary>Server-only: something a quest list shows may have moved (a new level) — send a fresh one this frame.</summary>
    public void QuestsMayHaveMoved() => questsPushOwed = true;

    // Asked BY a player: PLAY one of their own open tutorials. The server checks what it needs; only a refusal for that
    // reason is said, and pressing Play on the one already playing is a quiet yes.
    [Command]
    void CmdPlayTutorial(string questId)
    {
        if (Leaving || ServerCombatUnit is not Character c || c == null) return;
        var answer = c.Quests.Play(questId, c, out string missingItemId);
        switch (answer)
        {
            case QuestPlay.Started: SaveSoon(); questsPushOwed = true; break;
            case QuestPlay.NotMet: TargetRequirementsNotMet(connectionToClient); break;
            case QuestPlay.MissingPiece:
            {
                var missing = ItemCatalog.Resolve(missingItemId);
                TellOwner("To play this again you need a Raw " + (missing != null ? missing.displayName : missingItemId)
                          + " with room for all of its folds, worn or carried.");
                break;
            }
        }
        if (GameLog.On(LogCat.Server))
            LogDid(c, string.Concat("pressed Play on tutorial ", questId, " — ", answer.ToString()));
    }

    public void AskToPlayTutorial(string questId) { if (isOwned) CmdPlayTutorial(questId); }

    [TargetRpc]
    void TargetRequirementsNotMet(NetworkConnection _) => OverheadNotice.Warn("Requirements are not met");

    [TargetRpc]
    void TargetQuestsChanged(NetworkConnection _, QuestOpenWire[] open, QuestTargetWire target, QuestTargetWire guide,
                             QuestNeedWire[] needs, string[] offers, string[] ongoingAt, QuestKillWire[] kills,
                             string[] freeBuyIds, int[] freeBuyCounts)
    {
        QuestStore.Receive(open, target, guide, needs, offers, ongoingAt, freeBuyIds, freeBuyCounts);
        QuestKillNotice.Show(kills);
    }

    [Command]
    void CmdReportPosition(Vector3 p, float yaw, float aboveGround)
    {
        if (Leaving) return;
        // SENT SOMEWHERE BY THE SERVER, AND NOT THERE YET: nothing counts until a report lands where they were sent —
        // anything else is dropped, and not counted against them (see ExpectArrival).
        if (arrivalExpected) { if (!ArrivedWhereSent(p)) return; }
        // COULD THEY ACTUALLY HAVE GOT THERE. A person's own machine works out where they are standing and then
        // says so, which is the one thing in the world a player is still trusted about — so this is where that
        // trust is checked. A report that could not be true is DROPPED: nothing is written, so the server goes on
        // holding the last place this person could actually have reached, and that is what everybody else is told.
        else if (!MoveWasPossible(p))
        {
            // PUT BACK only once the refusals have piled up. A single one is the game's own doing far more often
            // than it is a lie — and dropping it already cost the liar the move, so there is no hurry.
            //
            // AND ONLY IF THERE IS SOMEWHERE TO PUT THEM. Until one report has been believed, the position the
            // server holds is the empty one every object starts at, and "putting them back" would mean dragging
            // somebody who has never successfully said where they are to the corner of their map.
            if (haveAcceptedAPosition && refusedInARow >= RefusalsBeforeCorrecting)
            {
                // They are being moved by us now, so nothing they say counts until it says they are back there — a
                // report already on its way is not one more lie, and is not the place they now stand either.
                ExpectArrival(netPosition, true);
                TargetReturnTo(connectionToClient, netPosition);
            }
            return;
        }

        netPosition = p;
        netYaw = yaw;
        netSpeed = acceptedGroundSpeed;
        // A PICTURE, KEPT SANE RATHER THAN TRUSTED. It moves nothing the server decides — it lifts a model — so the
        // only thing worth guarding is that a broken or lying machine cannot leave somebody hanging in the sky.
        netAboveGround = Mathf.Clamp(aboveGround, 0f, HighestAnybodyGets);

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

    // The ceiling on the height a person's machine reports for its own body. Far above anything the game produces —
    // a jump clears a fraction of a unit — and here only so that a nonsense number cannot leave somebody floating.
    const float HighestAnybodyGets = 20f;

    /// <summary>Server-only: accept whatever position comes next without question. Called when the server ITSELF
    /// moved this person — walking through a door is a jump the checker must not read as a lie.</summary>
    void ForgiveNextMove()
    {
        lastAcceptedAt = 0f;
        refusedInARow = 0;
    }

    // ---- sent somewhere by the server: where they must appear next ----------------------------------------
    //
    // A TRIP THE SERVER ORDERS — a teleport, a save point, a Keeper's trip, a wing, being put back — LANDS THEM WHERE THE
    // SERVER SAYS, and the server holds them there from that moment. Their machine is told and moves them; until a
    // report arrives within a step of that spot, every report is DROPPED, and none of them counts as a lie: a report
    // sent before the jump was still in the air when it happened, and it says nothing about where they are now. A
    // machine that never arrives there is stood there by the server, ArrivalWait seconds after it has the map loaded.
    // This replaced forgiving whatever came next, which believed a modified machine anywhere after any trip, and spent
    // the forgiveness on a report already in the air, so the real arrival was refused and dragged back.
    //
    // Costs one true/false test per frame per player, and one distance per report while an arrival is expected.

    bool arrivalExpected;
    Vector3 expectedArrival;
    // Their machine has the map they were sent to (said so through CmdReportMap) — only then is it waited for.
    bool arrivalConfirmed;
    float arrivalDue = float.PositiveInfinity;
    bool arrivalNudged;

    // How long a machine that has the map is given to say it stands where it was sent before it is stood there.
    const float ArrivalWait = 2f;

    /// <summary>SERVER: they are being sent to <paramref name="atInMap"/> in their current map — hold them there, and
    /// believe nothing until they report it. <paramref name="mapAlreadyLoaded"/> is false for a trip into a map their
    /// machine still has to load: the wait starts when it says it has.</summary>
    void ExpectArrival(Vector3 atInMap, bool mapAlreadyLoaded)
    {
        arrivalExpected = true;
        expectedArrival = atInMap;
        arrivalConfirmed = mapAlreadyLoaded;
        arrivalDue = mapAlreadyLoaded ? Time.time + ArrivalWait : float.PositiveInfinity;
        arrivalNudged = false;
        refusedInARow = 0;
        netPosition = atInMap;
        // The server put them there, so there is a real place to hold them at — a save writes it, not where they began.
        haveAcceptedAPosition = true;
        acceptedGroundSpeed = 0f;
        transform.position = MapManager.InWorld(atInMap, mapName);
    }

    // A report while an arrival is expected: TRUE (and the wait is over) when it lands within a step of where they were
    // sent. Measured flat, like the movement check — the ground under the spot may sit a little above or below it.
    bool ArrivedWhereSent(Vector3 p)
    {
        if (float.IsNaN(p.x) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.z)) return false;
        if (!KeeperMath.WithinReach(p.x - expectedArrival.x, p.z - expectedArrival.z, StepSlack)) return false;
        arrivalExpected = false;
        arrivalDue = float.PositiveInfinity;
        lastAcceptedAt = Time.time;
        refusedInARow = 0;
        acceptedGroundSpeed = 0f;
        return true;
    }

    // SERVER, once a frame: a machine that has the map and still has not reported the spot is stood on it — and again
    // every ArrivalWait seconds until it says it is there.
    void StandThemWhereSentIfLate()
    {
        if (Time.time < arrivalDue) return;
        arrivalDue = Time.time + ArrivalWait;
        if (!arrivalNudged)
        {
            arrivalNudged = true;
            GameLog.Warn(LogCat.Server, playerName + " did not report arriving where the server sent them in '" + mapName
                         + "' " + Coords.Text(expectedArrival, "0.0") + " within " + ArrivalWait + "s — standing them there");
        }
        if (connectionToClient != null) TargetReturnTo(connectionToClient, expectedArrival);
    }

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
        float top = spec != null ? spec.FullSpeed : 8f;
        var unit = ServerCombatUnit;
        if (unit != null && unit.States != null) top *= Mathf.Max(1f, unit.States.MoveMult);
        if (now < orderedMoveUntil) top = Mathf.Max(top, orderedMoveSpeed);

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
            PutAwayIfSprintingDrawn();
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
        if (watchedMove.IsNew(netMove)) watchedMove.Tell(netMove, MapManager.InWorld(netMove.to, mapName), body.transform);
        WatchedBody.Follow(body.transform, MapManager.InWorld(netPosition, mapName), facing, watchedMove);

        // THE SMOKE AT THE LEVEL CAP, from the level the server sent — the same one every kind of unit wears.
        levelAura.Show(netLevel, body.transform);

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
            bodyVisual.TellPose(netHasDriven, netDriven, netDrivenWindow, netDrivenRelease, netDrivenBeat, netSprinting);
            // …AND THE ONE THING A COPY CANNOT WORK OUT FOR ITSELF: how far off the floor they are, which is what
            // makes a jump actually leave the ground here.
            bodyVisual.TellHeightAboveGround(HeightRightNow());
        }

        ShowTheirHeldStance();
        ShowTheirWindup();
        ShowTheirHealth();
    }

    // WHAT THIS PERSON IS CARRYING, put on the body that shows it. The pair of components below is the same pair a
    // watched creature's body carries, so anything looking at somebody else — a floating plate, a target readout —
    // reads one kind of thing and never has to ask whether it is pointed at a creature or a person.
    Health bodyHealth;
    UnitStatBlock bodyBlock;

    // What was last put on that body. A person at steady health costs nothing, and a body just built is told again
    // from scratch, exactly as a recycled creature body is.
    int lastDrawnHealth = -1;
    int lastDrawnMaxHealth = -1;
    int lastDrawnMana = -1;
    int lastDrawnMaxMana = -1;

    void ShowTheirHealth()
    {
        if (!healthKnown || bodyHealth == null) return;

        if (netHealth != lastDrawnHealth || netMaxHealth != lastDrawnMaxHealth)
        {
            lastDrawnHealth = netHealth;
            lastDrawnMaxHealth = netMaxHealth;
            bodyHealth.SetHealthFromServer(netHealth, netMaxHealth);
        }

        if (bodyBlock != null && (netMana != lastDrawnMana || netMaxMana != lastDrawnMaxMana))
        {
            lastDrawnMana = netMana;
            lastDrawnMaxMana = netMaxMana;
            bodyBlock.SetManaFromServer(netMana, netMaxMana);
        }
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

    /// <summary>The standing pose this person is holding — guarding, casting, sitting — put on their watched body through
    /// the one held-pose door every drawn body shares (<see cref="Agent3DVisual.TellHeld"/>), the same one a creature's
    /// casting pose goes through.</summary>
    void ShowTheirHeldStance()
    {
        if (bodyVisual != null) bodyVisual.TellHeld(netHasHeld, netHeld, netHeldWindow);
    }

    // ---- somebody else's wind-up: their cast bar and the pose they hold while it runs --------------------
    //
    // A wind-up is the one thing a watcher has to draw for ITSELF over time rather than being told each frame:
    // the server says it began, how long it runs and when it ended, and everything in between is this machine's
    // own clock. Both halves are driven exactly as the caster's own machine drives them — the same bar and the
    // same held stance, refreshed every frame — so the two cannot come to look different.

    // Their bar, their ring and their casts' effects — drawn by the same code a creature's shell draws its casts with.
    readonly WatchedCast watchedCast = new WatchedCast();

    readonly LevelCapAura levelAura = new LevelCapAura();

    /// <summary>Start showing this person's wind-up. Called when the server says one began.
    ///
    /// <para>The POSE they hold while it runs is not started here — it travels with every other pose they take, on
    /// the one road that carries them. This is the two things a wind-up has that no other pose does: a bar counting
    /// down over their head, and a ring on the ground where the spell is going to land.</para></summary>
    public void ShowCastWindup(SkillDef def, double startedAt, float seconds, Vector3 aimPointInMap)
    {
        watchedCast.BeginWindup(def, startedAt, seconds, aimPointInMap, body != null ? body.transform : null, mapName);
    }

    /// <summary>Stop showing it — the cast finished, or was broken. Called when the server says so, and whenever
    /// this machine stops being able to draw them at all.</summary>
    public void StopCastWindup()
    {
        watchedCast.StopWindup(body != null ? body.transform : null);
    }

    void ShowTheirWindup() => watchedCast.Tick(body != null ? body.transform : null, mapName);

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
        // The level-cap smoke too — on a watched body, and on this machine's own avatar when this person leaves.
        levelAura.Stop();
        // …and a move being drawn on the body, with any see-through fade that move put on it.
        watchedMove.Stop();
        // STRUCK FROM THE REGISTRY BEFORE THE BODY GOES, or a cursor scan would walk a destroyed one.
        if (bodyHealth != null) WatchedBodies.Remove(bodyHealth);
        if (body != null) byBody.Remove(body);
        bodyHealth = null;
        bodyBlock = null;
        if (body != null) Destroy(body);
        body = null;
        bodyGear = null;
        bodySocket = null;
        bodyDye = null;
        bodyHead = null;
        ReleaseBodyEmblem();
        bodyVisual = null;
        bodyLoading = false;
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

    // WATCHING MACHINES ONLY: the worn set the body this machine built is drawn from, and the pieces read back out
    // of the server's word. One list, reused, so dressing somebody allocates nothing per change.
    UnitEquipment bodyGear;
    static readonly System.Collections.Generic.List<WornList.Entry> wornEntries =
        new System.Collections.Generic.List<WornList.Entry>(8);

    // Put on what the server says this person wears. Only on arrival or change — never per frame.
    void DressTheBody()
    {
        if (bodyGear == null) return;
        WornList.Decode(worn, wornEntries);
        bodyGear.Clear();
        for (int i = 0; i < wornEntries.Count; i++)
        {
            var e = wornEntries[i];
            bodyGear.Equip(e.def.slot, new EquipmentInstance(e.def, e.folds) { quality = e.quality });
        }
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
            if (this == null) return null;                   // player left while their model was loading
            // A GM's /model landed while this was loading. The hook already dropped the body and started loading the
            // new one, so this stale load builds nothing and leaves that load's latch alone.
            if (wanted != modelName) return null;
            if (model == null)
            {
                GameLog.Warn(LogCat.Net, "no character model called '" + wanted + "' — "
                                 + playerName + " will be invisible.");
                return null;
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
            // AND THE COLOURS THEY CHOSE, by the same recolouring their own avatar wears on their machine.
            // perf-ok: once per body built.
            bodyDye = CharacterDye3D.AddShowing(pose.gameObject, look);

            // AND WHAT THEY ARE CARRYING, IN THEIR HAND. The same socket the person's own avatar and every creature
            // wear, reading a worn set this machine is TOLD rather than one it decides — so another person's sword is
            // the sword the server has them holding.
            // perf-ok: once per body built.
            bodyGear = root.AddComponent<UnitEquipment>();
            var socket = pose.gameObject.AddComponent<WeaponSocket>();
            socket.ShowWeaponOf(bodyGear);
            DressTheBody();
            // …and where that weapon is: in their hand or on their body, as the server last said.
            bodySocket = socket;
            socket.ShowDraw(netDraw);

            // AND WHAT IS ON THEIR HEAD: the hair the server worked out for them, and the Head item in that same
            // worn set — the same socket every body wears.
            // perf-ok: once per body built.
            bodyHead = pose.gameObject.AddComponent<HeadSocket>();
            bodyHead.ShowHair(hair);
            bodyHead.ShowHeadgearOf(bodyGear);
            // …and the Garment in that same worn set on their back, with their emblem on it.
            // perf-ok: once per body built.
            var garment = pose.gameObject.AddComponent<GarmentSocket>();
            garment.ShowGarmentOf(bodyGear);
            GiveBodyItsEmblem(garment);
            // …and the Costume Body in that same worn set, swapped in for their plain body.
            // perf-ok: once per body built.
            pose.gameObject.AddComponent<CostumeSocket>().ShowCostumeOf(bodyGear);

            // AND THE NUMBERS, on the body that shows them. Marked as a watched copy first thing: this body does
            // not get to decide it died or how hard it was hit — both arrive from the server like every other fact
            // about this person.
            // perf-ok: once per body built.
            bodyHealth = root.AddComponent<Health>();
            bodyHealth.MarkAsWatchedCopy();

            // That component insists on a sprite renderer, because for a billboard creature the sprite IS the body
            // it flashes. A live-3D character has no billboard, so the renderer it just added is switched off
            // rather than left to draw a white square where the person is standing.
            var flashQuad = root.GetComponent<SpriteRenderer>();
            if (flashQuad != null) flashQuad.enabled = false;

            bodyBlock = root.AddComponent<UnitStatBlock>();

            // Nothing has been drawn on this body yet, so the first word from the server must land whatever it says.
            lastDrawnHealth = -1;
            lastDrawnMaxHealth = -1;
            lastDrawnMana = -1;
            lastDrawnMaxMana = -1;

            WatchedBodies.Add(bodyHealth);
            byBody[root] = this;

            // The body belongs to the WORLD, not to the map this machine is standing in — same reason a creature's
            // does. Travelling unloads the map you left, and everything built into it goes with it.
            MapManager.KeepAcrossMaps(root);

            body = root;
            bodyLoading = false;
            return root;
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
    public void ShowBlow(in DamageInfo info, in UnitMitigation.Outcome mitigated, bool tick = false,
                         TrueDamageCause tickCause = TrueDamageCause.None, int total = 0, uint attackerNetId = 0u,
                         bool dodged = false)
    {
        if (isOwned)
        {
            var me = Character.Instance;
            if (me == null) return;
            me.ShowBlowFromServer(info, tick, tickCause, dodged);

            // WHAT THE BLOW RAN INTO, drawn here because this is the first machine in the chain with a screen. The
            // server decided all of it and suppresses every floating number it makes, and none of these survives in
            // the figure above — a fully braced or parried blow arrives as a plain zero. Over the body, beside the
            // white number, exactly where each of them used to appear.
            if (!mitigated.Nothing)
            {
                var av = me.Body;
                if (av != null) UnitMitigation.Show(mitigated, av);
                if (mitigated.blocked) Sfx.Play(me.blockSfx);   // the clang of the block
            }
            ShowRisingTotal(total, attackerNetId);
            return;
        }

        if (body == null || info.amount <= 0 || info.missed) return;
        // The SAME colour the person it happened to sees over their own body — one owner, read here.
        DamagePopup.Spawn(body.transform, info.amount, Character.ColourOfBlow(info, tick, tickCause));
        ShowRisingTotal(total, attackerNetId);
    }

    /// <summary>Put the RISING YELLOW TOTAL the server summed up over the body this machine draws for this person, when
    /// the blow belongs to one (<paramref name="total"/> above 0) — the exact twin of what a creature's blow does, over
    /// the person's body instead.
    ///
    /// <para>Called from BOTH halves of <see cref="ShowBlow"/> on purpose: the person it happened to and everybody
    /// watching them see the same climbing number, the same way both see the same white one. Keyed by who struck, so
    /// two creatures flurrying one person keep two numbers.</para></summary>
    void ShowRisingTotal(int total, uint attackerNetId)
    {
        if (total <= 0) return;
        var on = DrawnBody;
        if (on != null) RepeatsDamageTotal.ReportArrived(on, total, attackerNetId);
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
        watchedCast.Play(def, on, aimedAt, point, seed);
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
        Mine = this;
        GameNetwork.ConfirmedInTheWorld();
        ShowMyEmblemOnStart();
        // AND THIS WORLD KNOWS NOTHING ABOUT WHAT THIS BODY IS DOING. Poses are reported only when they CHANGE, and
        // a world we have only just arrived in starts out believing this body is doing nothing — so whatever it is
        // holding right now has to be said again, or it would be held on this screen and on no other until the next
        // time it happened to change. The one place that arrival is known is here.
        if (StanceController.Player != null) StanceController.Player.ForgetWhatWasSaid();
    }

    public override void OnStopClient()
    {
        if (Mine == this) Mine = null;
        // THE SAME TIDY-UP A BODY GOING AWAY GETS, because this is a body going away — the person left, or this
        // machine did. Written as the one call so a route out of the world cannot skip a step the other takes.
        DropTheBody();
        base.OnStopClient();
    }
}
