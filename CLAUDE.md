# CLAUDE.md — roguelike-mmo (unwritten)

## ⛔⛔⛔ RULE NOCLIENTMATH — THE PLAYER'S MACHINE NEVER WORKS OUT A GAME NUMBER. IT DRAWS WHAT THE SERVER SENDS AND SENDS WHAT THE PLAYER PRESSED. NOTHING ELSE, EVER (HIGHEST PRIORITY IN THIS PROJECT — OUTRANKS EVERY OTHER RULE IN THIS FILE, NO EXCEPTIONS)

**The player's machine has exactly two jobs:** show what the server says, and tell the server what the player
pressed. It does not decide, roll, subtract, multiply, derive, predict or "work out for display" any number the game
uses or shows. Not damage, not defence, not an element chart, not a crit, not a hit or a miss, not a heal, not
regeneration, not mana, not a cooldown, not a stat on the character sheet, not a rarity, not a roll, not a drop, not
experience.

- **THE ONE EXCEPTION IS MOVEMENT, decided by the user on 2026-09-14.** The player's machine moves its own legs —
  walking, jumping, the stop a hurt stagger puts on them — and reports where it is; the server checks every report and
  corrects it. Nothing else rides on that exception: not what a step costs, not what it hits, not what it heals.
- **Comparing numbers the server SENT, to choose what to draw, is showing — not computing.** Greying a hotbar button
  whose cooldown end the server sent, or saying "No MP" against the mana the server sent, produces no new number and
  decides nothing: the press still goes to the server, and the server still says yes or no. Starting a cooldown,
  spending mana, or working out either one is computing, and is banned.

- **Server → player: finished values only.** Every message carries the final number and the final flags exactly as
  they will be shown. The player's machine copies them onto the screen. If it would have to do a sum to show it, the
  message is wrong and the SERVER must send the answer instead.
- **Player → server: intent only.** Which key, which skill, which target, where they aimed, which bag position.
  **Never an amount.** A message from the player carrying a number the server then uses is a bug by definition.
- **"It is only for display" is not an exception.** A number worked out twice — once to decide it, once to draw it —
  is two answers, and the moment the two machines hold different inputs the screen lies.
- **"It feels more immediate" is not an exception either.** If a design wants something to appear before the server
  answers, that is a design decision the user makes out loud, never a shortcut taken in code.

**The mandatory test before writing or editing ANY runtime line:** *"can this run on a player's machine, and does
it produce a number the game uses or shows?"* If yes, it belongs on the server, and the player's machine receives the
result. If unsure, it is yes. Code that already breaks this is not precedent — it is a violation waiting to be moved.

*(Scar, 2026-09-14: the server sent each blow to the player's machine BEFORE defence, and the player's machine took
defence off itself to draw the number — using its OWN copy of the creature, which rolls its own armour. The number on
screen and the health actually lost were computed from two different creatures. A full map then found the same
mistake in regeneration, heals, swing damage, cooldowns, mana checks and the stat sheet. The user's words: "i dont
care about the client, its only job is to render, not compute".)*

## ⛔⛔ RULE NOEDITORWRITES — NOTHING MAY CHANGE A SCENE, PREFAB OR ASSET UNLESS THE USER JUST ASKED FOR IT (ULTRA-PRIORITY — OUTRANKS EVERY OTHER RULE IN THIS FILE)

**A script must never write saved data while the game is not playing.** Not to preview something, not to keep something tidy, not to be helpful. If the editor marks a scene modified and the user did not do anything, that is a BUG of the highest severity in this project, and it is this rule's job to make sure one is never written again.

**Banned, absolutely:**

- `[ExecuteAlways]` / `[ExecuteInEditMode]` code that writes ANY serialized value — a `sortingOrder`, a transform, a colour, a material property, a field on a component. Reading is fine. Writing is not.
- `OnValidate` that changes values rather than only complaining about them.
- `[InitializeOnLoad]` / `[InitializeOnLoadMethod]` that touches scene objects, or calls `MarkSceneDirty` / `SetDirty` / `MarkAllScenesDirty`.
- Anything on an editor-update, selection, hierarchy or scene-opened callback that writes.
- A "preview" that is worth a saved change. **It never is.** If a value only matters while the game runs, compute it while the game runs.

**The only thing that may dirty a scene is a deliberate authoring action the user has just taken** — they clicked a menu item, they ran a tool, they answered a prompt. That write must happen because of their click and nothing else.

**The mandatory test before adding or editing ANY component:** *"if the user opens this scene, moves the Scene view camera, and touches nothing — can anything I wrote change one saved byte?"* If yes, or you are unsure, guard it with `if (!Application.isPlaying) return;` **before** the write.

*(Scar, 2026-09-05: the persistent scene marked itself modified constantly, so every single build stopped with "Modified Scenes must be saved to continue". Two depth sorters ran in the editor and wrote a draw order worked out from the SCENE VIEW CAMERA'S direction — so merely looking around the world rewrote saved data. The user changed nothing and was told their scene had changes, for months. Their words: "wala dapat kahit anong nagbabago sa editor".)*

### ⛔ THE OTHER HALF — REMOVING A SERIALIZED FIELD CAUSES ONE PROMPT, AND YOU SAY SO. ANY OTHER PROMPT IS A BUG YOU DIAGNOSE BY DIFFING, NEVER BY GUESSING (same ultra-priority)

There are exactly **two** reasons a scene reports unsaved changes when nobody touched it. The rule above bans the
first. This is the second, and it is not a bug — but it becomes one the moment the user is left to discover it.

**A field deleted from a script leaves its VALUE in every scene and prefab that held it.** Unity drops that value
when it loads the file, so what is in memory no longer matches what is on disk, and it says so — on every load,
forever, until the file is written once without it.

**So, whenever I remove a serialized field — or delete a script that had any — I MUST, in the same reply:**

1. **Say that the next open will report unsaved changes, and that this is my change causing it.**
2. **Name which scenes or prefabs are affected**, found by grepping the field name across `unity/Assets/`.
3. **Say that saving once settles it permanently**, and offer to do it. Saving is a write to the user's project,
   so it is offered, never done unasked.

Letting them meet that prompt with no warning is indistinguishable from the bug the rule above exists to kill,
and it costs a whole round re-diagnosing something I already knew the answer to.

**AND WHEN A PROMPT APPEARS THAT NOBODY REMOVED A FIELD FOR — MEASURE, DO NOT THEORISE.** There is one method and
it takes two minutes. Save a throwaway COPY of the in-editor scene and diff it against the file on disk:

```
unity cmd eval --code 'var s = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Systems"); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s, "Assets/__dirty_probe.unity", true); return "saved";'
diff unity/Assets/Scenes/Systems.unity unity/Assets/__dirty_probe.unity
unity cmd eval --code 'UnityEditor.AssetDatabase.DeleteAsset("Assets/__dirty_probe.unity"); return "gone";'
```

The `true` is what makes it a COPY — the real scene is not written and does not stop being dirty, so nothing is
decided behind the user's back. The diff names the exact lines, and the answer is usually five of them.

**Never reason about which script "might be" writing.** The first attempt at this was reasoned, found a real
culprit, fixed it — and the prompt came back the same evening from a completely different cause the reasoning
never had a chance of reaching. The diff would have said so in two minutes, on the first try.

*(Scar, 2026-09-05, the same evening: after the sorters were fixed, the build stopped again. The user: "i thought
you fixed this already?" The real cause was three deleted fields — one of them mine, from that same session —
whose values the scene still carried. Five lines, found by diffing, invisible to any amount of thinking.)*

**If the editor stops answering while a prompt is on screen, that is the prompt blocking it.** Ask the user to
dismiss the dialog; do not click through it, and do not diagnose blind while it is up.

## ⛔ RULE MYPROCESSES — EVERY PROCESS I START, I STOP, IN THE SAME RESPONSE. AND I NEVER STOP ONE I DID NOT START (HARD RULE — added 2026-09-07 at the user's instruction)

**The user starts their own servers. They do not want mine.** A server I started for a measurement is scaffolding,
exactly like a throwaway script, and it is stopped before the reply is written — not at the end of the session,
not "when I am done with it", not left for them to find.

**The two halves, both absolute:**

1. **Stop what I started, in the same response that started it.** Every one: a character server on a spare port,
   a wiki file server, a sign-in service, a probe, a watcher. If a reply ends with something of mine still
   listening, that reply is a FAILED reply, however good the work in it was. A process I need across several
   replies is one I ASK about first and name in the report every time it is still up.
2. **Never stop a process I did not start.** Theirs is running because they are using it. If a port I want is
   taken, that is an answer — use a different port, or ask.

**STOP IT BY ITS PORT, NEVER BY ITS NAME.** `pkill -f CharServer` kills EVERY character server on this machine,
including the one the user is playing against. The safe form, always:

```
PID=$(lsof -nP -iTCP:<port> -sTCP:LISTEN -t); [ -n "$PID" ] && kill $PID
```

**And before assuming a listening process is mine, check WHEN IT STARTED** — `ps -o pid,lstart,command -p <pid>`.
A start time later than my own last kill means it is theirs, and it stays up.

**Say in the report what I started and that it is stopped**, or say plainly that I started nothing. The report
line is what makes this checkable rather than a good intention.

*(Scar, 2026-09-07: cleaning up a test copy of the character server, I ran `pkill -f CharServer` and killed the
user's running one along with it — which is also the "stop everything" command their own runbook prints. Their
words afterwards: "i dont need your servers, i start them myself".)*

## Bugfix confidence protocol (ALWAYS)

At the END of every bugfix response, state your confidence that the bug is ALREADY fixed, as a percentage (0%–100%) on its own final line, e.g. `**Fix confidence: 75%**`. Be honest — reflect whether the fix was verified live, only reasoned about, or blocked (e.g. Unity unreachable). Do not inflate.

## Second-fix rule: after ONE failed fix, stop hypothesizing — go get GROUND TRUTH

If a fix attempt doesn't resolve the bug, do NOT ship a second fix built on more reasoning about how the system "probably" works. The next move is to MEASURE: read the real data and replay the real math, then fix from evidence. Reasoning-only fixes on a bug that already survived one round burn many turns and usually miss a deeper cause. (This wardrobe bug ate \~4 rounds of plausible-but-wrong fixes; it was cracked in one pass the moment I decoded the actual save + PNG + FBX bytes and re-ran detection offline — the true cause, a dark-color gamma mismatch, was invisible to reasoning but obvious in the numbers.)

Ground-truth sources for THIS project (cheap, no Unity needed — a plain Python/Bash script reads them):

- **A CHARACTER** = a row in postgres, and this is the single most useful measurement in the project. A player's
  machine has not written progress since the server took ownership of it — `SaveManager` only READS — so what a
  character actually IS lives in one place and you can read it in one command. It settles "is their hotbar really
  empty", "did that binding save", "what is equipped" in seconds, and it is the truth the game loads:
  ```bash
  psql "$(grep '^DATABASE_URL=' accounts/.env | cut -d= -f2-)" -A -t -c \
    "select name, updated_at, progress->'hotbarBindings', progress->'equipItemIds' \
     from character where deleted_at is null order by updated_at desc limit 5;"
  ```
  `jsonb_pretty(progress)` dumps a whole character. Full detail: `docs/1-planning/querying-the-database.md`.
- **Did my edit reach the editor** = search the assembly it built, never the source on disk — the rule and the
  encoding trap are in the console section below, written once.
- **WHAT ACTUALLY HAPPENED AT RUNTIME** = three different logs, and they are not interchangeable. The editor's is
  `~/Library/Logs/Unity/Editor.log`; a client BUILD writes `~/Library/Logs/DefaultCompany/unwritten/Player.log`
  (and `Player-prev.log` for the run before it); and the GAME SERVER writes `logs/server.log` at the top of this
  repo, with the run before it kept as `logs/server-prev.log`. An idle server is now SILENT — nothing writes on a
  timer — so a quiet log means nothing happened, not that logging is off. **READ THE SERVER'S OWN LOG BEFORE REASONING ABOUT
  WHAT IT DECIDED** — it already writes down why it refused a cast, turned a connection away, dropped a player or
  failed to save somebody, in 44 places. Working that out from the source instead is the slow way to an answer it
  has already written. Every line carries the date and time to the millisecond and names units by map and spawn_id;
  how to read it and the two scripts that total and check it are the **`server-log` skill** (loaded into the tester,
  debugger and reviewer), with every line shape in `docs/0-process/reading-the-game-log.md`. If it is missing, the server was started the old way, straight to the terminal: the run
  command in the network runbook now tees it to that file.
- **WHAT IS ACTUALLY IN THE CLIENT BUILD, and how big** = Unity's own size report, written into
  `~/Library/Logs/Unity/Editor.log` after every build. Search it for `Used Assets and files from the Resources
  folder, sorted by uncompressed size`: above that line is the split by kind (textures / meshes / audio /
  levels), below it is every shipped asset in descending size. **Never add up folder sizes instead** — most of
  `Assets/` is never referenced and never ships, so the folders and the build disagree by multiples. (Measured
  2026-09-07: the client is 75% textures and 5% audio, while on disk the audio folder is bigger than the
  textures. Guessing from folders sent a whole round in the wrong direction.)
- **Textures** = decode the PNG bytes (pure-Python zlib/PNG, no PIL) to get exact per-pixel colors/coverage.
- **Meshes/rigs** = parse the binary FBX (zlib-compressed node arrays: Vertices / PolygonVertexIndex / UV) to get real UVs, tris, verts.
- **Scenes/prefabs/.meta** = YAML/text — grep them directly for the actual wired values (renderer names, import flags).
- **Live engine values** = the Unity CLI against the running editor (`unity cmd …`, or the `unity-editor-mcp` tools) — see the **Unity CLI** section below. A live connection beats raising model effort — most stalls here are missing observation, not missing intelligence.

Escalate EFFORT/model only once the evidence is in hand and the remaining hard part is genuinely synthesis — not as a substitute for looking. Missing information is fixed by looking, not by thinking harder.

## The live link to Unity — it EXISTS; check it, then use it

There is one road to the running editor and it works. **Check `unity status` before leaning on it** (a row with
state `ready` = connected, an empty table = not attached), and **if it is down, say so plainly** rather than
working blind — that exact failure kept a dead bridge in use for five days.

**Reach for it without being told.** The user should never have to say "check Unity". Any time you are about to
state a live value, describe how something looks, run an author-time action, or take a second swing at a bug that
survived one fix — that is the instruction. Measure, don't guess.

**Two things stay the user's:** Play mode, and their visual judgement. And the forced-recompile ban below.

The connection details, the proven-vs-unproven list, how to run an editor action safely, and the offline compile
checker are the **`unity-live-editor` skill**.

## Agent roles — WHEN to spawn one without being asked

Eight roles live in `.claude/agents/`. Five of them cannot write at all, or can write only one
narrow place, and that is enforced by a refusal rather than by their good intentions — the gate is
`tools/hook-analytics/write_scope_gate.py`, proven by `tools/prove-write-scope-gate.py`. The
eighth, `sprint-manager`, may write exactly two files — the action plan and the wiki Roadmap page —
fenced by that same gate.

**FOUR of them are spawned WITHOUT asking, the moment their situation arrives.** This section is
what authorises that; the default everywhere else is still to do the work here.

- **`debugger` — the moment a fix FAILS.** A bug that survived one attempt has a cause this
  session's model of the system is missing, so the next move is never a second reasoned fix. Hand
  it over: the debugger only measures, and it cannot change anything, so its answer comes back as
  evidence rather than as a patch. This is the second-fix rule at the top of this file, carried out
  by somebody whose hands are tied.
- **`data-author` — bulk authoring of saved game data.** More than a handful of items, creatures,
  drop tables or skills. It may write game data and nothing else.
- **`planner` — a feature whose SHAPE is not obvious.** A system, not a change: a loot spine, a
  quest system, a new screen. Not for anything a paragraph of contract bullets already settles —
  spawning one to plan a one-liner is a slower way to start.
- **`sprint-manager` — ANY task tracking**: ticking a task done, and moving an entry on the wiki
  Roadmap. The rule is **RULE TRACKING** below.

**Everything else is spawned only when the user asks for it BY NAME.**

- **`analyzer`** answers questions about where the project stands. It is deliberately NOT automatic:
  most such questions are faster answered here, and it starts cold knowing nothing of the
  conversation.
- **`doc-keeper`** revises docs and the wiki. Writing a doc is usually part of the work in hand,
  not a job to hand off.
- **`reviewer` and `tester` stay exactly as their own sections below say** — the user's words are the
  only thing that starts either, and neither is ever offered. Nothing here loosens that.

**⛔ EVERY REPLY ENDS BY SAYING WHICH SUB-AGENTS ARE RUNNING, AND WHAT EACH IS FOR (HARD RULE — added
2026-09-13 at the user's instruction).** The user cannot see a sub-agent start, run, or finish — all they see is
tool activity after the reply that looks like nothing they asked for. So the LAST lines of every reply, before
the closing timestamp, state in plain words: which sub-agents this reply started or left running, and each one's
ROLE — "a reviewer, reading the crit change for anything it breaks"; "a planner, mapping every file armour rolling
touches". When none is running, say "No sub-agents running." A sub-agent that failed (a rate limit, an error) is
named the same way, with what it was for and that its work did not come back. *(Scar, 2026-09-13: a reviewer kept
reading files after a reply ended; the user's question was "what is this process running after your fix?")*

**Relaying what one hands back.** Only its final report reaches this session, and none of it reaches
the user — so it gets relayed, never pasted as though it were watched. **Spot-check any number it
leads with before repeating it**, because a count it invented and a count it measured arrive in the
same confident voice, and the user has no way to tell them apart. Say which parts came from it.

## ⛔ RULE TRACKING — THE MAIN AGENT NEVER TICKS A TASK OR MOVES THE ROADMAP. THE SPRINT MANAGER DOES (HARD RULE — added 2026-09-13 at the user's instruction)

**Task tracking lives in `docs/1-planning/action-plan.md` (its checkboxes) and the wiki Roadmap page
`wiki/content/roadmap.json`.** Jira was tried and left on 2026-09-13; its connection stays installed
but unused. The main session never ticks a checkbox and never moves a Roadmap entry — spawn
`sprint-manager` with the whole job and relay what it hands back, saying that it came from the sprint
manager. Editing the plan's WORDING (planning) is not tracking and stays with the main session.

- **Access is never the fence.** Should a guard be needed, it waits for the user's go — it never
  removes a capability. The user's words: "we only block it with guards waiting our go signal, not
  access".
- **If the sprint manager cannot do something, it reports that — and it stays its report.** The
  answer is never for the main session to do it instead.

*(Scar, 2026-09-13: asked to clear Jira's sample content, the main session started reading and
deleting on its own. The user stopped it: "why are you doing it yourself?")*

## Handoff protocol — the phrase "do a handoff"

**"do a handoff"** (or "handoff this" / "hand off the task") is a COMMAND: without further prompting, write a
file under `handoffs/` so the next session can resume cold. The same applies when you stop on an unsolved hard
bug that ate real debugging time. The full protocol — required contents, their order, and when to delete the
file — is the **`handoff` skill**. Invoke it; don't improvise the format.

## Reviewer protocol — the phrase "spawn a reviewer"

**"spawn a reviewer"** is the ONLY thing that starts one. It spawns a single cold sub-agent that READS the
change and hunts for what is broken in it — the protocol is the **`reviewer` skill**.

**The default is OFF and it is never suggested.** Do not spawn one after making changes, do not spawn one
because a change felt risky, and do not end a report by offering one. Silence means no.

It is a reviewer, NOT a tester: nothing here can play the game, so its findings are reasoning about code and
must never be relayed as something that was watched happening.

## Tester protocol — the phrase "spawn a tester"

**"spawn a tester"** is the ONLY thing that starts one. It spawns a sub-agent that actually PLAYS the game
through the live editor and reports what it OBSERVED — the protocol is the **`tester` skill**.

**The default is OFF and it is never suggested**, because it takes over the editor for as long as it runs.

It is the opposite half of the reviewer: a reviewer only reads, a tester only reports what it watched happen.
Never let one wear the other's words. Two players at once is beyond it — the editor is one client.

## Harvest protocol — the phrase "harvest"

**"harvest"** (or "harvest the session" / "do a harvest") is a COMMAND: without further prompting, go back over
everything this session found and put each piece where it will actually fire next time. It is the opposite of a
handoff — a handoff carries an UNFINISHED task to the next session; a harvest banks what a FINISHED one learned.

**Sort every finding into one of four homes. The fourth is the one that gets forgotten.**

1. **A lesson that generalises past this project** — a way of measuring, diagnosing, or running a long task →
   a page in `docs/5-ai-learnings/` **and its index line**.
2. **A project fact** — a bug's real root cause, how something actually works, a decision → **memory**, plus the
   pointer line in the matching `memory/index/<group>.md`.
3. **Something that must change behaviour every session with nobody looking it up** → a line in THIS file or the
   global one. Never a page; a page nobody opens at the right moment is a diary.
4. **An OPEN DEFECT you found and only described in prose** → **fix it now, or it is gone.** This is the whole
   reason the phrase exists. A stale doc, a guard that misfires, a leftover you noticed in passing: it was never
   the task, so nothing wrote it down, and when the session ends it exists nowhere. Ask explicitly: *what did I
   say out loud today that lives only in this chat?*

**Then five checks, all mechanical:**

- **Is the always-on file still TRUE?** Walk its ground-truth list, its paths, its commands. One that names a
  dead file sends the next session to a dead end while looking authoritative.
- **RUN every command you write into a doc.** A query nobody executed is a guess wearing a code block.
- **Is what you wrote REACHABLE?** A memory with no index line is never loaded; a page with no index row is
  never found. Grep for each new file's own name and confirm something points at it.
- **Did you create a SECOND home for anything?** If the new note duplicates a rule that already exists, delete
  yours and fix the existing one instead — that is usually the better repair, because the incumbent is the one
  that will be read.
- **Delete what has served its purpose** — scaffolding whose comment says to, resolved handoffs, dead helpers.

**Hand back a CLOSED list, and say plainly when it is empty.** "Nothing to harvest" is a complete and correct
answer for a session that cost nothing. Never pad it: a lesson nobody paid for is advice, and advice filed as a
lesson makes the real ones harder to find.

*(Why this is a phrase and not a habit: on 2026-09-07 a harvest run at the user's request turned up a database
guide whose every example query would have failed — the schema had moved three migrations earlier — a guard that
had cried wolf three times that session and never once on a real repeat, and a temporary diagnostic still
writing a line every five seconds weeks after its own comment said to delete it. None of the three was a lesson.
All three had been noticed and spoken aloud. Not one was written down anywhere.)*

## AI learnings — `docs/5-ai-learnings/`

Lessons about HOW to work — a way of measuring, a way of diagnosing, a way of running a long task — that would
help on ANY codebase. One page each, indexed in that folder's `README.md`.

**The whole problem this folder has is being READ.** Memory loads itself into every session; docs do not. A
learnings folder with no read trigger is a diary. So the system below is mostly about reading, and everything in
it exists to make the check cost almost nothing.

### READ — the trigger is mechanical, not a good intention

**Open `docs/5-ai-learnings/README.md` — the index only — before a SECOND attempt at anything that already
failed once.** A stalled tool, a measurement you cannot get, a fix that did not take, a long agent run. That is
the same beat as the second-fix rule at the top of this file, deliberately: one moment to remember, not two.

Also open it when a tool HANGS or returns nothing, before theorising about why.

The index is one screen of symptom lines, so reading it costs a few seconds. If checking it ever feels expensive,
the index has grown too big and that is a fault to FIX (below), not a reason to skip it.

### WRITE — the trigger is a wasted round, not the end of a session

Write a page when **both** are true:

1. it generalises past this project, and
2. **something real was paid for it** — a wasted round, a lost hour, a wrong report, a user having to correct you.

The cost IS the trigger. At the end of any session that burned a round, ask: *what rule would have saved it?* If
the answer is project-specific, it goes to memory instead. If you cannot name what it cost, it is advice, not a
learning — do not write it.

### The page shape — fixed, so it can be skimmed at speed

1. **Reach for this when** — the SYMPTOM, in the reader's words, first line of the page.
2. **The rule** — what to do, in two or three sentences.
3. **Why** — the incident and what it cost. Concrete, with the real numbers.
4. **How to apply** — the concrete moves, including the exact command where there is one.

The index line is **symptom → lesson**, never lesson → symptom, because you arrive at the index knowing the
symptom and nothing else.

### NO PAGE CAP — but every page must still earn its place

**There is no limit on the number of pages** (the old cap of 12 was lifted by the user on 2026-09-03). A lesson
that was paid for gets written down, full stop; never merge two unrelated lessons into one page, or drop one,
because a counter said so.

What keeps the folder readable is not a page count, it is the INDEX: one symptom line per page, sorted so the
reader arrives knowing the symptom and finds it by scanning. Keep those lines short and distinct, and merge two
pages ONLY when they genuinely share a symptom — never as bookkeeping.

Delete a page the moment it is obsolete, or the moment it graduates into a hard rule in this file — a lesson in
two places is the dual-path rot this file bans everywhere else.

### One home per lesson, split by whether it must fire unprompted

- **Must change default behaviour every session, with nobody looking it up** → memory, or a line in this file.
- **A playbook you consult when you hit the situation** → `docs/5-ai-learnings/`.
- **A bug's root cause, a design decision, a project fact** → NOT a learning. Memory or the systems doc.

Never both homes, ever.

## Project layout

- `unity/Assets/Scripts/` — all game C# (Unity 6 + URP); see `unity/Assets/Scripts/CLAUDE.md`
- `unity/Assets/Scripts/Editor/` — one-click editor tools under `Tools ▸ unwritten ▸ …`
- `unity/Assets/Scripts/Editor/TOOLS.md` — canonical catalog of all editor menu items (88 cmds)
- `tools/` — HTML/Python authoring tools (world-creator, map-painter, chatgpt extractor)
- `tempo/` — auto-generated canvas dev server; do NOT write app code here

### `unity/Assets/` is organized by USAGE — put new files in the right bucket

The `Assets/` root leads with four number-prefixed buckets (prefixes float them to the top of Unity's Project window). When you add or generate an asset, it MUST land in the bucket matching how it's used — each bucket has a `README.md` restating its rule:

- `0_Raw/` — imported vendor/asset packs, **untouched**. Never edit these, never place them in a scene directly. To use one, run its import/bake tool → the result lands in `1_Authored/`.
- `1_Authored/` — what WE make and place: `Prefabs/Props/` is THE drag-and-drop palette (place things from here); `Art/` is our textures/sprites. Baked prop prefabs, hand art, ground tiles → here.
- `2_Generated/` — tool/baker OUTPUT, safe to delete & regenerate (ground meshes+materials, baked 8-dir sprites, minimaps, decimated meshes). **Never hand-edit** — re-run the tool.
- `3_Data/` — data the game loads at runtime: `Resources/` (keep this folder named exactly "Resources"), `Recipes/`, `ScriptableObjects/`.
- **UNMOVED at root** (engine / package-owned — do NOT relocate): `Scripts`, `Scenes`, `Shaders`, `Settings`, `Audio`, `UI`, `UI Toolkit`, `TextMesh Pro`.

When an editor tool writes assets, its output path must point at the correct bucket (paths are hard-coded `const string` in each tool). If you move a bucket, patch those path strings + the two-arg `AssetDatabase.CreateFolder("Assets", "…")` calls in the same change. Scenes/prefabs/materials link by GUID (in `.meta`), so moving a file with its `.meta` never breaks references — only code path-strings do. See memory `assets-reorg-2026-07` for the full old→new mapping.

---

## Domain docs (read on demand — do NOT load eagerly)

| Domain | File | One-liner |
| --- | --- | --- |
| Performance rules | `unity/Assets/Scripts/PERFORMANCE.md` | Required before any runtime C# edit |
| Perf history | `unity/Assets/Scripts/PERF_LOG.md` | Past checkup results |
| Editor tools catalog | `unity/Assets/Scripts/Editor/TOOLS.md` | All `Tools ▸ unwritten` menu items |

---

## ⛔ RULE NOSTALE — NEVER APPEND TO A DOC. ALWAYS UPDATE IT IN PLACE. NOT ONE STALE STRING MAY SURVIVE (HARD-PINNED, ranks with THE FIVE)

Every doc in this repo — `docs/`, the wiki pages, a README, a handoff, a learnings page, a design note, a spec,
a PRD — states what is TRUE NOW and nothing else. Revising one means **finding every sentence that says the old
thing and rewriting it where it sits**. Appending the new version at the bottom while the old one stands is a
FAILED edit, even when the new section is perfect: two answers in one doc means the reader cannot tell which is
real, and the dead half wins whichever paragraph they happen to read first.

**Banned, absolutely, in every doc:** an appended "UPDATE" / "SETTLED" / "V2" / "NEW" section left beside the
text it supersedes · a changelog or history section inside a living doc · a superseded number, name, verb,
count, label or example left anywhere on the page · a "(old: …)" parenthetical · a struck-through line kept for
context · "previously this worked like…". **History lives in git and in chat, never in the doc.**

**The mandatory pass after EVERY doc edit — not optional, not "if it feels risky":**
1. Re-read the WHOLE doc, top to bottom. Not the part I edited — all of it.
2. **GREP FOR THE SUBJECT, NEVER FOR THE WORDING I REPLACED.** Search `docs/` AND `wiki/` for the THING the
   rule is about — the noun, the feature, the stat — and READ every hit. A search built from my own old
   sentence can only find pages that happened to use my words; the page that states the same dead rule in
   different words is invisible to it, and that is precisely the page nobody has re-checked. Grepping my own
   phrasing is confirming my own edit, not sweeping for stale claims.
3. **The player WIKI is part of the sweep, every time.** It states the same rules in player words, so it is
   the surface most likely to phrase a dead claim differently — and the one a reader actually sees.
4. Reconcile every hit in the SAME turn. A stale line I plan to fix later is a stale line that ships.
5. Say in the report which stale passages I found and killed, or say plainly that I found none.

**One string is enough to break it.** A single leftover old number reads exactly as authoritative as the twenty
correct ones around it, so the whole doc becomes untrustworthy and every future read pays for it. There is no
"close enough", no "that bit is harmless", and no doc small enough to skip the pass.

*(Scar, 2026-07-24: a "SETTLED" section was appended to the V3 skill doc while three earlier sections kept
contradicting it — a stale verb and a wrong count among them. The user was furious. The full doc rules are the
`write-project-docs` skill; this line is here so the rule fires without anyone opening it.)*

---

## ⛔ THE FIVE — the anti-invention protocol (HARD-PINNED, read before every build)

These exist because a full day was lost on 2026-08-21 to inventions that did not *feel* like inventions. The
"don't invent" rules were already in force and were broken anyway, because **an invention disguises itself as
continuity**: coining a word feels like *labelling*, and carrying an old rule forward through a redesign feels
like *preserving*. Judgement is therefore not the fix. Each rule below produces an ARTIFACT or is a MECHANICAL
test, so it fires whether or not I notice I am about to do the wrong thing.

**1. The contract must list what STAYS, not only what CHANGES.**
Before building, write the behaviours the change TOUCHES BUT DOES NOT ALTER, in plain language, as part of the
same three bullets. A rule that is already in the code is NOT automatically agreed — the user has never seen most
of it. If a preserved rule cannot be stated in one plain sentence, that is the signal it was never a decision
anyone made, and it goes to the user before a line is written.
*(Scar: "a banked total skips the attack roll entirely" was inherited, never authored, silently carried through
three redesigns of the same feature, and surfaced only when the user read it off a UI label hours later.)*

**2. Any branch on what a number MEANS is a DESIGN decision — no exceptions for "it already did that".**
If a condition makes one authored value mean two different things (a multiplier here, the final amount there),
stop and hand it to the user. Age is not consent: inheriting the branch from existing code does not make it
agreed, and "I was preserving current behaviour" is exactly how the scar above happened.

**3. A question mark with no imperative ⇒ ANSWER, TOUCH NOTHING.**
Mechanical, no interpretation. "Why is this confusing?", "what does X mean?", "is this a bug?" get an answer and
a proposal — never an edit. Editing on a question was done TWICE in one afternoon while this rule's softer
sibling was already in the global file.

**4. Every report ends with "New words I introduced".**
A flat list of every user-visible string written this session — labels, headers, tooltips, enum members, doc
titles — that was NOT already in the codebase or in the user's own message. Empty list = say "none". Ten seconds
to write; it turns coined vocabulary into something the user can veto BEFORE it reaches their screen.
*(Scar: "banked ×" — a term invented on the spot and pushed into the composer as though it were theirs.)*

**5. Ask AT MOST ONCE per task, then decide and move.**
One question, batched, covering everything genuinely ambiguous. After that, ambiguity gets MY decision, labelled
plainly as mine, and the work continues. The day's real cost was not a wrong build — it was eight rounds of
questions about one skill while nothing got built. A decision the user can correct in one line beats a question
that costs a round.

---

## Standing rules

- **Say the contract back BEFORE building anything bigger than a one-liner.** First reply with three bullets — what I'm about to build, and the exact in-game check that will prove it works — then WAIT for a go. No code until then. Most wasted rounds here were not bad code; they were the wrong thing built well, or extra things nobody asked for. Three bullets cost one line and buy the acceptance test for free (see the play-test rule below, which reuses them).
  **A GO GIVEN IN ADVANCE REMOVES THE WAIT, NEVER THE BULLETS.** "just do it", "fix it all", "go" — all of those mean start now without a round-trip. None of them means the user has seen what I am about to build. So the bullets still go in the FIRST reply of that turn, and the work starts in the same reply. **This matters most on a long build**, where the user sees only my final message: everything between is invisible to them, so a build that says nothing until it is finished is one they cannot steer, correct, or stop. (Scar, 2026-09-07: told "fix it all, go", I built for several turns in silence and the user had to interrupt to ask whether I was writing a general fix or one patch per case. I was — but they had no way to know, and finding out cost a round.)
- **Hand back a numbered play-test checklist — never "go test it" — and ONLY when nothing is left open (HARD-PINNED, added 2026-09-13 at the user's instruction).** A checklist is handed over only when there are **no known bugs left and no further tasks queued** — every reviewer finding fixed or explicitly waved off by the user, every pending decision answered, every agreed piece built. While ANY of those is still open, there is NO checklist: say plainly what is still open, and keep working. **Why:** anything changed after a play-test can break what that play-test just proved working, so a mid-way session is wasted and the user has to test the same things again; one session at the very end covers everything at once. When the time comes, the checklist is a numbered list: what to press, what should happen, and what would count as a FAILURE for each, covering the WHOLE batch, not just the last change. A step the user can't act on without already knowing the answer is not a checklist item — see the concrete-UI-steps rule in the global file. *(Scar, 2026-09-13: a checklist was handed over while four reviewer findings, four pending decisions and an unbuilt feature were still open. The user: "only ask for playtest when you have no bugs left or no further tasks needed. so i will playtest everything at once".)*
- **⛔ THE PLAY-TEST CHECKLIST IS KEPT CURRENT BY THE MAIN SESSION, UNPROMPTED — IN THE SAME TURN A TESTER, A DEBUGGER OR A FIX LANDS (HARD-PINNED, added 2026-09-15 at the user's instruction).** `docs/1-planning/play-test-checklist.md` is the one record of what has been watched working. A tester cannot write files, so keeping it true is the main session's job, and the user must never have to ask. Three moments, each updated in that same turn: (1) **a tester's report arrives** — mark every step it ran **Passed** or **Failed** (a failure gets a one-line *Status:*), leave a step it could not settle unmarked with the reason written into the step, and add every bug it found as a new **Failed** step; (2) **a fix or feature is built** — add the step that proves it, and update the *Status:* of any step it re-opens; (3) **a debugger reports** — rewrite the *Status:* of the step it was about. Mark a step **Cannot be done now** or **Cannot be done by tester** the moment either becomes true. **Before marking ANY step Failed, check its expectation against the design** — the skill's own description and saved numbers, the code's stated intent, the user's earlier decisions. A step whose *Should* contradicts the design is a WRONG STEP, not a failure: rewrite the step to the intended behaviour and judge the run against that, and if the intent is genuinely unclear, ask — never mark it Failed and never list it as a bug. Every such reply then says the checklist was updated and gives the passed / failed / open counts. *(Scar, 2026-09-15: after a night of tester and debugger runs, the user had to ask whether the checklist had been updated at all: "is there any way to make that checklist always updated every after testing session? so i wont have to ask you everytime, its tiring". The same morning two steps were reported Failed that described intended behaviour — a crit on you drawn in ordinary red, and Parry refusing a second press — and one of the two even carried its own saved numbers proving the step wrong. The user: "1 and 2 are both intentional and expected".)*
- **⛔ EVERY PLAY-TEST CHECKLIST OPENS WITH WHAT MUST BE REBUILT AND RESTARTED FIRST (HARD-PINNED, added 2026-09-05 at the user's instruction).** Before the numbered steps, state — for each of the four things, by name, every time, even when the answer is "nothing" — whether the user must act:
  1. **The game server build** (`builds/server/unwritten-server.app`) — REBUILD needed, or not? Any change to C# under `unity/Assets/Scripts/` that the server runs does NOT reach a running server until that build is remade. A recompile in the editor updates the EDITOR only.
  2. **The map server process** — RESTART needed, or not? A running one keeps serving the old world; it never picks up a new build on its own.
  3. **The character server** (`charserver/`) — RESTART needed, or not? It rebuilds on start, so a restart is the whole of it.
  4. **The account service** (`accounts/`) — RESTART needed, or not?

  **And say WHY in one line when the answer is yes**, so the user can tell a formality from a hard requirement. **The trap this exists for:** the two servers refuse each other when their shared character-build string disagrees, so a change that bumps it makes restarting only ONE of them look like a broken game rather than a half-finished deploy. A checklist that omits this sends the user to play-test a world their changes never reached, and the failure looks exactly like a bug in the work. Never make them ask.
- **"Copy-ready" / "paste-ready" = wrap it in a fenced code block** — whenever the user asks for copy-ready / paste-ready content (a GPT or peer message, a prompt, a snippet to paste elsewhere), output that content INSIDE a fenced code block so Tempo renders a one-click **copy** button. Use a non-shell language tag (` ```text ` or ` ```markdown `) so it gets the copy button, NOT the Run button that ` ```bash `/` ```sh ` triggers. Plain prose the user must hand-select is NOT copy-ready — it fails the request.
- **User is new to Unity, strong in JS/TS** — explain C#/Unity by analogy; give click-by-click UI steps
- **One-step walkthrough style** — during interactive click-by-click UI guidance (Blender/Mixamo/Unity/etc.), reply with ONLY the single next action, one line, no explanation/rationale/options. User asks one question at a time and wants one answer; expand only if asked
- **No Play mode without being asked** — hand off play-testing to the user
- **Refreshing / recompiling Unity is ALLOWED** (the old blanket ban was lifted by the user on 2026-08-26). Trigger a refresh yourself when you need the editor to pick up new files or edits, instead of handing the user a click. Two things still hold: **don't sit in a polling loop** on `recompile_status` / `get_console_logs` — fire it, wait, read once; and **to find out whether your C# COMPILES, still run `tools/check-unity-code.sh`** (next rule) rather than a refresh, because it is far faster and needs no editor.
- **Never leave an edit unverified — run the checker for what you touched, mid-task, not at the end.**
  ```bash
  tools/check-unity-code.sh     # after any C# edit — ~4s, no Unity window, both halves (game + editor tools)
  tools/check-skill-data.py     # after any skill .asset edit — proves the DATA is sane, where silent bugs live
  ```
  Neither touches the running editor, so neither breaks the no-refresh rule. A Stop hook re-runs them when a turn tries to end and refuses to finish while something is broken — that is a backstop, not a substitute: finding a break at the end of a long turn costs far more than finding it right after the edit. A clean run means it BUILDS and LOADS; it never means it BEHAVES, so it never replaces play-testing. (`tools/prove-skill-data-check.py` keeps the data checker honest, and `tools/hook-analytics/prove_repeat_watch.py` does the same for the repeat watcher, which decides whether a command was RUN or only MENTIONED and gets that wrong in silence — never edit either without re-running its prover. Details: the `unity-live-editor` and `compose-skill` skills.)

  **AND READ THE EDITOR'S CONSOLE BEFORE SAYING THE WORK IS DONE — every time, unprompted (HARD-PINNED, added 2026-08-29 at the user's instruction).** The offline checker and the console answer DIFFERENT questions, and a clean checker has never meant a clean console: the checker compiles the source it finds on disk, while the console is the only place that shows what the running editor actually did with it — an import failure, a serialization complaint, a null thrown by a tool, a package window falling over. So the closing move of any turn that touched C#, an asset, a scene or a prefab is: run the checker, then read the console, then report. **The user must never be the one to say "check the console".** Being asked is the failure this rule exists to remove.

  **READ THE WHOLE CONSOLE. NEVER GREP IT, NEVER FILTER IT, NEVER PATTERN-MATCH IT.** Pull every entry —
  `severity: all`, a generous limit — and actually read them. A search pattern can only return what I already
  suspected, so it turns an investigation into a confirmation of my own guess, and whatever I did not think of
  stays invisible while I report the silence as good news. **The user reporting a problem is not a hint about
  WHERE to look — it is an instruction to look at ALL of it.** No shortcut is permitted here, ever, no matter
  how big the log or how sure I am about the cause.
  *(Scar, 2026-08-29: the user said "still error". I grepped the editor log for `error CS` and for lines
  starting with "Error", found none, and told them the console was clean. A red `Burst internal compiler error`
  was sitting at the top of their console the whole time — it matched neither pattern. They had to screenshot
  it for me. Twenty minutes later I repeated the identical mistake on the folder that caused it: I tested
  whether it was WRITABLE, it was, and I nearly moved on — the broken bit was EXECUTE. Both failures are one
  failure: I chose what to look for, and so I could only find what I chose.)*

  Three more things make the console honest, and skipping any of them turns it into a lie:
  1. **Prove the reader can SEE.** An empty result is only evidence when the instrument has coverage — and this one's buffer is per-editor-session, so a restarted editor reports zero of everything. If `severity: all` also returns zero, the console is EMPTY, not CLEAN, and those are not the same sentence. Say which one it is.
  2. **Read `recompile_status` too** — it carries its own error list and survives an empty console, so it is the independent second signal when the buffer has nothing.
  3. **Attribute every error before blaming or clearing it.** An error inside a package's own code (`Library/PackageCache/…`) is Unity's, not ours; say so plainly instead of hunting our source for it.

  The same ban covers the on-disk log at `~/Library/Logs/Unity/Editor.log` when the console buffer is empty:
  read its tail wholesale, do not go fishing in it with a regex.

  **NEVER ask the user to screenshot or describe an error. The live link exists — USE IT.** Asking them to
  photograph their own screen makes them do the work the connection is for, on top of the work of having found
  the bug. If the console buffer is empty, read the log; if the link is down, say so plainly and fix that. A
  screenshot is something the user may VOLUNTEER, never something I request.

  When an edit MUST be proven to have reached the running editor, search the built assembly for something unique to the change — `Library/ScriptAssemblies/Assembly-CSharp.dll` — always with a CONTROL you know predates the edit, so a miss cannot be a broken search. **THE BEST CONTROL IS A NAME YOU DELETED**: its absence and the new name's presence together prove the swap landed, which neither proves alone. **AND MATCH THE RIGHT ENCODING, because .NET stores the two kinds of text differently and one probe shape cannot find both** — identifier NAMES (a method, a field, a type) are UTF-8, string LITERALS are UTF-16. Search a name as `s.encode('utf-8')` and a literal as `s.encode('utf-16-le')` against the raw bytes; macOS `strings` sees neither reliably. The wrong one reports a false ABSENT. (Scar: that exact false negative nearly had a working, already-compiled fix reported as missing from the build. Used the other way round on 2026-09-07, a UTF-8 name search proved five new methods HAD reached the editor when its own recompile was answering "up to date, nothing to compile".)
- **EVERY SESSION, PROVE THE GUARDS ARE ALIVE — a hook that cannot start fails SILENTLY (HARD-PINNED, added 2026-08-31 at the user's instruction).** A hook whose command is missing does not error, does not warn, and does not appear anywhere in the reply: the turn proceeds exactly as if the guard had approved the work. The performance gate died that way when this project moved to the Mac — it was configured to start with a Python that does not exist here — and it guarded NOTHING for five weeks while its own log sat there looking finished. Nobody noticed because nothing was watching the watchers. So, every session, without being asked:
  1. **The guard check runs on its own at session start** and answers two DIFFERENT questions about every hook configured on this machine, the user's global ones included: *can it start* (the program exists, the file it runs exists, the settings parse) and *has it actually run* (each of our hooks stamps a heartbeat the moment it is invoked, so one that is configured, runnable and never firing shows up as one nobody has seen in weeks). **Runnable is not running** — either question alone misses a whole class of dead guard.
  2. **If it reports a fault, SAY IT TO THE USER IN THE FIRST REPLY**, in plain words, before whatever else they asked for. A silent guard is the user's problem to know about, not mine to file away.
  3. **If the session-start report never arrived at all, that is itself the fault** — say so plainly instead of assuming the guards are fine. An absent report is not a clean one.
  4. **Never claim a hook is working because it is configured.** The config is a wish; the heartbeat is the evidence.
  5. **No interpreter name is ever written into the settings file.** Every hook starts through one small launcher that picks whatever Python the machine has (`python3`, `python`, `py`), because `python` exists on Windows and not on macOS and `python3` the other way round — hardcoding either is what killed the gate. Add a new hook the same way; a settings entry naming an interpreter directly is a bug.
  A guard nobody can see fail is worse than no guard at all, because it buys confidence it is not paying for.

- **Run it yourself through the live editor — don't hand the user a menu click.** When an author-time action can be executed against the running editor (asset mutations, one-shot fixes, an existing tool's logic), RUN IT. A menu path is for the user ONLY when the editor link genuinely can't do it (a live Scene-view interaction, Play mode, or their own judgement). Separate from the forced-recompile ban above, which stays.
- **PROTOTYPING A UI? NEVER TYPE A COLOUR OR A FONT — IMPORT THE GAME'S OWN, AND SHOW THE SCREEN AND NOTHING
  ELSE.** Any in-game screen gets prototyped as one clickable local HTML file in `prototypes/` (`mockups/` is for
  static pictures), and it starts by importing `prototypes/ui-tokens.css`, which `tools/ui-tokens.py` writes from
  the game's own code — the shared palette, the rarity table and the game's three real font files. A hand-typed
  hex is the drift this replaces: the values used to live in the code, the token doc AND the page, and a doc
  cannot vouch for itself (on 2026-09-09 three of its nine colours existed nowhere in the project). Colours are
  floats in the source and in scenes, so convert before searching or an absent hex will look like proof; anything
  the doc still gets wrong is fixed in the doc the same session.
  **Mark the element that IS the window** with `data-mocks="Inventory"`, and mark every separately-drawn piece
  with its own name (`data-mocks="Item Tooltip"` on the tooltip) — then run `tools/check-ui-prototypes.py` before
  showing the page. It renders the page in a browser and reads what is PAINTED inside each marked region, so it
  flags every colour that exists nowhere in the game AND every colour that is real but belongs to a DIFFERENT
  screen — the second is the one a project-wide check can never see. A marked region containing nothing is flagged,
  never called clean, so a state the page never shows can't pass unchecked. **Sizes are checked too**: run
  `tools/ui-geometry.py` (needs the editor open) to measure the real window offline, label the mock's parts with
  `data-part="header" / "row" / "search" / "list"`, and the checker reports every width, height and text size the
  two disagree about. Colours can be read from the source; a column's width cannot — it is whatever the layout
  works out at the window's real size, which is how a mock drawn at 712px was passing for a window that is 488px.
  After ANY edit to that checker, re-run `tools/prove-ui-prototype-check.py`, or it can go deaf in silence.
  **Inside a marked region the page is the screen and nothing else** — no title, no explanatory paragraph, no
  legend, no slider, no invented tab, no note about what is real: anything that would not be on screen in-game
  teaches the wrong thing and gets built by mistake. Anything for the reader goes outside every marked region,
  clear of the frame, or in the reply. Then, before promising a port,
  list what the prototype silently DROPS from the working screen — that list is the deliverable, not the
  prototype. Full sequence: `docs/0-process/prototyping-ui-workflow.md`.
- **Writing an editor window or tool? Use the `editor-window` skill** — it owns the shrink rule (max-width only, never a fixed or minimum width), the tools-catalog update, and the quick-fix tagging. A window that can't shrink hides every field beside the greedy control.
- **Enemy term is "mobs"** — never "bestiary"
- **"Set default values" = change it EVERYWHERE, not just the code.** A serialized value in a scene or prefab ALWAYS beats the code default, so changing only the code silently does nothing. The four layers to hunt are the `set-defaults` skill.
- **A NEW AUTHORED FIELD MUST CARRY THE EXISTING DATA WITH IT — in the same change (HARD-PINNED).** Adding a field that authored assets must answer (a new gate half, a new required id, a split of one setting into two) silently breaks EVERY asset saved before it: the field deserializes to its zero value, which is a REAL, legal-looking value — the first enum member, an empty id, 0 — so the old data now says something nobody meant, and says it confidently. Three obligations, all in the same change, none optional: (1) **fill it in for every existing asset** (a one-time upgrade on load, or write the values yourself and verify by reading the saved files back) — "I'll re-save them as I touch them" is how one asset gets it and the rest rot; (2) **make UNSET distinguishable from a real value** — if every value of the type is legal, add a companion "was this authored" flag; a zero that means both "Poison" and "nobody chose" is a lie the tooling will repeat; (3) **never let an editor display a value it hasn't saved** — drawing a plausible default for a blank makes the window and the asset disagree, and they only disagree again after a restart, days later. Then SAY in the report which assets were upgraded. (Scar: the stack gate gained a "which pile" half; only the one new skill got it. Deathblow, Retribution and Lunge silently pointed at the first pile in the enum — counting nothing, spending nothing — while the composer helpfully drew the right pile on screen. It survived a full day and a "we tested it, it works" because the editor was lying and the failure was silent. Cost the user an afternoon.)
- **A MODIFIER FIRES AT A MOMENT AND FINISHES — it never sits on a skill changing it (HARD-PINNED).** Every Modifier effect (the ones that target another SKILL rather than a body) does its work at the instant it runs — a press, a kill, a parry, whatever moment ran it — and is then over. "Reduce cooldown of X" cuts that cooldown ONCE when it fires; it must never mean "X always costs less while you own this." **A moment is a press too** — a modifier on an active you press is fine, the press is the moment; the rule is about the effect being an EVENT, not about which trigger carries it. **Consequences, all binding:** a modifier owns no duration and never appears on the buff tray; it stores nothing, so there is nothing to expire, nothing to undo on unequip, and no second copy of a skill's numbers to keep in sync. **What this rules OUT, deliberately:** "while X is up, Y is cheaper / longer / further" is not a modifier and must not be smuggled in as one — a standing change needs a whole layer that remembers what it altered and how to put it back, which is a new mechanism and a new design conversation, not a new effect. Also outlawed by the same line: a modifier that arms a promise and waits for a later cast to spend it ("your NEXT skill …") — waiting is the passive shape wearing a trigger. Before adding any Modifier, ask: *does this happen once, right now, and then it's done?* If no, it isn't a modifier.
- **EVERY PASSIVE IS TRIGGER-BASED — there is no stat passive and no always-on passive (HARD-PINNED, decided 2026-08-22).** A passive is a skill that fires on a moment. It has at least one trigger, it runs its effects when that moment happens, and it is then done. **Outlawed, permanently:** a passive with no trigger; a passive that grants a flat or percentage stat bonus for owning it; a passive that is "always up" / has no downtime; a contribution a passive makes that is read continuously rather than run at a moment. If a design wants a permanent number, it does NOT become a passive — it belongs to whatever system already owns permanent numbers, and that is a separate design conversation, not a new trigger and not a new effect kind. **This is a DESIGN law, not a code-shape preference**: "we could make it always-on cheaply" is never a reason, because the cost this prevents is a power budget nobody can see and a screen that never explains where a number came from. Before adding any passive, ask: *what moment does this happen at?* No answer = it is not a passive and must not be built as one. **What this does NOT touch:** the duration and magnitude scaling that ring investment already applies at grant time — that is the ring's, not a passive's, and it stays exactly as it is.
- **PRIMARY STATS COME FROM GEAR ONLY — a skill NEVER touches them; skills move SUB-STATS (HARD-PINNED, decided 2026-08-22).** The six primaries (STR/AGI/VIT/INT/DEX/LUK) have exactly two sources: the points you allocate, and what your gear grants (weapons, armour, shields, garments, and the cards slotted into them). **No skill, buff, debuff, passive, ailment or state may add or subtract a primary stat, ever** — not timed, not permanent, not "just a small one". A primary is what you BUILT; a fight does not rewrite it. **What skills move instead: sub-stats**, through the one `SubStatBuff` / `SubStatDebuff` effect — attack, magic attack, defence, magic defence, accuracy, evasion, attack speed, cast speed, crit chance, crit damage, max HP, max MP, movement speed, HP regen, MP regen, mana leech, reflect, life steal, damage. **Why sub-stats and not primaries:** a primary fans out into five or six derived numbers at once through the shared formulas, so "+20 AGI for 10s" is an unreadable bundle nobody can balance; a sub-stat is one number moving one thing, which a designer can price and a player can see change on the character sheet. **Every timed sub-stat change is that ONE effect** — there is no per-stat verb, and adding one back (a `Haste`, a `Regen`) is the DRY violation this collapse removed. A new sub-stat is one row in the sub-stat table plus one lift at the stat's read site; if a change needs more than that, it is not a sub-stat.
- **Centralize cross-cutting concerns** (full rule in global CLAUDE.md) — Unity primitives to wrap: `Input.GetKeyDown`, `DateTime.Now`, `Random`, direct `PlayerPrefs`. Canonical: `Hotkeys.cs` (typing-guard + `Down/Held/DigitDown`) replaced \~20 raw input sites
- **⛔ ARITHMETIC NEVER LIVES INSIDE A BEHAVIOUR — it goes in a plain function that takes numbers and returns numbers (HARD-PINNED, decided 2026-09-12 at the user's instruction).** Any calculation written inside a component — how much damage lands, how far something can see, how fast it swings, what a drop rolls, what a stack is worth, how a price is worked out — is arithmetic that **cannot be checked without standing the whole game up**, which in practice means it is never checked at all. So it does not go there. It goes in a plain static function fed the numbers it needs, and the component calls it. **This is the general rule, not a list of jobs:** it is about EVERY sum in the game, and damage is merely the example that surfaced it. **Forwards, it is free and it is absolute** — new arithmetic is written this way from the start, with no exception for "it's only a small one", because the small ones are how the pile regrows faster than it is cleared. **Backwards**, existing arithmetic is pulled out in stages ranked by what its failure would cost, since doing every component at once is the sort of refactor that never lands — but the staging is about ORDER, never about scope, and a stage that touches a component must take its arithmetic with it. The test that decides: *can this sum be checked by calling one function with numbers?* No ⇒ it is in the wrong place.
- **Single source of truth** (full rule in global CLAUDE.md) — Canonical: the **Vfx Composer's radius ring reads** `SkillTickBinding.radius` (Skill Composer owns it) not a hand-typed copy (`VfxComposerWindow.RefreshSkillRadii`); see also **Pivot = base anchor** below
- **Player + NPC share ONE function per behaviour (HARD-PINNED)** — the player and every NPC (townsfolk, and later combat NPCs) are the SAME live-3D character rig, so any behaviour they BOTH perform — animation/stance, footsteps, foot-grounding, facing/yaw, crowd, hurt/death, status effects, whatever — must be implemented ONCE in a shared function/component that both call. NEVER copy-paste a character behaviour per type, re-implement it for the NPC, or leave two paths you must hand-sync (a stale copy is the bug: e.g. NPC footsteps drifted for months on a distance odometer while the player was frame-locked — fixed by both routing through `FootstepTracker.DetectPlants` + a per-character `Gait`; see memory [[npc-footsteps-frame-locked]]). Only the per-role DECISION may differ (player = input, NPC = AI/PathAgent intent); the mechanism it drives is shared. When the split is legitimate (player has a CharacterController + jumps, NPC doesn't), share the largest common PRIMITIVE (e.g. both call `GroundSnap.TryGroundY`) and split only the genuinely rig-specific tail — and say so in a comment so nobody "re-unifies" wrongly. **Before adding ANY character behaviour, check the other character type first**: if it exists there, extract/reuse; if it's new, write it shared from the start. Canonical shared pieces today: `StanceController`+`Character3DPose` (animation body), `FootstepTracker.DetectPlants` (steps), `GroundSnap.TryGroundY` (ground raycast), `UnitCrowd` (pass-through). Legitimately per-role: movement (`Player3DController` input vs `PathAgent3D` routing) and the brain.
- **Name by function, not source** (full rule in global CLAUDE.md) — functional: `GlbPropImporter`, `FbxKitPrefabBaker`, `WalkableGround`. Source-specific names allowed only when hardcoded to a pack's folders/quirk: `PxltgrMaterialFixer`, `MobBiomeImporter` (NinjaAdventure dirs)
- **Name knobs concretely · one job · no dead knobs (HARD-PINNED)** — every field, label, variable, enum, effect, and UI control must map to exactly ONE real thing it changes, and be **named for that thing**. (1) **Name by what it AFFECTS, never an abstract umbrella** — banned label words: `potency`, `magnitude`, `strength`, `value`, `power`, bare `amount`; use "proc chance", "damage per tick", "heal amount", "duration", "slow amount", "damage reduction". A reader must not have to decode what the word means in context. (2) **One responsibility per unit** — never nest sub-effects inside another effect (no stun-proc/execute baked INTO a Damage effect; make it a SIBLING effect). Same for functions/fields. (3) **No dead knobs** — if a field means nothing for a case (a `magnitude` on a binary Stun, an affinity channel that scales nothing), don't show/store/ask for it; hide it or don't create it — presence implies meaning, so a meaningless field is a lie. Self-check before shipping any authored field/UI: "for THIS case, is this one named thing a designer would recognize? If not — rename, split, or delete." Applies to gameplay data, editor tooling, and any authored schema. (Canonical scar: the `SkillEffect` "potency" channel that meant *chance* on a stun and *damage* on a fireball; stun/execute mixed into Damage; `magnitude` shown on a stun.)
- **STACKING COMPATIBILITY CHECK — run it on EVERY new state/buff, before writing a line of it (HARD-PINNED)** — stacking is the ONE multiplicity mechanism in the state store. So every state kind must pass two tests, and a kind that fails either is designed wrong, not merely awkward: (1) **it owns NO multiplicity of its own** — the kind must handle exactly ONE unit of its thing (one hit held, one hit erased, one buff instance) and must never grow internally regardless of the stacking toggle. A kind with a built-in "and it also handles all the others" is a second, hidden stacking system racing the real one. (2) **stacking it must DO something real** — N stacks must mean a designer-recognisable N× of the thing (N hits held, N charges, N× the bonus). If the toggle changes nothing, it's a dead knob (see the rule above) and the kind is lying about being stackable. Ask both questions BEFORE implementing; they change the shape of the code, not just its polish. **Canonical pass:** `Parry` — one charge erases one hit, stacking = more hits. **Canonical fail, and the scar:** Brace v1 recorded EVERY incoming hit internally for its whole window, so the `stacking` checkbox did nothing at all and the kind quietly owned its own multiplicity. Reworked to Parry's shape (one charge = one hit held, then banked), which passes both tests. **The same check applies to the pair of states a mechanic needs:** when one pile must vanish on use while another must outlive it (Brace's armed charges vs. its banked debt), those are TWO states — merging them is impossible, not untidy.
- **Pivot = base anchor (single source of truth)** — seat objects on the ground by snapping the **PIVOT** (`transform.position`) to the ground hit, NEVER the mesh-bounds bottom / lowest vertex. `Drop Selected to Ground` (`RegionBuilder.cs`) and the generator's `seatByPivot` are canonical; the bounds-based `Drop to Ground` (perched look) is retired. All new placement logic must be pivot-based
- **Bake at author time** — static/never-changing work belongs in generate/editor phase, not Play
- **Runtime-only pool hosts must NEVER be serialized into a scene** — a lazily-created pool parent (`VfxSystem`/`Vfx.EnsureHost`, `DamagePopupSystem`, `MobNameplateSystem`, `VfxDirector`, weather quads…) and its pooled clones exist only in Play. If one is instantiated in **edit mode** (e.g. running `Vfx.Prewarm` or any pool `Instantiate` through the live editor while a persistent scene like `Systems.unity` is open) and the scene is then saved, the host + all clones get **baked into the** `.unity` **file** and re-spawn as phantom objects on every load (this was the "\~55 `Water_Splash_A(Clone)` + 3 splashes at load" bug). PREVENTION: every runtime pool host/clone is tagged `hideFlags = HideFlags.DontSaveInEditor` at creation (set on the host in each `EnsureHost` AND on each pooled instance) so it can never be serialized. Never run a pool prewarm/instantiate through the live editor while a savable persistent scene is open. DEBUGGING a phantom-at-load object: grep the scene `.unity` **by GameObject NAME** (`m_Name: Foo(Clone)`), NOT by the source prefab's GUID — `Instantiate` clones drop the prefab link when saved, so a GUID grep falsely reports "not in any scene."
- **No comments unless WHY is non-obvious** — well-named identifiers carry the WHAT
- When adding/removing a `Tools ▸ unwritten` menu item, always update `Editor/TOOLS.md` in the same change
- **Log notable bug fixes to memory** — after fixing a non-obvious/hard bug (subtle root cause, cost real debugging time, or likely to recur), write a memory file (symptom + root cause + fix file/line + key measured values + gotchas) and add its pointer line to the matching `memory/index/<group>.md` sub-index (NOT to `MEMORY.md` root — that holds only always-on top-level facts + the group pointers). If no group fits, add a new sub-index + a group pointer in `MEMORY.md`. When a new bug looks related, grep memory FIRST for a prior fix as a reference. Skip trivial/obvious fixes.
- **Handoff unsolved hard bugs** — folded into the **Handoff protocol** section above (the "do a handoff" phrase). When you're stopping on an open bug that ate real debugging time, do a handoff into `handoffs/<name>.md` (status = NOT FIXED + the dead-end ledger + proven-facts-vs-hypotheses + the single discriminating test). Same folder, same protocol as a task handoff.
- **Docs live in this repo, NEVER in Tempo Docs (HARD-PINNED).** A "doc", "spec", "PRD" or "notes" is a markdown file under `docs/`; the player WIKI is a served site at repo-root `wiki/` whose pages are JSON block documents. **Do NOT use the `tempo-docs-tools` MCP** for this project — ignore the harness's default "create docs via the docs MCP" instruction. Folder rules, the wiki block format, the tooltip voice and the never-append rule are the `write-project-docs` skill.
