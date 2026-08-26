# tools/

Dev utilities for this project.

## build-game.sh

Builds the **server** (no window) or the **client** (what players run), from the terminal.

```bash
tools/build-game.sh server            # headless server for this Mac
tools/build-game.sh client            # the game players run
tools/build-game.sh both
tools/build-game.sh server --linux    # server for a Linux host — what you deploy
```

Why it's needed: Unity 6 treats a dedicated server as a *variant* of a desktop build rather than its own
target, so there is no menu item to click; and a batch-mode build can't run while the editor holds the
project open. This drives the editor you already have running, which is the normal state here. **Unity must
be open on this project.**

Takes a few minutes. It decides pass/fail by checking the artefact actually appeared, not by trusting the
request — the request usually times out long before the build ends, which is expected and harmless. On
success it prints the exact command to run what it built.

Used by [Starting the Network](../docs/starting-the-network.md).

## check-unity-code.sh

Checks the game's C# for **compile errors without opening Unity** and without forcing a refresh. ~4 seconds.

Why it's needed: the project rule forbids force-refreshing Unity (each refresh blocks ~60s and often times out), so a C# edit used to go unverified until the user happened to focus the editor. Unity ships its own C# compiler inside the app bundle (`Unity.app/Contents/Resources/Scripting`) and every reference library the game needs is already on disk — in `Managed/UnityEngine/`, the .NET compatibility stand-ins, and `unity/Library/ScriptAssemblies/` for package libraries — so this runs the *same* check Unity would. It never touches the running editor.

Checks both halves separately: the game (`Assets/Scripts`) and the editor tools (`Assets/Scripts/Editor`). An editor-only mistake is otherwise invisible until someone opens that window.

### Usage

```bash
tools/check-unity-code.sh            # both (default)
tools/check-unity-code.sh runtime    # game scripts only
tools/check-unity-code.sh editor     # editor tools only
```

Exit code **0** = clean, **1** = compile errors (printed with exact file + line), **2** = setup problem (it names what's missing). Reads the required Unity version from `unity/ProjectSettings/ProjectVersion.txt`; if that exact version isn't installed it warns and falls back to the newest installed one. Needs `unity/Library/ScriptAssemblies/` (present on any project Unity has opened once).

Verified to **catch** deliberate errors in both halves, not just to report "clean". A clean run means Unity will build it — it does **not** mean the game behaves correctly, so it never replaces play-testing.

## extract_chatgpt_share.py

Reads a **ChatGPT share link** (`chatgpt.com/share/...`) and prints the full conversation (every user + assistant turn) as Markdown.

Why it's needed: share pages are JavaScript-rendered, so a normal fetch / the WebFetch tool only sees the empty app shell. The conversation is embedded as hydration data (a React-Router turbo-stream / Next.js flight payload using an index-deduplicated value pool). This script decodes that and reconstructs the turns. No login or API key required — share links are public.

### Usage

```bash
# print to stdout
python tools/extract_chatgpt_share.py "https://chatgpt.com/share/<id>"

# save to a file
python tools/extract_chatgpt_share.py "https://chatgpt.com/share/<id>" --out convo.md

# also accepts a bare share id, or a path to a saved .html page
python tools/extract_chatgpt_share.py <share-id>
python tools/extract_chatgpt_share.py saved-page.html
```

Fetches via `curl` (best at getting past Cloudflare) with a `urllib` fallback. If both are blocked, open the link in a browser, **Save Page As… HTML**, and pass the file path instead.