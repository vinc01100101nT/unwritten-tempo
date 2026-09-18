#!/usr/bin/env python3
"""MCP server (stdio) that lets an AI play the game client with no window.

Three tools: game_launch, game (one text command), game_quit. All the work is in game_link.py; this file only
speaks the MCP wire format — newline-delimited JSON-RPC 2.0 on stdin/stdout — with the standard library.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import game_link  # noqa: E402

COMMANDS = (
    "One line per call; several commands may share it, separated by ';' (a say takes the rest of the line). "
    "The answer is one line per command saying how it ended, then the screen as text once, then what happened "
    "since the last call. Commands: look (the whole screen; other answers leave out sections that have not changed) | "
    "login <email> <password> | create <name> | play <name|number> | move <x> <z> | go <id> | attack <m#> | "
    "auto on|off [secs] | stop | cast <slot|R|name> [id|me|x z] | pickup <g#> | talk <n#> | use <bag#|name> | "
    "equip <bag#> | unequip <slot> | stat <STR|AGI|VIT|INT|DEX|LUK> | bag | gear | stats | say <text or /command> | "
    "revive | wait <secs> | help. Ids (m=mob p=player n=npc g=floor item d=door) come from the screen and stay the "
    "same while you are in the map. Walking and fighting end when they finish, you die, or your HP drops under 30%; "
    "a wait ends early when you are hit; every one says why it ended."
)

TOOLS = [
    {"name": "game_launch", "description": "Start the game client with no window (needs the game's servers running).",
     "inputSchema": {"type": "object", "properties": {}}},
    {"name": "game", "description": COMMANDS,
     "inputSchema": {"type": "object", "properties": {"command": {"type": "string"}}, "required": ["command"]}},
    {"name": "game_quit", "description": "Stop the game client that game_launch started.",
     "inputSchema": {"type": "object", "properties": {}}},
]


def handle(msg):
    method = msg.get("method")
    if method == "initialize":
        version = (msg.get("params") or {}).get("protocolVersion", "2024-11-05")
        return {"protocolVersion": version, "capabilities": {"tools": {}},
                "serverInfo": {"name": "unwritten-game", "version": "1.0"}}
    if method == "ping":
        return {}
    if method == "tools/list":
        return {"tools": TOOLS}
    if method == "tools/call":
        params = msg.get("params") or {}
        name = params.get("name")
        args = params.get("arguments") or {}
        if name == "game_launch":
            text = game_link.launch()
        elif name == "game_quit":
            text = game_link.quit_client()
        elif name == "game":
            text = game_link.send(args.get("command", "look"))
        else:
            return {"content": [{"type": "text", "text": "unknown tool " + str(name)}], "isError": True}
        return {"content": [{"type": "text", "text": text}]}
    raise LookupError(method)


def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
        except ValueError:
            continue
        if "id" not in msg:
            continue  # a notification — nothing to answer
        reply = {"jsonrpc": "2.0", "id": msg["id"]}
        try:
            reply["result"] = handle(msg)
        except LookupError:
            reply["error"] = {"code": -32601, "message": "method not found"}
        except Exception as e:  # keep the server alive whatever one call does
            reply["error"] = {"code": -32603, "message": str(e)}
        sys.stdout.write(json.dumps(reply) + "\n")
        sys.stdout.flush()


if __name__ == "__main__":
    main()
