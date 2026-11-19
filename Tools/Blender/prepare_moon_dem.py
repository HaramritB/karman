"""LOLA DEM (CGI Moon Kit ldem_16_uint.tif, 16 px/deg, half-metre units) -> KÁRMÁN binary height field.

Output: moon_dem16.bytes = little-endian uint16, row 0 = north (+90° lat), column 0 = 180°W, 5760 x 2880.
Height (m, relative to the 1737.4 km reference sphere) = value * 0.5 - 10000  (documented in LunarTerrain.cs).
Also writes a 16 px/deg east/north slope normal map for far-field shading.
Usage: Blender -b --python prepare_moon_dem.py -- <ldem_16_uint.tif> <out_dir>
"""
import bpy, sys, os
import numpy as np
src, out = sys.argv[sys.argv.index("--") + 1:][:2]
os.makedirs(out, exist_ok=True)
img = bpy.data.images.load(src)
img.colorspace_settings.name = 'Non-Color'
w, h = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, img.channels)[:, :, 0]
raw = np.round(px * 65535.0).astype(np.uint16)
raw = raw[::-1, :]  # Blender rows are bottom-up; store north-first
raw.astype('<u2').tofile(os.path.join(out, "moon_dem16.bytes"))
hm = raw.astype(np.float32) * 0.5 - 10000.0
print("DEM", w, h, "height range m", float(hm.min()), float(hm.max()), "mean", float(hm.mean()))
# Slope normals (east, north, up) for shading, rows north-first -> flip back for Blender's bottom-up save.
R = 1737400.0
lat = (0.5 - (np.arange(h) + 0.5) / h) * np.pi
dx = np.maximum(np.cos(lat), 0.02)[:, None] * R * 2 * np.pi / w
dy = R * np.pi / h
gx = (np.roll(hm, -1, axis=1) - np.roll(hm, 1, axis=1)) / (2 * dx)
gy = (np.roll(hm, 1, axis=0) - np.roll(hm, -1, axis=0)) / (2 * dy)  # north = row-1
n = np.stack([-gx, -gy, np.ones_like(gx)], axis=2)
n /= np.linalg.norm(n, axis=2, keepdims=True)
enc = (n * 0.5 + 0.5)[::-1]
rgba = np.concatenate([enc, np.ones((h, w, 1), np.float32)], axis=2)
im = bpy.data.images.new("moon_normal16", w, h, alpha=True)
im.pixels = rgba.ravel()
im.filepath_raw = os.path.join(out, "moon_normal16.png"); im.file_format = 'PNG'; im.save()
print("NORMAL saved")
