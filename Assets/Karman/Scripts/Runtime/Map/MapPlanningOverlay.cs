using System;
using System.Collections.Generic;
using Karman.Core;
using Karman.Flight;
using Karman.Simulation;
using Karman.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Karman.Map
{
    /// <summary>
    /// Mission-planning layer of the map: patched-conic trajectory (current and planned), maneuver node placement by
    /// clicking the trajectory, a node editor (prograde/normal/radial Δv and timing), encounter and apsis markers,
    /// target selection with relative inclination and closest approach, and node execution.
    /// </summary>
    [DefaultExecutionOrder(160)]
    public sealed class MapPlanningOverlay : MonoBehaviour
    {
        public MapView map;
        public ManeuverPlanner planner;
        public Material lineMaterial;

        static readonly Color CurrentColor = new Color(0.36f, 0.78f, 1f, 0.95f);
        static readonly Color LunarColor = new Color(0.75f, 0.95f, 0.55f, 0.95f);
        static readonly Color PlannedColor = new Color(1f, 0.62f, 0.2f, 0.95f);
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        readonly List<(Vector3 map, double time)> pickPoints = new List<(Vector3, double)>();
        int usedLines;
        Transform nodeMarker;
        Label nodeLabel, encounterLabel, plannedAp, plannedPe, closestLabel, info;
        VisualElement panel;
        double step = 10;
        Label stepLabel;

        void Start()
        {
            map.ExternalVesselTrajectory = true;
            var root = map.LabelsRoot;
            nodeLabel = Tag(root, PlannedColor); encounterLabel = Tag(root, LunarColor); plannedAp = Tag(root, PlannedColor); plannedPe = Tag(root, PlannedColor); closestLabel = Tag(root, LunarColor);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.layer = MapView.Layer; go.name = "Node Marker";
            go.GetComponent<Renderer>().sharedMaterial = new Material(lineMaterial) { color = PlannedColor };
            go.transform.SetParent(map.transform, false);
            nodeMarker = go.transform;
            BuildPanel(root);
        }

        static Label Tag(VisualElement root, Color c)
        {
            var l = new Label(); l.style.position = Position.Absolute; l.style.color = c; l.style.fontSize = 12; l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.backgroundColor = new Color(0, 0, 0, 0.45f); l.style.paddingLeft = 6; l.style.paddingRight = 6;
            l.style.borderTopLeftRadius = l.style.borderTopRightRadius = l.style.borderBottomLeftRadius = l.style.borderBottomRightRadius = 4;
            l.pickingMode = PickingMode.Ignore; l.style.display = DisplayStyle.None;
            root.Add(l); return l;
        }

        static Button Btn(string t, Action a, float w = 0)
        {
            var b = new Button(a) { text = t };
            b.style.backgroundColor = new Color(1, 1, 1, 0.07f); b.style.color = HudStyle.Text; b.style.fontSize = 12;
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 1;
            var bc = new Color(1, 1, 1, 0.12f); b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = bc;
            b.style.borderTopLeftRadius = b.style.borderTopRightRadius = b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 5;
            b.style.marginLeft = 2; b.style.marginRight = 2; b.style.marginTop = 2; b.style.paddingTop = 3; b.style.paddingBottom = 3;
            if (w > 0) b.style.width = w;
            return b;
        }

        static VisualElement Row() { var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row; r.style.alignItems = Align.Center; r.style.flexWrap = Wrap.Wrap; return r; }

        void BuildPanel(VisualElement root)
        {
            panel = HudStyle.PanelBox(560);
            panel.style.position = Position.Absolute; panel.style.bottom = 18; panel.style.left = Length.Percent(50); panel.style.translate = new Translate(Length.Percent(-50), 0);
            panel.Add(HudStyle.Title("MANEUVER PLANNING · click the trajectory to place a node"));
            var plan = Row();
            plan.Add(Btn("Node at Ap", () => planner.CreateNodeAtApsis(true)));
            plan.Add(Btn("Node at Pe", () => planner.CreateNodeAtApsis(false)));
            plan.Add(Btn("Circularise", planner.PlanCircularisation));
            plan.Add(Btn("Plan Moon transfer", planner.PlanLunarTransfer));
            plan.Add(Btn("Target Moon", () => planner.Target = planner.Target == null ? SimulationController.Instance.System.Get(SolarSystemData.Moon) : null));
            panel.Add(plan);
            var dv = Row();
            stepLabel = new Label(); stepLabel.style.color = HudStyle.Accent; stepLabel.style.width = 70;
            dv.Add(Btn("step", () => { step = step >= 100 ? 0.1 : step * 10; }, 44)); dv.Add(stepLabel);
            dv.Add(Btn("Pro +", () => planner.Adjust(step, 0, 0, 0))); dv.Add(Btn("Pro −", () => planner.Adjust(-step, 0, 0, 0)));
            dv.Add(Btn("Nrm +", () => planner.Adjust(0, step, 0, 0))); dv.Add(Btn("Nrm −", () => planner.Adjust(0, -step, 0, 0)));
            dv.Add(Btn("Rad +", () => planner.Adjust(0, 0, step, 0))); dv.Add(Btn("Rad −", () => planner.Adjust(0, 0, -step, 0)));
            panel.Add(dv);
            var time = Row();
            foreach (var (label, dt) in new[] { ("−10m", -600.0), ("−1m", -60.0), ("−10s", -10.0), ("+10s", 10.0), ("+1m", 60.0), ("+10m", 600.0) })
                time.Add(Btn(label, () => planner.Adjust(0, 0, 0, dt)));
            time.Add(Btn("+orbit", () => { var o = planner.vessel.Trajectory.Orbit; if (!o.IsHyperbolic) planner.Adjust(0, 0, 0, o.Period); }));
            time.Add(Btn("Delete", planner.DeleteNode));
            var exec = Btn("EXECUTE", () => { planner.BeginExecution(); map.SetActive(false); });
            exec.style.backgroundColor = new Color(1f, 0.62f, 0.2f, 0.85f); exec.style.color = new Color(0.08f, 0.05f, 0.02f); exec.style.unityFontStyleAndWeight = FontStyle.Bold;
            time.Add(exec);
            panel.Add(time);
            info = new Label(); info.style.color = HudStyle.Text; info.style.fontSize = 12; info.style.whiteSpace = WhiteSpace.Normal; info.style.marginTop = 4;
            panel.Add(info);
            root.Add(panel);
        }

        void Update()
        {
            if (!MapView.IsActive) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || planner.Executing) return;
            Vector2 mp = mouse.position.ReadValue();
            var p = map.LabelsRoot.panel;
            if (p != null)
            {
                var picked = p.Pick(RuntimePanelUtils.ScreenToPanel(p, new Vector2(mp.x, Screen.height - mp.y)));
                if (picked != null && picked != map.LabelsRoot) return;
            }
            // Nearest sampled trajectory point on screen.
            double bestT = double.NaN; float best = 14f;
            foreach (var (pt, t) in pickPoints)
            {
                Vector3 sp = map.Camera.WorldToScreenPoint(pt);
                if (sp.z <= 0) continue;
                float d = Vector2.Distance(mp, sp);
                if (d < best) { best = d; bestT = t; }
            }
            if (double.IsNaN(bestT)) return;
            if (planner.Node == null) planner.CreateNode(bestT);
            else planner.Adjust(0, 0, 0, bestT - planner.Node.UT);
        }

        void LateUpdate()
        {
            usedLines = 0;
            if (!MapView.IsActive || planner == null || planner.vessel == null || planner.vessel.Trajectory == null) { HideAll(); return; }
            var sim = SimulationController.Instance;
            pickPoints.Clear();
            var uiPanel = map.LabelsRoot.panel;
            if (planner.Current != null) DrawPrediction(planner.Current, false, true);
            if (planner.Planned != null) DrawPrediction(planner.Planned, true, false);
            for (int i = usedLines; i < lines.Count; i++) lines[i].enabled = false;

            var node = planner.Node;
            if (node != null && planner.Current?.SegmentAt(node.UT) is TrajectorySegment seg)
            {
                Vector3 p = map.MapPoint(BodyNow(seg.Body) + seg.Orbit.StateAt(node.UT).position);
                nodeMarker.gameObject.SetActive(true);
                nodeMarker.position = p;
                nodeMarker.localScale = Vector3.one * Vector3.Distance(map.Camera.transform.position, p) * map.PixelToWorld * 9f;
                Place(uiPanel, nodeLabel, p, $"Node · {node.DeltaV:F1} m/s · T−{HudStyle.Duration(node.UT - sim.Time)}");
            }
            else { nodeMarker.gameObject.SetActive(false); nodeLabel.style.display = DisplayStyle.None; }

            var pred = planner.Planned ?? planner.Current;
            encounterLabel.style.display = DisplayStyle.None; closestLabel.style.display = DisplayStyle.None;
            if (pred != null && pred.EncounterBody != null)
            {
                var es = pred.SegmentAt(pred.EncounterTime + 1);
                if (es != null)
                {
                    Vector3 p = map.MapPoint(BodyNow(es.Body) + es.Orbit.StateAt(pred.EncounterTime).position);
                    Place(uiPanel, encounterLabel, p, $"{pred.EncounterBody.Definition.DisplayName} encounter · T−{HudStyle.Duration(pred.EncounterTime - sim.Time)} · Pe {HudStyle.Distance(pred.EncounterPeriapsisAltitude)}");
                }
            }
            else if (pred != null && planner.Target != null && !double.IsNaN(pred.ClosestApproachTime))
            {
                var cs = pred.SegmentAt(pred.ClosestApproachTime);
                if (cs != null)
                {
                    Vector3 p = map.MapPoint(BodyNow(cs.Body) + cs.Orbit.StateAt(pred.ClosestApproachTime).position);
                    Place(uiPanel, closestLabel, p, $"Closest to {planner.Target.Definition.DisplayName}: {HudStyle.Distance(pred.ClosestApproachDistance)}");
                }
            }

            plannedAp.style.display = DisplayStyle.None; plannedPe.style.display = DisplayStyle.None;
            if (planner.Planned != null && planner.Planned.Segments.Count > 0)
            {
                var s0 = planner.Planned.Segments[0];
                var o = s0.Orbit;
                Vector3d bodyPos = BodyNow(s0.Body);
                if (!o.IsHyperbolic && o.ApoapsisRadius < s0.Body.SphereOfInfluence)
                    Place(uiPanel, plannedAp, map.MapPoint(bodyPos + o.StateFromTrueAnomaly(Math.PI).position), $"Ap {HudStyle.Distance(o.ApoapsisRadius - s0.Body.Radius)}");
                Place(uiPanel, plannedPe, map.MapPoint(bodyPos + o.StateFromTrueAnomaly(0).position), $"Pe {HudStyle.Distance(o.PeriapsisRadius - s0.Body.Radius)}");
            }

            stepLabel.text = $"{step:0.#} m/s";
            info.text = InfoText();
        }

        string InfoText()
        {
            var sim = SimulationController.Instance;
            var tr = planner.vessel.Trajectory;
            string s = "";
            if (planner.Node != null)
            {
                var n = planner.Node;
                s += $"Δv {n.DeltaV:F1} m/s (pro {n.Prograde:F1}, nrm {n.Normal:F1}, rad {n.Radial:F1}) · burn {HudStyle.Duration(planner.BurnTime)} · in {HudStyle.Duration(n.UT - sim.Time)}";
                if (planner.Planned?.Segments.Count > 0)
                {
                    var o = planner.Planned.Segments[0].Orbit; var b = planner.Planned.Segments[0].Body;
                    double inc = Vector3d.Angle(o.Normal, b.Orientation.Column(2)) * Constants.Rad2Deg;
                    s += $"\nAfter burn: Pe {HudStyle.Distance(o.PeriapsisRadius - b.Radius)} · Ap {(o.IsHyperbolic ? "escape" : HudStyle.Distance(o.ApoapsisRadius - b.Radius))} · i {inc:F2}°";
                }
            }
            else s += "No node. Click the blue trajectory, or use a planning button.";
            if (planner.Target != null && planner.Target.Parent == tr.Reference)
            {
                var ts = TrajectoryPredictor.RelativeState(planner.Target, tr.Reference, sim.Time);
                double relInc = Vector3d.Angle(Vector3d.Cross(tr.RelativeState.position, tr.RelativeState.velocity), Vector3d.Cross(ts.position, ts.velocity)) * Constants.Rad2Deg;
                s += $"\nTarget {planner.Target.Definition.DisplayName}: relative inclination {relInc:F2}°";
            }
            if (!string.IsNullOrEmpty(planner.Status)) s += $"\n{planner.Status}";
            return s;
        }

        Vector3d BodyNow(CelestialBody b) => b.Position;

        void DrawPrediction(TrajectoryPrediction pred, bool planned, bool pickable)
        {
            foreach (var seg in pred.Segments)
            {
                var lr = NextLine();
                Color c = planned ? PlannedColor : seg.Body.Id == SolarSystemData.Moon ? LunarColor : CurrentColor;
                lr.startColor = lr.endColor = c;
                const int N = 240;
                lr.positionCount = N + 1;
                Vector3d bodyPos = BodyNow(seg.Body);
                bool fullOrbit = !seg.Orbit.IsHyperbolic && seg.End - seg.Start >= seg.Orbit.Period * 0.999;
                float widthPx = planned ? 2.0f : 2.4f;
                for (int i = 0; i <= N; i++)
                {
                    double t;
                    Vector3d rel;
                    if (fullOrbit) { double nu = 2 * Math.PI * i / N; rel = seg.Orbit.StateFromTrueAnomaly(nu).position; t = seg.Start + seg.Orbit.TimeToTrueAnomaly(nu, seg.Start); }
                    else { t = seg.Start + (seg.End - seg.Start) * i / N; rel = seg.Orbit.StateAt(t).position; }
                    Vector3 p = map.MapPoint(bodyPos + rel);
                    lr.SetPosition(i, p);
                    if (pickable && seg == pred.Segments[0]) pickPoints.Add((p, t));
                }
                Vector3 mid = lr.GetPosition(N / 2);
                lr.widthMultiplier = Vector3.Distance(map.Camera.transform.position, mid) * map.PixelToWorld * widthPx;
                lr.enabled = true;
            }
        }

        LineRenderer NextLine()
        {
            if (usedLines < lines.Count) return lines[usedLines++];
            var go = new GameObject("Trajectory") { layer = MapView.Layer };
            go.transform.SetParent(map.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = lineMaterial; lr.useWorldSpace = true; lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lines.Add(lr); usedLines++;
            return lr;
        }

        void HideAll()
        {
            foreach (var l in lines) l.enabled = false;
            if (nodeMarker) nodeMarker.gameObject.SetActive(false);
        }

        void Place(IPanel p, Label l, Vector3 world, string text)
        {
            bool front = Vector3.Dot(world - map.Camera.transform.position, map.Camera.transform.forward) > 0;
            l.style.display = front && p != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (!front || p == null) return;
            l.text = text;
            Vector2 pp = RuntimePanelUtils.CameraTransformWorldToPanel(p, world, map.Camera);
            l.style.left = pp.x + 10; l.style.top = pp.y + 6;
        }
    }
}
