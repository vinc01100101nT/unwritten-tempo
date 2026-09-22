# Git LFS Push Says "no such host" While the Network Works

**Reach for this when** a `git push` fails with `lookup lfs.github.com: no such host` (or any `dial tcp: lookup …`
from Git LFS), yet `host lfs.github.com` in the same shell answers fine.

## The rule

Git LFS is a Go program, and Go can use its own DNS resolver instead of the system's. When the machine's DNS setup is
something Go's resolver does not read (a VPN, a split resolver, a profile), LFS fails every lookup while every other
tool succeeds. Push again with Go told to use the system resolver: `GODEBUG=netdns=cgo git push`.

## Why

2026-09-21: pushing one commit failed three times with `no such host` for `lfs.github.com`. Retrying, and running
outside the sandbox, changed nothing. `host lfs.github.com` resolved it the whole time. `GODEBUG=netdns=cgo` pushed
on the first try.

## How to apply

1. Confirm the machine can resolve it: `host lfs.github.com`. If that fails too, it is a real network problem, and
   this page does not apply.
2. If it resolves, push with `GODEBUG=netdns=cgo git push origin <branch>`.
3. Do not change git or LFS settings to get past it (the "locksverify false" hint in the same error message is
   about something else). The environment variable changes nothing permanently.
