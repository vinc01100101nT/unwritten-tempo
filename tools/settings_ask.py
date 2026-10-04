"""Read one value from settings.env from Python — through tools/load-settings.sh, the ONE reader of that file.

WHY THIS EXISTS: a Python script that parsed settings.env itself would be a second reader, free to disagree with the
one every shell script uses — which value wins over the terminal's, how quotes are taken. This asks that one.

    from settings_ask import setting
    setting("PATCH_URL")      # -> the value, or "" when it is not set
"""

import os
import re
import subprocess

_REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__))).replace("\\", "/")


def setting(name):
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", name):
        raise ValueError(f"not a setting name: {name!r}")
    r = subprocess.run(["sh", "-c", 'REPO="$1"; . "$REPO/tools/load-settings.sh"; printenv "$2" || true',
                        "sh", _REPO, name], capture_output=True, text=True)
    return r.stdout.rstrip("\r\n")
