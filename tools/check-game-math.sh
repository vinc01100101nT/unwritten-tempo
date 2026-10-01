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
. "$REPO/tools/platform.sh"
# Every path below ends up inside a file Unity's compiler reads, so each is written the way THIS OS's programs read it.
REPO="$(native_path "$REPO")"
TOOLS="$REPO/tools"
UNITY_PROJ="$REPO/unity"
TESTS="$TOOLS/game-math-tests"

fail() { printf '\033[31m%s\033[0m\n' "$1" >&2; exit 2; }

[ -d "$TESTS" ] || fail "No tests at $TESTS"

VER_FILE="$UNITY_PROJ/ProjectSettings/ProjectVersion.txt"
[ -f "$VER_FILE" ] || fail "No $VER_FILE — is this the right repo?"
# The project's exact version, or the newest installed one with a loud warning.
SCRIPTING="$(unity_data)" || exit 2

DOTNET="$SCRIPTING/NetCoreRuntime/dotnet$EXE"
CSC="$SCRIPTING/DotNetSdkRoslyn/csc.dll"
NETSTD="$SCRIPTING/NetStandard/ref/2.1.0/netstandard.dll"
# Unity's own runner for a compiled .dll. The Windows install ships without it, so there the same .NET runtime that
# runs Unity's compiler runs the checks instead (see step 3).
RUNNER="$SCRIPTING/netcorerun/netcorerun$EXE"
PKG_DIR="$UNITY_PROJ/Library/ScriptAssemblies"
[ -x "$DOTNET" ] || fail "Missing $DOTNET"
if [ ! -x "$RUNNER" ]; then
  NETCORE_VER="$(ls "$SCRIPTING/NetCoreRuntime/shared/Microsoft.NETCore.App" 2>/dev/null | sort -V | tail -1)"
  [ -n "$NETCORE_VER" ] || fail "Missing $RUNNER, and no .NET runtime beside Unity's compiler to run the checks with"
fi
[ -f "$CSC" ] || fail "Missing Unity's C# compiler at $CSC"
[ -f "$NETSTD" ] || fail "Missing netstandard reference at $NETSTD"
[ -d "$PKG_DIR" ] || fail "No $PKG_DIR — open the project in Unity once, then re-run."

OUT="$(native_path "$(mktemp -d)")"
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
if [ -x "$RUNNER" ]; then
  "$RUNNER" "$OUT/tests.dll"
else
  printf '{"runtimeOptions":{"tfm":"net%s","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}\n' \
         "${NETCORE_VER%.*}" "$NETCORE_VER" > "$OUT/tests.runtimeconfig.json"
  "$DOTNET" "$OUT/tests.dll"
fi
