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

## Approximations (documented)
Point-mass gravity of the dominant body only (no J2, no third-body perturbations yet); fixed ΔT = 69.2 s;
Moon series truncated (≈ 1.5′); planetary elements valid 1800–2050.
