using System.Collections.Generic;
using System.Linq;
using Karman.Core;
using Karman.Core.Vehicles;
using Karman.Rendering;
using Karman.Simulation;
using Karman.Vehicles;
using UnityEngine;

namespace Karman.Flight
{
    /// <summary>
    /// Visual representation of the active vessel: part models in craft space (offset so the transform pivot is the
    /// centre of mass), an exhaust plume per lit engine that widens and dims as ambient pressure falls, and debris
    /// objects for jettisoned stages that keep falling under gravity.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class VesselView : MonoBehaviour
    {
        public PartModelCatalog catalog;
        public Material plumeMaterial;
        public Mesh plumeMesh;

        VesselController vessel;
        Transform model;
        readonly Dictionary<int, GameObject> parts = new Dictionary<int, GameObject>();
        readonly Dictionary<int, Renderer> plumes = new Dictionary<int, Renderer>();
        readonly List<Debris> debris = new List<Debris>();
        MaterialPropertyBlock block;
        static readonly int ThrottleId = Shader.PropertyToID("_Throttle");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        sealed class Debris { public Transform t; public SpacecraftTrajectory traj; public Quaternion rot; public Vector3 spin; public float age; }

        public void Build(VesselController v)
        {
            vessel = v;
            block = new MaterialPropertyBlock();
            if (model) Destroy(model.gameObject);
            model = new GameObject("Model").transform;
            model.SetParent(transform, false);
            foreach (var p in v.Vehicle.AttachedParts)
            {
                var d = v.Vehicle.Def(p);
                var go = catalog.Create(d, model);
                go.transform.localPosition = new Vector3(p.Position[0], p.Position[1], p.Position[2]);
                go.transform.localRotation = Rot(p);
                parts[p.Id] = go;
                if (d.Engine != null)
                {
                    var plume = new GameObject("Plume");
                    plume.transform.SetParent(go.transform, false);
                    // Nozzle exit at the bottom of the engine; pod thrusters fire from the adapter base.
                    plume.transform.localPosition = new Vector3(0, -d.Height / 2f, 0);
                    plume.AddComponent<MeshFilter>().sharedMesh = plumeMesh;
                    var r = plume.AddComponent<MeshRenderer>();
                    r.sharedMaterial = plumeMaterial;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.enabled = false;
                    plumes[p.Id] = r;
                }
            }
            UpdatePivot();
        }

        static Quaternion Rot(PartInstance p) => p.Rotation != null && p.Rotation.Length == 4 ? new Quaternion(p.Rotation[0], p.Rotation[1], p.Rotation[2], p.Rotation[3]) : Quaternion.identity;

        void UpdatePivot()
        {
            var com = vessel.Vehicle.CenterOfMass;
            model.localPosition = new Vector3(-(float)com[0], -(float)com[1], -(float)com[2]);
        }

        public void OnDetached(List<int> ids)
        {
            var sim = SimulationController.Instance;
            var tr = vessel.Trajectory;
            Vector3 up = transform.up;
            foreach (var id in ids)
            {
                if (!parts.TryGetValue(id, out var go)) continue;
                parts.Remove(id); plumes.Remove(id);
                // Detach into world space and give the debris its own trajectory with a small separation impulse.
                go.transform.SetParent(null, true);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) if (r.gameObject.name == "Plume") r.enabled = false;
                var offset = Coords.ToSim(go.transform.position - transform.position);
                var state = new StateVector(tr.RelativeState.position + offset, tr.RelativeState.velocity - Coords.ToSim(up) * 2.0 + Coords.ToSim(go.transform.position - transform.position).normalized * 0.8);
                debris.Add(new Debris
                {
                    t = go.transform, traj = new SpacecraftTrajectory(sim.System, tr.Reference, state, sim.Time),
                    rot = go.transform.rotation, spin = Random.insideUnitSphere * 6f,
                });
            }
            UpdatePivot();
        }

        void LateUpdate()
        {
            if (vessel == null || vessel.Trajectory == null) return;
            UpdatePivot();
            // Plumes: lit engines with propellant; shape follows ambient pressure (narrow at sea level, wide in vacuum).
            var burning = new HashSet<int>(vessel.CurrentThrust > 0 ? vessel.Vehicle.BurningEngines() : Enumerable.Empty<int>());
            double p = vessel.Air.Pressure / 101_325.0;
            float vac = Mathf.Clamp01(1f - (float)p);
            foreach (var kv in plumes)
            {
                bool on = burning.Contains(kv.Key);
                kv.Value.enabled = on;
                if (!on) continue;
                var part = vessel.Vehicle.Craft.Get(kv.Key);
                var d = vessel.Vehicle.Def(part);
                float throttle = d.Engine.Solid ? 1f : vessel.Throttle;
                float exit = Mathf.Max(0.5f, d.Engine.VacuumThrust > 2e6 ? d.Diameter * 0.32f : d.Diameter * 0.75f);
                float len = exit * (5f + 9f * vac) * (0.6f + 0.4f * throttle);
                float width = exit * (2.2f + 5f * vac);
                kv.Value.transform.localScale = new Vector3(width, len, width);
                block.SetFloat(ThrottleId, throttle);
                block.SetFloat(IntensityId, Mathf.Lerp(6f, 2.2f, vac));
                kv.Value.SetPropertyBlock(block);
            }

            // Debris: gravity-only propagation (no drag yet), removed on impact or after two minutes.
            var sim = SimulationController.Instance;
            for (int i = debris.Count - 1; i >= 0; i--)
            {
                var d = debris[i];
                d.age += Time.deltaTime;
                double dt = sim.Time - d.traj.Time;
                if (dt > 0) d.traj.Advance(dt, (r, v) => Vector3d.zero, 0.25);
                d.rot = Quaternion.Euler(d.spin * Time.deltaTime) * d.rot;
                d.t.SetPositionAndRotation(RenderFrame.Local(d.traj.HeliocentricPosition), d.rot);
                if (d.age > 120 || d.traj.Altitude < 0) { Destroy(d.t.gameObject); debris.RemoveAt(i); }
            }
        }
    }
}
