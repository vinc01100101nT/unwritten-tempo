# hook-analytics — the read-first gate

A `PreToolUse` hook that **blocks an edit until the rulebook for that kind of file has been read in the current
session**, and logs every gate decision so we can visualize how often the hook actually had to intervene.

Three rulebooks, one gate (the `GATES` table in `perf_gate.py`; the file keeps its original name):

| Editing… | Must have read |
| --- | --- |
| runtime C# under `unity/Assets/Scripts` (not `Editor/`) | `unity/Assets/Scripts/PERFORMANCE.md` |
| a texture/model/audio `.meta`, a `.prefab`, a project or pipeline `.asset`, `Packages/manifest.json` | `unity/Assets/ASSETS.md` |
| `accounts/src`, `accounts/sql`, `charserver/` | `SERVER.md` (repo root) |

The second and third were added on 2026-09-20, after an audit found that half its findings broke rules that
were already written down and never injected. `prove-read-first-gate.py` feeds the gate fake edits for each
class and demands: refused unread, allowed read, untouched otherwise.

## Pieces

- **`perf_gate.py`** — the hook. Claude Code pipes it the hook payload on stdin
  (`session_id`, `tool_input.file_path`, `transcript_path`, …). It:
  1. Allows silently unless the edit targets a file one of the three rulebooks governs.
  2. Greps the session `transcript_path` for a sentinel phrase from inside that rulebook.
     Present → the doc was genuinely read this session → **allow**.
     Absent → **deny** with a "read it first" reason.
  3. Appends one JSON line to `hook-analytics.jsonl` either way.
- **`prove-read-first-gate.py`** — the proof that the gate still refuses and still allows, per class.
- **`hook-analytics.jsonl`** — append-only log. One line per gated edit:
  `{ts, session_id, file, outcome: "blocked"|"passed", doc}`.
- **`dashboard.html`** — open in a browser. Shows forced-vs-passed counts, forced-rate,
  a per-week bar chart, a "sessions since last miss" streak, and recent decisions.

## Why a content sentinel (not the filename)

When the gate denies, its reason text ("read PERFORMANCE.md") lands in the transcript.
A naive grep for `PERFORMANCE.md` would then match the gate's *own* message on retry and
wave an unread edit through. So the gate greps for a phrase that only exists in the doc's
**body** — it enters the transcript only via a real Read.
That phrase is DERIVED at run time (the longest prose line in the rulebook — never a line inside a code
fence — that does not also appear in the `CLAUDE.md` the harness injects on its own), so editing a rulebook
needs no follow-up here — there is no constant to keep in sync. It was a
hardcoded string once, which meant reading the gate's own source poisoned the transcript and
disabled the gate for the rest of the session; deriving it is what fixed that. Either way it
fails *safe* — the gate over-blocks; it never lets an unread edit through.

## Viewing the dashboard

Browsers block `file://` fetches, so either:
- `cd tools/hook-analytics && python -m http.server` then open the served `dashboard.html`, or
- open `dashboard.html` directly and use the file-picker to load `hook-analytics.jsonl`.

## Activating / changing the hook

Configured in `.claude/settings.json` — the COMMITTED project settings, not the gitignored
machine-local file, so it applies to anyone who opens this repo. It sits under `PreToolUse`
with the `Edit|Write` matcher, alongside the `Stop` entry that runs `tools/check-before-done.py`
(the compile + skill-data check that fires when a turn tries to end). After first creating a
settings file you must open `/hooks` once (or restart Claude Code) for the settings watcher to
pick it up. Review or disable either hook anytime via `/hooks`.

## Starting a hook on any OS — `tools/hooks/run-hook.sh`

No hook command names an interpreter. `python` exists on Windows and not on macOS; `python3` is
the reverse — so any name written into the settings file is right on one machine and fatal on
the other, which is exactly how the gate died. Every hook is therefore configured as

```
sh "$CLAUDE_PROJECT_DIR/tools/hooks/run-hook.sh" <repo-relative hook script>
```

and the launcher picks the first of `python3`, `python`, `py` that exists. Claude Code passes a
hook command to `sh` on macOS/Linux and to Git Bash on Windows, so the same line runs on both;
the launcher takes the repo root from `CLAUDE_PROJECT_DIR` and normalises Windows backslashes,
and says so loudly rather than dying quietly if there is no Python at all. **Adding a hook: use
the launcher.** A settings entry that names an interpreter is a bug.

## The guard on the guards — `guard_check.py`

The gate above was configured to start with `python`, which does not exist on macOS. It
therefore failed to start on every edit from the day this project moved machines, and a hook
that fails to start fails **silently** — the edit proceeds exactly as if the gate had allowed
it. It guarded nothing for five weeks (last log entry: 24 Jul, Windows paths) and nothing in
any session could notice.

`guard_check.py` runs at **SessionStart** and asks two different questions about every hook
configured on this machine — the user's global settings included, not just this repo's:

1. **Can it start?** The program exists on this machine, the script it names still exists, the
   settings file still parses. Inline shell commands name no single program, so those are only
   checked for the files they reference.
2. **Has it actually run?** Every hook of ours calls `guard_check.beat()` at the *top* of its
   run, before any early-out, writing to the gitignored `hook-heartbeat.json`. A hook that is
   configured, runnable and never firing — a matcher that matches nothing, a settings file the
   app never loaded — shows up as one nobody has seen in `UNSEEN_DAYS` (14) days.

Runnable is not the same as running; either question alone misses a whole class of dead guard.
Healthy machine → one line (`guards: N configured, all able to run and reporting in`). A fault
→ it lands in the session's context with an instruction to tell the user in the first reply.

`prove-guard-check.py` keeps it honest: seven planted faults it must catch, two healthy setups
it must stay quiet about. The `Stop` gate re-runs that proof automatically whenever
`guard_check.py` is edited, so the checker can never quietly go blind.

## Scope

Single rule for now: `PERFORMANCE.md` ⇄ runtime C#. To generalize later, replace the
single `SENTINEL`/path check with a doc→glob table.
