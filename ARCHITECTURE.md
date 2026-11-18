# Architecture

## Layers (brief §8 "numerical requirements")
1. **Astronomical state** (`Karman.Core`): `SolarSystem` updates every body from analytical ephemerides
   (`Ephemeris`: Standish mean elements for planets, truncated Meeus/ELP for the Moon, EMB split for Earth).
   `SpacecraftTrajectory` holds a vessel's double state relative to its reference body, advances it with RK4 when
   forces act and exactly on Kepler rails when coasting, and switches reference body at SOI boundaries (patched conics).
2. **Local spacecraft dynamics** (`VesselController`): attitude (angular velocity, reaction-wheel limit, SAS PD
   control), variable-mass propulsion (`PropulsionState`/`EngineSpec`, pressure-dependent Isp).
3. **Rendering transforms** (`RenderFrame`, `Coords`): camera-relative positions each frame; beyond 50,000 km objects
   are pulled in logarithmically and scaled uniformly (preserves angular size and depth order, keeps depth in range).
4. **Local collision/rigid bodies**: not yet used (Phase 3/5: launchpad, landing gear, terrain colliders near the vessel).

## Frame order per frame
`SimulationController.Update` (−200): warp, substeps (≤120 s) → `SolarSystem.SetTime` → `VesselController.Step`.
`OrbitCamera.LateUpdate` (50): sets `RenderFrame.Origin`, places the vessel view.
`CelestialBodyView` (100) and `SpaceLighting` (120): place bodies, feed shaders, sun light, eclipses, earthshine.
`MapView` (150): map camera/layer 31, orbit lines and labels.

## Rendering
- Planets are cube-sphere meshes (`SphereMeshFactory`) with per-pixel equirectangular sampling (dual-seam UVs).
- `Karman/Earth`: day albedo, ocean mask with GGX sun glint, normal map, clouds with projected cloud shadows,
  sunlight reddening by air mass, city lights on the night side.
- `Karman/Atmosphere`: per-pixel single-scattering Rayleigh+Mie ray march (32×6 samples) on a shell, ending at the
  planet, the atmosphere exit, or scene depth (so the vessel and Moon composite correctly).
- `Karman/Moon`: LROC colour, LOLA-derived slope normals, Lommel–Seeliger/Lambert blend, earthshine.
- `Karman/Planet`, `Ring`, `Sky` (Milky Way in galactic orientation), `Glare`, `Plume`, `Line`.
- URP: HDR, MSAA 4×, depth texture, ACES, bloom, soft shadows (250 m) for the vessel.

## Not yet built (see ROADMAP)
Quadtree terrain LOD and streaming, launch site, VAB/part system, aerodynamics, maneuver nodes, save/load, audio.
