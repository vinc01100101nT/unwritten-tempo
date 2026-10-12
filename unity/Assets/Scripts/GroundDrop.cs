using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ONE THING LYING ON THE GROUND. You see it as a SPARKLE at the spot, and you
/// read WHAT it is by pointing at the sparkle — which puts its name on a small plate above it and turns the cursor
/// into a hand — or by holding <b>Alt</b>, which puts up every name at once. Clicking the sparkle or the name picks
/// it up. There is still no icon and no model: the name is the only description of the item anywhere in this game,
/// on the floor exactly as in the bag.
///
/// <para><b>Alt is already this game's "tell me more" key</b> — the item tooltip opens a weapon's skill details on
/// it, and dashing is deliberately suppressed while it is held — so a floor that answers to it is the key doing
/// the job it already does, not a third meaning for one button.</para>
///
/// <para><b>It is not the thing that crosses the wire.</b> In a world the server owns the real drop and
/// <see cref="NetGroundDrop"/> announces it; every watching machine builds one of these to READ, carrying no
/// <see cref="EquipmentInstance"/> at all — a client is never handed the particular sword, only what to write
/// on the floor, because handing over the real copy would be handing over an item to invent from. With no world
/// (the game opened straight into a map to try something out) there is nobody to ask, so the one made here IS
/// the real drop and does carry the copy.</para>
///
/// <para><b>Nothing here is ever saved.</b> The objects are tagged not-to-serialize like every other
/// runtime-only object in this project, and no drop is written to the database: a thing on the floor lives as
/// long as the process holding that map does.</para>
///
/// <para>PERFORMANCE: every label in the world lives under ONE shared canvas — never one canvas each — and ONE
/// ticker on that canvas puts them all on the screen over their drops, reading the camera once per frame rather
/// than once per drop. The canvas is drawn flat on the screen through <see cref="OverheadScreenLayer"/>, so a
/// name stays the same size at every zoom.</para>
/// </summary>
[DisallowMultipleComponent]
public class GroundDrop : MonoBehaviour
{
    /// <summary>What it is.</summary>
    public Item def;

    /// <summary>How many. Always 1 for anything wearable, exactly as a bag stack is.</summary>
    public int count = 1;

    /// <summary>The particular copy, with its folds, grade, written stats and cards. THE SERVER'S ONLY — null on
    /// every watching machine.</summary>
    public EquipmentInstance instance;

    /// <summary>The grade in front of the name, and what a watcher is told instead of the copy above.</summary>
    public Quality quality = Quality.Raw;

    /// <summary>The +N in front of the name. Sent as its own number for the same reason the grade is.</summary>
    public int upgrade;

    /// <summary>How rare it is (<see cref="Rarities.Of"/>), which colours its name. Worked out where the copy is and
    /// sent like the grade, because a weapon's rarity is its trademark's and a watcher never holds the copy.</summary>
    public Rarity rarity = Rarity.Common;

    /// <summary>Which map it lies in. A server holds several at once and keeps them apart, so a drop that never
    /// said which one it was in would be reachable from all of them at once.</summary>
    public string mapName = "";

    /// <summary>The shell that announced it, so reaching for it can name it to the server. 0 with no world.</summary>
    public uint netId;

    /// <summary>SERVER ONLY: who may pick this up before anybody else — whoever earned the kill it fell from. Null for
    /// anything a player put down, which anybody may take at once.</summary>
    public Character reservedFor;

    /// <summary>SERVER ONLY: the moment, on the server's clock (<c>Time.time</c>), that anybody may take it.</summary>
    public float reservedUntil;

    /// <summary>SERVER ONLY: the moment, on the server's clock, it vanishes if nobody has taken it. 0 = it never does,
    /// which is everything a player put down.</summary>
    public float expiresAt;

    /// <summary>SERVER ONLY: the character id and the log name of whoever it was kept for, copied when it was kept for
    /// them — read by the line written if it vanishes untaken. Empty for anything a player put down.</summary>
    public string reservedForId = "", reservedForName = "";

    /// <summary>When it was put down, on this machine's clock (<c>Time.time</c>) — so a pick-up's log line can say how
    /// long it lay there first.</summary>
    public float placedAt;

    /// <summary>How long a kill's drops are kept for whoever earned the kill, in seconds.</summary>
    public const float KillerOnlySeconds = 10f;

    /// <summary>How long a kill's drops lie on the ground before they vanish, in seconds. Without an end, every creature
    /// killed and never looted would leave something the server holds and tells people about for as long as it runs.</summary>
    public const float KillDropLifetimeSeconds = 60f;

    /// <summary>SERVER: keep this for <paramref name="killer"/> for <paramref name="killerOnlySeconds"/>, and let it
    /// vanish after <paramref name="lifetimeSeconds"/>.</summary>
    public void ReserveFor(Character killer, float killerOnlySeconds, float lifetimeSeconds)
    {
        reservedFor = killer;
        // TAKEN NOW, while the killer is certainly here: the log line written when this vanishes untaken names
        // them, and by then they may have left and their body be gone.
        reservedForId = killer != null ? GameNetwork.CharacterIdOf(killer) : "";
        reservedForName = killer != null && GameLog.On(LogCat.Server) ? SpawnIds.Label(killer) : "";
        reservedUntil = Time.time + killerOnlySeconds;
        expiresAt = Time.time + lifetimeSeconds;
    }

    /// <summary>SERVER: whether <paramref name="who"/> may pick this up now. A killer who has since left frees it at once.</summary>
    public bool MayBeTakenBy(Character who) => reservedFor == null || Time.time >= reservedUntil || reservedFor == who;

    /// <summary>SERVER: true once a thing that has an end has reached it.</summary>
    public bool Expired => expiresAt > 0f && Time.time >= expiresAt;

    // Every drop on THIS machine. A registry rather than a scene scan, because the pick-up key asks it and a
    // long session can leave a few hundred of these about — the same reason the creatures keep one.
    static readonly List<GroundDrop> all = new List<GroundDrop>(32);

    /// <summary>How close you must be to pick something up, in world units. ONE number, read by the key that
    /// reaches for it and by the server that decides whether the reach was honest — two numbers would be two
    /// answers to "can I reach that", and the day they drifted the game would refuse a pick-up it had just
    /// offered.</summary>
    public const float Reach = 1.4f;

    /// <summary>Every drop this machine knows of, in no order. Read-only; <see cref="Readable"/> says which of them are
    /// in the map the player is standing in.</summary>
    public static IReadOnlyList<GroundDrop> All => all;

    /// <summary>True when this drop lies in the player's current map and can be reached for.</summary>
    public bool Readable => readable;

    /// <summary>The drop nearest <paramref name="worldPos"/> within <paramref name="range"/> world units, or null.
    /// Measured flat and squared: flat because a drop at the foot of the step you are standing on is still at
    /// your feet as far as reaching goes, squared because comparing distances never needs the square root.</summary>
    public static GroundDrop Nearest(Vector3 worldPos, float range)
    {
        GroundDrop best = null;
        float bestSqr = range * range;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var d = all[i];
            if (d == null) { all.RemoveAt(i); continue; }
            if (!d.readable) continue;
            float dx = d.transform.position.x - worldPos.x;
            float dz = d.transform.position.z - worldPos.z;
            float sqr = dx * dx + dz * dz;
            if (sqr > bestSqr) continue;
            bestSqr = sqr;
            best = d;
        }
        return best;
    }

    // ---- WHERE a new thing lands -------------------------------------------------------------------------
    //
    // Two drops in the same spot read as one thing, and one of them cannot be seen or aimed at. This is asked
    // ONLY where a drop is DECIDED — the server carrying out a request, or a game with no world doing it itself.
    // A watching machine never asks: it is TOLD where the thing is and must put it exactly there, or the floor
    // would look different on every screen.

    /// <summary>How far apart two things on the floor are kept, in world units. Derived from what a drop actually
    /// paints: its bright core is about 0.2 across and its streak about 0.3, so this clears both with a little
    /// air. (Its faint ground wash is 0.6 wide and two of those may still touch — raise this to 0.6 if even that
    /// should never overlap, at the cost of far fewer things fitting inside the circle below.)</summary>
    public const float Spacing = 0.35f;

    /// <summary>How far from whoever dropped it a thing may be pushed, in world units. A HARD boundary: the
    /// scatter may never put an item outside this circle, however crowded the floor is, so what you put down
    /// always stays where you put it and never trails away across the field.</summary>
    public const float MaxScatterRadius = 2f;

    // Rings are spread evenly out to the boundary, so the outermost one sits exactly ON it and the whole
    // allowance is used. Their spacing works out at or above Spacing, which is what makes most candidates land
    // clear on the first try — but it is NothingAt below that actually decides, never this layout.
    static readonly int RingCount = Mathf.Max(1, Mathf.FloorToInt(MaxScatterRadius / Spacing));

    /// <summary>The nearest spot to <paramref name="wanted"/> that no other drop is sitting on, seated on the
    /// ground. <paramref name="wanted"/> is wherever the thing came from — a player's feet, and a creature's when
    /// creatures start leaving things behind. Takes that spot itself when the floor there is clear, which is the
    /// ordinary case; otherwise steps outward ring by ring and takes the first clear spot it finds.
    ///
    /// <para><b>Keeping things apart is a SOFT rule; the circle is a HARD one.</b> When every spot inside
    /// <see cref="MaxScatterRadius"/> is taken, the thing goes down on top of something else rather than being
    /// pushed out into the field or refused — a crowded floor is allowed to look crowded, but an item must never
    /// end up somewhere its owner would not think to look.</para>
    ///
    /// <para>Deliberately a SEARCH rather than a random nudge. A random offset inside a circle — which is how
    /// this game's old loot scattered — makes an overlap unlikely rather than impossible, and "unlikely" happens
    /// constantly when somebody empties a bag.</para></summary>
    public static Vector3 FreeSpotNear(Vector3 wanted, string mapName)
    {
        if (NothingAt(wanted, mapName)) return Seated(wanted);

        for (int ring = 1; ring <= RingCount; ring++)
        {
            float radius = MaxScatterRadius * ring / RingCount;
            // Enough points that neighbours around the ring are themselves about one spacing apart.
            int points = Mathf.Max(6, Mathf.RoundToInt(2f * Mathf.PI * radius / Spacing));
            // Each ring is turned a little, so the rings do not line up into spokes with gaps between them.
            float turn = ring * 0.7f;
            for (int i = 0; i < points; i++)
            {
                float angle = turn + i * (2f * Mathf.PI / points);
                var at = new Vector3(wanted.x + Mathf.Cos(angle) * radius, wanted.y,
                                     wanted.z + Mathf.Sin(angle) * radius);
                if (NothingAt(at, mapName)) return Seated(at);
            }
        }

        // NO ROOM LEFT INSIDE THE CIRCLE. It goes where it was dropped, on top of whatever is already there. That
        // is the deliberate answer: overlapping is a cosmetic problem, and a thing flung out of sight is not.
        return Seated(wanted);
    }

    static bool NothingAt(Vector3 at, string mapName)
    {
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var d = all[i];
            if (d == null) { all.RemoveAt(i); continue; }
            if (d.mapName != mapName) continue;
            float dx = d.transform.position.x - at.x;
            float dz = d.transform.position.z - at.z;
            if (dx * dx + dz * dz < Spacing * Spacing) return false;
        }
        return true;
    }

    // ON the floor, not hovering over it or buried in it. A spot found by stepping sideways keeps the height it
    // started at, which is the height of whoever dropped it — and on a slope that is the wrong height by the time
    // you have moved a stride away. No ground under it (off the edge of the world) leaves the height alone.
    static Vector3 Seated(Vector3 at)
    {
        if (GroundSnap.TryGroundY(at, GroundSnap.GroundMask, out float y)) at.y = y;
        return at;
    }

    /// <summary>Put a thing on the ground at <paramref name="worldPos"/> and hand it back. A server passes the
    /// copy; a watching machine passes null and gives the grade, the folds and the <paramref name="rarity"/> it was
    /// sent instead. With no rarity given it is read off the item and the copy. Nothing is drawn here: a watching
    /// machine puts its plate and sparkle up with <see cref="ShowOnScreen"/>, and the server never does.</summary>
    public static GroundDrop Place(Vector3 worldPos, Item def, int count, EquipmentInstance instance,
                                   Quality quality, int upgrade, string mapName, uint netId, Rarity? rarity = null)
    {
        if (def == null || count <= 0) return null;

        var go = new GameObject("Drop_" + def.id) { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.position = worldPos;

        // IN THE MAP IT WAS DROPPED IN, not in whichever map this machine loaded most recently — which is where
        // anything built while the game runs would otherwise land. A server holds several maps at once and keeps
        // them apart, so a drop filed under the wrong one is one nobody standing over it can see.
        if (!string.IsNullOrEmpty(mapName))
        {
            var scene = SceneManager.GetSceneByName(mapName);
            if (scene.IsValid() && scene.isLoaded) MapManager.PutInMap(go, scene);
        }

        var drop = go.AddComponent<GroundDrop>();
        drop.def = def;
        drop.count = count;
        drop.instance = instance;
        drop.quality = instance != null ? instance.quality : quality;
        drop.upgrade = instance != null ? instance.upgrade : upgrade;
        drop.rarity = rarity ?? Rarities.Of(def, instance);
        drop.mapName = mapName;
        drop.netId = netId;
        drop.placedAt = Time.time;
        all.Add(drop);
        return drop;
    }

    /// <summary>Reach for <paramref name="drop"/> and try to take it. THE one door for picking anything up, so
    /// the rule about who may have it lives in a single place.
    ///
    /// <para>In a world this only ASKS: the server holds the real thing, checks that the person asking is
    /// actually standing over it, and hands it out first-come — two people reaching at the same moment cannot
    /// both be given it. With no world there is nobody to ask and it goes straight into the bag.</para></summary>
    public static void AskToTake(GroundDrop drop)
    {
        if (drop == null || drop.def == null) return;

        var me = GameNetwork.MyPlayer;
        if (me != null) { me.AskToPickUp(drop.netId); return; }

        if (Character.Instance == null) return;
        // The same room the server asks for, here because with no world this machine is the one deciding.
        var noRoom = Character.Instance.RoomFor(drop.def, drop.instance != null ? 1 : drop.count);
        if (noRoom != BagRefusal.None) { ChatController.GameSaid(Character.Instance.RefusalLine(drop.def, noRoom, "pick up")); return; }
        if (drop.instance != null) Character.Instance.TakeIntoBag(drop.instance);
        else Character.Instance.TakeIntoBag(drop.def, drop.count);
        drop.Remove();
        InventoryStore.Instance?.Notify();
    }

    /// <summary>What a click on a drop does — on its name or on its sparkle, the same thing: take it if you are
    /// standing over it, otherwise walk there and take it on arrival, the same answer a skill cast from too far
    /// gives. ONE function for both, so the two ways of clicking can never come to behave differently.</summary>
    public static void ReachFor(GroundDrop drop)
    {
        if (drop == null) return;

        var body = StatusController.Player != null
            ? StatusController.Player.GetComponent<Player3DController>() : null;
        if (body == null) { AskToTake(drop); return; }

        // ALREADY STANDING OVER IT — take it now rather than starting a walk of zero distance, which would stop
        // whatever the player was already doing for no reason.
        float dx = drop.transform.position.x - body.transform.position.x;
        float dz = drop.transform.position.z - body.transform.position.z;
        if (dx * dx + dz * dz <= Reach * Reach) { AskToTake(drop); return; }

        // THE WALK THE GAME ALREADY HAS, told a different distance. It re-measures every frame and runs the
        // errand on the first frame the gap is small enough, so nothing here has to know about routing.
        var wanted = drop;
        body.BeginMoveIntoRange(drop.transform, Reach, () => AskToTake(wanted));
    }

    // ---- pointing at it --------------------------------------------------------------------------------------

    /// <summary>How close to a drop's sparkle the cursor must be to point at it, in screen pixels. Smaller than a
    /// creature's 72 because drops lie only <see cref="Spacing"/> apart: a wide circle would pick a drop from
    /// across a pile, and would steal a click meant for the ground beside it.</summary>
    public const float HoverPixels = 24f;

    static int hoverFrame = -1;
    static GroundDrop hoverPick;

    // The drop whose NAME the pointer is on, told by the name itself. Over a name the interface answers "the
    // pointer is over a window", which hides the ground — so without this a name shown by hovering the sparkle
    // would vanish the moment the cursor moved up onto it.
    internal static GroundDrop nameUnderPointer;

    /// <summary>The drop the cursor is pointing at — its sparkle, or its name — or null. Frame-memoized, because the
    /// cursor picture, the name on the floor and the click all ask in the same frame and must get one answer.
    ///
    /// <para>A creature whose BODY the cursor is directly on wins over a drop: a fight going on over a pile must
    /// still be fightable. A creature the cursor is only NEAR does not — that is a drop being aimed at.</para></summary>
    public static GroundDrop UnderCursor(Camera cam)
    {
        if (Time.frameCount == hoverFrame) return hoverPick;
        hoverFrame = Time.frameCount;
        hoverPick = PickUnderCursor(cam);
        return hoverPick;
    }

    static GroundDrop PickUnderCursor(Camera cam)
    {
        var named = nameUnderPointer;
        if (named != null && named.readable && named.plate != null && named.plate.gameObject.activeInHierarchy)
            return named;
        if (cam == null || all.Count == 0 || UiBlock.PointerOverUI()) return null;

        Vector2 mp = PointerInput.Position;
        GroundDrop best = null;
        float bestSqr = HoverPixels * HoverPixels;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var d = all[i];
            if (d == null) { all.RemoveAt(i); continue; }
            if (!d.readable) continue;
            Vector3 sp = cam.WorldToScreenPoint(d.transform.position);
            // behind the camera
            if (sp.z <= 0f) continue;
            float sqr = ((Vector2)sp - mp).sqrMagnitude;
            if (sqr <= bestSqr) { bestSqr = sqr; best = d; }
        }
        if (best != null && Player3DController.MonsterUnderCursor(cam) != null && Player3DController.CursorOnMonsterBody)
            return null;
        return best;
    }

    /// <summary>Take this drop out of the world — the server when somebody has claimed it, a watcher when the
    /// server says it is gone.</summary>
    public void Remove()
    {
        if (this != null && gameObject != null) Destroy(gameObject);
    }

    void OnDestroy()
    {
        all.Remove(this);
        StopSparkle();
        if (plate != null) Destroy(plate.gameObject);
    }

    /// <summary>What is written on the floor: the refine, the name, and how many when there is more than one —
    /// the same three pieces the bag list writes, in the same order.</summary>
    public string Text
    {
        get
        {
            if (def == null) return "";
            string s = upgrade > 0 ? "+" + upgrade + " " + def.displayName : def.displayName;
            return count > 1 ? s + " ×" + count : s;
        }
    }

    // ---- the words on the floor --------------------------------------------------------------------------

    /// <summary>The sparkle that marks the spot. ONE registered effect, played through the
    /// game's own effect system — so it is pooled, it is stopped the same way every other attached effect is, and
    /// a server (which draws nothing) skips it without this having to ask.</summary>
    const string SparkleEffect = "loot.drop";

    static Transform host;
    RectTransform plate;       // the plate + rounded edge the name sits on
    Image plateFill;           // the plate's body, tinted while the cursor is on it
    TMP_Text label;

    /// <summary>THE one way a name's plate changes colour — resting or pointed at — and its words with it: the rarity's
    /// colour, darkened only as far as it must be to read on that plate.</summary>
    public void SetPlateFill(Color fill)
    {
        if (plateFill != null) plateFill.color = fill;
        if (label != null) label.color = UiKit.Readable(Rarities.Color(rarity), fill);
    }
    Vfx.EffectHandle sparkle;

    // Can this machine reach for it: false while its map is not the one this player is standing in. The sparkle
    // goes out and the name cannot be shown at the same moment, so a drop in another map is neither seen, read
    // nor picked up.
    bool readable = true;

    // How far above the drop's own position the plate sits, in world units. Head height for a thing on the floor:
    // clear of the sparkle underneath it, low enough to read as belonging to that spot rather than floating.
    const float LabelHeight = 0.45f;

    // Glyphs are rendered into a big atlas and scaled down — a large atlas downscaled stays crisp where a small
    // one upscaled goes soft. The pair below is the damage number's pair, so floor text and combat text are the
    // same size on screen at the starting zoom. BaseScale is the world units per UI unit the name was drawn
    // at in the world; it now only sets the name's fixed on-screen size (see OverheadScreenLayer.ScaleFor).
    const float BaseScale = 0.0026f;
    const int FontSize = 48;

    // Breathing room between the words and the edge, in the same pixels the font is measured in. Wider at
    // the sides than the ends, which is what stops a one-word name reading as a cramped box.
    const int TextPadSides = 14;
    const int TextPadEnds = 5;

    static bool NobodyToShowItTo => GameServer.CaughtDrawing("GroundDrop");

    /// <summary>A WATCHING MACHINE: put this drop's plate and sparkle up.</summary>
    public void ShowOnScreen()
    {
        if (NobodyToShowItTo) return;
        BuildPlate();
        StartSparkle();
    }

    /// <summary>The name on a plate with a thin rounded edge — the same frame the right-click menu and the item
    /// tooltip are built from, so a thing on the floor is dressed like every other panel in the game rather than
    /// being a look of its own. It sizes itself to the words, so a long name and a short one both fit exactly.</summary>
    void BuildPlate()
    {
        EnsureHost();

        // The OUTER box. It is one pixel bigger than the fill on every side, and that one pixel IS the border
        // — the same way every window in this game draws its edge.
        var border = UiKit.Round(UiKit.Img("DropLabel", host, UiKit.WindowEdge), UiKit.WindowRadius);
        plate = border.rectTransform;
        // Nothing until the ticker first puts it on the screen at its real size.
        plate.localScale = Vector3.zero;
        // The whole plate is the thing you point at, edge included — so the cursor never falls through the one
        // pixel of edge at its rim.
        border.raycastTarget = true;

        var frame = border.gameObject.AddComponent<HorizontalLayoutGroup>();
        frame.padding = new RectOffset(1, 1, 1, 1);
        frame.childControlWidth = frame.childControlHeight = true;
        frame.childForceExpandWidth = frame.childForceExpandHeight = true;
        var frameFitter = border.gameObject.AddComponent<ContentSizeFitter>();
        frameFitter.horizontalFit = frameFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // The fill. The game's own panel colour rather than one typed here, so the floor and the windows can never
        // drift apart.
        var fill = UiKit.Round(UiKit.Img("Fill", border.transform, UiKit.PanelFill), UiKit.WindowRadius - 1);
        fill.raycastTarget = false;
        plateFill = fill;
        var inner = fill.gameObject.AddComponent<HorizontalLayoutGroup>();
        inner.padding = new RectOffset(TextPadSides, TextPadSides, TextPadEnds, TextPadEnds);
        inner.childControlWidth = inner.childControlHeight = true;
        inner.childForceExpandWidth = inner.childForceExpandHeight = true;

        label = UiKit.MakeText("Text", fill.transform, Text, FontSize, FontStyle.Bold,
                               TextAnchor.MiddleCenter, null, false);
        // Dark text on the light plate in its rarity's colour, like the same item's name in the bag; the font's own
        // shared material, so a floor covered in them is still one draw call.
        label.color = UiKit.Readable(Rarities.Color(rarity));
        label.raycastTarget = false;

        // SIZED NOW, while it can be. A box that is switched off does no layout, so one measured only when Alt is
        // first held would appear at the wrong size for a frame and then snap.
        LayoutRebuilder.ForceRebuildLayoutImmediate(plate);
        var pointer = border.gameObject.AddComponent<GroundDropLabelPointer>();
        pointer.Bind(this, fill);

        // NOTHING IS READ UNTIL IT IS ASKED FOR — by pointing at this one, or by holding Alt for all of them. A field
        // where every drop shouts its name is unreadable; the sparkle says something is there, and pointing says
        // what. It is also what keeps these out of the cursor's way: a switched-off plate is not something the
        // pointer can find.
        plate.gameObject.SetActive(false);
    }

    // The one colour every drop's sparkle is drawn in, whatever it is. A drop's rarity colours its name on the plate,
    // never the sparkle; what a piece is worth is its grade and its folds, which are words on the label.
    static readonly Color SparkleColor = new Color(0.85f, 0.82f, 0.62f);

    /// <summary>Light the spot up. Through the game's own effect system, so it is pooled like every other effect
    /// and a headless server plays nothing.</summary>
    void StartSparkle()
    {
        if (sparkle != null) return;
        sparkle = Vfx.Attach(SparkleEffect, transform, tint: SparkleColor);
    }

    void StopSparkle()
    {
        if (sparkle == null) return;
        sparkle.Stop();
        sparkle = null;
    }

    static void EnsureHost()
    {
        if (host != null) return;

        // CLICKABLE: the raycaster is what makes a name clickable. Without it the plates are pictures; with it the
        // cursor can find them, and the game's own "is the pointer over a window" test starts answering yes over
        // one — which is what stops a click that takes an item from ALSO telling your legs to walk to that spot.
        hostCanvas = OverheadScreenLayer.CreateCanvas("GroundDropSystem", clickable: true);
        hostCanvas.gameObject.AddComponent<GroundDropLabels>();
        host = hostCanvas.transform;
    }

    static Canvas hostCanvas;

    /// <summary>Show or hide the words, and with them the reach. A player standing in a different map keeps the
    /// drop but must neither read it nor pick it up — the same rule a creature in another map lives by.</summary>
    public void SetReadable(bool on)
    {
        if (readable == on) return;
        readable = on;
        if (NobodyToShowItTo) return;

        if (on) StartSparkle(); else StopSparkle();
        // The name follows whether Alt is being held as well, so walking back into the map does not put a name up
        // that nobody asked for.
        ShowName(on && (namesWanted || hovered == this));
    }

    void ShowName(bool on)
    {
        if (plate == null || plate.gameObject.activeSelf == on) return;
        // UNLIT EVERY TIME IT COMES BACK. Letting go of Alt while the cursor is on a name switches the plate off
        // without the cursor ever leaving it, so the "you are pointing at me" brightening is never taken back —
        // and the next time you held Alt that name would already look hovered.
        if (!on) SetPlateFill(UiKit.PanelFill);
        plate.gameObject.SetActive(on);
    }

    // IS ALT DOWN, asked ONCE a frame for the whole world rather than once per drop, and acted on only when the
    // answer actually moves — so a field of drops costs nothing at all while nobody is holding the key.
    static bool namesWanted;

    /// <summary>The one per-frame job the floor needs, called by the single ticker below.
    ///
    /// <para>Two halves. First: hold Alt and every name appears, let go and they all vanish — read once here, and
    /// applied only on the frame it changes. Second: while names ARE up, each one is put on the screen over its
    /// drop at its fixed on-screen size, with the camera and that size read a single time for all of them.</para>
    ///
    /// <para>The position is written every frame, and that is deliberate rather than wasteful: the camera moves
    /// whenever the player does, and a drop MOVES when the machine sets its map down somewhere else, which happens
    /// whenever maps come and go. Words placed once and left would stay where the drop used to be on screen.</para></summary>
    internal static void TickNames()
    {
        bool wanted = Hotkeys.Held(GameKeys.ShowDrops) || Hotkeys.Held(GameKeys.ShowDropsAlt);
        if (wanted != namesWanted)
        {
            namesWanted = wanted;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var d = all[i];
                if (d == null) { all.RemoveAt(i); continue; }
                d.ShowName(d.readable && (wanted || d == hovered));
            }
        }

        var cam = CameraRig.Cam;
        if (cam == null) return;

        // THE ONE BEING POINTED AT shows its name without Alt, lit as pointed-at — changed only on the frame the
        // pointed-at drop changes, so a still cursor costs one pick and nothing else.
        var now = UnderCursor(cam);
        if (now != hovered)
        {
            var was = hovered;
            hovered = now;
            if (was != null)
            {
                was.SetPlateFill(UiKit.PanelFill);
                was.ShowName(was.readable && namesWanted);
            }
            if (now != null)
            {
                now.ShowName(true);
                now.SetPlateFill(UiKit.Hover);
            }
        }

        float scale = OverheadScreenLayer.ScaleFor(hostCanvas, BaseScale);
        if (!namesWanted)
        {
            if (hovered != null && hovered.plate != null) hovered.PlaceName(cam, scale);
            return;
        }
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var d = all[i];
            if (d == null) { all.RemoveAt(i); continue; }
            if (d.plate == null || !d.readable) continue;
            d.PlaceName(cam, scale);
        }
    }

    // The drop the cursor is on, as last acted on by the ticker — what decides which single name is up without Alt.
    static GroundDrop hovered;

    void PlaceName(Camera cam, float scale)
    {
        var at = transform.position;
        at.y += LabelHeight;
        OverheadScreenLayer.Place(plate, at, cam, scale);
    }
}

/// <summary>The one per-frame job the floor needs: noticing Alt, and turning every name that is up to face the
/// camera. It lives on the shared canvas so there is exactly ONE of it however many things are lying about — a
/// script on each drop would be an engine callback per drop, and each of them would read the key and the camera
/// all over again.</summary>
[DisallowMultipleComponent]
public class GroundDropLabels : MonoBehaviour
{
    void LateUpdate() => GroundDrop.TickNames();
}

/// <summary>What the cursor can do to a name on the floor: HOVER lifts its background, and a CLICK takes the
/// item. Standing too far away is not a refusal — you walk there first and pick it up on arrival, the same
/// answer a skill cast from too far gives.
///
/// <para>It rides on the plate itself rather than being a scan of what is under the cursor, so the answer to
/// "which one am I pointing at" is the one the interface already worked out, not a second one that could
/// disagree with what is drawn.</para></summary>
[DisallowMultipleComponent]
public class GroundDropLabelPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    GroundDrop drop;
    Image fill;

    internal void Bind(GroundDrop owner, Image background)
    {
        drop = owner;
        fill = background;
    }

    /// <summary>What a name looks like with the cursor on it: the plate filled with the theme's hover colour, the one
    /// this game uses to say "you are pointing at this". Solid, because a see-through plate would read the ground
    /// through the words. It also tells the floor which drop is pointed at, so the cursor turns into the hand.</summary>
    public void OnPointerEnter(PointerEventData e)
    {
        if (drop != null) drop.SetPlateFill(UiKit.Hover);
        else if (fill != null) fill.color = UiKit.Hover;
        GroundDrop.nameUnderPointer = drop;
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (drop != null) drop.SetPlateFill(UiKit.PanelFill);
        else if (fill != null) fill.color = UiKit.PanelFill;
        if (GroundDrop.nameUnderPointer == drop) GroundDrop.nameUnderPointer = null;
    }

    // A name switched off under a still cursor gets no exit, so it lets go here too.
    void OnDisable()
    {
        if (drop != null && GroundDrop.nameUnderPointer == drop) GroundDrop.nameUnderPointer = null;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (drop == null || e.button != PointerEventData.InputButton.Left) return;
        GroundDrop.ReachFor(drop);
    }
}
