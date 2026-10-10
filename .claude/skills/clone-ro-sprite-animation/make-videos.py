"""Join the frames render-video.cs wrote into one video per attack: three swings at real speed with a half-second rest
after each, then one swing at a third of the speed. Each weapon's videos go in its own folder:
<videos dir>/<weapon>/<weapon>_attack.mp4. Usage: python make-videos.py <frames dir> <videos dir>"""
import os, subprocess, sys
frames, out = sys.argv[1], sys.argv[2]
NAMES = {"AttackOneHandSword": "sword", "AttackAxe": "axe", "AttackMace": "warhammer", "AttackSpear": "spear",
         "AttackKnuckle": "fist", "AttackDaggerStab": "dagger", "AttackKatar": "katar", "AttackShurikenSwing": "shuriken",
         "AttackSniper": "sniper", "AttackStaffSwing": "staff", "AttackWand": "wand", "AttackScytheSweep": "scythe"}
os.makedirs(out, exist_ok=True)
for clip, name in NAMES.items():
    d = os.path.join(frames, clip)
    if not os.path.isdir(d): continue
    fs = sorted(f for f in os.listdir(d) if f.endswith(".png"))
    path = lambda f: os.path.join(d, f).replace(os.sep, "/")
    lines = []
    def add(f, seconds): lines.append("file '%s'\nduration %.4f" % (path(f), seconds))
    for _ in range(3):
        for f in fs[:-1]: add(f, 1 / 30)
        add(fs[-1], 0.5)
    for f in fs[:-1]: add(f, 3 / 30)
    add(fs[-1], 0.8)
    lines.append("file '%s'" % path(fs[-1]))
    os.makedirs(os.path.join(out, name), exist_ok=True)
    listing = os.path.join(frames, name + ".txt")
    open(listing, "w").write("\n".join(lines) + "\n")
    r = subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", listing,
                        "-vf", "fps=30,format=yuv420p", "-c:v", "libx264", "-crf", "18",
                        os.path.join(out, name, name + "_attack.mp4")], capture_output=True, text=True)
    os.remove(listing)
    print(name, "ok" if r.returncode == 0 else r.stderr[:300])
