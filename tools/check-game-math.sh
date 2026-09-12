#!/bin/sh
# Check the game's ARITHMETIC — without opening Unity, without a test framework, in about a second.
#
# WHY THIS EXISTS: a wrong number is SILENT. A hit that does 47 instead of 52 looks exactly like a hit, and
# nothing on screen says the experience curve moved. Today the only thing that finds a broken number is playing
# the game and eventually noticing, which is weeks later and gives no clue which change did it.
#
# HOW IT WORKS: the game's own scripts compile to a normal .NET library, and Unity ships a runner that executes
# one outside the editor. Pure arithmetic runs; anything reaching into the live engine throws loudly rather than
# quietly returning a wrong answer, which is exactly the boundary we want — the sums are testable and everything
# else refuses to pretend.
#
# WHAT IT NEVER DOES: it never opens the game and never judges how anything looks or feels. A clean run means the
# numbers are what they were. It never means the game is playable, and it never replaces play-testing.
#
# USAGE:  tools/check-game-math.sh
# EXIT:   0 = every number is as expected, 1 = a number moved, 2 = setup problem.
set -e

TOOLS="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
REPO="$(dirname "$TOOLS")"
UNITY_PROJ="$REPO/unity"
TESTS="$TOOLS/game-math-tests"

fail() { printf '\033[31m%s\033[0m\n' "$1" >&2; exit 2; }

[ -d "$TESTS" ] || fail "No tests at $TESTS"

VER_FILE="$UNITY_PROJ/ProjectSettings/ProjectVersion.txt"
[ -f "$VER_FILE" ] || fail "No $VER_FILE — is this the right repo?"
VER="$(awk '/^m_EditorVersion:/{print $2}' "$VER_FILE")"
SCRIPTING="/Applications/Unity/Hub/Editor/$VER/Unity.app/Contents/Resources/Scripting"
if [ ! -d "$SCRIPTING" ]; then
  ALT="$(ls -d /Applications/Unity/Hub/Editor/*/Unity.app/Contents/Resources/Scripting 2>/dev/null | sort -V | tail -1)"
  [ -n "$ALT" ] || fail "Unity $VER not installed, and no other Unity found."
  printf '\033[33mWARNING: Unity %s not installed; checking against %s instead.\033[0m\n' \
         "$VER" "$(echo "$ALT" | awk -F/ '{print $6}')"
  SCRIPTING="$ALT"
fi

DOTNET="$SCRIPTING/NetCoreRuntime/dotnet"
CSC="$SCRIPTING/DotNetSdkRoslyn/csc.dll"
NETSTD="$SCRIPTING/NetStandard/ref/2.1.0/netstandard.dll"
RUNNER="$SCRIPTING/netcorerun/netcorerun"
PKG_DIR="$UNITY_PROJ/Library/ScriptAssemblies"
for f in "$DOTNET" "$RUNNER"; do [ -x "$f" ] || fail "Missing $f"; done
[ -f "$CSC" ] || fail "Missing Unity's C# compiler at $CSC"
[ -f "$NETSTD" ] || fail "Missing netstandard reference at $NETSTD"
[ -d "$PKG_DIR" ] || fail "No $PKG_DIR — open the project in Unity once, then re-run."

OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

# The same reference list and defines the compile checker uses, so what is tested is what Unity builds.
REFS="$OUT/refs.txt"
: > "$REFS"
for f in "$SCRIPTING"/Managed/UnityEngine/*.dll; do [ -e "$f" ] && echo "/r:$f" >> "$REFS"; done
echo "/r:$NETSTD" >> "$REFS"
for f in "$SCRIPTING"/NetStandard/compat/2.1.0/shims/netstandard/*.dll \
         "$SCRIPTING"/NetStandard/compat/2.1.0/shims/netfx/*.dll; do
  [ -e "$f" ] && echo "/r:$f" >> "$REFS"
done
for f in "$PKG_DIR"/*.dll; do
  case "$(basename "$f")" in Assembly-CSharp*.dll|*Editor*.dll) continue ;; esac
  echo "/r:$f" >> "$REFS"
done

DEFS='UNITY_2023_1_OR_NEWER;UNITY_6000_0_OR_NEWER;UNITY_EDITOR;UNITY_STANDALONE_OSX;UNITY_STANDALONE;ENABLE_INPUT_SYSTEM;ENABLE_LEGACY_INPUT_MANAGER'
FLAGS=$'/nostdlib+\n/langversion:9.0\n/unsafe+\n/define:'"$DEFS"

# 1. the game itself, as a library
{ printf '%s\n' "$FLAGS"; echo '/target:library'; echo "/out:$OUT/game.dll"; cat "$REFS"
  find "$UNITY_PROJ/Assets/Scripts" -name '*.cs' -not -path '*/Editor/*'; } > "$OUT/game.rsp"
if ! "$DOTNET" "$CSC" "@$OUT/game.rsp" > "$OUT/game.log" 2>&1; then
  printf '\033[31mThe game does not compile, so its numbers cannot be checked:\033[0m\n'
  grep ': error ' "$OUT/game.log" | sed 's|^|  |' | head -20
  exit 2
fi

# 2. the checks, against it
{ printf '%s\n' "$FLAGS"; echo '/target:exe'; echo "/out:$OUT/tests.dll"; cat "$REFS"
  echo "/r:$OUT/game.dll"
  find "$TESTS" -name '*.cs'; } > "$OUT/tests.rsp"
if ! "$DOTNET" "$CSC" "@$OUT/tests.rsp" > "$OUT/tests.log" 2>&1; then
  printf '\033[31mThe checks themselves do not compile:\033[0m\n'
  grep ': error ' "$OUT/tests.log" | sed 's|^|  |' | head -20
  exit 2
fi

# 3. run them, with the engine libraries beside the output so the runner can load them
cp "$SCRIPTING"/Managed/UnityEngine/*.dll "$OUT"/ 2>/dev/null || true
cp "$PKG_DIR"/*.dll "$OUT"/ 2>/dev/null || true
"$RUNNER" "$OUT/tests.dll"
