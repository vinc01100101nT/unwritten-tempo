#!/usr/bin/env python3
"""
Extract the full conversation from a ChatGPT *share* link (chatgpt.com/share/...).

Why this exists: share pages are JavaScript-rendered, so a normal fetch / the
WebFetch tool only sees the empty app shell. But the page embeds the whole
conversation as hydration data — a React-Router turbo-stream (or, on some pages,
a Next.js flight payload) using an index-deduplicated value pool. This script
pulls that data out and prints every user/assistant turn in order.

Usage:
    python extract_chatgpt_share.py <share-url | share-id | path-to-saved.html> [--out out.md]

Notes:
  - Fetching uses `curl` (reliable past Cloudflare) with a urllib fallback. If
    both are blocked, save the page HTML in a browser and pass the file path.
  - No login / API key needed; share links are public.
"""
import sys
import re
import json
import subprocess
import urllib.request

UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/124.0 Safari/537.36")


def fetch_html(target: str) -> str:
    # A saved file?
    if not target.startswith("http") and ("\n" not in target):
        try:
            with open(target, "r", encoding="utf-8", errors="replace") as f:
                return f.read()
        except OSError:
            pass

    # A bare share id?
    if re.fullmatch(r"[0-9a-f-]{20,}", target):
        target = f"https://chatgpt.com/share/{target}"

    # Try curl first (best at getting past Cloudflare), then urllib.
    try:
        out = subprocess.run(
            ["curl", "-sL", "-A", UA, target],
            capture_output=True, text=True, timeout=60,
            encoding="utf-8", errors="replace",
        )
        if out.stdout and "linear_conversation" in out.stdout:
            return out.stdout
        html = out.stdout
    except (FileNotFoundError, subprocess.SubprocessError):
        html = ""

    if "linear_conversation" not in html:
        req = urllib.request.Request(target, headers={"User-Agent": UA})
        with urllib.request.urlopen(req, timeout=60) as r:
            html = r.read().decode("utf-8", "replace")

    if "linear_conversation" not in html:
        sys.exit("Could not retrieve the conversation HTML (blocked, or not a "
                 "share link). Save the page in a browser and pass the file path.")
    return html


def build_pool(html: str):
    """Return the flat value pool V that contains the conversation."""
    # Both known wrappers carry the same index-deduplicated payload.
    chunks = re.findall(r'enqueue\("((?:[^"\\]|\\.)*)"\)', html)
    chunks += re.findall(r'__next_f\.push\(\[1,"((?:[^"\\]|\\.)*)"\]\)', html)

    dec = json.JSONDecoder()
    values = []
    for raw in chunks:
        text = json.loads('"' + raw + '"')  # JS-unescape one level
        i = 0
        while i < len(text):
            while i < len(text) and text[i] in " \r\n\t":
                i += 1
            if i >= len(text):
                break
            try:
                val, end = dec.raw_decode(text, i)
            except ValueError:
                break
            values.append(val)
            i = end

    for v in values:
        if isinstance(v, list) and "linear_conversation" in v:
            return v
    sys.exit("Parsed the page but found no conversation pool. The share format "
             "may have changed.")


def resolve_messages(V):
    SKIP = {"parent", "children"}

    def deref(i, d=0):
        if type(i) is not int:
            return interpret(i, d)
        return interpret(V[i], d) if 0 <= i < len(V) else None

    def interpret(e, d=0):
        if d > 40:
            return None
        if isinstance(e, dict):
            out = {}
            for k, val in e.items():
                key = deref(int(k[1:]), d + 1) if isinstance(k, str) and k.startswith("_") else k
                if key in SKIP:
                    continue
                out[key] = deref(val, d + 1) if type(val) is int else interpret(val, d + 1)
            return out
        if isinstance(e, list):
            return [deref(x, d + 1) if type(x) is int else interpret(x, d + 1) for x in e]
        return e

    lin = V[V.index("linear_conversation") + 1]
    turns = []
    for n in (lin if isinstance(lin, list) else []):
        node = deref(n)
        if not isinstance(node, dict):
            continue
        msg = node.get("message") if isinstance(node.get("message"), dict) else node
        if not isinstance(msg, dict):
            continue
        author = msg.get("author")
        role = author.get("role") if isinstance(author, dict) else msg.get("role")
        meta = msg.get("metadata") if isinstance(msg.get("metadata"), dict) else {}
        if meta.get("is_visually_hidden_from_conversation"):
            continue
        content = msg.get("content")
        text = ""
        if isinstance(content, dict):
            parts = content.get("parts")
            if isinstance(parts, list):
                text = "\n".join(p for p in parts if isinstance(p, str))
            elif isinstance(content.get("text"), str):
                text = content["text"]
        if role in ("user", "assistant") and text.strip():
            turns.append((role, text))
    return turns


def main():
    args = [a for a in sys.argv[1:] if a != "--out"]
    out_path = None
    if "--out" in sys.argv:
        out_path = sys.argv[sys.argv.index("--out") + 1]
        args = [a for a in args if a != out_path]
    if not args:
        sys.exit(__doc__)

    html = fetch_html(args[0])
    turns = resolve_messages(build_pool(html))

    label = {"user": "🧑 User", "assistant": "🤖 ChatGPT"}
    blocks = [f"# ChatGPT conversation ({len(turns)} turns)\n"]
    for role, text in turns:
        blocks.append(f"\n## {label.get(role, role)}\n\n{text}\n")
    md = "\n".join(blocks)

    if out_path:
        with open(out_path, "w", encoding="utf-8") as f:
            f.write(md)
        print(f"Wrote {len(turns)} turns to {out_path}")
    else:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        print(md)


if __name__ == "__main__":
    main()
