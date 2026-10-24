using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The rising yellow combo total for a WHOLE repeating run, rather than for one flurry of it — what a skill asks for
/// with <see cref="SkillDef.showRepeatsTotalDamage"/>. One total per mob and per striker: it opens the first time the
/// run's damage on that mob arrives, climbs with every later strike, and closes on its own once that mob has gone
/// <see cref="IdleCloseSeconds"/> without being struck again — so a dance that hops away and comes back starts a fresh
/// number instead of parking one on screen for the whole run.
///
/// <para><b>Drawn only on the machines watching, from the server's sum.</b> Every strike reaches them separately,
/// already decided, carrying the total its striker's run now stands at (<see cref="StrikeTotals"/>); this machine puts
/// that number up and adds nothing. The server itself draws nothing.</para>
/// </summary>
public class RepeatsDamageTotal : MonoBehaviour
{
    /// <summary>The same yellow a multi-hit flurry pops — and the kill combo's count (<see cref="KillComboHud"/>).</summary>
    public static readonly Color TotalColor = new Color(1f, 0.92f, 0.15f);

    /// <summary>How long a mob may go unstruck before its total closes and the next hit starts a new one.</summary>
    public const float IdleCloseSeconds = 1f;

    struct Entry
    {
        public Transform mob;

        /// <summary>WHOSE run this total belongs to, as the shared name for a player that every machine agrees on.
        /// A total keyed by the creature alone merges two people hitting the same creature into one climbing number
        /// that belongs to neither of them — which is exactly what happens in a shared world the moment two players
        /// pick the same target.</summary>
        public uint attacker;

        /// <summary>WHICH SKILL's total it is (the number the server sent with it), so two skills one person has hitting
        /// the same creature keep two numbers.</summary>
        public int skill;

        public DamagePopup popup;
        public int total;
        public float lastHit;
    }

    readonly List<Entry> entries = new List<Entry>(8);

    /// <summary>The one keeper for every total. It never ends, because there is no run to end it: every strike reaches
    /// this machine separately, long after the cast that sent it has returned. What closes a number is the idle gap.</summary>
    static RepeatsDamageTotal watching;

    /// <summary>Show the total an ARRIVED strike brought its attacker's run over that creature to — the server's sum,
    /// put up as it is. Only a strike the server counted into a run carries one, so a machine cannot ask for a number it
    /// was not owed. <paramref name="amount"/> is that strike's own number: a total equal to it is the FIRST strike of a
    /// new run (the server starts every run from zero), which gets a number of its own while the last one fades.</summary>
    public static void ReportArrived(Transform mob, int total, int amount, uint attacker, int skill)
    {
        if (mob == null || total <= 0) return;
        if (watching == null)
        {
            var go = new GameObject("RepeatsDamageTotal_Watching") { hideFlags = HideFlags.DontSaveInEditor };
            watching = go.AddComponent<RepeatsDamageTotal>();
        }
        watching.Report(mob, total, total == amount, attacker, skill);
    }

    void Report(Transform mob, int total, bool startsRun, uint attacker, int skill)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].mob != mob || entries[i].attacker != attacker || entries[i].skill != skill) continue;
            var e = entries[i];
            // A NEW RUN never takes over the last one's number: that one is let go to fade where it is.
            if (startsRun && e.popup != null) { e.popup.Release(); e.popup = null; }
            e.total = total;
            e.lastHit = Time.time;
            if (e.popup == null) e.popup = DamagePopup.SpawnHeld(UnitAnchor.Overhead(mob), e.total, TotalColor, 1.7f);
            else e.popup.SetHeld(e.total, UnitAnchor.Overhead(mob));
            entries[i] = e;
            return;
        }
        entries.Add(new Entry
        {
            mob = mob,
            attacker = attacker,
            skill = skill,
            total = total,
            lastHit = Time.time,
            popup = DamagePopup.SpawnHeld(UnitAnchor.Overhead(mob), total, TotalColor, 1.7f),
        });
    }

    void Update()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            if (e.mob != null && Time.time - e.lastHit < IdleCloseSeconds) continue;
            if (e.popup != null) e.popup.Release();   // released → it stays put and fades
            entries.RemoveAt(i);
        }
    }
}

/// <summary>
/// SERVER: the running sum behind every rising yellow total — one per striker per unit struck. A strike counted into a
/// run adds its damage; a run that has gone <see cref="RepeatsDamageTotal.IdleCloseSeconds"/> without a strike starts
/// over, by the same rule the screens close their number on. The total travels with the strike, so no screen adds
/// anything up.
/// </summary>
public static class StrikeTotals
{
    // grouped = counted under a cast's or a landing's group, which alone decides when it starts over; it is forgotten
    // when that group is (Forget). Only a strike with no group falls back to the idle rule.
    struct Run { public int total; public float lastHit; public bool grouped; }

    // WHICH GROUP the strikes landing right now belong to (null = no say): the set of runs this group has already
    // struck. The first strike of a group on a unit starts that unit's run from zero; later ones add to it. The caller
    // owns the set and empties it when its group begins — a new cast, or a new landing — so nothing is numbered and
    // nothing counts up while the server runs (replaced 2026-10-24: each group used to be stamped with a new number).
    static HashSet<ulong> currentGroup;

    /// <summary>WHICH SKILL the strikes landing right now belong to (0 = none): its place in the skill list, so it is
    /// never bigger than the number of skills. Two skills one person has hitting the same unit keep two sums, and
    /// every strike carries this so the screen keeps two numbers.</summary>
    public static int CurrentSkill { get; private set; }

    /// <summary>Every total struck until the scope closes belongs to <c>group</c> (null = no say) of the skill numbered
    /// <c>skill</c>. Server only; nests by putting the outer group back.</summary>
    public readonly struct BeatScope : System.IDisposable
    {
        readonly HashSet<ulong> outer;
        readonly int outerSkill;
        public BeatScope(HashSet<ulong> group, int skill)
        {
            outer = currentGroup; outerSkill = CurrentSkill;
            if (group != null) { currentGroup = group; CurrentSkill = skill; }
        }
        public void Dispose() { currentGroup = outer; CurrentSkill = outerSkill; }
    }

    static readonly Dictionary<(ulong, int), Run> open = new Dictionary<(ulong, int), Run>(64);
    static readonly List<(ulong, int)> closing = new List<(ulong, int)>(16);
    static float nextSweep;

    /// <summary>Count <paramref name="damage"/> into <paramref name="strikerNetId"/>'s run over the unit called
    /// <paramref name="struckNetId"/>, and return the total it now stands at.</summary>
    public static int Add(uint struckNetId, uint strikerNetId, int damage)
    {
        float now = Time.time;
        if (now >= nextSweep) Sweep(now);
        ulong pair = ((ulong)struckNetId << 32) | strikerNetId;
        var key = (pair, CurrentSkill);
        bool known = open.TryGetValue(key, out var run);
        // A STRIKE IN A GROUP starts over only when its group begins — never because time passed, so a run whose beats
        // are slow (Cloudburst fires once a swing, 1.1 s at a normal bow's speed) keeps one total for the whole cast.
        if (currentGroup != null) { if (currentGroup.Add(pair) || !known) run.total = 0; }
        else if (!known || now - run.lastHit >= RepeatsDamageTotal.IdleCloseSeconds) run.total = 0;
        run.grouped = currentGroup != null;
        run.total += damage;
        run.lastHit = now;
        open[key] = run;
        return run.total;
    }

    /// <summary>A group is over (a new cast or landing begins, or its skill is switched off): drop its runs and empty
    /// it. The table therefore holds only the groups still going.</summary>
    public static void Forget(HashSet<ulong> group, int skill)
    {
        foreach (var pair in group) open.Remove((pair, skill));
        group.Clear();
    }

    // Ungrouped runs nobody has struck for a while are forgotten, at most once a second — so the table holds only the
    // fights going on now, however many have come and gone. Grouped runs leave with their group (Forget).
    static void Sweep(float now)
    {
        nextSweep = now + 1f;
        closing.Clear();
        foreach (var pair in open)
            if (!pair.Value.grouped && now - pair.Value.lastHit >= RepeatsDamageTotal.IdleCloseSeconds) closing.Add(pair.Key);
        for (int i = 0; i < closing.Count; i++) open.Remove(closing[i]);
    }
}
