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
        Label throttle, thrust, dv, burn, twr, sas, prop, light;
        VisualElement throttleFill, propFill, help, status;
        Label statusLabel;
        float nextRefresh;

        void OnEnable()
        {
            doc = GetComponent<UIDocument>();
            Build(doc.rootVisualElement);
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
            propPanel.Add(HudStyle.Title("PROPULSION · OAMS"));
            throttle = HudStyle.Row(propPanel, "Throttle");
            HudStyle.Bar(propPanel, HudStyle.Accent, out throttleFill);
            prop = HudStyle.Row(propPanel, "Propellant");
            HudStyle.Bar(propPanel, new Color(0.45f, 0.9f, 0.6f), out propFill);
            thrust = HudStyle.Row(propPanel, "Thrust");
            twr = HudStyle.Row(propPanel, "Accel.");
            dv = HudStyle.Row(propPanel, "Δv remaining");
            burn = HudStyle.Row(propPanel, "Burn time");
            sas = HudStyle.Row(propPanel, "SAS");
            right.Add(propPanel);
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
                "Shift/Ctrl throttle · Z full · X cut",
                "T SAS on/off · 1 hold · 2 prograde · 3 retrograde",
                "4/5 normal/anti-normal · 6/7 radial out/in",
                ", / . time warp · / real time · P pause",
                "Right-drag orbit camera · scroll zoom",
                "V cycle focus · Backspace vessel · M map · Esc menu",
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

            ap.text = o.IsHyperbolic ? "Escape" : HudStyle.Distance(tr.ApoapsisAltitude);
            pe.text = HudStyle.Distance(tr.PeriapsisAltitude);
            pe.style.color = tr.PeriapsisAltitude < 0 ? HudStyle.Bad : tr.PeriapsisAltitude < 140_000 && tr.Reference.Definition.Atmosphere != null ? HudStyle.Warn : HudStyle.Text;
            timeAp.text = o.IsHyperbolic ? "—" : HudStyle.Duration(o.TimeToTrueAnomaly(Math.PI, sim.Time));
            timePe.text = HudStyle.Duration(o.TimeToTrueAnomaly(0, sim.Time));
            inc.text = $"{tr.EquatorialInclination * Constants.Rad2Deg:F3}°";
            ecc.text = $"{o.eccentricity:F5}";
            period.text = o.IsHyperbolic ? "—" : HudStyle.Duration(o.Period);

            var p = v.Propulsion;
            throttle.text = $"{v.Throttle * 100:F0}%";
            throttleFill.style.width = Length.Percent(v.Throttle * 100);
            prop.text = $"{p.Propellant:N1} / {p.PropellantCapacity:N0} kg";
            propFill.style.width = Length.Percent((float)(p.Propellant / p.PropellantCapacity * 100));
            thrust.text = $"{v.CurrentThrust:N0} N";
            twr.text = $"{p.MaxThrust(0) * v.Throttle / p.Mass:F3} m/s²";
            dv.text = HudStyle.Speed(p.DeltaV());
            burn.text = HudStyle.Duration(p.BurnTimeAtFullThrottle);
            sas.text = v.Sas.ToString();
            sas.style.color = v.Sas == SasMode.Off ? HudStyle.Dim : HudStyle.Accent;

            statusLabel.text = v.Crashed ? "VESSEL LOST · IMPACT" : (sim.WarpIndex == 0 && v.Throttle > 0 && p.Propellant <= 0) ? "PROPELLANT DEPLETED" : "";
        }
    }
}
