import bpy, sys, math
from mathutils import Vector
args = sys.argv[sys.argv.index("--")+1:]
path, out = args[0], args[1]

bpy.ops.object.select_all(action="SELECT"); bpy.ops.object.delete()
bpy.ops.import_scene.fbx(filepath=path)

meshes = [o for o in bpy.data.objects if o.type == "MESH"]
# world bounds
mn = Vector(( 1e9, 1e9, 1e9)); mx = Vector((-1e9,-1e9,-1e9))
for o in meshes:
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c)
        mn = Vector((min(mn.x,w.x),min(mn.y,w.y),min(mn.z,w.z)))
        mx = Vector((max(mx.x,w.x),max(mx.y,w.y),max(mx.z,w.z)))
center = (mn+mx)/2
size = (mx-mn)
print("BOUNDS min=%s max=%s size=%s" % (tuple(round(v,2) for v in mn), tuple(round(v,2) for v in mx), tuple(round(v,2) for v in size)))
r = max(size.x, size.y, size.z)

# camera looking along -Y at the center, framed to bounds
cam_data = bpy.data.cameras.new("C"); cam = bpy.data.objects.new("C", cam_data); bpy.context.scene.collection.objects.link(cam)
cam.location = center + Vector((0, -r*2.2, 0))
cam.rotation_euler = (math.radians(90), 0, 0)
bpy.context.scene.camera = cam
# double-sided so backface issues still show a silhouette
for o in meshes:
    for m in o.data.materials:
        if m: m.use_backface_culling = False

light_d = bpy.data.lights.new("L","SUN"); light = bpy.data.objects.new("L",light_d); bpy.context.scene.collection.objects.link(light)
light.rotation_euler=(math.radians(60),0,math.radians(30)); light_d.energy=3

sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sc.render.resolution_x = 400; sc.render.resolution_y = 500
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("WROTE " + out)
