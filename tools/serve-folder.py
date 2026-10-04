#!/usr/bin/env python3
"""Serve one folder over plain HTTP on every address of this computer, and say only what is TRUE about where.

USAGE:  serve-folder.py <folder> <port> [SETTING ...]

Each SETTING names an environment variable holding the address other people are handed for this server
(PATCH_URL). It is tried from this computer, and reported as answering or as not — never printed as an address
to open unless it answered.

THE SAME RULE AS THE OTHER THREE SERVERS (the sign-in service, the character server, the map server): no address
is printed as reachable unless a connection to it was just made, and the report is made again whenever this
computer's own addresses change — a network adapter coming up later (Tailscale, a new Wi-Fi) is said when it
happens, not left looking down for the rest of the run.
"""
import functools
import http.server
import os
import re
import socket
import subprocess
import sys
import threading
import time
from urllib.parse import urlsplit

RED, PLAIN = "", ""


def colour_works():
    """Red only where it will show as red. A Windows console window prints the colour code literally ("←[31m")
    unless the program switches it into the mode that reads them; a pipe or a log file would keep the codes as
    junk, so neither gets them."""
    if not sys.stdout.isatty():
        return False
    if sys.platform != "win32":
        return True
    import ctypes
    kernel32 = ctypes.windll.kernel32
    handle = kernel32.GetStdHandle(-11)   # this program's output window
    mode = ctypes.c_uint32()
    if not kernel32.GetConsoleMode(handle, ctypes.byref(mode)):
        return False
    return bool(kernel32.SetConsoleMode(handle, mode.value | 0x0004))   # 0x0004: read colour codes
RECHECK_SECONDS = 10
PROBE_SECONDS = 1.0


def usable(address):
    # 169.254.x.x is the address an adapter gives ITSELF when it has no network to get one from: nobody can reach
    # this computer there, so listing it would be listing a door with no road to it.
    return not address.startswith("169.254.")


def address_set():
    """This computer's IPv4 addresses, cheaply — asked every few seconds, so on Windows it must not start a
    program. Windows answers its own name with every address its adapters can send from."""
    if sys.platform == "win32":
        found = {a[4][0] for a in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET)}
        found.add("127.0.0.1")
        return {a for a in found if usable(a)}
    return set(named_addresses())


def named_addresses():
    """{address: adapter name} — asked only when the set above changes, because on Windows it costs most of a
    second."""
    found = {}
    try:
        if sys.platform == "win32":
            out = subprocess.run(
                ["powershell.exe", "-NoProfile", "-Command",
                 "Get-NetIPAddress -AddressFamily IPv4 | Where-Object AddressState -eq 'Preferred' | "
                 "ForEach-Object { $_.IPAddress + \"`t\" + $_.InterfaceAlias }"],
                capture_output=True, text=True, timeout=15).stdout
            for line in out.splitlines():
                address, _, name = line.strip().partition("\t")
                if address:
                    found[address] = name
        elif sys.platform == "darwin":
            adapter = ""
            for line in subprocess.run(["ifconfig"], capture_output=True, text=True, timeout=15).stdout.splitlines():
                if line and not line[0].isspace():
                    adapter = line.split(":", 1)[0]
                m = re.match(r"\s+inet (\d+\.\d+\.\d+\.\d+)", line)
                if m:
                    found[m.group(1)] = adapter
        else:
            for line in subprocess.run(["ip", "-o", "-4", "addr", "show"],
                                       capture_output=True, text=True, timeout=15).stdout.splitlines():
                m = re.match(r"\d+:\s+(\S+)\s+inet\s+(\d+\.\d+\.\d+\.\d+)/", line)
                if m:
                    found[m.group(2)] = m.group(1)
    except (OSError, subprocess.SubprocessError) as e:
        print(f"{RED}could not list this computer's addresses — {e}{PLAIN}", flush=True)
    return {a: n for a, n in found.items() if usable(a)}


def answers(host, port):
    try:
        with socket.create_connection((host, port), timeout=PROBE_SECONDS):
            return True
    except OSError:
        return False


def given_address(url):
    """(host, port) a handed-out URL is dialled at."""
    parts = urlsplit(url if "://" in url else "http://" + url)
    return parts.hostname or "", parts.port or (443 if parts.scheme == "https" else 80)


def report(port, settings):
    named = named_addresses()
    answering = [(a, n) for a, n in sorted(named.items(), key=lambda kv: socket.inet_aton(kv[0]))
                 if answers(a, port)]
    if answering:
        print("answers at (tested from this computer):", flush=True)
        width = max(len(f"http://{a}:{port}") for a, _ in answering)
        for a, n in answering:
            print(f"  {f'http://{a}:{port}':<{width}}   {n}", flush=True)
    else:
        print(f"{RED}does not answer at any address of this computer.{PLAIN}", flush=True)

    for setting in settings:
        url = os.environ.get(setting, "").strip()
        if not url:
            continue
        host, url_port = given_address(url)
        if answers(host, url_port):
            print(f"{setting} {url} answers — that is the address players are given.", flush=True)
        else:
            print(f"{RED}{setting} {url} does not answer — anyone given that address cannot reach this server."
                  f"{PLAIN}", flush=True)


def main():
    if len(sys.argv) < 3:
        print(__doc__, file=sys.stderr)
        return 2
    folder, port, settings = sys.argv[1], int(sys.argv[2]), sys.argv[3:]
    # Windows writes a pipe or a log file in its old code page, which turns every dash in these lines into "?".
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    global RED, PLAIN
    if colour_works():
        RED, PLAIN = "\033[31m", "\033[0m"

    handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=folder)
    try:
        server = http.server.ThreadingHTTPServer(("0.0.0.0", port), handler)
    except OSError as e:
        print(f"{RED}cannot serve on port {port} — {e}{PLAIN}", file=sys.stderr, flush=True)
        return 2

    # SERVING FIRST, TESTED SECOND. Tested before this thread is accepting, the test connections would queue
    # behind a five-deep backlog and the sixth would be refused — reported as an address that does not answer.
    threading.Thread(target=server.serve_forever, daemon=True).start()
    print(f"serving {folder} on port {port}. Ctrl+C stops it.", flush=True)
    report(port, settings)

    seen = address_set()
    try:
        while True:
            time.sleep(RECHECK_SECONDS)
            now = address_set()
            if now != seen:
                seen = now
                print("this computer's addresses changed:", flush=True)
                report(port, settings)
    except KeyboardInterrupt:
        server.shutdown()
    return 0


if __name__ == "__main__":
    sys.exit(main())
