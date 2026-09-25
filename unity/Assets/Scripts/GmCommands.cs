using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// THE single dispatcher for every in-chat "/…" command — the one choke point a slash-line routes through
/// (see <see cref="ChatController"/>). Commands are looked up by name/alias in one registry, so adding a
/// command is a single <see cref="Register"/> call, never a new call site or a scattered <c>if</c> chain.
///
/// <para>Gameplay/GM commands are intentionally NOT authored here yet — the chat system ships first. When a
/// command is needed, register it in <see cref="EnsureBootstrap"/> (or call <see cref="Register"/> at
/// startup). Only the built-in <c>/help</c> exists today.</para>
/// </summary>
public static class GmCommands
{
    static readonly List<GmCommand> all = new List<GmCommand>();
    static readonly Dictionary<string, GmCommand> byName = new Dictionary<string, GmCommand>(StringComparer.OrdinalIgnoreCase);
    static bool bootstrapped;

    /// <summary>Every registered command, in registration order (for <c>/help</c>).</summary>
    public static IReadOnlyList<GmCommand> All { get { EnsureBootstrap(); return all; } }

    /// <summary>A chat line is a command when its first non-space char is '/'.</summary>
    public static bool IsCommand(string line)
        => !string.IsNullOrWhiteSpace(line) && line.TrimStart().StartsWith("/");

    /// <summary>Register (or re-bind) a command under its name + every alias. Last writer wins per key.</summary>
    public static void Register(GmCommand cmd)
    {
        if (cmd == null || string.IsNullOrWhiteSpace(cmd.name)) return;
        Bind(cmd.name, cmd);
        foreach (var a in cmd.aliases) Bind(a, cmd);
        if (!all.Contains(cmd)) all.Add(cmd);
    }

    static void Bind(string key, GmCommand cmd)
    {
        if (!string.IsNullOrWhiteSpace(key)) byName[key.TrimStart('/')] = cmd;
    }

    /// <summary>The words of a "/name arg arg" line, the leading slash dropped. A run of words inside double
    /// quotes is ONE word — so `/item "watery ink" 10` names the item, not the word `"watery`. Three callers split
    /// a line; this is the one place they agree on what a word is. Allocates, on a typed line, which is fine.</summary>
    static string[] Tokenize(string line)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        bool quoted = false, any = false;
        string body = line.Trim().Substring(1);
        for (int i = 0; i < body.Length; i++)
        {
            char ch = body[i];
            if (ch == '"') { quoted = !quoted; any = true; continue; }
            if (!quoted && (ch == ' ' || ch == '\t'))
            {
                if (any) { words.Add(word.ToString()); word.Clear(); any = false; }
                continue;
            }
            word.Append(ch);
            any = true;
        }
        if (any) words.Add(word.ToString());
        return words.ToArray();
    }

    /// <summary>Parse a "/name arg arg" line and run the matching command, sending any output to
    /// <paramref name="reply"/> (the chat log). Unknown names and handler exceptions reply an error rather
    /// than throwing, so a bad command can never take the chat down.</summary>
    public static void Execute(string line, Action<string> reply)
    {
        EnsureBootstrap();
        if (!IsCommand(line)) return;

        var tokens = Tokenize(line);
        if (tokens.Length == 0) { reply?.Invoke("Type a command after '/'. Try /help."); return; }

        var name = tokens[0];
        if (!byName.TryGetValue(name, out var cmd))
        {
            reply?.Invoke($"Unknown command '/{name}'. Type /help.");
            return;
        }

        // WHO IS ALLOWED, asked before anything is parsed or spent. Answered here, once, rather than by each
        // command remembering to ask — the check every handler shares is the one a new handler forgets.
        if (!cmd.anyoneMayRun && !AccountSession.IsGm && !GameServer.IsServer)
        {
            reply?.Invoke(NotAGm);
            return;
        }

        // A command that changes the shared world is not carried out here. A creature this game made by itself
        // is one the server has never heard of: invisible to everybody else, unhittable, and gone the moment the
        // real world is next believed. So the line is handed to the machine that owns the world, and its answer
        // comes back into this same chat log a moment later.
        if (cmd.changesTheWorld && !GameServer.IsServer)
        {
            if (!GameNetwork.SendCommandToServer(line, reply))
                reply?.Invoke("Not connected to a world — that command needs the server.");
            return;
        }

        var args = new string[tokens.Length - 1];
        Array.Copy(tokens, 1, args, 0, args.Length);

        var ctx = new GmContext { args = args, reply = reply };
        if (GameLog.On(LogCat.Command)) GameLog.Info(LogCat.Command, line);
        try { cmd.run(ctx); }
        catch (Exception e) { reply?.Invoke($"<color=#FF8080>/{name} failed: {e.Message}</color>"); }
    }

    /// <summary>What somebody who is not a GM is told, written once so the game and the server refuse in the
    /// same words — a command that is refused differently depending on where it was checked reads as a bug.</summary>
    public const string NotAGm = "That command is for GMs.";

    /// <summary>Run a line that arrived from somewhere other than this machine's own chat box — a player's
    /// request, carried to the server. The asker is named explicitly because a server holds many people and
    /// "the player" is not a question with a single answer there.</summary>
    public static void ExecuteAsAsker(string line, Character askerCharacter, Transform askerBody, Action<string> reply)
    {
        EnsureBootstrap();
        if (!IsCommand(line)) return;

        var tokens = Tokenize(line);
        if (tokens.Length == 0) return;

        if (!byName.TryGetValue(tokens[0], out var cmd))
        {
            reply?.Invoke($"Unknown command '/{tokens[0]}'. Type /help.");
            return;
        }

        var args = new string[tokens.Length - 1];
        Array.Copy(tokens, 1, args, 0, args.Length);

        var ctx = new GmContext { args = args, reply = reply, askerCharacter = askerCharacter, askerBody = askerBody };
        try { cmd.run(ctx); }
        catch (Exception e) { reply?.Invoke($"<color=#FF8080>/{tokens[0]} failed: {e.Message}</color>"); }
    }

    /// <summary>Does this command belong to the machine that owns the world? Asked by the server when a request
    /// arrives, so that a request for anything else is refused rather than quietly run on the wrong machine.</summary>
    public static bool ChangesTheWorld(string line)
    {
        EnsureBootstrap();
        if (!IsCommand(line)) return false;
        var tokens = Tokenize(line);
        return tokens.Length > 0 && byName.TryGetValue(tokens[0], out var cmd) && cmd.changesTheWorld;
    }

    // Registers every command once, on first use. Declare new gameplay/GM commands here.
    static void EnsureBootstrap()
    {
        if (bootstrapped) return;
        bootstrapped = true;
        Register(new GmCommand("help", "help [command]", "List commands, or show one command's usage.", Help,
                               anyoneMayRun: true, changesTheWorld: false, "h", "commands"));

        // WHICH KINDS OF LINE THE SERVER'S LOG WRITES. Marked as the server's, and that is the whole point:
        // hits, passives and buffs are DECIDED on the server and announced only in the server's log, so a
        // switch that could not reach the server left the categories that matter unreachable. A play-test hit
        // exactly that — it could not tell whether a passive had fired, and correctly refused to call that a
        // fault. To change what YOUR OWN machine writes, call the switch in code; this command is the server's.
        Register(new GmCommand("log", "log [category] [on|off]  ·  log all on|off  ·  log reset",
                               "Show which kinds of line the SERVER's log writes, or switch one on and off. "
                               + "Warnings and errors always print whatever this says.", CmdLog,
                               anyoneMayRun: false, changesTheWorld: true, "logs"));

        // --- Character ---
        // THE SERVER'S, like everything else about a character. Run on a player's own machine these changed only
        // that machine's copy: /statpts showed 61 points while the server — which decides whether a point may be
        // spent — held 1, so every + press after the real points ran out was silently refused.
        Register(new GmCommand("lvl", "lvl <n>", "Set character level.", CmdLevel, false, true, "level"));
        Register(new GmCommand("xp", "xp <n>", "Grant character XP.", CmdXp, false, true, "exp"));
        Register(new GmCommand("ringxp", "ringxp <n>", "Grant Ring of Progression XP.", CmdRingXp, false, true));
        Register(new GmCommand("statpts", "statpts <n>", "Grant allocatable stat points.", CmdStatPts, false, true, "sp"));
        Register(new GmCommand("heal", "heal", "Full-heal (revives if dead).", CmdHeal, false, true));
        Register(new GmCommand("revive", "revive", "Revive the player at full HP/MP.", CmdRevive, false, true));
        Register(new GmCommand("invuln", "invuln", "Toggle invincibility (no damage taken).", CmdInvuln, false, true, "nodmg"));
        Register(new GmCommand("model", "model [name]", "Wear a character model. No name lists the roster.", CmdModel, false, true, "body"));

        // --- Economy & items ---
        // THE FOUR BELOW CHANGE WHAT SOMEBODY OWNS, and what somebody owns is the server's. Run on a player's own
        // machine they said they had worked and had not: the reply read "Gave 5x Healing Potion" while the bag
        // went from nothing to nothing, and an added coin vanished the instant the server next spoke.
        Register(new GmCommand("gold", "gold <n>", "Add Zeny (may be negative).", CmdGold, false, true, "zeny"));
        Register(new GmCommand("item", "item <id|name> [count]", "Drop an item into the bag.", CmdItem, false, true, "give"));
        Register(new GmCommand("weapon", "weapon [type]",
            "Put a Raw, unfolded weapon base in your bag. Type = sword/bow/… (default: a random weapon base). " +
            "Fold it and quench it at a smith.", CmdWeapon, false, true, "drop"));
        Register(new GmCommand("mastery", "mastery [weapon] <n>",
            "Testing: set your Weapon Mastery level in a weapon (default: the one in your hands), and learn " +
            "whatever that opens. No arguments reports what you have practised.", CmdMastery, false, true));
        Register(new GmCommand("station", "station <smith|loom|tannery|none>",
            "Testing: make the nearest townsperson work at a station, so their station window opens on F. Lasts " +
            "until the world is reloaded.", CmdStation, false, true));
        Register(new GmCommand("clearbag", "clearbag", "Empty the bag (worn gear is left alone).", CmdClearBag, false, true, "clearinv", "clearinventory"));
        Register(new GmCommand("bind", "bind <skillId>", "Bind a composed skill to the first empty hotbar slot.", CmdBind, false, true));

        // --- Mobs ---
        Register(new GmCommand("kill", "kill", "Kill the mob nearest the player.", CmdKill,
                               anyoneMayRun: false, changesTheWorld: true));
        Register(new GmCommand("killall", "killall", "Kill every active mob.", CmdKillAll,
                               anyoneMayRun: false, changesTheWorld: true));
        Register(new GmCommand("dummy", "dummy", "Put a training dummy in front of you — an immobile target with "
                               + "enormous health that never fights back, for measuring what your build actually does. "
                               + "The server owns it, so everybody can see and hit the same one.", CmdDummy,
                               anyoneMayRun: false, changesTheWorld: true));
        Register(new GmCommand("summon", "summon <count> <normal|elite|shadow|creature> [level] [skill id]", "Summon N creatures near you: random ones of a kind (normal, elite or shadow), or a named creature by id or name. Give a level to re-stat each one at that level with a randomly weighted spread that spends exactly the stat points that level earns. Give a skill id to make it every summoned creature's extra skill, for every life, instead of a random one — any skill, published or not.", CmdSummon,
                               anyoneMayRun: false, changesTheWorld: true));
        Register(new GmCommand("where", "where", "Print the map you are in and your position in that map's own coordinates — the numbers a spawn area's Center takes in the world spawn list.", CmdWhere, false, false, "pos"));

        // --- World & weather ---
        Register(new GmCommand("tp", "tp <map id> [spawnId]", "Travel to another map.", CmdTp, false, true, "warp", "goto"));
        // THE SERVER'S TOO. Run on one machine these changed one person's sky and nobody else's; held on the server,
        // everybody on it — and everybody who arrives later — sees the same one.
        Register(new GmCommand("time", "time <0..1>", "Set time of day (0=midnight, 0.5=noon).", CmdTime, false, true));
        Register(new GmCommand("weather", "weather <clear|cloudy|rain|storm|snow|fog>", "Force the weather preset.", CmdWeather, false, true));
        Register(new GmCommand("speed", "speed <mult>", "Set a persistent move-speed multiplier on the player.", CmdSpeed, false, true));
    }

    // ---- handlers (grouped; each just calls into the owning system) --------------

    static readonly Color KillColor = new Color(1f, 0.3f, 0.3f);

    static void CmdLevel(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.TryInt(0, out var n)) { c.Reply("Usage: /lvl <n>"); return; }
        c.Ch.SetLevel(Mathf.Max(1, n));
        AfterChangingTheirStats(c.Ch);
        c.Reply($"Level → {c.Ch.Level}.");
    }

    // Listing and wearing both wait on the roster, which is fetched once and answered instantly after that. Replies
    // land in the chat log whenever they resolve — a command that answers a frame late is fine; a stalled one is not.
    static void CmdModel(GmContext c)
    {
        string typed = c.Rest(0);
        CharacterModels.Names(names =>
        {
            if (names == null || names.Count == 0)
            {
                c.Reply("No character models are registered — the roster is empty.");
                return;
            }
            if (string.IsNullOrWhiteSpace(typed))
            {
                c.Reply($"{names.Count} models: {string.Join(", ", names)}");
                return;
            }
            string match = CharacterModels.Resolve(typed);
            if (match == null) { c.Reply($"No model called '{typed}'. Type /model to list them."); return; }

            // ON THE BODY EVERYBODY SEES. The name travels to every machine watching this person, and each one —
            // theirs included — dresses the body in it (see NetPlayer.OnModelChanged).
            var shell = GameNetwork.ShellFor(c.Ch);
            if (shell == null) { c.Reply("No character."); return; }
            shell.modelName = match;
            c.Reply($"Wearing {match}.");
        });
    }

    static void CmdXp(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.TryInt(0, out var n) || n <= 0) { c.Reply("Usage: /xp <n>"); return; }
        int levelsGained = c.Ch.AddXP(n, out int ringLevelsGained);
        GameNetwork.TellOwnerTheyEarned(c.Ch, n, 0, levelsGained, ringLevelsGained);
        GameNetwork.ShellFor(c.Ch)?.SaveSoon();
        c.Reply($"+{n} XP.");
    }

    static void CmdRingXp(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.TryInt(0, out var n) || n <= 0) { c.Reply("Usage: /ringxp <n>"); return; }
        int ringLevelsGained = c.Ch.AddRingXP(n);
        GameNetwork.TellOwnerTheyEarned(c.Ch, 0, n, 0, ringLevelsGained);
        GameNetwork.ShellFor(c.Ch)?.SaveSoon();
        c.Reply($"+{n} ring XP.");
    }

    static void CmdStatPts(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.TryInt(0, out var n) || n <= 0) { c.Reply("Usage: /statpts <n>"); return; }
        c.Ch.GrantStatPoints(n);
        AfterChangingTheirStats(c.Ch);
        c.Reply($"+{n} stat points.");
    }

    static void CmdHeal(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (c.Ch.IsDead) { GameNetwork.ReviveOnServer(c.Ch); c.Reply("Revived at full HP/MP."); }
        else { c.Ch.Heal(c.Ch.MaxHP); c.Reply("Full HP."); }
    }

    static void CmdRevive(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        GameNetwork.ReviveOnServer(c.Ch);
        c.Reply("Revived at full HP/MP.");
    }

    static void CmdInvuln(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        c.Ch.Invincible = !c.Ch.Invincible;
        // WRITTEN AS A STATE, not only as "ran '/invuln'" — the command is a toggle, so the ran line alone cannot say
        // which way it went, and the log checker needs to know whether a blow landed on somebody who could not be hurt.
        GameLog.Info(LogCat.Server, string.Concat(SpawnIds.MapOf(c.Ch), " ", SpawnIds.Label(c.Ch),
                                                  " invincibility ", c.Ch.Invincible ? "on" : "off"));
        c.Reply(c.Ch.Invincible ? "Invincibility ON." : "Invincibility OFF.");
    }

    static void CmdGold(GmContext c)
    {
        if (!c.TryInt(0, out var n)) { c.Reply("Usage: /gold <n>"); return; }
        // THE PERSON WHO ASKED, not "the money on this machine". On a server the asker is one of several people
        // signed in and each has their own; the old call reached for the one figure the whole program used to
        // share, which on a server is nobody's.
        if (c.Ch == null) { c.Reply("No character."); return; }
        c.Ch.AddGold(n);
        c.Reply($"Zeny → {c.Ch.Gold}.");
    }

    static void CmdItem(GmContext c)
    {
        var key = c.Arg(0);
        if (key == null) { c.Reply("Usage: /item <id|name> [count]"); return; }
        if (!c.TryInt(1, out var count) || count <= 0) count = 1;

        if (c.Ch == null) { c.Reply("No character."); return; }

        Item found = null;
        foreach (var it in Resources.LoadAll<Item>("Items"))
        {
            if (it == null) continue;
            if (string.Equals(it.id, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(it.displayName, key, StringComparison.OrdinalIgnoreCase)) { found = it; break; }
        }
        if (found == null) { c.Reply($"No item '{key}'."); return; }

        GiveTo(c.Ch, found, count);
        c.Reply($"Gave {count}× {found.displayName}.");
    }

    // /weapon [type] — the testing door for a forged base. One argument, optional: whichever weapon type is asked
    // for. What comes back is a Raw, unfolded base, exactly what a forge hands over.
    static void CmdWeapon(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }

        WeaponType? type = null;
        for (int i = 0; i < c.ArgCount; i++)
        {
            var a = c.Arg(i);
            if (Enum.TryParse<WeaponType>(a, true, out var t) && t != WeaponType.None) { type = t; continue; }
            c.Reply($"Don't know '{a}' — expected a weapon type (sword, bow, ...).");
            return;
        }

        // Candidate bases: every weapon Item authored, filtered to the asked-for type.
        Item pick = null;
        int seen = 0;
        foreach (var it in Resources.LoadAll<Item>("Items"))
        {
            if (it == null || it.slot != EquipSlotType.Weapon || it.weaponType == WeaponType.None) continue;
            if (type.HasValue && it.weaponType != type.Value) continue;
            if (CombatRng.Range(0, ++seen) == 0) pick = it;   // reservoir pick — one pass, no list
        }
        if (pick == null)
        {
            c.Reply(type.HasValue ? $"No {type.Value} base weapon authored." : "No weapon items authored.");
            return;
        }

        var inst = EquipmentInstance.CreateFresh(pick);
        c.Ch.TakeIntoBag(inst);
        AfterChangingTheirBag(c.Ch);
        if (inst == null) { c.Reply("Couldn't create the weapon."); return; }
        c.Reply($"Raw {pick.displayName} — fold it and quench it at a smith.");
    }

    // Testing door for the stations, until townspeople are authored as smiths, weavers and tanners in the town
    // scene. Sets the role on the SERVER's copy of the nearest townsperson, and it is not written down anywhere.
    static void CmdStation(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (c.ArgCount < 1) { c.Reply("station <smith|loom|tannery|none>"); return; }
        if (!Enum.TryParse<CraftStation>(c.Arg(0), true, out var which))
        {
            c.Reply($"Don't know '{c.Arg(0)}' — expected smith, loom, tannery or none.");
            return;
        }

        var body = c.Ch.Body;
        var npc = body != null ? TownNPC.Nearest(body.position, 8f) : null;
        if (npc == null) { c.Reply("Nobody close enough. Stand next to a townsperson."); return; }
        npc.station = which;
        // AND EVERY MACHINE WATCHING THEM. The body a player presses F on is their own copy, so a role written
        // only here would open a conversation with a smith.
        GameNetwork.TellEverybodyTheyWorkAt(npc.gameObject, (int)which);
        c.Reply(which == CraftStation.None
            ? "That one is an ordinary villager again."
            : $"That one works at the {CraftStations.Label(which)} now. Press F on them.");
    }

    // Testing door for the mastery tracks: a level is thousands of kills wide, and a play-test cannot fight
    // them. Sets the practice to exactly what that level costs and learns whatever it opens.
    static void CmdMastery(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }

        var held = c.Ch.HeldWeapon;
        if (c.ArgCount == 0)
        {
            var sb = new StringBuilder("Practised: ");
            bool any = false;
            foreach (WeaponType w in Enum.GetValues(typeof(WeaponType)))
            {
                if (w == WeaponType.None) continue;
                int lvl = c.Ch.Mastery.EarnedLevel(w);
                if (lvl <= 0) continue;
                if (any) sb.Append(", ");
                sb.Append(w).Append(' ').Append(lvl);
                any = true;
            }
            if (!any) sb.Append("nothing yet");
            sb.Append(". Holding ").Append(held == WeaponType.None ? "nothing" : held.ToString())
              .Append(" (usable ").Append(c.Ch.WeaponMasteryLevel).Append("), ")
              .Append(c.Ch.Mastery.LearnedCount).Append(" skills learned.");
            c.Reply(sb.ToString());
            return;
        }

        var weapon = held;
        int level = -1;
        for (int i = 0; i < c.ArgCount; i++)
        {
            var a = c.Arg(i);
            if (Enum.TryParse<WeaponType>(a, true, out var w) && w != WeaponType.None) { weapon = w; continue; }
            if (int.TryParse(a, out int n)) { level = n; continue; }
            c.Reply($"Don't know '{a}' — expected a weapon type or a level.");
            return;
        }
        if (weapon == WeaponType.None) { c.Reply("Hold a weapon, or name one."); return; }
        if (level < 0) { c.Reply("mastery [weapon] <n>"); return; }

        c.Ch.Mastery.SetPractice(weapon, MasteryMath.WeaponXpFor(level));
        int learned = c.Ch.Mastery.LearnWhatIsOpen();
        c.Ch.RefreshUsableSkills();
        c.Reply($"{weapon} mastery {c.Ch.Mastery.EarnedLevel(weapon)} — {learned} new skill(s) learned, " +
                $"{c.Ch.WeaponMasteryLevel} usable with what you are holding.");
    }

    static void CmdClearBag(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        int n = c.Ch.Bag.Count;
        c.Ch.Bag.Clear();
        AfterChangingTheirBag(c.Ch);
        c.Reply(n == 0 ? "Bag was already empty." : $"Cleared {n} stack(s). Worn gear untouched.");
    }

    // ---- putting things in somebody's bag ----------------------------------------------------------------
    //
    // Straight onto the CHARACTER, because on a server that is the only bag there is — the window component
    // these used to go through belongs to a screen, and a server has none. It also has to be the asker's
    // character and not "the" character: a server holds several people at once.

    static void GiveTo(Character who, Item def, int count)
    {
        if (who == null || def == null || count <= 0) return;
        // THROUGH THE ONE DOOR, not a second copy of what it does. This used to roll copies and merge stacks
        // itself — the same rules written twice, and a way into a bag that nothing else could see, so a command
        // was the one source of items that never announced itself.
        who.TakeIntoBag(def, count);
        AfterChangingTheirBag(who);
    }

    /// <summary>Tell whoever owns this bag that it changed, and have it written down. On a server that means
    /// sending them the whole bag; with no world it means nudging the window and the save that live here.</summary>
    static void AfterChangingTheirBag(Character who)
    {
        var shell = GameNetwork.ShellFor(who);
        if (shell != null) { shell.PushBagToOwner(); shell.SaveSoon(); return; }
        InventoryStore.Instance?.Notify();
    }

    /// <summary>Hand whoever owns this character their level, points and primaries as the server now has them, and
    /// have it written down.</summary>
    static void AfterChangingTheirStats(Character who)
    {
        var shell = GameNetwork.ShellFor(who);
        if (shell == null) return;
        shell.PushStatsToOwner();
        shell.SaveSoon();
    }

    static void CmdBind(GmContext c)
    {
        var id = c.Arg(0);
        if (id == null) { c.Reply("Usage: /bind <skillId>"); return; }
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.Ch.Hotbar.AutoBind(id)) { c.Reply($"Couldn't bind '{id}' (unknown, already bound, or bar full)."); return; }
        // Bound HERE, so their machine is handed the bar the server now holds — the same push a weapon swap uses,
        // which also rebuilds the skills behind the bar on both sides and redraws it.
        var shell = GameNetwork.ShellFor(c.Ch);
        if (shell != null) { shell.PushBagToOwner(alsoTheBar: true); shell.SaveSoon(); }
        c.Reply($"Bound {id} (ultimates → ult slot).");
    }

    static void CmdKill(GmContext c)
    {
        var m = NearestMob(c.Player);
        if (m == null) { c.Reply("No mob nearby."); return; }
        // THROUGH THE ONE DAMAGE DOOR, not straight into the sink. Reaching past it skipped the rule about who is
        // allowed to change a creature's health, so on a player's machine this popped a number in the billions over
        // a mob that then shrugged it off — the health write was refused, the popup was not.
        CombatAuthority.ApplyTrue(m.Health, int.MaxValue, KillColor, "GM kill");
        c.Reply("Killed nearest mob.");
    }

    // WHAT THE LOG IS ALLOWED TO SAY. Named categories rather than one on-off switch, because the failure this
    // exists to fix was not "too little log" — it was one routine line printing twice a second and burying every
    // line that mattered underneath it, 1205 against 53.
    static void CmdLog(GmContext c)
    {
        var what = c.Arg(0);
        if (what == null) { c.Reply(GameLog.Describe()); return; }

        if (string.Equals(what, "reset", System.StringComparison.OrdinalIgnoreCase))
        {
            GameLog.Reset();
            c.Reply("Log categories back to their defaults. " + GameLog.Describe());
            return;
        }

        var state = c.Arg(1);
        if (state == null)
        {
            c.Reply("Usage: /log  ·  /log <category> on|off  ·  /log all on|off  ·  /log reset");
            return;
        }

        bool on;
        if (string.Equals(state, "on", System.StringComparison.OrdinalIgnoreCase)) on = true;
        else if (string.Equals(state, "off", System.StringComparison.OrdinalIgnoreCase)) on = false;
        else { c.Reply($"Say 'on' or 'off', not '{state}'."); return; }

        if (string.Equals(what, "all", System.StringComparison.OrdinalIgnoreCase))
        {
            GameLog.SetAll(on);
            c.Reply((on ? "Every category on. "
                        : "Every category off — warnings and errors still print. ")
                    + GameLog.Describe());
            return;
        }

        if (!GameLog.TryParse(what, out var cat))
        {
            c.Reply($"No log category called '{what}'. " + GameLog.Describe());
            return;
        }

        c.Reply($"{GameLog.Tag(cat)} → {(GameLog.Set(cat, on) ? "on" : "off")}.");
    }

    static void CmdKillAll(GmContext c)
    {
        var snapshot = new List<MonsterAI3D>(MonsterAI3D.Active);   // Die() mutates the live list
        int n = 0;
        foreach (var m in snapshot)
            if (m != null && m.Health != null && !m.Health.IsDead) { CombatAuthority.ApplyTrue(m.Health, int.MaxValue, KillColor, "GM kill"); n++; }
        c.Reply($"Killed {n} mob(s).");
    }

    // A dummy is a creature the SERVER makes and every player is told about, exactly like any other creature — which
    // is the whole point. The editor tool used to place one straight into a scene, and a scene fixture has no shared
    // identity, so a swing at it reached nothing the server had heard of and was refused: no damage, no number. Born
    // here instead, it travels the path that already works.
    static void CmdDummy(GmContext c)
    {
        if (!TryAskersMap(c, out var map)) return;

        var def = MonsterCatalog.WeightedPick();
        if (def == null) { c.Reply("No mobs in the catalog to build a dummy from."); return; }

        var reply = c.reply;   // the GmContext is transient; this callback outlives it
        var p = c.Player;
        Vector3 at = p != null ? p.position + p.forward * 2f : Vector3.zero;

        MobSpawner.Spawn(def, at, map, go =>
        {
            if (go == null) { reply?.Invoke("Couldn't build a dummy."); return; }

            go.name = "Training_Dummy";
            TrainingDummy.MakeOne(go);                 // the ONE description of what a dummy is
            GameNetwork.PublishMob(go, def.id);        // …and every connected player is told about it
            reply?.Invoke("Dummy placed. It resets to full health every 5 seconds.");
        });
    }

    // "random mobs" = pull random defs from the mob catalog and spawn them from their Addressable prefabs
    // (see MobSpawner / Bake Mob Prefabs). Normal = weighted field pick; boss = a random boss-flagged def.
    // Spawning is async (Addressables loads the prefab), so we count completions and reply once all land.
    static void CmdSummon(GmContext c)
    {
        const string usage = "Usage: /summon <count> <normal|elite|shadow|creature> [level] [skill id]";
        if (!TryAskersMap(c, out var askerMap)) return;
        if (!c.TryInt(0, out var count) || count <= 0) { c.Reply(usage); return; }
        count = Mathf.Clamp(count, 1, 50);

        // A KIND draws at random from that kind; anything else names one creature.
        var type = c.Arg(1);
        CreatureKind? kind = null;
        MonsterDef named = null;
        switch (type != null ? type.ToLowerInvariant() : null)
        {
            case null: c.Reply(usage); return;
            case "normal": case "mob": kind = CreatureKind.Normal; break;
            case "elite": kind = CreatureKind.Elite; break;
            case "shadow": kind = CreatureKind.Shadow; break;
            default:
                named = MonsterCatalog.Get(type);
                if (named == null) { c.Reply($"No creature or kind called '{type}'. {usage}"); return; }
                break;
        }

        // Optional trailing modifiers, order-free because each is told apart by its shape:
        //   a LEVEL    — re-stats the summoned creature at that level (see the re-stat block below), up to 160, the
        //                highest shadow;
        //   a SKILL ID — any id in the skill catalog, made the creature's extra skill for every life it lives.
        // A shadow is a 3D character because it is a shadow — MobSpawner decides that from its kind.
        int level = 0;
        string extraSkillId = null;
        for (int i = 2; i < c.ArgCount; i++)
        {
            var a = c.Arg(i);
            if (int.TryParse(a, out var lv)) { level = Mathf.Clamp(lv, 1, 160); continue; }
            if (SkillCatalog.IsKnown(a)) { extraSkillId = a; continue; }
            c.Reply(a.StartsWith("skill.") ? $"No skill with the id '{a}'. {usage}" : usage); return;
        }

        var defs = new List<MonsterDef>(count);
        for (int i = 0; i < count; i++)
        {
            var def = named != null ? named
                    : kind == CreatureKind.Normal ? MonsterCatalog.WeightedPick()
                    : MonsterCatalog.RandomOfKind(kind.Value);
            if (def == null) break;
            defs.Add(def);
        }
        if (defs.Count == 0)
        { c.Reply($"No {kind.ToString().ToLowerInvariant()} creatures on the roster."); return; }

        var reply = c.reply;   // capture the chat channel; the GmContext is transient but this Action persists
        var p = c.Player;
        Vector3 basePos = p != null ? p.position : Vector3.zero;
        int pending = defs.Count, done = 0, spawned = 0, missing = 0;

        foreach (var def in defs)
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle * 2f;
            MobSpawner.Spawn(def, basePos + new Vector3(r.x, 0f, r.y), askerMap, go =>
            {
                if (go != null)
                {
                    spawned++;
                    // A level was asked for: re-stat this mob at that level with a RANDOMLY WEIGHTED spread. The
                    // spread is bought through UnitStatMath.Allocate — the same point economy the player spends in —
                    // so the mob's primaries vary but always cost exactly the points its level has earned.
                    if (level > 0)
                    {
                        var block = go.GetComponent<UnitStatBlock>();
                        if (block != null)
                            block.SetSpawnInputs(UnitStatMath.Allocate(
                                level,
                                UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value,
                                UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value));
                    }
                    if (extraSkillId != null) { var health = go.GetComponent<Health>(); if (health != null) health.PinExtraSkill(extraSkillId); }
                    // Born on the server → every connected player gets to see it. Without this a summoned creature
                    // existed only on the machine that typed the command — the same way a pooled respawn goes
                    // invisible to everyone but the server if its fresh shell is skipped.
                    GameNetwork.PublishMob(go, def.id);
                }
                else missing++;
                if (++done < pending) return;

                if (spawned == 0)
                    reply?.Invoke("Couldn't summon — mob prefabs aren't built yet. Run Tools ▸ unwritten ▸ Mobs & NPCs ▸ Bake Mob Prefabs, then retry.");
                else
                    reply?.Invoke($"Summoned {spawned} " +
                                  (named != null ? named.displayName : "random " + kind.ToString().ToLowerInvariant() + " creature(s)") +
                                  (level > 0 ? $" at level {level}" : "") +
                                  (extraSkillId != null ? $" with extra skill {extraSkillId}" : "") +
                                  (missing > 0 ? $" ({missing} skipped — no Addressable prefab)" : "") + ".");
            });
        }
    }

    // WHERE YOU ARE, in the numbers the world spawn list is written in: the map's OWN coordinates, which stay the same
    // wherever a server happens to set that map down. Reads the asker's own machine — nothing changes.
    static void CmdWhere(GmContext c)
    {
        var p = c.Player;
        if (p == null) { c.Reply("No character in the world."); return; }
        Vector3 at = MapManager.InMap(p.position, c.MapName);
        c.Reply($"{c.MapName} — x {at.x:0.#}, y {at.y:0.#}, z {at.z:0.#}");
    }

    static void CmdTp(GmContext c)
    {
        var scene = c.Arg(0);
        if (scene == null) { c.Reply("Usage: /tp <map id> [spawnId]"); return; }
        // THROUGH THE DOOR EVERY PORTAL USES. Loading the map on the asker's machine moved nobody: the server
        // still had them where they stood, and the map they wanted may be held by another process entirely.
        var shell = GameNetwork.ShellFor(c.Ch);
        if (shell == null) { c.Reply("No character."); return; }
        shell.MoveToMap(scene, c.Arg(1));
        c.Reply($"Traveling to {scene}…");
    }

    static void CmdTime(GmContext c)
    {
        if (!c.TryFloat(0, out var t)) { c.Reply("Usage: /time <0..1>"); return; }
        var tod = TimeOfDay.Instance;
        if (tod == null) { c.Reply("No clock."); return; }
        t = Mathf.Clamp01(t);
        TimeOfDay.TakeShift(TimeOfDay.ShiftSeconds
                            + TimeOfDay.SecondsToReach(TimeOfDay.ElapsedForTimeOfDay, tod.DayLengthSeconds, t));
        GameNetwork.TellEveryoneTheTimeOfDay();
        c.Reply($"Time → {t:0.00}.");
    }

    static void CmdWeather(GmContext c)
    {
        var w = c.Arg(0);
        if (w == null || !ParseWeather(w, out var preset)) { c.Reply("Usage: /weather <clear|cloudy|rain|storm|snow|fog>"); return; }
        WeatherDirector.TakeForced(preset);
        GameNetwork.TellEveryoneTheWeather();
        c.Reply($"Weather → {preset}.");
    }

    static void CmdSpeed(GmContext c)
    {
        if (!c.TryFloat(0, out var s)) { c.Reply("Usage: /speed <mult>"); return; }
        // THE ASKER'S OWN STATUS HOST, never one looked up on their body: on a server the body and the fighter are
        // different objects, and a status found-or-made on the body is a second store nothing reads.
        var host = c.Ch != null ? c.Ch.StatusHost : null;
        if (host == null) { c.Reply("No player."); return; }
        host.Haste(Mathf.Max(0.1f, s), 9999f);
        c.Reply($"Move speed ×{s:0.0}.");
    }

    // ---- shared helpers ---------------------------------------------------------

    /// <summary>THE GATE EVERY COMMAND THAT BUILDS SOMETHING AT THE ASKER'S FEET GOES THROUGH. Answers with the
    /// map to build in, or refuses and says so.
    ///
    /// <para>A server holds only the maps it was told to open, and a player may walk into one it never did.
    /// <see cref="MapManager.PutInMap"/> can only move a new thing into a map that is open here, and does
    /// NOTHING AT ALL when it is not — so the thing stays in whichever map Unity loaded last, filed under the
    /// asker's map while standing two thousand units from that map's ground. MEASURED: six creatures filed
    /// under a field, standing in the town. Refusing is the only honest answer, because there is nowhere here
    /// to put it.</para>
    ///
    /// <para>A player's own game holds the one map it is standing in, so this never refuses in ordinary
    /// play.</para></summary>
    static bool TryAskersMap(GmContext c, out UnityEngine.SceneManagement.Scene map)
    {
        map = c.Map;
        if (map.IsValid() && map.isLoaded) return true;
        c.Reply($"The world isn't holding {c.MapName} — nothing can be built there.");
        return false;
    }

    static MonsterAI3D NearestMob(Transform player)
    {
        if (player == null) return null;
        Vector3 pos = player.position;
        MonsterAI3D best = null; float bestSqr = float.MaxValue;
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (m == null || m.Health == null || m.Health.IsDead) continue;
            float sqr = (m.transform.position - pos).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = m; }
        }
        return best;
    }

    static bool ParseWeather(string s, out WeatherDirector.Preset preset)
    {
        switch (s.ToLowerInvariant())
        {
            case "clear": case "sun": case "sunny": preset = WeatherDirector.Preset.Clear; return true;
            case "cloud": case "cloudy": case "overcast": preset = WeatherDirector.Preset.Cloudy; return true;
            case "rain": case "lightrain": preset = WeatherDirector.Preset.LightRain; return true;
            case "storm": case "thunder": case "thunderstorm": preset = WeatherDirector.Preset.Thunderstorm; return true;
            case "snow": preset = WeatherDirector.Preset.Snow; return true;
            case "fog": preset = WeatherDirector.Preset.Fog; return true;
            default: preset = WeatherDirector.Preset.Clear; return false;
        }
    }

    static void Help(GmContext ctx)
    {
        var one = ctx.Arg(0);
        if (one != null && byName.TryGetValue(one.TrimStart('/'), out var cmd))
        {
            ctx.Reply($"<color=#C8A862>/{cmd.usage}</color> — {cmd.summary}");
            return;
        }
        ctx.Reply("<color=#C8A862>Commands:</color>");
        foreach (var c in all)
            ctx.Reply($"  <color=#C8A862>/{c.name}</color> — {c.summary}");
    }
}
