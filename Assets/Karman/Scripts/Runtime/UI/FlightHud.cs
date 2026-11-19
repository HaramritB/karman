using System;
using Karman.Core;
using Karman.Flight;
using Karman.Rendering;
using Karman.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Karman.UI
{
    /// <summary>Flight telemetry built with UI Toolkit. Every number shown is read from the live simulation.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class FlightHud : MonoBehaviour
    {
        UIDocument doc;
        Label clock, warp, met, vesselLabel, refBody;
        Label alt, orbV, surfV, vertV, latlon;
        Label ap, pe, inc, ecc, period, timeAp, timePe;
        Label throttle, thrust, dv, burn, twr, sas, prop, light, stageLabel, machQ, toast, radar, entry;
        public Karman.Flight.LunarAutopilot lunarAutopilot;
        VisualElement stageStack;
        float toastUntil, nextAnalysis;
        Karman.Core.Vehicles.CraftAnalysis analysis;
        public NavballView navball;
        public Karman.Flight.AscentAutopilot autopilot;
        VisualElement throttleFill, propFill, help, status;
        Label statusLabel;
        float nextRefresh;

        void Start()
        {
            doc = GetComponent<UIDocument>();
            Build(doc.rootVisualElement);
            var v = SimulationController.Instance.ActiveVessel;
            if (v != null) v.Event += msg => { toast.text = msg; toastUntil = Time.unscaledTime + 4f; Debug.Log("[Karman] " + msg); };
        }

        static bool IsFedBy(Karman.Core.Vehicles.FlightVehicle veh, Karman.Core.Vehicles.PartInstance tank, System.Collections.Generic.List<int> engines)
        {
            // Same section = no separator between the tank and the engine along the tree.
            int Section(Karman.Core.Vehicles.PartInstance p)
            {
                int depth = 0; var cur = p;
                while (cur != null && cur.ParentId >= 0)
                {
                    if (veh.Def(cur).IsDecoupler) return cur.Id;
                    cur = veh.Craft.Get(cur.ParentId); depth++;
                }
                return -1;
            }
            int s = Section(tank);
            foreach (var e in engines) if (Section(veh.Craft.Get(e)) == s) return true;
            return false;
        }

        void Build(VisualElement root)
        {
            root.Clear();
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;

            // Top bar: brand, clock, warp.
            var top = new VisualElement();
            top.style.position = Position.Absolute; top.style.top = 14; top.style.left = 0; top.style.right = 0;
            top.style.flexDirection = FlexDirection.Row; top.style.justifyContent = Justify.Center;
            top.pickingMode = PickingMode.Ignore;
            var bar = HudStyle.PanelBox(560);
            bar.style.flexDirection = FlexDirection.Row; bar.style.justifyContent = Justify.SpaceBetween; bar.style.alignItems = Align.Center;
            bar.style.paddingTop = 7; bar.style.paddingBottom = 7;
            var brand = new Label("KÁRMÁN"); brand.style.color = HudStyle.Text; brand.style.fontSize = 15; brand.style.letterSpacing = 5; brand.style.unityFontStyleAndWeight = FontStyle.Bold;
            clock = new Label("—"); clock.style.color = HudStyle.Text; clock.style.fontSize = 13;
            warp = new Label("1×"); warp.style.color = HudStyle.Accent; warp.style.fontSize = 14; warp.style.unityFontStyleAndWeight = FontStyle.Bold;
            bar.Add(brand); bar.Add(clock); bar.Add(warp);
            top.Add(bar);
            root.Add(top);

            // Left column: vessel + flight + orbit.
            var left = new VisualElement();
            left.style.position = Position.Absolute; left.style.left = 18; left.style.top = 70;
            left.pickingMode = PickingMode.Ignore;
            var flight = HudStyle.PanelBox(270);
            vesselLabel = HudStyle.Title("GEMINI · EARTH ORBIT");
            flight.Add(vesselLabel);
            met = HudStyle.Row(flight, "Mission time");
            refBody = HudStyle.Row(flight, "Reference");
            alt = HudStyle.Row(flight, "Altitude");
            orbV = HudStyle.Row(flight, "Orbital speed");
            surfV = HudStyle.Row(flight, "Surface speed");
            vertV = HudStyle.Row(flight, "Vertical speed");
            latlon = HudStyle.Row(flight, "Lat / Lon");
            light = HudStyle.Row(flight, "Sunlight");
            radar = HudStyle.Row(flight, "Radar alt · g");
            entry = HudStyle.Row(flight, "Heat · chute");
            left.Add(flight);

            var orbit = HudStyle.PanelBox(270);
            orbit.style.marginTop = 10;
            orbit.Add(HudStyle.Title("ORBIT"));
            ap = HudStyle.Row(orbit, "Apoapsis");
            pe = HudStyle.Row(orbit, "Periapsis");
            timeAp = HudStyle.Row(orbit, "Time to Ap");
            timePe = HudStyle.Row(orbit, "Time to Pe");
            inc = HudStyle.Row(orbit, "Inclination");
            ecc = HudStyle.Row(orbit, "Eccentricity");
            period = HudStyle.Row(orbit, "Period");
            left.Add(orbit);
            root.Add(left);

            // Right column: propulsion.
            var right = new VisualElement();
            right.style.position = Position.Absolute; right.style.right = 18; right.style.bottom = 18;
            right.pickingMode = PickingMode.Ignore;
            var propPanel = HudStyle.PanelBox(270);
            propPanel.Add(HudStyle.Title("PROPULSION"));
            throttle = HudStyle.Row(propPanel, "Throttle");
            HudStyle.Bar(propPanel, HudStyle.Accent, out throttleFill);
            prop = HudStyle.Row(propPanel, "Stage propellant");
            HudStyle.Bar(propPanel, new Color(0.45f, 0.9f, 0.6f), out propFill);
            thrust = HudStyle.Row(propPanel, "Thrust · Isp");
            twr = HudStyle.Row(propPanel, "TWR (local g)");
            dv = HudStyle.Row(propPanel, "Δv stage / total");
            burn = HudStyle.Row(propPanel, "Stage burn time");
            machQ = HudStyle.Row(propPanel, "Mach · Q");
            sas = HudStyle.Row(propPanel, "SAS · Assist");
            right.Add(propPanel);

            var stagesPanel = HudStyle.PanelBox(270);
            stagesPanel.style.marginBottom = 10;
            stageLabel = HudStyle.Title("STAGES · Space to stage");
            stagesPanel.Add(stageLabel);
            stageStack = new VisualElement(); stagesPanel.Add(stageStack);
            right.Insert(0, stagesPanel);

            if (navball != null)
            {
                var nb = navball.Build();
                nb.style.position = Position.Absolute; nb.style.bottom = 14; nb.style.left = Length.Percent(50); nb.style.translate = new Translate(-115, 0);
                nb.pickingMode = PickingMode.Ignore;
                root.Add(nb);
            }
            toast = new Label(); toast.style.position = Position.Absolute; toast.style.top = 108; toast.style.left = 0; toast.style.right = 0;
            toast.style.unityTextAlign = TextAnchor.MiddleCenter; toast.style.color = HudStyle.Accent; toast.style.fontSize = 15; toast.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(toast);
            root.Add(right);

            // Status banner.
            status = new VisualElement();
            status.style.position = Position.Absolute; status.style.top = 70; status.style.left = 0; status.style.right = 0;
            status.style.alignItems = Align.Center; status.pickingMode = PickingMode.Ignore;
            statusLabel = new Label(""); statusLabel.style.color = HudStyle.Warn; statusLabel.style.fontSize = 16; statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            status.Add(statusLabel);
            root.Add(status);

            // Controls help (F1).
            help = HudStyle.PanelBox(330);
            help.style.position = Position.Absolute; help.style.left = 18; help.style.bottom = 18;
            help.Add(HudStyle.Title("CONTROLS  ·  F1"));
            foreach (var line in new[]
            {
                "W/S pitch · A/D yaw · Q/E roll",
                "Shift/Ctrl throttle · Z full · X cut · Space stage",
                "T SAS on/off · 1 hold · 2 prograde · 3 retrograde",
                "4/5 normal/anti-normal · 6/7 radial · 8 surface prograde",
                "G assist: Earth ascent · lunar landing / ascent · C arm parachute",
                ", / . time warp · / real time · P pause",
                "Right-drag orbit camera · scroll zoom",
                "V cycle focus · Backspace vessel · M map (plan nodes) · N execute node · Esc menu",
            })
            {
                var l = new Label(line); l.style.color = HudStyle.Dim; l.style.fontSize = 12; l.style.marginTop = 1;
                help.Add(l);
            }
            root.Add(help);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (Karman.Map.MapView.IsActive) FindAnyObjectByType<Karman.Map.MapView>().SetActive(false);
                else UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            }
            if (kb != null && kb.nKey.wasPressedThisFrame && Karman.Flight.ManeuverPlanner.Instance != null) Karman.Flight.ManeuverPlanner.Instance.BeginExecution();
            if (kb != null && kb.f1Key.wasPressedThisFrame) help.style.display = help.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.1f;
            Refresh();
        }

        void Refresh()
        {
            var sim = SimulationController.Instance;
            var v = sim?.ActiveVessel;
            if (sim == null) return;
            clock.text = TimeSystem.ToUtc(sim.Time).ToString("yyyy-MM-dd  HH:mm:ss") + " UTC";
            warp.text = sim.Paused ? "PAUSED" : $"{sim.Warp:N0}×";
            if (v == null || v.Trajectory == null) return;
            var tr = v.Trajectory;
            var o = tr.Orbit;
            string body = tr.Reference.Definition.DisplayName;
            vesselLabel.text = $"{v.vesselName.ToUpperInvariant()} · {body.ToUpperInvariant()} {(tr.PeriapsisAltitude > 0 && !o.IsHyperbolic ? "ORBIT" : o.IsHyperbolic ? "ESCAPE" : "SUBORBITAL")}";
            met.text = "T+ " + HudStyle.Duration(v.MissionElapsed);
            refBody.text = body;
            alt.text = HudStyle.Distance(tr.Altitude);
            orbV.text = HudStyle.Speed(tr.OrbitalSpeed);
            surfV.text = HudStyle.Speed(tr.SurfaceVelocity.magnitude);
            vertV.text = HudStyle.Speed(tr.VerticalSpeed);
            tr.LatLon(out double lat, out double lon);
            latlon.text = $"{Math.Abs(lat * Constants.Rad2Deg):F2}°{(lat >= 0 ? "N" : "S")}  {Math.Abs(lon * Constants.Rad2Deg):F2}°{(lon >= 0 ? "E" : "W")}";
            var lighting = FindAnyObjectByType<SpaceLighting>();
            float ill = lighting ? lighting.Illumination : 1;
            light.text = ill > 0.99f ? "Daylight" : ill < 0.01f ? "Eclipse" : $"Penumbra {ill * 100:F0}%";

 radar.text = $"{HudStyle.Distance(v.GroundClearance)} · {v.GLoad:F2} g";
            radar.style.color = v.GroundClearance < 1000 && tr.VerticalSpeed < -5 ? HudStyle.Warn : HudStyle.Text;
            var chuteDef = v.Vehicle.Parachute;
            entry.text = $"{v.HeatFlux / 1000:N0} kW/m² · {(chuteDef == null ? "none" : v.Vehicle.ParachuteDeployed ? "deployed" : v.Vehicle.ParachuteArmed ? "armed" : "stowed (C)")}";
            if (v.Vehicle.HeatShield != null) entry.text += $" · shield {100 * (1 - v.Vehicle.HeatLoad / v.Vehicle.HeatShield.HeatShieldCapacity):F0}%";
            ap.text = o.IsHyperbolic ? "Escape" : HudStyle.Distance(tr.ApoapsisAltitude);
            pe.text = HudStyle.Distance(tr.PeriapsisAltitude);
            pe.style.color = tr.PeriapsisAltitude < 0 ? HudStyle.Bad : tr.PeriapsisAltitude < 140_000 && tr.Reference.Definition.Atmosphere != null ? HudStyle.Warn : HudStyle.Text;
            timeAp.text = o.IsHyperbolic ? "—" : HudStyle.Duration(o.TimeToTrueAnomaly(Math.PI, sim.Time));
            timePe.text = HudStyle.Duration(o.TimeToTrueAnomaly(0, sim.Time));
            inc.text = $"{tr.EquatorialInclination * Constants.Rad2Deg:F3}°";
            ecc.text = $"{o.eccentricity:F5}";
            period.text = o.IsHyperbolic ? "—" : HudStyle.Duration(o.Period);

            var veh = v.Vehicle;
            if (Time.unscaledTime > nextAnalysis || analysis == null) { analysis = veh.Analyse(v.Air.Pressure); nextAnalysis = Time.unscaledTime + 0.5f; }
            var cur = analysis.Stages.Count > 0 ? analysis.Stages[0] : null;
            throttle.text = $"{v.Throttle * 100:F0}%";
            throttleFill.style.width = Length.Percent(v.Throttle * 100);
            // Propellant of the sections feeding the engines that are lit (or will be lit next).
            double stageProp = 0, stageCap = 0;
            var feeding = veh.BurningEngines();
            var secs = new System.Collections.Generic.HashSet<int>();
            foreach (var p in veh.AttachedParts)
            {
                double have = 0, cap = 0;
                foreach (var r in p.Resources) have += r.Mass;
                foreach (var r in veh.Def(p).Resources) cap += r.Mass;
                if (cap <= 0) continue;
                bool inFeed = feeding.Count == 0 || IsFedBy(veh, p, feeding);
                if (inFeed) { stageProp += have; stageCap += cap; }
            }
            prop.text = stageCap > 0 ? $"{stageProp / 1000:N2} / {stageCap / 1000:N1} t" : "—";
            propFill.style.width = Length.Percent(stageCap > 0 ? (float)(stageProp / stageCap * 100) : 0);
            thrust.text = v.CurrentThrust > 0 ? $"{v.CurrentThrust / 1000:N0} kN · {v.CurrentIsp:F0} s" : "0 kN";
            double gLocal = tr.Reference.Mu / tr.RelativeState.position.sqrMagnitude;
            twr.text = $"{v.CurrentThrust / (veh.Mass * gLocal):F2}  (max {v.CurrentMaxThrust / (veh.Mass * gLocal):F2})";
            dv.text = $"{(cur != null ? cur.DeltaV : 0):N0} / {analysis.TotalDeltaV:N0} m/s";
            burn.text = cur != null ? HudStyle.Duration(cur.BurnTime) : "—";
            machQ.text = v.InAtmosphere ? $"{v.Mach:F2} · {v.DynamicPressure / 1000:F1} kPa" : "vacuum";
            string assist = autopilot != null && autopilot.State != Karman.Flight.AscentAutopilot.Phase.Off ? autopilot.Status
                          : lunarAutopilot != null && lunarAutopilot.State != Karman.Flight.LunarAutopilot.Phase.Off ? lunarAutopilot.Status : "";
            sas.text = $"{v.Sas}{(assist.Length > 0 ? " · " + assist : "")}";
            sas.style.color = v.Sas == SasMode.Off ? HudStyle.Dim : HudStyle.Accent;

            stageStack.Clear();
            stageLabel.text = $"STAGES · next {(veh.NextStage >= 0 ? veh.NextStage.ToString() : "—")} · Space";
            foreach (var st in analysis.Stages)
            {
                var row = new Label($"S{st.Stage}  {st.DeltaV:N0} m/s  ·  {HudStyle.Duration(st.BurnTime)}  ·  TWR {st.StartTwr:F2}");
                row.style.fontSize = 12; row.style.color = st == cur ? HudStyle.Text : HudStyle.Dim; row.style.marginTop = 2;
                stageStack.Add(row);
            }
            var mp = Karman.Flight.ManeuverPlanner.Instance;
            if (mp != null && mp.Node != null)
            {
                var nrow = new Label(mp.Executing
                    ? $"NODE · burning · {Math.Max(0, mp.RemainingDeltaV):F1} m/s left"
                    : $"NODE · T−{HudStyle.Duration(mp.Node.UT - sim.Time)} · {mp.Node.DeltaV:F1} m/s · burn {HudStyle.Duration(mp.BurnTime)}  (N execute)");
                nrow.style.color = new Color(1f, 0.62f, 0.2f); nrow.style.fontSize = 12; nrow.style.marginTop = 6;
                stageStack.Add(nrow);
            }
            else if (mp != null && !string.IsNullOrEmpty(mp.Status))
            {
                var nrow = new Label(mp.Status); nrow.style.color = HudStyle.Dim; nrow.style.fontSize = 11; nrow.style.marginTop = 6; stageStack.Add(nrow);
            }
            if (v.Landed && !v.Launched) statusLabel.text = "ON THE PAD · Z full throttle, Space to ignite · or G for ascent assist";
            else statusLabel.text = v.Crashed ? "VESSEL LOST · IMPACT" : v.Landed ? "LANDED" : "";
            if (Time.unscaledTime > toastUntil) toast.text = "";
        }
    }
}
