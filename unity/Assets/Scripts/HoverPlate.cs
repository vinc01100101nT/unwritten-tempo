using UnityEngine;

/// <summary>
/// THE READOUT THAT FLOATS OVER THE CREATURE YOU ARE POINTING AT — or, when you point at nobody, over the creature you
/// have locked as your target, for as long as it stays locked — its name (its portrait and its element beside it),
/// its health and its mana. The plate itself is a <see cref="UnitPlate"/>, the same widget the plates kept up over
/// every person are; this decides only WHICH creature it rides and feeds it what the server said about them.
///
/// <para><b>Not over people.</b> Every person already wears a plate of their own all the time
/// (<see cref="CharacterPlates"/>), so pointing at one adds nothing.</para>
///
/// <para><b>No buff icons.</b> Another unit's buffs and debuffs are not drawn anywhere.</para>
///
/// <para><b>ONE widget, not one per unit.</b> Only one thing can be under the cursor, so this keeps a single plate and
/// re-points it.</para>
///
/// <para>PERFORMANCE (see PERFORMANCE.md): per frame this only follows the unit and faces the camera. Numbers are
/// rewritten when they move, and the bar width is measured once when the plate binds to a new unit — a body does not
/// change size while it stands there.</para>
/// </summary>
public class HoverPlate : MonoBehaviour
{
    readonly UnitPlate plate = new UnitPlate("HoverPlate");

    Transform camT;
    Health tracked;
    UnitStates subscribed;
    bool elementDirty;

    // What the cursor found last, and whether that was a person — asked once per change, not per frame.
    Health lastPicked;
    bool pickedIsPerson;

    // The locked target's bar shows whenever the cursor is over nobody. The rig is found once per player body.
    StatusController lastPlayer;
    Player3DController rig;

    // A creature's name carries its level, which the server re-sends when it changes; null for anyone else.
    NetMob shell;
    string baseName;
    int lastLevel;

    void Awake()
    {
        // A SERVER HAS NO CURSOR AND NO SCREEN. Left switched on it would run a raycast and two registry scans every
        // frame to work out what a person who is not there is pointing at. Same guard, same reason, as the minimap's.
        if (GameServer.IsServer || Application.isBatchMode) { enabled = false; return; }
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void LateUpdate()
    {
        if (camT == null)
        {
            var cam = CameraRig.Cam;
            if (cam == null) return;
            camT = cam.transform;
        }

        var unit = Player3DController.UnitUnderCursor(CameraRig.Cam);
        if (unit != lastPicked)
        {
            lastPicked = unit;
            pickedIsPerson = unit != null && NetPlayer.ShellForBody(unit.gameObject) != null;
        }
        if (pickedIsPerson) unit = null;
        if (unit == null) unit = LockedTarget();
        if (unit != tracked) Bind(unit);

        // NOTHING UNDER THE CURSOR, AND NO LOCKED TARGET.
        if (tracked == null || tracked.IsDead)
        {
            plate.Show(false);
            return;
        }

        // The plate went with the map but the unit did not: bind it again, so the new plate gets its name and face too.
        if (plate.Ensure()) Bind(tracked);
        plate.Show(true);

        plate.Follow(tracked.transform, camT.rotation);
        plate.SetHealth(tracked.Current, tracked.maxHealth);
        plate.SetMana(tracked.CurrentMP, tracked.MaxMP);

        if (shell != null && shell.Level != lastLevel)
        {
            lastLevel = shell.Level;
            plate.SetName(UnitLabel.WithLevel(baseName, lastLevel));
        }

        // An armour converter starting or ending is a state change, and it changes the element the tile shows.
        if (elementDirty) { elementDirty = false; DrawElement(); }
    }

    Health LockedTarget()
    {
        var p = StatusController.Player;
        if (p != lastPlayer) { lastPlayer = p; rig = p != null ? p.GetComponent<Player3DController>() : null; }
        return rig != null ? rig.AttackTarget : null;
    }

    // ---- binding ------------------------------------------------------------------------------------------

    void Bind(Health unit)
    {
        Unsubscribe();
        tracked = unit;
        shell = null;
        if (unit == null) return;

        subscribed = unit.States;
        if (subscribed != null) subscribed.OnChanged += MarkElementDirty;

        plate.Ensure();

        // Found once per bind — a dictionary probe, never per frame.
        shell = NetMob.ShellForView(unit.gameObject) ?? NetMob.ShellForServerMob(unit.gameObject);
        baseName = UnitLabel.Of(unit);
        lastLevel = shell != null ? shell.Level : 0;

        // The body's own width, read once here rather than per frame: a body does not change size while it is
        // standing in front of you.
        plate.Begin(UnitAnchor.HalfWidth(unit.transform), UnitLabel.WithLevel(baseName, lastLevel),
                    CreaturePortraits.For(unit, shell));
        DrawElement();
    }

    void Unsubscribe()
    {
        if (subscribed != null) subscribed.OnChanged -= MarkElementDirty;
        subscribed = null;
    }

    void MarkElementDirty() => elementDirty = true;

    // WHAT THE CREATURE IS HIT AS RIGHT NOW — an armour converter's element while one runs, its own otherwise — read
    // through the same one answer the target panel reads, so the two can never show different elements. Every unit
    // gets a tile, Neutral included. Runs on a bind and on a state change, never per frame.
    void DrawElement()
    {
        if (tracked == null) { plate.SetElement(false, Element.Neutral); return; }
        var st = tracked.States;
        var el = st != null ? st.DefendingElementOr(tracked.element, tracked.elementLevel, out _) : tracked.element;
        plate.SetElement(true, el);
    }
}
