# KÁRMÁN working conventions

- Unity 6000.6.9f1, URP 17.6, Input System only, Metal. C# in `Assets/Karman/Scripts`.
- Assemblies: `Karman.Core` (pure C#, no UnityEngine, all physics/ephemeris; unit-tested), `Karman.Runtime`
  (MonoBehaviours, rendering, UI), `Karman.Editor` (setup/build), `Karman.Tests.EditMode`.
- Simulation frame: right-handed, ICRF-aligned, Earth mean equator/equinox J2000, SI units, doubles.
  Unity mapping swaps Y/Z (`Coords`). Unity world axes never rotate; the camera sits at the origin and
  `RenderFrame.Origin` moves. Never put astronomical positions in floats outside `RenderFrame.Project/Local`.
- Time: seconds of TT past J2000 (`TimeSystem`).
- The Flight scene is generated: change `KarmanSetup.cs` and rerun it instead of hand-editing the scene.
- Verify with: EditMode tests, `KarmanSetup.BuildMac`, then the player `-karmanCapture` run; look at the PNGs.
- Disk is tight on the dev Mac (~11 GB free at start): keep textures ≤ 8k, no bulk datasets without asking.
- Secrets only in environment variables (`OPENAI_API_KEY`, `GEMINI_API_KEY`). No paid API calls without approval.
- Every external asset goes in `ASSET_MANIFEST.md`; every generated image in `AI_ASSET_LOG.md`.
- Keep docs short and true. Do not claim features that are not wired to the simulation.
