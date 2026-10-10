#!/usr/bin/env bash
# Build the game — the SERVER (no window) or the CLIENT (the one players run).
#
# WHY THIS EXISTS: "build it from the editor" is not an instruction anyone can follow. Unity 6 treats a
# dedicated server as a VARIANT of a desktop build rather than its own target, so there is no menu item to
# click, and a batch-mode build cannot run while the editor holds the project open. This drives the editor
# you already have running, which is the normal state of things here.
#
# THE ONE WAY TO BUILD THE GAME, on a Mac and on Windows alike: it builds for the computer it runs on
# (unwritten.app on a Mac, unwritten.exe on Windows — tools/platform.sh decides). There is no second build
# menu in the editor; the old Windows-only one was folded into this so the two could not drift.
#
# USAGE:  tools/build-game.sh server            # headless server for this computer
#         tools/build-game.sh client            # the game players run
#         tools/build-game.sh both
#         tools/build-game.sh server --linux    # server for a Linux host (what you deploy)
#         tools/build-game.sh client --mac --dev   # the Mac game, made on this computer, into builds/mac/
#
# --mac MAKES THE MAC GAME ON ANY COMPUTER, into builds/mac/ so it never overwrites this computer's own build. The
# editor is switched to building for the Mac for the length of the build and switched back afterwards, because the
# creatures and models are built separately and only for the platform the editor is switched to — a Mac game built
# with Windows content would start with none of them. The Mac app is then signed without a certificate
# (tools/platform.sh), which an Apple silicon Mac needs before it runs anything.
#         tools/build-game.sh server --out /some/where
#         tools/build-game.sh client --dev        # a development build, for testing on your own Wi-Fi
#
# --dev MAKES A DEVELOPMENT BUILD. The game refuses plain (unencrypted) web traffic to any machine but the one it runs
# on, so a copy that signs in to this PC from another device on the Wi-Fi is refused. The project allows plain web
# traffic in development builds only, so a --dev copy can test on your own network while every copy handed to a
# player still refuses it. Never hand a --dev copy to a player.
#
# EXIT: 0 = built, 1 = build failed, 2 = setup problem (editor not running, etc).

set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$REPO/tools/platform.sh"
# The addresses a client build carries come from settings.env, the one file every program reads.
. "$REPO/tools/load-settings.sh"
# Paths below are handed to the editor inside C#, so they are written the way THIS OS's programs read them.
REPO="$(native_path "$REPO")"
OUT_ROOT="$REPO/builds"
TARGET="$(build_target)"

fail() { printf '\n\033[31m%s\033[0m\n' "$*" >&2; exit 2; }

WHICH="${1:-}"
shift || true
BUILD_OPTIONS="None"
case "$WHICH" in
  server|client|both) ;;
  *) fail "Say what to build: server | client | both
   e.g.  tools/build-game.sh server" ;;
esac

while [ $# -gt 0 ]; do
  case "$1" in
    --linux) TARGET="StandaloneLinux64"; shift ;;
    --mac)   TARGET="StandaloneOSX"; MAC_OUT=1; shift ;;
    --out)   OUT_ROOT="${2:-}"; shift 2 ;;
    --dev)   BUILD_OPTIONS="Development"; shift ;;
    *) fail "Unknown option: $1" ;;
  esac
done

UNITY_CLI="$(unity_cli)" || fail "No Unity command-line link on this computer — the build runs through it."
[ -z "${MAC_OUT:-}" ] || [ "$OUT_ROOT" != "$REPO/builds" ] || OUT_ROOT="$REPO/builds/mac"
OUT_ROOT="$(native_path "$OUT_ROOT")"

# Every request names THIS project. On Windows the link's own list of open editors comes back empty while the
# editor answers perfectly well when asked by project (measured 2026-10-01), so the list is never consulted.
UNITY_CMD=("$UNITY_CLI" cmd --project-path "$REPO/unity")

# The editor must be open on THIS project — the build runs inside it.
# NO ANSWER DOES NOT MEAN NO EDITOR. The link inside the editor can fail to start after a script reload while the
# editor itself stays open — MEASURED 2026-10-06: "Failed to start Pipeline Server: Sharing violation" on its
# address file, and the link stayed off until the next recompile. The editor writes its own process id into that
# file, so a live Unity under that id means the editor is open and only its link is off.
if ! "${UNITY_CMD[@]}" editor_status 2>/dev/null | grep -q '"projectPath"'; then
  EDITOR_PID="$(grep -o '"pid": *[0-9]*' "$REPO/unity/Library/Pipeline/.unity-pipeline-port" 2>/dev/null | grep -o '[0-9]*$')"
  case "$(process_name "${EDITOR_PID:-0}")" in
    Unity|Unity.exe) fail "Unity is open, but its link to this command is off — it can fail to start when Unity reloads its scripts.
   In Unity, click Tools > unwritten > Debug > Recompile Scripts, wait for it to finish, then run this again." ;;
  esac
  fail "Unity is not running with this project open.
   Open $REPO/unity in Unity, wait for it to finish loading, then run this again."
fi

mkdir -p "$OUT_ROOT"

# NO BUILD EVER MEETS "Modified Scenes must be saved". The content step refuses any open scene marked modified with
# a dialog that freezes the editor until somebody clicks it (measured 2026-10-09: 7 minutes 49 seconds). So every open
# scene marked modified is checked first, by saving a COPY into Temp/ and comparing it with the file on disk:
#   - the same, line for line: the mark changes nothing, so the scene is saved (it writes the bytes already there)
#     and the build goes on;
#   - different: the build stops here, before anything is deleted, and prints the exact lines — a real unsaved edit
#     is the user's to keep or throw away, never this script's.
# What keeps marking scenes modified in the first place is caught by tools/check-scene-stays-clean.py.
scenes_unchanged_or_stop() {
  local report
  report="$("${UNITY_CMD[@]}" eval --timeout 120 --code '
    var sb = new System.Text.StringBuilder();
    System.Func<string, byte[]> bytesNoCr = p => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(System.IO.File.ReadAllBytes(p), b => b != 13));
    for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
      var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
      if (!s.isDirty) continue;
      if (string.IsNullOrEmpty(s.path)) { sb.Append("UNTITLED|" + s.name + ";"); continue; }
      var copy = "Temp/__unsaved_check_" + i + ".unity";
      UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s, copy, true);
      if (System.Linq.Enumerable.SequenceEqual(bytesNoCr(s.path), bytesNoCr(copy))) {
        System.IO.File.Delete(copy); System.IO.File.Delete(copy + ".meta");
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s);
        sb.Append("SETTLED|" + s.path + ";");
      } else sb.Append("CHANGED|" + s.path + "|" + copy + ";");
    }
    return sb.ToString();' 2>/dev/null | grep -o '"result":"[^"]*"' | cut -d'"' -f4)"
  local entry kind path copy stop=0
  IFS=';' read -ra entries <<< "$report"
  for entry in "${entries[@]}"; do
    [ -n "$entry" ] || continue
    IFS='|' read -r kind path copy <<< "$entry"
    case "$kind" in
      SETTLED) printf 'scene %s was marked modified with nothing changed in it — saved, build goes on\n' "$path" >&2 ;;
      UNTITLED) printf '\033[31mAn unsaved new scene (%s) is open. Save or close it, then run this again.\033[0m\n' "$path" >&2; stop=1 ;;
      CHANGED)
        printf '\033[31mScene %s has real unsaved changes — the build stops here so they are not lost or baked in:\033[0m\n' "$path" >&2
        diff --strip-trailing-cr "$REPO/unity/$path" "$REPO/unity/$copy" >&2
        rm -f "$REPO/unity/$copy" "$REPO/unity/$copy.meta"
        printf '   Keep them: File > Save in Unity. Throw them away: reopen the scene without saving. Then run this again.\n' >&2
        stop=1 ;;
    esac
  done
  return "$stop"
}

# build_one <server|client> -> echoes the output path
build_one() {
  local kind="$1"
  local subtarget name
  if [ "$kind" = "server" ]; then subtarget="Server"; name="unwritten-server"
  else                            subtarget="Player"; name="unwritten"; fi

  # A Windows game is a program file with its data folder beside it; every other target keeps the name it had.
  local ext=".app"; [ "$TARGET" = "StandaloneWindows64" ] && ext=".exe"
  local out="$OUT_ROOT/$kind/$name$ext"
  local reserved; reserved="$(mktemp "${TMPDIR:-/tmp}/buildgame.XXXXXX")"
  local src; src="$(native_path "$reserved").cs"
  # Written by the build itself when Unity's build call returns, holding its result. Its name is this run's own, so
  # a later build can never read an earlier one's; the sweep below only clears what a killed run left behind.
  local finished="${src%.cs}.finished"
  rm -f "${TMPDIR:-/tmp}"/buildgame.*.finished

  cat > "$src" <<EOF
// Generated by tools/build-game.sh — runs inside the open editor.
// Called DIRECTLY rather than scheduled: a domain reload silently discards anything deferred, and the
// build then never happens with nothing reporting a failure.
// THE MOBS AND CHARACTER MODELS FIRST. They are fetched on demand from content built separately from the
// player, and a build made without it starts, listens, loads its maps and then has no creatures and no
// townsfolk at all — with nothing in the log saying why. MEASURED: one rebuild produced a server holding 0
// mobs where the one before it held 33, because this content build had refused to run over an unsaved scene.
// The refusal leaves a note beside the build rather than stopping it, so the failure is loud and immediate
// instead of being a wait for something that was never going to appear.
UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.BuildPlayerContent(out var content);
if (!string.IsNullOrEmpty(content.Error))
    System.IO.File.WriteAllText("$out.no-content.txt", content.Error);

var scenes = new System.Collections.Generic.List<string>();
foreach (var s in UnityEditor.EditorBuildSettings.scenes)
    if (s.enabled) scenes.Add(s.path);
// Systems is the base layer every map loads on top of; a build that does not start in it boots into an empty map.
int sys = scenes.FindIndex(p => p.EndsWith("/Systems.unity", System.StringComparison.OrdinalIgnoreCase));
if (sys > 0) { var first = scenes[sys]; scenes.RemoveAt(sys); scenes.Insert(0, first); }

var opts = new UnityEditor.BuildPlayerOptions
{
    scenes = scenes.ToArray(),
    locationPathName = "$out",
    target = UnityEditor.BuildTarget.$TARGET,
    subtarget = (int)UnityEditor.StandaloneBuildSubtarget.$subtarget,
    options = UnityEditor.BuildOptions.$BUILD_OPTIONS
};
var r = UnityEditor.BuildPipeline.BuildPlayer(opts);
System.IO.File.WriteAllText("$finished", r.summary.result.ToString());
UnityEngine.Debug.Log("[build-game] " + r.summary.result + " errors=" + r.summary.totalErrors);
return r.summary.result + " errors=" + r.summary.totalErrors;
EOF

  printf '\033[36mBuilding %s (%s) …\033[0m this takes a few minutes.\n' "$kind" "$TARGET" >&2

  # WAIT FOR THE EDITOR TO BE FREE BEFORE ASKING. The build runs INSIDE it, on its main thread, and a request
  # sent while it is still finishing the previous one is simply dropped — the editor answers "main thread timed
  # out" and that build never starts at all. MEASURED: building both in one run, the second half produced
  # nothing while the first half's own result sat in the console alone.
  local w
  for w in $(seq 1 120); do
    "${UNITY_CMD[@]}" editor_status 2>/dev/null | grep -q '"status":"ready"' && break
    sleep 5
  done

  scenes_unchanged_or_stop || return 1

  rm -rf "$out" "$out.no-content.txt"

  # The request usually times out long before the build ends — that is expected and harmless, the build
  # keeps going inside the editor. The filesystem below is what actually decides pass or fail.
  "${UNITY_CMD[@]}" eval_file --file "$src" --timeout 900 >/dev/null 2>&1
  rm -f "$src"

  # Wait for Unity to say the build is FINISHED. The artefact appearing is not that: Unity makes the folder first
  # and writes into it for a while after. MEASURED 2026-10-07: the Mac app's folder appeared at 14:41:40 and its
  # start program and signature were written at 14:42:14 — after the publish step had already read the folder and
  # stopped, because it was missing. It is hopeless when the EDITOR HAS GONE BACK TO IDLE without finishing — a
  # fixed count of ticks is neither. MEASURED: building both in one run reported the second one as FAILED four
  # times over while it was still going, and building that same half on its own straight afterwards succeeded
  # every time. A false failure is expensive here, because the old output was already deleted above.
  local i idle=0
  for i in $(seq 1 360); do
    [ -f "$finished" ] && break
    if "${UNITY_CMD[@]}" editor_status 2>/dev/null | grep -q '"status":"ready"'; then idle=$((idle + 1)); else idle=0; fi
    [ "$idle" -ge 12 ] && break     # two solid minutes idle and never finished: it is not coming
    sleep 10
  done

  local result=""
  [ -f "$finished" ] && result="$(tr -d '\r\n' < "$finished")"
  rm -f "$finished" "$reserved"

  if [ "$result" != "Succeeded" ] || [ ! -e "$out" ]; then
    printf '\033[31m%s build FAILED — Unity reported: %s\033[0m\n' "$kind" "${result:-nothing, it never finished}" >&2
    printf '   Look for [build-game] in the editor console for the reason.\n' >&2
    return 1
  fi

  if [ -f "$out.no-content.txt" ]; then
    printf '\033[31m%s built WITHOUT its mobs and character models — it will come up empty.\033[0m\n' "$kind" >&2
    printf '   %s\n' "$(cat "$out.no-content.txt")" >&2
    printf '   Usually an unsaved scene in the editor: save everything (File > Save), then run this again.\n' >&2
    return 1
  fi

  if [ "$kind" = "client" ]; then write_addresses "$out" || return 1; fi

  # The size of everything the build made: on Windows the program file is tiny and its data sits beside it.
  local measured="$out"; [ "$ext" = ".exe" ] && measured="$(dirname "$out")"
  printf '\033[32m%s built\033[0m  %s  (%s)\n' "$kind" "$out" "$(du -sh "$measured" | cut -f1)"
  return 0
}

# THE ADDRESSES A PLAYER'S COPY DIALS, copied from settings.env into the build's own settings file — the
# addresses players are handed (PUBLIC_*) when the rented server exists, this machine's own until then. Only
# these two names are written; nothing else in settings.env ever goes into a build.
write_addresses() {
  local build="$1" file
  # Inside the .app on a Mac; in the data folder BESIDE the .exe on Windows — so search the folder holding both.
  file="$(find "$(dirname "$build")" -path '*/StreamingAssets/server.txt' | head -1)"
  [ -n "$file" ] || { printf '\033[31mThe build has no StreamingAssets/server.txt to write the addresses into.\033[0m\n' >&2; return 1; }
  local accounts="${PUBLIC_ACCOUNTS_URL:-${ACCOUNTS_URL:-}}" characters="${PUBLIC_CHAR_SERVER_URL:-${CHAR_SERVER_URL:-}}"
  [ -n "$accounts" ] && [ -n "$characters" ] || {
    printf '\033[31mNo ACCOUNTS_URL / CHAR_SERVER_URL in settings.env — the build would reach no server.\033[0m\n' >&2; return 1; }
  grep -v -E '^(ACCOUNTS_URL|CHAR_SERVER_URL)=' "$file" > "$file.tmp"
  printf 'ACCOUNTS_URL=%s\nCHAR_SERVER_URL=%s\n' "$accounts" "$characters" >> "$file.tmp"
  mv "$file.tmp" "$file"
  # A file inside a Mac app just changed, which breaks its signature: sign it again (tools/platform.sh explains why).
  if [ "${build%.app}" != "$build" ]; then
    sign_mac_app "$build" >/dev/null 2>&1 || { printf '\033[31mCould not sign %s again.\033[0m\n' "$build" >&2; return 1; }
  fi
  printf 'the build dials  %s  and  %s\n' "$accounts" "$characters"
}

# SWITCH THE EDITOR TO ANOTHER PLATFORM, AND WAIT UNTIL IT HAS. The creatures and models are built only for the
# platform the editor is switched to, so building for another one means switching first.
#
# THE PLATFORM IS THE ACTIVE BUILD PROFILE'S. This project keeps one per platform in Assets/Settings/Build Profiles,
# and while one is active, switching the platform any other way is undone on the next reload — MEASURED 2026-10-02:
# the switch to Mac reported success, and after the reload the editor was back on Windows with the Windows profile
# still active. So the PROFILE is switched, by name.
#
# Its own request, asked only while the editor is free and asked again if it did not take: a request sent while the
# editor is compiling is dropped ("main thread operation timed out"), and a switch only finishes after the request
# that asked for it has returned.
PROFILES="Assets/Settings/Build Profiles"
profile_for() {
  case "$1" in
    StandaloneOSX)       echo "$PROFILES/macOS.asset" ;;
    StandaloneWindows64) echo "$PROFILES/Windows.asset" ;;
    *)                   echo "" ;;
  esac
}
editor_profile() {
  "${UNITY_CMD[@]}" eval --code 'var a = UnityEditor.Build.Profile.BuildProfile.GetActiveBuildProfile(); return a != null ? UnityEditor.AssetDatabase.GetAssetPath(a) : "";' 2>/dev/null \
    | grep -o '"result":"[^"]*"' | cut -d'"' -f4
}
editor_target() {
  "${UNITY_CMD[@]}" eval --code 'return UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();' 2>/dev/null \
    | grep -o '"result":"[A-Za-z0-9]*"' | cut -d'"' -f4
}
editor_ready() { "${UNITY_CMD[@]}" editor_status 2>/dev/null | grep -q '"status":"ready"'; }
switch_profile_to() {
  local want="$1" i   # $2 = the platform that profile builds for
  for i in $(seq 1 60); do
    if editor_ready; then
      # Landed when the editor is on that profile's platform. Measured 2026-10-02: once switched, the editor can
      # report no active profile at all while sitting on the right platform — so the platform is what is read.
      [ "$(editor_target)" = "$2" ] && return 0
      printf '\033[36mSwitching the editor to %s …\033[0m\n' "$want" >&2
      "${UNITY_CMD[@]}" eval --timeout 900 --code "UnityEditor.Build.Profile.BuildProfile.SetActiveBuildProfile(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Build.Profile.BuildProfile>(\"$want\")); return \"asked\";" >/dev/null 2>&1
    fi
    sleep 20
  done
  printf '\033[31mThe editor did not switch to %s.\033[0m\n' "$want" >&2
  return 1
}

# WHERE THE EDITOR STARTED, so it is switched back there afterwards however the build went.
HOME_TARGET="$(editor_target)"
HOME_PROFILE="$(editor_profile)"
[ -n "$HOME_PROFILE" ] || HOME_PROFILE="$(profile_for "$HOME_TARGET")"
SWITCHED=""
if [ -n "$HOME_TARGET" ] && [ "$HOME_TARGET" != "$TARGET" ]; then
  WANT_PROFILE="$(profile_for "$TARGET")"
  [ -n "$WANT_PROFILE" ] || fail "There is no build profile here for $TARGET to switch the editor to."
  SWITCHED=1
  switch_profile_to "$WANT_PROFILE" "$TARGET" || { switch_profile_to "$HOME_PROFILE" "$HOME_TARGET"; exit 1; }
fi

STATUS=0
case "$WHICH" in
  server) build_one server || STATUS=1 ;;
  client) build_one client || STATUS=1 ;;
  both)   build_one server || STATUS=1; build_one client || STATUS=1 ;;
esac

# AND BACK to where the editor was, whether or not the build worked.
if [ -n "$SWITCHED" ]; then
  switch_profile_to "$HOME_PROFILE" "$HOME_TARGET" || STATUS=1
fi

if [ "$STATUS" -eq 0 ]; then
  echo
  echo "Run the server with:"
  echo "  tools/run-map-server.sh"
  echo "Open a player's game with:"
  echo "  tools/platform.sh open-game"
fi
exit "$STATUS"
