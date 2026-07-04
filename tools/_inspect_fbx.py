import bpy, sys
path = sys.argv[sys.argv.index("--")+1:][0]
bpy.ops.object.select_all(action="SELECT"); bpy.ops.object.delete()
bpy.ops.import_scene.fbx(filepath=path)
print("=== " + path + " ===")
for o in bpy.data.objects:
    d = o.dimensions
    print("  %-8s %-16s dims=(%.3f, %.3f, %.3f) scale=(%.3f,%.3f,%.3f) loc=(%.2f,%.2f,%.2f)"
          % (o.type, o.name, d.x, d.y, d.z, o.scale.x, o.scale.y, o.scale.z, o.location.x, o.location.y, o.location.z))
