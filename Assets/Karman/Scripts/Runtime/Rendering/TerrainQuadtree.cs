using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Karman.Core;
using Karman.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Karman.Rendering
{
    /// <summary>
    /// Cube-sphere quadtree terrain for a body with an <see cref="ITerrainHeight"/>. Six root patches subdivide by
    /// camera distance (split when closer than <see cref="splitFactor"/> × patch edge) down to metre-scale cells; patch
    /// meshes are generated on worker threads, carry skirts to hide LOD cracks, and are placed camera-relative in double
    /// precision every frame. The base sphere is hidden while the terrain is active.
    /// </summary>
    [DefaultExecutionOrder(101)]
    public sealed class TerrainQuadtree : MonoBehaviour
    {
        public CelestialBodyView bodyView;
        public Material material;
        public int resolution = 32;
        public int maxDepth = 16;
        public float splitFactor = 1.8f;
        public float activeRadii = 4f;
        public int maxConcurrentBuilds = 12;

        public int VisiblePatches { get; private set; }
        public int BuiltPatches { get; private set; }

        CelestialBody body;
        ITerrainHeight terrain;
        double radius;
        Node[] roots;
        readonly List<Node> building = new List<Node>();
        readonly Stack<Mesh> meshPool = new Stack<Mesh>();
        static readonly Vector3[] FaceNormals = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        Transform patchRoot;
        bool colourFormatChecked;

        sealed class Node
        {
            public int face, depth;
            public double u0, v0, size;        // face-space square in [-1,1]²
            public Vector3d center;            // Unity object-space metres (body-fixed, Y/Z swapped)
            public double edge, bound;
            public Node[] kids;
            public GameObject go;
            public Mesh mesh;
            public Task<MeshData> job;
            public bool Built => mesh != null;
            public int lastVisible;
        }

        /// <summary>
        /// Per-vertex data. HDRP's Lit vertex path carries only two floats per UV channel, so the two 3-vectors the
        /// surface shader needs are split: uv0 = direction.xy, uv1 = detail.xy, colour = (direction.z, detail.z, 0, 1).
        /// </summary>
        sealed class MeshData { public Vector3[] v; public Vector3[] n; public Vector2[] dirXY; public Vector2[] detailXY; public Color[] zz; public int[] tris; }

        /// <summary>Detail-texture coordinates repeat every <see cref="DetailPeriod"/> metres (a multiple of every tiling).</summary>
        const double DetailPeriod = 2048;

        void Start()
        {
            body = SimulationController.Instance.System.Get(bodyView.bodyId);
            splitFactor = Karman.Settings.QualitySettingsStore.TerrainSplitFactor;
            terrain = TerrainRegistry.Get(bodyView.bodyId);
            radius = body.Radius;
            patchRoot = new GameObject($"{body.Definition.DisplayName} Terrain").transform;
            roots = new Node[6];
            for (int f = 0; f < 6; f++) roots[f] = MakeNode(f, 0, -1, -1, 2);
        }

        Node MakeNode(int face, int depth, double u0, double v0, double size)
        {
            var n = new Node { face = face, depth = depth, u0 = u0, v0 = v0, size = size };
            Vector3d c = Direction(face, u0 + size / 2, v0 + size / 2);
            // The centre sits on the terrain, not on the reference sphere, so the LOD distance is measured to the real
            // surface. (Padding every patch by the body's maximum relief instead made every patch within several
            // kilometres of the camera split to the deepest level.)
            double h = terrain != null ? terrain.Height(new Vector3d(c.x, c.z, c.y)) : 0;
            n.center = c * (radius + h);
            Vector3d a = Direction(face, u0, v0) * radius, b = Direction(face, u0 + size, v0 + size) * radius;
            n.edge = (a - b).magnitude / Math.Sqrt(2);
            n.bound = n.edge * 0.5;
            return n;
        }

        static Vector3d Direction(int face, double u, double v)
        {
            Vector3 fn = FaceNormals[face];
            var axisA = new Vector3d(fn.y, fn.z, fn.x);
            var fnd = new Vector3d(fn.x, fn.y, fn.z);
            var axisB = new Vector3d(fnd.y * axisA.z - fnd.z * axisA.y, fnd.z * axisA.x - fnd.x * axisA.z, fnd.x * axisA.y - fnd.y * axisA.x);
            double tu = Math.Tan(u * Math.PI / 4), tv = Math.Tan(v * Math.PI / 4);
            return (fnd + axisA * tu + axisB * tv).normalized;
        }

        void LateUpdate()
        {
            if (body == null || terrain == null) return;
            Vector3d rel = RenderFrame.Origin - body.Position;
            bool active = rel.magnitude < radius * activeRadii && bodyView.RenderScale > 0.9999f;
            if (bodyView.surface) bodyView.surface.enabled = !active;
            if (!active) { SetAllHidden(); return; }

            // Camera in Unity object space of the body (body-fixed, Y/Z swapped).
            Vector3d camBF = body.Orientation.Transposed * rel;
            var cam = new Vector3d(camBF.x, camBF.z, camBF.y);
            int frame = Time.frameCount;
            VisiblePatches = 0;
            foreach (var r in roots) Visit(r, cam, frame);
            PollBuilds();

            // Place visible patches camera-relative in double precision.
            Quaternion rot = Coords.ToUnity(body.Orientation);
            foreach (var r in roots) Place(r, frame, rot);
            var sun = SimulationController.Instance.System.Sun;
            material.SetVector("_SunDir", Coords.ToUnity((sun.Position - body.Position).normalized));
            var earth = SimulationController.Instance.System.Get(SolarSystemData.Earth);
            if (earth != null)
            {
                Vector3d toEarth = (earth.Position - body.Position).normalized;
                material.SetVector("_EarthDir", Coords.ToUnity(toEarth));
                // Earthshine scales with the sunlit fraction of Earth seen from the Moon.
                double phase = 0.5 * (1 + Vector3d.Dot(-toEarth, (sun.Position - earth.Position).normalized));
                material.SetFloat("_EarthShine", (float)(0.012 + 0.04 * phase));
            }
        }

        void Visit(Node n, Vector3d cam, int frame)
        {
            double dist = Math.Max(0, (cam - n.center).magnitude - n.bound);
            // Horizon culling: skip patches entirely behind the limb.
            double camR = cam.magnitude;
            if (n.depth > 1)
            {
                double cosHorizon = radius / Math.Max(camR, radius + 1);
                double cosPatch = Vector3d.Dot(cam / camR, n.center / n.center.magnitude);
                double patchAngle = n.edge / radius;
                if (cosPatch < Math.Cos(Math.Acos(cosHorizon) + patchAngle * 1.2 + 0.02)) { Collapse(n); return; }
            }
            bool split = n.depth < maxDepth && dist < n.edge * splitFactor;
            if (split)
            {
                if (n.kids == null)
                {
                    double h = n.size / 2;
                    n.kids = new[] { MakeNode(n.face, n.depth + 1, n.u0, n.v0, h), MakeNode(n.face, n.depth + 1, n.u0 + h, n.v0, h),
                                     MakeNode(n.face, n.depth + 1, n.u0, n.v0 + h, h), MakeNode(n.face, n.depth + 1, n.u0 + h, n.v0 + h, h) };
                }
                bool allBuilt = true;
                foreach (var k in n.kids) { if (!k.Built) { allBuilt = false; RequestBuild(k); } }
                if (allBuilt) { foreach (var k in n.kids) Visit(k, cam, frame); return; }
            }
            else Collapse(n);
            if (!n.Built) { RequestBuild(n); return; }
            n.lastVisible = frame;
            VisiblePatches++;
        }

        void Collapse(Node n)
        {
            if (n.kids == null) return;
            foreach (var k in n.kids) { Collapse(k); Release(k); }
            n.kids = null;
        }

        void Release(Node n)
        {
            if (n.go) Destroy(n.go);
            if (n.mesh) { n.mesh.Clear(); meshPool.Push(n.mesh); n.mesh = null; BuiltPatches--; }
            n.job = null;
        }

        void RequestBuild(Node n)
        {
            if (n.job != null || n.Built || building.Count >= maxConcurrentBuilds) return;
            int res = resolution; double R = radius; var t = terrain;
            n.job = Task.Run(() => Build(n, res, R, t));
            building.Add(n);
        }

        void PollBuilds()
        {
            for (int i = building.Count - 1; i >= 0; i--)
            {
                var n = building[i];
                if (n.job == null) { building.RemoveAt(i); continue; }
                if (!n.job.IsCompleted) continue;
                building.RemoveAt(i);
                if (n.job.IsFaulted) { Debug.LogException(n.job.Exception); n.job = null; continue; }
                var d = n.job.Result; n.job = null;
                var mesh = meshPool.Count > 0 ? meshPool.Pop() : new Mesh { indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(d.v); mesh.SetNormals(d.n); mesh.SetUVs(0, d.dirXY); mesh.SetUVs(1, d.detailXY); mesh.SetColors(d.zz); mesh.SetTriangles(d.tris, 0);
                mesh.RecalculateBounds();
                if (!colourFormatChecked)
                {
                    // The colour channel carries signed, unbounded floats; an 8-bit normalised format would destroy them.
                    colourFormatChecked = true;
                    var fmt = mesh.GetVertexAttributeFormat(VertexAttribute.Color);
                    if (fmt != VertexAttributeFormat.Float32) Debug.LogError($"[TerrainQuadtree] vertex colour stored as {fmt}, expected Float32: terrain colour and detail will be wrong");
                }
                n.mesh = mesh; BuiltPatches++;
                n.go = new GameObject($"Patch f{n.face} d{n.depth}");
                n.go.transform.SetParent(patchRoot, false);
                n.go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = n.go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true;
                n.go.SetActive(false);
            }
        }

        void Place(Node n, int frame, Quaternion rot)
        {
            if (n.kids != null && n.lastVisible != frame) { foreach (var k in n.kids) Place(k, frame, rot); }
            if (n.go == null) return;
            bool vis = n.lastVisible == frame;
            if (n.go.activeSelf != vis) n.go.SetActive(vis);
            if (!vis) return;
            var c = n.center; // Unity object space → sim body-fixed → inertial → camera-relative
            Vector3d simBF = new Vector3d(c.x, c.z, c.y);
            n.go.transform.SetPositionAndRotation(RenderFrame.Local(body.Position + body.Orientation * simBF), rot);
        }

        void SetAllHidden() { if (roots != null) foreach (var r in roots) Hide(r); }
        void Hide(Node n) { if (n.go && n.go.activeSelf) n.go.SetActive(false); if (n.kids != null) foreach (var k in n.kids) Hide(k); }

        // ---------------------------------------------------------------- mesh generation (worker thread)

        static MeshData Build(Node n, int N, double R, ITerrainHeight t)
        {
            int G = N + 3; // one-sample border for normals
            var pos = new Vector3d[G * G];
            double step = n.size / N;
            for (int j = 0; j < G; j++)
            for (int i = 0; i < G; i++)
            {
                Vector3d d = Direction(n.face, n.u0 + (i - 1) * step, n.v0 + (j - 1) * step);
                double h = t.Height(new Vector3d(d.x, d.z, d.y)); // sim body-fixed = (x, z, y)
                pos[j * G + i] = d * (R + h);
            }
            int vCount = (N + 1) * (N + 1) + 4 * (N + 1);
            var v = new Vector3[vCount]; var nm = new Vector3[vCount];
            var dirXY = new Vector2[vCount]; var detXY = new Vector2[vCount]; var zz = new Color[vCount];
            // Detail coordinates are measured from the patch centre snapped to the detail period: continuous across the
            // patch (no wrap seam inside a triangle), small enough for float precision where metre-scale detail is
            // visible, and in phase between neighbouring patches because every tiling divides the period.
            var detailOrigin = new Vector3d(Math.Floor(n.center.x / DetailPeriod) * DetailPeriod,
                                            Math.Floor(n.center.y / DetailPeriod) * DetailPeriod,
                                            Math.Floor(n.center.z / DetailPeriod) * DetailPeriod);
            int idx = 0;
            for (int j = 0; j <= N; j++)
            for (int i = 0; i <= N; i++)
            {
                Vector3d p = pos[(j + 1) * G + (i + 1)];
                Vector3d dx = pos[(j + 1) * G + (i + 2)] - pos[(j + 1) * G + i];
                Vector3d dy = pos[(j + 2) * G + (i + 1)] - pos[j * G + (i + 1)];
                Vector3d normal = Vector3d.Cross(dy, dx).normalized;
                if (Vector3d.Dot(normal, p) < 0) normal = -normal;
                Vector3d dn = p.normalized, dp = p - detailOrigin;
                v[idx] = ToF(p - n.center); nm[idx] = ToF(normal);
                dirXY[idx] = new Vector2((float)dn.x, (float)dn.y); detXY[idx] = new Vector2((float)dp.x, (float)dp.y);
                zz[idx] = new Color((float)dn.z, (float)dp.z, 0, 1);
                idx++;
            }
            var tris = new List<int>(N * N * 6 + 4 * N * 6);
            int W = N + 1;
            for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                int a = j * W + i;
                tris.Add(a); tris.Add(a + W + 1); tris.Add(a + W);
                tris.Add(a); tris.Add(a + 1); tris.Add(a + W + 1);
            }
            // Skirts: duplicate each edge ring, pushed toward the centre by a fraction of the patch edge.
            double drop = n.edge * 0.03 + 5;
            int[][] edges =
            {
                Ring(i => i, N), Ring(i => N * W + (N - i), N), Ring(i => (N - i) * W, N), Ring(i => i * W + N, N),
            };
            foreach (var ring in edges)
            {
                int start = idx;
                foreach (int k in ring)
                {
                    Vector3d p = new Vector3d(v[k].x, v[k].y, v[k].z) + n.center;
                    Vector3d q = p - p.normalized * drop;
                    v[idx] = ToF(q - n.center); nm[idx] = nm[k]; dirXY[idx] = dirXY[k]; detXY[idx] = detXY[k]; zz[idx] = zz[k]; idx++;
                }
                for (int s = 0; s < ring.Length - 1; s++)
                {
                    int a = ring[s], b = ring[s + 1], c = start + s, d = start + s + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }
            return new MeshData { v = v, n = nm, dirXY = dirXY, detailXY = detXY, zz = zz, tris = tris.ToArray() };
        }

        static int[] Ring(Func<int, int> f, int N) { var r = new int[N + 1]; for (int i = 0; i <= N; i++) r[i] = f(i); return r; }

        static Vector3 ToF(Vector3d d) => new Vector3((float)d.x, (float)d.y, (float)d.z);
    }
}
