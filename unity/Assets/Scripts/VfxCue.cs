using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ONE choke point for a visual that answers a gameplay EVENT (a "verb" or a status beat) — the status/verb
/// analog of what <see cref="SkillRunner"/> is for skills. Gameplay code that heals, hits, dies, or applies an ailment
/// calls <see cref="Fire"/> (a one-shot puff) or <see cref="Begin"/> (a lasting aura it holds and later ends); it never
/// touches <see cref="VfxDirector"/> or <see cref="Vfx"/> directly. That keeps the event→look wiring in a single table
/// (<see cref="VfxCueLibrary"/>, edited in the Vfx Cue Composer) instead of scattered spawn calls across the
/// combat/status code — so re-skinning "what a heal looks like" is a one-asset edit.
///
/// <para><b>THE SERVER SAYS, EVERY SCREEN DRAWS — one rule, no exceptions.</b> The server decides every event, so it
/// is the only machine that fires a cue — and firing one means ANNOUNCING it, by name, to each machine watching the
/// unit. It draws nothing. A player's machine fires nothing of its own; it draws only what it is told
/// (<see cref="PlayFromServer"/>), cues on its own body included, from its own copy of the cue table.</para>
///
/// PERFORMANCE: called per-hit / per-DoT-tick, not per-frame. Alloc-free — the binding lookup is an array index
/// (<see cref="VfxCueCatalog"/>), the context is a struct built on the stack, and the null onTick means no
/// capturing closure. The underlying <see cref="VfxDirector"/> / <see cref="Vfx"/> pools are reused.
/// </summary>
public static class VfxCue
{
    /// <summary>SERVER: announce a cue's one-shot look on <paramref name="actor"/> to every machine watching it, which
    /// draws it with <paramref name="actor"/> as the sequence's Origin and the optional <paramref name="target"/> as its
    /// Target. Fire-and-forget: nothing to hold. No-op off the server, for a cue with no look and no word (nothing for
    /// anyone to draw, so nothing is sent), or a null actor.</summary>
    public static void Fire(VfxCueId id, Transform actor, Transform target = null)
    {
        if (actor == null || !GameServer.IsServer || !(Bound(id) || CueWords.Has(id))) return;
        GameNetwork.AnnounceCue(actor, target, id, 0);
    }

    static bool Bound(VfxCueId id, WeaponType weapon = WeaponType.None)
    {
        var b = VfxCueCatalog.Get(id, weapon);
        return b != null && b.vfx != null;
    }

    /// <summary>WHICH hit cue a landed blow plays on the unit it landed on — the one rule for every unit, so a player
    /// and a creature taking the same blow show the same thing. Magic, and any strike that rolled no accuracy (a skill
    /// strike, <paramref name="attackerHit"/> −1), is a skill hit; a swing that rolled accuracy is a normal or a
    /// critical attack.</summary>
    public static VfxCueId HitCue(bool isMagic, bool crit, int attackerHit)
        => isMagic || attackerHit < 0 ? VfxCueId.OnHitBySkill
         : crit ? VfxCueId.OnHitByCriticalAttack : VfxCueId.OnHitByNormalAttack;

    /// <summary>The cue a sub-stat effect plays when it BEGINS on a unit: a movement-speed debuff is a Slow, a
    /// movement-speed buff is a Haste, an accuracy debuff is a Blind. Judged per effect, never by the unit's total, so
    /// a slow landing on a hasted unit still shows. <paramref name="started"/> is false for a refresh or a stack added
    /// to an effect already running, which plays nothing — each plays once, when it begins.</summary>
    public static bool SubStatStartCue(SubStat stat, bool started, float amount, out VfxCueId cue)
    {
        cue = default;
        if (!started) return false;
        switch (stat)
        {
            case SubStat.MovementSpeed:
                if (amount < 0f) { cue = VfxCueId.OnSlow;  return true; }
                if (amount > 0f) { cue = VfxCueId.OnHaste; return true; }
                return false;
            case SubStat.Accuracy:
                if (amount < 0f) { cue = VfxCueId.OnBlind; return true; }
                return false;
            default:
                return false;
        }
    }

    /// <summary>A lasting cue the server announced on a unit, by the name both sides call it — what its holder keeps
    /// so it can say when it is over. The default value is "nothing running".</summary>
    public struct Aura
    {
        internal uint on;
        internal int key;
        public bool Running => key != 0;
    }

    /// <summary>SERVER: announce a lasting cue that TRACKS <paramref name="actor"/> (a status aura), and return its
    /// name — the caller MUST hand it to <see cref="End"/> when the state ends (e.g. the ailment expires), which tells
    /// every watching machine to take it off the body it rides. Not running (nothing to end) off the server, for an
    /// unbound cue, a null actor, or a unit nobody can watch. <paramref name="weapon"/> picks that weapon's own
    /// version of the cue when it has one (the Bow's cast); every screen is told which, so all draw the same.</summary>
    public static Aura Begin(VfxCueId id, Transform actor, WeaponType weapon = WeaponType.None)
    {
        if (actor == null || !GameServer.IsServer) return default;
        // NO LOOK, BUT A WORD: said once, as a one-shot — there is no glow for anybody to take off later.
        if (!Bound(id, weapon)) { if (CueWords.Has(id)) GameNetwork.AnnounceCue(actor, actor, id, 0); return default; }

        // NAMED, so the ending can be said as plainly as the beginning. A one-shot needs no name — it is over by
        // the time the message lands — but an aura has to be taken OFF a body later, and a watching machine can
        // only do that if both sides call it the same thing.
        int key = NextAuraKey();
        uint on = GameNetwork.AnnounceCue(actor, actor, id, key, weapon);
        return on != 0u ? new Aura { on = on, key = key } : default;
    }

    /// <summary>SERVER: say <paramref name="aura"/> is over, and forget it — safe on one that is not running.</summary>
    public static void End(ref Aura aura)
    {
        if (!aura.Running) return;
        GameNetwork.AnnounceCueStopped(aura.on, aura.key);
        aura = default;
    }

    // Aura names, handed out by the machine that owns the world. Starts at 1 because 0 means "a one-shot, there is
    // nothing to stop". Wraps back to 1 rather than to 0 — a session would have to start four billion auras.
    static int nextAuraKey = 1;

    static int NextAuraKey()
    {
        if (nextAuraKey == int.MaxValue) nextAuraKey = 1;
        return nextAuraKey++;
    }

    // ---- the watching side: draw what the server said happened -------------------------------------------

    // Auras running on THIS machine that the server named, so the "it ended" that arrives later can find the right
    // one. Keyed by the unit AND the aura's name together: two people can be poisoned at once, and each one's glow
    // has to come off its own body. Event-driven (an ailment starting or ending), never per frame.
    static readonly Dictionary<long, ToldAura> toldAuras = new Dictionary<long, ToldAura>(16);

    /// <summary>An aura running here because the world said so, and WHICH PLAY of that runner it is. The runner
    /// comes out of a pool: if it finishes on its own before the "it is over" arrives, it is handed straight to the
    /// next effect that needs one, and stopping it then would tear down something else entirely. The number says
    /// whether the runner is still on the play we started.</summary>
    struct ToldAura { public VfxSequenceRunner runner; public int generation; }

    static long AuraSlot(uint onNetId, int key) => ((long)onNetId << 32) | (uint)key;

    /// <summary>Play a cue the SERVER said happened, on the body THIS machine draws for that unit. The only door a
    /// joined client draws a cue through — it never decides one for itself, so nothing here announces anything and
    /// there is no way for a cue to echo back and forth.</summary>
    public static void PlayFromServer(VfxCueId id, Transform actor, Transform target, int auraKey, uint onNetId,
                                      WeaponType weapon = WeaponType.None)
    {
        if (actor == null) return;
        var b = VfxCueCatalog.Get(id, weapon);
        if (b == null || b.vfx == null) return;

        if (auraKey == 0) { Draw(b, actor, target); return; }

        // A LASTING ONE. A refreshed ailment gets a NEW name and the world stops the old one first, in that order,
        // so there is nothing to clear away here — the ending of the previous glow has already arrived.
        var ctx = BuildCtx(b, actor, actor);
        ctx.follow = actor;
        ctx.persistent = true;
        var runner = VfxDirector.Play(b.vfx, ctx, null);
        if (runner != null)
            toldAuras[AuraSlot(onNetId, auraKey)] = new ToldAura { runner = runner, generation = runner.Generation };
    }

    /// <summary>Take off an aura the server has said is over.</summary>
    public static void StopFromServer(uint onNetId, int auraKey)
    {
        long slot = AuraSlot(onNetId, auraKey);
        if (!toldAuras.TryGetValue(slot, out var held)) return;
        toldAuras.Remove(slot);
        StopIfStillOurs(held);
    }

    // Stop a runner only while it is still on the play we started it for — see ToldAura.
    static void StopIfStillOurs(ToldAura held)
    {
        if (held.runner != null && held.runner.Generation == held.generation) held.runner.Stop();
    }

    /// <summary>Every aura this machine was told about, taken off at once — for a body that has gone away, or a
    /// world this machine has left. An aura is held by name rather than by the body it rides, so without this a
    /// person who walked out of view mid-poison would leave their glow behind them forever.</summary>
    public static void ForgetAurasOn(uint onNetId)
    {
        if (toldAuras.Count == 0) return;
        forgetting.Clear();
        foreach (var pair in toldAuras)
            if ((uint)(pair.Key >> 32) == onNetId) forgetting.Add(pair.Key);
        for (int i = 0; i < forgetting.Count; i++)
        {
            if (toldAuras.TryGetValue(forgetting[i], out var held)) StopIfStillOurs(held);
            toldAuras.Remove(forgetting[i]);
        }
    }

    // Reused so taking a body's auras off allocates nothing — it happens whenever anybody walks out of view.
    static readonly List<long> forgetting = new List<long>(8);

    // ---- drawing ------------------------------------------------------------------------------------------

    static void Draw(VfxCueBinding b, Transform actor, Transform target)
    {
        var ctx = BuildCtx(b, actor, target);
        ctx.follow = actor;   // let one-shot cues use Attach steps too (they auto-stop when the sequence ends)
        VfxDirector.Play(b.vfx, ctx, null);
    }

    // Feed the two reference points; the sequence's own steps decide which one each piece spawns at. The Composer
    // authors from the ground, so we raycast the floor under each point and anchor there (matches the ruler standing
    // on the floor). Robust to the unit's pivot — units are feet-pivoted now, but this still finds true ground either
    // way. Struct return → no allocation.
    static VfxContext BuildCtx(VfxCueBinding b, Transform actor, Transform target)
    {
        Vector3 a = actor.position; a.y = GroundY(a);
        Vector3 t = target != null ? target.position : actor.position; t.y = GroundY(t);
        return new VfxContext
        {
            origin = a + b.offset,
            target = t + b.offset,
            effectPoint = t + b.offset,   // a cue has no aim stage; the target spot is the only point it knows
            follow = null,
            targetUnit = target,
            scaleMult = b.scale <= 0f ? 1f : b.scale,
            groundY = a.y,
        };
    }

    static int groundMask = -1;   // cached Ground-layer mask (-1 unresolved, 0 = no Ground layer)

    // World Y of the floor under a point. Single non-alloc raycast (cues are event-driven, not per-frame). Falls
    // back to the point's own Y if there's no Ground layer / no hit, so an off-floor actor isn't yanked down.
    static float GroundY(Vector3 p)
    {
        if (groundMask == -1) { int l = LayerMask.NameToLayer("Ground"); groundMask = l >= 0 ? 1 << l : 0; }
        if (groundMask != 0 && Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 12f, groundMask))
            return hit.point.y;
        return p.y;
    }
}

/// <summary>
/// The WORD some events put over the unit they happen to — "Poison!", "Stun!" — drawn by every
/// screen watching, from the event the server announced (<see cref="VfxCue"/>). One table, so the server knows which
/// events are worth announcing for their word alone, and every screen says the same thing.
/// </summary>
public static class CueWords
{
    /// <summary>Does <paramref name="id"/> put a word up on anybody?</summary>
    public static bool Has(VfxCueId id) => Word(id, true, out _, out _);

    /// <summary>A WATCHING MACHINE: put <paramref name="id"/>'s word over <paramref name="on"/>, if it has one for this
    /// kind of unit.</summary>
    public static void Show(VfxCueId id, Transform on, bool isCreature)
    {
        if (on != null && Word(id, isCreature, out var word, out var colour)) DamagePopup.SpawnText(on, word, colour);
    }

    static bool Word(VfxCueId id, bool isCreature, out string word, out Color colour)
    {
        switch (id)
        {
            case VfxCueId.OnPoisonStart: word = "Poison!";   colour = new Color(0.55f, 0.85f, 0.35f); return true;
            case VfxCueId.OnBleedStart:  word = "Bleed!";    colour = new Color(1f, 0.3f, 0.3f);       return true;
            case VfxCueId.OnBurnStart:   word = "Burn!";     colour = new Color(1f, 0.55f, 0.25f);     return true;
            case VfxCueId.OnStun:        word = "Stun!";     colour = new Color(1f, 0.9f, 0.3f);       return true;
            case VfxCueId.OnFreeze:      word = "Frozen!";   colour = new Color(0.6f, 0.85f, 1f);      return true;
            case VfxCueId.OnSilence:     word = "Silenced!"; colour = new Color(0.7f, 0.5f, 1f);       return true;
            default: word = null; colour = default; return false;
        }
    }
}
