# tools/

Dev utilities for this project.

## build-game.sh

Builds the **server** (no window) or the **client** (what players run), from the terminal.

```bash
tools/build-game.sh server            # headless server for this Mac
tools/build-game.sh client            # the game players run
tools/build-game.sh both
tools/build-game.sh server --linux    # server for a Linux host — what you deploy
```

Why it's needed: Unity 6 treats a dedicated server as a *variant* of a desktop build rather than its own
target, so there is no menu item to click; and a batch-mode build can't run while the editor holds the
project open. This drives the editor you already have running, which is the normal state here. **Unity must
be open on this project.**

Takes a few minutes. It decides pass/fail by checking the artefact actually appeared, not by trusting the
request — the request usually times out long before the build ends, which is expected and harmless. On
success it prints the exact command to run what it built.

Used by [Starting the Network](../docs/2-systems/networking/starting-the-network.md).

## check-unity-code.sh

Checks the game's C# for **compile errors without opening Unity** and without forcing a refresh. ~4 seconds.

Why it's needed: the project rule forbids force-refreshing Unity (each refresh blocks ~60s and often times out), so a C# edit used to go unverified until the user happened to focus the editor. Unity ships its own C# compiler inside the app bundle (`Unity.app/Contents/Resources/Scripting`) and every reference library the game needs is already on disk — in `Managed/UnityEngine/`, the .NET compatibility stand-ins, and `unity/Library/ScriptAssemblies/` for package libraries — so this runs the *same* check Unity would. It never touches the running editor.

Checks both halves separately: the game (`Assets/Scripts`) and the editor tools (`Assets/Scripts/Editor`). An editor-only mistake is otherwise invisible until someone opens that window.

### Usage

```bash
tools/check-unity-code.sh            # both (default)
tools/check-unity-code.sh runtime    # game scripts only
tools/check-unity-code.sh editor     # editor tools only
```

Exit code **0** = clean, **1** = compile errors (printed with exact file + line), **2** = setup problem (it names what's missing). Reads the required Unity version from `unity/ProjectSettings/ProjectVersion.txt`; if that exact version isn't installed it warns and falls back to the newest installed one. Needs `unity/Library/ScriptAssemblies/` (present on any project Unity has opened once).

Verified to **catch** deliberate errors in both halves, not just to report "clean". A clean run means Unity will build it — it does **not** mean the game behaves correctly, so it never replaces play-testing.

## check-before-commit.sh

Every commit runs the game's checks over the **whole project** first, and is refused while one fails.

```bash
tools/check-before-commit.sh      # run by .git/hooks/pre-commit; fine to run by hand
git commit --no-verify            # commit past a failure anyway, knowing it is broken
```

Why it's needed: the end-of-turn guard only checks the files an AI session edited, and only inside that session. A
change to one file that breaks another, or any change made by hand, used to reach a commit with nothing checked.

Always runs: the compile check, the game-math check, the skill-data check, the asset-wiring sweep and the per-frame
performance scan — 13 seconds together on this Mac. A checker's own self-test runs only when that checker is part of
the commit, and adds up to a minute (34 seconds with the game-math and skill-data self-tests both in). Left out on purpose: `wiki-sync-check.py` and `check-ui-prototypes.py`, which judge documents and mock pages
rather than whether the game works.

It reads the project **as it is on disk**, not only what is staged, so unfinished work outside the commit can stop
it. A check that cannot run at all (no Unity, no Python) is reported and does not block.

The hook itself lives in `.git/hooks/`, which git does not copy with the repository — a fresh clone needs it put
back: a two-line file whose last line is `exec sh "$(git rev-parse --show-toplevel)/tools/check-before-commit.sh"`.

## check-game-math.sh · prove-game-math-check.py · game-math-tests/

Is every number the game works out still what it was?

```bash
tools/check-game-math.sh          # ~3 s, no Unity window; exit 1 if a number moved
tools/prove-game-math-check.py    # prove the check still CATCHES a moved number
```

A wrong number is **silent**. A hit that does 47 instead of 52 looks exactly like a hit, and nothing on screen
says the experience curve moved. Without this, the only thing that finds one is playing the game and eventually
noticing — weeks later, with no clue which change caused it.

**How it runs without Unity:** the game's scripts compile to an ordinary .NET library, and Unity ships a runner
that executes one outside the editor. Pure arithmetic runs; anything reaching the live engine throws loudly
rather than quietly returning a wrong answer — which is the exact boundary wanted, since the sums are what is
being checked. No test framework, no test assembly, no domain reload. The test sources live outside
`unity/Assets/` so Unity never imports them and they never ship in a build.

**Pinned today:** the experience curve against its own closed form · the stat-point budget round-tripping · the
promised accuracy window · a critical ignoring both halves of defence · magic never falling below half · the
element chart matching its enum's real size · every stat on the ring exactly once and under its ceiling · the
rarity ladder accelerating · a buff's number read as a bonus whether it stacks or not · regen paid per second, not
per tick.

The last two are past bugs pinned so they cannot come back: Momentum's +3% a stack once ran as +103%, and regen once
healed 25% more than its own label. The prover puts each bug back in turn and watches the check fail.

**Two rules for anything added here.** Write the GENERAL form — "the table matches its list's real size" survives
an eleventh element; "the table is 10 by 10" quietly stops covering the thing it names. And where a specific
number was PROMISED, assert the literal, never the constant: reading the ceiling from the code makes the check
self-referential, so raising the ceiling raises the assertion and it can never fail. That mistake was made here
on the first draft and the prover caught it — which is what the prover is for.

## check-asset-rules.py · prove-asset-rules-check.py · packages-in-use.txt

Are the saved assets the shape `unity/Assets/ASSETS.md` says they must be?

```bash
tools/check-asset-rules.py           # every rule; exit 1 on a FAIL; --all also lists the WARN debt
tools/prove-asset-rules-check.py     # plants 13 faults in a scratch copy and expects each named
```

Reads texture import settings (compressed, ≤ 2048, not readable), the prop palette (static flags; plants with no
collider and no shadow casting; no batching on wind-shader foliage), level-of-detail families, the pinned project
settings (shadow map, GPU skinning, bone weights), the package manifest against `packages-in-use.txt`, and — the
one that matters most — whether every field a script declares is present in the prefab that bakes it. Built on
2026-09-20 from an audit that found 85 MB of uncompressed ground tiles, 4,608 solid shadow-casting plants, eight
unused packages and all 84 creatures loading at level 1 because one struct was not marked serializable.

## check-biome-table.py · prove-biome-table-check.py

Is every biome's look whole, and do the biome test maps hold their props where they were put?

```bash
tools/check-biome-table.py           # the Biome Table, the biome list, the World Creator's list, the held weather, the test_* maps
tools/prove-biome-table-check.py     # plants 14 faults in a throwaway mirror and expects each named
```

Fails when a biome has no row, a ground tile is laid for a job its file name does not allow (BASE / PATCH / PATH),
a row keeps a colour wash, a prop points at no file, a biome places no props at all, the Grassland row changes, the
browser World Creator is missing a biome, a `test_*` map's saved prop positions point at a part the prop no
longer has (the fault that stacks every prop at the map centre), a `test_*` map is missing its biome marker or
carries the wrong one, or the per-biome weather list holds a level outside 0..1. Built 2026-09-25 with the
all-biome generator.

## check-map-ids.py · prove-map-ids-check.py

Does every map id agree with the ONE place map ids come from — the world layout
(`tools/world-creator/worlds/world-regions.json`)? A map's id is its name in snake_case, set by the browser World
Creator; it is the scene file name, what `/tp` takes, and what a character's save names.

```bash
tools/check-map-ids.py               # the layout, the scenes, their doors, Build Settings, the world map, Map Painter files, starting map, music table
tools/prove-map-ids-check.py         # plants 9 faults in a throwaway mirror and expects each named
```

Fails when an id is not the snake_case of its name or is used twice, a generated map has no scene file of that
exact name or is missing from Build Settings, a door in a world scene leads to a map outside the layout or to an
arrival point that map does not have, the in-game world map lists a different set of maps than the layout, a Map
Painter file's saved name differs from its file name, or the starting map or a music-table entry names no scene.
Built 2026-09-26 when every map was renamed to its name.

## check-editor-writes.py · prove-editor-writes-check.py · cs_methods.py

Can editor code write a saved file with nobody having clicked? (RULE NOEDITORWRITES, mechanically.)

```bash
tools/check-editor-writes.py         # follows every load/import/save-time trigger to the writes it reaches
tools/prove-editor-writes-check.py   # plants the three real 2026-09-20 shapes and expects each named
```

A write on such a path passes only with `// editor-write-ok: <why>` beside it — the reason sits with the code, as
`// perf-ok:` does. `cs_methods.py` is the shared method-and-call reader this and the client-math check use.

## check-client-math.py · prove-client-math-check.py · client-math-baseline.txt

Does runtime code work out a game number on a path the player's machine can run? (RULE NOCLIENTMATH.)

```bash
tools/check-client-math.py           # exit 1 on a NEW sum; --all also lists the known ones
tools/prove-client-math-check.py     # plants the level-up, a tooltip and a gold roll; expects each named
```

A sum is fine in a `static class`, behind a server guard or `[Server]`/`[Command]`, or with `// server-math: <why>`.
Everything else that existed on 2026-09-20 is listed in `client-math-baseline.txt` — 54 known sums, debt that can
only shrink: fix one, delete its line; never add a line.

## check-server-rules.py · prove-server-rules-check.py

Do the two services still keep the rules in `SERVER.md`?

```bash
tools/check-server-rules.py          # exit 1 on a FAIL; --all also lists unapplied migrations
tools/prove-server-rules-check.py    # plants 8 faults in a scratch copy and expects each named
```

No secret with a literal default, both pools bounded, the rate limiter registered, no character route on the
sign-in service, and every `order by` naming the index a migration creates.

## check-asset-wiring.py · prove-asset-wiring-check.py · unity_assets.py

Does everything the game loads still point at something that exists?

```bash
tools/check-asset-wiring.py          # sweep; exit 1 if anything points at nothing
tools/prove-asset-wiring-check.py    # prove the sweep still SEES a planted break
```

A stale reference is **silent**: Unity drops it, the code falls back to a default, and the game runs wearing the
wrong art with nothing written anywhere. The case that prompted it had all 20 bosses standing in the world
wearing an ordinary creature's sprite for weeks, because the prefab baker searched one art folder and their art
was in the other.

It checks two things today: every reference in our own files resolves, and every creature's sprite folder exists
in **either** art folder. It reports and never repairs — what to do about a break is a decision, not a sweep's
job. Runs in about 3.5 s and is wired into the end-of-turn gate, which shows only faults in files the session
actually edited.

**The trap it is built against:** resolving guids from `Assets/` alone reports every reference into an installed
package as broken. Measured on 2026-09-12, that mistake produced 34 confident false alarms about hand-authored
material files that were completely fine. `unity_assets.py` is the shared reader that resolves guids across the
project **and** its packages — 28,346 of them against the 7,547 a project-only walk sees — so both this sweep and
anything written later ask one correct question instead of each getting it wrong separately.

## hook-analytics/write_scope_gate.py · prove-write-scope-gate.py

The fence behind every restricted agent role in `.claude/agents/`. A role told "you only read" is a promise the
agent can break the moment it decides that would help; this turns the promise into a refusal.

```bash
tools/prove-write-scope-gate.py    # prove the fence still refuses AND still allows
```

It is a `PreToolUse` gate, wired from a role's own frontmatter rather than from a settings file. Given no
arguments the role may write **nothing** (planner, reviewer, debugger); given folders it may write only under
those and nowhere else (the data author gets the game-data folder, the doc keeper gets docs and the wiki). The
scratchpad is always allowed, so a measurement can still have a throwaway script.

It asks `tool_writes.py` which files a call writes, so an edit made with a shell command counts exactly like one
made with the editing tools. Editor-mutating command lines are refused by name as well, because changing the
running editor never names a file and so is invisible to that answer.

The prover matters as much as the gate: a fence that silently stops refusing looks identical to one with nothing
to refuse, and the first version of this gate refused *everything* — including the writes its roles exist to
make — because it worked out the repo's location one folder short. Both halves are checked, 17 cases.

How the session-start checker REPORTS on these gates is proven next door, in `hook-analytics/prove-guard-check.py`, not here: a gate on a role stamps nothing until somebody spawns
that role, so "never reported running" is its ordinary state and must never be raised as a fault.

## ui-tokens.py · ui-geometry.py · check-ui-prototypes.py · prove-ui-prototype-check.py

Keep the mock pages in `prototypes/` and `mockups/` honest about the game's real look.

```bash
tools/ui-tokens.py                 # read the game's colours + fonts → prototypes/ui-tokens.css (+ .json)
tools/ui-geometry.py               # measure a real screen in the running editor → prototypes/ui-geometry.json
tools/check-ui-prototypes.py       # check every mock page; exit 1 if anything is flagged
tools/check-ui-prototypes.py path/to/page.html    # or just these pages
tools/prove-ui-prototype-check.py  # prove the checker still catches what it claims to
```

`ui-geometry.py` is the only one that needs the Unity editor open. Colours can be read from the source; sizes
cannot — a column's width is what the layout works out at the window's real size. So it builds the screen offline
in a throwaway scene, at the window's true size, and writes down what every part measures. A screen appears there
only once someone writes its recipe (a short piece of C# that builds it); today that is the Inventory.

`ui-tokens.py` never writes to the game — it only reads. A mock page links the generated stylesheet and uses
`var(--gold)` / `var(--rarity-rare-text)` / `var(--font-ui)` instead of typing a colour, so the palette cannot
drift out of the code.

`check-ui-prototypes.py` opens each page in a real browser and reads the colour and font of every element
**painted inside a marked region** — what the screen shows, not what the file says. A page marks the element that
IS the window with `data-mocks="Inventory"`, and marks each separately-drawn piece with its own name
(`data-mocks="Item Tooltip"` on the tooltip), because those are different screens in the game with different
colours. Everything outside a marked region is ignored, so a page can carry a title or an explanation for the
reader without any of it being compared.

Each painted colour lands in one of: from the shared palette, declared by the screen this region names, **real
but belonging to a different screen**, or existing nowhere in the game. The last two are flagged; the third is
the one a project-wide check can never find. A marked region containing nothing is flagged too, never called
clean — a right-click menu that only appears when you right-click paints nothing at rest, and an empty report
from a tool that looked at nothing reads exactly like a clean one. Fonts are checked the same way: the game has
Cinzel, Inter and PixelFont. Needs Chrome; without one it falls back to reading the page's text, furniture
included, and says so.

It also compares **sizes**, for any screen `ui-geometry.py` has measured: the window's width, each column's
width, row height and every text size, reported as "the mock is 33.7px, the game is 22px". A mock whose layout
demands more room than the real window has is called out by name — the bag's mock needed 710px against a 488px
window. Label the mock's parts with `data-part="header"`, `"row"`, `"search"`, `"list"`; column names come from
the headings themselves, so a column the game does not have shows up on its own.

`prove-ui-prototype-check.py` is what keeps the checker honest: ten pages with known answers, including a clean
one, a bad colour placed outside a marked region that must be ignored, an empty region that must be flagged, and
a mock built to the game's own measurements that must report no size differences at all. Re-run it after **any**
edit to the checker — a check that has gone deaf prints exactly what a clean page prints.

Full sequence: [Prototyping a UI Before Building It](../docs/0-process/prototyping-ui-workflow.md).

## server-log-tally.py · check-server-log.py · server_log_lines.py

Read the game server's log (`logs/server.log`) after a play-test.

```bash
tools/server-log-tally.py                        # per unit: blows, skills, damage, states, passives, heals, experience
tools/server-log-tally.py --map lv_1_3 --since 23:27:00
tools/check-server-log.py                        # flags impossible events; exit 1 flagged, 3 no hit lines, 4 old-build log
tools/check-server-log.py logs/server-prev.log   # the run before
```

The tally names every unit by map and spawn_id, so two snakes are counted apart, and splits damage by skill. The
checker flags a kill that paid nobody, a player's kill with no loot line, a blow that hurt somebody whose invincibility
was on, a unit named without a spawn_id, and a spawn_id handed out twice. Both need the `hit` category switched on
while playing, and both say so when the log has no hit lines. `server_log_lines.py` holds the line shapes both read,
so a line whose wording changes in the game is changed there once.
Line shapes and everything else: [Reading the Game Log](../docs/0-process/reading-the-game-log.md).

## extract_chatgpt_share.py

Reads a **ChatGPT share link** (`chatgpt.com/share/...`) and prints the full conversation (every user + assistant turn) as Markdown.

Why it's needed: share pages are JavaScript-rendered, so a normal fetch / the WebFetch tool only sees the empty app shell. The conversation is embedded as hydration data (a React-Router turbo-stream / Next.js flight payload using an index-deduplicated value pool). This script decodes that and reconstructs the turns. No login or API key required — share links are public.

### Usage

```bash
# print to stdout
python tools/extract_chatgpt_share.py "https://chatgpt.com/share/<id>"

# save to a file
python tools/extract_chatgpt_share.py "https://chatgpt.com/share/<id>" --out convo.md

# also accepts a bare share id, or a path to a saved .html page
python tools/extract_chatgpt_share.py <share-id>
python tools/extract_chatgpt_share.py saved-page.html
```

Fetches via `curl` (best at getting past Cloudflare) with a `urllib` fallback. If both are blocked, open the link in a browser, **Save Page As… HTML**, and pass the file path instead.