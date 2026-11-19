using System;
using System.Collections.Generic;
using Karman.Core.Vehicles;
using UnityEngine;

namespace Karman.Vehicles
{
    /// <summary>
    /// Cosmetic interstage shells: wherever an engine has a part stacked below it (e.g. a vacuum engine above a
    /// separator, or a service engine above the lander), its bell is enclosed by a cylindrical interstage that spans
    /// the engine's height. The shell belongs to the lower part, so it falls away at separation and reveals the engine.
    /// </summary>
    public static class InterstageFairings
    {
        static Mesh shell;

        public static void Build(Craft craft, Func<string, PartDefinition> lookup, IDictionary<int, GameObject> visuals, Material material)
        {
            if (material == null) return;
            foreach (var engine in craft.Parts)
            {
                var ed = lookup(engine.PartId);
                if (ed?.Engine == null || ed.Node("bottom") == null) continue;
                PartInstance below = null;
                foreach (var c in craft.Children(engine.Id)) if (c.ParentNode == "bottom") below = c;
                if (below == null || !visuals.TryGetValue(below.Id, out var belowGo) || belowGo == null) continue;
                var above = craft.Get(engine.ParentId);
                float dAbove = above != null ? lookup(above.PartId).Diameter : ed.Diameter;
                float dBelow = lookup(below.PartId).Diameter;
                float radius = Mathf.Max(dAbove, dBelow, 1f) / 2f + 0.01f;
                float top = engine.Position[1] + ed.Height / 2f, bottom = engine.Position[1] - ed.Height / 2f;
                var go = new GameObject("Interstage");
                go.transform.SetParent(belowGo.transform.parent, false);
                go.transform.localPosition = new Vector3(engine.Position[0], (top + bottom) / 2f, engine.Position[2]);
                go.transform.localScale = new Vector3(radius, (top - bottom) / 2f, radius);
                go.AddComponent<MeshFilter>().sharedMesh = shell ??= ShellMesh();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                go.transform.SetParent(belowGo.transform, true);
            }
        }

        /// <summary>Open unit cylinder (radius 1, y ∈ [−1, 1]) with smooth normals, inner faces included.</summary>
        static Mesh ShellMesh()
        {
            const int seg = 96;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int side = 0; side < 2; side++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2 / seg;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    for (int k = 0; k < 2; k++) { v.Add(d + Vector3.up * (k == 0 ? -1 : 1)); n.Add(side == 0 ? d : -d); uv.Add(new Vector2(i / (float)seg, k)); }
                }
            int stride = (seg + 1) * 2;
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2;
                t.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 });                   // outside
                int b = stride + i * 2;
                t.AddRange(new[] { b, b + 3, b + 1, b, b + 2, b + 3 });                   // inside
            }
            var m = new Mesh { name = "InterstageShell" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
