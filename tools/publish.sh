#!/usr/bin/env bash
# Publish what is ALREADY BUILT — the Windows game, the Mac game, the website, the wiki and the patch notes — as one
# update in builds/patch. Builds nothing: to build first, run tools/build-and-publish.sh instead.
#
# USAGE:  tools/publish.sh
set -euo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$REPO/tools/platform.sh"
PY="$(find_python)" || { echo "No working Python found." >&2; exit 2; }
"$PY" "$REPO/tools/publish-update.py" --windows "$REPO/builds/client" --mac "$REPO/builds/mac/client"
