"""Headless Blender batch cleaner for Hunyuan3D props.

Import a turbo-generated GLB -> Voxel Remesh (watertight, fills holes) ->
Decimate (drop poly count for the game) -> export GLB.

Single file:
    blender --background --python clean_prop.py -- input.glb output.glb

Whole folder (every .glb in IN_DIR -> OUT_DIR, suffix _clean):
    blender --background --python clean_prop.py -- --dir IN_DIR OUT_DIR

Tunables (override via flags, see parse_args):
    --voxel 0.03      voxel size in object units; smaller = more detail, heavier
    --decimate 0.5    decimate ratio; 1.0 = keep all, 0.5 = halve tris
    --no-decimate     skip the decimate pass

Notes:
- Voxel remesh DISCARDS UVs/textures. These props are re-textured/baked
  downstream, so that's intended. If you need the Hunyuan texture kept, this
  is the wrong tool — do a manual pass instead.
- Matches the manual flow we validated by hand: remesh ~0.03 then decimate.
"""

import bpy
import sys
import os
import glob


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []

    opts = {"voxel": 0.03, "decimate": 0.5, "do_decimate": True, "dir": False}
    positional = []
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--voxel":
            opts["voxel"] = float(argv[i + 1]); i += 2
        elif a == "--decimate":
            opts["decimate"] = float(argv[i + 1]); i += 2
        elif a == "--no-decimate":
            opts["do_decimate"] = False; i += 1
        elif a == "--dir":
            opts["dir"] = True; i += 1
        else:
            positional.append(a); i += 1

    if len(positional) < 2:
        raise SystemExit(
            "usage: blender --background --python clean_prop.py -- "
            "[--voxel N] [--decimate N|--no-decimate] [--dir] IN OUT"
        )
    opts["in_path"], opts["out_path"] = positional[0], positional[1]
    return opts


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(path):
    bpy.ops.import_scene.gltf(filepath=path)


def join_meshes():
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise RuntimeError("no mesh found in import")
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    return bpy.context.view_layer.objects.active


def clean(obj, voxel, do_decimate, decimate_ratio):
    bpy.context.view_layer.objects.active = obj

    rem = obj.modifiers.new(name="Remesh", type="REMESH")
    rem.mode = "VOXEL"
    rem.voxel_size = voxel
    bpy.ops.object.modifier_apply(modifier=rem.name)

    if do_decimate:
        dec = obj.modifiers.new(name="Decimate", type="DECIMATE")
        dec.ratio = decimate_ratio
        bpy.ops.object.modifier_apply(modifier=dec.name)

    bpy.ops.object.shade_smooth()


def export_glb(path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB")


def process_one(in_path, out_path, opts):
    print(f"[clean_prop] {in_path} -> {out_path}")
    reset_scene()
    import_glb(in_path)
    obj = join_meshes()
    clean(obj, opts["voxel"], opts["do_decimate"], opts["decimate"])
    export_glb(out_path)
    tris = len(obj.data.polygons)
    print(f"[clean_prop] done: {tris} faces")


def main():
    opts = parse_args()

    if opts["dir"]:
        in_dir, out_dir = opts["in_path"], opts["out_path"]
        files = sorted(glob.glob(os.path.join(in_dir, "*.glb")))
        if not files:
            raise SystemExit(f"no .glb files in {in_dir}")
        for f in files:
            name = os.path.splitext(os.path.basename(f))[0]
            process_one(f, os.path.join(out_dir, f"{name}_clean.glb"), opts)
        print(f"[clean_prop] batch complete: {len(files)} props")
    else:
        process_one(opts["in_path"], opts["out_path"], opts)


if __name__ == "__main__":
    main()
