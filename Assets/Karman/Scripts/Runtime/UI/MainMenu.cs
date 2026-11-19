using Karman.Cameras;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Karman.UI
{
    /// <summary>Title screen over the live Earth (real date/time lighting). Every button leads to a working scene.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenu : MonoBehaviour
    {
        public OrbitCamera orbitCamera;
        public float driftDegPerSecond = 1.2f;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            HudStyle.ApplyFont(root);
            var col = new VisualElement();
            col.style.position = Position.Absolute; col.style.left = 110; col.style.top = Length.Percent(18);
            var title = HudStyle.Weight(new Label("KÁRMÁN"), HudStyle.SemiBold); title.style.color = HudStyle.Text; title.style.fontSize = 64; title.style.letterSpacing = 18;
            var tag = HudStyle.Weight(new Label("BUILD.  LAUNCH.  EXPLORE."), HudStyle.Medium); tag.style.color = HudStyle.Accent; tag.style.fontSize = 13; tag.style.letterSpacing = 7; tag.style.marginBottom = 36; tag.style.marginLeft = 4;
            col.Add(title); col.Add(tag);
            col.Add(MenuButton("Vehicle Assembly Building", "Design a launcher from real-hardware-based parts", () => SceneManager.LoadScene("VAB")));
            col.Add(MenuButton("Orbit Sandbox", "Fly a Gemini spacecraft in low Earth orbit", () => Start(Karman.Simulation.Scenario.OrbitSandbox)));
            col.Add(MenuButton("Lunar Mission (Earth orbit start)", "Lander stack in a Moon-plane parking orbit: plan the transfer, land, return", () => Start(Karman.Simulation.Scenario.LunarStackLeo)));
            col.Add(MenuButton("Lunar Landing", "Start in a 100 km lunar orbit and land on real LOLA terrain", () => Start(Karman.Simulation.Scenario.LunarOrbitLander)));
            col.Add(MenuButton("Moon Mission from the Pad", "Kármán V heavy launcher with the lunar lander on LC-39A", () =>
            {
                var lib = Karman.Core.Vehicles.PartLibrary.Lookup(Karman.Core.Vehicles.PartLibrary.CreateStarter());
                Karman.Vehicles.LaunchRequest.Craft = Karman.Core.Vehicles.SampleCrafts.HeavyLunar(lib);
                SceneManager.LoadScene("Flight");
            }));
            col.Add(MenuButton("Settings", "Graphics quality, resolution, volume", ToggleSettings));
            col.Add(MenuButton("Quit", "", Application.Quit));
            settings = BuildSettings();
            settings.style.display = DisplayStyle.None;
            root.Add(settings);
            Karman.Audio.UiSound.PlayAmbient(true);
            root.Add(col);
            var foot = new Label("Early development build · Earth, Moon and planets at their real positions for the current date");
            foot.style.position = Position.Absolute; foot.style.left = 114; foot.style.bottom = 28; foot.style.color = HudStyle.Faint; foot.style.fontSize = 11;
            root.Add(foot);
        }

        static void Start(Karman.Simulation.Scenario s) { Karman.Simulation.GameStart.Scenario = s; SceneManager.LoadScene("Flight"); }

        VisualElement settings;
        Label presetLabel;
        void ToggleSettings() => settings.style.display = settings.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;

        VisualElement BuildSettings()
        {
            var p = HudStyle.PanelBox(420);
            p.style.position = Position.Absolute; p.style.right = 110; p.style.top = Length.Percent(28);
            p.Add(HudStyle.Title("SETTINGS"));
            presetLabel = new Label(); presetLabel.style.color = HudStyle.Text; presetLabel.style.marginBottom = 6;
            p.Add(presetLabel);
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            foreach (Karman.Settings.QualityPreset q in System.Enum.GetValues(typeof(Karman.Settings.QualityPreset)))
            {
                var qq = q;
                var b = new Button(() => { Karman.Settings.QualitySettingsStore.Apply(qq); Refresh(); }) { text = q.ToString() };
                b.clicked += Karman.Audio.UiSound.Click;
                row.Add(b);
            }
            p.Add(row);
            var res = new Toggle("2560 × 1440") { value = Karman.Settings.QualitySettingsStore.Use1440p };
            res.RegisterValueChangedCallback(e => Karman.Settings.QualitySettingsStore.SetResolution1440(e.newValue));
            res.style.marginTop = 8; p.Add(res);
            var vol = new Slider("Volume", 0, 1) { value = Karman.Settings.QualitySettingsStore.MasterVolume };
            vol.RegisterValueChangedCallback(e => { Karman.Settings.QualitySettingsStore.SetVolume(e.newValue); Karman.Audio.UiSound.PlayAmbient(true); });
            vol.style.marginTop = 6; p.Add(vol);
            var note = new Label("High targets 60 fps at 1080p on an M3 Pro. Ultra adds shadow range, terrain detail and atmosphere samples for 1440p.");
            note.style.color = HudStyle.Dim; note.style.fontSize = 11; note.style.whiteSpace = WhiteSpace.Normal; note.style.marginTop = 8;
            p.Add(note);
            Refresh();
            return p;
        }

        void Refresh() { if (presetLabel != null) presetLabel.text = $"Quality preset: {Karman.Settings.QualitySettingsStore.Preset}"; }

        static VisualElement MenuButton(string title, string sub, System.Action a)
        {
            var b = new Button(a);
            b.clicked += Karman.Audio.UiSound.Click;
            b.style.flexDirection = FlexDirection.Column; b.style.alignItems = Align.FlexStart;
            b.style.width = 400; b.style.marginBottom = 4; b.style.paddingLeft = 16; b.style.paddingTop = 9; b.style.paddingBottom = 9;
            b.style.marginLeft = 0;
            var idle = new Color(0.03f, 0.04f, 0.055f, 0.45f);
            b.style.backgroundColor = idle;
            HudStyle.Radius(b, 10);
            HudStyle.Border(b, 0, Color.clear);
            b.style.borderLeftWidth = 2; b.style.borderLeftColor = new Color(1, 1, 1, 0.08f);
            b.RegisterCallback<MouseEnterEvent>(_ => { b.style.backgroundColor = new Color(0.06f, 0.09f, 0.12f, 0.8f); b.style.borderLeftColor = HudStyle.Accent; });
            b.RegisterCallback<MouseLeaveEvent>(_ => { b.style.backgroundColor = idle; b.style.borderLeftColor = new Color(1, 1, 1, 0.08f); });
            var t = HudStyle.Weight(new Label(title), HudStyle.Medium); t.style.color = HudStyle.Text; t.style.fontSize = 16;
            b.Add(t);
            if (sub.Length > 0) { var s = new Label(sub); s.style.color = HudStyle.Dim; s.style.fontSize = 11; s.style.marginTop = 1; b.Add(s); }
            return b;
        }

        System.Collections.IEnumerator Start()
        {
            string dir = Karman.Simulation.CommandLine.Value("-karmanCapture");
            if (string.IsNullOrEmpty(dir)) yield break;
            System.IO.Directory.CreateDirectory(dir);
            for (int i = 0; i < 90; i++) yield return null;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "00_main_menu.png"));
            yield return null; yield return null;
            if (Karman.Simulation.CommandLine.Has("-karmanHeavy"))
            {
                var lib = Karman.Core.Vehicles.PartLibrary.Lookup(Karman.Core.Vehicles.PartLibrary.CreateStarter());
                Karman.Vehicles.LaunchRequest.Craft = Karman.Core.Vehicles.SampleCrafts.HeavyLunar(lib);
                SceneManager.LoadScene("Flight");
                yield break;
            }
            SceneManager.LoadScene(Karman.Simulation.CommandLine.Value("-karmanScene") ?? "VAB");
        }

        void Update() { if (orbitCamera) orbitCamera.yaw += driftDegPerSecond * Time.unscaledDeltaTime; }
    }
}
