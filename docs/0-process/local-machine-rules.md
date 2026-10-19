# Local-machine rules — the user's own Mac and PC only

These rules need what only the user's own machine has: the running Unity editor and its console, the
servers they start, the game's logs, the client build, and `settings.env`. A cloud session has none of
them, so this file is kept out of it, and the always-on `CLAUDE.md` stays smaller there.

**How it loads.** On the user's machines, the session-start guard check (`tools/hook-analytics/guard_check.py`)
makes sure the git-ignored `CLAUDE.local.md` at the repo root imports this file, so Claude Code reads it with
`CLAUDE.md` at every session start. A cloud session is detected by `CLAUDE_CODE_REMOTE=true`
(`tools/platform.sh where`), and there the import is never written. These rules rank exactly as they would
inside `CLAUDE.md`.

## Diagnosing a scene that reports changes nobody made (from RULE NOEDITORWRITES)

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
including the one the user is playing against. The safe form, always — the same command on the Mac and on Windows:

```
tools/platform.sh stop-port <port>
```

**And before assuming a listening process is mine, check WHEN IT STARTED** — `tools/platform.sh port-info <port>`
prints each listener's id, start time and program. A start time later than my own last kill means it is theirs, and
it stays up.

**Say in the report what I started and that it is stopped**, or say plainly that I started nothing. The report
line is what makes this checkable rather than a good intention.

*(Scar, 2026-09-07: cleaning up a test copy of the character server, I ran `pkill -f CharServer` and killed the
user's running one along with it — which is also the "stop everything" command their own runbook prints. Their
words afterwards: "i dont need your servers, i start them myself".)*

## Ground truth that lives on this machine (from the second-fix rule)

- **A CHARACTER** = a row in postgres, and this is the single most useful measurement in the project. A player's
  machine has not written progress since the server took ownership of it — `SaveManager` only READS — so what a
  character actually IS lives in one place and you can read it in one command. It settles "is their hotbar really
  empty", "did that binding save", "what is equipped" in seconds, and it is the truth the game loads:
  ```bash
  psql "$(grep '^DATABASE_URL=' settings.env | cut -d= -f2-)" -A -t -c \
    "select c.name, c.updated_at, h.slots, c.progress->'equipItemIds' \
     from character c left join character_hotbar h on h.character_id = c.id \
     where c.deleted_at is null order by c.updated_at desc limit 5;"
  ```
  The hotbar (save v18+) is its own row in `character_hotbar`, not in the progress document.
  `jsonb_pretty(progress)` dumps a whole character. Full detail: `docs/1-planning/querying-the-database.md`.
- **Did my edit reach the editor** = search the assembly it built, never the source on disk — the rule and the
  encoding trap are in the console section below, written once.
- **WHAT ACTUALLY HAPPENED AT RUNTIME** = three different logs, and they are not interchangeable. The editor's is wherever
  `tools/platform.sh editor-log` says (`~/Library/Logs/Unity/Editor.log` on the Mac); a client BUILD writes
  `tools/platform.sh player-log` (and `Player-prev.log` beside it for the run before); and the GAME SERVER writes `logs/server.log` at the top of this
  repo, with the run before it kept as `logs/server-prev.log`. An idle server is now SILENT — nothing writes on a
  timer — so a quiet log means nothing happened, not that logging is off. **READ THE SERVER'S OWN LOG BEFORE REASONING ABOUT
  WHAT IT DECIDED** — it already writes down why it refused a cast, turned a connection away, dropped a player or
  failed to save somebody, in 44 places. Working that out from the source instead is the slow way to an answer it
  has already written. Every line carries the date and time to the millisecond and names units by map and spawn_id;
  how to read it and the two scripts that total and check it are the **`server-log` skill** (loaded into the tester,
  debugger and reviewer), with every line shape in `docs/0-process/reading-the-game-log.md`. If it is missing, the server was started the old way, straight to the terminal: the run
  command in the network runbook now tees it to that file.
- **WHAT IS ACTUALLY IN THE CLIENT BUILD, and how big** = Unity's own size report, written into
  the editor's log (`tools/platform.sh editor-log`) after every build. Search it for `Used Assets and files from the Resources
  folder, sorted by uncompressed size`: above that line is the split by kind (textures / meshes / audio /
  levels), below it is every shipped asset in descending size. **Never add up folder sizes instead** — most of
  `Assets/` is never referenced and never ships, so the folders and the build disagree by multiples. (Measured
  2026-09-07: the client is 75% textures and 5% audio, while on disk the audio folder is bigger than the
  textures. Guessing from folders sent a whole round in the wrong direction.)
- **Live engine values** = the Unity CLI against the running editor (`unity cmd …`, or the `unity-editor-mcp` tools) — see the **live link to Unity** section below. A live connection beats raising model effort — most stalls here are missing observation, not missing intelligence.

## The live link to Unity — it EXISTS; check it, then use it

There is one road to the running editor and it works. **Check it before leaning on it** — the `unity-editor-mcp`
`editor_status` tool, or `unity cmd --project-path <this repo>/unity editor_status` from a terminal (`"status":"ready"` = connected) — and
**if it is down, say so plainly** rather than working blind — that exact failure kept a dead bridge in use for five days.

**Reach for it without being told.** The user should never have to say "check Unity". Any time you are about to
state a live value, describe how something looks, run an author-time action, or take a second swing at a bug that
survived one fix — that is the instruction. Measure, don't guess.

**Three things stay the user's:** Play mode, their visual judgement, and their Scene view — never move its camera
and never change which scene is open (added 2026-09-25 at the user's instruction; work on a scene without showing
it). And the forced-recompile ban below.

The connection details, the proven-vs-unproven list, how to run an editor action safely, and the offline compile
checker are the **`unity-live-editor` skill**.

## Tester protocol — the phrase "spawn a tester"

**"spawn a tester"** is the ONLY thing that starts one. It spawns a sub-agent that actually PLAYS the game
through the live editor and reports what it OBSERVED — the protocol is the **`tester` skill**.

**The default is OFF and it is never suggested**, because it takes over the editor for as long as it runs.

It is the opposite half of the reviewer: a reviewer only reads, a tester only reports what it watched happen.
Never let one wear the other's words. Two players at once is beyond it — the editor is one client.

## Working against the running editor (from the standing rules)

- **Refreshing / recompiling Unity is ALLOWED** (the old blanket ban was lifted by the user on 2026-08-26). Trigger a refresh yourself when you need the editor to pick up new files or edits, instead of handing the user a click. **BUT NEVER WHILE THE USER IS PLAYING — read `editor_status` first, and if `playMode` is anything but `stopped`, do not recompile.** A recompile mid-play reloads every script's memory and ends the session as a wall of errors (2026-09-21: two scripts threw every frame and the user had to stop and restart Play to test the very fix I had just compiled). Two things still hold: **don't sit in a polling loop** on `recompile_status` / `get_console_logs` — fire it, wait, read once; and **to find out whether your C# COMPILES, still run `tools/check-unity-code.sh`** (next rule) rather than a refresh, because it is far faster and needs no editor.

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

The same ban covers the editor's on-disk log (`tools/platform.sh editor-log`) when the console buffer is empty:
read its tail wholesale, do not go fishing in it with a regex.

**NEVER ask the user to screenshot or describe an error. The live link exists — USE IT.** Asking them to
photograph their own screen makes them do the work the connection is for, on top of the work of having found
the bug. If the console buffer is empty, read the log; if the link is down, say so plainly and fix that. A
screenshot is something the user may VOLUNTEER, never something I request.

When an edit MUST be proven to have reached the running editor, search the built assembly for something unique to the change — `Library/ScriptAssemblies/Assembly-CSharp.dll` — always with a CONTROL you know predates the edit, so a miss cannot be a broken search. **THE BEST CONTROL IS A NAME YOU DELETED**: its absence and the new name's presence together prove the swap landed, which neither proves alone. **AND MATCH THE RIGHT ENCODING, because .NET stores the two kinds of text differently and one probe shape cannot find both** — identifier NAMES (a method, a field, a type) are UTF-8, string LITERALS are UTF-16. Search a name as `s.encode('utf-8')` and a literal as `s.encode('utf-16-le')` against the raw bytes; macOS `strings` sees neither reliably. The wrong one reports a false ABSENT. (Scar: that exact false negative nearly had a working, already-compiled fix reported as missing from the build. Used the other way round on 2026-09-07, a UTF-8 name search proved five new methods HAD reached the editor when its own recompile was answering "up to date, nothing to compile".)

- **Run it yourself through the live editor — don't hand the user a menu click.** When an author-time action can be executed against the running editor (asset mutations, one-shot fixes, an existing tool's logic), RUN IT. A menu path is for the user ONLY when the editor link genuinely can't do it (a live Scene-view interaction, Play mode, or their own judgement). Separate from the forced-recompile ban above, which stays.

## Proving the guards are alive (from the standing rules)

- **EVERY SESSION, PROVE THE GUARDS ARE ALIVE — a hook that cannot start fails SILENTLY (HARD-PINNED, added 2026-08-31 at the user's instruction).** A hook whose command is missing does not error, does not warn, and does not appear anywhere in the reply: the turn proceeds exactly as if the guard had approved the work. The performance gate died that way when this project moved to the Mac — it was configured to start with a Python that does not exist here — and it guarded NOTHING for five weeks while its own log sat there looking finished. Nobody noticed because nothing was watching the watchers. So, every session, without being asked:
  1. **The guard check runs on its own at session start** and answers two DIFFERENT questions about every hook configured on this machine, the user's global ones included: *can it start* (the program exists, the file it runs exists, the settings parse) and *has it actually run* (each of our hooks stamps a heartbeat the moment it is invoked, so one that is configured, runnable and never firing shows up as one nobody has seen in weeks). **Runnable is not running** — either question alone misses a whole class of dead guard.
  2. **If it reports a fault, SAY IT TO THE USER IN THE FIRST REPLY**, in plain words, before whatever else they asked for. A silent guard is the user's problem to know about, not mine to file away.
  3. **If the session-start report never arrived at all, that is itself the fault** — say so plainly instead of assuming the guards are fine. An absent report is not a clean one.
  4. **Never claim a hook is working because it is configured.** The config is a wish; the heartbeat is the evidence.
  A guard nobody can see fail is worse than no guard at all, because it buys confidence it is not paying for.
