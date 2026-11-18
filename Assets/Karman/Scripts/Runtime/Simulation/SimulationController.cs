using System;
using System.Globalization;
using Karman.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Karman.Simulation
{
    /// <summary>
    /// Owns simulation time, time warp and the solar system. Advances bodies (on rails) and the active vessel's
    /// astronomical state in fixed-size substeps so SOI changes are caught even at high warp.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class SimulationController : MonoBehaviour
    {
        public static SimulationController Instance { get; private set; }

        [Tooltip("Start date (UTC, ISO 8601). Empty = current time. Overridden by -karmanDate.")]
        public string startDateUtc = "";
        [Tooltip("Largest on-rails substep in simulated seconds.")]
        public double maxSubstep = 120;

        /// <summary>Warp levels up to this index keep full physics (thrust, drag, attitude); above it the vessel is on rails.</summary>
        public const int MaxPhysicsWarpIndex = 2;
        public static readonly double[] WarpLevels = { 1, 2, 5, 10, 50, 100, 1_000, 10_000, 100_000, 1_000_000 };

        public SolarSystem System { get; private set; }
        public double Time { get; private set; }
        public int WarpIndex { get; private set; }
        public double Warp => WarpLevels[WarpIndex];
        public bool Paused { get; set; }
        public Flight.VesselController ActiveVessel { get; set; }
        public double StartTime { get; private set; }

        public event Action<int> WarpChanged;

        void Awake()
        {
            Instance = this;
            DateTime start = DateTime.UtcNow;
            string arg = CommandLine.Value("-karmanDate");
            string s = !string.IsNullOrEmpty(arg) ? arg : startDateUtc;
            if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                start = parsed;
            Time = TimeSystem.FromUtc(start);
            StartTime = Time;
            System = SolarSystem.CreateDefault(Time);
            Application.targetFrameRate = -1;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.periodKey.wasPressedThisFrame) SetWarp(WarpIndex + 1);
                if (kb.commaKey.wasPressedThisFrame) SetWarp(WarpIndex - 1);
                if (kb.slashKey.wasPressedThisFrame) SetWarp(0);
                if (kb.pauseKey.wasPressedThisFrame || (kb.pKey.wasPressedThisFrame && !kb.ctrlKey.isPressed)) Paused = !Paused;
            }
            if (Paused) return;

            if (ActiveVessel != null && !ActiveVessel.CanWarp && WarpIndex > MaxPhysicsWarpIndex) SetWarp(MaxPhysicsWarpIndex);

            double dt = Math.Min(UnityEngine.Time.unscaledDeltaTime, 0.1) * Warp;
            bool physics = WarpIndex <= MaxPhysicsWarpIndex;   // full dynamics (thrust, drag, attitude) up to 5×
            double step = physics ? 0.05 : maxSubstep;
            int n = Math.Max(1, (int)Math.Ceiling(dt / step));
            n = Math.Min(n, 400);
            double h = dt / n;
            for (int i = 0; i < n; i++)
            {
                Time += h;
                System.SetTime(Time);
                if (ActiveVessel != null) ActiveVessel.Step(h, physics);
            }
        }

        public void SetWarp(int index)
        {
            index = Mathf.Clamp(index, 0, WarpLevels.Length - 1);
            if (index > MaxPhysicsWarpIndex && ActiveVessel != null && !ActiveVessel.CanWarp) index = MaxPhysicsWarpIndex;
            if (index == WarpIndex) return;
            WarpIndex = index;
            WarpChanged?.Invoke(index);
        }

        /// <summary>Jump the clock (used by automated capture). Bodies and the vessel are propagated on rails.</summary>
        public void AdvanceOnRails(double seconds)
        {
            int n = Math.Max(1, (int)Math.Ceiling(seconds / maxSubstep));
            double h = seconds / n;
            for (int i = 0; i < n; i++)
            {
                Time += h;
                System.SetTime(Time);
                if (ActiveVessel != null) ActiveVessel.Step(h, false);
            }
        }
    }

    public static class CommandLine
    {
        public static string Value(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == flag) return args[i + 1];
            return null;
        }
        public static bool Has(string flag) => Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;
    }
}
