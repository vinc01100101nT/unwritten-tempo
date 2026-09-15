"""THE shapes of the game server's log lines, written once and read by every script that reads that log
(server-log-tally.py, check-server-log.py). A line whose wording changes in the game changes here, in one place.

The authority for each shape is the code that writes it; the guide that describes them is
docs/0-process/reading-the-game-log.md.
"""
import re

TIME = r"(?P<t>\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})"
_HP = r"(?: \[HP (?P<hp>\d+)/(?P<max>\d+)\])?"

HIT = re.compile(TIME + r" \[hit\] (?P<map>\S+) (?P<who>.+?) hit (?P<whom>.+?) with (?P<skill>.+?) for (?P<dealt>\d+) "
                        r"\(rolled (?P<rolled>-?\d+)\)(?P<crit> \(critical\))?(?P<late> \(after it died\))?" + _HP +
                        r"(?: — nothing got through: (?P<why>.+))?$")
MISS = re.compile(TIME + r" \[hit\] (?P<map>\S+) (?P<who>.+?) MISSED (?P<whom>.+?) with (?P<skill>.+)$")
DEATH = re.compile(TIME + r" \[hit\] (?P<map>\S+) (?P<who>.+?) died — killed by (?P<killer>.+)$")
TOOK = re.compile(TIME + r" \[hit\] (?P<map>\S+) (?P<who>.+?) took (?P<dealt>\d+) from (?P<cause>.+?)"
                         r"(?: by (?P<by>.+? #\d+|.+?))?" + _HP + r"$")
EXPERIENCE = re.compile(TIME + r" \[loot\] (?P<map>\S+) (?P<who>.+?) \((?P<id>[^)]*)\) earned (?P<xp>\d+) experience "
                               r"from (?P<victim>.+)$")
LOOT = re.compile(TIME + r" \[loot\] (?P<map>\S+) (?P<who>.+?) \((?P<id>[^)]*)\) earned (?P<what>(?!\d+ experience from ).+) "
                         r"from (?P<victim>.+)$")
HEAL = re.compile(TIME + r" \[heal\] (?P<map>\S+) (?P<who>.+?) healed (?P<amount>\d+)(?: by (?P<by>.+?))?" + _HP + r"$")
STATE_ADDED = re.compile(TIME + r" \[buff\] (?P<map>\S+) (?P<state>\S+) x(?P<stacks>\d+) on (?P<who>.+?) "
                                r"from (?P<skill>\S+) for (?P<seconds>[\d.]+)s$")
STATE_LOST = re.compile(TIME + r" \[buff\] (?P<map>\S+) (?P<state>\S+) on (?P<who>.+?) from (?P<skill>\S+): "
                               r"(?P<n>\d+) (?P<how>.+?), (?P<left>\d+) left$")
PASSIVE_FIRED = re.compile(TIME + r" \[passive\] (?P<map>\S+) (?P<who>.+?) fired (?P<skill>\S+) on (?P<trigger>\S+)(?: at .*)?$")
PASSIVE_SKIPPED = re.compile(TIME + r" \[passive\] (?P<map>\S+) (?P<who>.+?) skipped (?P<skill>\S+) on (?P<trigger>\S+) "
                                    r"— (?P<why>.+)$")
APPEARED = re.compile(TIME + r" \[spawn\] (?P<map>\S+) (?P<who>.+? #(?P<n>\d+)) \((?P<id>[^)]*)\) (?P<how>appeared|entered)$")
CAME_BACK = re.compile(TIME + r" \[spawn\] (?P<map>\S+) (?P<who>.+? #\d+) came back$")
GOT_UP = re.compile(TIME + r" \[spawn\] (?P<map>\S+) (?P<who>.+?) got up" + _HP + r"$")
INVINCIBLE = re.compile(TIME + r" \[server\] (?P<map>\S+) (?P<who>.+?) invincibility (?P<state>on|off)$")

# Any line carrying the hit category, whatever its shape — used to tell "hit was switched off" apart from "this log
# was written by an older build whose lines these shapes cannot read".
ANY_HIT = re.compile(r"\[hit\] ")

NUMBERED = re.compile(r".+ #\d+$")
NOT_A_UNIT = {"somebody", "nobody"}
REASON_NOT_RECORDED = "reason not recorded"
