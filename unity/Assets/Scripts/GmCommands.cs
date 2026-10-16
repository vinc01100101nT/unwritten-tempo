using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// THE single dispatcher for every in-chat "/…" command — the one choke point a slash-line routes through
/// (see <see cref="ChatController"/>). Commands are looked up by name/alias in one registry, so adding a
/// command is a single <see cref="Register"/> call, never a new call site or a scattered <c>if</c> chain.
///
/// <para>Every command is registered in <see cref="EnsureBootstrap"/>; <c>/help</c> lists them.</para>
/// </summary>
public static class GmCommands
{
    static readonly List<GmCommand> all = new List<GmCommand>();
    static readonly Dictionary<string, GmCommand> byName = new Dictionary<string, GmCommand>(StringComparer.OrdinalIgnoreCase);

    // Rich-text tints for command replies, read from the interface theme once: a failure, and a name or command.
    static readonly string FailHex = UiKit.Hex(UiKit.Bad);
    static readonly string NameHex = UiKit.Hex(UiKit.Accent);
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

        var ctx = new GmContext { args = args, reply = reply, command = cmd };
        if (GameLog.On(LogCat.Command)) GameLog.Info(LogCat.Command, line);
        try { cmd.run(ctx); }
        catch (Exception e) { reply?.Invoke($"<color={FailHex}>/{name} failed: {e.Message}</color>"); }
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

        var ctx = new GmContext { args = args, reply = reply, command = cmd, askerCharacter = askerCharacter, askerBody = askerBody };
        try { cmd.run(ctx); }
        catch (Exception e) { reply?.Invoke($"<color={FailHex}>/{tokens[0]} failed: {e.Message}</color>"); }
    }

    /// <summary>Run a line sent from a terminal through the character server (see <see cref="TerminalGm"/>). The
    /// server secret it came with is the trust, so there is no GM check; every other rule a player's request meets
    /// still holds. <paramref name="askerCharacter"/> is the <c>--as</c> character, or null when none was named — and
    /// then only a command marked <see cref="GmCommand.worksWithNobody"/> runs.</summary>
    public static void ExecuteFromTerminal(string line, string map, Character askerCharacter, Transform askerBody,
                                           Action<string> reply)
    {
        EnsureBootstrap();
        if (!IsCommand(line)) { reply?.Invoke("A command starts with '/'. Try /help."); return; }

        var tokens = Tokenize(line);
        if (tokens.Length == 0) { reply?.Invoke("Type a command after '/'. Try /help."); return; }
        if (!byName.TryGetValue(tokens[0], out var cmd))
        {
            reply?.Invoke($"Unknown command '/{tokens[0]}'. Type /help.");
            return;
        }

        // The same rule as a player's request, with /help let through: it only reads the list, and a terminal has no
        // game of its own to run it in.
        if (!cmd.changesTheWorld && cmd.name != "help")
        {
            reply?.Invoke($"/{cmd.name} runs in a player's own game, not on the server.");
            return;
        }
        if (askerCharacter == null && !cmd.worksWithNobody)
        {
            reply?.Invoke($"/{cmd.name} acts on a person — name one with --as <character>.");
            return;
        }

        var args = new string[tokens.Length - 1];
        Array.Copy(tokens, 1, args, 0, args.Length);

        var ctx = new GmContext
        {
            args = args, reply = reply, command = cmd, askerCharacter = askerCharacter, askerBody = askerBody,
            fromTerminal = true, terminalMap = map, askedBy = "the terminal",
        };
        try { cmd.run(ctx); }
        catch (Exception e) { reply?.Invoke($"<color={FailHex}>/{tokens[0]} failed: {e.Message}</color>"); }
    }

    /// <summary>May somebody who is not a GM run this command? Asked by the server when a request arrives, because the
    /// server turns away every other request from a non-GM.</summary>
    public static bool AnyoneMayRun(string line)
    {
        EnsureBootstrap();
        if (!IsCommand(line)) return false;
        var tokens = Tokenize(line);
        return tokens.Length > 0 && byName.TryGetValue(tokens[0], out var cmd) && cmd.anyoneMayRun;
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
        Register(new GmCommand("help", "help [command]", "List commands, or show one command's usage.", "help mastery", Help,
                               anyoneMayRun: true, changesTheWorld: false, "h", "commands") { worksWithNobody = true });

        // WHICH KINDS OF LINE THE SERVER'S LOG WRITES. Marked as the server's, and that is the whole point:
        // hits, passives and buffs are DECIDED on the server and announced only in the server's log, so a
        // switch that could not reach the server left the categories that matter unreachable. A play-test hit
        // exactly that — it could not tell whether a passive had fired, and correctly refused to call that a
        // fault. To change what YOUR OWN machine writes, call the switch in code; this command is the server's.
        Register(new GmCommand("log", "log [category] [on|off]  ·  log all on|off  ·  log reset",
                               "Show which kinds of line the SERVER's log writes, or switch one on and off. "
                               + "Warnings and errors always print whatever this says.", "log hit on", CmdLog,
                               anyoneMayRun: false, changesTheWorld: true, "logs") { worksWithNobody = true });

        // --- Character ---
        // THE SERVER'S, like everything else about a character. Run on a player's own machine these changed only
        // that machine's copy: /statpts showed 61 points while the server — which decides whether a point may be
        // spent — held 1, so every + press after the real points ran out was silently refused.
        Register(new GmCommand("lvl", "lvl <n>", "Set character level.", "lvl 50", CmdLevel, false, true, "level"));
        Register(new GmCommand("xp", "xp <n>", "Grant character XP.", "xp 1000", CmdXp, false, true, "exp"));
        Register(new GmCommand("ringxp", "ringxp <n>", "Grant Ring of Progression XP.", "ringxp 500", CmdRingXp, false, true));
        Register(new GmCommand("statpts", "statpts <n>", "Grant allocatable stat points.", "statpts 20", CmdStatPts, false, true, "sp"));
        Register(new GmCommand("heal", "heal", "Refill your HP and MP (revives if dead).", "heal", CmdHeal, false, true));
        Register(new GmCommand("revive", "revive", "Stand back up from death at full HP/MP.", "revive", CmdRevive, false, true));
        Register(new GmCommand("save", "save", "Make where you stand your save point.", "save", CmdSave, false, true));
        Register(new GmCommand("invuln", "invuln", "Toggle invincibility (no damage taken).", "invuln", CmdInvuln, false, true, "nodmg"));
        Register(new GmCommand("model", "model [name]", "Wear a character model. No name lists the roster.", "model Viking_Male", CmdModel, false, true, "body"));
        Register(new GmCommand("clearemblem", "clearemblem", "Remove your cape emblem (an offensive upload: run it as that player).",
            "clearemblem", CmdClearEmblem, false, true));

        // --- Economy & items ---
        // THE FOUR BELOW CHANGE WHAT SOMEBODY OWNS, and what somebody owns is the server's. Run on a player's own
        // machine they said they had worked and had not: the reply read "Gave 5x Healing Potion" while the bag
        // went from nothing to nothing, and an added coin vanished the instant the server next spoke.
        Register(new GmCommand("gold", "gold <n>", "Add " + Wallet.Name + " (may be negative).", "gold 5000", CmdGold, false, true, "zeny"));
        Register(new GmCommand("item", "item <id|name> [count]", "Drop an item into the bag.", "item \"watery ink\" 10", CmdItem, false, true, "give"));
        Register(new GmCommand("weapon", "weapon [type] [trademark] [folds] [grade] [rarity]",
            "Put a new weapon in your bag. Type = sword/bow/…; trademark = one of that weapon's skills a new weapon " +
            "can roll (quote a name with spaces); folds = a number up to the grade's cap; grade = Raw or F to A; " +
            "rarity = Common to Legendary. Anything left out is random but the rarity, which is Common; the trademark " +
            "is rolled exactly as a new weapon's is.", "weapon sword 3 C Epic",
            CmdWeapon, false, true));
        Register(new GmCommand("armor", "armor [slot|item] [folds] [grade] [rarity]",
            "Put a new armour piece in your bag. Slot = body, garment or shoes (a hat never folds or grades, so it is " +
            "not one); or name the exact piece (quote a name with spaces); folds = a number up to the grade's cap; " +
            "grade = Raw or F to A; rarity = Common to Legendary. Anything left out is random but the rarity, which is " +
            "Common.", "armor body 7 C Epic",
            CmdArmor, false, true, "armour"));
        Register(new GmCommand("mastery", "mastery [weapon] <n>",
            "Testing: set your Weapon Mastery level in a weapon (default: the one in your hands) — one mastery " +
            "point per level, to spend in the Skills window. No arguments reports what you have practised.", "mastery sword 70",
            CmdMastery, false, true));
        Register(new GmCommand("resetskills", "resetskills [weapon|all]",
            "Take back every mastery point spent on a weapon's skills (default: the one in your hands; all = every " +
            "weapon), so they can be spent again.", "resetskills sword", CmdResetSkills, false, true));
        Register(new GmCommand("clearbag", "clearbag", "Empty the bag (worn gear is left alone).", "clearbag", CmdClearBag, false, true, "clearinv", "clearinventory"));
        Register(new GmCommand("bind", "bind <skillId>", "Bind a composed skill to the first empty hotbar slot.", "bind skill.blink_step", CmdBind, false, true));

        // --- Mobs ---
        Register(new GmCommand("kill", "kill", "Kill the mob nearest you, in your map.", "kill", CmdKill,
                               anyoneMayRun: false, changesTheWorld: true));
        Register(new GmCommand("killall", "killall", "Kill every mob in your map.", "killall", CmdKillAll,
                               anyoneMayRun: false, changesTheWorld: true) { worksWithNobody = true });
        Register(new GmCommand("dummy", "dummy", "Put a training dummy in front of you — an immobile target with "
                               + "enormous health that never fights back, for measuring what your build actually does. "
                               + "The server owns it, so everybody can see and hit the same one.", "dummy", CmdDummy,
                               anyoneMayRun: false, changesTheWorld: true));
        Register(new GmCommand("summon", "summon <count> <normal|elite|shadow|creature> [level] [norespawn]", "Summon N creatures near you: random ones of a kind (normal, elite or shadow), or a named creature by id or name. Give a level to re-stat each one at that level with a randomly weighted spread that spends exactly the stat points that level earns. Add 'norespawn' and each one, killed once, never comes back.", "summon 3 adder 10 norespawn", CmdSummon,
                               anyoneMayRun: false, changesTheWorld: true));
        Register(new GmCommand("unsummon", "unsummon", "Remove every creature /summon made in your map. They vanish without dying — no drops, no experience — and never come back. The map's own creatures are not touched.", "unsummon", CmdUnsummon,
                               anyoneMayRun: false, changesTheWorld: true));
        // ANY PLAYER MAY ASK THESE, and the SERVER answers both: a creature's numbers are worked out on the machine that
        // owns the world, never on a player's, so these ride the server's door although they change nothing.
        Register(new GmCommand("whereis", "whereis <creature>", "List every map a creature lives in, and how many of it "
                               + "each keeps alive.", "whereis adder", CmdWhereIs, anyoneMayRun: true, changesTheWorld: true) { worksWithNobody = true });
        Register(new GmCommand("mobinfo", "mobinfo <creature>", "Show a creature's level, size, element, combat numbers, "
                               + "stats, experience and drops, as it spawns.", "mobinfo adder", CmdMobInfo,
                               anyoneMayRun: true, changesTheWorld: true) { worksWithNobody = true });
        Register(new GmCommand("whodrops", "whodrops <item>", "List every creature that drops an item, and at what "
                               + "chance.", "whodrops watery ink", CmdWhoDrops, anyoneMayRun: true, changesTheWorld: true) { worksWithNobody = true });
        Register(new GmCommand("iteminfo", "iteminfo <item>", "Show what an item is — its type, weight, sell value, "
                               + "what it does when used — and every creature that drops it.", "iteminfo watery ink", CmdItemInfo,
                               anyoneMayRun: true, changesTheWorld: true) { worksWithNobody = true });
        Register(new GmCommand("where", "where", "Print the map you are in and your position in that map's own coordinates — the numbers a spawn area's Center takes in the world spawn list.", "where", CmdWhere, false, false, "pos"));

        // --- World & weather ---
        Register(new GmCommand("tp", "tp <map id> [spawnId]", "Travel to another map.", "tp lst_fild01", CmdTp, false, true, "warp", "goto"));
        // THE SERVER'S TOO. Run on one machine these changed one person's sky and nobody else's; held on the server,
        // everybody on it — and everybody who arrives later — sees the same one.
        Register(new GmCommand("time", "time <0..1>", "Set time of day (0=midnight, 0.5=noon).", "time 0.5", CmdTime, false, true) { worksWithNobody = true });
        Register(new GmCommand("weather", "weather <clear|cloudy|rain|storm|snow>", "Force the weather preset.", "weather rain", CmdWeather, false, true) { worksWithNobody = true });
        // A WORLD EVENT: every map server, not only this one — the character server holds the list and hands it to
        // all of them (see WorldEvents). Everyone online is told when it starts and when it ends.
        Register(new GmCommand("exprate", "exprate <duration> <multiplier>  ·  exprate off",
                               "World event: multiply the experience every creature kill pays, on every map server, for "
                               + "a duration (90s, 30m, 2h, 1d). Running it again replaces the rate; off ends it now.",
                               "exprate 2h 1.5", CmdExpRate, false, true) { worksWithNobody = true });
        Register(new GmCommand("speed","speed <mult>", "Multiply your move speed for 9999 seconds (2 h 46 min); dying ends it.", "speed 1.5", CmdSpeed, false, true));
    }

    // ---- handlers (grouped; each just calls into the owning system) --------------

    static void CmdLevel(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.TryInt(0, out var n)) { c.ReplyUsage(); return; }
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
        if (!c.TryInt(0, out var n) || n <= 0) { c.ReplyUsage(); return; }
        int levelsGained = c.Ch.AddXP(n, out int ringLevelsGained);
        GameNetwork.TellOwnerTheyEarned(c.Ch, n, 0, levelsGained, ringLevelsGained);
        GameNetwork.ShellFor(c.Ch)?.SaveSoon();
        c.Reply($"+{n} XP.");
    }

    static void CmdRingXp(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.TryInt(0, out var n) || n <= 0) { c.ReplyUsage(); return; }
        int ringLevelsGained = c.Ch.AddRingXP(n);
        GameNetwork.TellOwnerTheyEarned(c.Ch, 0, n, 0, ringLevelsGained);
        GameNetwork.ShellFor(c.Ch)?.SaveSoon();
        c.Reply($"+{n} ring XP.");
    }

    static void CmdStatPts(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.TryInt(0, out var n) || n <= 0) { c.ReplyUsage(); return; }
        c.Ch.GrantStatPoints(n);
        AfterChangingTheirStats(c.Ch);
        c.Reply($"+{n} stat points.");
    }

    static void CmdHeal(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (c.Ch.IsDead) { GameNetwork.ReviveOnServer(c.Ch); c.Reply("Revived at full HP/MP."); return; }
        c.Ch.Heal(c.Ch.MaxHP);
        c.Ch.GainMana(c.Ch.MaxMP);
        c.Reply("Full HP/MP.");
    }

    // The removal for an offensive emblem: run as that player (from a terminal, tools/gm.py --as <name>). The row is
    // written empty, so it stays gone after they sign in again.
    static void CmdClearEmblem(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        var shell = GameNetwork.ShellFor(c.Ch);
        if (shell == null) { c.Reply("No player shell for that character."); return; }
        shell.RemoveEmblemOnServer();
        c.Reply("Emblem removed.");
    }

    static void CmdRevive(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        // ONLY FROM DEATH. Standing a living character "back up" also starts a new life, which wipes every timed buff
        // and debuff on them — a side effect nobody asking for a refill wants. /heal is the refill.
        if (!c.Ch.IsDead) { c.Reply("You are not dead — /heal refills HP and MP."); return; }
        GameNetwork.ReviveOnServer(c.Ch);
        c.Reply("Revived at full HP/MP.");
    }

    static void CmdSave(GmContext c)
    {
        var shell = GameNetwork.ShellFor(c.Ch);
        if (shell == null) { c.Reply("No character."); return; }
        if (!shell.MakeSavePointHere()) { c.Reply("You are not standing in a map yet."); return; }
        c.Reply("Save point set: " + shell.ServerMapName + " — " + Coords.Text(shell.ServerPositionInMap));
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
        if (!c.TryInt(0, out var n)) { c.ReplyUsage(); return; }
        // THE PERSON WHO ASKED, not "the money on this machine". On a server the asker is one of several people
        // signed in and each has their own; the old call reached for the one figure the whole program used to
        // share, which on a server is nobody's.
        if (c.Ch == null) { c.Reply("No character."); return; }
        c.Ch.AddGold(n);
        c.Reply(Wallet.Name + " → " + Wallet.Amount(c.Ch.Gold) + ".");
    }

    static void CmdItem(GmContext c)
    {
        var key = c.Arg(0);
        if (key == null) { c.ReplyUsage(); return; }
        if (!c.TryInt(1, out var count) || count <= 0) count = 1;

        if (c.Ch == null) { c.Reply("No character."); return; }

        var found = FindItem(key);
        if (found == null) { c.Reply($"No item '{key}'."); return; }

        // The same limits as any other way in (BagRoom) — a command does not get to stuff a bag past them.
        var noRoom = c.Ch.RoomFor(found, count);
        if (noRoom != BagRefusal.None) { c.Reply(c.Ch.RefusalLine(found, noRoom, "give")); return; }
        GiveTo(c.Ch, found, count);
        c.Reply($"Gave {count}× {found.displayName}.");
    }

    // /weapon [type] [trademark] [folds] [grade] [rarity] — the testing door for a new weapon, in any order. Each word
    // is read as a weapon type, a grade, a rarity, a number of folds, or else a trademark. Whatever is left out is
    // random — the trademark rolled exactly as a new weapon's is, the grade drawn from Raw to A, the folds from none to
    // that grade's cap — except the rarity, which is Common unless named. No Ink is written, which is what a piece
    // fresh off the quench carries until somebody inks it.
    static void CmdWeapon(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }

        WeaponType? type = null;
        Quality? grade = null;
        int folds = -1;
        var rarity = Rarity.Common;
        SkillDef trademark = null;
        for (int i = 0; i < c.ArgCount; i++)
        {
            var a = c.Arg(i);
            if (WeaponTypes.TryParse(a, out var t)) { type = t; continue; }
            if (TryName<Quality>(a, out var q)) { grade = q; continue; }
            if (TryName<Rarity>(a, out var r)) { rarity = r; continue; }
            if (int.TryParse(a, out var f) && f >= 0) { folds = f; continue; }
            var skill = FindSkill(a);
            if (skill != null && Trademarks.Qualifies(skill)) { trademark = skill; continue; }
            c.Reply($"Don't know '{a}' — expected a weapon type (sword, bow, ...), a grade (Raw, F to A), " +
                    "a rarity (Common to Legendary), a number of folds, or a trademark skill.");
            return;
        }

        // A NAMED TRADEMARK DECIDES THE WEAPON when none was named, and must be one a new weapon could roll.
        if (trademark != null)
        {
            if (!type.HasValue) type = trademark.weaponType;
            if (!Trademarks.Rollable(trademark, type.Value))
            {
                c.Reply($"{trademark.displayName} can't be a {type.Value} trademark — a new weapon only rolls a published " +
                        $"skill of that weapon.");
                return;
            }
        }

        var quality = grade ?? (Quality)CombatRng.Range((int)Quality.Raw, (int)Quality.A + 1);
        int cap = CraftingMath.FoldCap(quality);
        if (folds > cap)
        {
            c.Reply($"A {(quality == Quality.Raw ? "Raw" : quality.ToString())} piece folds to +{cap} at most.");
            return;
        }
        if (folds < 0) folds = CombatRng.Range(0, cap + 1);

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

        // THROUGH THE ROLLING DOOR, so the trademark is rolled exactly as a new piece's is; a named one replaces it.
        var noRoom = c.Ch.RoomFor(pick, 1);
        if (noRoom != BagRefusal.None) { c.Reply(c.Ch.RefusalLine(pick, noRoom, "make")); return; }
        var inst = EquipmentInstance.CreateFresh(pick);
        if (trademark != null) inst.trademarkSkillId = trademark.id;
        inst.quality = quality;
        inst.upgrade = folds;
        inst.rarity = rarity;
        inst.Recompute();
        c.Ch.TakeIntoBag(inst);
        AfterChangingTheirBag(c.Ch);
        var rolled = Trademarks.SkillOf(inst);
        c.Reply($"Made {ItemTooltip.CopyTitle(inst)} — a {Rarities.Label(rarity)} {pick.displayName}" +
                (rolled != null ? $", trademark {rolled.displayName}." : $", no trademark ({WeaponTypes.Label(pick.weaponType)} has no skill a new weapon can roll)."));
    }

    // /armor [slot|item] [folds] [grade] [rarity] — /weapon's twin for the three armour slots a grade can sit on, in
    // any order. Each word is read as a slot, a grade, a rarity, a number of folds, or else an armour piece's id or
    // name. Whatever is left out is random — the slot, the piece within it, the grade drawn from Raw to A, the folds
    // from none to that grade's cap — except the rarity, which is Common unless named. The word is rolled exactly as
    // a new piece's is.
    static void CmdArmor(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }

        EquipSlotType? slot = null;
        Item named = null;
        Quality? grade = null;
        int folds = -1;
        var rarity = Rarity.Common;
        for (int i = 0; i < c.ArgCount; i++)
        {
            var a = c.Arg(i);
            if (TryArmorSlot(a, out var s)) { slot = s; continue; }
            if (TryName<Quality>(a, out var q)) { grade = q; continue; }
            if (TryName<Rarity>(a, out var r)) { rarity = r; continue; }
            if (int.TryParse(a, out var f) && f >= 0) { folds = f; continue; }
            var it = FindItem(a);
            if (it != null && IsGradedArmorSlot(it.slot)) { named = it; continue; }
            c.Reply($"Don't know '{a}' — expected a slot (body, garment, shoes), a grade (Raw, F to A), " +
                    "a rarity (Common to Legendary), a number of folds, or a body, garment or shoes piece.");
            return;
        }
        if (named != null && slot.HasValue && named.slot != slot.Value)
        {
            c.Reply($"{named.displayName} is not a {ArmorSlotLabel(slot.Value)} piece.");
            return;
        }

        var quality = grade ?? (Quality)CombatRng.Range((int)Quality.Raw, (int)Quality.A + 1);
        int cap = CraftingMath.FoldCap(quality);
        if (folds > cap)
        {
            c.Reply($"A {(quality == Quality.Raw ? "Raw" : quality.ToString())} piece folds to +{cap} at most.");
            return;
        }
        if (folds < 0) folds = CombatRng.Range(0, cap + 1);

        var pick = named;
        if (pick == null)
        {
            int seen = 0;
            foreach (var it in Resources.LoadAll<Item>("Items"))
            {
                if (it == null || !IsGradedArmorSlot(it.slot)) continue;
                if (slot.HasValue && it.slot != slot.Value) continue;
                if (CombatRng.Range(0, ++seen) == 0) pick = it;   // reservoir pick — one pass, no list
            }
        }
        if (pick == null)
        {
            c.Reply(slot.HasValue ? $"No {ArmorSlotLabel(slot.Value)} piece authored." : "No armour items authored.");
            return;
        }

        var noRoom = c.Ch.RoomFor(pick, 1);
        if (noRoom != BagRefusal.None) { c.Reply(c.Ch.RefusalLine(pick, noRoom, "make")); return; }
        var inst = EquipmentInstance.CreateFresh(pick);
        inst.quality = quality;
        inst.upgrade = folds;
        inst.rarity = rarity;
        inst.Recompute();
        c.Ch.TakeIntoBag(inst);
        AfterChangingTheirBag(c.Ch);
        c.Reply($"Made {ItemTooltip.CopyTitle(inst)} — a {Rarities.Label(rarity)} {pick.displayName} ({ArmorSlotLabel(pick.slot)}).");
    }

    // The three armour slots a piece can be folded, graded and given a rarity in — the three the set's aura reads.
    static bool IsGradedArmorSlot(EquipSlotType s) =>
        s == EquipSlotType.Armor || s == EquipSlotType.Garment || s == EquipSlotType.Boots;

    static string ArmorSlotLabel(EquipSlotType s) =>
        s == EquipSlotType.Armor ? "body" : s == EquipSlotType.Garment ? "garment" : s == EquipSlotType.Boots ? "shoes" : s.ToString();

    static bool TryArmorSlot(string typed, out EquipSlotType slot)
    {
        switch (typed?.ToLowerInvariant())
        {
            case "body": case "armor": case "armour": case "chest": slot = EquipSlotType.Armor; return true;
            case "garment": case "cape": case "mantle": case "back": slot = EquipSlotType.Garment; return true;
            case "shoes": case "shoe": case "boots": case "feet": slot = EquipSlotType.Boots; return true;
            default: slot = EquipSlotType.None; return false;
        }
    }

    /// <summary>The skill a player typed: its id, or its name as shown, either case.</summary>
    static SkillDef FindSkill(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var byId = SkillCatalog.Get(key);
        if (byId != null) return byId;
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && string.Equals(all[i].displayName, key, StringComparison.OrdinalIgnoreCase)) return all[i];
        return null;
    }

    // Testing door for the mastery tracks: a level is thousands of kills wide, and a play-test cannot fight
    // them. Sets the practice to exactly what that level costs; the points it gives are spent in the Skills window.
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
                sb.Append(WeaponTypes.Label(w)).Append(' ').Append(lvl).Append(" (").Append(c.Ch.Mastery.PointsLeft(w)).Append(" points left)");
                any = true;
            }
            if (!any) sb.Append("nothing yet");
            sb.Append(". Holding ").Append(held == WeaponType.None ? "nothing" : WeaponTypes.Label(held))
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
            if (WeaponTypes.TryParse(a, out var w)) { weapon = w; continue; }
            if (int.TryParse(a, out int n)) { level = n; continue; }
            c.Reply($"Don't know '{a}' — expected a weapon type or a level.");
            return;
        }
        if (weapon == WeaponType.None) { c.Reply("Hold a weapon, or name one."); return; }
        if (level < 0) { c.ReplyUsage(); return; }

        c.Ch.Mastery.SetPractice(weapon, MasteryMath.WeaponXpFor(level));
        GameNetwork.SaveSoonFor(c.Ch);
        c.Reply($"{WeaponTypes.Label(weapon)} mastery {c.Ch.Mastery.EarnedLevel(weapon)} — {c.Ch.Mastery.PointsLeft(weapon)} mastery " +
                $"point(s) left to spend, {c.Ch.WeaponMasteryLevel} usable with what you are holding.");
    }

    // The only way to take spent mastery points back, until the game has a refund of its own.
    static void CmdResetSkills(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        var weapon = c.Ch.HeldWeapon;
        if (c.ArgCount > 0)
        {
            var a = c.Arg(0);
            if (string.Equals(a, "all", StringComparison.OrdinalIgnoreCase)) weapon = WeaponType.None;
            else if (!WeaponTypes.TryParse(a, out weapon))
            {
                c.Reply($"Don't know '{a}' — expected a weapon type or 'all'.");
                return;
            }
        }
        else if (weapon == WeaponType.None) { c.Reply("Hold a weapon, or name one (or 'all')."); return; }

        int back = c.Ch.Mastery.ResetPoints(weapon);
        c.Ch.RefreshUsableSkills();
        GameNetwork.SaveSoonFor(c.Ch);
        c.Reply(back == 0 ? "No points were spent there."
                          : $"{back} mastery point(s) back on {(weapon == WeaponType.None ? "every weapon" : WeaponTypes.Label(weapon))}.");
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
        if (id == null) { c.ReplyUsage(); return; }
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.Ch.MayBind(id) || c.Ch.Hotbar.BindToFirstEmpty(id) < 0)
        { c.Reply($"Couldn't bind '{id}' (unknown, not learned, already bound, or bar full)."); return; }
        // Bound HERE, so their machine is handed the bar the server now holds — the same push a weapon swap uses,
        // which also rebuilds the skills behind the bar on both sides and redraws it.
        var shell = GameNetwork.ShellFor(c.Ch);
        if (shell != null) { shell.PushBagToOwner(alsoTheBar: true); shell.SaveHotbarSoon(); }
        c.Reply($"Bound {id}.");
    }

    static void CmdKill(GmContext c)
    {
        var m = NearestMob(c.Player, c.MapName);
        if (m == null) { c.Reply("No mob in " + c.MapName + "."); return; }
        // THROUGH THE ONE DAMAGE DOOR, not straight into the sink. Reaching past it skipped the rule about who is
        // allowed to change a creature's health, so on a player's machine this popped a number in the billions over
        // a mob that then shrugged it off — the health write was refused, the popup was not.
        CombatAuthority.ApplyTrue(m.Health, int.MaxValue, TrueDamageCause.GmKill);
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
            c.ReplyUsage();
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
        // Die() mutates the live list, so the kills walk a copy.
        var snapshot = new List<MonsterAI3D>(MonsterAI3D.Active);
        // ONLY THE ASKER'S MAP. A server holds several maps at once, and every creature in all of them is on the one
        // list — so "every mob" used to end other players' fights in maps the asker was not even in.
        string map = c.MapName;
        int n = 0;
        foreach (var m in snapshot)
            if (m != null && m.Health != null && !m.Health.IsDead && m.Health.Map == map)
            { CombatAuthority.ApplyTrue(m.Health, int.MaxValue, TrueDamageCause.GmKill); n++; }
        c.Reply($"Killed {n} mob(s) in {map}.");
    }

    // THE CREATURES /summon MADE, AND NOTHING ELSE — the only ones 'unsummon' may touch. A creature a map spawns for
    // itself, a dummy, and a helper another creature called up are never put here, so none of them can be removed by it.
    static readonly List<GameObject> summonedByCommand = new List<GameObject>();

    // TAKEN OUT OF THE WORLD, NOT KILLED: no death, so no drops, no experience and nothing waiting to come back. The
    // creature's object is destroyed outright, which also ends a return it was already waiting on; its shell on every
    // screen sees it gone and goes with it. Helpers it called up are its own, and go with it (CreatureSummons.OnDestroy).
    static void CmdUnsummon(GmContext c)
    {
        if (!TryAskersMap(c, out var map)) return;

        int removed = 0;
        for (int i = summonedByCommand.Count - 1; i >= 0; i--)
        {
            var go = summonedByCommand[i];
            if (go == null) { summonedByCommand.RemoveAt(i); continue; }
            // ONLY THE ASKER'S MAP, the same line /killall draws.
            if (go.scene != map) continue;

            UnityEngine.Object.Destroy(go);
            summonedByCommand.RemoveAt(i);
            removed++;
        }

        c.Reply(removed == 0 ? $"No summoned creatures in {c.MapName}."
                             : $"Removed {removed} summoned creature(s) from {c.MapName}. They will not come back.");
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
        if (!TryAskersMap(c, out var askerMap)) return;
        if (!c.TryInt(0, out var count) || count <= 0) { c.ReplyUsage(); return; }
        count = Mathf.Clamp(count, 1, 50);

        // A KIND draws at random from that kind; anything else names one creature.
        var type = c.Arg(1);
        CreatureKind? kind = null;
        MonsterDef named = null;
        switch (type != null ? type.ToLowerInvariant() : null)
        {
            case null: c.ReplyUsage(); return;
            case "normal": case "mob": kind = CreatureKind.Normal; break;
            case "elite": kind = CreatureKind.Elite; break;
            case "shadow": kind = CreatureKind.Shadow; break;
            default:
                named = MonsterCatalog.Get(type);
                if (named == null) { c.Reply($"No creature or kind called '{type}'."); c.ReplyUsage(); return; }
                break;
        }

        // Optional trailing LEVEL — re-stats the summoned creature at that level (see the re-stat block below), up to
        // the highest level any creature has (the top shadow).
        // A shadow is a 3D character because it is a shadow — MobSpawner decides that from its kind.
        int level = 0;
        // 'norespawn' — killed once, it never comes back: without the part that brings a creature back, its death
        // ends in its object being destroyed (Health.Die).
        bool noRespawn = false;
        for (int i = 2; i < c.ArgCount; i++)
        {
            var a = c.Arg(i);
            if (int.TryParse(a, out var lv)) { level = Mathf.Clamp(lv, 1, MonsterCatalog.HighestLevel); continue; }
            if (string.Equals(a, "norespawn", System.StringComparison.OrdinalIgnoreCase)) { noRespawn = true; continue; }
            c.ReplyUsage(); return;
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
                    summonedByCommand.Add(go);
                    if (noRespawn)
                    {
                        var comesBack = go.GetComponent<MobRespawn>();
                        if (comesBack != null) UnityEngine.Object.Destroy(comesBack);
                    }
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
                                  (noRespawn ? ", not coming back once killed" : "") +
                                  (missing > 0 ? $" ({missing} skipped — no Addressable prefab)" : "") + ".");
            });
        }
    }

    // WHERE YOU ARE, in the numbers the world spawn list is written in: the map's OWN coordinates, which stay the same
    // wherever a server happens to set that map down. Reads the asker's own machine — nothing changes.
    static readonly List<(string map, int count)> homesScratch = new List<(string map, int count)>();

    static void CmdWhereIs(GmContext c)
    {
        string typed = c.Rest(0);
        if (string.IsNullOrWhiteSpace(typed)) { c.ReplyUsage(); return; }
        var def = MonsterCatalog.Get(typed);
        if (def == null) { c.Reply($"No creature called '{typed}'."); return; }

        WorldSpawns.HomesOf(def, homesScratch);
        if (homesScratch.Count == 0) { c.Reply($"{def.displayName} lives in no map."); return; }
        var sb = new StringBuilder(def.displayName).Append(" — ");
        for (int i = 0; i < homesScratch.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(homesScratch[i].map).Append(" (×").Append(homesScratch[i].count).Append(')');
        }
        c.Reply(sb.ToString());
    }

    static void CmdMobInfo(GmContext c)
    {
        string typed = c.Rest(0);
        if (string.IsNullOrWhiteSpace(typed)) { c.ReplyUsage(); return; }
        var d = MonsterCatalog.Get(typed);
        if (d == null) { c.Reply($"No creature called '{typed}'."); return; }

        // The one wording of a creature's details — the map window's card shows these same lines.
        foreach (var line in MobInfo.Lines(d)) c.Reply(line);
    }

    static readonly List<CreatureDrops.Source> sourcesScratch = new List<CreatureDrops.Source>();

    /// <summary>The item a player typed: its id, or its name as shown, either case.</summary>
    static Item FindItem(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        foreach (var it in ItemCatalog.All)
        {
            if (it == null) continue;
            if (string.Equals(it.id, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(it.displayName, key, StringComparison.OrdinalIgnoreCase)) return it;
        }
        return null;
    }

    static void CmdWhoDrops(GmContext c)
    {
        string typed = c.Rest(0);
        if (string.IsNullOrWhiteSpace(typed)) { c.ReplyUsage(); return; }
        var item = FindItem(typed);
        if (item == null) { c.Reply($"No item called '{typed}'."); return; }
        ReplyWhoDrops(c, item);
    }

    // The most creatures named on one line of /whodrops before the rest are counted instead of named.
    const int MostNamesOnALine = 8;

    /// <summary>Every creature that drops <paramref name="item"/>, grouped by the chance and count they drop it at,
    /// the likeliest first. Shared by /whodrops and /iteminfo.</summary>
    static void ReplyWhoDrops(GmContext c, Item item)
    {
        var groups = new List<(float chance, int min, int max, List<MonsterDef> who)>();
        void Note(MonsterDef d, float chance, int min, int max)
        {
            if (chance <= 0f) return;
            int at = groups.FindIndex(g => g.chance == chance && g.min == min && g.max == max);
            if (at < 0) groups.Add((chance, min, max, new List<MonsterDef> { d }));
            else groups[at].who.Add(d);
        }
        CreatureDrops.WhoDrops(item, sourcesScratch);
        foreach (var s in sourcesScratch) Note(s.creature, s.chance, s.min, s.max);
        if (groups.Count == 0) { c.Reply($"No creature drops {item.displayName}."); return; }

        groups.Sort((a, b) => b.chance.CompareTo(a.chance));
        c.Reply($"<color={NameHex}>{item.displayName}</color> — dropped by:");
        foreach (var g in groups)
        {
            g.who.Sort((a, b) => a.level.CompareTo(b.level));
            var sb = new StringBuilder("  ").Append(LootDropper.ChanceText(g.chance));
            if (g.max > 1) sb.Append(" ×").Append(CreatureDrops.CountText(g.min, g.max));
            sb.Append(" — ");
            for (int i = 0; i < g.who.Count && i < MostNamesOnALine; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(g.who[i].displayName);
            }
            if (g.who.Count > MostNamesOnALine) sb.Append(" and ").Append(g.who.Count - MostNamesOnALine).Append(" more");
            c.Reply(sb.ToString());
        }
    }

    static void CmdItemInfo(GmContext c)
    {
        string typed = c.Rest(0);
        if (string.IsNullOrWhiteSpace(typed)) { c.ReplyUsage(); return; }
        var it = FindItem(typed);
        if (it == null) { c.Reply($"No item called '{typed}'."); return; }

        var type = ItemTypes.Of(it);
        var head = new StringBuilder($"<color={NameHex}>{it.displayName}</color> ({it.id}) — {type}");
        if (type == ItemType.Weapon && it.weaponType != WeaponType.None) head.Append(" · ").Append(WeaponTypes.Label(it.weaponType));
        else if (type == ItemType.Armor) head.Append(" · ").Append(EquipSlots.Label(it.slot));
        if (it.element != Element.Neutral) head.Append(" · ").Append(it.element);
        c.Reply(head.ToString());

        var facts = new StringBuilder($"Weight {it.weight} · " + (it.CanBeSold ? "Value " + Wallet.Amount(it.value) : Item.CannotBeSoldText));
        if (it.cardSlots > 0) facts.Append(" · Card slots ").Append(it.cardSlots);
        if (it.madeOf != MaterialKind.None) facts.Append(" · Made of ").Append(it.madeOf);
        // An Ink's or a Salt's step is its TIER — the word the item's own field uses.
        if (it.materialKind == MaterialKind.Creature)
            facts.Append(" · Creature material · ").Append(Rarities.Of(it, null));
        else if (it.materialKind == MaterialKind.Ink || it.materialKind == MaterialKind.Salt)
            facts.Append(" · ").Append(it.materialKind).Append(" tier ").Append(it.materialStep);
        c.Reply(facts.ToString());

        if (it.IsUsable)
        {
            var skill = SkillCatalog.Get(it.useSkillId);
            c.Reply("Use: " + (skill != null ? skill.displayName : it.useSkillId));
        }
        ReplyWhoDrops(c, it);
    }

    static void CmdWhere(GmContext c)
    {
        var p = c.Player;
        if (p == null) { c.Reply("No character in the world."); return; }
        Vector3 at = MapManager.InMap(p.position, c.MapName);
        c.Reply(c.MapName + " — " + Coords.Text(at));
    }

    static void CmdTp(GmContext c)
    {
        var scene = c.Arg(0);
        if (scene == null) { c.ReplyUsage(); return; }
        // THROUGH THE DOOR EVERY PORTAL USES. Loading the map on the asker's machine moved nobody: the server
        // still had them where they stood, and the map they wanted may be held by another process entirely.
        var shell = GameNetwork.ShellFor(c.Ch);
        if (shell == null) { c.Reply("No character."); return; }
        shell.MoveToMap(scene, c.Arg(1));
        c.Reply($"Traveling to {scene}…");
    }

    static void CmdTime(GmContext c)
    {
        if (!c.TryFloat(0, out var t)) { c.ReplyUsage(); return; }
        var tod = TimeOfDay.Instance;
        if (tod == null) { c.Reply("No clock."); return; }
        t = Mathf.Clamp01(t);
        TimeOfDay.TakeShift(TimeOfDay.ShiftSeconds
                            + TimeOfDay.SecondsToReach(TimeOfDay.ElapsedForTimeOfDay, tod.DayLengthSeconds, t));
        GameNetwork.TellEveryoneTheTimeOfDay();
        c.Reply($"Time → {t:0.00}, on every map this server holds: {MapServerLink.HeldMapsText()}.");
    }

    static void CmdWeather(GmContext c)
    {
        var w = c.Arg(0);
        if (w == null || !ParseWeather(w, out var preset)) { c.ReplyUsage(); return; }
        WeatherDirector.TakeForced(preset);
        GameNetwork.TellEveryoneTheWeather();
        c.Reply($"Weather → {preset}, on every map this server holds: {MapServerLink.HeldMapsText()}.");
    }

    // Asked of the character server, which holds the one list and hands it to every map server. Its answer is this
    // server's copy at once, so the GM's own players hear the change without waiting for the push.
    static void CmdExpRate(GmContext c)
    {
        if (c.Ch == null && c.askedBy == null) { c.Reply("No character."); return; }
        string by = c.askedBy ?? GameNetwork.ShellFor(c.Ch)?.playerName ?? "a GM";
        var reply = c.reply;

        if (c.ArgCount == 1 && string.Equals(c.Arg(0), "off", StringComparison.OrdinalIgnoreCase))
        {
            var stop = JsonUtility.ToJson(new EventStop { kind = WorldEvents.ExperienceRate, stoppedBy = by });
            ServerHttp.Send("POST", MapServerLink.CharacterServerUrl + "/server/events/stop", stop, MapServerLink.ServerSecret,
                (ok, answer, error, _) =>
                {
                    if (!ok) { reply?.Invoke($"<color={FailHex}>Could not end the experience rate — {error}</color>"); return; }
                    WorldEvents.Apply(WorldEvents.Parse(answer), justChanged: true);
                    reply?.Invoke("Experience rate ended on every map server.");
                });
            return;
        }

        if (c.ArgCount != 2 || !WorldEventMath.TryParseDuration(c.Arg(0), out long seconds)
            || !double.TryParse(c.Arg(1), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double multiplier)
            || !(multiplier > 0) || double.IsInfinity(multiplier))
        { c.ReplyUsage(); return; }

        var start = JsonUtility.ToJson(new EventStart
        {
            kind = WorldEvents.ExperienceRate, multiplier = multiplier, seconds = seconds, startedBy = by,
        });
        ServerHttp.Send("POST", MapServerLink.CharacterServerUrl + "/server/events", start, MapServerLink.ServerSecret,
            (ok, answer, error, _) =>
            {
                if (!ok) { reply?.Invoke($"<color={FailHex}>Could not start the experience rate — {error}</color>"); return; }
                WorldEvents.Apply(WorldEvents.Parse(answer), justChanged: true);
                reply?.Invoke($"Experience {WorldEventMath.MultiplierText(multiplier)} for "
                              + $"{WorldEventMath.DurationText(seconds)}, on every map server.");
            });
    }

    [Serializable] class EventStart { public string kind; public double multiplier; public long seconds; public string startedBy; }
    [Serializable] class EventStop { public string kind; public string stoppedBy; }

    static void CmdSpeed(GmContext c)
    {
        if (!c.TryFloat(0, out var s)) { c.ReplyUsage(); return; }
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

    // IN THE ASKER'S MAP ONLY. The maps a server holds share one set of coordinates, so the creature nearest by the
    // numbers can be standing in a different map entirely.
    static MonsterAI3D NearestMob(Transform player, string map)
    {
        if (player == null) return null;
        Vector3 pos = player.position;
        MonsterAI3D best = null; float bestSqr = float.MaxValue;
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (m == null || m.Health == null || m.Health.IsDead || m.Health.Map != map) continue;
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
            default: preset = WeatherDirector.Preset.Clear; return false;
        }
    }

    static void Help(GmContext ctx)
    {
        var one = ctx.Arg(0);
        if (one != null && byName.TryGetValue(one.TrimStart('/'), out var cmd))
        {
            ctx.Reply($"<color={NameHex}>/{ChatController.Sanitize(cmd.usage)}</color> — {cmd.summary}");
            ctx.Reply("  e.g. " + ChatController.Sanitize("/" + cmd.example));
            return;
        }
        // Two groups, by the one thing that decides who may type a command: everyone's first, then the GMs'.
        HelpGroup(ctx, "Commands for everyone:", anyone: true);
        HelpGroup(ctx, "GM commands:", anyone: false);
    }

    static void HelpGroup(GmContext ctx, string title, bool anyone)
    {
        ctx.Reply($"<color={NameHex}>{title}</color>");
        foreach (var c in all)
            if (c.anyoneMayRun == anyone)
                ctx.Reply($"  <color={NameHex}>/{c.name}</color> — {c.summary}  e.g. {ChatController.Sanitize("/" + c.example)}");
    }

    /// <summary>What a line typed wrong is answered with, and what <c>/help</c> shows for one command. The usage keeps
    /// its angle brackets visible: the chat draws command replies as styled text, where <c>&lt;n&gt;</c> is read as a
    /// style tag and vanishes.</summary>
    public static string UsageLine(GmCommand cmd)
        => ChatController.Sanitize("Usage: /" + cmd.usage + "  ·  e.g. /" + cmd.example);

    /// <summary>An enum member by its NAME only, any case. Never <c>Enum.TryParse</c> on a typed word: it also takes
    /// a bare number, so the 70 in <c>/mastery sword 70</c> was read as weapon type 70 and the level was never set.</summary>
    static bool TryName<T>(string typed, out T value) where T : struct, Enum
    {
        if (!string.IsNullOrEmpty(typed))
            foreach (T v in Enum.GetValues(typeof(T)))
                if (string.Equals(v.ToString(), typed, StringComparison.OrdinalIgnoreCase)) { value = v; return true; }
        value = default;
        return false;
    }
}
