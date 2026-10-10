---
name: image-to-3d
description: Use whenever a picture must become a 3D model with real depth — an ornate weapon (a wand head, a staff crown, anything with parts at different depths), a prop, a creature part — or when running, repairing or reinstalling Trellis.2 on this machine. Covers the one-command tool that drives Trellis with no server or window, the picture it needs, getting a game-sized triangle count, the three fixes the install needed, judging the model from five angles before Unity, and the license caveat.
---

# A picture into a 3D model (Trellis.2)

`tools/sprite-sheet-to-weapon.py` builds a weapon from a two-view drawing, one horizontal slice at a time. That
holds for flat shapes (the greatsword, the shuriken) and falls apart on an ornate head: from the side every slice
shows as a layer, and a head whose side view is filled by ornaments comes out as one thick lens (the sun-and-cloud
wand, 2026-10-10 — "the worst wand I've ever seen"). Trellis.2 makes a real 3D model from ONE picture: the same wand
came back with a round sun, spikes all round and clouds curling in depth, and held its shape from above.

**License, word for word from the install's own note:** *"Trellis uses some nvidia code that disallows commercial
use, and only allow for research / evaluation purposes. This code prevents you from selling assets generated via
Trellis right now."* Say so whenever a Trellis model is about to ship.

## Run it

```bash
python3 tools/image-to-3d.py <picture.png> --name <name> [--faces 1500] [--seed 42] [--texture 1024] [--out <folder>]
```

Writes `<out>/<name>/<name>.obj`, `<name>.png` (base colour) and `<name>_input.png` (the picture after Trellis's own
background removal). Default out: `unity/Assets/2_Generated/WeaponModels/`. The model stands along +Y, centred.
- It re-runs itself under the Trellis install's Python (`--trellis` / `TRELLIS_HOME`, default
  `C:\Users\Admin\Downloads\trellis2-stableprojectorz_v22`) and drives the install's own worker once: **no server,
  no window, nothing left running.** Measured on the RTX 4060 (8 GB): about 2 minutes to load, 50 s to generate,
  6 s to export. Run it in the background.
- **Same picture + same seed = nearly the same model** (two wand runs: 1,457 and 1,462 triangles, same shape).
  Change `--seed` for a different take.
- **The picture:** ONE object, whole, centred, plain or transparent background. A weapon's front view cut from its
  two-view sheet works (mask from the converter's `foreground`, pasted onto a transparent square). Open
  `<name>_input.png` afterwards — it must show the whole object, nothing cropped.

## Triangle count — ask Trellis, never shrink afterwards

`--faces` goes to Trellis's own exporter, which reduces the shape BEFORE it lays out the texture. Its full model is
~100,000 triangles cut into many texture pieces, and `tools/optimize-glb.sh` (gltf-transform) stalls at ~6,800
triangles on those seams whatever ratio or error it is given (measured on the wand at error 0.02, 0.05 and 0.15).
Budget, measured: the character body is 2,312 triangles; the wand at 1,500 kept its sun, ring, gem and clouds,
losing only some spikes; 3,000 is more than a body.

## Judge it before Unity — five angles

```bash
python3 tools/render-model-views.py <name>.obj <name>.png <reference.png> <out.png> [--top 0.3]
```

Front, side, three-quarter, high three-quarter and from behind and above, beside the picture. Look at it yourself,
then show the user — a model is unjudged until they have seen it, and never "fixed" in the same reply.

## Into the game

- **A weapon:** the `import-weapon-models` skill, section "An ornate weapon made with Trellis" — fitting to the hand
  and switching the item.
- Imported textures from here are ordinary painted textures, not pixel art: compressed, bilinear, mip-mapped,
  `maxTextureSize` 1024 (`tools/check-asset-rules.py` requires compression outside `2_Generated/Weapons/`).

## If the install breaks or is reinstalled — the three fixes it needed (2026-10-10)

1. **OpenCV.** The installer pulled `opencv-python-headless` 5.0, built with `OpenEXR: NO`, so the worker died
   loading its light maps (`assets/hdri/*.exr`). Fix, inside the install:
   `code\venv\Scripts\python.exe -m pip install "opencv-python-headless<5"` (4.14 reads them).
2. **The launcher's switches.** `tools\environment.bat` sets `SETUPTOOLS_USE_DISTUTILS=stdlib` and
   `PYTHONNOUSERSITE=1`; without the first, the renderer's import of `torch.utils.cpp_extension` fails on
   `python311.zip\distutils\core.pyc`. The tool sets both, plus `OPENCV_IO_ENABLE_OPENEXR=1`.
3. **The worker's profiling block.** `PipelineWorker.generate` reads six profiling keys unconditionally; the tool
   passes them all switched off.

First-time install (only if the folder is fresh): its batch files must be called by FULL path from `cmd /c` — run
from Git Bash, `call environment.bat` in the right folder still answered "not recognized". It downloads PyTorch
(3.4 GB), two helper models and ~20 GB of Trellis weights; on 2026-10-09 that took about 80 minutes here.
