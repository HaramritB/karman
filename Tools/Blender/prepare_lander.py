"""Split the NASA Apollo Lunar Module glTF into descent and ascent stage parts for KÁRMÁN.

Usage: Blender -b --python prepare_lander.py -- <in.glb> <out_dir> [preview.png]
Scales to the real 7.04 m height (legs deployed), splits mesh objects at the stage interface (46% of height,
matching the LM's 3.23 m descent / 2.83 m ascent stages), puts each part's origin at its bounding-box centre,
and prints the interface-plane offset of each part for its attachment node.
"""
import bpy, sys, os, mathutils

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
preview = argv[2] if len(argv) > 2 else None
os.makedirs(out, exist_ok=True)

def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    for o in bpy.context.scene.objects: o.select_set(False)
    zmin = min((o.matrix_world @ mathutils.Vector(c)).z for o in objs for c in o.bound_box)
    zmax = max((o.matrix_world @ mathutils.Vector(c)).z for o in objs for c in o.bound_box)
    return objs, zmin, zmax

objs, zmin, zmax = load()
height = zmax - zmin
scale = 7.04 / height
interface = zmin + 0.46 * height

def centre_z(o):
    bb = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
    return (min(v.z for v in bb) + max(v.z for v in bb)) / 2

for stage in ("descent", "ascent"):
    objs, _, _ = load()
    keep = [o for o in objs if (centre_z(o) < interface) == (stage == "descent")]
    for o in objs:
        if o not in keep: bpy.data.objects.remove(o, do_unlink=True)
    for o in keep: o.select_set(True)
    bpy.context.view_layer.objects.active = keep[0]
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in list(bpy.context.scene.objects):
        if o != obj: bpy.data.objects.remove(o, do_unlink=True)
    obj.scale = (scale, scale, scale)
    bpy.ops.object.transform_apply(scale=True)
    bb = [mathutils.Vector(c) for c in obj.bound_box]
    c = mathutils.Vector([(min(v[i] for v in bb) + max(v[i] for v in bb)) / 2 for i in range(3)])
    iface = interface * scale - c.z
    for v in obj.data.vertices: v.co -= c
    ext = [round(max(v[i] for v in bb) - min(v[i] for v in bb), 3) for i in range(3)]
    obj.name = f"LM_{stage}"
    print("STAGE", stage, "extent_m", ext, "interface_y", round(iface, 3), "tris", sum(len(p.vertices) - 2 for p in obj.data.polygons))
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, f"lm_{stage}.fbx"), apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, path_mode='COPY', embed_textures=True, mesh_smooth_type='FACE')
