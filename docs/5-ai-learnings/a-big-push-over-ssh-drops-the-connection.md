---
status:
owner: vince
last-updated: 2026-09-26
tags:
  - ai-learnings
---

# 📡 A Big Push Over SSH Drops the Connection

## Reach for this when

`git push` of a large commit ends with `Connection to github.com closed by remote host`, `client_loop: send
disconnect: Broken pipe` or `the remote end hung up unexpectedly` — and the command's own exit code may still read 0.

## The rule

The connection went quiet while git was packing and uploading, and something between you and the host closed it. Push
again with SSH keep-alives, so the connection never sits silent, then **prove the push landed** by fetching and
reading `origin/main` — never trust the exit code of the first try.

## Why

2026-09-26, this project: one commit of 952 files (273 MB raw, 23 MB packed) over a slow uplink. The first push ran
past a 10-minute limit, then died with a broken pipe while the shell still reported exit 0. The retry with keep-alives
went through in one go. Cost: a lost ten minutes and a push that looked done and was not.

## How to apply

```bash
GIT_SSH_COMMAND="ssh -o ServerAliveInterval=15 -o ServerAliveCountMax=20" git push --progress origin main
git fetch origin && git status -sb     # "ahead 0" — or "## main...origin/main" with nothing after — means it landed
```
Run it in the background on a big push and read the log when it finishes.
