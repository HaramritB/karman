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
            col.Add(MenuButton("Orbit Sandbox", "Fly a Gemini spacecraft in low Earth orbit", () => SceneManager.LoadScene("Flight")));
            col.Add(MenuButton("Quit", "", Application.Quit));
            root.Add(col);
            var foot = new Label("Early development build · Earth, Moon and planets at their real positions for the current date");
            foot.style.position = Position.Absolute; foot.style.left = 114; foot.style.bottom = 40; foot.style.color = HudStyle.Dim; foot.style.fontSize = 12;
            root.Add(foot);
        }

        static VisualElement MenuButton(string title, string sub, System.Action a)
        {
            var b = new Button(a);
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
            SceneManager.LoadScene("VAB");
        }

        void Update() { if (orbitCamera) orbitCamera.yaw += driftDegPerSecond * Time.unscaledDeltaTime; }
    }
}
