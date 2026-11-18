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

## Phase 3–6
Launch & atmospheric flight · maneuver nodes & encounters · lunar landing & return · polish. See the brief.

## Known issues
- Earth surface from LEO is slightly hazy; atmosphere is single-scattering only.
- Sky panorama longitude direction (mirror) not verified against star positions.
- Moon libration ignored (prime meridian points at Earth).
