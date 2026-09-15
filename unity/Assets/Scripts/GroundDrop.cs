using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ONE THING LYING ON THE GROUND. You see it as a SPARKLE at the spot, tinted the item's rarity colour, and you
/// read WHAT it is by holding <b>Alt</b> — which puts its name on a small dark plate above it, in the same rarity
/// colour the bag list writes that name in. There is still no icon and no model: the name is the only description
/// of the item anywhere in this game, on the floor exactly as in the bag.
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
/// <para>PERFORMANCE: every label in the world lives under ONE shared world-space canvas — never one canvas
/// each — and ONE ticker on that canvas turns them all to face the camera, reading the camera once per frame
/// rather than once per drop. The same shape <see cref="MobNameplate"/> and <see cref="DamagePopup"/> use.</para>
/// </summary>
[DisallowMultipleComponent]
public class GroundDrop : MonoBehaviour
{
    /// <summary>What it is.</summary>
    public Item def;

    /// <summary>How many. Always 1 for anything wearable, exactly as a bag stack is.</summary>
    public int count = 1;

    /// <summary>The particular copy, with its upgrade level, cards and rolled kit. THE SERVER'S ONLY — null on
    /// every watching machine.</summary>
    public EquipmentInstance instance;

    /// <summary>The colour the name is written in, and what a watcher is told instead of the copy above.</summary>
    public Rarity rarity = Rarity.Common;

    /// <summary>The +N in front of the name. Sent as its own number for the same reason the rarity is.</summary>
    public int upgrade;

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

    /// <summary>Put a thing on the ground at <paramref name="worldPos"/> and hand it back. A server and an
    /// offline game pass the copy; a watching machine passes null and gives the rarity and upgrade instead.</summary>
    public static GroundDrop Place(Vector3 worldPos, Item def, int count, EquipmentInstance instance,
                                   Rarity rarity, int upgrade, string mapName, uint netId)
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
        drop.rarity = instance != null ? instance.Rarity : rarity;
        drop.upgrade = instance != null ? instance.upgrade : upgrade;
        drop.mapName = mapName;
        drop.netId = netId;
        all.Add(drop);

        drop.BuildVisuals();
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
        if (drop.instance != null) Character.Instance.TakeIntoBag(drop.instance);
        else Character.Instance.TakeIntoBag(drop.def, drop.count);
        drop.Remove();
        InventoryStore.Instance?.Notify();
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

    /// <summary>The sparkle that marks the spot. ONE registered effect, tinted per rarity, played through the
    /// game's own effect system — so it is pooled, it is stopped the same way every other attached effect is, and
    /// a server (which draws nothing) skips it without this having to ask.</summary>
    const string SparkleEffect = "loot.drop";

    static Transform host;
    RectTransform plate;       // the dark plate + gold border the name sits on
    Image plateFill;           // the dark part, brightened while the cursor is on it
    TMP_Text label;
    Vfx.EffectHandle sparkle;

    // Can this machine reach for it: false while its map is not the one this player is standing in. The sparkle
    // goes out and the name cannot be shown at the same moment, so a drop in another map is neither seen, read
    // nor picked up.
    bool readable = true;

    // How far above the drop's own position the plate sits, in world units. Head height for a thing on the floor:
    // clear of the sparkle underneath it, low enough to read as belonging to that spot rather than floating.
    const float LabelHeight = 0.45f;

    // Glyphs are rendered into a big atlas and scaled down — a large atlas downscaled stays crisp where a small
    // one upscaled goes soft. The pair below is the damage number's pair, so floor text and combat text carry
    // the same weight on screen.
    const float BaseScale = 0.0026f;
    const int FontSize = 48;

    // Breathing room between the words and the gold edge, in the same pixels the font is measured in. Wider at
    // the sides than the ends, which is what stops a one-word name reading as a cramped box.
    const int TextPadSides = 14;
    const int TextPadEnds = 5;

    static bool NobodyToShowItTo => GameServer.IsServer;

    void BuildVisuals()
    {
        if (NobodyToShowItTo) return;
        BuildPlate();
        StartSparkle();
    }

    /// <summary>The name on a dark plate with a thin gold edge — the same frame the right-click menu and the item
    /// tooltip are built from, so a thing on the floor is dressed like every other panel in the game rather than
    /// being a look of its own. It sizes itself to the words, so a long name and a short one both fit exactly.</summary>
    void BuildPlate()
    {
        EnsureHost();

        // The gold OUTER box. It is one pixel bigger than the fill on every side, and that one pixel IS the border
        // — the same way every window in this game draws its edge.
        var border = UiKit.Img("DropLabel", host, UiKit.Gold);
        plate = border.rectTransform;
        plate.localScale = Vector3.one * BaseScale;
        // The whole plate is the thing you point at, edge included — so the cursor never falls through the one
        // pixel of gold at its rim.
        border.raycastTarget = true;

        var frame = border.gameObject.AddComponent<HorizontalLayoutGroup>();
        frame.padding = new RectOffset(1, 1, 1, 1);
        frame.childControlWidth = frame.childControlHeight = true;
        frame.childForceExpandWidth = frame.childForceExpandHeight = true;
        var frameFitter = border.gameObject.AddComponent<ContentSizeFitter>();
        frameFitter.horizontalFit = frameFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // The dark fill. The game's own panel colour rather than a fresh black typed here, so the floor and the
        // windows can never drift apart.
        var fill = UiKit.Img("Fill", border.transform, UiKit.PanelFill);
        fill.raycastTarget = false;
        plateFill = fill;
        var inner = fill.gameObject.AddComponent<HorizontalLayoutGroup>();
        inner.padding = new RectOffset(TextPadSides, TextPadSides, TextPadEnds, TextPadEnds);
        inner.childControlWidth = inner.childControlHeight = true;
        inner.childForceExpandWidth = inner.childForceExpandHeight = true;

        label = UiKit.MakeText("Text", fill.transform, Text, FontSize, FontStyle.Normal,
                               TextAnchor.MiddleCenter, null, false);
        label.font = UiKit.PixelFont;
        // ONE shared outlined material across every drop, so a floor covered in them is still one draw call.
        label.fontSharedMaterial = UiKit.OutlinedMaterial(UiKit.PixelFont, 0.3f);
        label.color = Rarities.Text(rarity);
        label.raycastTarget = false;

        plate.position = transform.position + Vector3.up * LabelHeight;
        // SIZED NOW, while it can be. A box that is switched off does no layout, so one measured only when Alt is
        // first held would appear at the wrong size for a frame and then snap.
        LayoutRebuilder.ForceRebuildLayoutImmediate(plate);
        var pointer = border.gameObject.AddComponent<GroundDropLabelPointer>();
        pointer.Bind(this, fill);

        // NOTHING IS READ UNTIL ALT IS HELD. A field where every drop shouts its name is unreadable; the sparkle
        // says something is there, and the key says what. It is also what keeps these out of the cursor's way:
        // a switched-off plate is not something the pointer can find.
        plate.gameObject.SetActive(false);
    }

    /// <summary>Light the spot up in the item's rarity colour. Through the game's own effect system, so it is
    /// pooled like every other effect and a headless server plays nothing.</summary>
    void StartSparkle()
    {
        if (sparkle != null) return;
        // The BORDER colour, which is what this game means by "the rarity signal" — the same channel a slot's edge
        // and a tooltip's header carry. Never the name's colour, which is deliberately paler so it stays readable
        // as text and would wash out as a light.
        sparkle = Vfx.Attach(SparkleEffect, transform, tint: Rarities.Border(rarity));
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

        var go = new GameObject("GroundDropSystem") { hideFlags = HideFlags.DontSaveInEditor };
        // Out of the water reflection, the same band the damage numbers sit in.
        go.layer = LayerMask.NameToLayer("TransparentFX");
        hostCanvas = go.AddComponent<Canvas>();
        hostCanvas.renderMode = RenderMode.WorldSpace;
        hostCanvas.sortingOrder = DepthSortRuntime.FxPopup;
        // WHAT MAKES A NAME CLICKABLE. Without this the plates are pictures; with it the cursor can find them,
        // and the game's own "is the pointer over a window" test starts answering yes over one — which is what
        // stops a click that takes an item from ALSO telling your legs to walk to that spot.
        go.AddComponent<GraphicRaycaster>();
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        go.AddComponent<GroundDropLabels>();
        host = go.transform;
    }

    // A world-space canvas needs to be told which camera is looking at it before anything can be clicked on it,
    // and at the moment the canvas is built there may not be one yet — so it is asked for again until there is.
    static Canvas hostCanvas;

    internal static void EnsureEventCamera()
    {
        if (hostCanvas == null || hostCanvas.worldCamera != null) return;
        hostCanvas.worldCamera = CameraRig.Cam;
    }

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
        ShowName(on && namesWanted);
    }

    void ShowName(bool on)
    {
        if (plate == null || plate.gameObject.activeSelf == on) return;
        // UNLIT EVERY TIME IT COMES BACK. Letting go of Alt while the cursor is on a name switches the plate off
        // without the cursor ever leaving it, so the "you are pointing at me" brightening is never taken back —
        // and the next time you held Alt that name would already look hovered.
        if (!on && plateFill != null) plateFill.color = UiKit.PanelFill;
        plate.gameObject.SetActive(on);
    }

    // IS ALT DOWN, asked ONCE a frame for the whole world rather than once per drop, and acted on only when the
    // answer actually moves — so a field of drops costs nothing at all while nobody is holding the key.
    static bool namesWanted;

    /// <summary>The one per-frame job the floor needs, called by the single ticker below.
    ///
    /// <para>Two halves. First: hold Alt and every name appears, let go and they all vanish — read once here, and
    /// applied only on the frame it changes. Second: while names ARE up, each one is placed above its drop and
    /// turned to face the camera, with the camera read a single time for all of them.</para>
    ///
    /// <para>The position is written every frame as well as the rotation, and that is deliberate rather than
    /// wasteful: a drop MOVES when the machine sets its map down somewhere else, which happens whenever maps come
    /// and go. Words placed once and left would stay where the map used to be.</para></summary>
    internal static void TickNames()
    {
        bool wanted = Hotkeys.Held(KeyCode.LeftAlt) || Hotkeys.Held(KeyCode.RightAlt);
        if (wanted != namesWanted)
        {
            namesWanted = wanted;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var d = all[i];
                if (d == null) { all.RemoveAt(i); continue; }
                d.ShowName(wanted && d.readable);
            }
        }
        if (!namesWanted) return;

        EnsureEventCamera();

        var cam = CameraRig.Cam;
        if (cam == null) return;
        var facing = cam.transform.rotation;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var d = all[i];
            if (d == null) { all.RemoveAt(i); continue; }
            if (d.plate == null || !d.readable) continue;
            var at = d.transform.position;
            at.y += LabelHeight;
            d.plate.SetPositionAndRotation(at, facing);
        }
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

    /// <summary>What a name looks like with the cursor on it: the plate's own dark fill carrying the cyan bloom
    /// this game already uses to say "you are pointing at this", at the same weight a bag row and a menu row use
    /// it. Mixed into the fill rather than laid over it, because the plate must stay solid — a see-through one
    /// would read the ground through the words.</summary>
    static readonly Color Hovered = Color.Lerp(UiKit.PanelFill, UiKit.Cyan, 0.14f);

    public void OnPointerEnter(PointerEventData e) { if (fill != null) fill.color = Hovered; }
    public void OnPointerExit(PointerEventData e) { if (fill != null) fill.color = UiKit.PanelFill; }

    public void OnPointerClick(PointerEventData e)
    {
        if (drop == null || e.button != PointerEventData.InputButton.Left) return;

        var body = StatusController.Player != null
            ? StatusController.Player.GetComponent<Player3DController>() : null;
        if (body == null) { GroundDrop.AskToTake(drop); return; }

        // ALREADY STANDING OVER IT — take it now rather than starting a walk of zero distance, which would stop
        // whatever the player was already doing for no reason.
        float dx = drop.transform.position.x - body.transform.position.x;
        float dz = drop.transform.position.z - body.transform.position.z;
        if (dx * dx + dz * dz <= GroundDrop.Reach * GroundDrop.Reach) { GroundDrop.AskToTake(drop); return; }

        // THE WALK THE GAME ALREADY HAS, told a different distance. It re-measures every frame and runs the
        // errand on the first frame the gap is small enough, so nothing here has to know about routing.
        var wanted = drop;
        body.BeginMoveIntoRange(drop.transform, GroundDrop.Reach, () => GroundDrop.AskToTake(wanted));
    }
}
