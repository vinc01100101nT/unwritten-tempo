"""Join render-stance frames into one video per job: real speed (loops repeated), then once at a third of the speed.
A job is named <weapon>_<kind> (kind: idle, run, sheathe, unsheathe, attack, or one of those with more after it, like
run_put_away); its video goes in the weapon's own folder: <videos dir>/<weapon>/<weapon>_<kind>.mp4.
Usage: python make-stance-videos.py <frames dir> <videos dir>"""
import os, subprocess, sys
frames, out = sys.argv[1], sys.argv[2]
REPEAT = {"idle": 2, "run": 4, "sheathe": 2, "unsheathe": 2, "attack": 3}
for name in sorted(os.listdir(frames)):
    d = os.path.join(frames, name)
    if not os.path.isdir(d): continue
    fs = sorted(f for f in os.listdir(d) if f.endswith(".png"))
    weapon, kind = name.split("_", 1)
    base = kind.split("_")[0]
    p = lambda f: os.path.join(d, f).replace(os.sep, "/")
    lines = []
    def add(f, s): lines.append("file '%s'\nduration %.4f" % (p(f), s))
    loop = base in ("idle", "run")
    for _ in range(REPEAT.get(base, 2)):
        for f in (fs[:-1] if loop else fs): add(f, 1 / 30)
        if not loop: add(fs[-1], 0.5)
    for f in (fs[:-1] if loop else fs): add(f, 3 / 30)
    add(fs[-1], 0.8)
    lines.append("file '%s'" % p(fs[-1]))
    listing = os.path.join(frames, name + ".txt"); open(listing, "w").write("\n".join(lines) + "\n")
    os.makedirs(os.path.join(out, weapon), exist_ok=True)
    r = subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", listing, "-vf", "fps=30,format=yuv420p",
                        "-c:v", "libx264", "-crf", "18", os.path.join(out, weapon, name + ".mp4")], capture_output=True, text=True)
    os.remove(listing)
    print(name, "ok" if r.returncode == 0 else r.stderr[:300])
