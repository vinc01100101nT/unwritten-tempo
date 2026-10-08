---
name: restart-servers-keeps-old-settings
description: "restart-servers.sh used to restart each server with the settings.env values from when its window was FIRST opened; fixed 2026-10-08 — a window now rereads settings.env before every start, but only windows opened by the fixed start-servers.sh do"
metadata:
  node_type: memory
  type: project
  originSessionId: 4ddd00f1-c035-40fa-9e4f-1d29950e9130
  modified: 2026-10-07T18:33:27.390Z
---

Measured 2026-10-08: PATCH_URL in settings.env had been changed to the https Tailscale address; the character server,
restarted with restart-servers.sh many hours later, still allowed only the OLD website address — curl with the old
Origin got the allow header, the new https one did not.

Cause: each server window is one long-lived shell (`start-servers.sh window <name>`), and load-settings.sh lets a value
ALREADY SET in the shell win over the file (deliberate, so a typed value wins). So every restart re-used the values
loaded when the window opened. A brand-new setting still loaded, because nothing was set before — which hid the bug.

Fix (start-servers.sh, 2026-10-08): the top-level run records which settings were typed in front of it and passes that
list to each window; before every start a window unsets everything it loaded last time (except the typed ones) and
reloads the file. Replayed offline with a throwaway settings file: changed values and removed keys both reach a restart,
a typed value survives.

**Why:** a settings change followed by "restart everything" looked applied and silently wasn't; it cost a debugging round
on the website's Server Status box.

**How to apply:** windows opened BEFORE the fix still run the old loop in memory (bash parsed it once), so they need one
close-and-start-fresh. When a server behaves as if a setting is old, check the live value (e.g. the CORS Origin probe)
before suspecting code.
