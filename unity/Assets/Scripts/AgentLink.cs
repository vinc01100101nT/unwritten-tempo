using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Lets a program play this client the way a person does — read what is on the screen, as text, and give the
/// orders a click or a key gives. Off unless the game is started with <c>-agent-port &lt;port&gt;</c>, and then it
/// listens on this computer only (127.0.0.1), never on the network.
///
/// <para><b>It decides nothing.</b> Every order goes through the same door the mouse or keyboard reaches — the
/// click orders on <see cref="Player3DController"/>, a hotbar skill's own <see cref="IHotbarSkill.Activate"/>, the
/// bag's own use and equip, the chat box's own send — so the server checks it exactly as it checks a person. Every
/// number in the screen text is one this machine was already holding to draw.</para>
///
/// <para><b>The conversation.</b> One line in; the answer is some lines of text followed by a line reading
/// <c>&lt;&lt;END&gt;&gt;</c>. A line may hold several commands separated by <c>;</c>, run one after another, and then
/// the answer is one line per command saying how it ended, and the screen once at the end. A command that starts
/// something (walk, fight, cast) ends when it is over or something interrupts it, and says which. <c>help</c> lists
/// them.</para>
///
/// <para><b>Started with no window</b> (<c>-batchmode -nographics</c>) it holds the game to 30 frames a second, since
/// nothing is drawn, and keeps the render pipeline from trying to draw anything at all.</para>
/// </summary>
public class AgentLink : MonoBehaviour
{
    const string EndMarker = "<<END>>";
    const float MaxWaitSeconds = 30f;
    // The longest any single command may wait when it asks for more than the default: a `wait 90` for a respawn
    // timer, or entering the world. The terminal script's socket waits at least this long per command.
    const float LongestWaitSeconds = 120f;
    // How long the world must go without anything new arriving before an arrival counts as settled: a map's creatures,
    // people and doors come in over a few frames after the player is standing in it, and a screen read before then
    // is missing the thing about to hit them (MEASURED: a 901-HP creature 1.6 m away absent from the first screen).
    const float SettleSeconds = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (GameServer.IsServer) return;
        int port = PortFromCommandLine();
        if (port <= 0) return;

        if (Application.isBatchMode)
        {
            // Nothing is drawn without a screen, so nothing needs more than a steady, cheap tick.
            Application.targetFrameRate = 30;
        }

        var go = new GameObject("AgentLink") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);
        go.AddComponent<AgentLink>().Listen(port);
    }

    static int PortFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "-agent-port" && int.TryParse(args[i + 1], out int p)) return p;
        return 0;
    }

    // ---- no window: keep the cameras from drawing ------------------------------------------------------------------

    // With no graphics device every camera the pipeline draws fails, several lines a frame, into the log file
    // (MEASURED: about 190 lines a second, 24 MB an hour). The cameras stay — Camera.main and the screen maths that
    // reads them keep working — but as OVERLAY cameras in no stack, which the pipeline skips without a word. New
    // cameras appear at sign-in, for the character preview and on each map, so this sweeps once a second.
    float nextSweep;

    void StopDrawing()
    {
        if (!Application.isBatchMode || Time.unscaledTime < nextSweep) return;
        nextSweep = Time.unscaledTime + 1f;
        var cams = Camera.allCameras;   // perf-ok: once a second, a handful of cameras, no window
        for (int i = 0; i < cams.Length; i++)
        {
            var data = cams[i].GetUniversalAdditionalCameraData();
            if (data != null && data.renderType != CameraRenderType.Overlay) data.renderType = CameraRenderType.Overlay;
        }
    }

    // ---- the socket: a background thread that hands each line to the main thread and waits for its answer --------

    sealed class Request
    {
        public string line;
        public string answer;
        public readonly ManualResetEventSlim done = new ManualResetEventSlim(false);
    }

    readonly ConcurrentQueue<Request> inbox = new ConcurrentQueue<Request>();
    TcpListener listener;
    Thread acceptThread;
    volatile bool closing;

    void Listen(int port)
    {
        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "AgentLink" };
        acceptThread.Start();
        Debug.Log("[AgentLink] listening on 127.0.0.1:" + port);
        ChatController.LineAdded += OnChatLine;
        BattleNotice.Warned += OnWarning;
        DamagePopup.Shown += OnPopup;
    }

    void AcceptLoop()
    {
        while (!closing)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); }
            catch (Exception) { return; }
            try { Serve(client); }
            catch (Exception) { /* the other end hung up — wait for the next one */ }
            finally { client.Close(); }
        }
    }

    void Serve(TcpClient client)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
        string line;
        while (!closing && (line = reader.ReadLine()) != null)
        {
            if (line.Trim().Length == 0) continue;
            var req = new Request { line = line.Trim() };
            inbox.Enqueue(req);
            req.done.Wait();
            writer.WriteLine(req.answer);
            writer.WriteLine(EndMarker);
        }
    }

    void OnDestroy()
    {
        closing = true;
        ChatController.LineAdded -= OnChatLine;
        BattleNotice.Warned -= OnWarning;
        DamagePopup.Shown -= OnPopup;
        try { listener?.Stop(); } catch (Exception) { }
    }

    // ---- the main thread: one line at a time, one command of it after another -------------------------------------

    Request current;
    readonly List<string> batch = new List<string>();
    int step;
    string stepCommand;
    readonly StringBuilder output = new StringBuilder(1024);
    bool showScreen, fullScreen;

    Func<string> finished;      // polled once a frame while a command waits: null = still waiting, else why it ended
    string timedOut;            // what to say when the wait runs out
    Func<string> textAnswer;    // a command whose whole answer is text (help, bag, a sign-in), with no screen
    float startedAt, giveUpAt;
    int hpAtStart;
    bool deadAtStart;

    void Update()
    {
        StopDrawing();
        if (current == null)
        {
            if (!inbox.TryDequeue(out current)) return;
            Split(current.line, batch);
            step = 0;
            output.Clear();
            showScreen = fullScreen = false;
        }

        while (current != null)
        {
            if (finished != null)
            {
                string why;
                try { why = finished(); }
                catch (Exception e) { why = "error: " + e.Message; }
                if (why == null && Time.unscaledTime < giveUpAt) return;   // still waiting — look again next frame
                finished = null;
                EndStep(why ?? timedOut);
            }
            if (step >= batch.Count) { Reply(); return; }

            stepCommand = batch[step++];
            startedAt = Time.unscaledTime;
            giveUpAt = startedAt + MaxWaitSeconds;
            timedOut = MaxWaitSeconds + "s passed";
            hpAtStart = Character.Instance != null ? Character.Instance.CurrentHP : 0;
            deadAtStart = Dead();
            textAnswer = null;
            said = null;
            try { Begin(stepCommand); }
            catch (Exception e) { finished = null; textAnswer = null; EndStep("error: " + e.Message); continue; }
            if (finished != null) return;   // the order needs at least a frame to take
            EndStep(said);
        }
    }

    // Several commands on one line, split at ';' — except that a `say` takes the whole rest of the line, so a chat
    // message may hold a semicolon.
    static void Split(string line, List<string> into)
    {
        into.Clear();
        int at = 0;
        while (at < line.Length)
        {
            int cut = line.IndexOf(';', at);
            string piece = (cut < 0 ? line.Substring(at) : line.Substring(at, cut - at)).Trim();
            if (piece.StartsWith("say ", StringComparison.OrdinalIgnoreCase) || piece.Equals("say", StringComparison.OrdinalIgnoreCase))
            {
                string rest = line.Substring(at).Trim();
                while (rest.EndsWith(";")) rest = rest.Substring(0, rest.Length - 1).TrimEnd();   // "say /tp lastline; "
                into.Add(rest);
                return;
            }
            if (piece.Length > 0) into.Add(piece);
            if (cut < 0) return;
            at = cut + 1;
        }
    }

    void EndStep(string why)
    {
        string text;
        if (textAnswer == null) { text = why; showScreen = true; }
        else if (why != null && why.StartsWith("error: ")) text = why;
        else
        {
            try { text = textAnswer(); }
            catch (Exception e) { text = "error: " + e.Message; }
        }
        textAnswer = null;
        if (string.IsNullOrEmpty(text)) return;
        if (batch.Count > 1) output.Append(stepCommand).Append(": ");
        output.Append(text).Append('\n');
    }

    void Reply()
    {
        var req = current;
        current = null;
        finished = null;
        textAnswer = null;
        if (req == null) return;
        string text;
        // Once per line a player sends, never per frame: every frame between answers returns above.
        try { text = showScreen ? output.Append(Screen(fullScreen)).ToString() : output.ToString(); }   // perf-ok: once per answer
        catch (Exception e) { text = output.Append("error: ").Append(e.Message).ToString(); }   // perf-ok: once per answer
        req.answer = text.TrimEnd();
        req.done.Set();
    }

    // Wait until <paramref name="until"/> says why the command is over, or <paramref name="seconds"/> pass.
    void WaitFor(Func<string> until, float seconds, string whenTimeIsUp)
    {
        finished = until;
        giveUpAt = Time.unscaledTime + Mathf.Min(seconds, LongestWaitSeconds);
        timedOut = whenTimeIsUp;
    }

    // Wait a fixed moment for an order to land, then say what was done.
    void WaitMoment(float seconds, string didWhat)
    {
        float until = Time.unscaledTime + seconds;
        WaitFor(() => Time.unscaledTime > until ? didWhat : null, seconds + 1f, didWhat);
    }

    // Answer this command with a line and no wait.
    string said;
    void Say(string line) { said = line; }

    // The whole answer is this text, with no screen after it.
    void Text(Func<string> text) { textAnswer = text; }

    // ---- what the commands are -------------------------------------------------------------------------------------

    const string Help =
        "commands (ids like m3/p1/n2/g4/d1 come from the screen; several per line with ';' — the answer is one line per\n" +
        "command saying how it ended, then the screen once; a say takes the rest of the line):\n" +
        "look                      the whole screen (other answers leave out sections that have not changed)\n" +
        "login <email> <password>  sign in, lists characters\n" +
        "create <name>             make a character\n" +
        "play <name|number>        enter the world; answers once everything nearby has arrived\n" +
        "move <x> <z> | go <id>    walk (ends on arrival, a new map, your HP under 30%, death, or 30s)\n" +
        "attack <m#>               fight a mob (ends when it or you die, your HP drops under 30%, or 30s)\n" +
        "auto on|off [secs]        the auto-pilot fights nearby mobs; answers after secs (default 10)\n" +
        "stop                      drop every order\n" +
        "cast <slot|R|name> [id | x z]  press a hotbar skill, aimed at a unit or a spot\n" +
        "pickup <g#>               walk to a floor item and take it\n" +
        "talk <n#>                 talk to a townsperson\n" +
        "use <bag#|name> | equip <bag#> | unequip <slot>\n" +
        "stat <STR|AGI|VIT|INT|DEX|LUK>  spend a stat point\n" +
        "bag | gear | stats        detail views\n" +
        "ground                    what you are standing on, and everything solid touching you\n" +
        "say <text>                chat; a /command runs like typed\n" +
        "revive                    press respawn when dead\n" +
        "wait <secs>               let time pass (ends early on a hit)\n" +
        "log <category|all> on|off switch this game's own log lines (written to its log file)";

    void Begin(string line)
    {
        var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        string verb = parts[0].ToLowerInvariant();
        string rest = line.Length > parts[0].Length ? line.Substring(parts[0].Length).Trim() : "";

        switch (verb)
        {
            case "help": Text(() => Help); return;
            case "look": fullScreen = true; return;
            case "login": Login(parts); return;
            case "create": Create(rest); return;
            case "play": Play(rest); return;
            case "move": Move(parts); return;
            case "go": Go(parts); return;
            case "attack": Attack(parts); return;
            case "auto": Auto(parts); return;
            case "stop": { var r = Rig(); if (r != null) r.Halt(); Say("stopped"); return; }
            case "cast": Cast(parts); return;
            case "pickup": Pickup(parts); return;
            case "talk": Talk(parts); return;
            case "use": UseItem(rest); return;
            case "equip": EquipItem(rest); return;
            case "unequip": UnequipSlot(rest); return;
            case "stat": SpendStat(rest); return;
            case "ground": Text(GroundText); return;
            case "bag": Text(BagText); return;
            case "gear": Text(GearText); return;
            case "stats": Text(StatsText); return;
            case "say": Chat(rest); return;
            case "revive": Revive(); return;
            case "wait": Wait(parts); return;
            case "log": ClientLog(parts); return;
            default: Text(() => "unknown command '" + verb + "'. " + Help); return;
        }
    }

    // ---- signing in ------------------------------------------------------------------------------------------------

    AccountClient.CharacterInfo[] characters;
    string pendingError;
    bool pendingDone;

    void Login(string[] parts)
    {
        if (parts.Length < 3) { Text(() => "usage: login <email> <password>"); return; }
        pendingDone = false; pendingError = null; characters = null;
        AccountClient.SignIn(parts[1], parts[2], (ok, error) =>
        {
            if (!ok) { pendingError = error ?? "could not sign in"; pendingDone = true; return; }
            AccountClient.ListCharacters((list, err) => { characters = list; pendingError = err; pendingDone = true; });
        });
        WaitFor(() => pendingDone ? "" : null, 20f, "login failed: no answer from the sign-in service after 20s");
        Text(() => pendingError != null ? "login failed: " + pendingError : pendingDone ? CharacterListText() : timedOut);
    }

    string CharacterListText()
    {
        if (characters == null) return "not signed in";
        if (characters.Length == 0) return "signed in. no characters yet — create <name>";
        var sb = new StringBuilder("signed in. characters:");
        for (int i = 0; i < characters.Length; i++)
            sb.Append("\n").Append(i + 1).Append(". ").Append(characters[i].name).Append(" Lv").Append(characters[i].level);
        return sb.ToString();
    }

    void Create(string name)
    {
        if (string.IsNullOrEmpty(AccountSession.AccessToken)) { Text(() => "login first"); return; }
        if (name.Length == 0) { Text(() => "usage: create <name>"); return; }
        pendingDone = false; pendingError = null;
        AccountClient.ListModels((models, err) =>
        {
            if (models == null || models.Length == 0) { pendingError = err ?? "no character models offered"; pendingDone = true; return; }
            AccountClient.CreateCharacter(name, models[0], null, null, (info, e2) =>
            {
                if (info == null) { pendingError = e2 ?? "could not create"; pendingDone = true; return; }
                AccountClient.ListCharacters((list, e3) => { characters = list; pendingError = e3; pendingDone = true; });
            });
        });
        WaitFor(() => pendingDone ? "" : null, 20f, "create failed: no answer from the sign-in service after 20s");
        Text(() => pendingError != null ? "create failed: " + pendingError : pendingDone ? CharacterListText() : timedOut);
    }

    void Play(string which)
    {
        if (characters == null || characters.Length == 0) { Text(() => "login first (and create a character)"); return; }
        AccountClient.CharacterInfo pick = null;
        if (int.TryParse(which, out int n) && n >= 1 && n <= characters.Length) pick = characters[n - 1];
        else
            foreach (var c in characters)
                if (string.Equals(c.name, which, StringComparison.OrdinalIgnoreCase)) pick = c;
        if (pick == null) { Text(() => "no character '" + which + "'. " + CharacterListText()); return; }

        AccountSession.PlayAs(pick.id, pick.name, pick.modelName, pick.dyeSources, pick.dyeTargets);
        var settled = Settling();
        WaitFor(() => InWorld() && settled() ? "in the world" : null, 60f, "not in the world after 60s");
    }

    static bool InWorld() =>
        Character.Instance != null && GameNetwork.MyPlayer != null && !LoadingScreen.Showing
        && !string.IsNullOrEmpty(MapManager.CurrentMap) && Rig() != null;

    // TRUE once nothing new has arrived for a moment — the same test for entering the world and for stepping through
    // a door, so both answer with a screen that has everything on it. What counts as "arrived" is anything that can
    // appear on the screen: things the server sent, creatures, people, floor items, doors, and the map itself.
    static Func<bool> Settling()
    {
        int seenMap = -1, seenCount = -1;
        float quietSince = 0f, firstAsked = -1f;
        return () =>
        {
            if (firstAsked < 0f) firstAsked = Time.unscaledTime;
            int count = Mirror.NetworkClient.spawned.Count + MonsterAI3D.Active.Count + TownNPC.All.Count
                      + GroundDrop.All.Count + Portal3D.Active.Count;
            if (count != seenCount || MapManager.MapVersion != seenMap)
            {
                seenCount = count; seenMap = MapManager.MapVersion;
                quietSince = Time.unscaledTime;
                // A busy map — creatures crossing in and out of sight every second — never goes quiet, so it counts as
                // settled after this long whatever is still arriving.
                return Time.unscaledTime >= firstAsked + SettleSeconds * 5f;
            }
            return Time.unscaledTime >= quietSince + SettleSeconds;
        };
    }

    // ---- orders ----------------------------------------------------------------------------------------------------

    static Player3DController rig;
    static Player3DController Rig()
    {
        if (rig == null) rig = FindAnyObjectByType<Player3DController>();   // perf-ok: on a command, never per frame
        return rig;
    }

    bool Hurt() => Character.Instance != null && Character.Instance.CurrentHP < hpAtStart;
    static bool Dead() => Character.Instance == null || Character.Instance.IsDead;
    // Dying ends a command; being dead already does not, or a wait for the respawn timer could never run.
    bool Died() => !deadAtStart && Dead();

    // The two things that end a wait the moment they happen.
    string DiedOrHurt() => Died() ? "you died" : Hurt() ? "hit" : null;
    // A walk ends only for a blow that matters: a creature nibbling 1 HP a second would otherwise end every walk
    // the moment it started (MEASURED: eight walks in a row answered "hit" at once and moved nobody).
    string DiedOrLow() => Died() ? "you died" : LowHp() ? "HP under 30%" : null;

    void Move(string[] parts)
    {
        var r = Rig();
        if (r == null) { Say("not in the world"); return; }
        if (parts.Length < 3 || !TryNum(parts[1], out float x) || !TryNum(parts[2], out float z))
        { Say("usage: move <x> <z>"); return; }
        WalkTo(new Vector3(x, r.transform.position.y, z), null);
    }

    void Go(string[] parts)
    {
        if (parts.Length < 2 || !TryFind(parts[1], out var target)) { Say("unknown id" + (parts.Length < 2 ? "" : " '" + parts[1] + "'")); return; }
        WalkTo(target.position, target);
    }

    void WalkTo(Vector3 point, Transform target)
    {
        var r = Rig();
        if (r == null) { Say("not in the world"); return; }
        string mapBefore = MapManager.CurrentMap;
        Func<bool> settled = null;
        r.OrderMove(point);
        // A spot that is walked onto ends the order; a door ends it by loading a new map, and the answer waits until the
        // new map's creatures have come in and the old map's doors have gone.
        WaitFor(() =>
        {
            if (MapManager.CurrentMap != mapBefore && !LoadingScreen.Showing)
            {
                if (settled == null) settled = Settling();
                return settled() ? "new map " + MapManager.CurrentMap : null;
            }
            string ended = DiedOrLow();
            if (ended != null) return ended;
            return !LoadingScreen.Showing && r.OrderName == "Idle" && Time.unscaledTime > startedAt + 0.2f ? "arrived" : null;
        }, MaxWaitSeconds, "still walking after " + MaxWaitSeconds + "s");
    }

    void Attack(string[] parts)
    {
        var r = Rig();
        if (r == null) { Say("not in the world"); return; }
        if (parts.Length < 2 || !TryFind(parts[1], out var t)) { Say("unknown id" + (parts.Length < 2 ? "" : " '" + parts[1] + "'")); return; }
        var mob = t.GetComponentInParent<Health>();   // perf-ok: once per command a player sends, never per frame
        if (mob == null || mob.IsDead) { Say(parts[1] + " is not a living mob"); return; }
        string id = parts[1].ToLowerInvariant();
        r.OrderAttack(mob);
        WaitFor(() =>
        {
            if (mob == null || mob.IsDead) return id + " died";
            string ended = DiedOrLow();
            if (ended != null) return ended;
            return r.OrderName == "Idle" && Time.unscaledTime > startedAt + 0.2f ? "stopped fighting " + id : null;
        }, MaxWaitSeconds, "still fighting after " + MaxWaitSeconds + "s");
    }

    static bool LowHp()
    {
        var c = Character.Instance;
        return c != null && c.MaxHP > 0 && c.CurrentHP * 10 < c.MaxHP * 3;
    }

    void Auto(string[] parts)
    {
        var r = Rig();
        if (r == null) { Say("not in the world"); return; }
        bool on = parts.Length < 2 || parts[1].ToLowerInvariant() != "off";
        r.OrderAutoPilot(on);
        if (!on) { Say("auto-pilot off"); return; }
        float secs = Mathf.Min(parts.Length >= 3 && TryNum(parts[2], out float s) ? s : 10f, LongestWaitSeconds);
        WaitFor(DiedOrLow, secs, "auto-pilot on, " + secs + "s passed");
    }

    void Cast(string[] parts)
    {
        if (Dead() || Rig() == null) { Say("cannot cast now"); return; }
        if (parts.Length < 2) { Say("usage: cast <slot|R|name> [id | x z]"); return; }
        var skill = FindHotbarSkill(parts[1], out string label);
        if (skill == null) { Say("no hotbar skill '" + parts[1] + "'"); return; }

        Transform unit = null;
        bool unitIsMe = false;
        bool hasPoint = false;
        Vector3 point = Vector3.zero;
        if (parts.Length == 2) { }
        else if (parts.Length >= 4 && TryNum(parts[2], out float x) && TryNum(parts[3], out float z))
        { hasPoint = true; point = new Vector3(x, Rig().transform.position.y, z); }
        else if (parts[2].ToLowerInvariant() == "me") { unit = Rig().transform; unitIsMe = true; }
        else if (parts[2].Length > 0 && parts[2][0] != 'm' && parts[2][0] != 'p' && parts[2][0] != 'M' && parts[2][0] != 'P')
        { Say("a skill is aimed at a creature (m#), a player (p#), me, or a spot (x z) — not '" + parts[2] + "'"); return; }
        else if (TryFind(parts[2], out unit))
        {
            // A mob named as the target is engaged first, which is what the quick-cast setting fires at.
            var h = unit.GetComponentInParent<Health>();   // perf-ok: once per command a player sends, never per frame
            if (h != null && !unitIsMe && MonsterAI3D.Active.Contains(h.GetComponent<MonsterAI3D>())) Rig().OrderAttack(h);
        }
        else { Say("unknown target '" + parts[2] + "'"); return; }

        if (!skill.Activate()) { Say(label + " did not fire"); return; }

        // Aiming screens the press may have opened, answered the way a click answers them.
        if (UnitTargeter.Active)
        {
            if (unit == null) { UnitTargeter.Cancel(); Say(label + " needs a target: cast " + parts[1] + " <id|me>"); return; }
            UnitTargeter.ConfirmOn(unit, unitIsMe);
        }
        if (GroundTargeter.Active)
        {
            if (!hasPoint && unit != null) { hasPoint = true; point = unit.position; }
            if (!hasPoint) { GroundTargeter.Cancel(); Say(label + " needs a spot: cast " + parts[1] + " <x> <z>"); return; }
            GroundTargeter.ConfirmAt(point);
        }

        var r = Rig();
        // A cast out of range walks first; either way give the wind-up a moment before looking.
        WaitFor(() => Died() ? "you died"
                      : r.OrderName != "MoveIntoRange" && Time.unscaledTime > startedAt + 1.2f ? "cast " + label : null,
                15f, "still walking into range for " + label + " after 15s");
    }

    IHotbarSkill FindHotbarSkill(string key, out string label)
    {
        label = key;
        var me = Character.Instance;
        if (me == null) return null;
        string id = null;
        if (key.Equals("R", StringComparison.OrdinalIgnoreCase)) id = me.Hotbar.Ult;
        else if (int.TryParse(key, out int slot) && HotbarBindings.IsValidSlot(slot - 1)) id = me.Hotbar.Get(slot - 1);
        else
        {
            for (int i = -1; i < HotbarBindings.SlotCount && id == null; i++)
            {
                string candidate = i < 0 ? me.Hotbar.Ult : me.Hotbar.Get(i);
                if (string.IsNullOrEmpty(candidate)) continue;
                string name = SkillName(candidate);
                if (name.StartsWith(key, StringComparison.OrdinalIgnoreCase)) id = candidate;
            }
        }
        if (string.IsNullOrEmpty(id)) return null;
        label = SkillName(id);
        return PlayerSkills.ById(id);
    }

    static string SkillName(string id)
    {
        var runner = PlayerSkills.ById(id) as SkillRunner;
        return runner != null && runner.def != null ? runner.def.displayName : id;
    }

    void Pickup(string[] parts)
    {
        if (Dead() || Character.Instance == null) { Say("cannot pick up now"); return; }
        if (parts.Length < 2 || !TryFind(parts[1], out var t)) { Say("unknown id" + (parts.Length < 2 ? "" : " '" + parts[1] + "'")); return; }
        var drop = t.GetComponent<GroundDrop>();   // perf-ok: once per command a player sends, never per frame
        if (drop == null) { Say(parts[1] + " is not on the floor"); return; }
        string what = drop.Text;
        // A kill's drops vanish on their own after a minute, so "gone from the floor" only means "picked up" when the
        // bag actually gained it (MEASURED: a Bow answered as picked up that had expired instead).
        int bagBefore = BagTotal();
        // Reached the way the reach key reaches: close enough first, then ask.
        if (!Character.Instance.WalkIntoRange(drop.transform, GroundDrop.Reach * 0.6f, () => GroundDrop.AskToTake(drop)))
            GroundDrop.AskToTake(drop);
        WaitFor(() =>
        {
            // THE BAG IS THE ANSWER, asked before anything else: the item is in it a moment before the thing on the
            // floor goes away, and asking the floor first called a good pickup "still on the floor" (MEASURED).
            if (BagTotal() > bagBefore) return "picked up " + what;
            if (drop == null || !drop.Readable) return what + " is gone from the floor (not in your bag)";
            string ended = DiedOrLow();
            if (ended != null) return ended;
            return Rig() != null && Rig().OrderName == "Idle" && Time.unscaledTime > startedAt + 1.5f ? "still on the floor: " + what : null;
        }, 15f, "still reaching for " + what + " after 15s");
    }

    void Talk(string[] parts)
    {
        if (parts.Length < 2 || !TryFind(parts[1], out var t)) { Say("unknown id" + (parts.Length < 2 ? "" : " '" + parts[1] + "'")); return; }
        var npc = t.GetComponent<TownNPC>();   // perf-ok: once per command a player sends, never per frame
        var interactor = Rig() != null ? Rig().GetComponent<PlayerInteractor>() : null;
        if (npc == null || interactor == null) { Say(parts[1] + " is not someone to talk to"); return; }
        interactor.StartTalk(npc);
        var box = DialogBox.Instance;
        if (box == null || box.Lines == null) { Say("they had nothing to say"); return; }
        var said = new StringBuilder(box.Speaker ?? "");   // perf-ok: once per command a player sends, never per frame
        said.Append(":");
        foreach (var l in box.Lines) said.Append("\n  ").Append(Plain(l));
        box.Close();
        Say(said.ToString());
    }

    void UseItem(string which)
    {
        var e = FindBagEntry(which, out int index);
        if (e == null) { Say("no bag item '" + which + "'"); return; }
        if (InventoryStore.Instance == null) { Say("no bag"); return; }
        string name = e.def != null ? e.def.displayName : which;
        InventoryStore.Instance.Consume(e);
        WaitMoment(0.6f, "used " + name);
    }

    void EquipItem(string which)
    {
        var e = FindBagEntry(which, out _);
        if (e == null || !e.IsEquip) { Say("no wearable bag item '" + which + "'"); return; }
        string name = e.def != null ? e.def.displayName : which;
        InventoryStore.Instance?.Equip(e);
        WaitMoment(0.6f, "equipped " + name);
    }

    void UnequipSlot(string which)
    {
        if (!Enum.TryParse(which, true, out EquipSlotType slot) || slot == EquipSlotType.None)
        { Say("slots: " + string.Join(", ", Enum.GetNames(typeof(EquipSlotType)))); return; }
        InventoryStore.Instance?.Unequip(slot);
        WaitMoment(0.6f, "unequipped " + slot);
    }

    // Everything in the bag, counted the way the bag counts it — what a pickup adds to.
    static int BagTotal()
    {
        var bag = Character.Instance != null ? Character.Instance.Bag : null;
        if (bag == null) return 0;
        int total = 0;
        for (int i = 0; i < bag.Count; i++) total += Mathf.Max(1, bag[i].count);
        return total;
    }

    static BagEntry FindBagEntry(string which, out int index)
    {
        index = -1;
        var bag = Character.Instance != null ? Character.Instance.Bag : null;
        if (bag == null || which.Length == 0) return null;
        if (int.TryParse(which, out int n) && n >= 1 && n <= bag.Count) { index = n - 1; return bag[n - 1]; }
        for (int i = 0; i < bag.Count; i++)
            if (bag[i].def != null && bag[i].def.displayName.StartsWith(which, StringComparison.OrdinalIgnoreCase))
            { index = i; return bag[i]; }
        return null;
    }

    void SpendStat(string which)
    {
        if (!Enum.TryParse(which, true, out PrimaryStat stat)) { Say("usage: stat <STR|AGI|VIT|INT|DEX|LUK>"); return; }
        var me = GameNetwork.MyPlayer;
        if (me == null) { Say("not in the world"); return; }
        me.AskToSpendStatPoint(stat);
        float until = Time.unscaledTime + 0.6f;
        WaitFor(() => Time.unscaledTime > until ? "" : null, 2f, "");
        Text(StatsText);
    }

    void Chat(string text)
    {
        // The server cuts a said line at its longest, so a longer one goes out as several, split between words.
        var lines = new List<string>();   // perf-ok: once per chat command, never per frame
        string left = text.Trim();
        while (!left.StartsWith("/") && left.Length > GameNetwork.MostCharactersInALine)
        {
            int cut = left.LastIndexOf(' ', GameNetwork.MostCharactersInALine);
            if (cut <= 0) cut = GameNetwork.MostCharactersInALine;
            lines.Add(left.Substring(0, cut));
            left = left.Substring(cut).Trim();
        }
        lines.Add(left);
        foreach (var l in lines)
            if (!ChatController.Send(l)) { Say("no chat to type into"); return; }
        string sent = lines.Count > 1 ? "said, in " + lines.Count + " lines" : "said";
        if (!text.TrimStart().StartsWith("/")) { WaitMoment(0.5f, sent); return; }
        // A TYPED COMMAND MAY MOVE YOU, and a move hangs up and dials again about a second later with no curtain up
        // in between — so a command is watched for two seconds, and if the world goes away in that time the answer
        // waits for the new map and names it. Without this, a /tp answered half a second later with the map it had
        // just left on the screen (MEASURED 2026-09-18). An ordinary command costs those two seconds and nothing
        // else; there is no list of which commands move you, deliberately — the server owns that and this watches
        // for what actually happens.
        string mapBefore = MapManager.CurrentMap;
        float giveUpWatching = Time.unscaledTime + 2f;
        bool wentAway = false;
        Func<bool> settled = null;
        WaitFor(() =>
        {
            if (!wentAway)
            {
                wentAway = !InWorld() || MapManager.CurrentMap != mapBefore;
                if (!wentAway) return Time.unscaledTime >= giveUpWatching ? sent : null;
            }
            if (!InWorld()) return null;
            if (settled == null) settled = Settling();
            if (!settled()) return null;
            return sent + (MapManager.CurrentMap != mapBefore ? "; new map " + MapManager.CurrentMap : "; back in " + MapManager.CurrentMap);
        }, 30f, sent + "; still moving after 30s");
    }

    void Revive()
    {
        if (!Dead()) { Say("not dead"); return; }
        if (!DeathScreen.PressRespawn()) { Say("respawn is not ready yet — wait a few seconds"); return; }
        WaitFor(() => !Dead() && !LoadingScreen.Showing ? "revived" : null, 15f, "still dead after 15s");
    }

    void Wait(string[] parts)
    {
        float secs = Mathf.Min(parts.Length >= 2 && TryNum(parts[1], out float s) ? s : 5f, LongestWaitSeconds);
        WaitFor(DiedOrHurt, secs, secs + "s passed");
    }

    // ---- ids: short names for the things on screen -----------------------------------------------------------------

    // A thing keeps its id for as long as the map is the same, whatever happens to the object drawing it: the key is
    // what the SERVER calls it (its network number), or for a door, where it stands and where it leads. MEASURED
    // before this: the doors were rebuilt a moment after arrival, so the first screen's d1 was gone by the next.
    readonly Dictionary<string, string> idByKey = new Dictionary<string, string>();
    readonly Dictionary<string, string> keyById = new Dictionary<string, string>();
    readonly Dictionary<string, Component> byId = new Dictionary<string, Component>();
    readonly Dictionary<char, int> nextNumber = new Dictionary<char, int>();
    string idsForMap;

    string IdFor(char kind, Component c) => IdFor(kind, KeyOf(kind, c), c);

    string IdFor(char kind, string key, Component c)
    {
        if (idsForMap != MapManager.CurrentMap) { idByKey.Clear(); keyById.Clear(); byId.Clear(); nextNumber.Clear(); idsForMap = MapManager.CurrentMap; }
        if (!idByKey.TryGetValue(key, out var id))
        {
            nextNumber.TryGetValue(kind, out int n);
            nextNumber[kind] = ++n;
            id = kind.ToString() + n;
            idByKey[key] = id;
            keyById[id] = key;
        }
        byId[id] = c;   // always the live object, however many times it has been rebuilt
        return id;
    }

    static string KeyOf(char kind, Component c)
    {
        switch (kind)
        {
            case 'm': { var shell = NetMob.ShellForView(c.gameObject); return shell != null ? "m" + shell.netId : "m#" + c.GetInstanceID(); }
            case 'n': { var shell = NetNpc.ShellForView(c.gameObject); return shell != null ? "n" + shell.netId : "n#" + c.GetInstanceID(); }
            case 'p': return c is NetPlayer p ? "p" + p.netId : "p#" + c.GetInstanceID();
            case 'g': return c is GroundDrop g && g.netId != 0 ? "g" + g.netId : "g#" + c.GetInstanceID();
            case 'd': return c is Portal3D d ? "d" + d.targetScene + "@" + Pos(d.transform.position) : "d#" + c.GetInstanceID();
            default: return kind + "#" + c.GetInstanceID();
        }
    }

    bool TryFind(string id, out Transform t)
    {
        t = null;
        id = id.ToLowerInvariant();
        if (!byId.TryGetValue(id, out var c) || c == null) return false;
        // A creature's body is banked when it leaves and handed to the next creature of its kind, so the object
        // behind an id is checked to still be the thing the id was given to, not merely to be alive.
        if (keyById.TryGetValue(id, out var key) && KeyOf(id[0], c) != key) return false;
        t = Where(c);
        return t != null && t.gameObject.activeInHierarchy;
    }

    // Where a thing actually stands. Another player's network object never moves on this machine; the body drawn for
    // them does, so that is the one to read and walk to.
    static Transform Where(Component c) => c is NetPlayer p && p.DrawnBody != null ? p.DrawnBody : c.transform;

    // ---- the screen, as text ---------------------------------------------------------------------------------------

    const int MostEvents = 25;
    readonly List<string> events = new List<string>();
    readonly List<int> eventRepeats = new List<int>();
    int eventsDropped;

    void OnChatLine(string line) { AddEvent(Plain(line)); }
    void OnWarning(string line) { AddEvent("! " + line); }

    // The same line again right after itself — a creature's 1-damage nibbles, the regen's +1 — is counted, not
    // repeated: "me 1 x9" is one line where nine were.
    void AddEvent(string line)
    {
        lock (events)
        {
            int last = events.Count - 1;
            if (last >= 0 && events[last] == line) { eventRepeats[last]++; return; }
            if (events.Count >= MostEvents) { events.RemoveAt(0); eventRepeats.RemoveAt(0); eventsDropped++; }
            events.Add(line);
            eventRepeats.Add(1);
        }
    }

    // The bodies other players were last drawn with, so a number over one of them can be named. Filled by the screen.
    readonly Dictionary<Transform, string> playerIdByBody = new Dictionary<Transform, string>();

    // A number drawn over somebody, said as who it was over: "me 12", "m3 46", "p1 MISS".
    void OnPopup(Transform over, string label)
    {
        if (over == null) return;
        string who = "?";
        var r = Rig();
        if (r != null && over.IsChildOf(r.transform)) who = "me";
        else
        {
            var shell = NetMob.ShellForView(over.gameObject);
            if (shell != null) who = IdFor('m', "m" + shell.netId, over);
            else
            {
                for (var t = over; t != null && who == "?"; t = t.parent)
                    if (playerIdByBody.TryGetValue(t, out var pid)) who = pid;
                // A player whose body the last screen did not list yet: found once, then remembered above.
                if (who == "?")
                    foreach (var identity in Mirror.NetworkClient.spawned.Values)
                    {
                        var p = identity != null ? identity.GetComponent<NetPlayer>() : null;   // perf-ok: only on a miss — the first number over a player the last screen did not list, at most once per player per answer
                        var body = p != null ? p.DrawnBody : null;
                        if (body == null || !over.IsChildOf(body)) continue;
                        who = IdFor('p', p);
                        playerIdByBody[body] = who;
                        break;
                    }
            }
        }
        AddEvent(who + " " + Plain(label));   // perf-ok: once per number drawn, the same cost as drawing it
    }

    void ClientLog(string[] parts)
    {
        if (parts.Length < 3) { Text(() => "usage: log <category|all> on|off — categories: " + string.Join(", ", Enum.GetNames(typeof(LogCat)))); return; }
        bool on = parts[2].ToLowerInvariant() == "on";
        if (parts[1].ToLowerInvariant() == "all") { GameLog.SetAll(on); Text(() => "every log category " + (on ? "on" : "off")); return; }
        if (!Enum.TryParse(parts[1], true, out LogCat cat)) { Text(() => "no category '" + parts[1] + "' — " + string.Join(", ", Enum.GetNames(typeof(LogCat)))); return; }
        GameLog.Set(cat, on);
        Text(() => cat + " " + (on ? "on" : "off"));
    }

    const float SeeMobs = 25f, SeeOthers = 15f;
    const int MostMobs = 10, MostOthers = 6;

    // A section that reads exactly as it did on the previous answer is left out and named on one line instead — the
    // hotbar, the townsfolk and the doors seldom change between two commands, and each is a line of tokens a reader
    // pays for every time. `look` always gives everything.
    readonly Dictionary<string, string> lastSent = new Dictionary<string, string>();
    readonly StringBuilder section = new StringBuilder(256);
    readonly List<string> unchanged = new List<string>();

    void AppendSection(StringBuilder sb, string name, bool full)
    {
        string text = section.ToString();
        section.Clear();
        lastSent.TryGetValue(name, out var before);
        if (!full && text == before) { if (text.Length > 0) unchanged.Add(name); return; }
        lastSent[name] = text;
        sb.Append(text);
    }

    string Screen(bool full)
    {
        var sb = new StringBuilder(512);   // perf-ok: once per answer a player asked for, never per frame
        section.Clear();   // a screen that threw half-way must not leak into the next one
        unchanged.Clear();
        var me = Character.Instance;
        var r = Rig();

        if (!InWorld())
        {
            sb.Append(LoadingScreen.Showing ? "loading…" : string.IsNullOrEmpty(AccountSession.AccessToken) ? "signed out — login <email> <password>" : "not in the world — play <name>");
            AppendEvents(sb);
            return sb.ToString();
        }

        Vector3 here = r.transform.position;
        sb.Append("map ").Append(MapManager.CurrentMap).Append('\n');
        sb.Append("me ").Append(AccountSession.CharacterName).Append(" Lv").Append(me.Level)
          .Append(" HP ").Append(me.CurrentHP).Append('/').Append(me.MaxHP)
          .Append(" MP ").Append(me.CurrentMP).Append('/').Append(me.MaxMP)
          .Append(" XP ").Append(me.XP).Append('/').Append(me.XPToNext)
          .Append(" Gold ").Append(me.Gold)
          .Append(" at ").Append(Pos(here))
          .Append(" order ").Append(r.OrderName);
        if (me.StatPoints > 0) sb.Append(" statPoints ").Append(me.StatPoints);
        if (me.IsDead) sb.Append(" DEAD");
        if (r.AttackTarget != null) sb.Append(" target ").Append(IdFor('m', r.AttackTarget));
        sb.Append('\n');

        section.Append("hotbar");
        for (int i = 0; i < HotbarBindings.SlotCount; i++) AppendSlot(section, (i + 1).ToString(), me.Hotbar.Get(i));   // perf-ok: once per answer
        AppendSlot(section, "R", me.Hotbar.Ult);
        section.Append('\n');
        AppendSection(sb, "hotbar", full);

        var states = me.States != null ? me.States.Active : null;
        if (states != null && states.Count > 0)
        {
            sb.Append("states");
            AppendStates(sb, states);
            sb.Append('\n');
        }

        AppendMobs(sb, here);
        AppendOthers(sb, here, full);

        if (unchanged.Count > 0) sb.Append("unchanged: ").Append(string.Join(", ", unchanged)).Append('\n');

        var box = DialogBox.Instance;
        if (box != null && box.IsOpen && box.Lines != null) sb.Append("dialog open with ").Append(box.Speaker).Append('\n');

        AppendEvents(sb);
        return sb.ToString().TrimEnd();   // perf-ok: once per answer
    }

    void AppendSlot(StringBuilder sb, string key, string skillId)
    {
        sb.Append(" | ").Append(key).Append(' ');
        if (string.IsNullOrEmpty(skillId)) { sb.Append('-'); return; }
        var skill = PlayerSkills.ById(skillId);
        sb.Append(SkillName(skillId));
        if (skill == null) return;
        if (skill.ManaCost > 0) sb.Append(' ').Append(skill.ManaCost).Append("mp");
        float cd = skill.CooldownRemaining;
        if (cd > 0f) sb.Append(" cd").Append(Mathf.CeilToInt(cd)).Append('s');
        else if (!skill.Affordable) sb.Append(" noMP");
        else sb.Append(" ready");
    }

    // " Poison 4s x2, Slow 1s" — each state as the tray and the nameplate draw it: its name, seconds left, stacks.
    static void AppendStates(StringBuilder sb, IReadOnlyList<UnitStates.State> states)
    {
        for (int i = 0; i < states.Count; i++)
        {
            var s = states[i];
            sb.Append(i == 0 ? " " : ", ").Append(string.IsNullOrEmpty(s.name) ? s.id.ToString() : s.name);
            int secs = Mathf.CeilToInt(s.Remaining);
            if (secs > 0) sb.Append(' ').Append(secs).Append('s');
            if (s.StackCount > 1) sb.Append(" x").Append(s.StackCount);
        }
    }

    readonly List<(float d, MonsterAI3D ai)> mobScratch = new List<(float, MonsterAI3D)>();

    void AppendMobs(StringBuilder sb, Vector3 here)
    {
        mobScratch.Clear();
        foreach (var ai in MonsterAI3D.Active)
        {
            if (ai == null || ai.Health == null || ai.Health.IsDead) continue;
            float d = Flat(here, ai.transform.position);
            if (d <= SeeMobs) mobScratch.Add((d, ai));
        }
        if (mobScratch.Count == 0) { sb.Append("mobs none within ").Append(SeeMobs).Append('\n'); return; }
        mobScratch.Sort((a, b) => a.d.CompareTo(b.d));
        sb.Append("mobs");
        for (int i = 0; i < mobScratch.Count && i < MostMobs; i++)
        {
            var (d, ai) = mobScratch[i];
            var plate = ai.GetComponent<MobNameplate>();
            string name = plate != null && !string.IsNullOrEmpty(plate.displayName) ? plate.displayName : ai.name;
            if (name.EndsWith("(Clone)")) name = name.Substring(0, name.Length - 7);
            sb.Append("\n  ").Append(IdFor('m', ai.Health)).Append(' ').Append(name)
              .Append(" HP ").Append(ai.Health.Current).Append('/').Append(ai.Health.MaxHP)
              .Append(' ').Append(D(d)).Append(' ').Append(Pos(ai.transform.position));
            // No state of a creature's is written here: the server sends this machine only its OWN states
            // (NetPlayer.TargetStates), so a creature's store on a watching machine is always empty.
        }
        if (mobScratch.Count > MostMobs) sb.Append("\n  +").Append(mobScratch.Count - MostMobs).Append(" more");
        sb.Append('\n');
    }

    void AppendOthers(StringBuilder sb, Vector3 here, bool full)
    {
        var mine = GameNetwork.MyPlayer;
        var line = section;
        int shown = 0;
        playerIdByBody.Clear();
        foreach (var identity in Mirror.NetworkClient.spawned.Values)
        {
            if (identity == null || shown >= MostOthers) continue;
            var p = identity.GetComponent<NetPlayer>();
            if (p == null || p == mine) continue;
            var body = Where(p);
            float d = Flat(here, body.position);
            if (d > SeeOthers) continue;
            string id = IdFor('p', p);
            playerIdByBody[body] = id;
            line.Append("\n  ").Append(id).Append(' ').Append(p.playerName).Append(' ').Append(D(d)).Append(' ').Append(Pos(body.position));
            shown++;
        }
        if (shown > 0) sb.Append("players").Append(line).Append('\n');
        line.Clear();

        shown = 0;
        foreach (var n in TownNPC.All)
        {
            if (n == null || shown >= MostOthers) continue;
            float d = Flat(here, n.transform.position);
            if (d > SeeOthers) continue;
            line.Append("\n  ").Append(IdFor('n', n)).Append(' ').Append(n.SpeakerName).Append(' ').Append(D(d)).Append(' ').Append(Pos(n.transform.position));
            shown++;
        }
        if (shown > 0) { line.Insert(0, "npcs").Append('\n'); }
        AppendSection(sb, "npcs", full);

        shown = 0;
        foreach (var g in GroundDrop.All)
        {
            if (g == null || !g.Readable || shown >= MostOthers * 2) continue;
            float d = Flat(here, g.transform.position);
            if (d > SeeOthers) continue;
            line.Append("\n  ").Append(IdFor('g', g)).Append(' ').Append(g.Text);
            if (g.quality != Quality.Raw) line.Append(" [").Append(g.quality).Append(']');
            line.Append(' ').Append(D(d)).Append(' ').Append(Pos(g.transform.position));
            shown++;
        }
        if (shown > 0) sb.Append("floor").Append(line).Append('\n');
        line.Clear();

        shown = 0;
        foreach (var door in Portal3D.Active)
        {
            if (door == null) continue;
            line.Append("\n  ").Append(IdFor('d', door)).Append(" to ").Append(door.targetScene)
                .Append(' ').Append(D(Flat(here, door.transform.position))).Append(' ').Append(Pos(door.transform.position));
            shown++;
        }
        if (shown > 0) { line.Insert(0, "doors").Append('\n'); }
        AppendSection(sb, "doors", full);
    }

    void AppendEvents(StringBuilder sb)
    {
        lock (events)
        {
            if (events.Count == 0) return;
            sb.Append("\nsince last:");
            if (eventsDropped > 0) sb.Append(" (+").Append(eventsDropped).Append(" earlier not shown)");
            for (int i = 0; i < events.Count; i++)
            {
                sb.Append("\n  ").Append(events[i]);
                if (eventRepeats[i] > 1) sb.Append(" x").Append(eventRepeats[i]);
            }
            events.Clear();
            eventRepeats.Clear();
            eventsDropped = 0;
        }
    }

    // WHAT IS ACTUALLY UNDER YOUR FEET, as the physics engine answers it — for hunting a fall or a stall. Reading
    // only: it asks the engine what it already holds and repeats the answer, deciding nothing and computing no game
    // number. A debugger chasing a player who falls out of a map has no other way to see this from outside.
    string GroundText()
    {
        var r = Rig();
        if (r == null) return "not in the world";
        var cc = r.GetComponent<CharacterController>();   // perf-ok: once per command a player sends
        Vector3 here = r.transform.position;
        var sb = new StringBuilder("standing at (");
        sb.Append(here.x.ToString("0.00", CultureInfo.InvariantCulture)).Append(", ")
          .Append(here.y.ToString("0.00", CultureInfo.InvariantCulture)).Append(", ")
          .Append(here.z.ToString("0.00", CultureInfo.InvariantCulture)).Append(") in map ").Append(MapManager.CurrentMap);
        if (cc != null) sb.Append(cc.isGrounded ? " — grounded" : " — IN THE AIR");

        sb.Append("\nbelow:");
        var down = Physics.RaycastAll(here + Vector3.up * 2f, Vector3.down, 500f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(down, (a, b) => a.distance.CompareTo(b.distance));
        int shown = 0;
        for (int i = 0; i < down.Length && shown < 6; i++)
        {
            // Your own body is not something you are standing on.
            if (down[i].collider != null && down[i].collider.transform.IsChildOf(r.transform)) continue;
            shown++;
            sb.Append("\n  ").Append(Describe(down[i].collider))
              .Append(" top at y ").Append(down[i].point.y.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(", ").Append((here.y - down[i].point.y).ToString("0.00", CultureInfo.InvariantCulture)).Append(" below you");
        }
        if (shown == 0) sb.Append(" nothing at all within 500 down");

        sb.Append("\ntouching within 1.5:");
        var near = Physics.OverlapSphere(here + Vector3.up * 0.5f, 1.5f, ~0, QueryTriggerInteraction.Ignore);
        int also = 0;
        for (int i = 0; i < near.Length && also < 8; i++)
        {
            if (near[i] != null && near[i].transform.IsChildOf(r.transform)) continue;
            also++;
            sb.Append("\n  ").Append(Describe(near[i]));
        }
        if (also == 0) sb.Append(" nothing");
        return sb.ToString();
    }

    static string Describe(Collider c)
    {
        if (c == null) return "(gone)";
        var sb = new StringBuilder(c.name);   // perf-ok: once per line of a ground command
        sb.Append(" [").Append(c.gameObject.scene.name).Append(", layer ").Append(LayerMask.LayerToName(c.gameObject.layer));
        if (c is BoxCollider box) sb.Append(", box ").Append(box.size.ToString("0.#"));
        else if (c is MeshCollider mesh) sb.Append(", mesh ").Append(mesh.convex ? "convex" : "solid");
        else if (c is CapsuleCollider) sb.Append(", capsule");
        else if (c is SphereCollider) sb.Append(", sphere");
        sb.Append(c.enabled ? "" : ", SWITCHED OFF").Append(']');
        return sb.ToString();
    }

    string BagText()
    {
        var me = Character.Instance;
        if (me == null) return "not in the world";
        var sb = new StringBuilder("bag (use/equip by number):");
        for (int i = 0; i < me.Bag.Count; i++)
        {
            var e = me.Bag[i];
            if (e.def == null) continue;
            sb.Append("\n").Append(i + 1).Append(". ");
            if (e.instance != null && e.instance.upgrade > 0) sb.Append('+').Append(e.instance.upgrade).Append(' ');
            sb.Append(e.def.displayName);
            if (e.count > 1) sb.Append(" x").Append(e.count);
            if (e.instance != null && e.instance.quality != Quality.Raw) sb.Append(" [").Append(e.instance.quality).Append(']');
            if (e.IsEquip) sb.Append(" (").Append(e.def.slot).Append(')');
            if (e.def.IsUsable) sb.Append(" (usable)");
        }
        if (me.Bag.Count == 0) sb.Append(" empty");
        return sb.ToString();
    }

    string GearText()
    {
        var me = Character.Instance;
        if (me == null) return "not in the world";
        var sb = new StringBuilder("worn:");
        foreach (var kv in me.Gear.Worn)
        {
            if (kv.Value == null || kv.Value.def == null) continue;
            sb.Append("\n").Append(kv.Key).Append(": ");
            if (kv.Value.upgrade > 0) sb.Append('+').Append(kv.Value.upgrade).Append(' ');
            sb.Append(kv.Value.def.displayName);
            if (kv.Value.quality != Quality.Raw) sb.Append(" [").Append(kv.Value.quality).Append(']');
        }
        return sb.ToString();
    }

    string StatsText()
    {
        var me = Character.Instance;
        if (me == null) return "not in the world";
        var sb = new StringBuilder("stats");
        foreach (PrimaryStat s in Enum.GetValues(typeof(PrimaryStat))) sb.Append(' ').Append(s).Append(' ').Append(me.GetStat(s));
        sb.Append(" | points left ").Append(me.StatPoints);
        return sb.ToString();
    }

    // ---- formatting ------------------------------------------------------------------------------------------------

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    static string D(float d) => d.ToString("0.0", CultureInfo.InvariantCulture) + "m";
    static string Pos(Vector3 p) =>
        "(" + p.x.ToString("0.0", CultureInfo.InvariantCulture) + "," + p.z.ToString("0.0", CultureInfo.InvariantCulture) + ")";
    static bool TryNum(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    // Rich-text tags are how the chat log colours a row; as text they are noise.
    static string Plain(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        bool inTag = false;
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            // The chat box writes a typed '<' followed by a zero-width space so it cannot open a tag; keep that one.
            if (ch == '<' && i + 1 < s.Length && s[i + 1] == '​') { sb.Append('<'); i++; continue; }
            if (ch == '<') { inTag = true; continue; }
            if (ch == '>' && inTag) { inTag = false; continue; }
            if (!inTag) sb.Append(ch);
        }
        return sb.ToString().Replace("​", "");
    }
}
