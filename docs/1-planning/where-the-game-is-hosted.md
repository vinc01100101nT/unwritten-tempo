---
status:
owner: vince
last-updated: 2026-09-09
tags:
  - planning-production
---

# Where the Game Is Hosted

**The choice: one always-on virtual machine in Singapore, from Vultr.** Players are in the Philippines, and
Singapore is the closest place that every cheap host actually has, that every South-East Asian game already
uses, and that a certificate and a domain can be pointed at without ceremony.

This page is the CHOICE and the money. How to actually run the four processes on that machine — every command,
every setting — is [Starting the Network](../2-systems/networking/starting-the-network.md).

---

## Why Singapore and not somewhere cheaper

The cheap, generous hosting everyone recommends is European. Europe is unusable for this game.

Measured 2026-09-08 from one host in Manila (WonderNetwork's probes — **one network path, not an average across
Philippine providers**, so read them as the shape rather than the exact number a player will see):

| From Manila to | Round trip |
| --- | --- |
| Hong Kong | 18.65 ms |
| Singapore | 48.93 ms |
| Seoul | 94.90 ms |
| Los Angeles | 158.58 ms |
| Frankfurt | 243.65 ms |
| Amsterdam | 251.09 ms |

A quarter of a second is not a playable game where the server decides every hit. That single fact removes
Hetzner's and OVHcloud's headline bargains, because those prices are for their European and Canadian machines.

---

## What it costs, Singapore, September 2026

Prices read from each provider's own live pricing data on 2026-09-08, at the size that runs everything —
database, sign-in, characters, one world — on one machine.

| Provider | 4 cores / 8 GB | Traffic included |
| --- | --- | --- |
| **Vultr** (AMD, NVMe) | **$48/mo** | 6 TB |
| Vultr (previous generation) | $40/mo | 4 TB |
| DigitalOcean | $48/mo | 5 TB |
| Akamai (Linode) | $48/mo | 4.9 TB |
| Hetzner | $57.99/mo | 2 TB, then $8.49 per further TB |
| Contabo (8 cores / 24 GB) | about $23/mo | shared cores, slower network port |

Two results worth keeping, because they overturn the usual advice:

- **Hetzner is no longer the cheap one.** Its Singapore prices sit 25–30% above its European ones after the
  2026 rises, and Singapore machines carry as little as half a terabyte of traffic against Europe's twenty.
  Its smallest Singapore machine — one core, 2 GB — is $17.99/mo, where Vultr charges $10 for the same shape.
- **Contabo is the cheapest per gigabyte of memory and the wrong shape for the world.** Its processor cores are
  shared with other customers. The world server runs its whole simulation on one thread, so one greedy
  neighbour on that machine becomes lag for every player at once, invisibly and with nothing to tune. Fine for
  a database or the wiki; not for a world.

**OVHcloud is unpriced here.** It was the earlier recommendation and it may still be competitive in Singapore,
but its website and its price API were both unreachable from this machine on 2026-09-08, so there is no
verified number to put in the table. Its European bargains are ruled out by the latency table above regardless.

### The plan by size

- **Testing with friends** — one Vultr Singapore machine, 2 cores / 4 GB, **$24/mo**, 5 TB. Billed by the hour,
  so a weekend trial costs about a dollar.
- **Soft launch** — split it: one small machine for the database, sign-in and characters ($24), plus one machine
  per world ($24–48 each). Roughly **$75–125/mo**.
- **When a world starts stuttering** — move that world onto a machine whose cores nobody else shares ($40/mo for
  two, $80 for four) rather than buying more memory.

Traffic is a non-issue at this size: fifty players connected without a break is a few hundred gigabytes a
month against five or six terabytes included. **The game's own per-player data rate has never been measured** —
that is the one number worth taking off a running server before committing to a plan.

---

## What has to be true on that machine

Full instructions are in the runbook; this is the shape, so nothing is discovered late.

- **All four processes run there** — database, sign-in service, character server, world server — and each must
  start on boot and restart itself after a crash. Nothing does that today; they are started by hand.
- **A Linux build of the world server**, which has never been made. The build command already takes a Linux
  option and the editor has the Linux support installed, so this is untested rather than blocked.
- **A domain name.** The two web services must stop speaking readable text over the internet, and a certificate
  cannot be issued for a bare numeric address. Two names, both pointing at the same machine.
- **The plain doors shut.** Both services take a setting for which addresses they answer on; on a hosted machine
  that is the machine itself, so the encrypting front door is the only way in.
- **Backups off the machine.** The database is the only thing in the project that exists nowhere else.

---

## Worth revisiting, not now

- **Hong Kong is 30 ms closer than Singapore** on the path measured above. That is a real difference for a
  combat game. Against it: Vultr, DigitalOcean, Hetzner and Contabo have no Hong Kong location, so it means a
  less familiar host — and one measurement from one Manila machine is not proof it holds for Globe, PLDT and
  Converge customers alike. A $5 machine and a week of real pings would settle it.
- **Hosting inside the Philippines is possible and expensive.** Amazon has had a Manila site since June 2023
  and Alibaba Cloud runs a Manila region; both put players in single-digit milliseconds and both charge several
  times these prices, with a bill per gigabyte leaving the building. The cheap "Manila VPS" sellers that turn
  up in search are small resellers. Revisit when players are paying.
- **Vultr has no Manila region**, whatever the affiliate blogs say — read from its own region list on
  2026-09-08.

---

## Related

- [Starting the Network](../2-systems/networking/starting-the-network.md) — every command and setting for running the four processes, on this machine or a rented one.
- [Networking — Architecture](../2-systems/networking/README.md) — what the four processes are and what each decides.
- [Roadmap to Launch](roadmap-to-launch.md) — Tier 2, where paying for hosting starts.
