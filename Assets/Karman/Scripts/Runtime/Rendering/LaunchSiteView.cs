using System;
using System.Collections.Generic;
using Karman.Core;
using Karman.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Karman.Rendering
{
    /// <summary>
    /// Places the launch complex on the rotating Earth (local frame: +Y up, +Z north, +X east at the pad) and builds a
    /// curved high-resolution ground patch around it. The patch samples the same global imagery as the planet and adds
    /// procedural detail, so it blends into the globe at its edge. The Cape is flat (pad ~15 m), so no DEM is needed here.
    /// </summary>
    [DefaultExecutionOrder(105)]
    public sealed class LaunchSiteView : MonoBehaviour
    {
        public double latitude = 28.60822, longitude = -80.60411;
        public float patchRadius = 45_000f;
        public int rings = 96, segments = 192;
        public MeshRenderer ground;
        public bool hideWhenFar = true;

        CelestialBody earth;
        Vector3d bodyFixed;
        Matrix3d enuToBody;

        void Start()
        {
            earth = SimulationController.Instance.System.Get(SolarSystemData.Earth);
            double lat = latitude * Constants.Deg2Rad, lon = longitude * Constants.Deg2Rad;
            Vector3d up = new Vector3d(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
            Vector3d east = new Vector3d(-Math.Sin(lon), Math.Cos(lon), 0);
            Vector3d north = Vector3d.Cross(up, east);
            bodyFixed = up * earth.Radius;
            // Columns: local X (east), local Y (up), local Z (north) expressed in body-fixed sim coordinates.
            enuToBody = new Matrix3d { m00 = east.x, m10 = east.y, m20 = east.z, m01 = up.x, m11 = up.y, m21 = up.z, m02 = north.x, m12 = north.y, m22 = north.z };
            if (ground) ground.GetComponent<MeshFilter>().sharedMesh = BuildPatch(lat, lon, up, east, north);
        }

        Mesh BuildPatch(double lat0, double lon0, Vector3d up, Vector3d east, Vector3d north)
        {
            double R = earth.Radius;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var uv2 = new List<Vector2>(); var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                // Quadratic ring spacing: dense near the pad, coarse at the rim.
                double f = (double)i / rings;
                double arc = patchRadius * f * f;
                double ang = arc / R;
                for (int j = 0; j <= segments; j++)
                {
                    double az = 2 * Math.PI * j / segments;
                    Vector3d dir = up * Math.Cos(ang) + (east * Math.Sin(az) + north * Math.Cos(az)) * Math.Sin(ang);
                    Vector3d p = dir * R - up * R;              // relative to the pad's ground point, body-fixed
                    // Local ENU coordinates (Unity: x east, y up, z north).
                    var local = new Vector3((float)Vector3d.Dot(p, east), (float)Vector3d.Dot(p, up), (float)Vector3d.Dot(p, north));
                    verts.Add(local);
                    normals.Add(new Vector3((float)Vector3d.Dot(dir, east), (float)Vector3d.Dot(dir, up), (float)Vector3d.Dot(dir, north)));
                    double lat = Math.Asin(dir.z), lon = Math.Atan2(dir.y, dir.x);
                    uvs.Add(new Vector2((float)(lon / (2 * Math.PI) + 0.5), (float)(lat / Math.PI + 0.5)));
                    uv2.Add(new Vector2((float)f, 0));
                }
            }
            int n = segments + 1;
            for (int i = 0; i < rings; i++)
            for (int j = 0; j < segments; j++)
            {
                int a = i * n + j, b = a + n;
                tris.AddRange(new[] { a, a + 1, b + 1, a, b + 1, b });
            }
            var mesh = new Mesh { name = "LaunchSitePatch", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetUVs(1, uv2); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        void LateUpdate()
        {
            if (earth == null) return;
            Vector3d helio = earth.Position + earth.Orientation * bodyFixed;
            Vector3d rel = helio - RenderFrame.Origin;
            bool near = rel.magnitude < 2.5e6;
            if (hideWhenFar) foreach (Transform c in transform) c.gameObject.SetActive(near);
            if (!near) return;
            // The detailed patch and the globe are lit differently; above a few km the globe alone avoids a visible rim.
            if (ground) ground.enabled = (RenderFrame.Origin - earth.Position).magnitude - earth.Radius < 4000;
            transform.position = Coords.ToUnity(rel);
            Matrix3d m = earth.Orientation * enuToBody;
            // Local axes: X east, Y up, Z north (already Unity-style), so map columns directly.
            transform.rotation = Quaternion.LookRotation(Coords.ToUnity(m.Column(2)), Coords.ToUnity(m.Column(1)));
            // Pad materials project their textures in this frame so they stay put under the floating origin.
            Shader.SetGlobalMatrix("_KarmanSiteW2L", transform.worldToLocalMatrix);
            var sun = SimulationController.Instance.System.Sun;
            if (ground) ground.sharedMaterial.SetVector("_SunDir", Coords.ToUnity((sun.Position - helio).normalized));
        }
    }
}
