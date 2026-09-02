using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The rising yellow combo total for a WHOLE repeating run, rather than for one flurry of it — what a skill asks for
/// with <see cref="SkillDef.showRepeatsTotalDamage"/>. One total per mob: it opens the first time the run damages that
/// mob, climbs with every later strike of the same run, and closes on its own once that mob has gone
/// <see cref="IdleCloseSeconds"/> without being struck again — so a dance that hops away and comes back starts a fresh
/// number instead of parking one on screen for the whole run.
///
/// <para><b>Purely presentation.</b> It only reads damage that has already been dealt; nothing here decides, scales or
/// suppresses a single point of it. That is why it can sit at the one place the white number is spawned
/// (<c>Health.SpawnElementNumber</c>) and catch every path — single strike, multi-hit cascade, overkill beat — with no
/// per-path plumbing to keep in sync.</para>
///
/// <para>The multi-hit flurry owns this same number when a run does NOT ask for it (<see cref="MultiHitBurst"/>), which
/// is why exactly one of the two shows it: the applier hands the burst <c>showTotal: false</c> while a run total is
/// live, so a meteor shower keeps its per-meteor totals and a charge-driven dance gets one per mob.</para>
///
/// <para>Scoped like the causal chain it rides beside: <see cref="Current"/> is set only while a cast is applying, so a
/// basic attack landing in the same instant can never fold into a skill's total.</para>
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
        /// pick the same target. 0 = nobody in particular, which is what a single-machine run has always been.</summary>
        public uint attacker;

        public DamagePopup popup;
        public int total;
        public float lastHit;
    }

    readonly List<Entry> entries = new List<Entry>(8);
    bool ended;

    /// <summary>The run whose damage is being applied right now, or null when nothing is.</summary>
    public static RepeatsDamageTotal Current { get; private set; }

    /// <summary>Open a run's totals. Runs on its own object so a total outlives the beat that spawned it (and the
    /// caster being disabled mid-run), exactly like the multi-hit flurry's.</summary>
    public static RepeatsDamageTotal Begin()
    {
        // NOBODY TO SHOW IT TO on a headless server, which resolves everybody's runs and would otherwise build a
        // floating number per run in front of no screen. The twin of Sfx.Deaf, Vfx.Blind and BattleNotice's guard.
        if (GameServer.IsServer) return null;
        var go = new GameObject("RepeatsDamageTotal") { hideFlags = HideFlags.DontSaveInEditor };
        return go.AddComponent<RepeatsDamageTotal>();
    }

    public static RepeatsDamageTotal Push(RepeatsDamageTotal t) { var prev = Current; Current = t; return prev; }
    public static void Pop(RepeatsDamageTotal prev) { Current = prev; }

    /// <summary>Fold one landed hit into that mob's running total, opening the number if this is the first.
    /// <paramref name="attacker"/> names WHOSE run it belongs to, so two people striking one creature keep two
    /// numbers instead of pooling into a third that is nobody's.</summary>
    public void Report(Transform mob, int dmg, uint attacker = 0)
    {
        if (mob == null || dmg <= 0) return;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].mob != mob || entries[i].attacker != attacker) continue;
            var e = entries[i];
            e.total += dmg;
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
            total = dmg,
            lastHit = Time.time,
            popup = DamagePopup.SpawnHeld(UnitAnchor.Overhead(mob), dmg, TotalColor, 1.7f),
        });
    }

    // ---- the watching machine's totals ---------------------------------------------------------------------------

    /// <summary>The one keeper for totals built from numbers that ARRIVE rather than from numbers this machine
    /// decided. It never ends, because there is no run to end it: a player's machine no longer works damage out, so
    /// every strike reaches it separately, long after the cast that sent it has returned. What closes a number here
    /// is the same idle gap that closes every other one, which is why this needed no rule of its own.</summary>
    static RepeatsDamageTotal watching;

    /// <summary>Fold an ARRIVED strike into its own attacker's total over that creature. Called for a strike the
    /// server marked as belonging to a run that draws one — the flag is the server's reading of the authored skill,
    /// never a client's claim, so a machine cannot ask for a number it was not owed.</summary>
    public static void ReportArrived(Transform mob, int dmg, uint attacker)
    {
        if (mob == null || dmg <= 0) return;
        if (watching == null)
        {
            var go = new GameObject("RepeatsDamageTotal_Watching") { hideFlags = HideFlags.DontSaveInEditor };
            watching = go.AddComponent<RepeatsDamageTotal>();
        }
        watching.Report(mob, dmg, attacker);
    }

    /// <summary>The run is over. Any total still open finishes its idle wait first, so the last beat's number reads the
    /// same as every earlier one instead of being cut short by the run ending.</summary>
    public void End() => ended = true;

    void Update()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            if (e.mob != null && Time.time - e.lastHit < IdleCloseSeconds) continue;
            if (e.popup != null) e.popup.Release();   // released → it stays put and fades
            entries.RemoveAt(i);
        }
        if (ended && entries.Count == 0) Destroy(gameObject);
    }
}
