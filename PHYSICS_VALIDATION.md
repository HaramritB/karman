# Physics validation

Run: Unity EditMode tests, 2026-11-18, Unity 6000.6.9f1 on M3 Pro. **22 / 22 passed.**

## Ephemeris vs JPL Horizons (2026-01-01 00:00 TT, ICRF)
| Body | Angular error | Range error |
|---|---|---|
| Earth (heliocentric) | 0.05′ | 0.0028 % (4,524 km) |
| Mars | 0.33′ | 0.015 % |
| Jupiter | 0.04′ | 0.054 % |
| Neptune | 0.57′ | 0.008 % |
| Moon (geocentric) | 1.41′ | 0.0015 % (148 km) |
Earth heliocentric velocity within 0.05 km/s. GMST at J2000 matches 280.46061837° to 1e-6°.

## Orbital mechanics
- State ↔ elements round trip (LEO, Molniya, circular-equatorial GEO, hyperbolic): position ≤ 1 mm, velocity ≤ 1 µm/s.
- Circular 400 km: v = 7672.6 m/s, T = 5544.9 s (analytic).
- RK4, dt = 1 s, one LEO orbit vs analytic Kepler: **1.2 µm** position error.
- Velocity Verlet, dt = 2 s, 10 orbits (e = 0.1): max |ΔE/E| = **4.0e-7**, |Δh|/h = **1.6e-14**.
- Hyperbolic escape: speed at 30 days matches vis-viva with v∞ to 1 mm/s.
- SOI radii: Moon 66,183 km, Earth 924,649 km (Laplace, mean distance).
- Patched conics: vessel 70,000 km from the Moon transitions into the lunar SOI; state continuous.

## Spacecraft
- 1000 min on rails at 300 km: altitude constant to < 1 m, periapsis to < 1 mm.
- Full OAMS burn (2 × 445 N, Isp 273 s, 360 kg): Tsiolkovsky Δv 267.20 m/s = integrated Δv 267.20 m/s (< 0.2 %).

## In-game checks (player build, `-karmanCapture`, 1920×1080)
- One full orbital period on rails at high warp: closure **2–21 mm**, semi-major-axis drift 0.
- 2.2 s full-thrust burn: 0.72 kg propellant (expected ṁ = 0.332 kg/s), apoapsis +1.76 km.

## Vehicles (Phase 2, 31/31 tests)
- Two-stage kerolox stack: staging-simulation Δv equals Tsiolkovsky per stage (3,036.5 m/s; 10,519.8 vs 10,520.0 m/s).
- Burn time equals propellant / ṁ (9 × 914 kN at Isp 311 s).
- Tank capacity reproduces geometry × density at O/F 2.36 (3.7 × 32 m → 322 t).
- Strap-on boosters: stage ends at booster burnout; core carries over; auto-staging order verified.
- JSON save/load round trip preserves parts, resources and Δv exactly (also in the player: 12,698.552 m/s before and after).

## Atmosphere and ascent (Phase 3)
- US Standard Atmosphere 1976: pressure, density, temperature within 1 % of the published table at 0, 5, 11, 20, 30, 50, 70 km.
- FlightVehicle burn-to-depletion Δv equals the staging analysis (13,552.6 vs 13,556.3 m/s, 0.03 %).
- Headless pad-to-orbit (game models + `AscentGuidance`, KSC due east): core 190.0 × 172.0 km, heavy 194.5 × 171.3 km,
  i = 28.61°, max-Q 62 / 78 kPa.
- In the built game (automated run, 5× physics warp during ascent): Kármán I Heavy reached **191.0 × 170.3 km,
  i 28.61°, e 0.0016**, max-Q 73.1 kPa, 4,175 m/s left; one further orbit on rails: periapsis change 0.000 m.

## Maneuvers and prediction (Phase 4)
- Prograde node: post-burn apoapsis matches vis-viva to < 1 m; plane change: Δv = 2v·sin(Δi/2) gives exactly Δi with speed preserved.
- Burn time formula equals the rocket equation; escape trajectory hands over to the Sun's SOI; deorbit burn predicts impact.
- Trans-lunar injection planner (coplanar 300 km parking orbit): 3,188 m/s, Moon SOI after 40.5 h, periselene 100.0 km;
  the step-by-step flight trajectory enters the Moon's SOI within 22 s of the predicted time.
- In the built game: +40 m/s node at apoapsis predicted Ap 419.469 km; the node autopilot's finite 169 s burn achieved
  418.543 km (0.2 % low, the expected finite-burn loss) with Pe unchanged.

## Lunar mission (Phase 5, headless with the game's models)
- LOLA DEM orientation checked: highest point 10,685 m at 5.4°N 158.7°W, lowest −8,982 m in the South Pole–Aitken basin;
  Tycho's floor ~2 km below its rim.
- Powered descent from a 100 km lunar orbit (deorbit Δv 19.5 m/s) onto real terrain (far side, 4.8 km elevation):
  touchdown **1.5 m/s vertical, 0.0 m/s horizontal**, descent Δv 2,306 m/s.
- Lunar ascent with the return vehicle: 46.6 × 60.2 km orbit, 1,784 m/s.
- Trans-Earth injection planner: 1,038 m/s, Earth perigee 45.0 km; entry at 11,019 m/s, −6.14°, 64.9 h later.
- Entry (L/D 0.25 capsule, bank law, reefed 480 m² chute): peak **6.3 g**, peak heating 3.2 MW/m², heat load 351 of
  450 MJ/m², chute at 5.3 km, splashdown 11.1 m/s, downrange 2,124 km.
- In the built game (automated, Lunar Landing scenario): touchdown 1.5 m/s vertical / 0.01 m/s horizontal / 0.1° tilt;
  ascent to 45.0 × 60.3 km. Terrain: ~300–2,000 visible patches, 140–420 fps near the surface (M3 Pro, 1080p).

## Heavy launcher (Phase 6)
- Headless: Kármán V (2,714 t at liftoff) reaches 191.1 × 171.3 km; transfer stage 3,137 + descent stage 3,475 m/s
  ≥ TLI 3,190 + LOI 850 + landing 2,300 m/s.
- In the built game (automated): 190.5 × 170.2 km, i 28.61°, max-Q 38 kPa; remaining stages 3,091 / 3,475 / 3,100 m/s.

## Approximations (documented)
Rigid vehicle; lunar terrain detail below 1.9 km is procedural (not measured); drag only (no lift or aerodynamic torque yet); attitude authority = pod torque + engine gimbal; point-mass gravity of the dominant body only (no J2, no third-body perturbations yet); fixed ΔT = 69.2 s;
Moon series truncated (≈ 1.5′); planetary elements valid 1800–2050.
