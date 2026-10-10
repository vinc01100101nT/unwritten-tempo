#!/usr/bin/env python3
"""Play the game client with no window, one text command at a time.

The client build listens for commands only when started with -agent-port (see AgentLink.cs in the game).
This file starts that client, sends it commands, and stops it. The MCP server beside it uses the same three
functions, so a terminal and an AI player drive the game identically.

USAGE
  tools/agent-play/game_link.py launch            start the client with no window (logs/agent-client.log)
  tools/agent-play/game_link.py "look"            send one command, print the answer
  tools/agent-play/game_link.py "go d1; wait 2"   several commands on one line, one answer at the end
  tools/agent-play/game_link.py quit              stop the client this script started

A second player on the same machine: put AGENT_PORT=7791 (or any free port) in front of every call.

It needs the four services already running (database, sign-in, character server, game server) — see
docs/2-systems/networking/starting-the-network.md. It never starts or stops those.
"""
import os
import socket
import subprocess
import sys
import time

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.join(REPO, "tools"))
from settings_ask import setting  # noqa: E402
from platform_ask import ask  # noqa: E402
# The client build made for THIS computer — platform.sh is the one place that knows which computer that is.
CLIENT = ask("game-program", "client") or ""
PORT = int(os.environ.get("AGENT_PORT", "7790"))
END = "<<END>>"


def _log(port):
    return os.path.join(REPO, "logs", "agent-client-%d.log" % port if port != 7790 else "agent-client.log")


def _pid_file(port):
    # One per port, so two players on one machine never stop each other's game.
    return os.path.join(REPO, "logs", "agent-client-%d.pid" % port if port != 7790 else "agent-client.pid")


def _listening(port):
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=0.5):
            return True
    except OSError:
        return False


def launch(port=PORT):
    if _listening(port):
        return "the client is already running on port %d" % port
    if not os.path.exists(CLIENT):
        return "no client build at %s — run tools/build-game.sh client" % CLIENT
    LOG = _log(port)
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    env = dict(os.environ)
    if not env.get("CHAR_SERVER_URL"):
        env["CHAR_SERVER_URL"] = setting("CHAR_SERVER_URL")
    if not env["CHAR_SERVER_URL"]:
        return "CHAR_SERVER_URL is not set in settings.env — the client would have no character server to ask"
    # Detached from this terminal, so closing it does not take the game with it: a new session on a Mac or Linux, a
    # new process group with no console window on Windows.
    detach = ({"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_NO_WINDOW}
              if os.name == "nt" else {"start_new_session": True})
    proc = subprocess.Popen(
        [CLIENT, "-batchmode", "-nographics", "-agent-port", str(port), "-logFile", LOG],
        env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, **detach)
    with open(_pid_file(port), "w") as f:
        f.write(str(proc.pid))
    for _ in range(120):
        if proc.poll() is not None:
            return "the client exited during start-up (code %s) — read %s" % (proc.returncode, LOG)
        if _listening(port):
            return "client running (pid %d, port %d). next: login <email> <password>" % (proc.pid, port)
        time.sleep(0.5)
    return "the client started (pid %d) but is not answering on port %d after 60s — read %s" % (proc.pid, port, LOG)


def _is_our_client(pid, port):
    # A pid can be handed to a new process once the client is gone; only a process still running the game with
    # THIS port on its command line is ours to stop.
    cmd = ask("process-command", str(pid)) or ""
    return "-agent-port %d" % port in cmd


def quit_client(port=PORT):
    # Only ever the process this script started: the pid it wrote down, checked to still be that game.
    PID_FILE = _pid_file(port)
    try:
        pid = int(open(PID_FILE).read().strip())
    except (OSError, ValueError):
        return "no client started by this script"
    if not _is_our_client(pid, port):
        os.remove(PID_FILE)
        return "the client (pid %d) was already gone" % pid
    # Through platform.sh rather than os.kill: on Windows os.kill(pid, 0) does not ask whether a process is alive, it
    # ENDS it, so the same call cannot be used to stop and to check on both computers.
    ask("stop-pid", str(pid))
    for _ in range(20):
        if not _is_our_client(pid, port):
            break
        time.sleep(0.25)
    else:
        os.remove(PID_FILE)
        return "asked the client (pid %d) to stop, and it is still running after 5s" % pid
    os.remove(PID_FILE)
    return "client stopped (pid %d)" % pid


def send(command, port=PORT, timeout=None):
    try:
        sock = socket.create_connection(("127.0.0.1", port), timeout=3)
    except OSError:
        return "the client is not running — launch it first"
    with sock:
        # Each command on the line may wait up to 120 s for its outcome (the client's own ceiling), so a line is
        # given that long for every one of them before the socket gives up.
        sock.settimeout(timeout if timeout else 130 * (1 + command.count(";")))
        sock.sendall((command.strip().replace("\n", " ") + "\n").encode("utf-8"))
        buf = b""
        while True:
            chunk = sock.recv(65536)
            if not chunk:
                break
            buf += chunk
            text = buf.decode("utf-8", errors="replace")
            marker = text.find("\n" + END + "\n")
            if marker >= 0:
                return text[:marker]
        return buf.decode("utf-8", errors="replace") or "the client hung up without answering"


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    # The game answers in UTF-8; a Windows terminal otherwise prints every dash in it as a question mark.
    sys.stdout.reconfigure(encoding="utf-8")
    arg = " ".join(argv[1:])
    if arg == "launch":
        print(launch())
    elif arg == "quit":
        print(quit_client())
    else:
        print(send(arg))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
