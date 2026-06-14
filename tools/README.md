# tools/

Dev utilities for this project.

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