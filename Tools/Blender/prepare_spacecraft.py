"""KÁRMÁN asset pipeline: clean a NASA glTF spacecraft and export a Unity-ready FBX.

Usage: Blender -b --python prepare_spacecraft.py -- <in.glb> <out.fbx> <real_length_m> [preview.png]
- joins meshes per material, scales so the longest axis equals the real length,
- orients the longest axis to +Z (Blender up -> Unity +Y),
- moves the origin to the mesh-volume centroid (approximate centre of mass),
- exports FBX with embedded textures.
"""
import bpy, bmesh, sys, mathutils, math

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst, length = argv[0], argv[1], float(argv[2])
preview = argv[3] if len(argv) > 3 else None

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o != obj:
        bpy.data.objects.remove(o, do_unlink=True)

# Orient longest bounding axis to +Z.
bb = [mathutils.Vector(c) for c in obj.bound_box]
ext = [max(v[i] for v in bb) - min(v[i] for v in bb) for i in range(3)]
axis = ext.index(max(ext))
if axis == 0:
    obj.rotation_euler = (0, math.radians(-90), 0)
elif axis == 1:
    obj.rotation_euler = (math.radians(90), 0, 0)
bpy.ops.object.transform_apply(rotation=True)

# Scale to real length.
bb = [mathutils.Vector(c) for c in obj.bound_box]
h = max(v.z for v in bb) - min(v.z for v in bb)
s = length / h
obj.scale = (s, s, s)
bpy.ops.object.transform_apply(scale=True)

# Remove doubles / recalc normals for a cleaner import.
bm = bmesh.new(); bm.from_mesh(obj.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bm.to_mesh(obj.data); bm.free()

# Origin at volume centroid (approximate CoM for a uniform-density shell).
bpy.ops.object.origin_set(type='ORIGIN_CENTER_OF_VOLUME', center='MEDIAN')
obj.location = (0, 0, 0)
obj.name = "Spacecraft"
bb = [mathutils.Vector(c) for c in obj.bound_box]
print("PREPARED", obj.name, "tris", sum(len(p.vertices) - 2 for p in obj.data.polygons),
      "extent_m", [round(max(v[i] for v in bb) - min(v[i] for v in bb), 3) for i in range(3)])

bpy.ops.export_scene.fbx(filepath=dst, use_selection=False, apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                         path_mode='COPY', embed_textures=True, mesh_smooth_type='FACE')

if preview:
    scn = bpy.context.scene
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scn.collection.objects.link(cam)
    cam.location = (length * 1.4, -length * 1.6, length * 0.6)
    d = mathutils.Vector((0, 0, 0)) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    scn.camera = cam
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN'))
    sun.data.energy = 4; sun.rotation_euler = (0.8, 0.2, 0.6)
    scn.collection.objects.link(sun)
    scn.render.engine = 'BLENDER_EEVEE'
    scn.render.resolution_x, scn.render.resolution_y = 900, 900
    scn.render.filepath = preview
    bpy.ops.render.render(write_still=True)
