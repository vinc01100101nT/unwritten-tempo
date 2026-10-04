#!/usr/bin/env python3
"""Publish a game update: stamp one version into the player builds, and lay them out as the folder the launcher
downloads from.

WHAT IT WRITES, into --out (default builds/patch):

  version.txt                  the version every player's copy must match. THE one owner of that number: the
                               character server reads this file (its PATCH_DIR setting points at this folder) and
                               turns away any copy that says otherwise.
  news.json                    the launcher's news box, copied from launcher/news.json.
  <platform>/manifest.json     every game file: its path, size, fingerprint, and whether it is a program.
  <platform>/files/<sha256>    each file's contents, under its fingerprint.
  index.html + its files       the website (website/index.html), so opening this folder's address in a browser
                               shows the site and its Download page offers the launcher. Beside it: ui-tokens.css
                               and fonts/ (the game's own colours and fonts, from prototypes/ui-tokens.css),
                               content/ and assets/ (the wiki's pages and pictures, copied from wiki/), favicon.ico,
                               and site.json (the character server's address the status page reads, from
                               settings.env).
  download/unwritten-windows.zip   the Windows launcher with the launcher.txt naming this server, zipped, from
                               builds/launcher/windows when one has been built. One download: the launcher fetches
                               the game itself on its first run.

THE SAME VERSION GOES INTO THE GAME. Before listing a build, its settings file (StreamingAssets/server.txt) gets
GAME_VERSION=<version>, which the game sends to the character server with every request. So a platform left out
of a publish keeps its old version, and its players are asked to update until it is published too.

version.txt is written LAST, after every manifest, so the server never demands a version the launcher cannot yet
hand out. A file is uploaded once and kept while the newest or the previous manifest still names it, so a player
halfway through downloading the previous update is not left pointing at nothing.

--site-only refreshes the website, its wiki pages and news.json and touches nothing else — no build is read, no
version is written — so a wiki edit reaches the site without publishing the game again.

USAGE:   tools/publish-update.py                        # builds/client, as the platform of THIS computer
         tools/publish-update.py --site-only            # the website, wiki pages and news only
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


def publish_platform(platform, build, out):
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

    tmp = manifest_path + ".part"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump({"start": start, "files": entries}, f, indent=1)
    os.replace(tmp, manifest_path)

    keep = previous | {e["sha256"] for e in entries}
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


def publish_launchers(out):
    """The launcher the Download page offers. Players download only the launcher; its first run downloads the game
    from this same folder."""
    # ZIPPED WITH launcher.txt, because that file beside the program is how the launcher knows which server to
    # download from — the program alone would look for updates on the player's own computer and find nothing.
    folder = os.path.join(REPO, "builds", "launcher", "windows")
    if not os.path.isfile(os.path.join(folder, "unwritten-launcher.exe")):
        print("\033[33mNo Windows launcher at builds/launcher/windows — the Download page's button will find "
              "nothing. Run tools/build-launcher.sh, then publish again.\033[0m", file=sys.stderr)
        return
    if not os.path.isfile(os.path.join(folder, "launcher.txt")):
        print("\033[33mThe Windows launcher has no launcher.txt, so it will look for updates on the player's own "
              "computer. Set PATCH_URL in settings.env, run tools/build-launcher.sh, then publish again.\033[0m",
              file=sys.stderr)
    os.makedirs(os.path.join(out, "download"), exist_ok=True)
    target = os.path.join(out, "download", "unwritten-windows.zip")
    with zipfile.ZipFile(target + ".part", "w", zipfile.ZIP_DEFLATED) as z:
        for name in ("unwritten-launcher.exe", "launcher.txt"):
            path = os.path.join(folder, name)
            if os.path.isfile(path):
                z.write(path, "unwritten/" + name)
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

    for platform, build in (("mac", args.mac), ("windows", args.windows)):
        if not build:
            continue
        stamp_version(build, version)
        if platform == "mac":
            resign_mac(build)
        publish_platform(platform, build, out)

    shutil.copyfile(args.news, os.path.join(out, "news.json"))
    publish_site(out)
    publish_launchers(out)
    with open(os.path.join(out, "version.txt.part"), "w", encoding="utf-8") as f:
        f.write(version + "\n")
    os.replace(os.path.join(out, "version.txt.part"), os.path.join(out, "version.txt"))
    print(f"\033[32mPublished version {version}\033[0m to {out}")


if __name__ == "__main__":
    main()
