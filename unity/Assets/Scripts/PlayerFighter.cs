using System;
using UnityEngine;

/// <summary>
/// THE SERVER'S COPY OF ONE PERSON'S CHARACTER — the thing it rolls their blows with, and the thing a creature
/// actually hurts when it bites them.
///
/// <para><b>Why this had to exist before anything else.</b> A character used to be a single global handle, which is
/// a fact about a screen: one person is at this keyboard. A server holds as many people as have signed in, so it
/// had no way to say whose stats a blow should use — and rather than guess, it refused every swing. Nothing in
/// combat could work until each connection had a character of its own, which is what this builds.</para>
///
/// <para><b>Where the numbers come from.</b> The account service, read with the player's OWN sign-in proof, which
/// they hand over when they join. Nothing about the fighter is taken from what the client says about itself: a
/// person could otherwise arrive claiming to be level 99. The proof also settles WHO they are — the service only
/// answers for characters belonging to the account that proof was issued to — so somebody typing another player's
/// name no longer gets that player's character.</para>
///
/// <para><b>It has no body of its own.</b> The body is the shell the server already keeps for that connection, which
/// is where their reported position lives. Building a second one would be a rig, an animator and a per-frame blend
/// on a machine with no screen — exactly what the shells exist to avoid.</para>
/// </summary>
public static class PlayerFighter
{
    /// <summary>Build the fighter for <paramref name="shell"/> and hand it over as soon as it is ready. Server only.
    ///
    /// <para>The fighter EXISTS immediately, at the character's starting numbers, and is corrected the moment their
    /// real progress arrives. Waiting for the answer instead would leave a person standing in the world unable to
    /// hit anything or be hit for as long as a network round trip takes, which reads as a broken game rather than
    /// as a loading pause.</para></summary>
    /// <summary>Build the fighter for somebody the character server ANNOUNCED, from the character it sent with
    /// the announcement. Nothing is fetched — this server never asks the character store for anything.</summary>
    public static bool BuildFrom(NetPlayer shell, MapServerLink.Expected announced)
    {
        if (announced == null) return false;
        return Build(shell, announced.characterId, announced.accountId, announced.progress, announced.hotbar,
                     announced.autoCombat, announced.emblem, announced.quests, announced.isGm, announced.map);
    }

    static bool Build(NetPlayer shell, string characterId, string accountId, SaveData pushed, string[] hotbar,
                      AutoCombatSettings autoCombat, EmblemWire emblem, QuestRowWire[] quests, bool isGm, string sentToMap)
    {
        if (shell == null || !GameServer.IsServer) return false;

        // ONE CHARACTER, ONE PLACE. Two connections playing the same character would each get a live copy of it,
        // both loaded from the same saved progress and both writing back over each other — so the second one is
        // turned away rather than allowed in to fight itself. MY DECISION, and the conservative one: the person
        // already in the world keeps their place. A connection that really has dropped is cleared out by the
        // wire's own timeout, which frees the name a moment later.
        if (!string.IsNullOrEmpty(characterId))
        {
            if (playing.ContainsKey(characterId))
            {
                GameLog.Warn(LogCat.Server, "'" + shell.playerName + "' is already in this world — the second "
                                 + "connection claiming that character was turned away.");
                return false;
            }
            playing[characterId] = shell;
            shell.ServerCharacterId = characterId;
            // AND WHOSE ACCOUNT IT IS. Needed the moment they walk through a door into a zone another process
            // holds: the character server mints their one-time ticket against the account, not the character.
            shell.ServerAccountId = accountId;
        }

        var host = new GameObject("Fighter_" + shell.playerName) { hideFlags = HideFlags.DontSaveInEditor };
        host.transform.SetParent(shell.transform, false);

        // ORDER MATTERS. The status host reads the unit's state store when it is told who it belongs to, and the
        // character is what owns that store — so the character goes on first and the two are introduced after.
        var character = host.AddComponent<Character>();
        var status = host.AddComponent<StatusController>();
        status.BindUnit(character);
        character.BindStatusHost(status);
        character.BindBody(shell.transform);
        character.BindServerIdentity(shell.netIdentity);

        shell.BindCombatUnitOnServer(character);
        PlayerUnits.Enter(character);
        GiveThemTheirSkills(host, character);
        // …and from now on their own machine is told what is running on them. Their skills are decided here, so the
        // windows, the charges and the piles exist here; without this a person casts a brace and sees nothing.
        shell.ReportStatesOf(character.States);

        // WHAT THE CHARACTER SERVER SENT, laid straight onto them. It arrived before this person did.
        shell.ServerIsGm = isGm;
        if (pushed != null && pushed.version > 0)
        {
            character.RestoreSave(pushed);
            // AND WHERE THE CHARACTER SERVER SENT THEM. Their save says which map they were in, and that is the
            // map this server was asked to hold for them — so it is known now, not a second later when their own
            // machine gets round to saying it.
            shell.SetMapOnArrival(pushed, sentToMap);
            GameLog.Info(LogCat.Server, "" + shell.playerName + " is level " + character.Level + " with "
                      + character.Bag.Count + " stack(s) — their character came with them.");
        }
        else
        {
            // AND THE MAP THEY WERE SENT TO, which for a character never played is the one the character server named —
            // the starting map. Their save names none.
            shell.SetMapOnArrival(pushed, sentToMap);
            // NEVER PLAYED, so their starter goods are given HERE — by the thing that owns bags now. It used to
            // be their own machine that decided this, and the two raced: whichever landed last won, so a new
            // player either saw a bag the server did not have (every use silently ignored) or none at all.
            character.GiveStarterKit();
            // THE STATS CHOSEN AT CREATION, checked here against the one rule before any of them is used. A spread
            // the hexagon cannot make leaves the character on the default start (all 1, the full starting pool).
            if (pushed != null && pushed.primary != null && !character.StartWithCreationStats(pushed.primary))
                GameLog.Warn(LogCat.Server, "" + shell.playerName + " came with creation stats the hexagon cannot make ("
                          + string.Join(",", pushed.primary) + ") — started on the default spread.");
            GameLog.Info(LogCat.Server, "" + shell.playerName + " has never been played — given "
                      + character.Bag.Count + " starter stack(s), stats " + string.Join(",", character.CaptureStatsForOwner())
                      + ", " + character.StatPoints + " point(s) unspent.");
            // AND THEIR OWN MACHINE IS TOLD. It builds a never-played character fresh (all 1, the full pool) because
            // there is no save to read yet, so the stats laid on above never reached their screen until they
            // happened to spend a point.
            shell.PushStatsToOwner();
            shell.SaveSoon();
        }
        // THE VISIT'S TALLY starts here, or carries on when this is a door into another map this server holds.
        PlaySession.Arrived(character, shell.ServerCharacterId, shell.playerName);
        // AND THE PER-HOUR METER starts with them, so the walk to the first creature counts as time spent.
        character.KillCombo.StartMeter(KillCombo.Now);

        // THEIR BAR, from its own row — after the save, because what may stay on it is held to the skills they have
        // learned and the things they carry. A character whose bar has never been written to that row carries the
        // old one inside its save (before v18): it is laid into the new rows once and then written to its row.
        if (hotbar != null && hotbar.Length > 0)
        {
            var slots = (string[])hotbar.Clone();
            character.KeepOnlyBindable(slots);
            character.Hotbar.Restore(slots);
            shell.HotbarAlreadyStored();
        }
        else if (pushed != null && pushed.hotbarBindings != null && pushed.hotbarBindings.Length > 0)
        {
            character.Hotbar.RestoreLegacy(pushed.hotbarBindings);
            var slots = character.Hotbar.Capture();
            character.KeepOnlyBindable(slots);
            character.Hotbar.Restore(slots);
            shell.SaveHotbarSoon();
        }

        // THEIR AUTO-COMBAT SETTINGS, from their own row — after the save for the same reason as the bar: a slot may
        // keep only a skill they may press. Whether it is ON is never stored, so every arrival starts with it off.
        // A v1 row (one set of four slots for every weapon) is carried here to each skill's own weapon, and written back.
        bool carriedForward = autoCombat != null && autoCombat.v < AutoCombatSettings.Version;
        var settings = AutoCombatMath.Clamped(autoCombat, Character.WeaponOfSkillFn);
        string storedAs = settings.ToJson();
        character.KeepOnlyAutoSkills(settings);
        character.AutoCombat = settings;
        // Stored exactly as it now stands only if nothing had to be emptied or carried forward; otherwise it is written.
        if (!carriedForward && settings.ToJson() == storedAs) shell.AutoCombatAlreadyStored();
        else shell.SaveAutoCombatSoon();

        // THEIR CAPE EMBLEM, from its own row: worn at once, so everyone who can see them is told its id.
        shell.EmblemArrived(emblem);

        // AND THE SHARED COOLDOWN, if one was still running when they left, reaches their screen.
        if (character.Skills.SharedUltimateRemaining > 0f)
            GameNetwork.TellOwnerCooldownStarted(character, SharedCooldown.UltimateKey, character.Skills.SharedUltimateDueAtUtc);

        // AND NOW THEY GET THEIR SKILLS, NOT BEFORE. A character carries only the skills it can USE — its bar, its
        // learned passives, whatever its bag can run — and NONE of that exists until the save
        // above has been laid onto them. Bound any earlier and the answer is an empty list, which is a person
        // standing in the world who cannot cast anything at all. The binding a few lines up gives them their
        // positions; this is the one that fills them.
        character.RefreshUsableSkills();

        // AND THEIR BELONGINGS GO STRAIGHT TO THEM. The bag is the server's, so the person's own machine starts
        // empty and only ever learns what it is carrying from here.
        shell.PushBagToOwner(alsoTheBar: true, alsoTheBuyback: true);
        // …and their auto-combat settings, off.
        shell.PushAutoCombatToOwner("");

        // AND THEIR QUESTS — after the bag, because every step is judged on what they hold. A quest whose unlock
        // condition is entering the world, that this character has never had, is given now, and anything already true
        // moves it on at once.
        character.Quests.Load(quests);
        character.Quests.Unlock(QuestUnlock.EnterWorld, character.CurrentHP, character.MaxHP);
        character.Quests.Advance(character);
        // Pays anything that finished on arrival, saves, and tells them — always, since they have just arrived.
        shell.QuestsMoved();

        // ANYTHING THAT CHANGES THEM IS WORTH WRITING DOWN. Levelling, spending a point, gear — all of it now
        // happens on the server, so the server is what notices. Subscribed AFTER the load is asked for, but the
        // load itself arrives later still and calls these, which is harmless: the first save simply writes back
        // what was just read.
        character.OnLeveledUp += _ => shell.SaveSoon();
        // A tutorial can need a level, so a new level is worth a fresh quest list.
        character.OnLeveledUp += _ => shell.QuestsMayHaveMoved();
        character.OnStatsChanged += shell.SaveSoon;
        character.OnGoldChanged += _ => shell.SaveSoon();
        // AND EVERY PURSE CHANGE REACHES THEIR SCREEN, whatever made it. The bag carries the purse, and each place that
        // changed the gold used to have to remember to send it — the GM's /gold did not, so the window, the bag and the
        // status bar never moved. Sent once per frame however many changes asked (NetPlayer.PushBagToOwner).
        character.OnGoldChanged += _ => shell.PushBagToOwner();
        // A QUEST STEP JUDGED ON HEALTH moves when their health does — a drink, a heal, regeneration. The shell looks
        // only while such a step is current, so every other change of health costs one yes-or-no.
        character.OnHealthChanged += shell.HealthMoved;
        // Timed effects ride the save now, so a buff starting or ending is worth writing down too — otherwise a crash
        // would bring back whatever the last save happened to catch. SaveSoon only marks; the write is still once a beat.
        character.States.OnChanged += shell.SaveSoon;
        return true;
    }

    /// <summary>Put every composed skill on this fighter, exactly as a player's own avatar does — the SAME component,
    /// running the SAME skill body, so a skill cannot behave one way where it is decided and another where it is
    /// watched.
    ///
    /// <para>Nothing is drawn by any of it: the server plays no effect and no sound, and a skill's beats come from
    /// the effect table's measured lengths rather than from a particle that was never built. What arrives here is
    /// only the decision — the damage, the buff, the hold, the charge, the dash — against the character this server
    /// holds for that person.</para>
    ///
    /// <para>Which of them they may actually cast is a separate question, asked per request against their hotbar and
    /// their weapon. A runner existing is not permission.</para></summary>
    /// <summary>Let go of a character's name so its owner can come back. Takes the ID rather than the body,
    /// because the moment this matters most is AFTER the body has been destroyed — a person leaving. Safe to
    /// call twice.</summary>
    public static void ReleaseCharacter(string characterId)
    {
        if (!string.IsNullOrEmpty(characterId)) playing.Remove(characterId);
    }

    /// <summary>Let go of a character's name ONLY if this shell is still the one holding it.
    ///
    /// <para>Somebody walking into a map this same server holds ends up with TWO shells for a moment: the one they
    /// arrived on, and the one they walked in with, which is torn down a fraction of a second later. That old
    /// shell's teardown used to free the name outright — while the new shell was live and playing it — so the roll
    /// said nobody was playing that character and a third connection could be let in to play a second copy of it.
    /// The whole point of the roll is that this cannot happen.</para></summary>
    public static void ReleaseCharacterIfHeldBy(string characterId, NetPlayer shell)
    {
        if (string.IsNullOrEmpty(characterId)) return;
        if (playing.TryGetValue(characterId, out var held) && !ReferenceEquals(held, shell)) return;
        playing.Remove(characterId);
    }

    static void GiveThemTheirSkills(GameObject host, Character fighter)
    {
        fighter.BindSkills(host, fighter.UsableSkills());
    }

    /// <summary>The runner this fighter casts <paramref name="skillId"/> with, or null if that is not a skill.</summary>
    public static SkillRunner RunnerFor(Character fighter, string skillId)
        => fighter != null ? fighter.RunnerById(skillId) : null;

    // Which characters are being played on this server right now, AND BY WHICH SHELL. Emptied one name at a time
    // as people leave, so it never grows past the number of people actually here.
    //
    // WHY THE SHELL AND NOT JUST THE NAME. A person walking into a map this same server holds is briefly on two
    // connections at once, and the older one is torn down after the newer one is already playing. Without knowing
    // whose name it is, that teardown frees a name that is live.
    static readonly System.Collections.Generic.Dictionary<string, NetPlayer> playing =
        new System.Collections.Generic.Dictionary<string, NetPlayer>();

    /// <summary>Is this character already being played here? Asked at the door, before a body is made for somebody
    /// who is not going to be let in.</summary>
    public static bool AlreadyPlaying(string characterId)
        => !string.IsNullOrEmpty(characterId) && playing.ContainsKey(characterId);

    /// <summary>The shell playing this character here, or null — the roll above, read without walking every
    /// connection. A destroyed shell reads as null.</summary>
    public static NetPlayer PlayedBy(string characterId)
        => !string.IsNullOrEmpty(characterId) && playing.TryGetValue(characterId, out var shell) && shell ? shell : null;

    /// <summary>Tear the fighter down when its player leaves. The roll of who is here must not keep a name that is
    /// gone, or every creature in the world goes on chasing somebody who is not there.</summary>
    public static void Dismiss(NetPlayer shell)
    {
        // DESTROYED IS STILL GONE. A body whose connection has already been torn down reads as null here, and
        // that used to end this method before the name was released — so that character was held forever and
        // its owner could never come back until the server restarted. Whoever calls this after the body has
        // gone must use ReleaseCharacter below instead; this early return only skips the parts that need a body.
        if (shell == null) return;

        // Their character is free to be played again — released BEFORE the early return below, because a
        // connection that never got as far as a fighter has still claimed the name. Only if it is still THEIRS:
        // this same shell may already have been replaced by a newer one for the same character.
        ReleaseCharacterIfHeldBy(shell.ServerCharacterId, shell);

        var unit = shell.ServerCombatUnit;
        if (unit == null) return;

        PlayerUnits.Leave(unit);
        shell.ReportStatesOf(null);   // stop listening before the store goes with the fighter below
        shell.BindCombatUnitOnServer(null);
        if (unit is Character c && c != null) UnityEngine.Object.Destroy(c.gameObject);
    }
}
