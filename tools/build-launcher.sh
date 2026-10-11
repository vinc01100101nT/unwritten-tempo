#!/usr/bin/env bash
# Build the LAUNCHER: a program that fetches what changed since the player's last update, then starts the game (the
# source is launcher/, the update folder it reads is made by tools/publish-update.py). And the SMALL PROGRAM A PLAYER
# DOWNLOADS, which fetches that launcher into the player's own folder, keeps it up to date and starts it — one for
# Windows (launcher-updater/) and one for Mac (launcher-updater-mac/).
#
# USAGE:  tools/build-launcher.sh      # downloads from PATCH_URL in settings.env; refuses to build without it
#         PATCH_URL=https://patch.example.com tools/build-launcher.sh   # a value typed here wins
#
# WRITES: builds/launcher/windows/unwritten-launcher.exe          the small program, with launcher.txt — the download
#         builds/launcher/windows-patcher/unwritten-launcher.exe  the Windows launcher the small program fetches
#         builds/launcher/mac/unwritten.app                       the small Mac program, for both kinds of chip — the download
#         builds/launcher/mac.zip                                 the same, zipped to carry to the Windows PC
#         builds/launcher/mac-patcher-arm64/unwritten.app         the Mac launcher it fetches on Apple silicon Macs
#         builds/launcher/mac-patcher-x64/unwritten.app           the Mac launcher it fetches on Intel Macs
# Each program carries a launcher.txt naming the update server.
#
# EACH SMALL PROGRAM IS BUILT ON ITS OWN SYSTEM ONLY: it is compiled straight to a Windows or a Mac program, which
# only that kind of computer can do (Windows also needs Visual Studio's C++ build tools). Off Windows the small
# Windows program's folder is REMOVED rather than left holding an older build, so a publish can never hand out a
# stale one. Off a Mac the small Mac program's folder is LEFT AS IT IS, because that is where the one built on the Mac
# is unzipped (builds/launcher/mac.zip) — tools/publish-update.py refuses it if it names a different update server.
#
# EXIT: 0 = built, 1 = a build failed, 2 = setup problem.

set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$REPO/tools/platform.sh"
OUT="$REPO/builds/launcher"
# Where players' launchers download updates from: PATCH_URL in settings.env, the one file for every address.
. "$REPO/tools/load-settings.sh"
PATCH_URL="${PATCH_URL:-}"
[ $# -eq 0 ] || { printf 'This takes no options; set PATCH_URL in settings.env.\n' >&2; exit 2; }
[ -n "$PATCH_URL" ] || { printf 'PATCH_URL is empty in settings.env, so a launcher would not know where updates are.\n' >&2; exit 2; }

command -v dotnet >/dev/null || { echo "dotnet is not installed." >&2; exit 2; }

settings() {
  printf '# Where this launcher downloads updates from.\nPATCH_URL=%s\n' "$PATCH_URL" > "$1/launcher.txt"
}

publish() {
  local rid="$1" dir="$2"
  rm -rf "$dir"
  dotnet publish "$REPO/launcher/UnwrittenLauncher.csproj" -c Release -r "$rid" -o "$dir" -p:DebugType=none \
    >"$dir.log" 2>&1 || { echo "launcher build for $rid FAILED — see $dir.log" >&2; return 1; }
  rm -f "$dir.log"
}

# The launcher wears the game's colours, read from a file written from the game's code — written again here, so a
# launcher is never built with colours the game has since changed.
"$(find_python)" "$REPO/tools/ui-tokens.py" >/dev/null || { echo "could not read the game's colours (tools/ui-tokens.py)." >&2; exit 2; }

STATUS=0
mkdir -p "$OUT"

publish win-x64 "$OUT/windows-patcher" || STATUS=1

rm -rf "$OUT/windows"
if [ "$PLATFORM" = windows ]; then
  # The compiler finds Visual Studio's linker through vswhere, which Visual Studio installs here and not on the PATH.
  PATH="$PATH:/c/Program Files (x86)/Microsoft Visual Studio/Installer" \
    dotnet publish "$REPO/launcher-updater/LauncherUpdater.csproj" -c Release -o "$OUT/windows" -p:DebugType=none \
    >"$OUT/windows.log" 2>&1 \
    && [ -f "$OUT/windows/unwritten-launcher.exe" ] && rm -f "$OUT/windows.log" && settings "$OUT/windows" \
    || { echo "the small Windows program FAILED to build — see $OUT/windows.log" >&2; rm -rf "$OUT/windows"; STATUS=1; }
else
  printf '\033[33mNot on Windows: the small program Windows players download was not built, and its folder was removed.\033[0m\n' >&2
fi

# Where the Mac launchers were built before the small Mac program existed; nothing reads them any more.
rm -rf "$OUT/mac-arm64" "$OUT/mac-x64"

for arch in arm64 x64; do
  staging="$OUT/mac-$arch.staging"
  publish "osx-$arch" "$staging" || { STATUS=1; continue; }

  # A Mac only treats a program as an app when it sits inside a .app folder with a description of itself.
  app="$OUT/mac-patcher-$arch/unwritten.app"
  rm -rf "$OUT/mac-patcher-$arch"; mkdir -p "$app/Contents/MacOS"
  cp -R "$staging/." "$app/Contents/MacOS/"
  rm -rf "$staging"
  # IN Resources, NOT BESIDE THE PROGRAM. A Mac treats everything in MacOS/ as code that must carry its own
  # signature, so a text file there is never sealed — MEASURED 2026-10-02: a Mac refused the launcher as "damaged",
  # codesign saying "a sealed resource is missing or invalid". In Resources it is sealed like any other resource.
  mkdir -p "$app/Contents/Resources"
  settings "$app/Contents/Resources"
  # THE BUILD NUMBER MARKS A LAUNCHER BUILT SINCE 2026-10-11. Every Mac launcher handed out before carries none, and
  # that is how the launcher tells those apart and replaces them with the small Mac program (MacApps.OldLaunchers).
  cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>unwritten-launcher</string>
  <key>CFBundleIdentifier</key><string>com.unwritten.launcher</string>
  <key>CFBundleName</key><string>unwritten</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>$(date -u +%Y%m%d%H%M)</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF
  # Signed with no certificate, as Unity signs the game: an Apple silicon Mac will not run an unsigned program.
  # On any computer — tools/platform.sh signs off a Mac too.
  sign_mac_app "$app" >/dev/null 2>&1 || { echo "could not sign $app" >&2; STATUS=1; }
done

# THE SMALL MAC PROGRAM: built once per kind of chip, then the two joined into one program that runs on both, so
# one app is the whole download and the copy the Mac game list carries runs on any Mac.
if [ "$PLATFORM" = mac ]; then
  rm -rf "$OUT/mac" "$OUT/mac.zip"
  app="$OUT/mac/unwritten.app"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  built=1
  for arch in arm64 x64; do
    dotnet publish "$REPO/launcher-updater-mac/LauncherUpdaterMac.csproj" -c Release -r "osx-$arch" \
      -o "$OUT/mac-$arch.fetcher" -p:DebugType=none >"$OUT/mac-$arch.fetcher.log" 2>&1 \
      && [ -f "$OUT/mac-$arch.fetcher/unwritten-launcher" ] && rm -f "$OUT/mac-$arch.fetcher.log" \
      || { echo "the small Mac program for $arch FAILED to build — see $OUT/mac-$arch.fetcher.log" >&2; built=0; }
  done
  [ "$built" = 1 ] && lipo -create "$OUT/mac-arm64.fetcher/unwritten-launcher" "$OUT/mac-x64.fetcher/unwritten-launcher" \
                        -output "$app/Contents/MacOS/unwritten-launcher" || built=0
  rm -rf "$OUT/mac-arm64.fetcher" "$OUT/mac-x64.fetcher"
  settings "$app/Contents/Resources"
  # The build number is the time it was built: the launcher only ever replaces a player's copy with a newer one.
  cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>unwritten-launcher</string>
  <key>CFBundleIdentifier</key><string>com.unwritten.patcher</string>
  <key>CFBundleName</key><string>unwritten</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>$(date -u +%Y%m%d%H%M)</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
  if [ "$built" = 1 ] && sign_mac_app "$app" >/dev/null 2>&1; then
    # Zipped by the Mac's own tool with no Mac-only extras, which would land inside the app as files its signature
    # does not cover. Unzipped in builds/launcher/ on the Windows PC, it becomes builds/launcher/mac/unwritten.app.
    ditto -c -k --norsrc --noextattr --noqtn --keepParent "$OUT/mac" "$OUT/mac.zip"
  else
    echo "the small Mac program FAILED to build or sign" >&2; rm -rf "$OUT/mac"; STATUS=1
  fi
else
  printf '\033[33mNot on a Mac: the small program Mac players download was not built; builds/launcher/mac is left as it is.\033[0m\n' >&2
fi

if [ "$STATUS" -eq 0 ]; then
  for f in "$OUT/windows/unwritten-launcher.exe" "$OUT/windows-patcher/unwritten-launcher.exe" \
           "$OUT/mac/unwritten.app" "$OUT/mac-patcher-arm64/unwritten.app" "$OUT/mac-patcher-x64/unwritten.app"; do
    [ -e "$f" ] && printf '\033[32mbuilt\033[0m  %s  (%s)\n' "$f" "$(du -sh "$f" | cut -f1)"
  done
fi
exit "$STATUS"
