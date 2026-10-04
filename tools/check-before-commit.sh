#!/bin/sh
# Run every game check over the WHOLE project before a commit is made, and refuse the commit while one fails.
#
# WHY THIS EXISTS: the end-of-turn guard runs only the checks whose files an AI session edited, and only inside
# that session. A change to one file that breaks a different one, or any change made by hand, reached a commit
# with nothing checked at all — which is how a fixed bug comes back without anyone noticing which change did it.
# This is the one gate every commit passes through, whoever made it.
#
# WHAT IT READS: the project as it stands on disk, not only what is staged. The checks compile and load the whole
# project, so unfinished work that is not part of this commit can still stop it.
#
# LEFT OUT ON PURPOSE: the wiki-freshness report and the mock-page checker. Both judge documents and prototype
# pages rather than whether the game works, and a commit of game code should not wait on a doc.
#
# The self-tests that prove a checker still CATCHES faults run only when that checker is part of the commit —
# they take most of a minute between them and have nothing to prove when the checker did not change.
#
# A check that cannot run at all (Unity not installed, no Python) is reported and does NOT stop the commit: a
# missing tool is not a broken game.
#
# USAGE:  tools/check-before-commit.sh      (run by .git/hooks/pre-commit; fine to run by hand)
# EXIT:   0 = every check passed or could not run, 1 = a check failed.

ROOT=$(git rev-parse --show-toplevel 2>/dev/null) || ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$ROOT" || exit 1

PY=""
for candidate in python3 python py; do
  if command -v "$candidate" >/dev/null 2>&1; then PY=$candidate; break; fi
done

OUT=$(mktemp -d)
trap 'rm -rf "$OUT"' EXIT
FAILS=0
STAGED=$(git diff --cached --name-only 2>/dev/null)
START=$(date +%s)

check() {
  name=$1; shift
  printf '  %-46s' "$name"
  "$@" > "$OUT/log" 2>&1
  code=$?
  case $code in
    0) printf 'passed\n' ;;
    1) printf '\033[31mFAILED\033[0m\n'
       sed 's/^/      /' "$OUT/log" | tail -60
       FAILS=$((FAILS + 1)) ;;
    *) printf '\033[33mcould not run (exit %s) — not blocking\033[0m\n' "$code"
       tail -3 "$OUT/log" | sed 's/^/      /' ;;
  esac
}

py() {
  [ -n "$PY" ] || { echo "no Python on this machine (tried python3, python, py)"; return 127; }
  "$PY" "$@"
}

in_commit() {
  printf '%s\n' "$STAGED" | grep -qE "$1"
}

echo "Checking the whole project before this commit:"
check "the game's code compiles"                  bash tools/check-unity-code.sh
check "the game's numbers are what they were"      sh tools/check-game-math.sh
check "every skill's saved data holds together"    py tools/check-skill-data.py
check "every asset reference points at a file"     py tools/check-asset-wiring.py
check "per-frame code follows PERFORMANCE.md"      py tools/check-perf-hotpaths.py
check "every asset follows ASSETS.md"             py tools/check-asset-rules.py
check "every biome's look is whole"               py tools/check-biome-table.py
check "every map id agrees with the world layout"  py tools/check-map-ids.py
check "every quest can be finished"               py tools/check-quest-data.py

if in_commit '^tools/(check-game-math\.sh|prove-game-math-check\.py|game-math-tests/)'; then
  check "the numbers check still catches a change"   py tools/prove-game-math-check.py
fi
if in_commit '^tools/(check-skill-data|prove-skill-data-check)\.py$'; then
  check "the skill-data check still catches faults"  py tools/prove-skill-data-check.py
fi
if in_commit '^tools/(check-asset-wiring|prove-asset-wiring-check|unity_assets)\.py$'; then
  check "the asset check still catches a break"      py tools/prove-asset-wiring-check.py
fi
if in_commit '^tools/(check-asset-rules|prove-asset-rules-check)\.py$'; then
  check "the asset-rules check still catches faults" py tools/prove-asset-rules-check.py
fi
if in_commit '^tools/(prove-write-scope-gate\.py|hook-analytics/(write_scope_gate|tool_writes)\.py)$'; then
  check "the agent write fence still holds"          py tools/prove-write-scope-gate.py
fi
if in_commit '^tools/(check-biome-table|prove-biome-table-check)\.py$'; then
  check "the biome check still catches faults"      py tools/prove-biome-table-check.py
fi
if in_commit '^tools/(check-map-ids|prove-map-ids-check)\.py$'; then
  check "the map id check still catches faults"     py tools/prove-map-ids-check.py
fi
if in_commit '^tools/(check-quest-data|prove-quest-data-check)\.py$'; then
  check "the quest data check still catches faults"  py tools/prove-quest-data-check.py
fi
if in_commit '^tools/hook-analytics/(guard_check|prove-guard-check)\.py$'; then
  check "the guard check still sees a dead guard"    py tools/hook-analytics/prove-guard-check.py
fi
if in_commit '^tools/hook-analytics/(repeat_watch|prove_repeat_watch)\.py$'; then
  check "the repeat watcher still tells run from mention" py tools/hook-analytics/prove_repeat_watch.py
fi

echo "  ($(( $(date +%s) - START ))s)"
if [ "$FAILS" -gt 0 ]; then
  printf '\n\033[31mThe commit was stopped: %s check(s) failed.\033[0m Fix what is listed above and commit again.\n' "$FAILS"
  printf 'To commit anyway, knowing it is broken: git commit --no-verify\n'
  exit 1
fi
exit 0
