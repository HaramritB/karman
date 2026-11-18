using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Karman.Rendering
{
    /// <summary>Generates unit cube-spheres (six warped grids). Equal-angle warping keeps cells near-uniform,
    /// avoiding the pole pinching of UV spheres. This is also the topology the quadtree terrain will subdivide.</summary>
    public static class SphereMeshFactory
    {
        static readonly Dictionary<int, Mesh> cache = new Dictionary<int, Mesh>();

        public static Mesh CubeSphere(int resolution)
        {
            if (cache.TryGetValue(resolution, out var cached) && cached != null) return cached;
            int n = resolution + 1;
            var verts = new List<Vector3>(6 * n * n);
            var normals = new List<Vector3>(6 * n * n);
            var tris = new List<int>(6 * resolution * resolution * 6);
            Vector3[] faceNormals = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            foreach (var fn in faceNormals)
            {
                Vector3 axisA = new Vector3(fn.y, fn.z, fn.x);
                Vector3 axisB = Vector3.Cross(fn, axisA);
                int start = verts.Count;
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = Mathf.Tan(((float)x / resolution - 0.5f) * Mathf.PI / 2f);
                    float v = Mathf.Tan(((float)y / resolution - 0.5f) * Mathf.PI / 2f);
                    Vector3 p = (fn + axisA * u + axisB * v).normalized;
                    verts.Add(p); normals.Add(p);
                }
                for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    int i = start + y * n + x;
                    tris.Add(i); tris.Add(i + n + 1); tris.Add(i + n);
                    tris.Add(i); tris.Add(i + 1); tris.Add(i + n + 1);
                }
            }
            var mesh = new Mesh { name = $"CubeSphere{resolution}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);
            cache[resolution] = mesh;
            return mesh;
        }

        /// <summary>Flat annulus for planetary rings; uv.x = 0 at inner radius, 1 at outer.</summary>
        public static Mesh Ring(float inner, float outer, int segments = 256)
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts.Add(d * inner); uvs.Add(new Vector2(0, (float)i / segments));
                verts.Add(d * outer); uvs.Add(new Vector2(1, (float)i / segments));
                if (i < segments)
                {
                    int b = i * 2;
                    tris.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
                }
            }
            var mesh = new Mesh { name = "Ring" };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * outer * 2.2f);
            return mesh;
        }
    }
}
