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
        public Material canopyMaterial;
        public Material interstageMaterial;
        public Material plasmaMaterial;
        Transform canopy, plasma;
        MaterialPropertyBlock plasmaBlock;

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
            Karman.Vehicles.InterstageFairings.Build(v.Vehicle.Craft, v.Lookup, parts, interstageMaterial);
            UpdatePivot();
            if (v.Vehicle.Parachute != null && canopyMaterial != null) BuildCanopy();
            if (plasmaMaterial != null)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(g.GetComponent<Collider>());
                g.name = "Entry Plasma"; g.GetComponent<Renderer>().sharedMaterial = plasmaMaterial;
                g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                g.transform.SetParent(transform, false);
                plasma = g.transform; plasma.gameObject.SetActive(false);
                plasmaBlock = new MaterialPropertyBlock();
            }
        }

        /// <summary>Ringsail-style canopy: shallow dome of 24 gores plus suspension lines, built procedurally.</summary>
        void BuildCanopy()
        {
            var go = new GameObject("Parachute");
            go.transform.SetParent(transform, false);
            const int gores = 24, rings = 8;
            var verts = new System.Collections.Generic.List<Vector3>(); var tris = new System.Collections.Generic.List<int>(); var cols = new System.Collections.Generic.List<Color>();
            for (int r = 0; r <= rings; r++)
            {
                float t = (float)r / rings;
                float ang = t * 70f * Mathf.Deg2Rad;
                for (int g = 0; g <= gores; g++)
                {
                    float a = g * Mathf.PI * 2 / gores;
                    float scallop = 1 - 0.06f * Mathf.Abs(Mathf.Sin(a * gores / 2)) * t;
                    verts.Add(new Vector3(Mathf.Sin(ang) * Mathf.Cos(a) * scallop, Mathf.Cos(ang), Mathf.Sin(ang) * Mathf.Sin(a) * scallop));
                    cols.Add(g % 2 == 0 ? new Color(0.92f, 0.36f, 0.18f) : new Color(0.95f, 0.95f, 0.93f));
                }
            }
            for (int r = 0; r < rings; r++)
            for (int g = 0; g < gores; g++)
            {
                int i = r * (gores + 1) + g, j = i + gores + 1;
                tris.AddRange(new[] { i, j, i + 1, i + 1, j, j + 1 });
            }
            int baseIdx = verts.Count;
            // Suspension lines: thin quads from the skirt to the confluence point below.
            for (int g = 0; g < gores; g += 2)
            {
                Vector3 top = verts[rings * (gores + 1) + g];
                Vector3 conf = new Vector3(0, -1.6f, 0);
                Vector3 side = Vector3.Cross(top - conf, Vector3.up).normalized * 0.004f;
                int k = verts.Count;
                verts.Add(top - side); verts.Add(top + side); verts.Add(conf - side); verts.Add(conf + side);
                for (int c = 0; c < 4; c++) cols.Add(new Color(0.8f, 0.8f, 0.8f));
                tris.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
            }
            var mesh = new Mesh { name = "Canopy" };
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = canopyMaterial;
            canopy = go.transform; canopy.gameObject.SetActive(false);
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
                float exit = d.Engine.NozzleExitDiameter > 0 ? d.Engine.NozzleExitDiameter : Mathf.Max(0.5f, d.Engine.VacuumThrust > 2e6 ? d.Diameter * 0.32f : d.Diameter * 0.75f);
                if (d.Engine.VacuumThrust > 5e6) exit = d.Diameter * 0.32f;
                float len = exit * (5f + 9f * vac) * (0.6f + 0.4f * throttle);
                float width = exit * (2.2f + 3f * vac);
                kv.Value.transform.localScale = new Vector3(width, len, width);
                block.SetFloat(ThrottleId, throttle);
                block.SetFloat(IntensityId, Mathf.Lerp(6f, 0.35f, vac * vac));   // vacuum plumes are faint (HDRP exposure brightens dark space)
                kv.Value.SetPropertyBlock(block);
            }

            // Parachute: trails upwind, opening with the reefed area fraction.
            var chute = vessel.Vehicle.Parachute;
            if (canopy != null)
            {
                bool open = vessel.Vehicle.ParachuteDeployed && chute != null;
                canopy.gameObject.SetActive(open);
                if (open)
                {
                    float frac = Mathf.Clamp01((float)(vessel.Vehicle.ParachuteOpenTime / chute.ParachuteOpeningTime));
                    float radius = Mathf.Sqrt((float)chute.ParachuteDragArea / 0.8f / Mathf.PI) * Mathf.Lerp(0.25f, 1f, frac);
                    Vector3 up = -Coords.ToUnity(vessel.Trajectory.SurfaceVelocity.normalized);
                    canopy.SetPositionAndRotation(transform.position + up * (vessel.Length * 0.5f + radius * 2.2f), Quaternion.FromToRotation(Vector3.up, up));
                    canopy.localScale = new Vector3(radius, radius * Mathf.Lerp(0.5f, 1f, frac), radius) / Mathf.Max(1e-3f, transform.lossyScale.x);
                }
            }
            // Entry plasma: glowing sheath ahead of the heat shield, intensity from the stagnation heat flux.
            if (plasma != null)
            {
                float q = (float)(vessel.HeatFlux / 1e6);
                bool on = q > 0.05f;
                plasma.gameObject.SetActive(on);
                if (on)
                {
                    Vector3 fwd = Coords.ToUnity(vessel.Trajectory.SurfaceVelocity.normalized);
                    float size = Mathf.Max(3f, vessel.Length * 0.9f);
                    plasma.SetPositionAndRotation(transform.position + fwd * size * 0.15f, Quaternion.FromToRotation(Vector3.up, fwd));
                    plasma.localScale = new Vector3(size, size * (1.4f + q * 0.6f), size);
                    plasmaBlock.SetFloat(ThrottleId, Mathf.Clamp01(q / 2f));
                    plasma.GetComponent<Renderer>().SetPropertyBlock(plasmaBlock);
                }
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
