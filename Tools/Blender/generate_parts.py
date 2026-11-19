"""KÁRMÁN procedural part models, built to the dimensions in PartLibrary.cs.

Usage: Blender -b --python generate_parts.py -- <out_dir> [preview_dir]
Each part: metres, origin at the part centre, vehicle axis = Blender +Z (Unity +Y), PBR materials, FBX export.
Geometry detail comes from real construction features: weld rings, stringers, raceways, regeneratively cooled
nozzle tubes, turbopump/gas-generator plumbing, thrust structure, separation bolts.
"""
import bpy, bmesh, math, sys, os
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = argv[0]; PREV = argv[1] if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, color, metallic=0.0, rough=0.5, emission=None):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    return m

M = {}
def materials():
    M["white"] = mat("PaintWhite", (0.86, 0.87, 0.88), 0.0, 0.38)
    M["grey"] = mat("PaintGrey", (0.32, 0.33, 0.35), 0.0, 0.5)
    M["black"] = mat("CarbonBlack", (0.025, 0.025, 0.028), 0.0, 0.42)
    M["alu"] = mat("Aluminium", (0.80, 0.80, 0.82), 1.0, 0.32)
    M["steel"] = mat("Steel", (0.55, 0.56, 0.58), 1.0, 0.38)
    M["copper"] = mat("CopperAlloy", (0.62, 0.36, 0.22), 1.0, 0.42)
    M["nozzle"] = mat("NozzleSoot", (0.10, 0.09, 0.085), 0.85, 0.55)
    M["niobium"] = mat("NiobiumC103", (0.22, 0.21, 0.24), 1.0, 0.35)
    M["gold"] = mat("GoldFoil", (0.85, 0.62, 0.25), 1.0, 0.28)
    M["cork"] = mat("CorkTPS", (0.48, 0.36, 0.25), 0.0, 0.85)

def assign(obj, m):
    obj.data.materials.clear(); obj.data.materials.append(m)

def cyl(r, h, z=0.0, seg=64, m=None, r2=None, cap=True, name="cyl"):
    bpy.ops.mesh.primitive_cone_add(vertices=seg, radius1=r, radius2=r if r2 is None else r2, depth=h, location=(0, 0, z), end_fill_type='NGON' if cap else 'NOTHING')
    o = bpy.context.active_object; o.name = name
    if m: assign(o, m)
    bpy.ops.object.shade_auto_smooth(angle=math.radians(35))
    return o

def torus(R, r, z, m, seg=64):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=seg, minor_segments=8, location=(0, 0, z))
    o = bpy.context.active_object; assign(o, m)
    bpy.ops.object.shade_smooth()
    return o

def box(sx, sy, sz, loc, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.active_object; o.scale = (sx, sy, sz); assign(o, m)
    bpy.ops.object.transform_apply(scale=True)
    return o

def revolve(profile, m, seg=72, name="rev", thickness=0.0):
    """profile: list of (r, z) points; builds a surface of revolution about Z."""
    me = bpy.data.meshes.new(name); o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    bm = bmesh.new()
    rings = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        rings.append([bm.verts.new((r * math.cos(a), r * math.sin(a), z)) for r, z in profile])
    for i in range(seg):
        j = (i + 1) % seg
        for k in range(len(profile) - 1):
            bm.faces.new((rings[i][k], rings[j][k], rings[j][k + 1], rings[i][k + 1]))
    bm.to_mesh(me); bm.free()
    assign(o, m)
    bpy.context.view_layer.objects.active = o; o.select_set(True)
    if thickness > 0:
        mod = o.modifiers.new("solid", 'SOLIDIFY'); mod.thickness = thickness
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.shade_smooth()
    me.normals_domain if hasattr(me, "normals_domain") else None
    return o

def bell_profile(throat_r, exit_r, length, z_throat, chamber_r, chamber_len, n=28):
    """Rao-like parabolic bell (80% length) plus chamber and convergent section."""
    pts = []
    pts.append((chamber_r * 0.82, z_throat + chamber_len + throat_r * 0.6))
    pts.append((chamber_r, z_throat + chamber_len))
    pts.append((chamber_r, z_throat + throat_r * 2.2))
    pts.append((throat_r * 1.35, z_throat + throat_r * 0.8))
    pts.append((throat_r, z_throat))
    th_n, th_e = math.radians(32), math.radians(9)
    for i in range(1, n + 1):
        t = i / n
        # quadratic Bezier from throat to exit with the given wall angles
        p0 = Vector((throat_r, 0)); p2 = Vector((exit_r, -length))
        # Control point = intersection of the throat tangent (θn) and the exit tangent (θe), as in Rao's method.
        tn, te = math.tan(th_n), math.tan(th_e)
        b = (exit_r - throat_r - length * tn) / (te - tn)
        b = max(0.05 * length, min(0.95 * length, b))
        a = length - b
        p1 = Vector((throat_r + a * tn, -a))
        p = (1 - t) ** 2 * p0 + 2 * (1 - t) * t * p1 + t * t * p2
        pts.append((p.x, z_throat + p.y))
    return pts

def join_all(name):
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object; o.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return o

def export(name):
    obj = join_all(name)
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    bb = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    ext = [round(max(v[i] for v in bb) - min(v[i] for v in bb), 3) for i in range(3)]
    print("PART", name, "tris", tris, "extent", ext)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, mesh_smooth_type='FACE', use_selection=False)
    if PREV: preview(name, max(ext))

def preview(name, size):
    scn = bpy.context.scene
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scn.collection.objects.link(cam)
    cam.location = (size * 1.3, -size * 1.5, size * 0.5)
    cam.rotation_euler = (Vector((0, 0, 0)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scn.camera = cam
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN')); sun.data.energy = 4; sun.rotation_euler = (0.9, 0.1, 0.7)
    scn.collection.objects.link(sun)
    w = bpy.data.worlds.new("W"); scn.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.055, 0.065, 1)
    scn.render.engine = 'BLENDER_EEVEE'; scn.render.resolution_x = scn.render.resolution_y = 600
    scn.render.filepath = os.path.join(PREV, name + ".png")
    bpy.ops.render.render(write_still=True)

# ------------------------------------------------------------------ parts

def tank(name, d, h):
    reset(); materials()
    r = d / 2
    cyl(r, h, 0, 96, M["white"], name="skin")
    # weld / ring-frame seams every ~2 m
    n = max(2, int(h / 2.0))
    for i in range(n + 1):
        z = -h / 2 + h * i / n
        torus(r + 0.004, 0.012, z, M["grey"], 96)
    # cable raceway along the side and stringer hint plates
    box(0.16, 0.10, h * 0.96, (r + 0.05, 0, 0), M["white"])
    box(0.06, 0.04, h * 0.96, (r + 0.13, 0, 0), M["grey"])
    # feedline cover
    box(0.22, 0.16, h * 0.9, (-r - 0.07, 0, 0), M["white"])
    export(name)

def engine_core(scale=1.0, exit_r=0.46, length=1.6, top=1.45, vacuum=False):
    """Single gas-generator engine: chamber, bell, turbopump, GG exhaust, gimbal block."""
    tr = 0.13 * scale
    bell = bell_profile(tr, exit_r, length, top - 0.95 * scale, 0.21 * scale, 0.45 * scale)
    revolve(bell, M["copper"] if not vacuum else M["steel"], 72, "chamber", 0.012)
    # regen tube ribs on the bell (thin rings)
    for i in range(1, 8):
        t = i / 8
        idx = 5 + int(t * (len(bell) - 6))
        rr, zz = bell[idx]
        torus(rr + 0.006, 0.006, zz, M["steel"], 72)
    # injector dome + gimbal
    cyl(0.18 * scale, 0.18 * scale, top - 0.35 * scale, 32, M["steel"], name="dome")
    cyl(0.07 * scale, 0.22 * scale, top - 0.12 * scale, 24, M["alu"], name="gimbal")
    # turbopump + gas generator exhaust
    tp = cyl(0.11 * scale, 0.42 * scale, top - 0.5 * scale, 32, M["steel"], name="turbopump")
    tp.location.x += 0.32 * scale
    ex = cyl(0.05 * scale, 0.9 * scale, top - 1.15 * scale, 24, M["nozzle"], r2=0.04 * scale, name="ggexhaust")
    ex.location.x += 0.36 * scale
    for a in (0.0, 2.1, 4.2):
        p = cyl(0.025 * scale, 0.55 * scale, top - 0.45 * scale, 12, M["alu"], name="pipe")
        p.location.x += 0.17 * scale * math.cos(a); p.location.y += 0.17 * scale * math.sin(a)
        p.rotation_euler = (0.25 * math.sin(a), 0.25 * math.cos(a), 0)

def engine_booster():
    reset(); materials()
    engine_core(1.0, 0.46, 1.65, 1.45)
    export("engine_booster")

def engine_cluster9():
    reset(); materials()
    # thrust structure + heat shield
    cyl(1.85, 0.5, 1.35, 96, M["black"], name="octaweb")
    cyl(1.83, 0.06, 1.08, 96, M["cork"], name="heatshield")
    torus(1.85, 0.02, 1.6, M["grey"], 96)
    # 8 + 1 engines (Octaweb layout), each ~2.0 m tall below the shield
    pos = [(0, 0)] + [(1.25 * math.cos(2 * math.pi * k / 8), 1.25 * math.sin(2 * math.pi * k / 8)) for k in range(8)]
    for (x, y) in pos:
        before = set(bpy.context.scene.objects)
        engine_core(0.82, 0.38, 1.35, 1.05)
        for o in set(bpy.context.scene.objects) - before:
            o.location.x += x; o.location.y += y
    export("engine_cluster9")

def engine_vacuum():
    reset(); materials()
    engine_core(1.0, 0.2, 0.6, 2.45, vacuum=True)
    # radiatively cooled nozzle extension (niobium) to 3.0 m exit
    ext = [(0.2 + (1.5 - 0.2) * (t ** 0.75), 2.45 - 1.6 - 3.3 * t) for t in [i / 30 for i in range(31)]]
    revolve(ext, M["niobium"], 96, "extension", 0.008)
    torus(1.5, 0.02, ext[-1][1], M["steel"], 96)
    export("engine_vacuum")

def srb():
    reset(); materials()
    r, h = 0.8, 20.0
    cyl(r, h - 3.4, -0.6, 64, M["white"], name="case")
    for i in range(5):
        torus(r + 0.01, 0.025, -h / 2 + 1.2 + i * 3.6, M["grey"])
    nose = [(0.0, 10.0)] + [(r * math.sin(math.pi / 2 * t) ** 0.8, 10.0 - 2.4 * (1 - math.cos(math.pi / 2 * t))) for t in [i / 20 for i in range(1, 21)]]
    revolve(nose, M["white"], 64, "nose")
    noz = bell_profile(0.25, 0.62, 1.1, -h / 2 + 1.2, 0.5, 0.2)
    revolve(noz, M["nozzle"], 64, "nozzle", 0.02)
    box(0.12, 0.12, h * 0.8, (r + 0.05, 0, -0.5), M["grey"])
    export("srb")

def decoupler():
    reset(); materials()
    cyl(1.85, 0.4, 0, 96, M["alu"], name="ring")
    torus(1.86, 0.03, 0.18, M["grey"], 96); torus(1.86, 0.03, -0.18, M["grey"], 96)
    for k in range(24):
        a = 2 * math.pi * k / 24
        b = cyl(0.03, 0.12, 0, 8, M["steel"], name="bolt")
        b.rotation_euler = (0, math.pi / 2, a); b.location = (1.88 * math.cos(a), 1.88 * math.sin(a), 0)
    export("decoupler")

def radial_decoupler():
    reset(); materials()
    box(0.36, 0.5, 1.2, (0.18, 0, 0), M["alu"])
    box(0.5, 0.12, 0.2, (0.25, 0, 0.45), M["steel"]); box(0.5, 0.12, 0.2, (0.25, 0, -0.45), M["steel"])
    export("radial_decoupler")

def adapter():
    reset(); materials()
    cyl(1.85, 1.5, 0, 96, M["black"], r2=1.525, name="cone")
    torus(1.85, 0.02, -0.75, M["grey"], 96); torus(1.525, 0.02, 0.75, M["grey"], 96)
    export("adapter")

def interstage():
    reset(); materials()
    cyl(1.85, 4.0, 0, 96, M["black"], cap=False, name="shell")
    torus(1.85, 0.025, 2.0, M["grey"], 96); torus(1.85, 0.025, -2.0, M["grey"], 96)
    for k in range(4):
        a = 2 * math.pi * k / 4 + 0.4
        f = box(0.5, 0.08, 1.2, (1.88 * math.cos(a), 1.88 * math.sin(a), 1.2), M["alu"], rot=(0, 0, a))
    export("interstage")

def nose():
    reset(); materials()
    R, L = 1.85, 6.0
    rho = (R * R + L * L) / (2 * R)
    def ogive_r(z):  # z = height above the base
        return max(math.sqrt(max(rho * rho - z * z, 0)) + R - rho, 0.0)
    prof = [(ogive_r(L * i / 40), L * i / 40 - 3.0) for i in range(41)]
    revolve(prof, M["white"], 96, "ogive")
    torus(R, 0.02, -3.0, M["grey"], 96)
    for zz in (1.8, 3.6):   # fairing panel seams
        torus(ogive_r(zz) + 0.003, 0.008, zz - 3.0, M["grey"], 96)
    export("nose")

for f in (lambda: tank("tank_8", 3.7, 8.0), lambda: tank("tank_10", 3.7, 10.0), lambda: tank("tank_16", 3.7, 16.0), lambda: tank("tank_32", 3.7, 32.0), lambda: tank("tank_s15", 3.05, 1.5), lambda: tank("tank_s30", 3.05, 3.0), engine_booster, engine_cluster9, engine_vacuum, srb, decoupler, radial_decoupler, adapter, interstage, nose):
    f()
