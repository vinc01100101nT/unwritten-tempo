---
name: recompile-gate-in-same-command
description: "Never chain a recompile after a status read in one command — the play-mode check must STOP the command, not just print. 2026-10-20 a chained status+recompile fired while the user was playing."
metadata:
  node_type: memory
  type: feedback
  originSessionId: b449c404-9be3-4ad2-9a7c-6c809db8a6cf
  modified: 2026-10-20T09:58:58.227Z
---

The rule "never recompile while the user is playing" is only kept if the play-mode check can STOP the recompile.
On 2026-10-20 I ran one shell command that printed `editor_status` and then ran `recompile` regardless; it printed
`"playMode":"playing"` and recompiled anyway, mid-session.

**Why:** a recompile during Play reloads every script and can end the user's session in errors; printing the status
is not checking it.

**How to apply:** gate in the same command so it exits on anything but stopped:
`unity cmd ... editor_status | grep -q '"playMode":"stopped"' || { echo "user is playing - not recompiling"; exit 1; }`
then the recompile. Same gate before any editor action that reloads or saves (eval that imports, Sync(true)).
Related: [[assume-compiled-never-blame-recompile]].
