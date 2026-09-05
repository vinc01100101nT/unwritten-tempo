# CLAUDE.md — roguelike-mmo (unwritten)

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

## Bugfix confidence protocol (ALWAYS)

At the END of every bugfix response, state your confidence that the bug is ALREADY fixed, as a percentage (0%–100%) on its own final line, e.g. `**Fix confidence: 75%**`. Be honest — reflect whether the fix was verified live, only reasoned about, or blocked (e.g. Unity unreachable). Do not inflate.

## Second-fix rule: after ONE failed fix, stop hypothesizing — go get GROUND TRUTH

If a fix attempt doesn't resolve the bug, do NOT ship a second fix built on more reasoning about how the system "probably" works. The next move is to MEASURE: read the real data and replay the real math, then fix from evidence. Reasoning-only fixes on a bug that already survived one round burn many turns and usually miss a deeper cause. (This wardrobe bug ate \~4 rounds of plausible-but-wrong fixes; it was cracked in one pass the moment I decoded the actual save + PNG + FBX bytes and re-ran detection offline — the true cause, a dark-color gamma mismatch, was invisible to reasoning but obvious in the numbers.)

Ground-truth sources for THIS project (cheap, no Unity needed — a plain Python/Bash script reads them):

- **Save file** = the persisted state that's actually loaded: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\unwritten\unwritten_save.json`.
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
- `unity/Assets/Scripts/Editor/TOOLS.md` — canonical catalog of all editor menu items (\~39 cmds)
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
2. Grep the doc, and the folder around it, for the OLD term / OLD number / OLD name I just replaced.
3. Reconcile every hit in the SAME turn. A stale line I plan to fix later is a stale line that ships.
4. Say in the report which stale passages I found and killed, or say plainly that I found none.

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
- **Hand back a numbered play-test checklist — never "go test it".** When a batch of gameplay changes is ready, end with a numbered list: what to press, what should happen, and what would count as a FAILURE for each. Batch the changes so one play session covers several, instead of one session per change. A step the user can't act on without already knowing the answer is not a checklist item — see the concrete-UI-steps rule in the global file.
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
  Neither touches the running editor, so neither breaks the no-refresh rule. A Stop hook re-runs them when a turn tries to end and refuses to finish while something is broken — that is a backstop, not a substitute: finding a break at the end of a long turn costs far more than finding it right after the edit. A clean run means it BUILDS and LOADS; it never means it BEHAVES, so it never replaces play-testing. (`tools/prove-skill-data-check.py` keeps the data checker honest — never edit that checker without re-running it. Details: the `unity-live-editor` and `compose-skill` skills.)

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

  When an edit MUST be proven to have reached the running editor, search the built assembly for a string unique to the change — `Library/ScriptAssemblies/Assembly-CSharp.dll`, with a control string you know predates the edit to prove the search works. **.NET stores literals as UTF-16, so macOS `strings` cannot see them and will report a false ABSENT** — match `s.encode('utf-16-le')` against the raw bytes instead. (Scar: that exact false negative nearly had a working, already-compiled fix reported as missing from the build.)
- **EVERY SESSION, PROVE THE GUARDS ARE ALIVE — a hook that cannot start fails SILENTLY (HARD-PINNED, added 2026-08-31 at the user's instruction).** A hook whose command is missing does not error, does not warn, and does not appear anywhere in the reply: the turn proceeds exactly as if the guard had approved the work. The performance gate died that way when this project moved to the Mac — it was configured to start with a Python that does not exist here — and it guarded NOTHING for five weeks while its own log sat there looking finished. Nobody noticed because nothing was watching the watchers. So, every session, without being asked:
  1. **The guard check runs on its own at session start** and answers two DIFFERENT questions about every hook configured on this machine, the user's global ones included: *can it start* (the program exists, the file it runs exists, the settings parse) and *has it actually run* (each of our hooks stamps a heartbeat the moment it is invoked, so one that is configured, runnable and never firing shows up as one nobody has seen in weeks). **Runnable is not running** — either question alone misses a whole class of dead guard.
  2. **If it reports a fault, SAY IT TO THE USER IN THE FIRST REPLY**, in plain words, before whatever else they asked for. A silent guard is the user's problem to know about, not mine to file away.
  3. **If the session-start report never arrived at all, that is itself the fault** — say so plainly instead of assuming the guards are fine. An absent report is not a clean one.
  4. **Never claim a hook is working because it is configured.** The config is a wish; the heartbeat is the evidence.
  5. **No interpreter name is ever written into the settings file.** Every hook starts through one small launcher that picks whatever Python the machine has (`python3`, `python`, `py`), because `python` exists on Windows and not on macOS and `python3` the other way round — hardcoding either is what killed the gate. Add a new hook the same way; a settings entry naming an interpreter directly is a bug.
  A guard nobody can see fail is worse than no guard at all, because it buys confidence it is not paying for.

- **Run it yourself through the live editor — don't hand the user a menu click.** When an author-time action can be executed against the running editor (asset mutations, one-shot fixes, an existing tool's logic), RUN IT. A menu path is for the user ONLY when the editor link genuinely can't do it (a live Scene-view interaction, Play mode, or their own judgement). Separate from the forced-recompile ban above, which stays.
- **Writing an editor window or tool? Use the `editor-window` skill** — it owns the shrink rule (max-width only, never a fixed or minimum width), the tools-catalog update, and the quick-fix tagging. A window that can't shrink hides every field beside the greedy control.
- **Enemy term is "mobs"** — never "bestiary"
- **"Set default values" = change it EVERYWHERE, not just the code.** A serialized value in a scene or prefab ALWAYS beats the code default, so changing only the code silently does nothing. The four layers to hunt are the `set-defaults` skill.
- **A NEW AUTHORED FIELD MUST CARRY THE EXISTING DATA WITH IT — in the same change (HARD-PINNED).** Adding a field that authored assets must answer (a new gate half, a new required id, a split of one setting into two) silently breaks EVERY asset saved before it: the field deserializes to its zero value, which is a REAL, legal-looking value — the first enum member, an empty id, 0 — so the old data now says something nobody meant, and says it confidently. Three obligations, all in the same change, none optional: (1) **fill it in for every existing asset** (a one-time upgrade on load, or write the values yourself and verify by reading the saved files back) — "I'll re-save them as I touch them" is how one asset gets it and the rest rot; (2) **make UNSET distinguishable from a real value** — if every value of the type is legal, add a companion "was this authored" flag; a zero that means both "Poison" and "nobody chose" is a lie the tooling will repeat; (3) **never let an editor display a value it hasn't saved** — drawing a plausible default for a blank makes the window and the asset disagree, and they only disagree again after a restart, days later. Then SAY in the report which assets were upgraded. (Scar: the stack gate gained a "which pile" half; only the one new skill got it. Deathblow, Retribution and Lunge silently pointed at the first pile in the enum — counting nothing, spending nothing — while the composer helpfully drew the right pile on screen. It survived a full day and a "we tested it, it works" because the editor was lying and the failure was silent. Cost the user an afternoon.)
- **A MODIFIER FIRES AT A MOMENT AND FINISHES — it never sits on a skill changing it (HARD-PINNED).** Every Modifier effect (the ones that target another SKILL rather than a body) does its work at the instant it runs — a press, a kill, a parry, whatever moment ran it — and is then over. "Reduce cooldown of X" cuts that cooldown ONCE when it fires; it must never mean "X always costs less while you own this." **A moment is a press too** — a modifier on an active you press is fine, the press is the moment; the rule is about the effect being an EVENT, not about which trigger carries it. **Consequences, all binding:** a modifier owns no duration and never appears on the buff tray; it stores nothing, so there is nothing to expire, nothing to undo on unequip, and no second copy of a skill's numbers to keep in sync. **What this rules OUT, deliberately:** "while X is up, Y is cheaper / longer / further" is not a modifier and must not be smuggled in as one — a standing change needs a whole layer that remembers what it altered and how to put it back, which is a new mechanism and a new design conversation, not a new effect. Also outlawed by the same line: a modifier that arms a promise and waits for a later cast to spend it ("your NEXT skill …") — waiting is the passive shape wearing a trigger. Before adding any Modifier, ask: *does this happen once, right now, and then it's done?* If no, it isn't a modifier.
- **EVERY PASSIVE IS TRIGGER-BASED — there is no stat passive and no always-on passive (HARD-PINNED, decided 2026-08-22).** A passive is a skill that fires on a moment. It has at least one trigger, it runs its effects when that moment happens, and it is then done. **Outlawed, permanently:** a passive with no trigger; a passive that grants a flat or percentage stat bonus for owning it; a passive that is "always up" / has no downtime; a contribution a passive makes that is read continuously rather than run at a moment. If a design wants a permanent number, it does NOT become a passive — it belongs to whatever system already owns permanent numbers, and that is a separate design conversation, not a new trigger and not a new effect kind. **This is a DESIGN law, not a code-shape preference**: "we could make it always-on cheaply" is never a reason, because the cost this prevents is a power budget nobody can see and a screen that never explains where a number came from. Before adding any passive, ask: *what moment does this happen at?* No answer = it is not a passive and must not be built as one. **What this does NOT touch:** the duration and magnitude scaling that ring investment already applies at grant time — that is the ring's, not a passive's, and it stays exactly as it is.
- **PRIMARY STATS COME FROM GEAR ONLY — a skill NEVER touches them; skills move SUB-STATS (HARD-PINNED, decided 2026-08-22).** The six primaries (STR/AGI/VIT/INT/DEX/LUK) have exactly two sources: the points you allocate, and what your gear grants (weapons, armour, shields, accessories, garments, and the cards slotted into them). **No skill, buff, debuff, passive, ailment or state may add or subtract a primary stat, ever** — not timed, not permanent, not "just a small one". A primary is what you BUILT; a fight does not rewrite it. **What skills move instead: sub-stats**, through the one `SubStatBuff` / `SubStatDebuff` effect — attack, magic attack, defence, magic defence, accuracy, evasion, attack speed, cast speed, crit chance, crit damage, max HP, max MP, movement speed, HP regen, MP regen, mana leech, reflect, life steal, damage. **Why sub-stats and not primaries:** a primary fans out into five or six derived numbers at once through the shared formulas, so "+20 AGI for 10s" is an unreadable bundle nobody can balance; a sub-stat is one number moving one thing, which a designer can price and a player can see change on the character sheet. **Every timed sub-stat change is that ONE effect** — there is no per-stat verb, and adding one back (a `Haste`, a `Regen`) is the DRY violation this collapse removed. A new sub-stat is one row in the sub-stat table plus one lift at the stat's read site; if a change needs more than that, it is not a sub-stat.
- **Centralize cross-cutting concerns** (full rule in global CLAUDE.md) — Unity primitives to wrap: `Input.GetKeyDown`, `DateTime.Now`, `Random`, direct `PlayerPrefs`. Canonical: `Hotkeys.cs` (typing-guard + `Down/Held/DigitDown`) replaced \~20 raw input sites
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
