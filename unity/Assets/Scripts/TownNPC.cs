using UnityEngine;

/// <summary>
/// Ambient-life brain for a wandering live-3D townsperson (liveliness layers 1 + 3). It is a deliberately
/// DUMB cousin of <see cref="MonsterAI3D"/>: no aggro, no combat — it just strolls to random points near where
/// it spawned, pauses, and occasionally throws a canned "bark" when the player passes. "Lively" here = motion +
/// believable purpose + reactivity, NOT intelligence (RO felt alive with almost no NPC AI). No Claude/LLM here.
///
/// Reuse, not reinvention:
///   • Movement rides the SAME <see cref="PathAgent3D"/> the mobs use (routes around props, crowd-spreads,
///     gravity-grounded); the live-3D <see cref="Character3DPose"/> self-animates Walk/Idle from that motion.
///   • Barks are CHOSEN here and SHOWN by whoever is watching (<see cref="NetNpc"/>), in the pooled white bubble
///     every character speaks through (zero per-bark allocation).
///   • Crowd pass-through reuses <see cref="UnitCrowd"/> so the player walks THROUGH townsfolk (no getting
///     stuck) — registered by the shared build recipe (<see cref="Townsperson"/>), because it is true of a
///     townsperson's body whether or not this machine is the one deciding where it walks.
///
/// <para>ONE TOWN, SO ONE SET OF PEOPLE. This brain only ever runs on the machine that owns the world; every
/// other machine receives the townsperson over the wire (see <see cref="NetNpc"/>) and gets a copy of this
/// component with <see cref="ThinksForItself"/> switched off — kept, rather than left out, because pressing F to
/// talk finds a townsperson through the registry below, and a body with no brain is one nobody can talk to.</para>
///
/// PERFORMANCE (PERFORMANCE.md — hard constraint): simulate-near / freeze-far. Beyond <see cref="simulateRadius"/>
/// from the player the brain stops AND the movement/animation components are disabled, so a town full of NPCs
/// costs almost nothing while you're across the map. The LOD test itself runs on a staggered ~0.4s cadence
/// (random phase per NPC) — never a per-frame distance scan for every townsperson. Nothing here allocates.
/// </summary>
[RequireComponent(typeof(PathAgent3D))]
public class TownNPC : MonoBehaviour
{
    // ---- configured by WorldTownsfolk at spawn (see Init) ----
    Vector3 home;
    float wanderRadius = 6f;
    bool barksEnabled;

    /// <summary>FALSE on the copy a watching machine draws for a townsperson the server owns. Such a body still
    /// stands in the registry below, still answers for its height and its lines when somebody presses F, and still
    /// puts its feet down audibly — it simply stops DECIDING anything, because where it walks, which way it looks
    /// and what it says all arrive from the server. The same word a watched creature's brain uses, for the same
    /// reason and with the same shape.</summary>
    public bool ThinksForItself { get; private set; } = true;

    /// <summary>Who this person is currently turned toward in conversation, or null. Read by the animation driver,
    /// which is the one thing that can turn a body on a machine with no screen.</summary>
    public Transform TalkTarget => talkTarget;

    /// <summary>This person's level, handed over by whatever placed them (see <see cref="WorldTownsfolk.Row.level"/>).
    /// Read by the server to tell every watcher; it changes nothing about how they walk or talk.</summary>
    public int Level { get; private set; } = 1;

    [Header("Craft station")]
    [Tooltip("What this townsperson WORKS at. None = an ordinary villager who only talks. A smith works metal, " +
             "a loom cloth, a tannery leather — pressing F on one opens their station instead of a " +
             "conversation, and a piece may only be worked on at a station of its own craft.")]
    public CraftStation station = CraftStation.None;

    [Header("Wander")]
    [Tooltip("Seconds to linger at a spot before strolling to the next one.")]
    public float pauseMin = 1.5f, pauseMax = 5f;

    [Header("Barks (reactive chatter when the player is near)")]
    [Tooltip("Bark only when the player is within this distance (world units).")]
    public float barkRadius = 4.5f;
    [Tooltip("Cooldown between this NPC's barks once one fires.")]
    public float barkCooldownMin = 10f, barkCooldownMax = 24f;

    [Header("Level of detail")]
    [Tooltip("Beyond this distance from the player the brain + movement + animation freeze (perf). Fed to the " +
             "shared SimulationLod switch, which mobs use too.")]
    public float simulateRadius = 35f;

    [Header("Unstick")]
    [Tooltip("Give up on a stroll target and pick a new one if it isn't reached within this long — frees " +
             "townsfolk that walked into a wall on a blocked/straight-line route.")]
    public float maxWanderSeconds = 5f;

    PathAgent3D agent;
    // The visual is resolved through the IStancePose seam — a townsperson's live-3D model (Character3DPose) with no
    // hardcoded type here. visualBehaviour is that same object typed as a Behaviour (for the LOD enable/disable);
    // visualAnimator + visualDriver are the 3D pieces to freeze when far.
    IStancePose visual;
    Behaviour visualBehaviour;
    Animator visualAnimator;
    Behaviour visualDriver;
    float nextWander, nextBark;
    bool paused;                               // true while lingering at an arrived spot (waiting out the pause before the next stroll)
    float wanderDeadline, nextProgressCheck;   // stuck-detection: time budget + periodic progress sampling
    Vector3 lastProgressPos;
    SimulationLod lod;                         // the SHARED simulate-near / freeze-far switch (mobs use the same one)

    Transform talkTarget;   // set while the player is talking to us (F-interact): hold still + face them, no wander/bark

    // Live registry of townsfolk for the press-to-talk lookup. FindObjectsByType can't be used — townsfolk are
    // spawned with HideFlags.DontSaveInEditor, which that call silently omits — so we keep our own list.
    static readonly System.Collections.Generic.List<TownNPC> Active = new System.Collections.Generic.List<TownNPC>();

    /// <summary>Every townsperson live in the loaded map, read-only.</summary>
    public static System.Collections.Generic.IReadOnlyList<TownNPC> All => Active;

    void Awake()
    {
        agent = GetComponent<PathAgent3D>();
        visual = GetComponentInChildren<IStancePose>(true);
        visualBehaviour = visual as Behaviour;
        visualAnimator = GetComponentInChildren<Animator>(true);   // 3D only; null for a billboard NPC
        visualDriver = GetComponent<TownNPC3DVisual>();            // 3D only; null for a billboard NPC

        // The freeze-far switch. It owns the staggered distance test and disables the mover/animator/visual driver;
        // this brain only asks whether it's simulating, and re-seeds the gait when it thaws.
        lod = GetComponent<SimulationLod>() ?? gameObject.AddComponent<SimulationLod>();
        lod.simulateRadius = simulateRadius;
        lod.Freeze(agent, visualBehaviour, visualAnimator, visualDriver);
        lod.OnSimulatingChanged += OnSimulatingChanged;
    }

    // Re-seed on THAW so re-entering range can't fire a phantom footstep from a position we left long ago; halt on
    // FREEZE so a frozen NPC doesn't resume mid-stride. Same two beats the old inline gate did.
    void OnSimulatingChanged(bool on)
    {
        if (on) (visualDriver as Agent3DVisual)?.ReseatAfterMove();
        else if (agent != null) agent.Halt();
    }

    void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    void OnDisable() { Active.Remove(this); }

    /// <summary>The nearest townsperson to <paramref name="pos"/> within <paramref name="maxDist"/> (world units),
    /// or null. Scans the small live registry (≈ town population) — called on a keypress, never per frame.</summary>
    public static TownNPC Nearest(Vector3 pos, float maxDist)
    {
        TownNPC best = null;
        float bestSq = maxDist * maxDist;
        for (int i = 0; i < Active.Count; i++)
        {
            var n = Active[i];
            if (n == null) continue;
            float d = (n.transform.position - pos).sqrMagnitude;
            if (d <= bestSq) { bestSq = d; best = n; }
        }
        return best;
    }

    /// <summary>The nearest townsperson who works at <paramref name="wanted"/>, or null. Separate from
    /// <see cref="Nearest"/> because a craft asks a different question: not "who is closest" but "is the station I
    /// need within reach" — and an ordinary villager standing closer must not answer it.</summary>
    /// <summary>The nearest keeper of shop <paramref name="shopId"/> within <paramref name="maxDist"/>, or null.</summary>
    public static TownNPC NearestShop(Vector3 pos, float maxDist, string shopId)
    {
        if (string.IsNullOrEmpty(shopId)) return null;
        TownNPC best = null;
        float bestSq = maxDist * maxDist;
        for (int i = 0; i < Active.Count; i++)
        {
            var n = Active[i];
            if (n == null || n.Shop != shopId) continue;
            float d = (n.transform.position - pos).sqrMagnitude;
            if (d <= bestSq) { bestSq = d; best = n; }
        }
        return best;
    }

    /// <summary>The nearest Keeper within <paramref name="maxDist"/>, or null — the reach every Keeper service is held to.</summary>
    public static TownNPC NearestKeeper(Vector3 pos, float maxDist)
    {
        TownNPC best = null;
        float bestSq = maxDist * maxDist;
        for (int i = 0; i < Active.Count; i++)
        {
            var n = Active[i];
            if (n == null || !n.Keeper) continue;
            float d = (n.transform.position - pos).sqrMagnitude;
            if (d <= bestSq) { bestSq = d; best = n; }
        }
        return best;
    }

    public static TownNPC NearestStation(Vector3 pos, float maxDist, CraftStation wanted)
    {
        TownNPC best = null;
        float bestSq = maxDist * maxDist;
        for (int i = 0; i < Active.Count; i++)
        {
            var n = Active[i];
            if (n == null || n.station != wanted) continue;
            float d = (n.transform.position - pos).sqrMagnitude;
            if (d <= bestSq) { bestSq = d; best = n; }
        }
        return best;
    }

    /// <summary>Enter a conversation: stop where we are and turn to face <paramref name="speaker"/> (the player).
    /// While talking we don't wander or bark — <see cref="EndTalk"/> resumes normal ambient life.</summary>
    public void BeginTalk(Transform speaker)
    {
        talkTarget = speaker;
        paused = false;
        if (agent != null) agent.Halt();
    }

    public void EndTalk() => talkTarget = null;

    /// <summary>Turn this into the copy a watching machine draws for a townsperson the server owns: it stops
    /// deciding anything, its mover is switched off, and the freeze switch is told to keep its hands off.
    ///
    /// <para>The brain is left switched ON rather than removed, deliberately — the same asymmetry a watched
    /// creature's has. Switching it off would drop this person out of the registry that pressing F searches,
    /// leaving somebody standing there who could never be spoken to.</para>
    ///
    /// <para>THE FREEZE SWITCH IS SUSPENDED, not merely left alone. It captured this body's mover as it woke and
    /// re-asserts everything it holds each time somebody crosses its radius — so a watched townsperson you walked
    /// away from and came back to would have their mover switched back ON, and a live mover shoves a body about
    /// with crowd separation and gravity, fighting the positions the server is sending.</para></summary>
    public void BecomeAWatchedCopy()
    {
        ThinksForItself = false;
        talkTarget = null;
        if (agent != null) { agent.Halt(); agent.enabled = false; }
        if (lod != null) lod.Suspend();
        // Baselined from where this body actually is. Left at nothing it reads its first frame as one enormous
        // stride and fires a footstep from the world origin — the copy never runs the set-up call that seeds it,
        // because everything that call decides is the server's to decide now.
        (visualDriver as Agent3DVisual)?.ReseatAfterMove();
    }

    /// <summary>Set this townsperson up. Called by <see cref="WorldTownsfolk"/> right after spawn so all the
    /// per-NPC randomization (home, phase offsets) lives in one place.</summary>
    public void Init(Vector3 homePoint, float wander, bool barksOn, int townLevel, string personName, string description,
                     CraftStation workStation, string shopId, bool isKeeper)
    {
        Level = Mathf.Max(1, townLevel);
        DisplayName = personName;
        Description = description;
        station = workStation;
        Shop = shopId ?? "";
        Keeper = isKeeper;
        home = homePoint;
        // 0 = stands at home: a shopkeeper at their door. Anything above is a stroll, never shorter than half a metre.
        Stands = wander <= 0f;
        wanderRadius = Stands ? 0f : Mathf.Max(0.5f, wander);
        StandFacing = transform.forward;
        barksEnabled = barksOn;

        // Desync the herd: random first-step delay + random LOD phase, so a freshly spawned crowd doesn't all
        // step / LOD-check on the same frame. The FIRST bark is allowed almost immediately (small random
        // delay) — otherwise you'd have to loiter for a full 10–24s cooldown before hearing a single line;
        // the long cooldown only governs REPEAT barks once the first has fired.
        nextWander = Time.time + Random.value * pauseMax;
        nextBark = Time.time + 0.5f + Random.value * 2.5f;
        (visualDriver as Agent3DVisual)?.ReseatAfterMove();
    }

    void Update()
    {
        // --- A COPY OF SOMEBODY THE SERVER OWNS. Nothing here is this machine's to decide. Their feet still make a
        // noise, because that comes off the animation the shared body driver is already playing, and it runs for
        // every body it drives whether or not that body thinks for itself. ---
        if (!ThinksForItself) return;

        // --- Frozen because nobody is near (the shared switch owns the distance test) ---
        if (lod != null && !lod.Simulating) return;

        // --- Talking to somebody (F-interact): hold still and skip wander/barks/footsteps. Which way they TURN is
        // the animation driver's answer, because the machine deciding this has no model to turn. ---
        if (talkTarget != null) return;

        // --- Layer 1: wander → pause → wander. Stroll to a random point near home, stand a moment, then pick a new
        // one. The pause is timed from ARRIVAL (not departure): when the agent reports Arrived we start the linger
        // clock once, stand Idle through it, then set off again — so the pause always happens no matter how long the
        // walk took. Same verb the mobs use.
        if (Stands)
        {
            // NOBODY TO STROLL TO. Somebody who stands only walks back to their spot if a crowd shoved them off it.
            if (agent.Arrived && (transform.position - home).sqrMagnitude > 0.25f) WanderTo(home);
        }
        else if (agent.Arrived)
        {
            if (!paused)
            {
                paused = true;
                nextWander = Time.time + Random.Range(pauseMin, pauseMax);   // just arrived → linger this long
            }
            else if (Time.time >= nextWander)
            {
                paused = false;
                WanderTo(home + RandomOffset(wanderRadius));   // pause over → free roam anywhere near home
            }
        }
        else if (IsStuck())
        {
            // Pushing a wall (a blocked goal makes PathAgent3D straight-line into geometry and never "arrive") —
            // bail to open home ground instead of shoving forever.
            paused = false;
            WanderTo(home + RandomOffset(wanderRadius * 0.35f));
        }

        // --- Layer 3: reactive barks when somebody passes by ---
        // WHO IS NEAR is asked of the roll of everybody actually here — the same question this unit's freeze switch
        // and the creatures' hunt already ask. It used to hunt the scene for the ONE avatar at this keyboard, which
        // is a fact about a screen: the machine that owns a town has no avatar of its own, so nobody ever spoke.
        if (barksEnabled && Time.time >= nextBark)
        {
            if (PlayerUnits.AnyoneWithin(transform.position, barkRadius, gameObject.scene.name))
            {
                // time-of-day aware: morning / day / evening / night pool
                string line = NpcBarks.PickTownLine();
                // SAID ONCE, BY THE MACHINE THAT OWNS THE TOWN, so everybody standing there hears the same words at
                // the same moment. The bubble itself is put up by each watcher when the line reaches it.
                GameNetwork.AnnounceNpcSaid(gameObject, line);
                nextBark = Time.time + Random.Range(barkCooldownMin, barkCooldownMax);
            }
            // out of range — re-check soon, don't burn the full cooldown
            else nextBark = Time.time + 1f;
        }

    }


    static Vector3 RandomOffset(float radius)
    {
        Vector2 r = Random.insideUnitCircle * radius;
        return new Vector3(r.x, 0f, r.y);
    }

    /// <summary>The model's rendered world height (feet to crown) — used to size the F-interact camera framing so it
    /// scales with the character instead of a hard-coded distance.</summary>
    public float HeadHeight => visual != null ? visual.VisualWorldHeight : 0.3f;

    /// <summary>A short multi-line, time-of-day-appropriate conversation for the F-interact dialog box.</summary>
    public string[] DialogueLines() => NpcBarks.PickTownConversation();

    /// <summary>Name shown in the dialog box's speaker slot.</summary>
    public string SpeakerName => string.IsNullOrEmpty(DisplayName) ? "Villager" : DisplayName;

    /// <summary>This person's name from the townsfolk list (<see cref="WorldTownsfolk"/>) — set by whatever placed them
    /// on the server, and by the shell that speaks for them on a watching machine. Empty for somebody unnamed.</summary>
    public string DisplayName { get; set; }

    /// <summary>This person's stable id in the townsfolk list (<see cref="WorldTownsfolk.Row.id"/>) — what a quest step
    /// "talk to …" and a quest's giver name. Set on the server when they are placed, and copied onto every watching
    /// machine's copy (<see cref="NetNpc.personId"/>). Empty for anybody not placed from that list.</summary>
    public string PersonId { get; set; } = "";

    /// <summary>The hair on their head, "id|#RRGGBB" (<see cref="HeadPieces.Pack"/>), and the Head item they wear by
    /// its item id — set from the townsfolk list on the server and handed to every watching machine's copy.</summary>
    public string Hair { get; set; } = "";
    public string Headgear { get; set; } = "";

    /// <summary>What this person is, in a few words, shown under their name (<see cref="WorldTownsfolk.Row.description"/>).
    /// Set by whatever placed them on the server, and by the shell that speaks for them on a watching machine; empty for
    /// somebody with none.</summary>
    public string Description { get; set; }

    /// <summary>The shop this person keeps, by its id in <see cref="WorldShops"/>, or empty. Pressing F on them opens
    /// it. Set by whatever placed them on the server, and by the shell that speaks for them on a watching machine.</summary>
    public string Shop { get; set; } = "";

    /// <summary>True for the town's Keeper (<see cref="WorldTownsfolk.Row.keeper"/>): pressing F on them opens the Keeper
    /// window. Set by whatever placed them on the server, and by the shell that speaks for them on a watching machine.</summary>
    public bool Keeper { get; set; }

    /// <summary>True for somebody who stands at their home and never strolls — a shopkeeper at their door.</summary>
    public bool Stands { get; private set; }

    /// <summary>Which way somebody who stands looks while nobody is talking to them: the way they were placed.</summary>
    public Vector3 StandFacing { get; private set; }

    // Issue a new stroll target and (re)arm the stuck watchdog: a time budget to reach it + a progress baseline.
    void WanderTo(Vector3 target)
    {
        agent.MoveTo(target);
        // NOTE: the linger clock (nextWander) is armed on ARRIVAL, not here — see the wander block in Update.
        wanderDeadline = Time.time + maxWanderSeconds;
        lastProgressPos = transform.position;
        nextProgressCheck = Time.time + 0.7f;
    }

    // "Stuck" = blew the time budget, or sampled almost no forward progress since the last check (~0.7s) despite
    // being on the move — i.e. grinding against a wall. Cheap: no raycasts, just a periodic distance sample.
    bool IsStuck()
    {
        if (Time.time >= wanderDeadline) return true;
        if (Time.time < nextProgressCheck) return false;
        nextProgressCheck = Time.time + 0.7f;
        float moved2 = (transform.position - lastProgressPos).sqrMagnitude;
        lastProgressPos = transform.position;
        return moved2 < 0.18f * 0.18f;   // <0.18u in 0.7s while "walking" → not actually getting anywhere
    }

}
