---
name: publish-one-platform-leaves-other-stale
description: The launcher shows the shared version.txt, not the platform's manifest — publishing one platform makes the other look updated while it still downloads the old files
metadata:
  type: project
---

**Symptom (2026-10-06):** the user rebuilt the Mac client, the launcher said "Version 2026.10.06-2001", and the bug
fix was still missing on the Mac.

**Cause, measured:** `tools/publish-update.py` with no flags publishes only this machine's platform (`builds/client`
on Windows). The 20:01 run rewrote `builds/patch/windows/manifest.json` and the shared `builds/patch/version.txt`, but
`builds/patch/mac/manifest.json` stayed at the 18:07 build. The launcher prints `version.txt`
(`launcher/MainWindow.axaml.cs:51`) yet downloads `<platform>/manifest.json` (`launcher/Patcher.cs:97-98`), so the Mac
showed the new number while installing the old files. The Windows build published as 2001 was also only re-stamped,
not rebuilt (its DLL predated the fix).

**How to apply:** before telling the user a fix is live on a platform, check THAT platform's manifest time and search
its published DLL for a name unique to the fix — never trust the version the launcher shows. Publish both platforms
together with `tools/build-and-publish.sh`.

**Fixed in the launcher (2026-10-06, needs the launcher rebuilt and republished):** each platform's manifest now
carries `version`, and the launcher shows that one — and says when it is behind version.txt.
