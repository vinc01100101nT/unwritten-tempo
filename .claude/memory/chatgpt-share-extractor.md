---
name: chatgpt-share-extractor
description: How to read a ChatGPT share link — use the tools/extract_chatgpt_share.py script
metadata: 
  node_type: memory
  type: reference
  originSessionId: cec72265-2905-4691-b027-c21591836e99
---

The user shares **ChatGPT conversation links** (`chatgpt.com/share/...`) for me to read, and will keep doing so.

**Don't use WebFetch on them** — share pages are JS-rendered, so WebFetch/curl-to-markdown only sees the empty app shell. The conversation is embedded as hydration data (React-Router turbo-stream / Next.js flight payload, index-deduplicated value pool keyed off `linear_conversation`).

**Use the repo tool instead:**
```bash
python tools/extract_chatgpt_share.py "https://chatgpt.com/share/<id>"   # prints all turns as markdown
python tools/extract_chatgpt_share.py "<url>" --out convo.md             # save to file
```
It fetches via `curl` (UTF-8 decode; gets past Cloudflare where the backend `/backend-api/share/<id>` returns 403) with a urllib fallback, also accepts a bare share id or a saved `.html` path. Lives at `tools/extract_chatgpt_share.py` with usage in `tools/README.md`.
