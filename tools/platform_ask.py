"""Ask tools/platform.sh a question from Python — the ONE place that knows which computer this is.

WHY THIS EXISTS: a Python script that checked `sys.platform` itself would be a second OS detector, free to
disagree with the one every shell script uses. This asks the shell one instead, so there is exactly one answer.

    from platform_ask import ask
    ask("browser")            # -> a path, or None when the question has no answer on this computer
"""

import os
import subprocess

_PLATFORM_SH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "platform.sh")


def run(*question):
    """Ask, and hand back the finished process (exit code, answer, complaint) for callers that need all three."""
    return subprocess.run(["sh", _PLATFORM_SH, *question], capture_output=True, text=True)


def ask(*question):
    """The answer as one line of text, or None if platform.sh has none (or sh itself could not start)."""
    try:
        r = run(*question)
    except OSError:
        return None
    answer = r.stdout.strip()
    return answer if r.returncode == 0 and answer else None
