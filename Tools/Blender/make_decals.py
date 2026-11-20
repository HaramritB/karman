"""Decal textures: circular alpha for the AI-generated mission emblem, and the KÁRMÁN wordmark rendered from Inter.
Usage: Blender -b --python make_decals.py -- <decal_dir> <inter_semibold_ttf>"""
import bpy, sys, os, math
argv = sys.argv[sys.argv.index("--") + 1:]
out, font = argv[0], argv[1]

# 1. Emblem: alpha = inside the outer circle (soft 2 px edge).
img = bpy.data.images.load(os.path.join(out, "karman_emblem_raw.png"))
w, h = img.size
px = list(img.pixels)
cx, cy, R = (w - 1) / 2, (h - 1) / 2, w * 0.492
for y in range(h):
    for x in range(w):
        d = math.hypot(x - cx, y - cy)
        a = max(0.0, min(1.0, (R - d) / 2.0 + 0.5))
        px[(y * w + x) * 4 + 3] = a
img.pixels = px
img.filepath_raw = os.path.join(out, "karman_emblem.png"); img.file_format = "PNG"; img.save()

# 2. Wordmark: dark text, transparent background, 4096 x 512 (vertical on the tank, so rotate in the decal).
bpy.ops.wm.read_factory_settings(use_empty=True)
scn = bpy.context.scene
scn.render.engine = "BLENDER_EEVEE"; scn.render.film_transparent = True
scn.render.resolution_x, scn.render.resolution_y = 4096, 640
scn.render.image_settings.color_mode = "RGBA"
bpy.ops.object.text_add(location=(0, 0, 0))
t = bpy.context.active_object
t.data.body = "KÁRMÁN"
t.data.font = bpy.data.fonts.load(font)
t.data.align_x = "CENTER"; t.data.align_y = "CENTER"; t.data.space_character = 1.35
m = bpy.data.materials.new("Ink"); m.use_nodes = True
em = m.node_tree.nodes.new("ShaderNodeEmission"); em.inputs[0].default_value = (0.02, 0.025, 0.035, 1)
m.node_tree.links.new(em.outputs[0], m.node_tree.nodes["Material Output"].inputs[0])
t.data.materials.append(m)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scn.collection.objects.link(cam)
cam.data.type = "ORTHO"; cam.location = (0, 0, 10); scn.camera = cam
bpy.context.view_layer.update()
dims = t.dimensions
cam.data.ortho_scale = max(dims.x * 1.08, dims.y * 1.7 * 4096 / 640); cam.location.y = dims.y * 0.12
scn.view_settings.view_transform = "Standard"
scn.render.filepath = os.path.join(out, "karman_wordmark.png")
bpy.ops.render.render(write_still=True)
print("DECALS OK", w, h, dims.x, dims.y)
