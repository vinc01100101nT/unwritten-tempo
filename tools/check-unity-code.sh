#!/usr/bin/env bash
# Check the game's C# for compile errors WITHOUT opening Unity and WITHOUT forcing a refresh.
#
# WHY THIS EXISTS: the project rule forbids force-refreshing Unity (each refresh blocks ~60s and often
# times out), so an AI session used to edit C# and simply hope it compiled. Unity ships its own C# compiler
# inside the app bundle, and every reference library the game needs is already on disk, so we can run the
# exact same check ourselves in ~10 seconds. Errors here are real errors; a clean run means Unity will
# compile it too.
#
# USAGE:   tools/check-unity-code.sh          # check both assemblies
#          tools/check-unity-code.sh runtime  # game scripts only (faster)
#          tools/check-unity-code.sh editor   # editor tools only
#
# EXIT CODE: 0 = clean, 1 = compile errors (they are printed), 2 = setup problem (Unity/libs not found).

set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_PROJ="$REPO/unity"
WHICH="${1:-all}"

fail() { printf '\n\033[31m%s\033[0m\n' "$*" >&2; exit 2; }

# --- locate the Unity install that matches this project -------------------------------------------------
VER_FILE="$UNITY_PROJ/ProjectSettings/ProjectVersion.txt"
[ -f "$VER_FILE" ] || fail "No $VER_FILE — is this the right repo?"
VER="$(awk '/^m_EditorVersion:/{print $2}' "$VER_FILE")"
SCRIPTING="/Applications/Unity/Hub/Editor/$VER/Unity.app/Contents/Resources/Scripting"

if [ ! -d "$SCRIPTING" ]; then
  # Project's exact version isn't installed — fall back to the newest one that is, and say so loudly.
  ALT="$(ls -d /Applications/Unity/Hub/Editor/*/Unity.app/Contents/Resources/Scripting 2>/dev/null | sort -V | tail -1)"
  [ -n "$ALT" ] || fail "Unity $VER not installed, and no other Unity found under /Applications/Unity/Hub/Editor/."
  printf '\033[33mWARNING: Unity %s not installed; checking against %s instead.\033[0m\n' \
         "$VER" "$(echo "$ALT" | awk -F/ '{print $6}')"
  SCRIPTING="$ALT"
fi

DOTNET="$SCRIPTING/NetCoreRuntime/dotnet"
CSC="$SCRIPTING/DotNetSdkRoslyn/csc.dll"
NETSTD="$SCRIPTING/NetStandard/ref/2.1.0/netstandard.dll"
[ -x "$DOTNET" ] || fail "Missing Unity's dotnet runner at $DOTNET"
[ -f "$CSC" ]    || fail "Missing Unity's C# compiler at $CSC"
[ -f "$NETSTD" ] || fail "Missing netstandard reference at $NETSTD"

# Package libraries Unity already built. Needs Unity to have compiled at least once (normal for a live project).
PKG_DIR="$UNITY_PROJ/Library/ScriptAssemblies"
[ -d "$PKG_DIR" ] || fail "No $PKG_DIR — open the project in Unity once so it builds its package libraries, then re-run."

OUT="$(mktemp -d)"
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

COMMON_FLAGS=$'/target:library\n/nostdlib+\n/langversion:9.0\n/unsafe+\n/define:UNITY_2023_1_OR_NEWER;UNITY_6000_0_OR_NEWER;UNITY_EDITOR;UNITY_STANDALONE_OSX;UNITY_STANDALONE'

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

[ "$STATUS" -eq 0 ] && printf '\nNo compile errors. Unity will build this.\n'
exit "$STATUS"
