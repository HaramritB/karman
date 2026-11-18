# Asset manifest

| Asset (in project) | Source | Author | Version / date | License | Modifications | Attribution |
|---|---|---|---|---|---|---|
| `Art/Models/Spacecraft/Gemini.fbx` (source `Tools/SourceModels/Gemini.glb`) | https://github.com/nasa/NASA-3D-Resources/tree/master/3D%20Models/Gemini | NASA | repo master, fetched 2026-11-18 | "Free and without copyright" (repo README); NASA media usage guidelines apply (no endorsement, no NASA insignia use) | Blender `prepare_spacecraft.py`: joined meshes, removed doubles, long axis → +Z, scaled to 5.80 m length (real Gemini: 5.8 m × 3.05 m), origin at volume centroid, FBX with embedded textures | Courtesy NASA |
| `Art/Textures/Earth/8k_earth_{daymap,nightmap,clouds}.jpg`, `2k_earth_{normal,specular}_map.png` | https://www.solarsystemscope.com/textures/ | Solar System Scope (INOVE), based on NASA imagery | fetched 2026-11-18 | CC BY 4.0 | normal/specular TIFF → PNG | "Textures: Solar System Scope, CC BY 4.0" |
| `Art/Textures/Planets/2k_*.jpg`, `2k_saturn_ring_alpha.png` | https://www.solarsystemscope.com/textures/ | Solar System Scope | fetched 2026-11-18 | CC BY 4.0 | none | as above |
| `Art/Textures/Sky/8k_stars_milky_way.jpg` | https://www.solarsystemscope.com/textures/ | Solar System Scope (ESO-based) | fetched 2026-11-18 | CC BY 4.0 | none | as above |
| `Art/Textures/Moon/moon_color_4k.png` | https://svs.gsfc.nasa.gov/4720/ (`lroc_color_poles_4k.tif`) | NASA SVS, Ernie Wright; LRO LROC team | page modified 2026-01-09 | NASA public-domain media | TIFF → PNG | NASA's Scientific Visualization Studio |
| `Art/Textures/Moon/moon_normal_eastnorth.png`, `moon_height.png` | https://svs.gsfc.nasa.gov/4720/ (`ldem_4_uint.tif`, LOLA 4 px/deg) | NASA SVS; LRO LOLA team | as above | NASA public-domain media | Blender `prepare_textures.py`: heights → east/north slope normals (R = 1737.4 km), normalised height | NASA's Scientific Visualization Studio |

Candidates evaluated for later phases (not imported): NASA 3D `Apollo Lunar Module.glb` (98k tris, 3 textures),
`Saturn V.glb`, `Apollo Soyuz.glb` (single merged mesh; Apollo CSM would need separation in Blender).
