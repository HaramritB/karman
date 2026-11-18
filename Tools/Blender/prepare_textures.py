"""KÁRMÁN texture pipeline (runs inside Blender for its bundled numpy + image IO).

Usage: Blender -b --python prepare_textures.py -- <download_dir> <out_dir>
- Moon: LOLA 16-bit DEM (ldem_4_uint.tif, 0.5 m/unit offset) -> tangent-space-free
  object normal-perturbation map (RG = east/north slope, encoded), plus colour 4k -> PNG.
- Earth normal/specular TIFFs -> PNG.
"""
import bpy, sys, os
import numpy as np

src, out = sys.argv[sys.argv.index("--") + 1:][:2]
os.makedirs(out, exist_ok=True)

def load(name):
    img = bpy.data.images.load(os.path.join(src, name))
    if name.startswith("ldem"):
        img.colorspace_settings.name = 'Non-Color'
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, img.channels)
    return img, px

def save(px, name, w, h):
    img = bpy.data.images.new(name, w, h, alpha=True, float_buffer=False)
    if px.shape[2] == 3:
        px = np.concatenate([px, np.ones((h, w, 1), np.float32)], axis=2)
    img.pixels = px.ravel()
    img.filepath_raw = os.path.join(out, name)
    img.file_format = 'PNG'
    img.save()
    print("SAVED", name, w, h)

# Moon DEM -> slope map. ldem_4: 4 px/deg, 1440x720; value*0.5 m - 10 km? (uint: height(m)= v*0.5 - 20000 roughly)
img, px = load("ldem_4_uint.tif")
h, w = px.shape[:2]
height = px[:, :, 0] * 65535.0 * 0.5  # metres (relative; constant offset irrelevant for slopes)
R = 1737400.0
dlat = np.pi / h
lat = (np.arange(h)[::-1] + 0.5) / h * np.pi - np.pi / 2  # blender rows are bottom-up
dx_m = np.maximum(np.cos(lat), 0.02)[:, None] * R * (2 * np.pi / w)
dy_m = R * dlat
gx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) / (2 * dx_m)
gy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) / (2 * dy_m)
gy[0, :] = gy[1, :]; gy[-1, :] = gy[-2, :]
n = np.stack([-gx, -gy, np.ones_like(gx)], axis=2)
n /= np.linalg.norm(n, axis=2, keepdims=True)
enc = n * 0.5 + 0.5
save(enc, "moon_normal_eastnorth.png", w, h)
hn = (height - height.min()) / (height.max() - height.min())
save(np.repeat(hn[:, :, None], 3, axis=2), "moon_height.png", w, h)
print("MOON height range m", float(height.min()), float(height.max()))

img, px = load("lroc_color_poles_4k.tif"); h, w = px.shape[:2]
save(px[:, :, :3], "moon_color_4k.png", w, h)
for name in ["2k_earth_normal_map.tif", "2k_earth_specular_map.tif"]:
    img, px = load(name); h, w = px.shape[:2]
    save(px[:, :, :3], name.replace(".tif", ".png"), w, h)
