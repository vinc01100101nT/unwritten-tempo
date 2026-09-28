#!/usr/bin/env bash
# Build the LAUNCHER players download: a program that fetches what changed since their last update, then starts
# the game (the source is launcher/, the update folder it reads is made by tools/publish-update.py).
#
# USAGE:  tools/build-launcher.sh      # downloads from PATCH_URL in settings.env (http://localhost:8090 if empty)
#         PATCH_URL=https://patch.example.com tools/build-launcher.sh   # a value typed here wins
#
# WRITES: builds/launcher/windows/unwritten-launcher.exe
#         builds/launcher/mac-arm64/unwritten.app      (Apple silicon Macs)
#         builds/launcher/mac-x64/unwritten.app        (Intel Macs)
# Each carries a launcher.txt naming the update server, when PATCH_URL is set.
#
# EXIT: 0 = built, 1 = a build failed, 2 = setup problem.

set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$REPO/builds/launcher"
# Where players' launchers download updates from: PATCH_URL in settings.env, the one file for every address.
. "$REPO/tools/load-settings.sh"
PATCH_URL="${PATCH_URL:-}"
[ $# -eq 0 ] || { printf 'This takes no options; set PATCH_URL in settings.env.\n' >&2; exit 2; }

command -v dotnet >/dev/null || { echo "dotnet is not installed." >&2; exit 2; }

settings() {
  [ -n "$PATCH_URL" ] || return 0
  printf '# Where this launcher downloads updates from.\nPATCH_URL=%s\n' "$PATCH_URL" > "$1/launcher.txt"
}

publish() {
  local rid="$1" dir="$2"
  rm -rf "$dir"
  dotnet publish "$REPO/launcher/UnwrittenLauncher.csproj" -c Release -r "$rid" -o "$dir" -p:DebugType=none \
    >"$dir.log" 2>&1 || { echo "launcher build for $rid FAILED — see $dir.log" >&2; return 1; }
  rm -f "$dir.log"
}

STATUS=0
mkdir -p "$OUT"

publish win-x64 "$OUT/windows" && settings "$OUT/windows" || STATUS=1

for arch in arm64 x64; do
  staging="$OUT/mac-$arch.staging"
  publish "osx-$arch" "$staging" || { STATUS=1; continue; }

  # A Mac only treats a program as an app when it sits inside a .app folder with a description of itself.
  app="$OUT/mac-$arch/unwritten.app"
  rm -rf "$OUT/mac-$arch"; mkdir -p "$app/Contents/MacOS"
  cp -R "$staging/." "$app/Contents/MacOS/"
  rm -rf "$staging"
  settings "$app/Contents/MacOS"
  cat > "$app/Contents/Info.plist" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>unwritten-launcher</string>
  <key>CFBundleIdentifier</key><string>com.unwritten.launcher</string>
  <key>CFBundleName</key><string>unwritten</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF
  # Signed with no certificate, as Unity signs the game: an Apple silicon Mac will not run an unsigned program.
  if [ "$(uname)" = "Darwin" ]; then
    codesign --force --deep --sign - "$app" >/dev/null 2>&1 || { echo "could not sign $app" >&2; STATUS=1; }
  fi
done

if [ "$STATUS" -eq 0 ]; then
  for f in "$OUT/windows/unwritten-launcher.exe" "$OUT/mac-arm64/unwritten.app" "$OUT/mac-x64/unwritten.app"; do
    printf '\033[32mbuilt\033[0m  %s  (%s)\n' "$f" "$(du -sh "$f" | cut -f1)"
  done
  [ -n "$PATCH_URL" ] || echo "PATCH_URL is empty in settings.env: these launchers look for updates at http://localhost:8090."
fi
exit "$STATUS"
