---
name: lan-play-plain-http-blocked
description: Built games (client AND headless map server) refuse plain-HTTP web requests to any address but their own machine — LAN play needs PUBLIC_* addresses plus a --dev build, never LAN IPs in ACCOUNTS_URL/CHAR_SERVER_URL
metadata:
  type: project
---

Unity's Player Settings "insecure HTTP" option was 0 (not allowed) until 2026-10-02, now 1 (development builds
only). A built game then refuses `UnityWebRequest` over plain http to anything but localhost: "Non-secure network
connections disabled in Player Settings / InvalidOperationException: Insecure connection not allowed". The
headless MAP SERVER is a built game too.

**Why:** on 2026-10-02 I pointed `ACCOUNTS_URL`/`CHAR_SERVER_URL` in settings.env at the Wi-Fi address
(192.168.1.8) for LAN play; the map server reads `CHAR_SERVER_URL` to register, was refused 7 times, and nobody
could enter. A round lost, my change.

**How to apply:** for other devices on the Wi-Fi: keep `ACCOUNTS_URL`/`CHAR_SERVER_URL` on localhost (this PC's own
servers), set `MAP_SERVER_ADDRESS` to the LAN address, put the LAN address in `PUBLIC_ACCOUNTS_URL` /
`PUBLIC_CHAR_SERVER_URL` (only builds read those), and build the other devices' copy with
`tools/build-game.sh client --dev`. Runbook: "On your own wifi" in starting-the-network.md.
Related: [[the-game-is-always-online]].
