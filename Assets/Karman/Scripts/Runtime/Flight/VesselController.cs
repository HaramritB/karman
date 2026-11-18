using System;
using Karman.Core;
using Karman.Core.Vehicles;
using Karman.Rendering;
using Karman.Simulation;
using Karman.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Karman.Flight
{
    public enum SasMode { Off, Stability, Prograde, Retrograde, Normal, AntiNormal, RadialOut, RadialIn, SurfacePrograde }

    /// <summary>
    /// Layer 2 (local spacecraft dynamics) for the active vessel: multi-stage propulsion (<see cref="FlightVehicle"/>),
    /// attitude dynamics with reaction-wheel + engine-gimbal torque and SAS, aerodynamic drag in Earth's atmosphere,
    /// pad hold-down and ground contact, and the link to the astronomical trajectory (layer 1). The Unity transform is
    /// only a view placed camera-relative each frame.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class VesselController : MonoBehaviour
    {
        [Header("Sandbox (no launch request): Gemini in orbit")]
        public double initialPeriapsisAlt = 280_000;
        public double initialApoapsisAlt = 320_000;
        public double initialInclinationDeg = 28.9;
        public double startSunAngleDeg = 72;

        [Header("Launch site: LC-39A, Kennedy Space Center")]
        public double padLatitude = 28.60822;
        public double padLongitude = -80.60411;
        public double padHeight = 15;

        [Tooltip("Reaction-wheel/RCS torque available from command parts, N·m.")]
        public float commandTorque = 4500f;
        public VesselView view;

        public string vesselName = "Gemini";
        public FlightVehicle Vehicle { get; private set; }
        public Func<string, PartDefinition> Lookup { get; private set; }
        public SpacecraftTrajectory Trajectory { get; private set; }
        public SasMode Sas { get; set; } = SasMode.Stability;
        public float Throttle { get; set; }
        public double CurrentThrust { get; private set; }
        public double CurrentMaxThrust { get; private set; }
        public double CurrentIsp { get; private set; }
        public Vector3 AngularVelocity { get; private set; }
        public bool Landed { get; private set; }
        public bool Crashed { get; private set; }
        public bool Launched { get; private set; }
        public AtmosphereSample Air { get; private set; }
        public double DynamicPressure { get; private set; }
        public double Mach { get; private set; }
        public double MaxQ { get; private set; }
        /// <summary>Autopilot override: Unity-world direction the nose should point.</summary>
        public Vector3? AttitudeTarget { get; set; }
        public bool WantsThrust => !Crashed && Throttle > 0 && Vehicle.BurningEngines().Count > 0;
        public bool InAtmosphere => Trajectory != null && Trajectory.Reference.Definition.Atmosphere != null && Trajectory.Altitude < EarthAtmosphere.Cutoff;
        public bool CanWarp => !WantsThrust && !Crashed && !InAtmosphere && !Landed;
        public double MissionElapsed => Launched ? SimulationController.Instance.Time - launchTime : 0;
        /// <summary>Height of the centre of mass above the vessel's lowest point (m).</summary>
        public double StandoffHeight { get; private set; }
        public float Length { get; private set; } = 6;
        public bool IsLaunchFromPad { get; private set; }
        public event Action<string> Event;

        Quaternion attitude = Quaternion.identity;
        double launchTime;
        Vector3d landedBodyFixed;

        void Start()
        {
            var sim = SimulationController.Instance;
            sim.ActiveVessel = this;
            Lookup = PartLibrary.Lookup(PartLibrary.CreateStarter());
            var earth = sim.System.Get(SolarSystemData.Earth);
            if (LaunchRequest.Craft != null)
            {
                Vehicle = new FlightVehicle(LaunchRequest.Craft, Lookup);
                vesselName = LaunchRequest.Craft.Name;
                LaunchRequest.Craft = null;
                IsLaunchFromPad = true;
                RefreshGeometry();
                landedBodyFixed = PadBodyFixed(earth);
                Landed = true;
                Trajectory = new SpacecraftTrajectory(sim.System, earth, LandedState(earth), sim.Time);
                attitude = SurfaceAttitude(earth);
                Sas = SasMode.Stability;
            }
            else
            {
                var c = new Craft { Name = "Gemini" };
                c.Add(Lookup("pod.gemini"), -1, null, null);
                AutoStaging.Apply(c, Lookup);
                Vehicle = new FlightVehicle(c, Lookup);
                RefreshGeometry();
                Launched = true;
                launchTime = sim.Time;
                Trajectory = new SpacecraftTrajectory(sim.System, earth, OrbitState(earth, sim.System.Sun), sim.Time);
                var v = Coords.ToUnity(Trajectory.RelativeState.velocity.normalized);
                var up = Coords.ToUnity(Trajectory.RelativeState.position.normalized);
                attitude = Quaternion.LookRotation(-up, v);
            }
            Vehicle.PartsDetached += ids =>
            {
                RefreshGeometry();
                if (view) view.OnDetached(ids);
                Event?.Invoke($"Separation: {ids.Count} parts jettisoned");
            };
            if (view) view.Build(this);
        }

        void RefreshGeometry()
        {
            Vehicle.VerticalExtent(out float bottom, out float top);
            var com = Vehicle.CenterOfMass;
            StandoffHeight = com[1] - bottom;
            Length = Mathf.Max(3, top - bottom);
        }

        // ---------------------------------------------------------------- initial states

        Vector3d PadBodyFixed(CelestialBody earth)
        {
            double lat = padLatitude * Constants.Deg2Rad, lon = padLongitude * Constants.Deg2Rad;
            var up = new Vector3d(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
            return up * (earth.Radius + padHeight + StandoffHeight);
        }

        StateVector LandedState(CelestialBody body)
        {
            Vector3d r = body.Orientation * landedBodyFixed;
            double omega = 2 * Math.PI / body.Definition.SiderealRotationPeriod;
            Vector3d pole = body.Orientation.Column(2);
            return new StateVector(r, Vector3d.Cross(pole * omega, r));
        }

        Quaternion SurfaceAttitude(CelestialBody body)
        {
            Vector3d r = body.Orientation * landedBodyFixed;
            Vector3d pole = body.Orientation.Column(2);
            Vector3d east = Vector3d.Cross(pole, r).normalized;
            return Quaternion.LookRotation(Coords.ToUnity(east), Coords.ToUnity(r.normalized));
        }

        StateVector OrbitState(CelestialBody earth, CelestialBody sun)
        {
            Vector3d pole = earth.Orientation.Column(2);
            Vector3d toSun = (sun.Position - earth.Position).normalized;
            Vector3d s = (toSun - pole * Vector3d.Dot(toSun, pole)).normalized;
            Vector3d e = Vector3d.Cross(pole, s);
            double a = startSunAngleDeg * Constants.Deg2Rad;
            Vector3d rHat = s * Math.Cos(a) - e * Math.Sin(a);
            double inc = initialInclinationDeg * Constants.Deg2Rad;
            Vector3d h = pole * Math.Cos(inc) + Vector3d.Cross(rHat, pole) * Math.Sin(inc);
            Vector3d vHat = Vector3d.Cross(h, rHat).normalized;
            double rp = earth.Radius + initialPeriapsisAlt, ra = earth.Radius + initialApoapsisAlt;
            double sma = (rp + ra) / 2;
            return new StateVector(rHat * rp, vHat * Math.Sqrt(earth.Mu * (2 / rp - 1 / sma)));
        }

        // ---------------------------------------------------------------- input

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || Crashed || Map.MapView.IsActive && kb.tabKey.isPressed) return;
            float dt = Time.unscaledDeltaTime;
            if (kb.leftShiftKey.isPressed) Throttle = Mathf.Clamp01(Throttle + dt * 0.8f);
            if (kb.leftCtrlKey.isPressed) Throttle = Mathf.Clamp01(Throttle - dt * 0.8f);
            if (kb.zKey.wasPressedThisFrame) Throttle = 1;
            if (kb.xKey.wasPressedThisFrame) Throttle = 0;
            if (kb.spaceKey.wasPressedThisFrame) StageNext();
            if (kb.tKey.wasPressedThisFrame) Sas = Sas == SasMode.Off ? SasMode.Stability : SasMode.Off;
            if (kb.digit1Key.wasPressedThisFrame) Sas = SasMode.Stability;
            if (kb.digit2Key.wasPressedThisFrame) Sas = SasMode.Prograde;
            if (kb.digit3Key.wasPressedThisFrame) Sas = SasMode.Retrograde;
            if (kb.digit4Key.wasPressedThisFrame) Sas = SasMode.Normal;
            if (kb.digit5Key.wasPressedThisFrame) Sas = SasMode.AntiNormal;
            if (kb.digit6Key.wasPressedThisFrame) Sas = SasMode.RadialOut;
            if (kb.digit7Key.wasPressedThisFrame) Sas = SasMode.RadialIn;
            if (kb.digit8Key.wasPressedThisFrame) Sas = SasMode.SurfacePrograde;
        }

        public void StageNext()
        {
            if (Crashed || Vehicle.NextStage < 0) return;
            int s = Vehicle.NextStage;
            Vehicle.Stage();
            Event?.Invoke($"Stage {s} activated");
        }

        Vector3 ControlInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return Vector3.zero;
            float pitch = (kb.sKey.isPressed ? 1 : 0) - (kb.wKey.isPressed ? 1 : 0);
            float yaw = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
            float roll = (kb.qKey.isPressed ? 1 : 0) - (kb.eKey.isPressed ? 1 : 0);
            return new Vector3(pitch, roll, -yaw);
        }

        // ---------------------------------------------------------------- simulation

        /// <summary>Angular acceleration authority: command-pod torque plus engine gimbal torque about the CoM.</summary>
        public float MaxAngularAcceleration
        {
            get
            {
                double m = Vehicle.Mass;
                double inertia = m * (Length * Length / 12.0 + 1.0);
                double gimbalTorque = CurrentThrust * Math.Sin(Vehicle.MaxGimbalRadians) * Math.Max(1, StandoffHeight);
                return (float)Math.Min(1.5, (commandTorque + gimbalTorque) / inertia);
            }
        }

        /// <summary>Advance by dt simulated seconds (called by SimulationController in substeps).</summary>
        public void Step(double dt, bool realTime)
        {
            if (Crashed) return;
            var sim = SimulationController.Instance;
            var body = Trajectory.Reference;
            bool hasAtmo = body.Definition.Atmosphere != null;
            Air = hasAtmo ? EarthAtmosphere.Sample(Trajectory.Altitude) : default;

            if (realTime) UpdateAttitude((float)dt);
            else if (Sas >= SasMode.Prograde || AttitudeTarget.HasValue)
            {
                attitude = Quaternion.FromToRotation(attitude * Vector3.up, SasTarget()) * attitude;
                AngularVelocity = Vector3.zero;
            }

            double m0 = Vehicle.Mass;
            double isp = 0, maxT = 0;
            CurrentThrust = realTime ? Vehicle.Burn(Throttle, dt, Air.Pressure, out isp, out maxT) : 0;
            CurrentIsp = isp; CurrentMaxThrust = maxT;
            Vector3d thrustAccel = Coords.ToSim(attitude * Vector3.up) * (CurrentThrust / m0);

            if (Landed)
            {
                double g = body.Mu / Trajectory.RelativeState.position.sqrMagnitude;
                if (CurrentThrust / m0 > g * 1.0005)
                {
                    Landed = false;
                    if (!Launched) { Launched = true; launchTime = sim.Time; Event?.Invoke("Liftoff"); }
                }
                else
                {
                    Trajectory.SetState(body, LandedState(body), Trajectory.Time + dt);
                    attitude = Quaternion.FromToRotation(attitude * Vector3.up, Coords.ToUnity(Trajectory.RelativeState.position.normalized)) * attitude;
                    AngularVelocity = Vector3.zero;
                    return;
                }
            }

            double area = Vehicle.FrontalArea;
            Vector3d pole = body.Orientation.Column(2);
            double omega = 2 * Math.PI / body.Definition.SiderealRotationPeriod;
            var air = Air;
            Func<Vector3d, Vector3d, Vector3d> forces = (r, v) =>
            {
                Vector3d a = thrustAccel;
                if (air.Density > 0)
                {
                    Vector3d vRel = v - Vector3d.Cross(pole * omega, r);
                    double speed = vRel.magnitude;
                    if (speed > 1e-3)
                    {
                        double cd = EarthAtmosphere.DragCoefficient(speed / air.SpeedOfSound);
                        a -= vRel * (0.5 * air.Density * speed * cd * area / m0);
                    }
                }
                return a;
            };
            bool integrate = thrustAccel.sqrMagnitude > 0 || (hasAtmo && Trajectory.Altitude < EarthAtmosphere.Cutoff);
            if (integrate) Trajectory.Advance(dt, forces, 0.02);
            else Trajectory.Advance(dt, Vector3d.zero);

            Vector3d vr = Trajectory.SurfaceVelocity;
            DynamicPressure = 0.5 * Air.Density * vr.sqrMagnitude;
            Mach = Air.SpeedOfSound > 0 ? vr.magnitude / Air.SpeedOfSound : 0;
            MaxQ = Math.Max(MaxQ, DynamicPressure);

            double groundClearance = Trajectory.Altitude - StandoffHeight;
            if (groundClearance < 0 && Trajectory.VerticalSpeed < 0)
            {
                if (vr.magnitude < 6)
                {
                    landedBodyFixed = body.Orientation.Transposed * (Trajectory.RelativeState.position.normalized * (body.Radius + StandoffHeight));
                    Landed = true; Throttle = 0;
                    Event?.Invoke("Touchdown");
                }
                else
                {
                    Crashed = true; Throttle = 0;
                    Event?.Invoke($"Impact at {vr.magnitude:F0} m/s");
                }
            }
        }

        void UpdateAttitude(float dt)
        {
            Vector3 input = ControlInput();
            Vector3 w = AngularVelocity;
            float maxA = MaxAngularAcceleration;
            Vector3 alpha = attitude * (input * maxA);
            bool hold = input.sqrMagnitude < 1e-6f && (Sas != SasMode.Off || AttitudeTarget.HasValue);
            if (hold)
            {
                if (Sas == SasMode.Stability && !AttitudeTarget.HasValue) alpha = -w / Mathf.Max(dt, 0.02f);
                else
                {
                    Vector3 nose = attitude * Vector3.up;
                    Vector3 target = SasTarget();
                    Vector3 err = Vector3.Cross(nose, target);
                    float ang = Mathf.Asin(Mathf.Clamp(err.magnitude, -1, 1));
                    if (Vector3.Dot(nose, target) < 0) ang = Mathf.PI - ang;
                    Vector3 axis = err.sqrMagnitude > 1e-12f ? err.normalized : attitude * Vector3.right;
                    // PD gains scaled to the available authority (critically damped for a double integrator).
                    float kp = 2.0f * maxA, kd = 2.0f * Mathf.Sqrt(kp);
                    alpha = axis * (ang * kp) - w * kd;
                }
                alpha = Vector3.ClampMagnitude(alpha, maxA);
            }
            w += alpha * dt;
            AngularVelocity = w;
            float speed = w.magnitude;
            if (speed > 1e-7f) attitude = Quaternion.AngleAxis(speed * Mathf.Rad2Deg * dt, w / speed) * attitude;
            attitude.Normalize();
        }

        public Vector3 SasTarget()
        {
            if (AttitudeTarget.HasValue) return AttitudeTarget.Value.normalized;
            var s = Trajectory.RelativeState;
            Vector3 v = Coords.ToUnity(s.velocity.normalized);
            Vector3 n = Coords.ToUnity(Vector3d.Cross(s.position, s.velocity).normalized);
            switch (Sas)
            {
                case SasMode.Prograde: return v;
                case SasMode.Retrograde: return -v;
                case SasMode.Normal: return n;
                case SasMode.AntiNormal: return -n;
                case SasMode.RadialOut: return Vector3.Cross(n, v).normalized;
                case SasMode.RadialIn: return -Vector3.Cross(n, v).normalized;
                case SasMode.SurfacePrograde: return Coords.ToUnity(Trajectory.SurfaceVelocity.normalized);
                default: return attitude * Vector3.up;
            }
        }

        /// <summary>Called by the camera after it has set the render origin for this frame.</summary>
        public void PlaceView()
        {
            if (Trajectory == null) return;
            transform.SetPositionAndRotation(RenderFrame.Local(Trajectory.HeliocentricPosition), attitude);
        }

        public void SetAttitude(Quaternion q) { attitude = q; AngularVelocity = Vector3.zero; }
        public Quaternion Attitude => attitude;

        /// <summary>Local up / east / north unit vectors (Unity world) at the vessel.</summary>
        public void LocalFrame(out Vector3 up, out Vector3 east, out Vector3 north)
        {
            var r = Trajectory.RelativeState.position;
            var pole = Trajectory.Reference.Orientation.Column(2);
            var e = Vector3d.Cross(pole, r).normalized;
            up = Coords.ToUnity(r.normalized);
            east = Coords.ToUnity(e);
            north = Coords.ToUnity(Vector3d.Cross(r.normalized, e));
        }
    }
}
