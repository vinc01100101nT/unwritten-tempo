"""
Batch mesh-only exporter for Mixamo auto-rigging — WITH baked colour.

Reads every FBX in an input folder, strips the armature/skeleton (leaving the mesh
in its bind/A-pose), BAKES the per-part Quaternius materials into a single texture
atlas, collapses the mesh to ONE material that samples that atlas, and writes a clean
mesh-only FBX (texture embedded) per character into an output folder. These are what
you upload to Mixamo.

Why bake?
  Quaternius bodies carry NO texture and NO UVs — each body part is a separate
  submesh with its own SOLID-COLOUR material (Skin/Face/Clothes/…). Mixamo's
  auto-rigger re-exports a SINGLE-material mesh, so multi-material colour is lost and
  the character comes back plain grey. Here we UV-unwrap (Smart UV Project) and bake
  every material's base colour into one atlas, so the whole look survives as ONE
  material + ONE texture — which Mixamo keeps (UVs are preserved either way, so even
  if Mixamo drops the embed, the saved PNG can be re-applied in Unity by UV).

Run headless (no Blender UI):

    blender --background --python tools/export_mesh_for_rigging.py -- \
        --in  unity/Assets/Art/Quat/FBX \
        --out MixamoUpload \
        --size 512

Paths are relative to the current working directory (run it from the repo root).
Only the mesh + its baked atlas are exported; animations and bones are dropped.
"""

import bpy
import os
import sys
import argparse


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--in", dest="in_dir", required=True, help="folder of source FBX files")
    p.add_argument("--out", dest="out_dir", required=True, help="folder to write mesh-only FBX")
    p.add_argument("--size", dest="size", type=int, default=512, help="baked atlas resolution (px)")
    return p.parse_args(argv)


def wipe_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    # purge orphaned datablocks so repeated imports don't leak memory/name collisions
    bpy.ops.outliner.orphans_purge(do_recursive=True)


def setup_bake_engine():
    """Cycles is required for object baking; configure it once for fast flat-colour bakes."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    # Flat solid colours need no light transport — 1 sample is plenty and keeps it fast.
    scene.cycles.samples = 1
    try:
        scene.cycles.device = "CPU"  # headless CI has no GPU; CPU is deterministic
    except Exception:
        pass
    scene.render.bake.margin = 4  # dilate island edges so bilinear/mip sampling doesn't bleed


def part_base_color(mat):
    """The flat solid colour of a Quaternius part (its Principled Base Color), or grey."""
    if mat and mat.use_nodes:
        b = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if b is not None:
            return tuple(b.inputs["Base Color"].default_value)
    return (0.8, 0.8, 0.8, 1.0)


def bake_mesh_atlas(mesh_obj, size, img_name):
    """UV-unwrap the mesh and bake every part's flat colour into one image. Returns the image.

    Baking the imported Principled materials directly (DIFFUSE pass) silently yields BLACK — the
    FBX-imported node graph won't emit under a bake. So we swap each material slot for a clean
    Emission shader of that part's base colour (a known-good bake target) and bake the EMIT pass,
    which is pure flat colour with no lighting. Slot indices are untouched, so each face still
    paints its own part's colour.
    """
    me = mesh_obj.data

    # 1) One non-overlapping UV set for the whole mesh (Quaternius meshes ship with none).
    bpy.ops.object.select_all(action="DESELECT")
    mesh_obj.select_set(True)
    bpy.context.view_layer.objects.active = mesh_obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02)  # ~66° in radians
    bpy.ops.object.mode_set(mode="OBJECT")

    # 2) Replace each slot's material with a clean Emission=base-colour shader whose active node is
    #    the shared target image (the bake target must be each material's active image node).
    img = bpy.data.images.new(img_name, width=size, height=size, alpha=False)
    img.generated_color = (0.0, 0.0, 0.0, 1.0)
    for slot in mesh_obj.material_slots:
        color = part_base_color(slot.material)
        m = bpy.data.materials.new((slot.material.name if slot.material else "part") + "_emit")
        m.use_nodes = True
        nt = m.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        emit = nt.nodes.new("ShaderNodeEmission")
        emit.inputs["Color"].default_value = color
        nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        tex.select = True
        nt.nodes.active = tex
        slot.material = m

    # 3) Bake the emission pass — each face paints its part's flat colour into the atlas.
    bpy.ops.object.bake(type="EMIT")
    return img


def collapse_to_baked_material(mesh_obj, img, mat_name):
    """Replace all part materials with ONE material that samples the baked atlas."""
    me = mesh_obj.data
    me.materials.clear()

    mat = bpy.data.materials.new(mat_name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None:
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])

    me.materials.append(mat)
    for poly in me.polygons:  # every face now points at the single slot
        poly.material_index = 0


def export_mesh_only(src_fbx, dst_fbx, png_path, size):
    wipe_scene()
    bpy.ops.import_scene.fbx(filepath=src_fbx)

    # Delete every armature; a mesh with an Armature modifier falls back to its
    # bind (rest) pose once the armature is gone — that's the A/T-pose Mixamo wants.
    for obj in list(bpy.data.objects):
        if obj.type == "ARMATURE":
            bpy.data.objects.remove(obj, do_unlink=True)

    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if not meshes:
        print("  !! no mesh found, skipped")
        return False

    # Drop leftover Armature modifiers (their target is gone) so the export is pure mesh.
    for m in meshes:
        for mod in [md for md in m.modifiers if md.type == "ARMATURE"]:
            m.modifiers.remove(mod)

    bpy.ops.object.select_all(action="DESELECT")
    for m in meshes:
        m.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # If several mesh objects exist, join into one so a single atlas covers the character.
    if len(meshes) > 1:
        bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active

    # Bake the multi-material colour into one atlas, then collapse to a single textured material.
    name = os.path.splitext(os.path.basename(dst_fbx))[0]
    img = bake_mesh_atlas(body, size, name + "_Atlas")

    # Persist the atlas next to the FBX so Unity can re-apply by UV even if Mixamo strips the embed.
    img.filepath_raw = png_path
    img.file_format = "PNG"
    img.save()

    collapse_to_baked_material(body, img, name + "_Baked")

    bpy.ops.export_scene.fbx(
        filepath=dst_fbx,
        use_selection=True,
        object_types={"MESH"},
        bake_anim=False,
        add_leaf_bones=False,
        path_mode="COPY",       # copy the texture next to the FBX so the reference resolves
        embed_textures=True,    # …and embed it IN the FBX so Mixamo's uploader sees the colour
    )
    return True


def main():
    args = parse_args()
    in_dir = os.path.abspath(args.in_dir)
    out_dir = os.path.abspath(args.out_dir)
    tex_dir = os.path.join(out_dir, "textures")
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(tex_dir, exist_ok=True)

    setup_bake_engine()

    fbxs = sorted(f for f in os.listdir(in_dir) if f.lower().endswith(".fbx"))
    if not fbxs:
        print("No FBX files in", in_dir)
        return

    print("Baking + exporting %d mesh-only FBX from %s -> %s" % (len(fbxs), in_dir, out_dir))
    ok = 0
    for name in fbxs:
        print("-", name)
        base = os.path.splitext(name)[0]
        png = os.path.join(tex_dir, base + ".png")
        try:
            if export_mesh_only(os.path.join(in_dir, name), os.path.join(out_dir, name), png, args.size):
                ok += 1
        except Exception as e:  # one bad character shouldn't sink the batch
            print("  !! FAILED:", e)
    print("Done: %d/%d exported to %s (atlases in %s)" % (ok, len(fbxs), out_dir, tex_dir))


if __name__ == "__main__":
    main()
