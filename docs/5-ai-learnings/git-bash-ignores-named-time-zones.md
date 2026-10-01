# A Time-Zone Clock Reading Comes Back Eight Hours Off on Windows

**Reach for this when** `TZ=Asia/Manila date` (or any `TZ=<Region>/<City>`) on Windows prints a time that differs
from the taskbar clock, the Ubuntu prompt, or PowerShell's clock.

## The rule

The `date` that ships with Git for Windows cannot read named time zones like `Asia/Manila`. It treats them as an
unknown zone and prints UTC without any error. Use the offset form instead: `TZ=UTC-8 date` (the sign is reversed
on purpose; `UTC-8` means eight hours AHEAD of UTC), or plain `date`, which uses the Windows clock's own zone.

## Why

2026-10-02: four replies in a row were stamped with times from `TZ=Asia/Manila date`, all eight hours behind and
on the previous day (`2026-10-01 20:56` when the real time was `2026-10-02 04:56`). Nothing flagged it. It was
caught only because a PowerShell clock reading and the Ubuntu welcome screen both said 04:xx.

## How to apply

1. On Windows, take the clock reading with `TZ=UTC-8 date "+%Y-%m-%d %H:%M"`.
2. Check it once per session against a second source: `date -u` must be exactly 8 hours earlier.
3. If `TZ=<Region>/<City> date` and `date -u` print the same time, the zone name was ignored.
