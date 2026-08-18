# hook-analytics — PERFORMANCE.md read gate

A `PreToolUse` hook that **blocks edits to runtime C# until `PERFORMANCE.md` has been
read in the current session**, and logs every gate decision so we can visualize how
often the hook actually had to intervene.

## Pieces

- **`perf_gate.py`** — the hook. Claude Code pipes it the hook payload on stdin
  (`session_id`, `tool_input.file_path`, `transcript_path`, …). It:
  1. Allows silently unless the edit targets `unity/Assets/Scripts/**/*.cs`
     (excluding `Editor/` — that's not a runtime hot path).
  2. Greps the session `transcript_path` for a sentinel phrase from inside
     `PERFORMANCE.md`. Present → the doc was genuinely read this session → **allow**.
     Absent → **deny** with a "read it first" reason.
  3. Appends one JSON line to `hook-analytics.jsonl` either way.
- **`hook-analytics.jsonl`** — append-only log. One line per gated edit:
  `{ts, session_id, file, outcome: "blocked"|"passed", doc}`.
- **`dashboard.html`** — open in a browser. Shows forced-vs-passed counts, forced-rate,
  a per-week bar chart, a "sessions since last miss" streak, and recent decisions.

## Why a content sentinel (not the filename)

When the gate denies, its reason text ("read PERFORMANCE.md") lands in the transcript.
A naive grep for `PERFORMANCE.md` would then match the gate's *own* message on retry and
wave an unread edit through. So the gate greps for a phrase that only exists in the doc's
**body** — it enters the transcript only via a real Read.
That phrase is DERIVED at run time (the longest prose line in `PERFORMANCE.md` that does not
also appear in the sibling `CLAUDE.md`, which the harness injects on its own), so editing
`PERFORMANCE.md` needs no follow-up here — there is no constant to keep in sync. It was a
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

## Scope

Single rule for now: `PERFORMANCE.md` ⇄ runtime C#. To generalize later, replace the
single `SENTINEL`/path check with a doc→glob table.
