using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// A DASH STRIKES WHERE IT LANDS. When a skill's caster effects send the caster gliding (a Dash), the same binding's
/// target effects — its damage, its ailments, its knockback — are held here for exactly as long as the trip takes
/// (<see cref="GlideEase.Seconds"/>) and land when the caster arrives, never as the dash leaves. Every dash skill, a
/// person's or a creature's, comes through here, so none needs saying separately.
///
/// <para><b>Checked again on arrival.</b> The caster must still be here, still the same life, still in the same map and
/// standing; otherwise the strike never lands. The target is judged by the ordinary burst, as it would have been at
/// the start.</para>
///
/// <para><b>A dash that never arrives FAILS.</b> The caster must actually stand where the dash sent it
/// (<see cref="ArriveSlack"/>). One stopped on the way — a wall, a creature, any collision — is waited for no longer
/// than the skill's whole cast range takes at the dash's speed, plus, for a person, the time their own machine's
/// reports take to reach the server (their round trip and two report beats); then the strike is dropped.</para>
///
/// <para><b>Why the timing matches every screen.</b> The server holds the strike for the trip's length from the moment
/// it sends the trip. The owner's machine starts the trip half a round trip later, and each blow reaches the screens
/// half a round trip after it lands — so the number and the arrival are drawn together.</para>
///
/// <para>Server only. Nothing allocates per dash once warm: entries are pooled, each with its own effect list (the list
/// a cast hands in can be a runner's scratch list, refilled on its next beat).</para>
/// </summary>
public static class DashLandings
{
    sealed class Held
    {
        public ICombatUnit caster;
        public int casterLife;
        public string map;
        public double landsAt, giveUpAt;
        public Vector3 point, arriveAt;
        public float radius;
        public SkillScope scope;
        public SkillTargets targets;
        public readonly List<SkillEffect> effects = new List<SkillEffect>(8);
        public Transform singleTarget;
        public string symbol, skillName, skillId;
        public Vector3? aimPoint;
        public StackVars vars;
        public bool efficient;
        public ulong chain;

        public void Clear()
        {
            caster = null; map = null; effects.Clear(); singleTarget = null;
            symbol = null; skillName = null; skillId = null;
        }
    }

    // How near where the dash sent it the caster must stand to count as arrived, in metres.
    const float ArriveSlack = 0.5f;

    static readonly List<Held> live = new List<Held>(32);
    static readonly Stack<Held> spare = new Stack<Held>(32);

    /// <summary>Hold a binding's target effects until <paramref name="seconds"/> from now, when the caster's dash
    /// arrives at <paramref name="arriveAt"/>, giving up <paramref name="giveUpSeconds"/> from now if it has not. False
    /// when it cannot be held (not the server, or no caster) — the caller then lands it at once.</summary>
    public static bool Hold(float seconds, float giveUpSeconds, Vector3 arriveAt, Vector3 point, float radius, SkillScope scope, SkillTargets targets,
                            List<SkillEffect> effects, Transform singleTarget, string symbol, string skillName,
                            string skillId, Vector3? aimPoint, ICombatUnit caster, StackVars vars, bool efficient)
    {
        if (!NetworkServer.active || caster == null || effects == null || seconds <= 0f) return false;
        var h = spare.Count > 0 ? spare.Pop() : new Held();
        h.caster = caster;
        h.casterLife = caster.States != null ? caster.States.LifeCount : 0;
        h.map = caster.Map;
        h.landsAt = NetworkTime.time + seconds;
        h.giveUpAt = NetworkTime.time + Mathf.Max(seconds, giveUpSeconds) + ReportLag(caster);
        h.arriveAt = arriveAt;
        h.point = point;
        h.radius = radius;
        h.scope = scope;
        h.targets = targets;
        h.effects.AddRange(effects);
        h.singleTarget = singleTarget;
        h.symbol = symbol;
        h.skillName = skillName;
        h.skillId = skillId;
        h.aimPoint = aimPoint;
        h.vars = vars;
        h.efficient = efficient;
        h.chain = PassiveEvents.CurrentChain;
        live.Add(h);
        return true;
    }

    /// <summary>Land every held strike whose dash has arrived. Once a server frame.</summary>
    public static void Tick()
    {
        if (live.Count == 0) return;
        double now = NetworkTime.time;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            if (i >= live.Count) continue;
            var h = live[i];
            if (now < h.landsAt) continue;
            bool arrived = Arrived(h);
            if (!arrived && now < h.giveUpAt) continue;
            live.RemoveAt(i);
            if (arrived) Land(h);
            else Failed(h);
            h.Clear();
            spare.Push(h);
        }
    }

    // Standing where the dash sent it, on the ground plane. A caster with no body cannot be measured and is believed.
    static bool Arrived(Held h)
    {
        var body = h.caster != null ? h.caster.Body : null;
        if (body == null) return true;
        Vector3 gap = body.position - h.arriveAt; gap.y = 0f;
        return gap.sqrMagnitude <= ArriveSlack * ArriveSlack;
    }

    // How late a person's own machine tells the server where they stand: one round trip (the order to dash going out,
    // the report coming back) and two report beats. A creature moves on the server and is never late.
    static double ReportLag(ICombatUnit caster)
    {
        if (!(caster is Character person)) return 0.0;
        var conn = GameNetwork.ShellFor(person)?.connectionToClient;
        return (conn != null ? conn.rtt : 0.0) + 2.0 * NetPlayer.ReportInterval;
    }

    static void Failed(Held h)
    {
        if (!GameLog.On(LogCat.Cast)) return;
        GameLog.Info(LogCat.Cast, string.Concat(SpawnIds.MapOf(h.caster), " ", SpawnIds.Label(h.caster), " ", h.skillId,
                                                " failed — stopped short of where the dash sent it"));
    }

    static void Land(Held h)
    {
        var c = h.caster;
        if (PlayerUnits.Gone(c) || c.IsDead) return;
        if (c.States != null && c.States.LifeCount != h.casterLife) return;
        if (c.Map != h.map) return;

        var prev = PassiveEvents.Push(h.chain, h.skillId);
        try
        {
            SkillEffectApplier.ApplyBurst(h.point, h.radius, h.scope, h.targets, h.effects, h.singleTarget,
                                          h.symbol, h.skillName, h.skillId, aimPoint: h.aimPoint, caster: c,
                                          vars: h.vars, efficient: h.efficient, casterPass: false);
        }
        finally { PassiveEvents.Pop(prev); }
    }
}
