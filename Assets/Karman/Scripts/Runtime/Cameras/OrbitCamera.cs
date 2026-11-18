using System;
using Karman.Core;
using Karman.Flight;
using Karman.Rendering;
using Karman.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Karman.Cameras
{
    /// <summary>
    /// Flight camera that orbits a target (vessel or celestial body). The camera always sits at Unity's origin;
    /// it sets <see cref="RenderFrame.Origin"/> to its own double-precision position, so every object is rendered
    /// camera-relative. Zoom is logarithmic from metres to millions of kilometres.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(Camera))]
    public sealed class OrbitCamera : MonoBehaviour
    {
        public enum TargetKind { Vessel, Body }

        public TargetKind targetKind = TargetKind.Vessel;
        public string targetBodyId = SolarSystemData.Earth;
        public float yaw = 30, pitch = 12;
        public double distance = 22;
        public float orbitSensitivity = 0.25f;
        public float zoomStep = 1.18f;
        public float smoothing = 12f;

        public Camera Cam { get; private set; }
        double smoothDistance;
        float smoothYaw, smoothPitch;
        static readonly string[] cycleBodies = { SolarSystemData.Earth, SolarSystemData.Moon, SolarSystemData.Sun };
        int cycleIndex = -1;

        void Awake()
        {
            Cam = GetComponent<Camera>();
            smoothDistance = distance; smoothYaw = yaw; smoothPitch = pitch;
        }

        public void Focus(TargetKind kind, string bodyId = null, double? dist = null)
        {
            targetKind = kind;
            if (bodyId != null) targetBodyId = bodyId;
            if (dist.HasValue) { distance = dist.Value; smoothDistance = distance; }
            else distance = DefaultDistance();
        }

        /// <summary>Snap without smoothing (for scripted shots).</summary>
        public void Snap() { smoothDistance = distance; smoothYaw = yaw; smoothPitch = pitch; }

        double DefaultDistance()
        {
            var b = TargetBody();
            return targetKind == TargetKind.Vessel ? 22 : b.Radius * 3.2;
        }

        CelestialBody TargetBody() => SimulationController.Instance.System.Get(targetBodyId);

        double MinDistance()
        {
            if (targetKind == TargetKind.Vessel) return 6;
            return TargetBody().Radius * 1.05;
        }

        void Update()
        {
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (mouse != null && !Map.MapView.IsActive)
            {
                if (mouse.rightButton.isPressed || mouse.middleButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    yaw += d.x * orbitSensitivity;
                    pitch = Mathf.Clamp(pitch - d.y * orbitSensitivity, -89f, 89f);
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    distance *= Math.Pow(zoomStep, -Math.Sign(scroll) * Math.Min(3, Math.Abs(scroll) / 120f + 0.999));
            }
            if (kb != null && !Map.MapView.IsActive)
            {
                if (kb.vKey.wasPressedThisFrame)
                {
                    cycleIndex++;
                    if (cycleIndex >= cycleBodies.Length) { cycleIndex = -1; Focus(TargetKind.Vessel); }
                    else Focus(TargetKind.Body, cycleBodies[cycleIndex]);
                }
                if (kb.backspaceKey.wasPressedThisFrame) { cycleIndex = -1; Focus(TargetKind.Vessel); yaw = 30; pitch = 12; }
                if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed) distance /= Math.Pow(1.02, Time.unscaledDeltaTime * 60);
                if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed) distance *= Math.Pow(1.02, Time.unscaledDeltaTime * 60);
            }
            distance = Math.Max(MinDistance(), Math.Min(5e12, distance));
        }

        void LateUpdate()
        {
            var sim = SimulationController.Instance;
            if (sim == null) return;
            float k = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            smoothYaw = Mathf.LerpAngle(smoothYaw, yaw, k);
            smoothPitch = Mathf.Lerp(smoothPitch, pitch, k);
            smoothDistance = Math.Exp(Math.Log(smoothDistance) + (Math.Log(distance) - Math.Log(smoothDistance)) * k);

            Vector3d targetPos; Quaternion frame;
            VesselController vessel = sim.ActiveVessel;
            if (targetKind == TargetKind.Vessel && vessel != null && vessel.Trajectory != null)
            {
                targetPos = vessel.Trajectory.HeliocentricPosition;
                // Local-horizon frame: up = radial, forward = horizontal velocity. Keeps the planet "below".
                var s = vessel.Trajectory.RelativeState;
                Vector3 up = Coords.ToUnity(s.position.normalized);
                Vector3 fwd = Vector3.ProjectOnPlane(Coords.ToUnity(s.velocity), up);
                if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
                frame = Quaternion.LookRotation(fwd.normalized, up);
            }
            else
            {
                var b = TargetBody();
                targetPos = b.Position;
                // Celestial north up, yaw measured from the Sun direction (yaw 180 = fully lit, 90 = terminator).
                Vector3 toSun = Vector3.ProjectOnPlane(Coords.ToUnity((sim.System.Sun.Position - b.Position).normalized), Vector3.up);
                frame = toSun.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(toSun.normalized, Vector3.up) : Quaternion.identity;
            }

            Quaternion rot = frame * Quaternion.Euler(smoothPitch, smoothYaw, 0);
            Vector3d offset = Coords.ToSim(rot * Vector3.back) * smoothDistance;
            RenderFrame.Origin = targetPos + offset;
            transform.SetPositionAndRotation(Vector3.zero, rot);
            if (vessel != null) vessel.PlaceView();

            // Depth range: keep near plane proportional to the closest thing we care about.
            float near = (float)Math.Max(0.05, Math.Min(smoothDistance * 0.02, 1000));
            double closestSurface = double.MaxValue;
            foreach (var b in sim.System.Bodies)
                closestSurface = Math.Min(closestSurface, (b.Position - RenderFrame.Origin).magnitude - b.Radius);
            if (targetKind == TargetKind.Body) near = (float)Math.Max(near, Math.Min(closestSurface * 0.3, 1e5));
            Cam.nearClipPlane = near;
            Cam.farClipPlane = RenderFrame.FarClip;
        }
    }
}
