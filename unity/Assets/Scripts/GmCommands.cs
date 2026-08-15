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

    /// <summary>Parse a "/name arg arg" line and run the matching command, sending any output to
    /// <paramref name="reply"/> (the chat log). Unknown names and handler exceptions reply an error rather
    /// than throwing, so a bad command can never take the chat down.</summary>
    public static void Execute(string line, Action<string> reply)
    {
        EnsureBootstrap();
        if (!IsCommand(line)) return;

        var tokens = line.Trim().Substring(1).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) { reply?.Invoke("Type a command after '/'. Try /help."); return; }

        var name = tokens[0];
        if (!byName.TryGetValue(name, out var cmd))
        {
            reply?.Invoke($"Unknown command '/{name}'. Type /help.");
            return;
        }

        var args = new string[tokens.Length - 1];
        Array.Copy(tokens, 1, args, 0, args.Length);

        var ctx = new GmContext { args = args, reply = reply };
        try { cmd.run(ctx); }
        catch (Exception e) { reply?.Invoke($"<color=#FF8080>/{name} failed: {e.Message}</color>"); }
    }

    // Registers every command once, on first use. Declare new gameplay/GM commands here.
    static void EnsureBootstrap()
    {
        if (bootstrapped) return;
        bootstrapped = true;
        Register(new GmCommand("help", "help [command]", "List commands, or show one command's usage.", Help, "h", "commands"));

        // --- Character ---
        Register(new GmCommand("lvl", "lvl <n>", "Set character level.", CmdLevel, "level"));
        Register(new GmCommand("xp", "xp <n>", "Grant character XP.", CmdXp, "exp"));
        Register(new GmCommand("ringxp", "ringxp <n>", "Grant Ring of Progression XP.", CmdRingXp));
        Register(new GmCommand("statpts", "statpts <n>", "Grant allocatable stat points.", CmdStatPts, "sp"));
        Register(new GmCommand("heal", "heal", "Full-heal (revives if dead).", CmdHeal));
        Register(new GmCommand("revive", "revive", "Revive the player at full HP/MP.", CmdRevive));
        Register(new GmCommand("invuln", "invuln", "Toggle invincibility (no damage taken).", CmdInvuln, "nodmg"));

        // --- Economy & items ---
        Register(new GmCommand("gold", "gold <n>", "Add Zeny (may be negative).", CmdGold, "zeny"));
        Register(new GmCommand("item", "item <id|name> [count]", "Drop an item into the bag.", CmdItem, "give"));
        Register(new GmCommand("weapon", "weapon [type] [rarity]",
            "Roll a weapon into your bag. Type = sword/bow/… (default: a random weapon base). Rarity = common/uncommon/" +
            "rare/epic/legendary (default: rolled off the drop table). Its skills are rolled from the published pool " +
            "of that weapon type.", CmdWeapon, "drop"));
        Register(new GmCommand("clearbag", "clearbag", "Empty the bag (worn gear is left alone).", CmdClearBag, "clearinv", "clearinventory"));
        Register(new GmCommand("bind", "bind <skillId>", "Bind a composed skill to the first empty hotbar slot.", CmdBind));

        // --- Mobs ---
        Register(new GmCommand("kill", "kill", "Kill the mob nearest the player.", CmdKill));
        Register(new GmCommand("killall", "killall", "Kill every active mob.", CmdKillAll));
        Register(new GmCommand("spawn", "spawn [count]", "Clone the nearest mob near the player.", CmdSpawn));
        Register(new GmCommand("summon", "summon <count> <normal|boss> [level] [shadow]", "Summon N random mobs near you (clones live mobs of that KIND — normal or boss). Give a level to re-stat each one at that level with a randomly weighted spread that spends exactly the stat points that level earns. Add 'shadow' to render them as live-3D characters instead of sprites.", CmdSummon));

        // --- World & weather ---
        Register(new GmCommand("tp", "tp <scene> [spawnId]", "Travel to another map.", CmdTp, "warp", "goto"));
        Register(new GmCommand("time", "time <0..1>", "Set time of day (0=midnight, 0.5=noon).", CmdTime));
        Register(new GmCommand("weather", "weather <clear|cloudy|rain|storm|snow|fog>", "Force the weather preset.", CmdWeather));
        Register(new GmCommand("speed", "speed <mult>", "Set a persistent move-speed multiplier on the player.", CmdSpeed));
    }

    // ---- handlers (grouped; each just calls into the owning system) --------------

    static readonly Color KillColor = new Color(1f, 0.3f, 0.3f);

    static void CmdLevel(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.TryInt(0, out var n)) { c.Reply("Usage: /lvl <n>"); return; }
        c.Ch.SetLevel(Mathf.Max(1, n));
        c.Reply($"Level → {c.Ch.Level}.");
    }

    static void CmdXp(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.TryInt(0, out var n) || n <= 0) { c.Reply("Usage: /xp <n>"); return; }
        c.Ch.AddXP(n);
        c.Reply($"+{n} XP.");
    }

    static void CmdRingXp(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.TryInt(0, out var n) || n <= 0) { c.Reply("Usage: /ringxp <n>"); return; }
        c.Ch.AddRingXP(n);
        c.Reply($"+{n} ring XP.");
    }

    static void CmdStatPts(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.TryInt(0, out var n) || n <= 0) { c.Reply("Usage: /statpts <n>"); return; }
        c.Ch.GrantStatPoints(n);
        c.Reply($"+{n} stat points.");
    }

    static void CmdHeal(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (c.Ch.IsDead) { c.Ch.Revive(); c.Reply("Revived at full HP/MP."); }
        else { c.Ch.Heal(c.Ch.MaxHP); c.Reply("Full HP."); }
    }

    static void CmdRevive(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        c.Ch.Revive();
        c.Reply("Revived at full HP/MP.");
    }

    static void CmdInvuln(GmContext c)
    {
        if (c.Ch == null) { c.Reply("No character."); return; }
        c.Ch.Invincible = !c.Ch.Invincible;
        c.Reply(c.Ch.Invincible ? "Invincibility ON." : "Invincibility OFF.");
    }

    static void CmdGold(GmContext c)
    {
        if (!c.TryInt(0, out var n)) { c.Reply("Usage: /gold <n>"); return; }
        Wallet.Add(n);
        c.Reply($"Zeny → {Wallet.Gold}.");
    }

    static void CmdItem(GmContext c)
    {
        var key = c.Arg(0);
        if (key == null) { c.Reply("Usage: /item <id|name> [count]"); return; }
        if (!c.TryInt(1, out var count) || count <= 0) count = 1;

        var store = InventoryStore.Instance;
        if (store == null) { c.Reply("Inventory not ready."); return; }

        Item found = null;
        foreach (var it in Resources.LoadAll<Item>("Items"))
        {
            if (it == null) continue;
            if (string.Equals(it.id, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(it.displayName, key, StringComparison.OrdinalIgnoreCase)) { found = it; break; }
        }
        if (found == null) { c.Reply($"No item '{key}'."); return; }

        store.Give(found, count);
        c.Reply($"Gave {count}× {found.displayName}.");
    }

    // /weapon [type] [rarity] — the testing door for the drop roll. Both args are optional and order-free: whichever
    // token parses as a weapon type is the base, whichever parses as a rarity forces the roll.
    static void CmdWeapon(GmContext c)
    {
        var store = InventoryStore.Instance;
        if (store == null) { c.Reply("Inventory not ready."); return; }

        WeaponType? type = null;
        Rarity? rarity = null;
        for (int i = 0; i < c.ArgCount; i++)
        {
            var a = c.Arg(i);
            if (Enum.TryParse<WeaponType>(a, true, out var t) && t != WeaponType.None) { type = t; continue; }
            if (Enum.TryParse<Rarity>(a, true, out var r)) { rarity = r; continue; }
            c.Reply($"Don't know '{a}' — expected a weapon type (sword, bow, …) or a rarity (common … legendary).");
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

        var inst = store.GiveEquipment(pick, rarity);
        if (inst == null) { c.Reply("Couldn't create the weapon."); return; }

        WeaponSkillRoll.PoolCounts(pick.weaponType, out int ults, out int acts, out int pass);
        var sb = new StringBuilder();
        sb.Append(Rarities.Label(inst.rarity)).Append(' ').Append(pick.displayName);
        if (inst.skillIds.Count == 0)
            sb.Append(" — rolled NO skills (published pool for ").Append(pick.weaponType)
              .Append(": ").Append(ults).Append(" ult / ").Append(acts).Append(" active / ").Append(pass)
              .Append(" passive). Tick 'published' in the Skill Composer.");
        else
        {
            sb.Append(" —");
            for (int i = 0; i < inst.skillIds.Count; i++)
            {
                var d = SkillCatalog.Get(inst.skillIds[i]);
                sb.Append(i == 0 ? " " : ", ").Append(d != null ? d.displayName : inst.skillIds[i]);
                if (d != null && d.IsUltimate) sb.Append(" (ult)");
                else if (d != null && d.IsPassive) sb.Append(" (passive)");
            }
        }
        c.Reply(sb.ToString());
    }

    static void CmdClearBag(GmContext c)
    {
        var store = InventoryStore.Instance;
        if (store == null) { c.Reply("Inventory not ready."); return; }
        int n = store.ClearAll();
        if (SaveManager.Instance != null) SaveManager.Instance.SaveNow();   // don't let a reload resurrect the bag
        c.Reply(n == 0 ? "Bag was already empty." : $"Cleared {n} stack(s). Worn gear untouched.");
    }

    static void CmdBind(GmContext c)
    {
        var id = c.Arg(0);
        if (id == null) { c.Reply("Usage: /bind <skillId>"); return; }
        if (c.Ch == null) { c.Reply("No character."); return; }
        if (!c.Ch.Hotbar.AutoBind(id)) { c.Reply($"Couldn't bind '{id}' (unknown, already bound, or bar full)."); return; }
        HotbarPanel.RebuildRequested?.Invoke();
        if (SaveManager.Instance != null) SaveManager.Instance.SaveNow();
        c.Reply($"Bound {id} (ultimates → ult slot).");
    }

    static void CmdKill(GmContext c)
    {
        var m = NearestMob(c.Player);
        if (m == null) { c.Reply("No mob nearby."); return; }
        m.Health.TakeTrueDamage(int.MaxValue, KillColor);
        c.Reply("Killed nearest mob.");
    }

    static void CmdKillAll(GmContext c)
    {
        var snapshot = new List<MonsterAI3D>(MonsterAI3D.Active);   // Die() mutates the live list
        int n = 0;
        foreach (var m in snapshot)
            if (m != null && m.Health != null && !m.Health.IsDead) { m.Health.TakeTrueDamage(int.MaxValue, KillColor); n++; }
        c.Reply($"Killed {n} mob(s).");
    }

    static void CmdSpawn(GmContext c)
    {
        if (!c.TryInt(0, out var count) || count <= 0) count = 1;
        count = Mathf.Clamp(count, 1, 50);

        var near = NearestMob(c.Player);
        if (near == null) { c.Reply("No mob to clone (spawn duplicates an existing mob)."); return; }

        var p = c.Player;
        Vector3 basePos = p != null ? p.position : near.transform.position;
        for (int i = 0; i < count; i++)
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle * 2f;
            UnityEngine.Object.Instantiate(near.gameObject, basePos + new Vector3(r.x, 0f, r.y), near.transform.rotation)
                .hideFlags = HideFlags.DontSaveInEditor;
        }
        c.Reply($"Spawned {count}× {near.name}.");
    }

    // "random mobs" = pull random defs from the mob catalog and spawn them from their Addressable prefabs
    // (see MobSpawner / Bake Mob Prefabs). Normal = weighted field pick; boss = a random boss-flagged def.
    // Spawning is async (Addressables loads the prefab), so we count completions and reply once all land.
    static void CmdSummon(GmContext c)
    {
        const string usage = "Usage: /summon <count> <normal|boss> [level] [shadow]";
        if (!c.TryInt(0, out var count) || count <= 0) { c.Reply(usage); return; }
        count = Mathf.Clamp(count, 1, 50);

        var type = c.Arg(1);
        bool wantBoss;
        switch (type != null ? type.ToLowerInvariant() : null)
        {
            case "boss": case "mvp": wantBoss = true; break;
            case "normal": case "mob": wantBoss = false; break;
            default: c.Reply(usage); return;
        }

        // Optional trailing modifiers, order-free because one is a number and the other a word:
        //   a LEVEL  — re-stats the summoned mob at that level (see the re-stat block below);
        //   'shadow' — a VISUAL variant, orthogonal to the normal/boss kind, so it rides the one summon path
        //              rather than forking a second command that would have to be kept in step with this one.
        int level = 0;
        bool asShadow = false;
        for (int i = 2; i < c.ArgCount; i++)
        {
            var a = c.Arg(i);
            if (int.TryParse(a, out var lv)) { level = Mathf.Clamp(lv, 1, 99); continue; }
            if (a.ToLowerInvariant() == "shadow") { asShadow = true; continue; }
            c.Reply(usage); return;
        }

        var defs = new List<MonsterDef>(count);
        for (int i = 0; i < count; i++)
        {
            var def = wantBoss ? MonsterCatalog.RandomBoss() : MonsterCatalog.WeightedPick();
            if (def == null) break;
            defs.Add(def);
        }
        if (defs.Count == 0)
        { c.Reply(wantBoss ? "No boss-flagged mobs in the catalog." : "No summonable mobs in the catalog."); return; }

        var reply = c.reply;   // capture the chat channel; the GmContext is transient but this Action persists
        var p = c.Player;
        Vector3 basePos = p != null ? p.position : Vector3.zero;
        int pending = defs.Count, done = 0, spawned = 0, missing = 0;

        foreach (var def in defs)
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle * 2f;
            MobSpawner.Spawn(def, basePos + new Vector3(r.x, 0f, r.y), go =>
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
                    // Purely the visual: the mob keeps the brain, stats, loot and death it just spawned with.
                    if (asShadow) go.AddComponent<MobShadow3DVisual>();
                }
                else missing++;
                if (++done < pending) return;

                if (spawned == 0)
                    reply?.Invoke("Couldn't summon — mob prefabs aren't built yet. Run Tools ▸ unwritten ▸ Mobs & NPCs ▸ Bake Mob Prefabs, then retry.");
                else
                    reply?.Invoke($"Summoned {spawned} random {(wantBoss ? "boss" : "normal")} " +
                                  (asShadow ? "shadow(s)" : "mob(s)") +
                                  (level > 0 ? $" at level {level}" : "") +
                                  (missing > 0 ? $" ({missing} skipped — no Addressable prefab)" : "") + ".");
            });
        }
    }

    static void CmdTp(GmContext c)
    {
        var scene = c.Arg(0);
        if (scene == null) { c.Reply("Usage: /tp <scene> [spawnId]"); return; }
        MapManager.Travel(scene, c.Arg(1));
        c.Reply($"Traveling to {scene}…");
    }

    static void CmdTime(GmContext c)
    {
        if (!c.TryFloat(0, out var t)) { c.Reply("Usage: /time <0..1>"); return; }
        var tod = TimeOfDay.Instance;
        if (tod == null) { c.Reply("No clock."); return; }
        tod.time01 = Mathf.Clamp01(t);
        c.Reply($"Time → {tod.time01:0.00}.");
    }

    static void CmdWeather(GmContext c)
    {
        var w = c.Arg(0);
        if (w == null || !ParseWeather(w, out var preset)) { c.Reply("Usage: /weather <clear|cloudy|rain|storm|snow|fog>"); return; }
        var wx = WeatherDirector.Instance;
        if (wx == null) { c.Reply("No weather director."); return; }
        wx.autoFromCalendar = false;
        wx.forcedPreset = preset;
        c.Reply($"Weather → {preset}.");
    }

    static void CmdSpeed(GmContext c)
    {
        if (!c.TryFloat(0, out var s)) { c.Reply("Usage: /speed <mult>"); return; }
        var p = c.Player;
        if (p == null) { c.Reply("No player."); return; }
        Status.Of(p.gameObject).Haste(Mathf.Max(0.1f, s), 9999f);
        c.Reply($"Move speed ×{s:0.0}.");
    }

    // ---- shared helpers ---------------------------------------------------------

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
