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

Used by [Starting the Network](../docs/2-systems/networking/starting-the-network.md).

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

## ui-tokens.py · ui-geometry.py · check-ui-prototypes.py · prove-ui-prototype-check.py

Keep the mock pages in `prototypes/` and `mockups/` honest about the game's real look.

```bash
tools/ui-tokens.py                 # read the game's colours + fonts → prototypes/ui-tokens.css (+ .json)
tools/ui-geometry.py               # measure a real screen in the running editor → prototypes/ui-geometry.json
tools/check-ui-prototypes.py       # check every mock page; exit 1 if anything is flagged
tools/check-ui-prototypes.py path/to/page.html    # or just these pages
tools/prove-ui-prototype-check.py  # prove the checker still catches what it claims to
```

`ui-geometry.py` is the only one that needs the Unity editor open. Colours can be read from the source; sizes
cannot — a column's width is what the layout works out at the window's real size. So it builds the screen offline
in a throwaway scene, at the window's true size, and writes down what every part measures. A screen appears there
only once someone writes its recipe (a short piece of C# that builds it); today that is the Inventory.

`ui-tokens.py` never writes to the game — it only reads. A mock page links the generated stylesheet and uses
`var(--gold)` / `var(--rarity-rare-text)` / `var(--font-ui)` instead of typing a colour, so the palette cannot
drift out of the code.

`check-ui-prototypes.py` opens each page in a real browser and reads the colour and font of every element
**painted inside a marked region** — what the screen shows, not what the file says. A page marks the element that
IS the window with `data-mocks="Inventory"`, and marks each separately-drawn piece with its own name
(`data-mocks="Item Tooltip"` on the tooltip), because those are different screens in the game with different
colours. Everything outside a marked region is ignored, so a page can carry a title or an explanation for the
reader without any of it being compared.

Each painted colour lands in one of: from the shared palette, declared by the screen this region names, **real
but belonging to a different screen**, or existing nowhere in the game. The last two are flagged; the third is
the one a project-wide check can never find. A marked region containing nothing is flagged too, never called
clean — a right-click menu that only appears when you right-click paints nothing at rest, and an empty report
from a tool that looked at nothing reads exactly like a clean one. Fonts are checked the same way: the game has
Cinzel, Inter and PixelFont. Needs Chrome; without one it falls back to reading the page's text, furniture
included, and says so.

It also compares **sizes**, for any screen `ui-geometry.py` has measured: the window's width, each column's
width, row height and every text size, reported as "the mock is 33.7px, the game is 22px". A mock whose layout
demands more room than the real window has is called out by name — the bag's mock needed 710px against a 488px
window. Label the mock's parts with `data-part="header"`, `"row"`, `"search"`, `"list"`; column names come from
the headings themselves, so a column the game does not have shows up on its own.

`prove-ui-prototype-check.py` is what keeps the checker honest: ten pages with known answers, including a clean
one, a bad colour placed outside a marked region that must be ignored, an empty region that must be flagged, and
a mock built to the game's own measurements that must report no size differences at all. Re-run it after **any**
edit to the checker — a check that has gone deaf prints exactly what a clean page prints.

Full sequence: [Prototyping a UI Before Building It](../docs/0-process/prototyping-ui-workflow.md).

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