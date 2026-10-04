#!/usr/bin/env bash
# Restart the servers start-servers.sh opened, inside their own windows — the same as pressing Ctrl+C in each and
# typing its command again. Double-click it to restart all four, or name the ones you want:
#
#     restart-servers.sh                # account, char, map and website
#     restart-servers.sh map            # only the map server
#     restart-servers.sh char map       # the character server, then the map server
#
# Whatever order they are named in, they restart in one fixed order (account, char, map, website), so the map server
# always waits for the character server that is coming up. A window start-servers.sh did not open is left alone and
# said so. How it works, and the list of windows, are in start-servers.sh — written once, there.
exec "$(dirname "${BASH_SOURCE[0]}")/start-servers.sh" restart "$@"
