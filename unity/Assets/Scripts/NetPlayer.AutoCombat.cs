using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// AUTO-COMBAT, THE SERVER'S HALF — and the one owner of whether it is ON.
///
/// <para><b>Who decides what.</b> The player's machine asks to switch it on and steers its own legs while it is on (the
/// movement exception in CLAUDE.md): it picks the nearest creature, walks there, swings, and presses the four
/// auto-skills when each is ready — every press is an ordinary cast this server judges like any other. Everything else
/// is decided HERE: whether it may be on at all (never in a town, Free Roam only in a field or a dungeon), what the
/// settings hold, what a kill's loot puts straight in the bag (<see cref="Rewards"/>), when a potion is drunk, and when
/// it switches itself off — a door refused, a trip taken, a death.</para>
///
/// <para><b>The settings are a row of their own</b> (accounts/sql/013_character_auto_combat.sql), written only when the
/// player changes one, exactly as the hotbar's row is. Whether it is on is never written: a sign-in always starts off.</para>
/// </summary>
public partial class NetPlayer
{
    // ---- on / off ---------------------------------------------------------------------------------------------

    bool autoCombatOn;

    /// <summary>SERVER: is auto-combat on for this person right now? The one answer — their machine only ever holds
    /// what this side last told it.</summary>
    public bool AutoCombatOn => autoCombatOn;

    [Command]
    void CmdAutoCombatOn(bool on)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null) return;
        if (!on) { SetAutoCombat(false, ""); return; }

        string why = AutoCombatMath.WhyItMayNotStart(MapKindHere(), c.AutoCombat.Hunt, c.IsDead);
        if (why != null)
        {
            if (GameLog.On(LogCat.Server)) LogDid(c, "was refused auto-combat — " + why);
            PushAutoCombatToOwner(why);
            return;
        }
        SetAutoCombat(true, "");
    }

    /// <summary>SERVER: switch auto-combat on or off and tell the owner, with <paramref name="why"/> said to them when
    /// it is not empty. Telling them is unconditional, so a refused or repeated ask is still answered.</summary>
    void SetAutoCombat(bool on, string why)
    {
        if (autoCombatOn != on)
        {
            autoCombatOn = on;
            if (GameLog.On(LogCat.Server))
                LogDid(ServerCharacter, on ? "switched auto-combat on (" + ServerCharacter?.AutoCombat.Hunt + ")"
                                           : "switched auto-combat off" + (string.IsNullOrEmpty(why) ? "" : " — " + why));
        }
        PushAutoCombatToOwner(why);
    }

    /// <summary>SERVER: switch it off, saying why — a trip, a door, a fall. Does nothing when it is already off.</summary>
    public void StopAutoCombat(string why)
    {
        if (!isServer || !autoCombatOn) return;
        SetAutoCombat(false, why);
    }

    // The kind of the map this person is standing in, from the world layout — a map it does not list counts as a field.
    MapKind MapKindHere()
    {
        var atlas = WorldRoutes.Atlas;
        var entry = atlas != null ? atlas.Find(mapName) : null;
        return MapKinds.Of(entry != null ? entry.type : null);
    }

    // ---- the settings -----------------------------------------------------------------------------------------

    [Command]
    void CmdAutoCombatSettings(string json)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        // The whole document is a few hundred characters; anything far past that is not a settings document.
        if (c == null || json == null || json.Length > 4096) return;

        // CLAMPED AND TRIMMED HERE, whatever was sent: every number inside its range, every slot a skill this character
        // may press (never an item, never gear), no skill twice.
        var s = AutoCombatMath.Clamped(AutoCombatSettings.FromJson(json));
        c.KeepOnlyAutoSkills(s.slots);
        bool slotsMoved = !SameSlots(c.AutoCombat.slots, s.slots);
        c.AutoCombat = s;
        // A skill only auto-combat presses still needs a runner here to be cast with — the same rebuild the bar asks for.
        if (slotsMoved) c.RefreshUsableSkills();
        SaveAutoCombatSoon();

        // A HUNT MODE THAT MAY NOT RUN HERE switches it off rather than running anyway.
        string why = autoCombatOn ? AutoCombatMath.WhyItMayNotStart(MapKindHere(), s.Hunt, c.IsDead) : null;
        if (why != null) SetAutoCombat(false, why);
        else PushAutoCombatToOwner("");
    }

    static bool SameSlots(string[] a, string[] b)
    {
        if (a == null || b == null) return a == b;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // ---- what the owner is told -------------------------------------------------------------------------------

    /// <summary>SERVER: tell this person's machine whether auto-combat is on and what its settings are, with
    /// <paramref name="why"/> said to them when it is not empty.</summary>
    public void PushAutoCombatToOwner(string why)
    {
        if (!isServer || connectionToClient == null) return;
        var c = ServerCharacter;
        if (c == null) return;
        TargetAutoCombat(connectionToClient, autoCombatOn, c.AutoCombat.ToJson(), why ?? "");
    }

    [TargetRpc]
    void TargetAutoCombat(NetworkConnectionToClient target, bool on, string settingsJson, string why)
        => AutoCombatClient.FromServer(on, AutoCombatSettings.FromJson(settingsJson), why);

    /// <summary>SERVER: a Fly Wing just set this person down somewhere else on the map, and auto-combat stays on — its
    /// Radius spot moves with them.</summary>
    public void AutoCombatLandedElsewhere()
    {
        if (!isServer || !autoCombatOn || connectionToClient == null) return;
        TargetAutoCombatLanded(connectionToClient);
    }

    [TargetRpc]
    void TargetAutoCombatLanded(NetworkConnectionToClient target) => AutoCombatClient.LandedElsewhere();

    // ---- asked from the player's machine ----------------------------------------------------------------------

    public void AskAutoCombat(bool on) => CmdAutoCombatOn(on);
    public void AskAutoCombatSettings(AutoCombatSettings s) { if (s != null) CmdAutoCombatSettings(s.ToJson()); }

    // ---- the settings' own row --------------------------------------------------------------------------------
    // Written ONLY when they changed, on the same beat as the bar's — a player sliding a number about is one write.

    bool needsAutoCombatSaving;
    float nextAutoCombatSaveAt;
    string autoCombatWritten;

    /// <summary>SERVER: the settings changed and should be written to their row before long.</summary>
    public void SaveAutoCombatSoon()
    {
        if (!isServer) return;
        if (!needsAutoCombatSaving) nextAutoCombatSaveAt = Time.time + SecondsBetweenSaves;
        needsAutoCombatSaving = true;
    }

    /// <summary>SERVER: the settings as they stand are what the store already holds — set on arrival, so unchanged
    /// settings are never written back.</summary>
    public void AutoCombatAlreadyStored()
    {
        var c = ServerCharacter;
        if (c != null) autoCombatWritten = c.AutoCombat.ToJson();
    }

    // Write the settings now if they changed since they were last written; answers yes at once when they did not —
    // after whatever is still in the air has landed, because whoever takes the character over reads the row as it stands.
    void SaveAutoCombatNow(Action<bool> whenDone)
    {
        needsAutoCombatSaving = false;
        var c = ServerCharacter;
        if (c == null || string.IsNullOrEmpty(ServerCharacterId)) { whenDone?.Invoke(true); return; }
        string json = c.AutoCombat.ToJson();
        if (json == autoCombatWritten) { SaveOutbox.FlushAutoCombat(ServerCharacterId, whenDone); return; }
        autoCombatWritten = json;
        // The door's body: { settings: { … }, handover: n } — see charserver/CharacterRows.cs.
        string body = string.Concat("{\"settings\":", json, ",\"handover\":", ServerHandover.ToString(), "}");
        SaveOutbox.SubmitAutoCombat(ServerCharacterId, body, whenDone);
    }

    // ---- every server frame -----------------------------------------------------------------------------------

    float lastHealthDrinkAt = -999f, lastManaDrinkAt = -999f;

    void AutoCombatServerTick()
    {
        if (needsAutoCombatSaving && Time.time >= nextAutoCombatSaveAt) SaveAutoCombatNow(null);
        if (!autoCombatOn) return;
        var c = ServerCharacter;
        if (c == null) return;
        if (c.IsDead) { SetAutoCombat(false, "Auto-combat stopped — you fell."); return; }
        DrinkIfLow(c);
    }

    // AUTO-POTION. At most one drink a second of each kind, and a draught that works over time is never drunk while
    // one of its kind is still running — a second mana draught would only waste the first. Throttled on every LOOK,
    // not only on a drink, so a bag with nothing to drink is searched once a second, never every frame.
    void DrinkIfLow(Character c)
    {
        var s = c.AutoCombat;
        float now = Time.time;
        if (AutoCombatMath.Below(c.CurrentHP, c.MaxHP, s.hpPotionBelowPercent)
            && AutoCombatMath.MayDrinkAgain(now, lastHealthDrinkAt))
        {
            lastHealthDrinkAt = now;
            Drink(c, mana: false);
        }
        if (AutoCombatMath.Below(c.CurrentMP, c.MaxMP, s.mpPotionBelowPercent)
            && AutoCombatMath.MayDrinkAgain(now, lastManaDrinkAt))
        {
            lastManaDrinkAt = now;
            Drink(c, mana: true);
        }
    }

    // The first thing in the bag, in bag order, that restores the resource asked for — so a player decides which potion
    // goes first by where it sits. Used through the same door a pressed item is.
    void Drink(Character c, bool mana)
    {
        var bag = c.Bag;
        for (int i = 0; i < bag.Count; i++)
        {
            var def = bag[i] != null ? bag[i].def : null;
            if (def == null || !def.IsUsable) continue;
            var what = WhatItRestores(def.useSkillId);
            bool restores = mana ? what.mana : what.health;
            if (!restores) continue;
            // A DRAUGHT ALREADY RUNNING is not stacked with a second.
            bool overTime = mana ? what.manaOverTime : what.healthOverTime;
            if (overTime && c.States.SubStatBonus(mana ? SubStat.MPRegen : SubStat.HPRegen) > 0f) continue;
            if (GameNetwork.UseItemOnServer(c, this, i))
            {
                if (GameLog.On(LogCat.Server)) LogDid(c, "drank " + def.displayName + " (auto-combat)");
                return;
            }
        }
    }

    struct Restores { public bool health, mana, healthOverTime, manaOverTime; }

    // What an item's skill gives back, read off its effects once per skill and kept: an instant heal restores health; a
    // timed health or mana regeneration restores that one over time. The skill table never changes while a server runs.
    static readonly Dictionary<string, Restores> restoresBySkill = new Dictionary<string, Restores>();

    static Restores WhatItRestores(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return default;
        if (restoresBySkill.TryGetValue(skillId, out var known)) return known;
        var r = new Restores();
        var def = SkillCatalog.Get(skillId);
        if (def != null && def.bindings != null)
            for (int b = 0; b < def.bindings.Count; b++)
            {
                var fx = def.bindings[b]?.effects;
                if (fx == null) continue;
                for (int i = 0; i < fx.Count; i++)
                {
                    var e = fx[i];
                    if (e == null) continue;
                    if (e.kind == SkillEffectKind.Heal) r.health = true;
                    else if (e.kind == SkillEffectKind.SubStatBuff && e.subStat == SubStat.HPRegen) { r.health = true; r.healthOverTime = true; }
                    else if (e.kind == SkillEffectKind.SubStatBuff && e.subStat == SubStat.MPRegen) { r.mana = true; r.manaOverTime = true; }
                }
            }
        restoresBySkill[skillId] = r;
        return r;
    }
}
