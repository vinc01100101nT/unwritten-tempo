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

THE FIRST LAUNCHER IS HANDED OVER THROUGH THE WINDOWS LIST. That launcher (in players' hands since 2026-10-04) cannot
update itself, keeps the game beside itself, and on Start runs the list's "start" and nothing else. So the Windows
list names the small program as one more file among the game's and as "start", and the game's own program as "play",
which every later launcher runs. The list stays a superset of the game, so the first launcher deletes nothing; its
Start runs the small program, which fetches the launcher, which moves the game into the player's own folder.
--without-hand-over publishes the list the old way ("start" is the game, no "play", no extra file), which withdraws
the small program from every first launcher in one publish.

THE SAME VERSION GOES INTO THE GAME. Before listing a build, its settings file (StreamingAssets/server.txt) gets
GAME_VERSION=<version>, which the game sends to the character server with every request. So a platform left out
of a publish keeps its old version, and its players are asked to update until it is published too.

version.txt is written LAST, after every manifest, so the server never demands a version the launcher cannot yet
hand out. A file is uploaded once and kept while the newest or the previous manifest still names it, so a player
halfway through downloading the previous update is not left pointing at nothing.

--site-only refreshes the website, its wiki pages and news.json and touches nothing else — no build is read, no
version is written — so a wiki edit reaches the site without publishing the game again.

--launcher-only publishes the Windows launcher, the small program and the downloads, with the website and news — no
game build is read and no version is written, so a launcher change reaches players without publishing the game again.
The Windows list already published is kept file for file; only the small program's entry in it is replaced.

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
    """The fingerprints the launcher's listing still needs kept: the launcher, and the one before it."""
    listing = read_json(os.path.join(target, "patcher.json")) or {}
    return {d for d in (listing.get("sha256"), listing.get("previous")) if d}


def hand_over(entries, start, files_dir, on):
    """The Windows list as the first launcher must see it (see the top of this file). Returns the list's fields."""
    entries = [e for e in entries if e["path"] != FETCHER_NAME]
    if not on:
        return {"start": start, "files": entries}
    digest, size = put_blob(files_dir, FETCHER)
    entries.append({"path": FETCHER_NAME, "size": size, "sha256": digest, "exec": True})
    return {"start": FETCHER_NAME, "play": start, "files": entries}


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


def publish_launchers(out, windows):
    """What the Download page offers. A Windows player downloads the small program, which fetches the launcher,
    which fetches the game; a Mac player downloads the launcher itself. `windows` is false when this publish left
    Windows out, and the Windows download already there is then left as it is."""
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

    # AND THE TWO MAC LAUNCHERS, one per kind of Mac chip. A .app is a folder, so it travels zipped — and the zip
    # carries each file's "may run" permission itself, because a zip made on Windows has none to copy and a Mac will
    # not start a program that lacks it.
    for arch in ("arm64", "x64"):
        app = os.path.join(REPO, "builds", "launcher", "mac-" + arch, "unwritten.app")
        if not os.path.isdir(app):
            continue
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
        publish_launcher_only(out, not args.without_hand_over)
        shutil.copyfile(args.news, os.path.join(out, "news.json"))
        publish_site(out)
        publish_launchers(out, windows=True)
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

    for platform, build in (("mac", args.mac), ("windows", args.windows)):
        if not build:
            continue
        stamp_version(build, version)
        if platform == "mac":
            resign_mac(build)
        if platform == "windows":
            publish_windows_launcher(out)
        publish_platform(platform, build, out, version, hands_over=not args.without_hand_over)

    shutil.copyfile(args.news, os.path.join(out, "news.json"))
    publish_site(out)
    publish_launchers(out, windows=bool(args.windows))
    with open(os.path.join(out, "version.txt.part"), "w", encoding="utf-8") as f:
        f.write(version + "\n")
    os.replace(os.path.join(out, "version.txt.part"), os.path.join(out, "version.txt"))
    print(f"\033[32mPublished version {version}\033[0m to {out}")


if __name__ == "__main__":
    main()
