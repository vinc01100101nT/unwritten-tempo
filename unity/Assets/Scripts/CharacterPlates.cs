using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A PLATE OVER EVERY PERSON, ALL THE TIME, with no pointing needed: your own character and every other player on
/// screen show their name, their health and their mana; every townsperson shows their name, and under it in smaller
/// letters what they are. The plate is the same
/// <see cref="UnitPlate"/> a creature gets when you point at it (<see cref="HoverPlate"/>, which leaves people alone).
/// A player's buffs and debuffs are not shown here.
///
/// <para><b>Nothing is worked out here.</b> Your own numbers are the ones the server sent your character; another
/// person's are the ones it sent the copy of their body this machine draws; a townsperson's name is the one the server
/// sent with them.</para>
///
/// <para>PERFORMANCE (see PERFORMANCE.md): one plate per person on screen, all under the one shared world-space canvas.
/// Per frame each plate follows its person and faces the camera; numbers are rewritten only when they move. The people
/// are walked through the registries the network layer already keeps (<see cref="NetPlayer.WatchedBodies"/>,
/// <see cref="NetNpc.Shown"/>), never by searching the scene, and a plate whose person has gone is put back in a pool
/// rather than destroyed.</para>
/// </summary>
public class CharacterPlates : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        // A SERVER HAS NO SCREEN, so it draws nobody's plate.
        if (GameServer.IsServer || Application.isBatchMode) return;
        var go = new GameObject("CharacterPlates") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);
        go.AddComponent<CharacterPlates>();
    }

    // One person's plate, and what it already holds of them.
    sealed class Worn
    {
        public Health body;
        public UnitPlate plate;
        public UnitBody size;      // how wide they are; a body that has not finished loading states it later
        public float drawnHalfWidth;
        public int seenFrame;
    }

    readonly UnitPlate mine = new UnitPlate("MyPlate");
    UnitBody mySize;
    float myDrawnHalfWidth = -1f;
    Transform myBody;
    float nextLookForMySize;

    readonly List<Worn> others = new List<Worn>(8);
    readonly Dictionary<Health, Worn> byBody = new Dictionary<Health, Worn>();
    readonly Stack<UnitPlate> spare = new Stack<UnitPlate>();

    // One townsperson's plate: their name and the line under it saying what they are, nothing else.
    sealed class Named
    {
        public NetNpc npc;
        public UnitPlate plate;
        public GameObject body;      // the body the plate was set up for; a new one is set up again
        public string drawnName;
        public string drawnDescription;
        public int seenFrame;
    }

    readonly List<Named> townsfolk = new List<Named>(16);
    readonly Dictionary<NetNpc, Named> byNpc = new Dictionary<NetNpc, Named>();
    readonly Stack<UnitPlate> spareNamed = new Stack<UnitPlate>();

    Transform camT;

    void LateUpdate()
    {
        if (camT == null)
        {
            var cam = CameraRig.Cam;
            if (cam == null) return;
            camT = cam.transform;
        }
        var facing = camT.rotation;

        DrawMine(facing);
        DrawOthers(facing);
        DrawTownsfolk(facing);
    }

    // ---- your own character -------------------------------------------------------------------------------

    void DrawMine(Quaternion facing)
    {
        var me = Character.Instance;
        var avatar = PersistentPlayer3D.Instance;
        if (me == null || avatar == null || me.IsDead || !avatar.HasArrived)
        {
            mine.Show(false);
            return;
        }

        var body = avatar.transform;
        if (mine.Ensure() || body != myBody)
        {
            myBody = body;
            mySize = null;
            nextLookForMySize = 0f;
            myDrawnHalfWidth = UnitAnchor.HalfWidth(body);
            mine.Begin(myDrawnHalfWidth, AccountSession.CharacterName ?? "", null);
            // YOUR OWN HEALTH BAR is a part of the screen the tutorial guide may point at.
            GuideMarker.Put(mine.HealthBar, GuideSpot.OwnHealthBar);
        }

        // THE BODY STATES ITS WIDTH ONCE ITS MODEL HAS LOADED, which can be after the plate went up. Looked for once a
        // second until it is there, then read as a plain field.
        if (mySize == null && Time.time >= nextLookForMySize)
        {
            nextLookForMySize = Time.time + 1f;
            mySize = body.GetComponent<UnitBody>();   // perf-ok: throttled to once a second, and only until found
        }
        if (mySize != null && mySize.halfWidth > 0f && !Mathf.Approximately(mySize.halfWidth, myDrawnHalfWidth))
        {
            myDrawnHalfWidth = mySize.halfWidth;
            mine.SetWidth(myDrawnHalfWidth);
        }

        mine.Show(true);
        mine.Follow(body, facing);
        mine.SetHealth(me.CurrentHP, me.MaxHP);
        mine.SetMana(me.CurrentMP, me.MaxMP);
    }

    // ---- everybody else -----------------------------------------------------------------------------------

    void DrawOthers(Quaternion facing)
    {
        int frame = Time.frameCount;
        var bodies = NetPlayer.WatchedBodies;
        for (int i = 0; i < bodies.Count; i++)
        {
            var h = bodies[i];
            if (h == null) continue;

            if (!byBody.TryGetValue(h, out var w))
            {
                w = new Worn { body = h, plate = spare.Count > 0 ? spare.Pop() : new UnitPlate("PlayerPlate") };
                byBody[h] = w;
                others.Add(w);
                w.drawnHalfWidth = -1f;
            }
            w.seenFrame = frame;

            // A dead person's plate goes, the same as a creature's does.
            if (h.IsDead)
            {
                w.plate.Show(false);
                continue;
            }

            if (w.plate.Ensure() || w.drawnHalfWidth < 0f)
            {
                // perf-ok: once per person per plate built (they arrive, or a map change took the plate).
                w.size = h.GetComponent<UnitBody>();
                w.drawnHalfWidth = UnitAnchor.HalfWidth(h.transform);
                w.plate.Begin(w.drawnHalfWidth, UnitLabel.Of(h), null);
            }
            if (w.size != null && w.size.halfWidth > 0f && !Mathf.Approximately(w.size.halfWidth, w.drawnHalfWidth))
            {
                w.drawnHalfWidth = w.size.halfWidth;
                w.plate.SetWidth(w.drawnHalfWidth);
            }

            w.plate.Show(true);
            w.plate.Follow(h.transform, facing);
            w.plate.SetHealth(h.Current, h.maxHealth);
            w.plate.SetMana(h.CurrentMP, h.MaxMP);
        }

        // A PERSON WHO HAS GONE — logged out, walked to another map, or their body was rebuilt — is no longer in the
        // registry, so their plate was not touched this frame. It goes back in the pool, hidden.
        for (int i = others.Count - 1; i >= 0; i--)
        {
            var w = others[i];
            if (w.seenFrame == frame) continue;
            w.plate.Show(false);
            spare.Push(w.plate);
            if (!ReferenceEquals(w.body, null)) byBody.Remove(w.body);
            others.RemoveAt(i);
        }
    }

    // ---- townsfolk ----------------------------------------------------------------------------------------

    void DrawTownsfolk(Quaternion facing)
    {
        int frame = Time.frameCount;
        var shown = NetNpc.Shown;
        for (int i = 0; i < shown.Count; i++)
        {
            var npc = shown[i];
            if (npc == null) continue;
            var body = npc.View;
            if (body == null) continue;

            if (!byNpc.TryGetValue(npc, out var n))
            {
                n = new Named { npc = npc, plate = spareNamed.Count > 0 ? spareNamed.Pop() : new UnitPlate("TownsfolkPlate") };
                byNpc[npc] = n;
                townsfolk.Add(n);
            }
            n.seenFrame = frame;

            if (n.plate.Ensure() || n.body != body)
            {
                n.body = body;
                n.drawnName = npc.personName;
                n.plate.SetBars(false);
                n.plate.Begin(UnitAnchor.HalfWidth(body.transform), n.drawnName, null);
                n.drawnDescription = npc.personDescription;
                n.plate.SetDescription(n.drawnDescription);
            }
            else
            {
                if (!ReferenceEquals(n.drawnName, npc.personName))
                {
                    n.drawnName = npc.personName;
                    n.plate.SetName(n.drawnName);
                }
                if (!ReferenceEquals(n.drawnDescription, npc.personDescription))
                {
                    n.drawnDescription = npc.personDescription;
                    n.plate.SetDescription(n.drawnDescription);
                }
            }

            n.plate.Show(true);
            n.plate.Follow(body.transform, facing);
            // "!" over somebody offering a quest this character can take, "?" where one of its quests is waiting — both
            // the server's answers (QuestMarks); drawn only when they change.
            n.plate.SetMark(QuestMarks.For(npc.personId));
        }

        // A townsperson who has gone — out of range, or their map was left — is no longer in the registry.
        for (int i = townsfolk.Count - 1; i >= 0; i--)
        {
            var n = townsfolk[i];
            if (n.seenFrame == frame) continue;
            n.plate.Show(false);
            spareNamed.Push(n.plate);
            if (!ReferenceEquals(n.npc, null)) byNpc.Remove(n.npc);
            townsfolk.RemoveAt(i);
        }
    }
}
