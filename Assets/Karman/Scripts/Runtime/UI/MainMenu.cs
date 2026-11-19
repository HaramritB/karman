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
            var col = new VisualElement();
            col.style.position = Position.Absolute; col.style.left = 110; col.style.top = Length.Percent(28);
            var title = new Label("KÁRMÁN"); title.style.color = HudStyle.Text; title.style.fontSize = 76; title.style.letterSpacing = 22; title.style.unityFontStyleAndWeight = FontStyle.Bold;
            var tag = new Label("BUILD.  LAUNCH.  EXPLORE."); tag.style.color = HudStyle.Accent; tag.style.fontSize = 15; tag.style.letterSpacing = 8; tag.style.marginBottom = 46; tag.style.marginLeft = 4;
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
            foot.style.position = Position.Absolute; foot.style.left = 114; foot.style.bottom = 40; foot.style.color = HudStyle.Dim; foot.style.fontSize = 12;
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
            b.style.width = 420; b.style.marginBottom = 10; b.style.paddingLeft = 18; b.style.paddingTop = 12; b.style.paddingBottom = 12;
            b.style.backgroundColor = HudStyle.Panel;
            b.style.borderTopLeftRadius = b.style.borderTopRightRadius = b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 10;
            b.style.borderLeftWidth = 3; b.style.borderLeftColor = HudStyle.Accent;
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderRightWidth = 0;
            var t = new Label(title); t.style.color = HudStyle.Text; t.style.fontSize = 18; t.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.Add(t);
            if (sub.Length > 0) { var s = new Label(sub); s.style.color = HudStyle.Dim; s.style.fontSize = 12; b.Add(s); }
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
