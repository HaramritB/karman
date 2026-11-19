using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Karman.EditorTools
{
    /// <summary>
    /// Small flat-shaded mesh builder for generated scenery. Faces are oriented automatically relative to a reference
    /// point (normals point away from it, or towards it for hollow shapes like a trench), so call order doesn't matter.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        readonly List<int> t = new List<int>();
        readonly Vector3 reference; readonly bool towards;

        public MeshBuilder(Vector3 reference, bool towards = false) { this.reference = reference; this.towards = towards; }

        public void Tri(Vector3 a, Vector3 b, Vector3 c) => Poly(a, b, c);
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => Poly(a, b, c, d);

        void Poly(params Vector3[] p)
        {
            Vector3 normal = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized;
            Vector3 centre = Vector3.zero; foreach (var q in p) centre += q; centre /= p.Length;
            float side = Vector3.Dot(normal, centre - reference);
            if (towards ? side > 0 : side < 0) { System.Array.Reverse(p); normal = -normal; }
            int b = v.Count;
            foreach (var q in p) { v.Add(q); n.Add(normal); }
            for (int i = 1; i < p.Length - 1; i++) { t.Add(b); t.Add(b + i); t.Add(b + i + 1); }
        }

        public static GameObject Save(MeshBuilder mb, string name, Material mat, Transform parent)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(mb.v); mesh.SetNormals(mb.n); mesh.SetTriangles(mb.t, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            const string dir = "Assets/Karman/Meshes/Generated";
            Directory.CreateDirectory(dir);
            string path = $"{dir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); mesh = existing; EditorUtility.SetDirty(existing); }
            else AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }
}
