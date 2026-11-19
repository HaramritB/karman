# Roadmap

## Phase 0 — Setup ✅ (2026-11-18)
- [x] Environment inspected: Unity 6000.6.9f1 (Mac/WebGL), Blender 5.2.3, M3 Pro 18 GB, ~11 GB free disk
- [x] URP project, git + LFS, modular assemblies, EditMode test suite (22 tests)
- [x] Blender headless pipeline (`Tools/Blender`): glTF clean-up/scale/pivot → FBX; DEM → normal maps
- [x] Image-generation MCP: inspected; no keys or servers configured (see AI_ASSET_LOG.md). Not set up yet.

## Phase 1 — Earth-orbit vertical slice ✅ first pass (2026-11-18)
- [x] Double-precision Kepler/ephemeris framework, validated against JPL Horizons
- [x] Earth: 8k day/night/clouds, ocean glint, cloud shadows, scattering atmosphere, city lights
- [x] Moon at true scale/position (LROC colour, LOLA relief); Sun with glare; 7 planets + Saturn rings
- [x] NASA Gemini spacecraft, attitude control + SAS modes, OAMS propulsion with real mass flow
- [x] Telemetry HUD, orbit camera (6 m → AU), time warp to 1,000,000×, eclipses, earthshine
- [x] Interactive map: Earth system → whole solar system, orbits from the same models, click-to-focus
- [ ] Quadtree terrain LOD for close approach (needed for Phase 3/5)
- [ ] Navball (Phase 3 flight UI)

## Phase 2 — Rocket construction ✅ first pass (2026-11-18)
- [x] Data-driven part definitions (13 parts) with real-hardware reference specs; tank capacity from geometry × density
- [x] Procedural Blender models built to those dimensions (`Tools/Blender/generate_parts.py`)
- [x] Craft tree model, staging simulation (per-stage Δv vac/ASL, TWR, burn time, CoM/CoT), auto-staging
- [x] VAB scene: searchable catalogue, node snapping, surface attach with 1–8× radial symmetry, select/delete,
      undo/redo, staging editor, readouts, CoM/CoT markers, versioned JSON save/load
- [x] Main menu → VAB / orbit sandbox; Esc returns to menu
- [ ] Launch the built craft (Phase 3); procedural/configurable tank lengths; part LODs (9-engine cluster is 165k tris)
- [ ] Part definitions as ScriptableObject/JSON assets for modding (currently `PartLibrary.cs`)

## Phase 3 — Launch and atmospheric flight ✅ first pass (2026-11-18)
- [x] VAB → LAUNCH: the built craft is assembled in flight and stands on LC-39A (28.608°N, 80.604°W), rotating with Earth
- [x] Launch complex: raised pad, flame trench, fixed service structure, water tower, crawlerway, floodlights,
      45 km curved ground patch with a local coastline; exhaust cloud particles
- [x] Multi-stage flight: staging (Space), propellant flow per section, solid motors, gimbal-limited attitude,
      US Standard Atmosphere 1976, Mach-dependent drag, hold-down release, ground contact, debris
- [x] Plumes that widen and dim with falling ambient pressure; physics warp up to 5×, rails above
- [x] Navball, stage stack with live Δv/TWR/burn time, Mach and dynamic pressure, event messages
- [x] Ascent assistant (G) using `AscentGuidance`, verified headless and in the built game: pad → 191 × 170 km orbit
- [ ] Aerodynamic torques/lift, reentry heating, parachutes (Phase 5), real terrain DEM tiles, launch audio
- [ ] Navball heading labels; free/tracking/cinematic cameras

## Phase 4 — Orbital mechanics and mission planning ✅ first pass (2026-11-19)
- [x] Maneuver nodes (prograde/normal/radial), rocket-equation burn time, click-to-place on the trajectory, Δv/time editor
- [x] Patched-conic predictor: analytic SOI exits, sampled+bisected SOI entries, impacts, closest approach to a target
- [x] Planned trajectory, encounter, apsis and node markers on the map; target Moon with relative inclination
- [x] Planning helpers: node at Ap/Pe, circularise, trans-lunar injection planner (periselene targeting by bisection)
- [x] Node execution autopilot (warp to burn, steer to Δv vector, throttle-down, staging); HUD and navball MNV marker
- [ ] Vessel-to-vessel rendezvous (needs a second active/passive vessel); multiple nodes

## Phase 5 — Lunar exploration ✅ first pass (2026-11-19)
- [x] LOLA global DEM (CGI Moon Kit, 16 px/deg) + procedural craters/relief below the DEM resolution, one height
      function for physics and rendering (`LunarTerrain`)
- [x] Quadtree cube-sphere terrain (worker-thread patches, skirts, horizon culling, double-precision placement)
- [x] Apollo LM (NASA model) split into descent/ascent stage parts; service engine; hypergolic tanks; nozzle-sized plumes
- [x] Terrain contact with touchdown limits (vertical/horizontal speed, tilt), landed state on the rotating Moon
- [x] Landing assist (deorbit → powered descent → terminal) and lunar ascent assist; trans-Earth injection planner
- [x] Earth entry: Sutton–Graves heating vs heat-shield capacity, capsule lift with bank law, reefed parachute,
      splashdown limit; plasma sheath and canopy visuals; HUD radar altitude, g, heat, chute
- [x] Scenarios: Lunar Mission (lander stack in a Moon-plane LEO), Lunar Landing (100 km lunar orbit)
- [ ] Higher-resolution DEM tiles at landing sites; terrain shadows; landing-leg suspension animation

## Phase 6 — Production polish (in progress, first pass 2026-11-19)
- [x] Kármán V heavy launcher (4 side cores, sustainer core, J-2X-class hydrolox transfer stage): pad → 190 × 170 km with
      the lunar stack and Δv for TLI + LOI + landing (headless and in the built game); "Moon Mission from the Pad"
- [x] Procedural, original audio: pressure-dependent engine roar (structure-borne hum in vacuum), wind, transients, UI
- [x] Settings: Low/Medium/High/Ultra presets (render scale, MSAA, shadow range, terrain split, atmosphere samples),
      1440p, volume (persisted)
- [x] Earthshine on lunar terrain from Earth's phase
- [ ] Terrain/vessel shadows on the Moon, Earth ground haze, sourced pad/VAB models, more parts, part fuel sliders in
      the VAB, rendezvous with a second vessel, aerodynamic torques, profiler pass

## Known issues
- Rigid vehicles: no structural loads or aerodynamic torques; attitude authority is idealised (pod torque + gimbal).
- Ground near the pad is washed out by atmospheric haze at low altitude, and its procedural detail is coarse.
- Pad structures are simple box geometry; replace with sourced/modelled assets.
- Earth surface from LEO is slightly hazy; atmosphere is single-scattering only.
- Sky panorama longitude direction (mirror) not verified against star positions.
- Moon libration ignored (prime meridian points at Earth).
