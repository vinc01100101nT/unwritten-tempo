#!/usr/bin/env bash
# Check the game's C# for compile errors WITHOUT opening Unity and WITHOUT forcing a refresh.
#
# WHY THIS EXISTS: the project rule forbids force-refreshing Unity (each refresh blocks ~60s and often
# times out), so an AI session used to edit C# and simply hope it compiled. Unity ships its own C# compiler
# inside the app bundle, and every reference library the game needs is already on disk, so we can run the
# exact same check ourselves in ~10 seconds. Errors here are real errors; a clean run means Unity will
# compile it too.
#
# USAGE:   tools/check-unity-code.sh          # every pass below
#          tools/check-unity-code.sh runtime  # game scripts only (faster)
#          tools/check-unity-code.sh editor   # editor tools only
#          tools/check-unity-code.sh android  # game scripts as the PHONE build sees them
#
# WHY THE ANDROID PASS: code inside `#if UNITY_ANDROID` is invisible to the desktop pass — the compiler never
# reads it — so the whole touch layer could be broken and every other pass would still print "clean". This pass
# compiles the same sources with the phone's symbols (and WITHOUT UNITY_EDITOR, which a real device build also
# lacks), so the phone-only half is held to the same standard as everything else.
#
# EXIT CODE: 0 = clean, 1 = compile errors (they are printed), 2 = setup problem (Unity/libs not found).

set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$REPO/tools/platform.sh"
# Every path below ends up inside a file Unity's compiler reads, so each is written the way THIS OS's programs read it.
REPO="$(native_path "$REPO")"
UNITY_PROJ="$REPO/unity"
WHICH="${1:-all}"

fail() { printf '\n\033[31m%s\033[0m\n' "$*" >&2; exit 2; }

# --- locate the Unity install that matches this project -------------------------------------------------
VER_FILE="$UNITY_PROJ/ProjectSettings/ProjectVersion.txt"
[ -f "$VER_FILE" ] || fail "No $VER_FILE — is this the right repo?"
# The project's exact version, or the newest installed one with a loud warning.
SCRIPTING="$(unity_data)" || exit 2

DOTNET="$SCRIPTING/NetCoreRuntime/dotnet$EXE"
CSC="$SCRIPTING/DotNetSdkRoslyn/csc.dll"
NETSTD="$SCRIPTING/NetStandard/ref/2.1.0/netstandard.dll"
[ -x "$DOTNET" ] || fail "Missing Unity's dotnet runner at $DOTNET"
[ -f "$CSC" ]    || fail "Missing Unity's C# compiler at $CSC"
[ -f "$NETSTD" ] || fail "Missing netstandard reference at $NETSTD"

# Package libraries Unity already built. Needs Unity to have compiled at least once (normal for a live project).
PKG_DIR="$UNITY_PROJ/Library/ScriptAssemblies"
[ -d "$PKG_DIR" ] || fail "No $PKG_DIR — open the project in Unity once so it builds its package libraries, then re-run."

OUT="$(native_path "$(mktemp -d)")"
trap 'rm -rf "$OUT"' EXIT

# --- shared reference list ------------------------------------------------------------------------------
# UnityEngine + UnityEditor libraries, the .NET base library, and every package library EXCEPT the game's
# own (we are rebuilding those) and the editor-only ones (added separately, for the editor pass only).
REFS="$OUT/refs.txt"
: > "$REFS"
for f in "$SCRIPTING"/Managed/UnityEngine/*.dll; do [ -e "$f" ] && echo "/r:$f" >> "$REFS"; done
echo "/r:$NETSTD" >> "$REFS"
# Compatibility stand-ins (mscorlib, System.*). Without these, code using List<>/Func<>/Enum reports a
# "type is defined in an assembly that is not referenced" error that is NOT a real fault in the code.
for f in "$SCRIPTING"/NetStandard/compat/2.1.0/shims/netstandard/*.dll \
         "$SCRIPTING"/NetStandard/compat/2.1.0/shims/netfx/*.dll; do
  [ -e "$f" ] && echo "/r:$f" >> "$REFS"
done
for f in "$PKG_DIR"/*.dll; do
  case "$(basename "$f")" in Assembly-CSharp*.dll|*Editor*.dll) continue ;; esac
  echo "/r:$f" >> "$REFS"
done

# Input symbols: the project's Active Input Handling is "Both", so BOTH of these are defined in the real build.
# Without them here, every `#if ENABLE_INPUT_SYSTEM` branch in the game silently goes unchecked.
INPUT_DEFS='ENABLE_INPUT_SYSTEM;ENABLE_LEGACY_INPUT_MANAGER'
BASE_FLAGS=$'/target:library\n/nostdlib+\n/langversion:9.0\n/unsafe+'
COMMON_FLAGS="$BASE_FLAGS"$'\n'"/define:UNITY_2023_1_OR_NEWER;UNITY_6000_0_OR_NEWER;UNITY_EDITOR;UNITY_STANDALONE_OSX;UNITY_STANDALONE;$INPUT_DEFS"
ANDROID_FLAGS="$BASE_FLAGS"$'\n'"/define:UNITY_2023_1_OR_NEWER;UNITY_6000_0_OR_NEWER;UNITY_ANDROID;$INPUT_DEFS"

STATUS=0

# run_pass <label> <response-file>
run_pass() {
  local label="$1" rsp="$2" log="$OUT/$1.log"
  "$DOTNET" "$CSC" "@$rsp" > "$log" 2>&1
  local errs
  errs=$(grep -c ': error ' "$log")
  if [ "$errs" -gt 0 ]; then
    printf '\033[31m%-8s %s error(s)\033[0m\n' "$label" "$errs"
    grep ': error ' "$log" | sed 's|^|  |' | head -40
    STATUS=1
  else
    printf '\033[32m%-8s clean\033[0m (%s warnings)\n' "$label" "$(grep -c ': warning ' "$log")"
  fi
}

# --- pass 1: the game's own scripts ----------------------------------------------------------------------
# Built even when only checking the editor pass, because the editor tools reference these types.
RT_RSP="$OUT/runtime.rsp"
{ printf '%s\n' "$COMMON_FLAGS"; echo "/out:$OUT/game.dll"; cat "$REFS"
  find "$UNITY_PROJ/Assets/Scripts" -name '*.cs' -not -path '*/Editor/*'; } > "$RT_RSP"

if [ "$WHICH" = "all" ] || [ "$WHICH" = "runtime" ]; then
  run_pass "runtime" "$RT_RSP"
elif [ "$WHICH" = "editor" ]; then
  "$DOTNET" "$CSC" "@$RT_RSP" > "$OUT/prep.log" 2>&1   # silent prerequisite build
fi

# --- pass 2: the editor tools ----------------------------------------------------------------------------
if [ "$WHICH" = "all" ] || [ "$WHICH" = "editor" ]; then
  if [ ! -f "$OUT/game.dll" ]; then
    printf '\033[33m%-8s skipped — game scripts must compile first\033[0m\n' "editor"
  else
    ED_RSP="$OUT/editor.rsp"
    { printf '%s\n' "$COMMON_FLAGS"; echo "/out:$OUT/editor.dll"; cat "$REFS"
      echo "/r:$OUT/game.dll"
      for f in "$PKG_DIR"/*Editor*.dll; do [ -e "$f" ] && echo "/r:$f"; done
      find "$UNITY_PROJ/Assets/Scripts/Editor" -name '*.cs'; } > "$ED_RSP"
    run_pass "editor" "$ED_RSP"
  fi
fi

# --- pass 3: the game's scripts as the PHONE build sees them ---------------------------------------------
if [ "$WHICH" = "all" ] || [ "$WHICH" = "android" ]; then
  AND_RSP="$OUT/android.rsp"
  { printf '%s\n' "$ANDROID_FLAGS"; echo "/out:$OUT/game-android.dll"; cat "$REFS"
    find "$UNITY_PROJ/Assets/Scripts" -name '*.cs' -not -path '*/Editor/*'; } > "$AND_RSP"
  run_pass "android" "$AND_RSP"
fi

[ "$STATUS" -eq 0 ] && printf '\nNo compile errors. Unity will build this.\n'
exit "$STATUS"
