---
name: clock-command-on-windows
description: "On this Windows machine, `TZ=Asia/Manila date` silently prints UTC (8 hours early); use `TZ=UTC-8 date` for the GMT+8 reply timestamps."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 0acd8992-7cc1-4ad9-bb9f-fba896a7afc6
  modified: 2026-10-02T08:09:59.906Z
---

The reply timestamps (global RULE ALPHA) must be measured in GMT+8. On Windows Git Bash, `TZ=Asia/Manila date "+%Y-%m-%d %H:%M"` does NOT know the zone name and prints **UTC with no warning** — 8 hours early. Measured 2026-10-02: it printed 08:08 while `TZ=UTC-8 date` and PowerShell `Get-Date` both said 16:08 +08:00.

**Why:** a whole session's timestamps went out 8 hours wrong (06:19 instead of 14:19) before a console timestamp didn't add up.

**How to apply:** measure with `TZ=UTC-8 date "+%Y-%m-%d %H:%M"` (POSIX: "UTC-8" means GMT+8 — the sign is inverted). It works on the Mac too. If ever unsure, cross-check once against `powershell -NoProfile -Command "Get-Date -Format 'yyyy-MM-dd HH:mm zzz'"`.
