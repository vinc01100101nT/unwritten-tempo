#!/usr/bin/env bash
# Start the three servers — the sign-in service, the character server and the map server — and the website, each in
# its own Windows Terminal window, one in each corner of the screen. Double-click this file.
# restart-servers.sh restarts them later, inside these same windows.
#
# WHY THIS EXISTS: starting them meant opening four terminals, typing four commands and dragging four windows into
# place, every time. This is the same four commands with nothing to type.
#
# THE LAYOUT: each window is exactly one quarter of the screen (the screen minus the taskbar, measured when you run
# this) — the same rectangle Windows gives a window snapped into a corner.
#   ACCOUNT SERVICE  top-left        CHAR SERVER  top-right
#   WEBSITE          bottom-left     MAP SERVER   bottom-right
#
# WHAT RUNS IN EACH: exactly what you would type by hand —   npm start (in accounts/)  ·  sh charserver/run.sh  ·
# sh tools/run-map-server.sh  ·  sh tools/serve-updates.sh   — inside a Command Prompt that stays open when the server
# stops, so its last words stay readable. The map server waits in its window until the character server answers, so
# it never tries to register with nobody there.
#
# HOW A RESTART HAPPENS IN THE SAME WINDOW: each window runs its server in a loop. When the server stops, the loop
# looks for a restart request (a file restart-servers.sh writes into logs/windows/) and, if there is one, runs the
# same command again; if there is none — you pressed Ctrl+C yourself — it ends, leaving the prompt, as before. Each
# window also writes its own process id there, which is how restart-servers.sh finds it to press Ctrl+C in it.
#
# A SERVER THAT IS ALREADY RUNNING IS LEFT ALONE: if its port is taken, its window is not opened and nothing is
# stopped.
#
# Windows only — the layout is Windows Terminal's.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$REPO/tools/platform.sh"
. "$REPO/tools/load-settings.sh"

# Every port comes from settings.env and nowhere else; a missing one is said, never guessed.
for __setting in PORT CHAR_SERVER_PORT MAP_SERVER_ARRIVALS_PORT PATCH_PORT PATCH_URL; do
  [ -n "${!__setting:-}" ] || { printf '%s is not set in %s/settings.env.\n' "$__setting" "$REPO" >&2
                                read -r -p "Press Enter to close." _ || true; exit 2; }
done
ACCOUNTS_PORT="$PORT"
CHAR_PORT="$CHAR_SERVER_PORT"
MAP_PORT="$MAP_SERVER_ARRIVALS_PORT"
WEBSITE_PORT="$PATCH_PORT"
WINDOW_STATE="$REPO/logs/windows"

# THE ONE LIST OF WINDOWS — name (what restart-servers.sh is given) | title | corner | port. Opened, and restarted, in
# this order: the character server stops before the map server is asked to, so the map server always registers with
# the character server that is coming up, never with the one going away.
SERVERS="account|ACCOUNT SERVICE|top-left|$ACCOUNTS_PORT
char|CHAR SERVER|top-right|$CHAR_PORT
map|MAP SERVER|bottom-right|$MAP_PORT
website|WEBSITE|bottom-left|$WEBSITE_PORT"

# What each window runs. In a subshell, so Ctrl+C stops it exactly as it would when typed by hand.
run_server() (
  case "$1" in
    account) cd "$REPO/accounts" && npm start ;;
    char)    cd "$REPO" && sh charserver/run.sh ;;
    map)     echo "Waiting for the character server to answer on port $CHAR_PORT ..."
             until curl -s --max-time 2 "http://127.0.0.1:$CHAR_PORT/health" | grep -q '"ok":true'; do sleep 2; done
             echo "The character server answered. Starting the map server."
             cd "$REPO" && sh tools/run-map-server.sh ;;
    website) cd "$REPO" && sh tools/serve-updates.sh ;;
  esac
)

# Run inside each server window: the server, again and again for as long as restarts are asked for.
if [ "${1:-}" = window ]; then
  NAME="$2"
  mkdir -p "$WINDOW_STATE"
  cat "/proc/$$/winpid" > "$WINDOW_STATE/$NAME.pid"
  trap 'rm -f "$WINDOW_STATE/$NAME.pid" "$WINDOW_STATE/$NAME.restart"' EXIT
  trap ':' INT   # Ctrl+C reaches every program in the window; this loop must outlive it to decide what comes next
  while :; do
    rm -f "$WINDOW_STATE/$NAME.restart"
    run_server "$NAME" || true
    [ -f "$WINDOW_STATE/$NAME.restart" ] || break
    printf '\n===== restarting =====\n\n'
  done
  exit 0
fi

finish() {
  # Only keep this window open when there is something to read; otherwise it closes by itself.
  if [ -n "$NOTES" ]; then printf '%b' "$NOTES"; read -r -p "Press Enter to close." _; fi
}
NOTES=""
trap finish EXIT

[ "$PLATFORM" = windows ] || { NOTES="This script drives Windows Terminal windows, so it only runs on Windows.\n"; exit 1; }

# Run by restart-servers.sh: press Ctrl+C in each named window (all of them when none is named) after asking it to
# start again.
if [ "${1:-}" = restart ]; then
  shift
  NAMES="$(printf '%s\n' "$SERVERS" | cut -d'|' -f1 | tr '\n' ' ')"
  for asked in "$@"; do
    case " $NAMES " in *" $asked "*) ;; *) NOTES="There is no window called '$asked'. The windows are: $NAMES\n"; exit 2 ;; esac
  done

  CTRLC="$(mktemp --suffix=.ps1)"
  RESULT="$(mktemp)"
  trap 'rm -f "$CTRLC" "$RESULT"; finish' EXIT
  cat > "$CTRLC" <<'PS1'
# Presses Ctrl+C in the window whose loop has this process id — but only if that process is still that loop, since a
# closed window's id can be handed to some other program later.
param([int]$TargetPid, [string]$Name)
$p = Get-CimInstance Win32_Process -Filter "ProcessId=$TargetPid"
if (-not $p -or $p.CommandLine -notlike "*start-servers.sh*window $Name*") { Write-Output 'gone'; exit }
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Con {
  [DllImport("kernel32.dll")] static extern bool FreeConsole();
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool AttachConsole(uint pid);
  [DllImport("kernel32.dll")] static extern bool SetConsoleCtrlHandler(IntPtr h, bool add);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool GenerateConsoleCtrlEvent(uint ev, uint group);
  // A program can only press Ctrl+C in the window it is attached to, so this leaves its own, joins that one, and
  // ignores the Ctrl+C itself — it reaches every program attached to the window, this one included.
  public static string Send(uint pid) {
    FreeConsole();
    if (!AttachConsole(pid)) return "could not reach its window (error " + Marshal.GetLastWin32Error() + ")";
    SetConsoleCtrlHandler(IntPtr.Zero, true);
    bool sent = GenerateConsoleCtrlEvent(0, 0);
    int error = Marshal.GetLastWin32Error();
    System.Threading.Thread.Sleep(500);
    FreeConsole();
    return sent ? "sent" : "could not press Ctrl+C in its window (error " + error + ")";
  }
}
"@
# Its own output leaves with the window it left, so the answer goes in a file.
[Con]::Send([uint32]$TargetPid) | Set-Content -Path $env:CTRLC_RESULT
PS1

  while IFS='|' read -r name title corner port; do
    if [ $# -gt 0 ]; then case " $* " in *" $name "*) ;; *) continue ;; esac; fi
    pid="$(cat "$WINDOW_STATE/$name.pid" 2>/dev/null || true)"
    if [ -z "$pid" ]; then NOTES="$NOTES$title has no window opened by start-servers.sh — left alone.\n"; continue; fi
    touch "$WINDOW_STATE/$name.restart"
    # The programs listening now. "Stopped" means these are gone — not that the port is free, because the window
    # starts the server again straight away and a fast one is listening on the same port again within a second.
    before="$(port_pids "$port" | tr '\n' ' ')"
    : > "$RESULT"
    out="$(CTRLC_RESULT="$(cygpath -w "$RESULT")" powershell.exe -NoProfile -ExecutionPolicy Bypass \
      -File "$(cygpath -w "$CTRLC")" -TargetPid "$pid" -Name "$name" 2>&1 | tr -d '\r')" || true
    [ "$out" = gone ] || out="$(tr -d '\r' < "$RESULT")${out:+ $out}"
    case "$out" in
      sent) ;;
      gone) rm -f "$WINDOW_STATE/$name.pid" "$WINDOW_STATE/$name.restart"
            NOTES="$NOTES$title's window from start-servers.sh is closed — left alone.\n"; continue ;;
      *)    rm -f "$WINDOW_STATE/$name.restart"; NOTES="$NOTES$title: $out\n"; continue ;;
    esac
    echo "$title: Ctrl+C pressed, waiting for it to stop ..."
    still_up() { for p in $(port_pids "$port"); do case " $before " in *" $p "*) return 0 ;; esac; done; return 1; }
    for _ in $(seq 60); do still_up || break; sleep 0.5; done
    if still_up; then
      NOTES="$NOTES$title had not stopped 30 seconds after Ctrl+C. It starts again by itself once it does.\n"
    else
      echo "$title: stopped, starting again in its window."
    fi
  done <<< "$SERVERS"
  exit 0
fi

# One line per window to open:  title|corner|starting folder|command
WINDOWS=""
while IFS='|' read -r name title corner port; do
  if [ -n "$(port_pids "$port")" ]; then
    NOTES="$NOTES$title is already running (port $port is taken) — left alone, no window opened.\n"
  else
    WINDOWS="$WINDOWS$title|$corner|$(cygpath -w "$REPO")|sh start-servers.sh window $name"$'\n'
  fi
done <<< "$SERVERS"
[ -n "$WINDOWS" ] || exit 0

# Opening and placing the windows needs Windows' own window calls, which only PowerShell can make from here. The
# script is written to a temporary file for the one run and removed afterwards; the window list goes in by the
# environment, so nothing in it has to survive a second round of quoting.
PLACER="$(mktemp --suffix=.ps1)"
trap 'rm -f "$PLACER"; finish' EXIT
cat > "$PLACER" <<'PS1'
$ErrorActionPreference = 'Stop'
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic;
public static class Win {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SystemParametersInfo(int a, int b, ref RECT r, int c);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);

  // Every visible Windows Terminal window, by handle, with its title.
  public static Dictionary<IntPtr, string> Terminals() {
    var found = new Dictionary<IntPtr, string>();
    EnumWindows((h, x) => {
      var c = new StringBuilder(256); GetClassName(h, c, 256);
      if (c.ToString() == "CASCADIA_HOSTING_WINDOW_CLASS" && IsWindowVisible(h)) {
        var t = new StringBuilder(512); GetWindowText(h, t, 512); found[h] = t.ToString();
      }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@

# Physical pixels, so the work area and the window positions are measured in the same unit at any display scaling.
[Win]::SetProcessDPIAware() | Out-Null
$area = New-Object Win+RECT
[Win]::SystemParametersInfo(0x30, 0, [ref]$area, 0) | Out-Null   # the screen minus the taskbar
$midX = $area.L + [int](($area.R - $area.L) / 2)
$midY = $area.T + [int](($area.B - $area.T) / 2)
$corners = @{
  'top-left'     = @($area.L, $area.T, $midX,   $midY)
  'top-right'    = @($midX,   $area.T, $area.R, $midY)
  'bottom-left'  = @($area.L, $midY,   $midX,   $area.B)
  'bottom-right' = @($midX,   $midY,   $area.R, $area.B)
}

# Puts the window's VISIBLE edges on the target. A window also has an invisible border for resizing (8 pixels on
# its left, right and bottom here), so its full rectangle is the target grown by that border.
function Place($h, $target) {
  [Win]::ShowWindow($h, 9) | Out-Null   # un-maximise first; a maximised window ignores a new position
  for ($try = 0; $try -lt 5; $try++) {
    $r = New-Object Win+RECT; [Win]::GetWindowRect($h, [ref]$r) | Out-Null
    $f = New-Object Win+RECT; [Win]::DwmGetWindowAttribute($h, 9, [ref]$f, 16) | Out-Null
    if ($f.L -eq $target[0] -and $f.T -eq $target[1] -and $f.R -eq $target[2] -and $f.B -eq $target[3]) { return $true }
    $left = $f.L - $r.L; $top = $f.T - $r.T; $right = $r.R - $f.R; $bottom = $r.B - $f.B
    [Win]::SetWindowPos($h, [IntPtr]::Zero, $target[0] - $left, $target[1] - $top,
      ($target[2] - $target[0]) + $left + $right, ($target[3] - $target[1]) + $top + $bottom, 0x14) | Out-Null
    Start-Sleep -Milliseconds 300   # a terminal can still be sizing itself just after it opens; check again
  }
  return $false
}

foreach ($line in ($env:SERVER_WINDOWS -split "`n" | Where-Object { $_ })) {
  $title, $corner, $folder, $command = $line -split '\|', 4
  $before = [Win]::Terminals()
  Start-Process wt.exe -ArgumentList "-w new --title `"$title`" --suppressApplicationTitle -d `"$folder`" cmd /k $command"

  # Only a window that did not exist before this launch is ever moved, so one of yours with the same title is safe.
  $h = [IntPtr]::Zero
  $deadline = (Get-Date).AddSeconds(20)
  while ($h -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 200
    foreach ($kv in [Win]::Terminals().GetEnumerator()) {
      if (-not $before.ContainsKey($kv.Key) -and $kv.Value -eq $title) { $h = $kv.Key }
    }
  }
  if ($h -eq [IntPtr]::Zero) { Write-Output "$title opened, but its window was not found within 20 seconds, so it was not moved."; continue }
  if (-not (Place $h $corners[$corner])) { Write-Output "$title opened, but would not stay in the $corner corner." }
}
PS1

PROBLEMS="$(SERVER_WINDOWS="$WINDOWS" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$PLACER")" 2>&1 | tr -d '\r')" || true
[ -z "$PROBLEMS" ] || NOTES="$NOTES$PROBLEMS\n"
