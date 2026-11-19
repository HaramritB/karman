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
    /// <summary>
    /// Flight HUD (UI Toolkit). Minimal by default: a mission chip, clock/warp, the navball cluster with altitude,
    /// speed, throttle and modes, a compact staging stack and a small orbit card. Tab opens the full telemetry panel,
    /// F1 the controls sheet. Every number shown is read from the live simulation.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class FlightHud : MonoBehaviour
    {
        public NavballView navball;
        public AscentAutopilot autopilot;
        public LunarAutopilot lunarAutopilot;

        UIDocument doc;
        Label vesselName, situation, clock, warp, toast, statusLabel;
        Label altValue, altCaption, vsValue, radarValue, speedValue, speedCaption, throttleValue, twrValue;
        Label sasChip, assistChip, chuteChip;
        Label ap, pe, timeAp, nodeLine;
        Label inc, ecc, period, timePe, latlon, light, heat, thrust, dv, prop, machQ, refBody, orbV, surfV;
        VisualElement throttleFill, stageList, details, help, status;
        Label stageHeader;
        float toastUntil, nextAnalysis, nextRefresh;
        Karman.Core.Vehicles.CraftAnalysis analysis;

        void Start()
        {
            Karman.Audio.UiSound.PlayAmbient(false);
            if (GetComponent<Karman.Audio.FlightAudio>() == null) gameObject.AddComponent<Karman.Audio.FlightAudio>();
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
                var cur = p;
                while (cur != null && cur.ParentId >= 0)
                {
                    if (veh.Def(cur).IsDecoupler) return cur.Id;
                    cur = veh.Craft.Get(cur.ParentId);
                }
                return -1;
            }
            int s = Section(tank);
            foreach (var e in engines) if (Section(veh.Craft.Get(e)) == s) return true;
            return false;
        }

        static VisualElement Abs(VisualElement parent, float? left = null, float? right = null, float? top = null, float? bottom = null)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
            parent.Add(e);
            return e;
        }

        void Build(VisualElement root)
        {
            root.Clear();
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;
            HudStyle.ApplyFont(root);

            // ---- Mission chip (top-left)
            var chip = Abs(root, left: 28, top: 22);
            vesselName = HudStyle.Value("—", 17);
            situation = HudStyle.Caption("—", HudStyle.Dim); situation.style.marginTop = 3;
            chip.Add(vesselName); chip.Add(situation);

            // ---- Clock + warp (top-right)
            var tr = Abs(root, right: 28, top: 22);
            tr.style.flexDirection = FlexDirection.Row; tr.style.alignItems = Align.Center;
            clock = HudStyle.Value("—", 13, HudStyle.Dim);
            warp = HudStyle.Chip("1×", HudStyle.Accent); warp.style.marginLeft = 10;
            tr.Add(clock); tr.Add(warp);

            // ---- Orbit card (right, below the clock)
            var orbit = HudStyle.PanelBox(220);
            orbit.style.position = Position.Absolute; orbit.style.right = 28; orbit.style.top = 58; orbit.pickingMode = PickingMode.Ignore;
            orbit.Add(HudStyle.Title("ORBIT"));
            ap = HudStyle.Row(orbit, "Apoapsis");
            pe = HudStyle.Row(orbit, "Periapsis");
            timeAp = HudStyle.Row(orbit, "Time to Ap");
            nodeLine = HudStyle.Value("", 11, new Color(1f, 0.66f, 0.28f)); nodeLine.style.marginTop = 6; nodeLine.style.whiteSpace = WhiteSpace.Normal;
            orbit.Add(nodeLine);
            root.Add(orbit);

            // ---- Details panel (Tab)
            details = HudStyle.PanelBox(260);
            details.style.position = Position.Absolute; details.style.right = 28; details.style.top = 216; details.pickingMode = PickingMode.Ignore;
            details.Add(HudStyle.Title("TELEMETRY"));
            refBody = HudStyle.Row(details, "Reference");
            orbV = HudStyle.Row(details, "Orbital speed");
            surfV = HudStyle.Row(details, "Surface speed");
            latlon = HudStyle.Row(details, "Lat / Lon");
            light = HudStyle.Row(details, "Sunlight");
            inc = HudStyle.Row(details, "Inclination");
            ecc = HudStyle.Row(details, "Eccentricity");
            period = HudStyle.Row(details, "Period");
            timePe = HudStyle.Row(details, "Time to Pe");
            var t2 = HudStyle.Title("PROPULSION"); t2.style.marginTop = 10; details.Add(t2);
            thrust = HudStyle.Row(details, "Thrust · Isp");
            prop = HudStyle.Row(details, "Stage propellant");
            dv = HudStyle.Row(details, "Δv stage / total");
            machQ = HudStyle.Row(details, "Mach · Q");
            heat = HudStyle.Row(details, "Heating");
            details.style.display = DisplayStyle.None;
            root.Add(details);

            // ---- Staging (bottom-left)
            var stages = Abs(root, left: 28, bottom: 26);
            stages.style.width = 250; Glass(stages); stages.style.paddingTop = 12; stages.style.paddingBottom = 12;
            stageHeader = HudStyle.Title("STAGING"); stages.Add(stageHeader);
            stageList = new VisualElement { pickingMode = PickingMode.Ignore }; stages.Add(stageList);

            // ---- Navball cluster (bottom-centre)
            var cluster = Abs(root, bottom: 18);
            cluster.style.left = 0; cluster.style.right = 0;
            cluster.style.flexDirection = FlexDirection.Row; cluster.style.justifyContent = Justify.Center; cluster.style.alignItems = Align.FlexEnd;

            var leftBlock = new VisualElement { pickingMode = PickingMode.Ignore };
            leftBlock.style.width = 200; leftBlock.style.alignItems = Align.FlexEnd; leftBlock.style.marginRight = 16; leftBlock.style.marginBottom = 46;
            Glass(leftBlock);
            altCaption = HudStyle.Caption("ALTITUDE");
            altValue = HudStyle.Value("—", 30);
            var vsCap = HudStyle.Caption("VERTICAL SPEED"); vsCap.style.marginTop = 10;
            vsValue = HudStyle.Value("—", 17);
            radarValue = HudStyle.Value("", 11, HudStyle.Dim); radarValue.style.marginTop = 6;
            leftBlock.Add(altCaption); leftBlock.Add(altValue); leftBlock.Add(vsCap); leftBlock.Add(vsValue); leftBlock.Add(radarValue);
            cluster.Add(leftBlock);

            var centre = new VisualElement { pickingMode = PickingMode.Ignore };
            centre.style.alignItems = Align.Center;
            if (navball != null)
            {
                var nb = navball.Build();
                nb.pickingMode = PickingMode.Ignore;
                centre.Add(nb);
            }
            var modes = new VisualElement { pickingMode = PickingMode.Ignore };
            modes.style.flexDirection = FlexDirection.Row; modes.style.marginTop = 8;
            sasChip = HudStyle.Chip("SAS", HudStyle.Dim);
            assistChip = HudStyle.Chip("ASSIST", HudStyle.Dim);
            chuteChip = HudStyle.Chip("CHUTE", HudStyle.Dim);
            modes.Add(sasChip); modes.Add(assistChip); modes.Add(chuteChip);
            centre.Add(modes);
            cluster.Add(centre);

            var rightBlock = new VisualElement { pickingMode = PickingMode.Ignore };
            rightBlock.style.width = 200; rightBlock.style.alignItems = Align.FlexStart; rightBlock.style.marginLeft = 16; rightBlock.style.marginBottom = 46;
            Glass(rightBlock);
            speedCaption = HudStyle.Caption("SURFACE SPEED");
            speedValue = HudStyle.Value("—", 30);
            var thrCap = HudStyle.Caption("THROTTLE"); thrCap.style.marginTop = 10;
            var thrRow = new VisualElement { pickingMode = PickingMode.Ignore }; thrRow.style.flexDirection = FlexDirection.Row; thrRow.style.alignItems = Align.Center;
            throttleValue = HudStyle.Value("0%", 17); throttleValue.style.width = 52;
            var barHost = new VisualElement { pickingMode = PickingMode.Ignore }; barHost.style.width = 120;
            HudStyle.Bar(barHost, HudStyle.Accent, out throttleFill, 5);
            thrRow.Add(throttleValue); thrRow.Add(barHost);
            twrValue = HudStyle.Value("", 11, HudStyle.Dim); twrValue.style.marginTop = 6;
            rightBlock.Add(speedCaption); rightBlock.Add(speedValue); rightBlock.Add(thrCap); rightBlock.Add(thrRow); rightBlock.Add(twrValue);
            cluster.Add(rightBlock);

            // ---- Status banner + event toast (top-centre)
            status = Abs(root, top: 24); status.style.left = 0; status.style.right = 0; status.style.alignItems = Align.Center;
            statusLabel = HudStyle.Chip("", HudStyle.Warn); statusLabel.style.fontSize = 11;
            status.Add(statusLabel);
            toast = HudStyle.Value("", 15, HudStyle.Accent);
            toast.style.position = Position.Absolute; toast.style.top = 62; toast.style.left = 0; toast.style.right = 0;
            toast.style.unityTextAlign = TextAnchor.MiddleCenter; toast.pickingMode = PickingMode.Ignore;
            root.Add(toast);

            // ---- Key hints (bottom-right) and the controls sheet (F1)
            var hints = Abs(root, right: 28, bottom: 26);
            hints.style.flexDirection = FlexDirection.Row;
            foreach (var h in new[] { "F1 CONTROLS", "TAB DETAILS", "M MAP" }) hints.Add(HudStyle.Chip(h, HudStyle.Faint));

            help = HudStyle.PanelBox(520);
            help.style.backgroundColor = HudStyle.PanelSolid;
            help.style.position = Position.Absolute; help.style.left = Length.Percent(50); help.style.top = Length.Percent(50);
            help.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            help.Add(HudStyle.Title("CONTROLS"));
            foreach (var (k, d) in new[]
            {
                ("W S · A D · Q E", "Pitch · yaw · roll"),
                ("Shift / Ctrl · Z · X", "Throttle up/down · full · cut"),
                ("Space", "Activate next stage"),
                ("T · 1–8", "SAS on/off · hold, prograde, retrograde, normal, radial, surface prograde"),
                ("G", "Assist: Earth ascent · lunar landing / ascent"),
                ("C", "Arm parachute"),
                (",  .  /  P", "Time warp down · up · real time · pause"),
                ("Right-drag · scroll", "Orbit camera · zoom"),
                ("V · Backspace", "Cycle focus · back to vessel"),
                ("M · N", "Map and maneuver planning · execute node"),
                ("Tab · Esc", "Telemetry details · main menu"),
            })
            {
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginTop = 4;
                var kl = HudStyle.Value(k, 12, HudStyle.Accent); kl.style.width = 150;
                var dl = new Label(d); dl.style.color = HudStyle.Text; dl.style.fontSize = 12; dl.style.whiteSpace = WhiteSpace.Normal; dl.style.flexShrink = 1;
                row.Add(kl); row.Add(dl); help.Add(row);
            }
            help.style.display = DisplayStyle.None;
            root.Add(help);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (help.style.display == DisplayStyle.Flex) help.style.display = DisplayStyle.None;
                else if (Karman.Map.MapView.IsActive) FindAnyObjectByType<Karman.Map.MapView>().SetActive(false);
                else UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            }
            if (kb != null && kb.nKey.wasPressedThisFrame && ManeuverPlanner.Instance != null) ManeuverPlanner.Instance.BeginExecution();
            if (kb != null && kb.f1Key.wasPressedThisFrame) Toggle(help);
            if (kb != null && kb.tabKey.wasPressedThisFrame) Toggle(details);
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.1f;
            Refresh();
        }

        static void Glass(VisualElement e)
        {
            e.style.backgroundColor = HudStyle.Panel;
            HudStyle.Radius(e, 16); HudStyle.Pad(e, 18, 12);
            HudStyle.Border(e, 1, new Color(1, 1, 1, 0.07f));
        }

        static void Toggle(VisualElement e) => e.style.display = e.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;

        public void ShowDetails(bool on) { if (details != null) details.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; }

        void Refresh()
        {
            var sim = SimulationController.Instance;
            if (sim == null) return;
            var v = sim.ActiveVessel;
            clock.text = TimeSystem.ToUtc(sim.Time).ToString("yyyy-MM-dd  HH:mm:ss") + " UTC";
            HudStyle.SetChip(warp, sim.Paused ? "PAUSED" : $"{sim.Warp:N0}×", sim.Paused ? HudStyle.Warn : sim.Warp > 1 ? HudStyle.Accent : HudStyle.Faint);
            if (v == null || v.Trajectory == null) return;
            var tr = v.Trajectory;
            var o = tr.Orbit;
            string body = tr.Reference.Definition.DisplayName;
            bool orbiting = tr.PeriapsisAltitude > 0 && !o.IsHyperbolic;
            vesselName.text = v.vesselName;
            string sit = v.Crashed ? "LOST" : v.Landed ? (v.Launched ? "LANDED" : "PRELAUNCH") : orbiting ? "ORBIT" : o.IsHyperbolic ? "ESCAPE" : v.InAtmosphere ? "FLYING" : "SUBORBITAL";
            situation.text = $"{body.ToUpperInvariant()}  ·  {sit}  ·  T+ {HudStyle.Duration(v.MissionElapsed)}";

            // Primary readouts.
            bool low = tr.Altitude < 36_000 && tr.Reference.Definition.Atmosphere != null || v.GroundClearance < 5_000;
            altValue.text = HudStyle.Distance(tr.Altitude);
            vsValue.text = (tr.VerticalSpeed >= 0 ? "+" : "") + HudStyle.Speed(tr.VerticalSpeed);
            vsValue.style.color = v.GroundClearance < 1000 && tr.VerticalSpeed < -5 ? HudStyle.Warn : HudStyle.Text;
            radarValue.text = v.GroundClearance < 20_000 ? $"RADAR {HudStyle.Distance(v.GroundClearance)}   ·   {v.GLoad:F2} g" : $"{v.GLoad:F2} g";
            speedCaption.text = low ? "SURFACE SPEED" : "ORBITAL SPEED";
            speedValue.text = HudStyle.Speed(low ? tr.SurfaceVelocity.magnitude : tr.OrbitalSpeed);
            throttleValue.text = $"{v.Throttle * 100:F0}%";
            throttleFill.style.width = Length.Percent(v.Throttle * 100);
            double gLocal = tr.Reference.Mu / tr.RelativeState.position.sqrMagnitude;
            var veh = v.Vehicle;
            twrValue.text = $"TWR {v.CurrentThrust / (veh.Mass * gLocal):F2}" + (v.InAtmosphere ? $"   ·   MACH {v.Mach:F2}" : "");

            string assist = autopilot != null && autopilot.State != AscentAutopilot.Phase.Off ? autopilot.Status
                          : lunarAutopilot != null && lunarAutopilot.State != LunarAutopilot.Phase.Off ? lunarAutopilot.Status : "";
            HudStyle.SetChip(sasChip, v.Sas == SasMode.Off ? "SAS OFF" : $"SAS {v.Sas.ToString().ToUpperInvariant()}", v.Sas == SasMode.Off ? HudStyle.Faint : HudStyle.Accent);
            assistChip.style.display = assist.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (assist.Length > 0) HudStyle.SetChip(assistChip, assist.ToUpperInvariant(), HudStyle.Good);
            var chuteDef = veh.Parachute;
            chuteChip.style.display = chuteDef == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (chuteDef != null) HudStyle.SetChip(chuteChip, veh.ParachuteDeployed ? "CHUTE OPEN" : veh.ParachuteArmed ? "CHUTE ARMED" : "CHUTE", veh.ParachuteDeployed || veh.ParachuteArmed ? HudStyle.Good : HudStyle.Faint);

            // Orbit card.
            ap.text = o.IsHyperbolic ? "Escape" : HudStyle.Distance(tr.ApoapsisAltitude);
            pe.text = HudStyle.Distance(tr.PeriapsisAltitude);
            pe.style.color = tr.PeriapsisAltitude < 0 ? HudStyle.Bad : tr.PeriapsisAltitude < 140_000 && tr.Reference.Definition.Atmosphere != null ? HudStyle.Warn : HudStyle.Text;
            timeAp.text = o.IsHyperbolic ? "—" : HudStyle.Duration(o.TimeToTrueAnomaly(Math.PI, sim.Time));
            var mp = ManeuverPlanner.Instance;
            if (mp != null && mp.Node != null)
                nodeLine.text = mp.Executing ? $"NODE  ·  burning  ·  {Math.Max(0, mp.RemainingDeltaV):F1} m/s left"
                                             : $"NODE  ·  T−{HudStyle.Duration(mp.Node.UT - sim.Time)}  ·  {mp.Node.DeltaV:F1} m/s  ·  N to execute";
            else nodeLine.text = mp != null && !string.IsNullOrEmpty(mp.Status) ? mp.Status : "";
            nodeLine.style.display = nodeLine.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            // Staging stack.
            if (Time.unscaledTime > nextAnalysis || analysis == null) { analysis = veh.Analyse(v.Air.Pressure); nextAnalysis = Time.unscaledTime + 0.5f; }
            var cur = analysis.Stages.Count > 0 ? analysis.Stages[0] : null;
            double stageProp = 0, stageCap = 0;
            var feeding = veh.BurningEngines();
            foreach (var p in veh.AttachedParts)
            {
                double have = 0, cap = 0;
                foreach (var r in p.Resources) have += r.Mass;
                foreach (var r in veh.Def(p).Resources) cap += r.Mass;
                if (cap <= 0) continue;
                if (feeding.Count == 0 || IsFedBy(veh, p, feeding)) { stageProp += have; stageCap += cap; }
            }
            stageHeader.text = $"STAGING  ·  Δv {analysis.TotalDeltaV:N0} m/s";
            stageList.Clear();
            int shown = 0;
            foreach (var st in analysis.Stages)
            {
                if (shown++ >= 5) break;
                bool active = st == cur;
                var card = new VisualElement { pickingMode = PickingMode.Ignore };
                card.style.flexDirection = FlexDirection.Column; card.style.marginTop = 4;
                if (active) { card.style.backgroundColor = HudStyle.Panel; HudStyle.Radius(card, 10); HudStyle.Pad(card, 12, 8); HudStyle.Border(card, 1, new Color(HudStyle.Accent.r, HudStyle.Accent.g, HudStyle.Accent.b, 0.35f)); }
                else { HudStyle.Pad(card, 12, 1); }
                var line = new VisualElement { pickingMode = PickingMode.Ignore }; line.style.flexDirection = FlexDirection.Row; line.style.justifyContent = Justify.SpaceBetween;
                var name = HudStyle.Value($"S{st.Stage}", active ? 14 : 12, active ? HudStyle.Accent : HudStyle.Faint);
                var val = HudStyle.Value(st.DeltaV < 1 ? "separation" : $"{st.DeltaV:N0} m/s", active ? 14 : 12, active ? HudStyle.Text : HudStyle.Dim);
                line.Add(name); line.Add(val); card.Add(line);
                if (active)
                {
                    var sub = new Label($"{HudStyle.Duration(st.BurnTime)} burn  ·  TWR {st.StartTwr:F2}  ·  Space to stage");
                    sub.style.color = HudStyle.Dim; sub.style.fontSize = 11; sub.style.marginTop = 2; HudStyle.Shadow(sub); card.Add(sub);
                    HudStyle.Bar(card, HudStyle.Good, out var fill, 4);
                    fill.style.width = Length.Percent(stageCap > 0 ? (float)(stageProp / stageCap * 100) : 0);
                }
                stageList.Add(card);
            }

            // Details panel (only computed while open).
            if (details.style.display == DisplayStyle.Flex)
            {
                refBody.text = body;
                orbV.text = HudStyle.Speed(tr.OrbitalSpeed);
                surfV.text = HudStyle.Speed(tr.SurfaceVelocity.magnitude);
                tr.LatLon(out double lat, out double lon);
                latlon.text = $"{Math.Abs(lat * Constants.Rad2Deg):F2}°{(lat >= 0 ? "N" : "S")}  {Math.Abs(lon * Constants.Rad2Deg):F2}°{(lon >= 0 ? "E" : "W")}";
                var lighting = FindAnyObjectByType<SpaceLighting>();
                float ill = lighting ? lighting.Illumination : 1;
                light.text = ill > 0.99f ? "Daylight" : ill < 0.01f ? "Eclipse" : $"Penumbra {ill * 100:F0}%";
                inc.text = $"{tr.EquatorialInclination * Constants.Rad2Deg:F3}°";
                ecc.text = $"{o.eccentricity:F5}";
                period.text = o.IsHyperbolic ? "—" : HudStyle.Duration(o.Period);
                timePe.text = HudStyle.Duration(o.TimeToTrueAnomaly(0, sim.Time));
                thrust.text = v.CurrentThrust > 0 ? $"{v.CurrentThrust / 1000:N0} kN · {v.CurrentIsp:F0} s" : "0 kN";
                prop.text = stageCap > 0 ? $"{stageProp / 1000:N2} / {stageCap / 1000:N1} t" : "—";
                dv.text = $"{(cur != null ? cur.DeltaV : 0):N0} / {analysis.TotalDeltaV:N0} m/s";
                machQ.text = v.InAtmosphere ? $"{v.Mach:F2} · {v.DynamicPressure / 1000:F1} kPa" : "vacuum";
                heat.text = $"{v.HeatFlux / 1000:N0} kW/m²" + (veh.HeatShield != null ? $" · shield {100 * (1 - veh.HeatLoad / veh.HeatShield.HeatShieldCapacity):F0}%" : "");
            }

            string st2 = v.Crashed ? "VESSEL LOST" : v.Landed && !v.Launched ? "ON THE PAD  ·  Z full throttle, Space to ignite  ·  G ascent assist" : "";
            statusLabel.text = st2;
            statusLabel.style.display = st2.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            HudStyle.SetChip(statusLabel, st2, v.Crashed ? HudStyle.Bad : HudStyle.Warn);
            toast.style.top = st2.Length > 0 ? 62 : 28;
            if (Time.unscaledTime > toastUntil) toast.text = "";
        }
    }
}
