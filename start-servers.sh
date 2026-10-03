#!/usr/bin/env bash
# Start the three servers — the sign-in service, the character server and the map server — each in its own Windows
# Terminal window, laid out in three corners of the screen. Double-click this file.
#
# WHY THIS EXISTS: starting them meant opening three terminals, typing three commands and dragging three windows
# into place, every time. This is the same three commands with nothing to type.
#
# THE LAYOUT: each window is exactly one quarter of the screen (the screen minus the taskbar, measured when you run
# this) — the same rectangle Windows gives a window snapped into a corner.
#   ACCOUNT SERVICE  top-left        CHAR SERVER  top-right
#   (left free for your own window)  MAP SERVER   bottom-right
#
# WHAT RUNS IN EACH: exactly what you would type by hand, inside a Command Prompt that stays open if the server stops,
# so its last words stay readable:   npm start (in accounts/)  ·  sh charserver/run.sh  ·  sh tools/run-map-server.sh
#
# A SERVER THAT IS ALREADY RUNNING IS LEFT ALONE: if its port is taken, its window is not opened and nothing is
# stopped. The map server waits in its window until the character server answers, so it never tries to register
# with nobody there.
#
# Windows only — the layout is Windows Terminal's.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$REPO/tools/platform.sh"
. "$REPO/tools/load-settings.sh"

ACCOUNTS_PORT="${PORT:-8080}"
CHAR_PORT="${CHAR_SERVER_PORT:-8081}"
MAP_PORT="${MAP_SERVER_ARRIVALS_PORT:-7788}"

# Run inside the MAP SERVER window: wait for the character server, then start the map server in this same window.
if [ "${1:-}" = map-after-char-server ]; then
  echo "Waiting for the character server to answer on port $CHAR_PORT ..."
  until curl -s --max-time 2 "http://127.0.0.1:$CHAR_PORT/health" | grep -q '"ok":true'; do sleep 2; done
  echo "The character server answered. Starting the map server."
  exec "$REPO/tools/run-map-server.sh"
fi

finish() {
  # Only keep this window open when there is something to read; otherwise it closes by itself.
  if [ -n "$NOTES" ]; then printf '%b' "$NOTES"; read -r -p "Press Enter to close." _; fi
}
NOTES=""
trap finish EXIT

[ "$PLATFORM" = windows ] || { NOTES="This script lays out Windows Terminal windows, so it only runs on Windows.\n"; exit 1; }

# One line per window to open:  title|corner|starting folder|command
WINDOWS=""
queue() {   # queue <title> <corner> <port> <folder> <command>
  if [ -n "$(port_pids "$3")" ]; then
    NOTES="$NOTES$1 is already running (port $3 is taken) — left alone, no window opened.\n"
  else
    WINDOWS="$WINDOWS$1|$2|$(cygpath -w "$4")|$5"$'\n'
  fi
}
queue "ACCOUNT SERVICE" top-left     "$ACCOUNTS_PORT" "$REPO/accounts" "npm start"
queue "CHAR SERVER"     top-right    "$CHAR_PORT"     "$REPO"          "sh charserver/run.sh"
queue "MAP SERVER"      bottom-right "$MAP_PORT"      "$REPO"          "sh start-servers.sh map-after-char-server"
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
