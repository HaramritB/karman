using System;
using Karman.Core;
using Karman.Rendering;
using Karman.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Karman.Flight
{
    public enum SasMode { Off, Stability, Prograde, Retrograde, Normal, AntiNormal, RadialOut, RadialIn }

    /// <summary>
    /// Layer 2 (local spacecraft dynamics) for a single rigid vessel: attitude dynamics with reaction-wheel torque
    /// and SAS, variable-mass propulsion, and the link to the astronomical trajectory (layer 1). The Unity transform
    /// is only a view (layer 3): it is placed camera-relative every frame.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class VesselController : MonoBehaviour
    {
        [Header("Vessel")]
        public string vesselName = "Gemini";
        public double dryMass = 3_430;               // kg (Gemini spacecraft without OAMS propellant, approx.)
        public double propellant = 360;              // kg MMH/N2O4
        public double thrustPerEngine = 445;         // N, OAMS 100 lbf aft translation thrusters
        public int engineCount = 2;
        public double isp = 273;                     // s, vacuum
        [Tooltip("Maximum angular acceleration from attitude control, rad/s².")]
        public float maxAngularAcceleration = 0.35f;
        public Transform model;
        public Renderer plume;

        [Header("Initial orbit")]
        public double initialPeriapsisAlt = 280_000;
        public double initialApoapsisAlt = 320_000;
        public double initialInclinationDeg = 28.9;
        [Tooltip("Angle from the subsolar meridian along the orbit at start; ~75° gives a low sun.")]
        public double startSunAngleDeg = 72;

        public SpacecraftTrajectory Trajectory { get; private set; }
        public PropulsionState Propulsion { get; private set; }
        public SasMode Sas { get; set; } = SasMode.Stability;
        public float Throttle { get; set; }
        public double CurrentThrust { get; private set; }
        public Vector3 AngularVelocity { get; private set; }
        public bool WantsThrust => Throttle > 0 && Propulsion.Propellant > 0;
        public bool CanWarp => !WantsThrust && !Crashed;
        public bool Crashed { get; private set; }
        public double MissionElapsed => SimulationController.Instance.Time - launchTime;
        public Vector3d ThrustDirectionSim => Coords.ToSim(transform.up);

        Quaternion attitude = Quaternion.identity; // inertial (Unity world axes never rotate)
        double launchTime;
        static readonly int ThrottleId = Shader.PropertyToID("_Throttle");
        MaterialPropertyBlock plumeBlock;

        void Start()
        {
            var sim = SimulationController.Instance;
            sim.ActiveVessel = this;
            launchTime = sim.Time;
            Propulsion = new PropulsionState
            {
                DryMass = dryMass, Propellant = propellant, PropellantCapacity = propellant, EngineCount = engineCount,
                Engine = new EngineSpec { Name = "OAMS aft thrusters", VacuumThrust = thrustPerEngine, VacuumIsp = isp, SeaLevelIsp = isp * 0.55, MinThrottle = 0 },
            };
            var earth = sim.System.Get(SolarSystemData.Earth);
            Trajectory = new SpacecraftTrajectory(sim.System, earth, InitialState(earth, sim.System.Sun), sim.Time);
            // Start pointing prograde.
            var v = Coords.ToUnity(Trajectory.RelativeState.velocity.normalized);
            var up = Coords.ToUnity(Trajectory.RelativeState.position.normalized);
            attitude = Quaternion.LookRotation(-up, v) ; // local +Y (nose) prograde, belly toward Earth
            plumeBlock = new MaterialPropertyBlock();
        }

        StateVector InitialState(CelestialBody earth, CelestialBody sun)
        {
            Vector3d pole = earth.Orientation.Column(2);
            Vector3d toSun = (sun.Position - earth.Position).normalized;
            Vector3d s = (toSun - pole * Vector3d.Dot(toSun, pole)).normalized;      // subsolar direction in equator plane
            Vector3d e = Vector3d.Cross(pole, s);
            double a = startSunAngleDeg * Constants.Deg2Rad;
            Vector3d rHat = s * Math.Cos(a) - e * Math.Sin(a);                         // morning side, sun low
            double inc = initialInclinationDeg * Constants.Deg2Rad;
            // Rotate the pole about rHat to tilt the orbit plane; h ⟂ rHat by construction.
            Vector3d h = pole * Math.Cos(inc) + Vector3d.Cross(rHat, pole) * Math.Sin(inc);
            Vector3d vHat = Vector3d.Cross(h, rHat).normalized;
            double rp = earth.Radius + initialPeriapsisAlt, ra = earth.Radius + initialApoapsisAlt;
            double sma = (rp + ra) / 2;
            double speed = Math.Sqrt(earth.Mu * (2 / rp - 1 / sma));
            return new StateVector(rHat * rp, vHat * speed);
        }

        void Update()
        {
            ReadInput(Time.unscaledDeltaTime);
            UpdatePlume();
        }

        void ReadInput(float dt)
        {
            var kb = Keyboard.current;
            if (kb == null || Crashed) return;
            if (kb.leftShiftKey.isPressed) Throttle = Mathf.Clamp01(Throttle + dt * 0.8f);
            if (kb.leftCtrlKey.isPressed) Throttle = Mathf.Clamp01(Throttle - dt * 0.8f);
            if (kb.zKey.wasPressedThisFrame) Throttle = 1;
            if (kb.xKey.wasPressedThisFrame) Throttle = 0;
            if (kb.tKey.wasPressedThisFrame) Sas = Sas == SasMode.Off ? SasMode.Stability : SasMode.Off;
            if (kb.digit1Key.wasPressedThisFrame) Sas = SasMode.Stability;
            if (kb.digit2Key.wasPressedThisFrame) Sas = SasMode.Prograde;
            if (kb.digit3Key.wasPressedThisFrame) Sas = SasMode.Retrograde;
            if (kb.digit4Key.wasPressedThisFrame) Sas = SasMode.Normal;
            if (kb.digit5Key.wasPressedThisFrame) Sas = SasMode.AntiNormal;
            if (kb.digit6Key.wasPressedThisFrame) Sas = SasMode.RadialOut;
            if (kb.digit7Key.wasPressedThisFrame) Sas = SasMode.RadialIn;
        }

        Vector3 ControlInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return Vector3.zero;
            float pitch = (kb.sKey.isPressed ? 1 : 0) - (kb.wKey.isPressed ? 1 : 0);
            float yaw = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
            float roll = (kb.qKey.isPressed ? 1 : 0) - (kb.eKey.isPressed ? 1 : 0);
            return new Vector3(pitch, roll, -yaw);  // local axes: X pitch, Y roll (nose axis), Z yaw
        }

        /// <summary>Advance local dynamics and the astronomical state by dt simulated seconds.</summary>
        public void Step(double dt, bool realTime)
        {
            if (Crashed) return;
            var sim = SimulationController.Instance;
            if (realTime) UpdateAttitude((float)dt);
            else if (Sas >= SasMode.Prograde) { attitude = Quaternion.FromToRotation(attitude * Vector3.up, SasTarget()) * attitude; AngularVelocity = Vector3.zero; }

            Vector3d accel = Vector3d.zero;
            CurrentThrust = 0;
            if (realTime && Throttle > 0)
            {
                double m = Propulsion.Mass;
                CurrentThrust = Propulsion.Burn(Throttle, dt);
                accel = Coords.ToSim(attitude * Vector3.up) * (CurrentThrust / m);
            }
            Trajectory.Advance(dt, accel);
            if (Trajectory.Altitude < 0) { Crashed = true; Throttle = 0; Debug.Log($"[Karman] {vesselName} impacted {Trajectory.Reference.Definition.DisplayName}"); }
        }

        void UpdateAttitude(float dt)
        {
            Vector3 input = ControlInput();
            Vector3 w = AngularVelocity;
            Vector3 alphaLocal = input * maxAngularAcceleration;
            Vector3 alpha = attitude * alphaLocal;
            if (input.sqrMagnitude < 1e-6f && Sas != SasMode.Off)
            {
                if (Sas == SasMode.Stability)
                    alpha = -w / Mathf.Max(dt, 0.02f);
                else
                {
                    // PD controller toward the target direction.
                    Vector3 nose = attitude * Vector3.up;
                    Vector3 target = SasTarget();
                    Vector3 err = Vector3.Cross(nose, target);
                    float ang = Mathf.Asin(Mathf.Clamp(err.magnitude, -1, 1));
                    if (Vector3.Dot(nose, target) < 0) ang = Mathf.PI - ang;
                    Vector3 axis = err.sqrMagnitude > 1e-12f ? err.normalized : Vector3.Cross(nose, attitude * Vector3.right);
                    alpha = axis * (ang * 1.2f) - w * 2.2f;
                }
                alpha = Vector3.ClampMagnitude(alpha, maxAngularAcceleration);
            }
            w += alpha * dt;
            AngularVelocity = w;
            float speed = w.magnitude;
            if (speed > 1e-7f) attitude = Quaternion.AngleAxis(speed * Mathf.Rad2Deg * dt, w / speed) * attitude;
            attitude.Normalize();
        }

        public Vector3 SasTarget()
        {
            var s = Trajectory.RelativeState;
            Vector3 v = Coords.ToUnity(s.velocity.normalized);
            Vector3 r = Coords.ToUnity(s.position.normalized);
            Vector3 n = Coords.ToUnity(Vector3d.Cross(s.position, s.velocity).normalized);
            switch (Sas)
            {
                case SasMode.Prograde: return v;
                case SasMode.Retrograde: return -v;
                case SasMode.Normal: return n;
                case SasMode.AntiNormal: return -n;
                case SasMode.RadialOut: return Vector3.Cross(n, v).normalized; // in-plane, ⟂ velocity, away from body
                case SasMode.RadialIn: return -Vector3.Cross(n, v).normalized;
                default: return attitude * Vector3.up;
            }
        }

        /// <summary>Called by the camera after it has set the render origin for this frame.</summary>
        public void PlaceView()
        {
            if (Trajectory == null) return;
            transform.SetPositionAndRotation(RenderFrame.Local(Trajectory.HeliocentricPosition), attitude);
        }

        void UpdatePlume()
        {
            if (!plume) return;
            float t = (float)(CurrentThrust > 0 ? Throttle : 0);
            plume.enabled = t > 0.001f;
            plumeBlock.SetFloat(ThrottleId, t);
            plume.SetPropertyBlock(plumeBlock);
        }

        /// <summary>Set attitude directly (automated capture / scripted views).</summary>
        public void SetAttitude(Quaternion q) { attitude = q; AngularVelocity = Vector3.zero; }
        public Quaternion Attitude => attitude;
    }
}
