using UnityEngine;

/// <summary>
/// Press-to-talk (F) for ambient townsfolk. Press F near a <see cref="TownNPC"/> and the camera eases to a side-on
/// two-shot of the player + that NPC, both turn to face each other, the HUD hides, and a bottom-screen dialog box
/// (<see cref="DialogBox"/>) opens with a random, time-of-day-appropriate conversation. Page it with the box's
/// NEXT/CLOSE button or the Right-Arrow key; only <b>Esc</b> cancels early (WASD does not). When it closes, the
/// camera eases back and the HUD returns.
///
/// <para>The townsperson you are talking to belongs to the server, so the halt-and-turn is ASKED FOR rather than
/// done here (see <see cref="GameNetwork.TellServerTalkingTo"/>) — everything else on this screen (the camera, the
/// hidden HUD, the box and its lines) is yours alone and stays local.</para>
///
/// <para>This is the 3D press-to-talk system the dormant <see cref="InteractPrompt"/>/<see cref="DialogueUI"/> were
/// left for. It finds NPCs via <see cref="TownNPC"/>'s registry (FindObjectsByType can't see them — DontSaveInEditor).
/// Self-attached by <see cref="Player3DController"/> (idempotent); while a chat is active <see cref="Talking"/> is
/// true and the controller freezes movement.</para>
/// </summary>
[DisallowMultipleComponent]
public class PlayerInteractor : MonoBehaviour
{
    [Tooltip("How close (world units) the player must be to a townsperson to start talking.")]
    public float interactRange = 1.3f;

    /// <summary>True while a conversation is active — <see cref="Player3DController"/> reads this to freeze movement.</summary>
    public static bool Talking { get; private set; }

    Player3DController player;
    TownNPC partner;

    void Awake() { player = GetComponent<Player3DController>(); }

    void Update()
    {
        if (player == null) return;

        if (Talking)
        {
            // Esc cancels (WASD does NOT — the player is frozen). If the partner despawned, close too.
            bool cancelled = Hotkeys.CancelDown();
            if (cancelled || partner == null)
            {
                if (cancelled) Hotkeys.MarkCancelHandled();
                if (atStation) { CraftStationPanel.CloseIt(); UISounds.PlayClose(); EndTalk(); }
                else if (atShop) { ShopPanel.CloseIt(); UISounds.PlayClose(); EndTalk(); }
                else if (atKeeper) { KeeperPanel.CloseIt(); UISounds.PlayClose(); EndTalk(); }
                else if (atSummoner) { SummonerPanel.CloseIt(); UISounds.PlayClose(); EndTalk(); }
                else if (DialogBox.Instance != null) DialogBox.Instance.Close();   // fires onClosed → EndTalk
                else EndTalk();
                return;
            }
            // THE WINDOW'S OWN X closes a station the same way Esc does: the window is gone, so the interaction
            // ends — camera back, townsperson released.
            if (atStation && !CraftStationPanel.IsOpen) { EndTalk(); return; }
            if (atShop && !ShopPanel.IsOpen) { EndTalk(); return; }
            if (atKeeper && !KeeperPanel.IsOpen) { EndTalk(); return; }
            if (atSummoner && !SummonerPanel.IsOpen) { EndTalk(); return; }

            // Keep both turned to each other and the camera framed on the side (they're frozen, but track anyway).
            player.FaceWorldPoint(partner.transform.position);
            FrameCamera();
            return;
        }

        // Not talking: F reaches for whatever is nearest — a thing on the floor, or a townsperson.
        if (Hotkeys.Down(GameKeys.Interact)) ReachForTheNearestThing();
    }

    /// <summary>What the reach key does: pick up the nearest thing lying on the ground, or start a chat with the
    /// nearest townsperson — whichever is actually CLOSER, rather than one of them always winning. A player who
    /// puts something down at a shopkeeper's feet and reaches for it must get the item back, and a player
    /// standing over nothing must still be able to talk.</summary>
    void ReachForTheNearestThing()
    {
        var here = transform.position;
        var drop = GroundDrop.Nearest(here, GroundDrop.Reach);
        var npc = TownNPC.Nearest(here, interactRange);

        if (drop != null && npc != null)
        {
            // Flat and squared, the way every other reach in this game is measured.
            float dropSqr = FlatSqrTo(here, drop.transform.position);
            float npcSqr = FlatSqrTo(here, npc.transform.position);
            if (npcSqr < dropSqr) { StartTalk(npc); return; }
            GroundDrop.AskToTake(drop);
            return;
        }

        if (drop != null) { GroundDrop.AskToTake(drop); return; }
        if (npc != null) StartTalk(npc);
    }

    static float FlatSqrTo(Vector3 from, Vector3 to)
    {
        float dx = to.x - from.x;
        float dz = to.z - from.z;
        return dx * dx + dz * dz;
    }

    /// <summary>Start a conversation with <paramref name="npc"/> — what the reach key does when a townsperson is the
    /// nearest thing. Public for a player who names who to talk to (AgentLink).</summary>
    public void StartTalk(TownNPC npc)
    {
        if (npc == null || Talking) return;

        partner = npc;
        Talking = true;
        // ONE SERVICE PER PERSON: the Keeper opens the Keeper's window; somebody who keeps a shop opens it; otherwise
        // one who works a station opens that.
        atKeeper = npc.Keeper;
        atSummoner = !atKeeper && npc.Summoner;
        atShop = !atKeeper && !atSummoner && !string.IsNullOrEmpty(npc.Shop);
        atStation = !atKeeper && !atSummoner && !atShop && npc.station != CraftStation.None;
        // THE TOWNSPERSON IS THE SERVER'S TO STOP, not this machine's. The body in front of you is a copy of
        // somebody the world owns, so asking here is what makes them halt and turn on EVERY screen — including the
        // screens of the other people standing in the square, who would otherwise watch them stroll away mid-chat.
        GameNetwork.TellServerTalkingTo(npc.gameObject, true);
        player.FaceWorldPoint(npc.transform.position);
        FrameCamera();

        // EVERY INTERACTION GETS THE CLOSE-UP — a conversation and a station alike (the user's rule, 2026-09-21:
        // "any kind of interaction, no exception"). The one difference: a station's window lives on the HUD, so
        // the HUD stays up for it, where a conversation hides it for a clean two-shot.
        if (atStation)
        {
            CraftStationPanel.OpenFor(npc.station);
            return;
        }
        if (atKeeper)
        {
            KeeperPanel.OpenFor(string.IsNullOrEmpty(npc.Description) ? npc.SpeakerName
                                                                      : npc.SpeakerName + " · " + npc.Description);
            return;
        }
        if (atSummoner)
        {
            SummonerPanel.OpenFor(string.IsNullOrEmpty(npc.Description) ? npc.SpeakerName
                                                                         : npc.SpeakerName + " · " + npc.Description);
            return;
        }
        if (atShop)
        {
            ShopPanel.OpenFor(npc.Shop, string.IsNullOrEmpty(npc.Description) ? npc.SpeakerName
                                                                              : npc.SpeakerName + " · " + npc.Description);
            return;
        }
        HideHud(true);
        // A QUEST THIS PERSON OFFERS, which the server says this character can take: their offer, and Accept on its last
        // line. The server decides whether it is accepted; the box only asks.
        var offered = QuestOfferedBy(npc.PersonId);
        if (offered != null)
        {
            var body = npc.gameObject;
            DialogBox.Open(npc.SpeakerName, OfferLines(offered), EndTalk, "Accept",
                           () => GameNetwork.TellServerAcceptQuest(body, offered.id));
            return;
        }
        // The box drives NEXT/CLOSE + Right-Arrow itself; when it closes it calls EndTalk to unwind the cinematic.
        DialogBox.Open(npc.SpeakerName, npc.DialogueLines(), EndTalk);
    }

    // The first quest the server listed as offered by this person and open to this character, or null.
    static QuestDef QuestOfferedBy(string personId)
    {
        if (string.IsNullOrEmpty(personId)) return null;
        foreach (var questId in QuestStore.Offers)
        {
            var def = QuestCatalog.Get(questId);
            if (def != null && def.offeredBy == personId) return def;
        }
        return null;
    }

    // The offer as conversation lines: each paragraph of the quest's offer text is one line; the title when it has none.
    static string[] OfferLines(QuestDef def)
    {
        var text = string.IsNullOrWhiteSpace(def.offerText) ? def.title : def.offerText;
        var parts = text.Split(new[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries);
        var kept = new System.Collections.Generic.List<string>(parts.Length);
        foreach (var p in parts) if (!string.IsNullOrWhiteSpace(p)) kept.Add(p.Trim());
        return kept.Count > 0 ? kept.ToArray() : new[] { string.IsNullOrWhiteSpace(def.title) ? "…" : def.title };
    }

    // True while the interaction in progress is a station, a shop, or the Keeper, rather than a conversation.
    bool atStation;
    bool atShop;
    bool atKeeper;
    bool atSummoner;

    // Invoked as the DialogBox's onClosed (natural CLOSE / past-last-line) AND on Esc/despawn via DialogBox.Close.
    // Idempotent: unwinds the cinematic once.
    void EndTalk()
    {
        if (!Talking) return;
        Talking = false;
        if (partner != null) GameNetwork.TellServerTalkingTo(partner.gameObject, false);
        partner = null;
        atStation = false;
        atShop = false;
        atKeeper = false;
        atSummoner = false;
        if (CameraFollow3D.Main != null) CameraFollow3D.Main.ExitCinematic();
        HideHud(false);
    }

    // Hide/show the whole game HUD (one canvas) + the perf overlay for a clean cinematic.
    void HideHud(bool hide)
    {
        var gameHud = UiKit.HudCanvas;
        if (gameHud != null) gameHud.enabled = !hide;
        // PerfHud compiles out of non-development builds entirely, so the call must go with it — the same
        // condition the class itself carries. Without this the game does not build for release OR for a server.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        PerfHud.SetHidden(hide);
#endif
    }

    // Side-on two-shot: camera perpendicular to the player↔NPC line, on whichever side the camera is already on (so
    // it doesn't swing 180° through the pair), pulled back + raised to frame both. Sized off the NPC's height so it
    // stays right at the world's small character scale.
    void FrameCamera()
    {
        var cam = CameraFollow3D.Main;
        if (cam == null || partner == null) return;

        Vector3 a = transform.position, b = partner.transform.position;
        Vector3 mid = (a + b) * 0.5f;
        Vector3 axis = b - a; axis.y = 0f;
        if (axis.sqrMagnitude < 1e-6f) axis = CameraRig.ForwardXZ;   // overlapping → any axis
        axis.Normalize();

        Vector3 side = Vector3.Cross(Vector3.up, axis);              // horizontal perpendicular
        if (Vector3.Dot(side, cam.transform.position - mid) < 0f) side = -side;   // keep the camera's current side

        float h = partner.HeadHeight; if (h < 1e-3f) h = 0.3f;
        float sep = Vector3.Distance(a, b);
        float dist = Mathf.Max(h * 3.2f, sep * 1.2f + h * 2f);

        Vector3 camPos = mid + side * dist + Vector3.up * (h * 1.1f);
        Vector3 lookAt = mid + Vector3.up * (h * 0.55f);
        cam.EnterCinematic(camPos, lookAt);
    }

    void OnDisable()
    {
        if (Talking)
        {
            if (DialogBox.Instance != null) DialogBox.Instance.Close(); else EndTalk();
        }
    }
}
