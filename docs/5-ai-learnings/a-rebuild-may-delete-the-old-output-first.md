---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - ai-learnings
---

# 🗑️ A Rebuild May Delete the Old Output First

**Reach for this when:** you are about to run a build, bake or regenerate script you have not read, and something is depending on the current output right now.

> Read what a build or regenerate script does to the **existing** output before running it. Many delete it first,
> so a rebuild that fails leaves you with less than you started with.

## The rule

Deleting the previous output is normal and sensible — it is how a build stays clean. It is also destructive the
moment the build does not finish: no new artifact, and the working one you had five minutes ago is gone too.

So the question to answer **before** the first run is not "will this build?" but "what does this take away while
it tries?"

## Why — what it cost

A server rebuild was started to test a change. The script deleted the previous binary and then the build never
ran, blocked by a dialog. The result was no server at all — and the user's running one, which had been serving a
live session, died along with its deleted binary.

Nothing was permanently lost: a build output is regenerable, and it was regenerated. But the working state was
lost, the user's session went down without warning, and the whole thing was avoidable by reading eight lines of
the script.

## How to apply it

- Before the first run of any build tool you have not read:
  `grep -n 'rm -rf\|rm -f\|Delete\|clean' <script>`
- If it deletes first **and** something depends on the current artifact right now — a running process, a demo, a
  session in progress — either copy the artifact aside first, or say plainly that the rebuild takes the current
  one down, before starting it.
- Be exact with the user afterwards: a regenerable output is a lost **working state**, not lost work. Those are
  different sentences and only one of them is alarming.
- Rebuilding is the right instinct. Rebuilding without knowing it is destructive is the failure.

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [A Hanging Tool Is Usually a Dialog](a-hanging-tool-is-usually-a-dialog.md) — the same build, the first half of the damage.
- [Starting the Network](../1-planning/starting-the-network.md) — the build and run commands this applies to here.
