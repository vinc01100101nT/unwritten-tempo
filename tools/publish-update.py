#!/usr/bin/env python3
"""Publish a game update: stamp one version into the player builds, and lay them out as the folder the launcher
downloads from.

WHAT IT WRITES, into --out (default builds/patch):

  version.txt                  the version every player's copy must match. THE one owner of that number: the
                               character server reads this file (its PATCH_DIR setting points at this folder) and
                               turns away any copy that says otherwise.
  news.json                    the launcher's news box, copied from launcher/news.json.
  <platform>/manifest.json     the version these files are, and every game file: its path, size, fingerprint, and
                               whether it is a program. The launcher shows THIS version, not version.txt, so a
                               platform left out of a publish shows its own older number and says it is behind.
  <platform>/files/<sha256>    each file's contents, under its fingerprint.
  index.html + its files       the website (website/index.html), so opening this folder's address in a browser
                               shows the site and its Download page offers the launcher. Beside it: ui-tokens.css
                               and fonts/ (the game's own colours and fonts, from prototypes/ui-tokens.css),
                               content/ and assets/ (the wiki's pages and pictures, copied from wiki/), favicon.ico,
                               and site.json (the character server's address the status page reads, from
                               settings.env).
  download/unwritten-windows.zip   what a Windows player downloads: the small program that fetches the launcher
                               (launcher-updater/), with the launcher.txt naming this server, zipped, from
                               builds/launcher/windows. It fetches the launcher, and the launcher fetches the game.
  windows/patcher.json         the Windows launcher that small program fetches: its size and fingerprint, the update
                               address players should be using, and the fingerprint of the launcher before it. Its
                               contents sit in windows/files/ under its fingerprint, from builds/launcher/windows-patcher.
  download/unwritten-mac-arm64.zip, download/unwritten-mac-x64.zip   what a Mac player downloads: the small Mac
                               program (launcher-updater-mac/, one app for both kinds of chip, so both zips hold the
                               same app), from builds/launcher/mac. It fetches the launcher for its chip.
  mac/patcher-arm64.json, mac/patcher-x64.json   the Mac launcher that program fetches, one per kind of chip: every
                               file of the app with its size, fingerprint and whether it is a program, the update
                               address, and the fingerprints of the launcher before it. Contents in mac/files/, from
                               builds/launcher/mac-patcher-arm64 and mac-patcher-x64.

THE FIRST LAUNCHER IS HANDED OVER THROUGH THE WINDOWS LIST. That launcher (in players' hands since 2026-10-04) cannot
update itself, keeps the game beside itself, and on Start runs the list's "start" and nothing else. So the Windows
list names the small program as one more file among the game's and as "start", and the game's own program as "play",
which every later launcher runs. The list stays a superset of the game, so the first launcher deletes nothing; its
Start runs the small program, which fetches the launcher, which moves the game into the player's own folder.
--without-hand-over publishes the list the old way ("start" is the game, no "play", no extra file), which withdraws
the small program from every first launcher in one publish.

THE MAC LAUNCHERS ARE HANDED OVER THE SAME WAY, through the Mac list. Two kinds are in players' hands: the first, which
runs "start", and the one handed out on 2026-10-11, which runs "play". So the Mac list carries the small Mac program's
files under unwritten-patcher.app/, names its program as both "start" and "play", the game as "game" (which the
launcher from now on runs), and the small program's build number as "fetcher". Whichever launcher the player has, its
Start runs the small program, which fetches the launcher, which puts the small program where the player's app was.
--without-hand-over withdraws it from the Mac list too.

A PUBLISH THAT LEAVES MAC OUT NEVER TOUCHES IT: the Mac list, the Mac launchers and the Mac downloads stay exactly as
they were, so a Windows publish is never held up by a Mac program that has not been built.

THE SAME VERSION GOES INTO THE GAME. Before listing a build, its settings file (StreamingAssets/server.txt) gets
GAME_VERSION=<version>, which the game sends to the character server with every request. So a platform left out
of a publish keeps its old version, and its players are asked to update until it is published too.

version.txt is written LAST, after every manifest, so the server never demands a version the launcher cannot yet
hand out. A file is uploaded once and kept while the newest or the previous manifest still names it, so a player
halfway through downloading the previous update is not left pointing at nothing.

--site-only refreshes the website, its wiki pages and news.json and touches nothing else — no build is read, no
version is written — so a wiki edit reaches the site without publishing the game again.

--launcher-only publishes the launchers, the small programs and the downloads, with the website and news — no game
build is read and no version is written, so a launcher change reaches players without publishing the game again. Each
list already published is kept file for file; only the small program's entries in it are replaced. Mac is included
when builds/launcher/mac holds the small Mac program (tools/build-launcher.sh makes it on any computer from the copy
in git), and skipped, saying so, when it does not.

USAGE:   tools/publish-update.py                        # builds/client, as the platform of THIS computer
         tools/publish-update.py --site-only            # the website, wiki pages and news only
         tools/publish-update.py --launcher-only        # the launcher, the downloads, the website and news only
         tools/publish-update.py --windows builds/client-windows --mac builds/client
         tools/publish-update.py --out /some/folder --version 2026.10.01-1200
EXIT:    0 = published, 1 = something is wrong (each named), 2 = setup problem.
"""
import argparse
import datetime
import hashlib
import json
import os
import plistlib
import re
import shutil
import sys
import zipfile

# Which computer this is, and the Mac-only signing, both come from tools/platform.sh — never checked here.
from platform_ask import run as run_platform
from settings_ask import setting

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Written beside a build by Unity or by tools/build-game.sh, never part of what a player runs.
SKIP_DIR_SUFFIX = "_DoNotShip"
SKIP_FILES = (".DS_Store",)
SKIP_PREFIX = ("mono_crash",)
SKIP_SUFFIX = (".no-content.txt",)


def fail(msg, code=1):
    print(f"\033[31m{msg}\033[0m", file=sys.stderr)
    sys.exit(code)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def shipped_files(build):
    for root, dirs, files in os.walk(build):
        dirs[:] = sorted(d for d in dirs if not d.endswith(SKIP_DIR_SUFFIX))
        for name in sorted(files):
            if name in SKIP_FILES or name.startswith(SKIP_PREFIX) or name.endswith(SKIP_SUFFIX):
                continue
            yield os.path.join(root, name)


def stamp_version(build, version):
    """Write GAME_VERSION into the build's settings file. Exactly one must exist."""
    found = [p for p in shipped_files(build)
             if os.path.basename(p) == "server.txt" and os.path.basename(os.path.dirname(p)) == "StreamingAssets"]
    if len(found) != 1:
        fail(f"{build}: expected exactly one StreamingAssets/server.txt, found {len(found)}.")
    path = found[0]
    with open(path, encoding="utf-8") as f:
        lines = [l for l in f.read().splitlines() if not l.strip().startswith("GAME_VERSION=")]
    local = [l.strip() for l in lines
             if not l.strip().startswith("#") and ("localhost" in l or "127.0.0.1" in l)]
    if local:
        print(f"\033[33mWARNING: {build} still points at this machine ({', '.join(local)}).\033[0m\n"
              f"   Players will reach nothing. Set PUBLIC_ACCOUNTS_URL and PUBLIC_CHAR_SERVER_URL in settings.env "
              f"and rebuild (tools/build-game.sh client) before publishing to real players.", file=sys.stderr)
    lines.append(f"GAME_VERSION={version}")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


def start_program(build, platform):
    """The file the launcher runs. Read from the build itself, never typed here."""
    top = sorted(os.listdir(build))
    if platform == "mac":
        apps = [t for t in top if t.endswith(".app")]
        if len(apps) != 1:
            fail(f"{build}: expected exactly one .app, found {apps}.")
        with open(os.path.join(build, apps[0], "Contents", "Info.plist"), "rb") as f:
            exe = plistlib.load(f)["CFBundleExecutable"]
        return f"{apps[0]}/Contents/MacOS/{exe}"
    exes = [t for t in top if t.lower().endswith(".exe") and t.lower() != "unitycrashhandler64.exe"]
    if len(exes) != 1:
        fail(f"{build}: expected exactly one game .exe, found {exes}.")
    return exes[0]


def resign_mac(build):
    """Stamping the version changes a file inside the app, which breaks the app's signature — and a Mac refuses to
    open a downloaded app whose signature is broken. So it is signed again, the same no-certificate way Unity
    signed it, before its files are fingerprinted. Off a Mac, tools/platform.sh signs with rcodesign instead."""
    for t in os.listdir(build):
        if t.endswith(".app"):
            r = run_platform("sign-mac-app", os.path.join(build, t))
            if r.returncode != 0:
                fail(f"Could not sign {t} again: {r.stderr.strip()}")


# The small program a Windows player downloads, and the name it goes by among the game's files when the first
# launcher is handed over to it. The same name the first launcher shipped under: unzipped over an older folder, it
# replaces that launcher.
FETCHER_NAME = "unwritten-launcher.exe"
FETCHER = os.path.join(REPO, "builds", "launcher", "windows", FETCHER_NAME)
WINDOWS_LAUNCHER = os.path.join(REPO, "builds", "launcher", "windows-patcher", "unwritten-launcher.exe")
# The small program is about 5 MB and the launcher about 100 MB. A "small program" bigger than this is the launcher,
# or the first launcher left over from an older build — and published as the small program it would have every
# Windows player's Start run a program that cannot find the update server.
FETCHER_LARGEST = 30_000_000


def windows_launchers():
    """The two Windows programs every Windows publish needs, proved to be the right two before anything is written."""
    for path, what in ((FETCHER, "the small program players download"), (WINDOWS_LAUNCHER, "the Windows launcher")):
        if not os.path.isfile(path):
            fail(f"No {what} at {path}. Run tools/build-launcher.sh on Windows, then publish again.", 2)
    if os.path.getsize(FETCHER) > FETCHER_LARGEST:
        fail(f"{FETCHER} is {os.path.getsize(FETCHER) / 1e6:.0f} MB — that is a launcher, not the small program "
             f"that fetches it. Run tools/build-launcher.sh on Windows, then publish again.", 2)
    if sha256(FETCHER) == sha256(WINDOWS_LAUNCHER):
        fail(f"{FETCHER} and {WINDOWS_LAUNCHER} are the same file. Run tools/build-launcher.sh, then publish again.", 2)
    if not os.path.isfile(os.path.join(os.path.dirname(FETCHER), "launcher.txt")):
        fail(f"No launcher.txt beside {FETCHER}, so the download would not know where the update server is. Set "
             f"PATCH_URL in settings.env, run tools/build-launcher.sh, then publish again.", 2)


# THE SMALL MAC PROGRAM can only be compiled on a Mac, so it reaches other computers through git
# (launcher-updater-mac/built/), and tools/build-launcher.sh makes this app from it on any computer.
MAC_FETCHER_APP = os.path.join(REPO, "builds", "launcher", "mac", "unwritten.app")
MAC_LAUNCHER_APPS = {arch: os.path.join(REPO, "builds", "launcher", "mac-patcher-" + arch, "unwritten.app")
                     for arch in ("arm64", "x64")}
# Where the Mac list keeps its copy of the small program among the game's files. Never the game's own folder name.
MAC_FETCHER_FOLDER = "unwritten-patcher.app"
MAC_PROGRAM = "Contents/MacOS/unwritten-launcher"
# Every file either Mac app may hold. Anything else inside one — a ._ or .DS_Store a copy picked up on the way from
# the Mac — is not covered by its signature, and a Mac then refuses the downloaded app as "damaged".
MAC_APP_FILES = {"Contents/Info.plist", MAC_PROGRAM, "Contents/_CodeSignature/CodeResources",
                 "Contents/Resources/launcher.txt"}
MAC_FETCHER_ID, MAC_LAUNCHER_ID = "com.unwritten.patcher", "com.unwritten.launcher"
# A Mac program built for both kinds of chip starts with this, then one entry per chip: Intel and Apple's own.
FAT_MAGIC, MAC_CHIPS = 0xCAFEBABE, {0x01000007: "x64", 0x0100000C: "arm64"}
SAFE_PART = re.compile(r"^[^/\\\x00]+$")


def app_files(app):
    """Every file inside a Mac app, as (path inside the app, path on disk), in a fixed order."""
    found = []
    for root, _, files in os.walk(app):
        for name in files:
            path = os.path.join(root, name)
            found.append((os.path.relpath(path, app).replace(os.sep, "/"), path))
    return sorted(found)


def plist_value(app, key):
    with open(os.path.join(app, "Contents", "Info.plist"), "rb") as f:
        return plistlib.load(f).get(key)


def address_in(folder):
    """PATCH_URL from a launcher.txt, or None."""
    path = os.path.join(folder, "launcher.txt")
    if not os.path.isfile(path):
        return None
    with open(path, encoding="utf-8") as f:
        for line in f:
            if line.strip().startswith("PATCH_URL="):
                return line.split("=", 1)[1].strip().rstrip("/")
    return None


def mac_chips(program):
    with open(program, "rb") as f:
        head = f.read(8 + 20 * 8)
    if len(head) < 8 or int.from_bytes(head[:4], "big") != FAT_MAGIC:
        return set()
    count = int.from_bytes(head[4:8], "big")
    return {MAC_CHIPS.get(int.from_bytes(head[8 + 20 * i:12 + 20 * i], "big")) for i in range(min(count, 8))} - {None}


def mac_launchers():
    """The Mac programs every Mac publish needs, proved to be the right ones before anything is written."""
    apps = [(MAC_FETCHER_APP, "the small Mac program players download", MAC_FETCHER_ID)] + \
           [(app, f"the Mac launcher for {arch} Macs", MAC_LAUNCHER_ID) for arch, app in MAC_LAUNCHER_APPS.items()]
    for app, what, wanted in apps:
        if not os.path.isfile(os.path.join(app, *MAC_PROGRAM.split("/"))):
            fail(f"No {what} at {app}. Run tools/build-launcher.sh, then publish again.", 2)
        extra = {p for p, _ in app_files(app)} - MAC_APP_FILES
        if extra:
            fail(f"{app} holds files its signature does not cover, which makes a Mac refuse it as damaged: "
                 f"{', '.join(sorted(extra))}. Run tools/build-launcher.sh, then publish again.", 2)
        if plist_value(app, "CFBundleIdentifier") != wanted:
            fail(f"{app} is not {what}: it says it is {plist_value(app, 'CFBundleIdentifier')!r}.", 2)
    program = os.path.join(MAC_FETCHER_APP, *MAC_PROGRAM.split("/"))
    if os.path.getsize(program) > FETCHER_LARGEST:
        fail(f"{program} is {os.path.getsize(program) / 1e6:.0f} MB — that is a launcher, not the small program.", 2)
    if mac_chips(program) != {"arm64", "x64"}:
        fail(f"{program} is not built for both kinds of Mac chip. Run tools/build-launcher.sh on a Mac, commit "
             f"launcher-updater-mac/built/, then pull and build here.", 2)
    # Every Mac app built since 2026-10-11 carries a build number; a launcher without one is taken for one handed out
    # before then, and replaced on players' Macs by the small program.
    for app, _, _ in apps:
        if not str(plist_value(app, "CFBundleVersion") or "").isdigit():
            fail(f"{app} carries no build number. Run tools/build-launcher.sh, then publish again.", 2)
    wanted = (setting("PATCH_URL") or "").rstrip("/")
    have = address_in(os.path.join(MAC_FETCHER_APP, "Contents", "Resources"))
    if have != wanted:
        fail(f"The small Mac program downloads from {have}, but PATCH_URL in settings.env is {wanted}. Run "
             f"tools/build-launcher.sh, then publish again.", 2)


def mac_entries(app, prefix=""):
    """A Mac app's files as list entries. Which files may run is decided by where they sit, not read off the disk —
    a copy brought onto Windows has no such permission to read."""
    return [{"path": prefix + rel, "size": os.path.getsize(path), "sha256": sha256(path),
             "exec": rel.startswith("Contents/MacOS/") and not rel.endswith(".txt")}
            for rel, path in app_files(app)]


def publish_mac_launchers(out):
    """The Mac launchers the small Mac program fetches. CONTENTS FIRST, THEN EACH LISTING, for the same reason as on
    Windows."""
    target = os.path.join(out, "mac")
    address = (setting("PATCH_URL") or "").rstrip("/")
    if not address:
        fail("PATCH_URL is empty in settings.env, so the launcher would not know where updates are.", 2)
    for arch, app in MAC_LAUNCHER_APPS.items():
        listing = os.path.join(target, f"patcher-{arch}.json")
        before = read_json(listing) or {}
        entries = mac_entries(app)
        for _, path in app_files(app):
            put_blob(os.path.join(target, "files"), path)
        was = {e["sha256"] for e in before.get("files", [])}
        now = {e["sha256"] for e in entries}
        previous = sorted(was - now) if was != now else before.get("previous", [])
        write_json(listing, {"address": address, "files": entries, "previous": previous})
        print(f"mac launcher ({arch}): {sum(e['size'] for e in entries) / 1e6:.0f} MB, "
              f"{'unchanged' if was == now else 'NEW'}.")


def mac_hand_over(entries, game, files_dir, on):
    """The Mac list as every Mac launcher must see it (see the top of this file). Returns the list's fields."""
    if game.startswith(MAC_FETCHER_FOLDER + "/"):
        fail(f"The Mac list would name the small program as the game ({game}). Publish the game again.")
    entries = [e for e in entries if not e["path"].startswith(MAC_FETCHER_FOLDER + "/")]
    if not on:
        return {"start": game, "files": entries}
    added = mac_entries(MAC_FETCHER_APP, MAC_FETCHER_FOLDER + "/")
    for _, path in app_files(MAC_FETCHER_APP):
        put_blob(files_dir, path)
    paths = [e["path"] for e in entries + added]
    if len({p.lower() for p in paths}) != len(paths) or not all(all(SAFE_PART.match(part) and part not in (".", "..")
                                                                    for part in p.split("/")) for p in paths):
        fail("The Mac list would name the same file twice, or a file outside the game folder.")
    fetcher = os.path.join(MAC_FETCHER_FOLDER, *MAC_PROGRAM.split("/")).replace(os.sep, "/")
    return {"start": fetcher, "play": fetcher, "game": game,
            "fetcher": int(plist_value(MAC_FETCHER_APP, "CFBundleVersion")), "files": entries + added}


def put_blob(files_dir, path):
    """Store a file's contents under its fingerprint; returns (fingerprint, size)."""
    os.makedirs(files_dir, exist_ok=True)
    digest = sha256(path)
    blob = os.path.join(files_dir, digest)
    if not os.path.exists(blob):
        shutil.copyfile(path, blob + ".part")
        os.replace(blob + ".part", blob)
    return digest, os.path.getsize(path)


def read_json(path):
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def write_json(path, value):
    with open(path + ".part", "w", encoding="utf-8") as f:
        json.dump(value, f, indent=1)
    os.replace(path + ".part", path)


def publish_windows_launcher(out):
    """The launcher the small program fetches. ITS CONTENTS FIRST, THEN ITS LISTING: the small program downloads
    whatever the listing names the moment it changes."""
    target = os.path.join(out, "windows")
    listing = os.path.join(target, "patcher.json")
    before = read_json(listing) or {}
    digest, size = put_blob(os.path.join(target, "files"), WINDOWS_LAUNCHER)
    previous = before.get("sha256") if before.get("sha256") != digest else before.get("previous")
    address = (setting("PATCH_URL") or "").rstrip("/")
    if not address:
        fail("PATCH_URL is empty in settings.env, so the launcher would not know where updates are.", 2)
    write_json(listing, {"file": "unwritten-launcher.exe", "size": size, "sha256": digest,
                         "address": address, "previous": previous})
    print(f"windows launcher: {size / 1e6:.0f} MB, {'unchanged' if before.get('sha256') == digest else 'NEW'}.")


def launcher_blobs(target):
    """The fingerprints the launcher's listings still need kept: the launcher, and the one before it — on Windows one
    file, on a Mac every file of each chip's launcher."""
    listing = read_json(os.path.join(target, "patcher.json")) or {}
    keep = {d for d in (listing.get("sha256"), listing.get("previous")) if d}
    for arch in MAC_LAUNCHER_APPS:
        listing = read_json(os.path.join(target, f"patcher-{arch}.json")) or {}
        keep |= {e["sha256"] for e in listing.get("files", [])} | set(listing.get("previous", []))
    return keep


def hand_over(entries, start, files_dir, on):
    """The Windows list as the first launcher must see it (see the top of this file). Returns the list's fields."""
    entries = [e for e in entries if e["path"] != FETCHER_NAME]
    if not on:
        return {"start": start, "files": entries}
    digest, size = put_blob(files_dir, FETCHER)
    entries.append({"path": FETCHER_NAME, "size": size, "sha256": digest, "exec": True})
    return {"start": FETCHER_NAME, "play": start, "files": entries}


def publish_mac_launcher_only(out, on):
    """The Mac half of a launcher-only publish: the Mac list already live is kept file for file."""
    target = os.path.join(out, "mac")
    manifest_path = os.path.join(target, "manifest.json")
    live = read_json(manifest_path)
    if live is None:
        fail(f"No Mac list at {manifest_path} yet — publish the Mac game once first.", 2)
    game = live.get("game") or live["start"]
    publish_mac_launchers(out)
    fields = mac_hand_over(live["files"], game, os.path.join(target, "files"), on)
    write_json(manifest_path, {"version": live.get("version"), **fields})
    print(f"mac list: {len(fields['files'])} files, hand-over {'on' if on else 'OFF'}; the game's files untouched.")


def publish_launcher_only(out, on):
    """A launcher change with no game publish: the Windows list already live is kept file for file."""
    target = os.path.join(out, "windows")
    manifest_path = os.path.join(target, "manifest.json")
    live = read_json(manifest_path)
    if live is None:
        fail(f"No Windows list at {manifest_path} yet — publish the game once first.", 2)
    start = live.get("play") or live["start"]
    if start == FETCHER_NAME:
        fail(f"{manifest_path} names the small program as the game. Publish the game again.")
    publish_windows_launcher(out)
    fields = hand_over(live["files"], start, os.path.join(target, "files"), on)
    write_json(manifest_path, {"version": live.get("version"), **fields})
    print(f"windows list: {len(fields['files'])} files, hand-over {'on' if on else 'OFF'}; the game's files untouched.")


def publish_platform(platform, build, out, version, hands_over=True):
    build = os.path.abspath(build)
    if not os.path.isdir(build):
        fail(f"No {platform} build at {build}.", 2)

    start = start_program(build, platform)
    target = os.path.join(out, platform)
    files_dir = os.path.join(target, "files")
    os.makedirs(files_dir, exist_ok=True)

    manifest_path = os.path.join(target, "manifest.json")
    previous = set()
    if os.path.exists(manifest_path):
        with open(manifest_path, encoding="utf-8") as f:
            previous = {e["sha256"] for e in json.load(f)["files"]}

    entries, uploaded, uploaded_bytes = [], 0, 0
    for path in shipped_files(build):
        digest = sha256(path)
        rel = os.path.relpath(path, build).replace(os.sep, "/")
        entries.append({
            "path": rel,
            "size": os.path.getsize(path),
            "sha256": digest,
            "exec": os.access(path, os.X_OK),
        })
        blob = os.path.join(files_dir, digest)
        if not os.path.exists(blob):
            shutil.copyfile(path, blob + ".part")
            os.replace(blob + ".part", blob)
            uploaded += 1
            uploaded_bytes += os.path.getsize(path)

    if not any(e["path"] == start for e in entries):
        fail(f"{build}: the program to start, {start}, is not among the files.")

    fields = {"start": start, "files": entries}
    if platform == "mac":
        if any(e["path"].startswith(MAC_FETCHER_FOLDER + "/") for e in entries):
            fail(f"{build} holds a folder named {MAC_FETCHER_FOLDER}, the name the small program is listed under.")
        fields = mac_hand_over(entries, start, files_dir, hands_over)
        entries = fields["files"]
    if platform == "windows":
        if any(e["path"] == FETCHER_NAME for e in entries):
            fail(f"{build} holds a file named {FETCHER_NAME}, the name the small program is listed under.")
        fields = hand_over(entries, start, files_dir, hands_over)
        entries = fields["files"]

    # The version these files ARE, written into this platform's own list: version.txt is shared by every
    # platform, so a launcher showing it would claim a platform left out of a publish was up to date.
    write_json(manifest_path, {"version": version, **fields})

    keep = previous | {e["sha256"] for e in entries} | launcher_blobs(target)
    removed = 0
    for name in os.listdir(files_dir):
        if name not in keep:
            os.remove(os.path.join(files_dir, name))
            removed += 1

    total = sum(e["size"] for e in entries)
    print(f"{platform}: {len(entries)} files ({total / 1e6:.0f} MB); {uploaded} new or changed "
          f"({uploaded_bytes / 1e6:.1f} MB); {removed} no longer needed, removed.")


def publish_site(out):
    """The website a browser gets at the update folder's address, and everything it reads."""
    shutil.copyfile(os.path.join(REPO, "website", "index.html"), os.path.join(out, "index.html"))
    shutil.copyfile(os.path.join(REPO, "wiki", "favicon.ico"), os.path.join(out, "favicon.ico"))

    # The colour sheet points at the game's font files inside the Unity project, which the site does not ship; the
    # fonts it names are copied beside it and the sheet is pointed at those copies. Which fonts, read from the sheet.
    with open(os.path.join(REPO, "prototypes", "ui-tokens.css"), encoding="utf-8") as f:
        css = f.read()
    os.makedirs(os.path.join(out, "fonts"), exist_ok=True)
    for src in set(re.findall(r'url\("([^"]+)"\)', css)):
        name = os.path.basename(src)
        shutil.copyfile(os.path.normpath(os.path.join(REPO, "prototypes", src)), os.path.join(out, "fonts", name))
        css = css.replace(f'url("{src}")', f'url("fonts/{name}")')
    with open(os.path.join(out, "ui-tokens.css"), "w", encoding="utf-8") as f:
        f.write(css)

    # The wiki's pages and pictures, replaced whole so a page deleted from the wiki leaves the site too.
    for folder in ("content", "assets"):
        target = os.path.join(out, folder)
        if os.path.isdir(target):
            shutil.rmtree(target)
        shutil.copytree(os.path.join(REPO, "wiki", folder), target)

    # The character server the status page reads, as players reach it — the rule tools/build-game.sh writes into the
    # game: the public address once there is one, this machine's until then.
    char_server = setting("PUBLIC_CHAR_SERVER_URL") or setting("CHAR_SERVER_URL")
    if not char_server:
        print("\033[31mNo PUBLIC_CHAR_SERVER_URL or CHAR_SERVER_URL in settings.env — the website's server status "
              "will say it cannot reach the character server.\033[0m", file=sys.stderr)
    with open(os.path.join(out, "site.json"), "w", encoding="utf-8") as f:
        json.dump({"charServerUrl": char_server}, f)


def publish_launchers(out, windows, mac):
    """What the Download page offers. A player downloads the small program, which fetches the launcher, which
    fetches the game. `windows` and `mac` are false when this publish left that platform out, and its download
    already there is then left as it is — so the Mac download always holds the same small program as the Mac list."""
    os.makedirs(os.path.join(out, "download"), exist_ok=True)
    if windows:
        # ZIPPED WITH launcher.txt, because that file beside the program is how it knows which server to download
        # from. Both were proved to be there, and to be the small program, before this publish wrote anything.
        folder = os.path.dirname(FETCHER)
        target = os.path.join(out, "download", "unwritten-windows.zip")
        with zipfile.ZipFile(target + ".part", "w", zipfile.ZIP_DEFLATED) as z:
            for name in (FETCHER_NAME, "launcher.txt"):
                z.write(os.path.join(folder, name), "unwritten/" + name)
        os.replace(target + ".part", target)

    # AND THE SMALL MAC PROGRAM, under both Mac download names: one app runs on both kinds of chip. A .app is a
    # folder, so it travels zipped — and the zip carries each file's "may run" permission itself, because a zip made
    # on Windows has none to copy and a Mac will not start a program that lacks it.
    for arch in ("arm64", "x64") if mac else ():
        app = MAC_FETCHER_APP
        target = os.path.join(out, "download", "unwritten-mac-" + arch + ".zip")
        with zipfile.ZipFile(target + ".part", "w", zipfile.ZIP_DEFLATED) as z:
            for root, _, files in os.walk(app):
                for name in sorted(files):
                    path = os.path.join(root, name)
                    rel = os.path.relpath(path, os.path.dirname(app)).replace(os.sep, "/")
                    info = zipfile.ZipInfo.from_file(path, rel)
                    runs = os.path.basename(os.path.dirname(path)) == "MacOS" and name != "launcher.txt"
                    info.external_attr = ((0o100755 if runs else 0o100644) & 0xFFFF) << 16
                    info.create_system = 3
                    info.compress_type = zipfile.ZIP_DEFLATED
                    with open(path, "rb") as f:
                        z.writestr(info, f.read())
        os.replace(target + ".part", target)


def main():
    now = datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=8)))
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--mac", help="the Mac player build folder (holds the .app)")
    ap.add_argument("--windows", help="the Windows player build folder (holds the .exe)")
    ap.add_argument("--out", default=os.path.join(REPO, "builds", "patch"))
    ap.add_argument("--news", default=os.path.join(REPO, "launcher", "news.json"))
    ap.add_argument("--version", default=now.strftime("%Y.%m.%d-%H%M"))
    ap.add_argument("--site-only", action="store_true",
                    help="refresh the website, its wiki pages and news.json only — no build, no new version")
    ap.add_argument("--launcher-only", action="store_true",
                    help="publish the Windows launcher, the downloads, the website and news — no build, no new version")
    ap.add_argument("--without-hand-over", action="store_true",
                    help="list the game as the first launcher's Start again, withdrawing the small program from it")
    args = ap.parse_args()

    with open(args.news, encoding="utf-8") as f:
        news = json.load(f)
    if not isinstance(news, list) or any(set(n) != {"date", "title", "text"} for n in news):
        fail(f"{args.news}: must be a list of {{\"date\", \"title\", \"text\"}}.")

    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)

    if args.site_only:
        shutil.copyfile(args.news, os.path.join(out, "news.json"))
        publish_site(out)
        print(f"\033[32mPublished the website\033[0m to {out}")
        return

    if args.launcher_only:
        windows_launchers()
        mac = os.path.isdir(MAC_FETCHER_APP)
        if mac:
            mac_launchers()
        publish_launcher_only(out, not args.without_hand_over)
        if mac:
            publish_mac_launcher_only(out, not args.without_hand_over)
        else:
            print(f"\033[33mMac skipped: no small Mac program at {MAC_FETCHER_APP}. The Mac list and downloads are "
                  f"left as they are.\033[0m", file=sys.stderr)
        shutil.copyfile(args.news, os.path.join(out, "news.json"))
        publish_site(out)
        publish_launchers(out, windows=True, mac=mac)
        print(f"\033[32mPublished the launcher\033[0m to {out}")
        return

    # builds/client holds whatever tools/build-game.sh made on THIS computer, so with no flag it is published as
    # this computer's platform. It used to be published as a Mac build everywhere, which on Windows fails.
    if not args.mac and not args.windows:
        if sys.platform.startswith("win"):
            args.windows = os.path.join(REPO, "builds", "client")
        else:
            args.mac = os.path.join(REPO, "builds", "client")
    version = args.version.strip()
    if not version or any(c.isspace() for c in version):
        fail("The version must be one word with no spaces.", 2)

    # EVERYTHING THAT CAN STOP THIS PUBLISH IS CHECKED BEFORE ANYTHING IS STAMPED OR WRITTEN. One platform's list
    # written and the other's refused leaves the first platform's players downloading files the character server
    # then turns away, until somebody publishes again.
    for platform, build in (("mac", args.mac), ("windows", args.windows)):
        if build and not os.path.isdir(build):
            fail(f"No {platform} build at {os.path.abspath(build)}.", 2)
    if args.windows:
        windows_launchers()
    if args.mac:
        mac_launchers()

    for platform, build in (("mac", args.mac), ("windows", args.windows)):
        if not build:
            continue
        stamp_version(build, version)
        if platform == "mac":
            resign_mac(build)
            publish_mac_launchers(out)
        if platform == "windows":
            publish_windows_launcher(out)
        publish_platform(platform, build, out, version, hands_over=not args.without_hand_over)

    shutil.copyfile(args.news, os.path.join(out, "news.json"))
    publish_site(out)
    publish_launchers(out, windows=bool(args.windows), mac=bool(args.mac))
    with open(os.path.join(out, "version.txt.part"), "w", encoding="utf-8") as f:
        f.write(version + "\n")
    os.replace(os.path.join(out, "version.txt.part"), os.path.join(out, "version.txt"))
    print(f"\033[32mPublished version {version}\033[0m to {out}")


if __name__ == "__main__":
    main()
