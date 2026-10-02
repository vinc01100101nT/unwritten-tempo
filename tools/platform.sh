#!/bin/sh
# THE ONE PLACE THAT KNOWS WHICH COMPUTER THIS IS — Mac, Windows or Linux — and where things live on it.
#
# WHY THIS EXISTS: the project is worked on from a Mac and from a Windows PC, and every script used to hardcode
# the Mac's answers (/Applications/Unity, codesign, lsof, unwritten.app). On the other machine those scripts did
# not fail loudly — they reported "Unity not installed" with Unity sitting on D:. Every OS-dependent answer now
# comes from here, so a script never asks the OS a question itself, and a third machine is one edit in one file.
#
# TWO WAYS TO USE IT, the same answers either way:
#   . "$REPO/tools/platform.sh"            # from a shell script: sets PLATFORM / EXE and the functions below
#   sh tools/platform.sh <question> [arg]  # from a terminal, from Python (tools/platform_ask.py), from a doc
#
# QUESTIONS:
#   os                      mac | windows | linux
#   python                  a Python command that actually RUNS (Windows ships a "python3" that only prints an error)
#   unity-data              the folder holding Unity's own compiler and libraries, for this project's Unity version
#   unity-cli               the Unity command-line link to the running editor
#   browser                 a Chrome-family browser
#   native-path <path>      the path written the way this OS's own programs read it (D:/x on Windows)
#   build-target            the Unity build target for a game that runs on THIS computer
#   game-program <client|server>   the program tools/build-game.sh made for this computer
#   open-game [args]        start one copy of the game in the background (settings typed in front still apply)
#   port-info <port>...     what is listening on each port: process id, when it started, its command (silent = none)
#   stop-port <port>...     stop whatever is listening on each port — by port, never by name
#   lan-address             this computer's address on the local network, for players on other machines
#   editor-log | player-log the log Unity's editor writes, and the one a client build writes (Player-prev.log beside it)
#   open-launcher           open the launcher tools/build-launcher.sh made for this computer
#   db-start | db-stop      start or stop the local Postgres
#   wal-archive-command <folder>   what Postgres runs to copy each finished change-log file into <folder>
#   wal-restore-command <folder>   what a rebuilt Postgres runs to fetch one change-log file back out of <folder>
#   time-offset "<YYYY-MM-DD HH:MM:SS>"   this computer's offset from UTC at that local moment, e.g. +0800
#   sign-mac-app <app>      sign a Mac app again without a certificate — codesign on a Mac, rcodesign elsewhere
#
# Plain POSIX sh: Claude Code's hooks start through sh on a Mac and through Git Bash on Windows.

case "$(uname -s)" in
  Darwin)               PLATFORM=mac ;;
  MINGW*|MSYS*|CYGWIN*) PLATFORM=windows ;;
  *)                    PLATFORM=linux ;;
esac
EXE=""; [ "$PLATFORM" = windows ] && EXE=".exe"

# A script that loads this sets REPO first, as it does for load-settings.sh; run as a command, it is this file's own.
__PLATFORM_REPO="${REPO:-$(CDPATH= cd -- "$(dirname -- "$0")/.." 2>/dev/null && pwd)}"

native_path() {
  if [ "$PLATFORM" = windows ] && command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s\n' "$1"; fi
}

find_python() {
  for __py in python3 python py; do
    command -v "$__py" >/dev/null 2>&1 && "$__py" -c "import sys" >/dev/null 2>&1 && { echo "$__py"; return 0; }
  done
  return 1
}

# Where Unity Hub installs editors on this computer: the folder chosen in the Hub (its own setting, read rather than
# copied), then the Hub's default.
__unity_roots() {
  case "$PLATFORM" in
    mac)     __hubcfg="$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json"; __default="/Applications/Unity/Hub/Editor" ;;
    windows) __hubcfg="${APPDATA:-$HOME/AppData/Roaming}/UnityHub/secondaryInstallPath.json"; __default="C:/Program Files/Unity/Hub/Editor" ;;
    *)       __hubcfg="$HOME/.config/UnityHub/secondaryInstallPath.json"; __default="$HOME/Unity/Hub/Editor" ;;
  esac
  if [ -f "$__hubcfg" ]; then
    __chosen="$(tr -d '"\r\n' < "$__hubcfg" | sed 's#\\\\#/#g; s#\\#/#g')"
    [ -n "$__chosen" ] && native_path "$__chosen"
  fi
  echo "$__default"
}

# The part of an editor install that holds its compiler and libraries.
__unity_data_suffix() {
  if [ "$PLATFORM" = mac ]; then echo "Unity.app/Contents/Resources/Scripting"; else echo "Editor/Data"; fi
}

# unity_data [version] — this project's version by default. Falls back to the newest installed one, and says so.
unity_data() {
  __ver="${1:-$(awk '/^m_EditorVersion:/{print $2}' "$__PLATFORM_REPO/unity/ProjectSettings/ProjectVersion.txt" 2>/dev/null | tr -d '\r')}"
  __suffix="$(__unity_data_suffix)"
  __roots="$(__unity_roots)"
  __found=""
  __IFS="$IFS"; IFS='
'
  for __r in $__roots; do
    [ -d "$__r/$__ver/$__suffix" ] && { __found="$__r/$__ver/$__suffix"; break; }
  done
  if [ -z "$__found" ]; then
    __found="$(for __r in $__roots; do
                 for __d in "$__r"/*/"$__suffix"; do [ -d "$__d" ] && echo "$__d"; done
               done | sort -V | tail -1)"
    [ -n "$__found" ] && printf '\033[33mWARNING: Unity %s not installed; using %s instead.\033[0m\n' "$__ver" "$__found" >&2
  fi
  IFS="$__IFS"
  [ -n "$__found" ] || { printf 'Unity %s not installed, and no other Unity found in: %s\n' "$__ver" "$(echo $__roots)" >&2; return 1; }
  echo "$__found"
}

unity_cli() {
  # The Mac installs it under ~/.unity, Windows under the user's local app data (measured 2026-10-01).
  for __c in "$HOME/.unity/bin/unity$EXE" "$HOME/.unity/bin/unity" \
             "$(native_path "${LOCALAPPDATA:-$HOME/AppData/Local}")/Unity/bin/unity$EXE"; do
    [ -f "$__c" ] && { echo "$__c"; return 0; }
  done
  command -v unity 2>/dev/null && return 0
  echo "No Unity command-line link on this computer (looked in $HOME/.unity/bin, the user's AppData/Local/Unity/bin, and on the PATH)." >&2
  return 1
}

find_browser() {
  case "$PLATFORM" in
    mac) set -- "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" \
                "/Applications/Chromium.app/Contents/MacOS/Chromium" \
                "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge" ;;
    windows) set -- "C:/Program Files/Google/Chrome/Application/chrome.exe" \
                    "C:/Program Files (x86)/Google/Chrome/Application/chrome.exe" \
                    "$(native_path "${LOCALAPPDATA:-$HOME/AppData/Local}")/Google/Chrome/Application/chrome.exe" \
                    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" \
                    "C:/Program Files/Microsoft/Edge/Application/msedge.exe" ;;
    *) set -- /usr/bin/google-chrome /usr/bin/chromium /usr/bin/chromium-browser ;;
  esac
  for __b in "$@"; do [ -f "$__b" ] && { echo "$__b"; return 0; }; done
  for __n in google-chrome chromium chrome; do command -v "$__n" 2>/dev/null && return 0; done
  return 1
}

build_target() {
  case "$PLATFORM" in mac) echo StandaloneOSX ;; windows) echo StandaloneWindows64 ;; *) echo StandaloneLinux64 ;; esac
}

# game_program <client|server> — the file tools/build-game.sh made for this computer, the one to run.
game_program() {
  __kind="${1:-client}"
  __name=unwritten; [ "$__kind" = server ] && __name=unwritten-server
  case "$PLATFORM" in
    # A Mac server build is a plain folder holding the program; only the player's build is a real app bundle.
    mac)     if [ "$__kind" = server ]; then echo "$__PLATFORM_REPO/builds/server/$__name.app/unwritten"
             else echo "$__PLATFORM_REPO/builds/client/$__name.app/Contents/MacOS/unwritten"; fi ;;
    windows) echo "$(native_path "$__PLATFORM_REPO")/builds/$__kind/$__name.exe" ;;
    *)       if [ "$__kind" = server ]; then echo "$__PLATFORM_REPO/builds/server/$__name.app/unwritten"
             else echo "$__PLATFORM_REPO/builds/client/$__name.app"; fi ;;
  esac
}

# The process ids listening on a port, TCP or UDP, one per line.
port_pids() {
  if [ "$PLATFORM" = windows ]; then
    netstat -ano | tr -d '\r' | awk -v p=":$1" '
      ($1=="TCP" && $4=="LISTENING" && substr($2, length($2)-length(p)+1)==p) { print $5 }
      ($1=="UDP" && substr($2, length($2)-length(p)+1)==p) { print $4 }' | sort -u
  else
    { lsof -nP -iTCP:"$1" -sTCP:LISTEN -t; lsof -nP -iUDP:"$1" -t; } 2>/dev/null | sort -u
  fi
}

# port_info <port>... — one line per listener; exits 1 when NOTHING listens on any of them (so "silent = all clear").
port_info() {
  __any=1
  for __port in "$@"; do
    __pids="$(port_pids "$__port")"
    [ -n "$__pids" ] || continue
    __any=0
    for __p in $__pids; do
      printf 'port %s  ' "$__port"
      if [ "$PLATFORM" = windows ]; then
        powershell.exe -NoProfile -Command "\$p = Get-Process -Id $__p; '{0}  started {1:yyyy-MM-dd HH:mm:ss}  {2}' -f \$p.Id, \$p.StartTime, \$p.Path" | tr -d '\r'
      else
        ps -o pid=,lstart=,command= -p "$__p"
      fi
    done
  done
  return $__any
}

# Stops by PORT, never by name — a name matches every copy on the machine, including one somebody else is using.
stop_port() {
  for __port in "$@"; do
    for __p in $(port_pids "$__port"); do
      if [ "$PLATFORM" = windows ]; then taskkill //PID "$__p" //F >/dev/null; else kill "$__p"; fi
      echo "stopped $__p (port $__port)"
    done
  done
}

# This computer's address on the local network, for players on other machines.
lan_address() {
  case "$PLATFORM" in
    mac)     ipconfig getifaddr en0 ;;
    # The adapter that has a way out to the router — virtual adapters (VMware, Hyper-V) have none.
    windows) powershell.exe -NoProfile -Command "(Get-NetIPConfiguration | Where-Object { \$_.IPv4DefaultGateway } | Select-Object -First 1).IPv4Address.IPAddress" | tr -d '\r' ;;
    *)       hostname -I | awk '{print $1}' ;;
  esac
}

# Where Unity writes its logs on this computer: the editor's, and a client build's (the run before it sits beside it
# as Player-prev.log).
editor_log() {
  case "$PLATFORM" in
    mac)     echo "$HOME/Library/Logs/Unity/Editor.log" ;;
    windows) echo "$(native_path "${LOCALAPPDATA:-$HOME/AppData/Local}")/Unity/Editor/Editor.log" ;;
    *)       echo "$HOME/.config/unity3d/Editor.log" ;;
  esac
}
player_log() {
  case "$PLATFORM" in
    mac)     echo "$HOME/Library/Logs/DefaultCompany/unwritten/Player.log" ;;
    windows) echo "$(native_path "$HOME")/AppData/LocalLow/DefaultCompany/unwritten/Player.log" ;;
    *)       echo "$HOME/.config/unity3d/DefaultCompany/unwritten/Player.log" ;;
  esac
}

# Open the launcher tools/build-launcher.sh made for this computer.
open_launcher() {
  case "$PLATFORM" in
    mac)     __arch=x64; [ "$(uname -m)" = arm64 ] && __arch=arm64
             open "$__PLATFORM_REPO/builds/launcher/mac-$__arch/unwritten.app" ;;
    windows) "$(native_path "$__PLATFORM_REPO")/builds/launcher/windows/unwritten-launcher.exe" >/dev/null 2>&1 & ;;
    *)       echo "No launcher is built for Linux." >&2; return 2 ;;
  esac
}

db_service() {
  __action="$1"
  case "$PLATFORM" in
    mac) brew services "$__action" postgresql@17 ;;
    # A Windows service needs administrator rights to start or stop, so this asks for them (one prompt).
    windows) __verb=Start-Service; [ "$__action" = stop ] && __verb=Stop-Service
             powershell.exe -NoProfile -Command "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -Command $__verb -Name postgresql*'" ;;
    *) sudo systemctl "$__action" postgresql ;;
  esac
}

# Postgres runs these two through cmd.exe on Windows and through sh everywhere else, so each is written in that
# shell's words. The folder must be a full path: Postgres runs them from inside its own data folder.
#
# Copying IN refuses to overwrite. A file already in the archive under the same name means two databases are
# writing to one folder, or a copy went wrong — either way Postgres must hear "failed" and keep the file, never
# have the old one silently replaced. (Measured on Windows 2026-10-02: the first copy exits 0, a second exits 1
# and leaves the first file untouched.)
wal_archive_command() {
  case "$PLATFORM" in
    windows) __d="$(cygpath -w "$1")"
             printf 'if exist "%s\\%%f" (exit 1) else (copy /b "%%p" "%s\\%%f" >nul)\n' "$__d" "$__d" ;;
    *)       printf "test ! -f '%s/%%f' && cp '%%p' '%s/%%f'\n" "$1" "$1" ;;
  esac
}
wal_restore_command() {
  case "$PLATFORM" in
    windows) printf 'copy /b "%s\\%%f" "%%p" >nul\n' "$(cygpath -w "$1")" ;;
    *)       printf "cp '%s/%%f' '%%p'\n" "$1" ;;
  esac
}

# time_offset "YYYY-MM-DD HH:MM:SS" — this computer's offset from UTC at THAT local moment, not today's, so a time
# on the far side of a daylight-saving change still means the instant the person typed. Refuses any other shape.
time_offset() {
  case "$1" in
    [0-9][0-9][0-9][0-9]-[0-1][0-9]-[0-3][0-9]\ [0-2][0-9]:[0-5][0-9]:[0-5][0-9]) ;;
    *) return 1 ;;
  esac
  if [ "$PLATFORM" = mac ]; then date -j -f '%Y-%m-%d %H:%M:%S' "$1" '+%z'; else date -d "$1" '+%z'; fi
}

# Changing a file inside a Mac app breaks its signature, and a Mac will not open a downloaded app whose signature
# is broken — so it is signed again, the same no-certificate way Unity signs it. Only a Mac can do that.
# A MAC APP MADE ON WINDOWS IS SIGNED HERE TOO, with rcodesign (github.com/indygreg/apple-platform-rs, installed to
# ~/.local/bin): with no certificate given it makes the same certificate-free signature `codesign --sign -` makes,
# which is all an Apple silicon Mac needs to run a program. Without it every Mac copy had to be built on a Mac.
sign_mac_app() {
  if [ "$PLATFORM" = mac ]; then codesign --force --deep --sign - "$1"; return; fi
  __rc="$(command -v rcodesign 2>/dev/null || true)"
  [ -n "$__rc" ] || [ ! -x "$HOME/.local/bin/rcodesign$EXE" ] || __rc="$HOME/.local/bin/rcodesign$EXE"
  [ -n "$__rc" ] || { echo "No rcodesign — install it to ~/.local/bin to sign a Mac app off a Mac." >&2; return 2; }
  "$__rc" sign "$1"
}

# Run as a command rather than loaded: answer the question asked.
if [ "$(basename -- "$0")" = platform.sh ]; then
  __q="${1:-}"; [ $# -gt 0 ] && shift
  case "$__q" in
    os)           echo "$PLATFORM" ;;
    python)       find_python || { echo "No working Python found (tried python3, python, py)." >&2; exit 1; } ;;
    unity-data)   unity_data "$@" ;;
    unity-cli)    unity_cli ;;
    browser)      find_browser || { echo "No Chrome-family browser found." >&2; exit 1; } ;;
    native-path)  native_path "$1" ;;
    build-target) build_target ;;
    game-program) game_program "$@" ;;
    open-game)    __prog="$(game_program client)"
                  [ -f "$__prog" ] || { echo "No game at $__prog — build it with: tools/build-game.sh client" >&2; exit 2; }
                  "$__prog" "$@" >/dev/null 2>&1 & ;;
    port-info)    port_info "$@" ;;
    stop-port)    stop_port "$@" ;;
    lan-address)  lan_address ;;
    editor-log)   editor_log ;;
    player-log)   player_log ;;
    open-launcher) open_launcher ;;
    db-start)     db_service start ;;
    db-stop)      db_service stop ;;
    wal-archive-command) [ -n "${1:-}" ] || { echo "Which folder?" >&2; exit 2; }; wal_archive_command "$1" ;;
    wal-restore-command) [ -n "${1:-}" ] || { echo "Which folder?" >&2; exit 2; }; wal_restore_command "$1" ;;
    time-offset)  time_offset "${1:-}" || { echo "Not a moment in the shape YYYY-MM-DD HH:MM:SS: '${1:-}'" >&2; exit 2; } ;;
    sign-mac-app) sign_mac_app "$1" ;;
    *) sed -n '/^# QUESTIONS:/,/^# Plain POSIX/p' "$0" | sed '$d; s/^# \{0,1\}//' >&2; exit 2 ;;
  esac
fi
