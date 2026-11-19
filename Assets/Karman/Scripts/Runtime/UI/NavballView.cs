using Karman.Core;
using Karman.Flight;
using Karman.Rendering;
using UnityEngine;
using UnityEngine.UIElements;

namespace Karman.UI
{
    /// <summary>
    /// Attitude indicator: a procedural horizon-frame ball rendered by a private camera into a texture, rotated so
    /// the vessel's nose is at the centre, with prograde/retrograde/normal/radial markers projected on top.
    /// </summary>
    public sealed class NavballView : MonoBehaviour
    {
        public const int Layer = 30;
        public Material ballMaterial;
        public VesselController vessel;

        Transform ball; Camera cam; RenderTexture rt;
        VisualElement root, disc;
        readonly (string label, Color color)[] markerStyles =
        {
            ("PRO", new Color(0.95f, 0.85f, 0.2f)), ("RET", new Color(0.95f, 0.6f, 0.2f)),
            ("NRM", new Color(0.8f, 0.4f, 1f)), ("ANT", new Color(0.8f, 0.4f, 1f)),
            ("RAD", new Color(0.3f, 0.9f, 1f)), ("RIN", new Color(0.3f, 0.9f, 1f)),
            ("MNV", new Color(0.35f, 0.6f, 1f)),
        };
        Label[] markers;
        Label heading;
        static readonly Quaternion C = Quaternion.LookRotation(Vector3.down, Vector3.back); // vessel local → navball camera (nose to centre, local −Z up)

        public VisualElement Build()
        {
            rt = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "Navball" };
            var go = new GameObject("Navball Ball") { layer = Layer };
            go.transform.position = new Vector3(0, -50_000, 0);
            go.AddComponent<MeshFilter>().sharedMesh = SphereMeshFactory.CubeSphere(32);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = ballMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ball = go.transform;
            var cgo = new GameObject("Navball Camera");
            cam = cgo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 1.0f; cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.targetTexture = rt; cam.nearClipPlane = 0.1f; cam.farClipPlane = 10;
            cgo.transform.SetPositionAndRotation(ball.position + Vector3.back * 3, Quaternion.identity);
            foreach (var c in Camera.allCameras) if (c != cam) c.cullingMask &= ~(1 << Layer);

            root = new VisualElement();
            root.style.width = 230; root.style.height = 230;
            disc = new VisualElement();
            disc.style.width = 230; disc.style.height = 230;
            disc.style.backgroundImage = Background.FromRenderTexture(rt);
            root.Add(disc);
            markers = new Label[markerStyles.Length];
            for (int i = 0; i < markers.Length; i++)
            {
                var l = new Label(markerStyles[i].label);
                l.style.position = Position.Absolute; l.style.color = markerStyles[i].color; l.style.fontSize = 10;
                l.style.unityFontStyleAndWeight = FontStyle.Bold;
                l.style.borderTopWidth = l.style.borderBottomWidth = l.style.borderLeftWidth = l.style.borderRightWidth = 2;
                l.style.borderTopColor = l.style.borderBottomColor = l.style.borderLeftColor = l.style.borderRightColor = markerStyles[i].color;
                l.style.borderTopLeftRadius = l.style.borderTopRightRadius = l.style.borderBottomLeftRadius = l.style.borderBottomRightRadius = 9;
                l.style.paddingLeft = 3; l.style.paddingRight = 3;
                l.style.backgroundColor = new Color(0, 0, 0, 0.35f);
                markers[i] = l; root.Add(l);
            }
            var cross = new Label("—  ✕  —"); cross.style.position = Position.Absolute; cross.style.left = 0; cross.style.right = 0; cross.style.top = 104;
            cross.style.unityTextAlign = TextAnchor.MiddleCenter; cross.style.color = new Color(1, 0.6f, 0.1f); cross.style.fontSize = 14; cross.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(cross);
            heading = new Label(); heading.style.position = Position.Absolute; heading.style.top = -20; heading.style.left = 0; heading.style.right = 0;
            heading.style.unityTextAlign = TextAnchor.MiddleCenter; heading.style.color = HudStyle.Text; heading.style.fontSize = 13;
            root.Add(heading);
            return root;
        }

        void LateUpdate()
        {
            if (vessel == null || vessel.Trajectory == null || ball == null) return;
            root.style.display = Karman.Map.MapView.IsActive ? DisplayStyle.None : DisplayStyle.Flex;
            vessel.LocalFrame(out Vector3 up, out Vector3 east, out Vector3 north);
            Quaternion H = Quaternion.LookRotation(north, up);         // ball frame → world
            Quaternion Ainv = Quaternion.Inverse(vessel.Attitude);
            ball.rotation = C * Ainv * H;

            var s = vessel.Trajectory.RelativeState;
            Vector3 v = vessel.Trajectory.Altitude < 36_000 ? Coords.ToUnity(vessel.Trajectory.SurfaceVelocity.normalized) : Coords.ToUnity(s.velocity.normalized);
            Vector3 n = Coords.ToUnity(Vector3d.Cross(s.position, s.velocity).normalized);
            Vector3 rad = Vector3.Cross(n, v).normalized;
            var burn = Karman.Flight.ManeuverPlanner.Instance?.BurnDirection;
            Vector3[] dirs = { v, -v, n, -n, rad, -rad, burn ?? Vector3.zero };
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector3 p = C * (Ainv * dirs[i]);   // navball camera space; visible if facing the camera (z < 0)
                bool vis = p.z < 0.05f && vessel.Trajectory.OrbitalSpeed > 1 && dirs[i].sqrMagnitude > 0.5f;
                markers[i].style.display = vis ? DisplayStyle.Flex : DisplayStyle.None;
                if (vis) { markers[i].style.left = 115 + p.x * 112 - 14; markers[i].style.top = 115 - p.y * 112 - 9; }
            }
            Vector3 nose = vessel.Attitude * Vector3.up;
            Vector3 flat = Vector3.ProjectOnPlane(nose, up);
            float hdg = flat.sqrMagnitude > 1e-6f ? Mathf.Atan2(Vector3.Dot(flat, east), Vector3.Dot(flat, north)) * Mathf.Rad2Deg : 0;
            if (hdg < 0) hdg += 360;
            float pitch = 90 - Vector3.Angle(nose, up);
            heading.text = $"HDG {hdg:000}°   PITCH {pitch:+0;-0}°";
        }

        void OnDestroy() { if (rt) rt.Release(); }
    }
}
