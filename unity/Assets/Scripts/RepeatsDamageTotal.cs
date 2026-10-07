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
    static readonly Color TotalColor = new Color(1f, 0.92f, 0.15f);   // the same yellow a multi-hit flurry pops

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
    /// was not owed.</summary>
    public static void ReportArrived(Transform mob, int total, uint attacker)
    {
        if (mob == null || total <= 0) return;
        if (watching == null)
        {
            var go = new GameObject("RepeatsDamageTotal_Watching") { hideFlags = HideFlags.DontSaveInEditor };
            watching = go.AddComponent<RepeatsDamageTotal>();
        }
        watching.Report(mob, total, attacker);
    }

    void Report(Transform mob, int total, uint attacker)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].mob != mob || entries[i].attacker != attacker) continue;
            var e = entries[i];
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
    struct Run { public int total; public float lastHit; }

    static readonly Dictionary<ulong, Run> open = new Dictionary<ulong, Run>(64);
    static readonly List<ulong> closing = new List<ulong>(16);
    static float nextSweep;

    /// <summary>Count <paramref name="damage"/> into <paramref name="strikerNetId"/>'s run over the unit called
    /// <paramref name="struckNetId"/>, and return the total it now stands at.</summary>
    public static int Add(uint struckNetId, uint strikerNetId, int damage)
    {
        float now = Time.time;
        if (now >= nextSweep) Sweep(now);
        ulong key = ((ulong)struckNetId << 32) | strikerNetId;
        if (!open.TryGetValue(key, out var run) || now - run.lastHit >= RepeatsDamageTotal.IdleCloseSeconds) run.total = 0;
        run.total += damage;
        run.lastHit = now;
        open[key] = run;
        return run.total;
    }

    // Runs nobody has struck for a while are forgotten, at most once a second — so the table holds only the fights
    // going on now, however many have come and gone.
    static void Sweep(float now)
    {
        nextSweep = now + 1f;
        closing.Clear();
        foreach (var pair in open)
            if (now - pair.Value.lastHit >= RepeatsDamageTotal.IdleCloseSeconds) closing.Add(pair.Key);
        for (int i = 0; i < closing.Count; i++) open.Remove(closing[i]);
    }
}
