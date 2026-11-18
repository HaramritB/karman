using System;
using System.Collections.Generic;
using Karman.Core;
using Karman.Rendering;
using Karman.Simulation;
using Karman.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Karman.Map
{
    /// <summary>
    /// Interactive orbital map. Renders on its own camera/layer at a zoom-dependent scale (metres per unit), so
    /// one view spans a 300 km orbit to the whole solar system. Orbits are drawn from the same Kepler/ephemeris
    /// models the simulation uses: planets from their mean elements, the Moon and the vessel as osculating conics.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class MapView : MonoBehaviour
    {
        public const int Layer = 31;
        public static bool IsActive { get; private set; }

        public Camera mapCamera;
        public Camera flightCamera;
        public UIDocument labelsDocument;
        public Material lineMaterial;
        public Material planetMaterialTemplate;
        public Material sunMaterial;
        public Material earthMaterial;
        public Material moonMaterial;
        public Texture[] bodyTextures;   // indexed parallel to bodyTextureIds
        public string[] bodyTextureIds;

        float yaw = 20, pitch = 55;
        double distance = 1.5e8;
        double smoothDistance = 1.5e8;
        int focusIndex;
        readonly List<string> focusOrder = new List<string>();

        sealed class BodyMarker
        {
            public CelestialBody body; public Transform t; public Renderer r; public LineRenderer orbit;
            public Label label; public Vector3d[] orbitPoints; public MaterialPropertyBlock block;
        }
        readonly List<BodyMarker> markers = new List<BodyMarker>();
        Transform vesselMarker; LineRenderer vesselOrbit; Label vesselLabel, apLabel, peLabel, infoLabel;
        Vector3d[] vesselOrbitPoints = Array.Empty<Vector3d>();
        Vector3d apPos, pePos; bool hasAp;
        float nextOrbitRefresh;
        static readonly Quaternion EclipticFrame = Quaternion.AngleAxis(-(float)(Constants.ObliquityJ2000 * Constants.Rad2Deg), Vector3.right);

        void Start()
        {
            var sys = SimulationController.Instance.System;
            focusOrder.Add("vessel");
            focusOrder.Add(SolarSystemData.Earth);
            focusOrder.Add(SolarSystemData.Moon);
            focusOrder.Add(SolarSystemData.Sun);
            foreach (var b in sys.Bodies) if (!focusOrder.Contains(b.Id)) focusOrder.Add(b.Id);

            var root = labelsDocument.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            var sphere = SphereMeshFactory.CubeSphere(24);
            foreach (var b in sys.Bodies)
            {
                var go = new GameObject("Map " + b.Definition.DisplayName) { layer = Layer };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = sphere;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaterialFor(b);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var m = new BodyMarker { body = b, t = go.transform, r = mr, block = new MaterialPropertyBlock() };
                if (b.Parent != null)
                {
                    var c = b.Definition.DisplayColor;
                    m.orbit = MakeLine("Orbit " + b.Definition.DisplayName, new Color(c[0], c[1], c[2], 0.55f));
                }
                m.label = MakeLabel(root, b.Definition.DisplayName, HudStyle.Text);
                string id = b.Id;
                m.label.RegisterCallback<ClickEvent>(_ => FocusOn(id));
                markers.Add(m);
            }
            var vgo = new GameObject("Map Vessel") { layer = Layer };
            vgo.transform.SetParent(transform, false);
            vgo.AddComponent<MeshFilter>().sharedMesh = sphere;
            var vmr = vgo.AddComponent<MeshRenderer>();
            vmr.sharedMaterial = new Material(lineMaterial) { color = HudStyle.Accent };
            vesselMarker = vgo.transform;
            vesselOrbit = MakeLine("Vessel Orbit", HudStyle.Accent);
            vesselLabel = MakeLabel(root, "Gemini", HudStyle.Accent);
            vesselLabel.RegisterCallback<ClickEvent>(_ => FocusOn("vessel"));
            apLabel = MakeLabel(root, "Ap", HudStyle.Warn); peLabel = MakeLabel(root, "Pe", HudStyle.Warn);
            apLabel.pickingMode = PickingMode.Ignore; peLabel.pickingMode = PickingMode.Ignore;

            var info = HudStyle.PanelBox(300);
            info.style.position = Position.Absolute; info.style.right = 18; info.style.top = 70;
            info.Add(HudStyle.Title("MAP  ·  TAB focus  ·  M close"));
            infoLabel = new Label(); infoLabel.style.color = HudStyle.Text; infoLabel.style.fontSize = 13; infoLabel.style.whiteSpace = WhiteSpace.Normal;
            info.Add(infoLabel);
            info.pickingMode = PickingMode.Ignore;
            root.Add(info);

            mapCamera.cullingMask = 1 << Layer;
            flightCamera.cullingMask &= ~(1 << Layer);
            SetActive(false);
            focusIndex = 1;
        }

        Material MaterialFor(CelestialBody b)
        {
            if (b.Id == SolarSystemData.Sun) return sunMaterial;
            if (b.Id == SolarSystemData.Earth && earthMaterial) return earthMaterial;
            if (b.Id == SolarSystemData.Moon && moonMaterial) return moonMaterial;
            var m = new Material(planetMaterialTemplate);
            int i = Array.IndexOf(bodyTextureIds, b.Id);
            if (i >= 0) m.SetTexture("_MainTex", bodyTextures[i]);
            return m;
        }

        LineRenderer MakeLine(string name, Color c)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = lineMaterial;
            lr.useWorldSpace = true;
            lr.startColor = lr.endColor = c;
            lr.numCapVertices = 0;
            lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        static Label MakeLabel(VisualElement root, string text, Color color)
        {
            var l = new Label(text);
            l.style.position = Position.Absolute;
            l.style.color = color;
            l.style.fontSize = 12;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.paddingLeft = 6; l.style.paddingRight = 6; l.style.paddingTop = 1; l.style.paddingBottom = 1;
            l.style.backgroundColor = new Color(0, 0, 0, 0.35f);
            l.style.borderTopLeftRadius = 4; l.style.borderTopRightRadius = 4; l.style.borderBottomLeftRadius = 4; l.style.borderBottomRightRadius = 4;
            root.Add(l);
            return l;
        }

        public void SetActive(bool on)
        {
            IsActive = on;
            mapCamera.enabled = on;
            flightCamera.enabled = !on;
            labelsDocument.rootVisualElement.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (Transform c in transform) c.gameObject.SetActive(on);
            if (on) RefreshOrbits();
        }

        public void FocusOn(string id)
        {
            focusIndex = Mathf.Max(0, focusOrder.IndexOf(id));
            distance = DefaultDistance(id);
        }

        /// <summary>Scripted view (automated capture).</summary>
        public void SetView(string id, double dist, float yawDeg, float pitchDeg)
        {
            FocusOn(id); distance = dist; smoothDistance = dist; yaw = yawDeg; pitch = pitchDeg;
        }

        double DefaultDistance(string id)
        {
            var sys = SimulationController.Instance.System;
            if (id == "vessel") return 4.0e7;
            if (id == SolarSystemData.Sun) return 1.2e13;
            var b = sys.Get(id);
            if (id == SolarSystemData.Earth) return 1.3e9;
            return Math.Max(b.Radius * 12, b.SphereOfInfluence * 0.6);
        }

        void Update()
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb != null && kb.mKey.wasPressedThisFrame) SetActive(!IsActive);
            if (!IsActive) return;
            if (kb != null && kb.tabKey.wasPressedThisFrame)
            {
                focusIndex = (focusIndex + (kb.shiftKey.isPressed ? focusOrder.Count - 1 : 1)) % focusOrder.Count;
                distance = DefaultDistance(focusOrder[focusIndex]);
            }
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed || mouse.middleButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    yaw += d.x * 0.25f; pitch = Mathf.Clamp(pitch - d.y * 0.25f, -89, 89);
                }
                float s = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(s) > 0.01f) distance *= Math.Pow(1.2, -Math.Sign(s));
            }
            distance = Math.Max(2e5, Math.Min(3e13, distance));
        }

        Vector3d FocusPosition()
        {
            var sim = SimulationController.Instance;
            string id = focusOrder[focusIndex];
            if (id == "vessel" && sim.ActiveVessel?.Trajectory != null) return sim.ActiveVessel.Trajectory.HeliocentricPosition;
            var b = sim.System.Get(id == "vessel" ? SolarSystemData.Earth : id);
            return b.Position;
        }

        void RefreshOrbits()
        {
            var sim = SimulationController.Instance;
            double t = sim.Time;
            foreach (var m in markers)
            {
                if (m.orbit == null) continue;
                KeplerOrbit o;
                if (m.body.Id == SolarSystemData.Moon)
                    o = KeplerOrbit.FromState(Ephemeris.MoonGeocentricState(t), m.body.Parent.Mu + m.body.Mu, t);
                else
                {
                    var rel = m.body.StateRelativeToParent;
                    o = KeplerOrbit.FromState(rel, m.body.Parent.Mu + m.body.Mu, t);
                }
                m.orbitPoints = SampleConic(o, 360, double.PositiveInfinity);
            }
            var v = sim.ActiveVessel;
            if (v?.Trajectory != null)
            {
                var o = v.Trajectory.Orbit;
                double rMax = v.Trajectory.Reference.SphereOfInfluence;
                vesselOrbitPoints = SampleConic(o, 512, rMax);
                pePos = o.StateFromTrueAnomaly(0).position;
                hasAp = !o.IsHyperbolic && o.ApoapsisRadius < rMax;
                if (hasAp) apPos = o.StateFromTrueAnomaly(Math.PI).position;
            }
        }

        static Vector3d[] SampleConic(KeplerOrbit o, int n, double rMax)
        {
            double nuMax = Math.PI;
            if (o.IsHyperbolic)
            {
                double nuInf = Math.Acos(-1.0 / o.eccentricity) * 0.999;
                double p = o.SemiLatusRectum;
                double cosNu = (p / Math.Min(rMax, 1e14) - 1) / o.eccentricity;
                nuMax = Math.Min(nuInf, Math.Acos(Math.Max(-1, Math.Min(1, cosNu))));
            }
            else if (o.ApoapsisRadius > rMax)
            {
                double cosNu = (o.SemiLatusRectum / rMax - 1) / o.eccentricity;
                nuMax = Math.Acos(Math.Max(-1, Math.Min(1, cosNu)));
            }
            bool closed = !o.IsHyperbolic && o.ApoapsisRadius <= rMax;
            var pts = new Vector3d[closed ? n + 1 : n];
            for (int i = 0; i < pts.Length; i++)
            {
                double nu = closed ? 2 * Math.PI * i / n : -nuMax + 2 * nuMax * i / (n - 1);
                pts[i] = o.StateFromTrueAnomaly(nu).position;
            }
            return pts;
        }

        void LateUpdate()
        {
            if (!IsActive) return;
            var sim = SimulationController.Instance;
            if (Time.unscaledTime > nextOrbitRefresh) { RefreshOrbits(); nextOrbitRefresh = Time.unscaledTime + 0.25f; }

            float k = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
            smoothDistance = Math.Exp(Math.Log(smoothDistance) + (Math.Log(distance) - Math.Log(smoothDistance)) * k);
            double scale = smoothDistance / 1000.0; // metres per map unit
            Vector3d focus = FocusPosition();
            Quaternion rot = EclipticFrame * Quaternion.Euler(pitch, yaw, 0);
            mapCamera.transform.SetPositionAndRotation(rot * Vector3.back * 1000f, rot);
            mapCamera.nearClipPlane = 0.5f; mapCamera.farClipPlane = 1e7f;
            float pxToWorld = 2f * Mathf.Tan(mapCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, mapCamera.pixelHeight);
            float lineWidthPx = 1.6f;
            var panel = labelsDocument.rootVisualElement.panel;

            Vector3 ToMap(Vector3d helio) => Coords.ToUnity((helio - focus) / scale);
            var sunPos = sim.System.Sun.Position;
            foreach (var m in markers)
            {
                Vector3 p = ToMap(m.body.Position);
                float camDist = Vector3.Distance(mapCamera.transform.position, p);
                float minSize = camDist * pxToWorld * (m.body.Id == SolarSystemData.Sun ? 10f : 7f);
                float size = Mathf.Max((float)(m.body.Radius / scale), minSize);
                m.t.SetPositionAndRotation(p, Coords.ToUnity(m.body.Orientation));
                m.t.localScale = Vector3.one * size;
                m.block.SetVector("_SunDir", Coords.ToUnity((sunPos - m.body.Position).normalized));
                m.r.SetPropertyBlock(m.block);
                if (m.orbit != null && m.orbitPoints != null)
                {
                    Vector3d parent = m.body.Parent.Position;
                    m.orbit.positionCount = m.orbitPoints.Length;
                    for (int i = 0; i < m.orbitPoints.Length; i++) m.orbit.SetPosition(i, ToMap(parent + m.orbitPoints[i]));
                    m.orbit.widthMultiplier = camDist * pxToWorld * lineWidthPx;
                    // Fade orbits that collapse to a dot at this zoom.
                    double orbitPx = m.orbitPoints[0].magnitude / scale / (camDist * pxToWorld);
                    m.orbit.enabled = orbitPx > 6;
                }
                PlaceLabel(panel, m.label, p, size, (m.orbit == null || m.orbit.enabled || m.body.Parent == null) && InFront(p));
            }

            var v = sim.ActiveVessel;
            if (v?.Trajectory != null)
            {
                Vector3d refPos = v.Trajectory.Reference.Position;
                Vector3 vp = ToMap(v.Trajectory.HeliocentricPosition);
                float camDist = Vector3.Distance(mapCamera.transform.position, vp);
                vesselMarker.position = vp;
                vesselMarker.localScale = Vector3.one * camDist * pxToWorld * 6f;
                vesselOrbit.positionCount = vesselOrbitPoints.Length;
                for (int i = 0; i < vesselOrbitPoints.Length; i++) vesselOrbit.SetPosition(i, ToMap(refPos + vesselOrbitPoints[i]));
                vesselOrbit.widthMultiplier = camDist * pxToWorld * 2.2f;
                double orbitPx = v.Trajectory.Orbit.PeriapsisRadius / scale / (camDist * pxToWorld);
                bool visible = orbitPx > 4;
                vesselOrbit.enabled = visible;
                vesselLabel.text = v.vesselName;
                PlaceLabel(panel, vesselLabel, vp, 0, visible && InFront(vp));
                var o = v.Trajectory.Orbit;
                apLabel.text = $"Ap {HudStyle.Distance(o.ApoapsisRadius - v.Trajectory.Reference.Radius)}";
                peLabel.text = $"Pe {HudStyle.Distance(o.PeriapsisRadius - v.Trajectory.Reference.Radius)}";
                Vector3 ap = ToMap(refPos + apPos), pe = ToMap(refPos + pePos);
                bool roomy = orbitPx > 90;
                PlaceLabel(panel, apLabel, ap, 0, roomy && hasAp && InFront(ap));
                PlaceLabel(panel, peLabel, pe, 0, roomy && InFront(pe));
            }

            string id = focusOrder[focusIndex];
            var fb = id == "vessel" ? null : sim.System.Get(id);
            if (fb != null)
            {
                var d = fb.Definition;
                string orbitInfo = fb.Parent != null ? $"\nOrbit: {HudStyle.Distance(fb.StateRelativeToParent.position.magnitude)} from {fb.Parent.Definition.DisplayName}" : "";
                infoLabel.text = $"{d.DisplayName}\nRadius {d.MeanRadius / 1000:N0} km · g {d.SurfaceGravity:F2} m/s²\nGM {d.GravitationalParameter:E4} m³/s²\nDay {Math.Abs(d.SiderealRotationPeriod) / 3600:N1} h{(d.SiderealRotationPeriod < 0 ? " (retrograde)" : "")}{orbitInfo}\nSOI {(double.IsInfinity(fb.SphereOfInfluence) ? "—" : HudStyle.Distance(fb.SphereOfInfluence))}\nView scale {HudStyle.Distance(smoothDistance)}";
            }
            else infoLabel.text = $"{v?.vesselName}\nAround {v?.Trajectory.Reference.Definition.DisplayName}\nView scale {HudStyle.Distance(smoothDistance)}";
        }

        bool InFront(Vector3 p) => Vector3.Dot(p - mapCamera.transform.position, mapCamera.transform.forward) > 0;

        void PlaceLabel(IPanel panel, Label l, Vector3 world, float size, bool visible)
        {
            l.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible || panel == null) return;
            Vector2 pp = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, mapCamera);
            l.style.left = pp.x + 8;
            l.style.top = pp.y - 8;
        }
    }
}
