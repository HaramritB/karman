using System.IO;
using System.Linq;
using Karman.Core.Vehicles;
using Karman.Persistence;
using Karman.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Karman.VAB
{
    /// <summary>VAB interface: part catalogue with search, engineering readouts, staging editor, file commands.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class VabHud : MonoBehaviour
    {
        public VabController vab;
        VisualElement root, catalog, readouts, staging, loadPanel, leftPanel, rightPanel, topBar;
        TextField nameField, search;
        Label symLabel, status, heldLabel;
        PartCategory? filter;

        void Start()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            Build();
            vab.Changed += Refresh;
            vab.IsPointerOverUi = OverUi;
        }

        void OnDisable() { if (vab) vab.Changed -= Refresh; }

        bool OverUi(Vector2 screen)
        {
            var panel = root?.panel;
            if (panel == null) return false;
            Vector2 p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            var picked = panel.Pick(p);
            return picked != null && picked != root;
        }

        static Button Btn(string text, System.Action a, float w = 0)
        {
            var b = new Button(a) { text = text };
            b.clicked += Karman.Audio.UiSound.Click;
            b.style.backgroundColor = new Color(1, 1, 1, 0.06f);
            b.style.color = HudStyle.Text; b.style.fontSize = 12;
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 1;
            var bc = new Color(1, 1, 1, 0.12f);
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = bc;
            b.style.borderTopLeftRadius = b.style.borderTopRightRadius = b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 6;
            b.style.paddingLeft = 10; b.style.paddingRight = 10; b.style.paddingTop = 5; b.style.paddingBottom = 5;
            b.style.marginLeft = 3; b.style.marginRight = 3;
            if (w > 0) b.style.width = w;
            return b;
        }

        void Build()
        {
            root.Clear();
            // Top bar
            topBar = HudStyle.PanelBox(1180);
            topBar.style.position = Position.Absolute; topBar.style.top = 14; topBar.style.left = Length.Percent(50); topBar.style.translate = new Translate(Length.Percent(-50), 0);
            topBar.style.flexDirection = FlexDirection.Row; topBar.style.alignItems = Align.Center; topBar.style.paddingTop = 6; topBar.style.paddingBottom = 6;
            var brand = new Label("KÁRMÁN  ·  VEHICLE ASSEMBLY"); brand.style.color = HudStyle.Text; brand.style.fontSize = 13; brand.style.letterSpacing = 3; brand.style.unityFontStyleAndWeight = FontStyle.Bold; brand.style.marginRight = 14;
            topBar.Add(brand);
            nameField = new TextField { value = vab.Craft.Name }; nameField.style.width = 260;
            nameField.RegisterValueChangedCallback(e => vab.Rename(e.newValue));
            topBar.Add(nameField);
            topBar.Add(Btn("New", vab.NewCraft));
            topBar.Add(Btn("Sample", vab.LoadSample));
            topBar.Add(Btn("Save", () => vab.Save()));
            topBar.Add(Btn("Load", ToggleLoad));
            topBar.Add(Btn("Undo", vab.Undo));
            topBar.Add(Btn("Redo", vab.Redo));
            symLabel = new Label(); symLabel.style.color = HudStyle.Accent; symLabel.style.marginLeft = 6; symLabel.style.marginRight = 2;
            topBar.Add(Btn("Symmetry", vab.CycleSymmetry)); topBar.Add(symLabel);
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; topBar.Add(spacer);
            topBar.Add(Btn("Moon rocket", () => vab.LoadLunarSample()));
            var launch = Btn("LAUNCH ▸", vab.Launch);
            launch.style.backgroundColor = new Color(0.36f, 0.78f, 1f, 0.85f); launch.style.color = new Color(0.02f, 0.05f, 0.08f);
            launch.style.unityFontStyleAndWeight = FontStyle.Bold;
            topBar.Add(launch);
            root.Add(topBar);

            // Left: catalogue
            leftPanel = HudStyle.PanelBox(300);
            leftPanel.style.position = Position.Absolute; leftPanel.style.left = 18; leftPanel.style.top = 74; leftPanel.style.bottom = 18;
            leftPanel.Add(HudStyle.Title("PARTS"));
            search = new TextField(); search.RegisterValueChangedCallback(_ => FillCatalog());
            leftPanel.Add(search);
            var cats = new VisualElement(); cats.style.flexDirection = FlexDirection.Row; cats.style.flexWrap = Wrap.Wrap; cats.style.marginTop = 6; cats.style.marginBottom = 6;
            cats.Add(Btn("All", () => { filter = null; FillCatalog(); }));
            foreach (PartCategory c in System.Enum.GetValues(typeof(PartCategory)))
            {
                var cc = c;
                if (vab.Definitions.Any(d => d.Category == cc)) cats.Add(Btn(c.ToString(), () => { filter = cc; FillCatalog(); }));
            }
            leftPanel.Add(cats);
            var scroll = new ScrollView(); scroll.style.flexGrow = 1;
            catalog = scroll.contentContainer;
            leftPanel.Add(scroll);
            heldLabel = new Label(); heldLabel.style.color = HudStyle.Warn; heldLabel.style.fontSize = 12; heldLabel.style.whiteSpace = WhiteSpace.Normal;
            leftPanel.Add(heldLabel);
            root.Add(leftPanel);
            FillCatalog();

            // Right: readouts + staging
            rightPanel = HudStyle.PanelBox(330);
            rightPanel.style.position = Position.Absolute; rightPanel.style.right = 18; rightPanel.style.top = 74; rightPanel.style.bottom = 18;
            rightPanel.Add(HudStyle.Title("ENGINEERING"));
            readouts = new VisualElement(); rightPanel.Add(readouts);
            var st = HudStyle.Title("STAGING  ·  fires top to bottom"); st.style.marginTop = 12; rightPanel.Add(st);
            var stageBtns = new VisualElement(); stageBtns.style.flexDirection = FlexDirection.Row;
            stageBtns.Add(Btn("▲ earlier", () => vab.ShiftStage(1))); stageBtns.Add(Btn("▼ later", () => vab.ShiftStage(-1))); stageBtns.Add(Btn("Auto", vab.ResetStaging));
            rightPanel.Add(stageBtns);
            var sscroll = new ScrollView(); sscroll.style.flexGrow = 1; staging = sscroll.contentContainer;
            rightPanel.Add(sscroll);
            root.Add(rightPanel);

            status = new Label(); status.style.position = Position.Absolute; status.style.bottom = 22; status.style.left = 340; status.style.right = 370;
            status.style.unityTextAlign = TextAnchor.MiddleCenter; status.style.color = HudStyle.Dim; status.style.fontSize = 12;
            root.Add(status);

            loadPanel = HudStyle.PanelBox(420);
            loadPanel.style.position = Position.Absolute; loadPanel.style.top = 74; loadPanel.style.left = Length.Percent(50); loadPanel.style.translate = new Translate(Length.Percent(-50), 0);
            loadPanel.style.display = DisplayStyle.None;
            root.Add(loadPanel);
            Refresh();
        }

        void FillCatalog()
        {
            catalog.Clear();
            string q = search?.value?.Trim().ToLowerInvariant() ?? "";
            foreach (var d in vab.Definitions)
            {
                if (filter.HasValue && d.Category != filter.Value) continue;
                if (q.Length > 0 && !(d.Title.ToLowerInvariant().Contains(q) || (d.Reference ?? "").ToLowerInvariant().Contains(q))) continue;
                var card = new Button(() => vab.PickUp(d));
                card.style.flexDirection = FlexDirection.Column; card.style.alignItems = Align.FlexStart;
                card.style.backgroundColor = new Color(1, 1, 1, 0.04f);
                card.style.borderTopLeftRadius = card.style.borderTopRightRadius = card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 6;
                card.style.borderTopWidth = card.style.borderBottomWidth = card.style.borderLeftWidth = card.style.borderRightWidth = 0;
                card.style.marginBottom = 5; card.style.paddingTop = 6; card.style.paddingBottom = 6; card.style.paddingLeft = 8;
                var t = new Label(d.Title); t.style.color = HudStyle.Text; t.style.fontSize = 13; t.style.unityFontStyleAndWeight = FontStyle.Bold;
                string thrust = d.Engine == null ? "" : d.Engine.VacuumThrust < 10_000 ? $"{d.Engine.VacuumThrust:N0} N" : $"{d.Engine.VacuumThrust / 1000:N0} kN";
                string spec = d.Engine != null ? $"{thrust} vac · Isp {d.Engine.SeaLevelIsp:F0}/{d.Engine.VacuumIsp:F0} s · {d.DryMass:N0} kg"
                    : d.Resources.Count > 0 ? $"{d.Resources.Sum(r => r.Mass) / 1000:N1} t propellant · {d.DryMass:N0} kg dry"
                    : $"{d.DryMass:N0} kg · Ø {d.Diameter:F2} m";
                var s = new Label(spec); s.style.color = HudStyle.Dim; s.style.fontSize = 11;
                var r = new Label(d.Reference); r.style.color = new Color(0.5f, 0.56f, 0.64f); r.style.fontSize = 10; r.style.whiteSpace = WhiteSpace.Normal;
                foreach (var lbl in new[] { t, s, r }) { lbl.style.unityTextAlign = TextAnchor.UpperLeft; lbl.style.whiteSpace = WhiteSpace.Normal; lbl.style.alignSelf = Align.Stretch; }
                card.Add(t); card.Add(s); card.Add(r);
                catalog.Add(card);
            }
        }

        void ToggleLoad()
        {
            if (loadPanel.style.display == DisplayStyle.Flex) { loadPanel.style.display = DisplayStyle.None; return; }
            loadPanel.Clear();
            loadPanel.Add(HudStyle.Title("LOAD CRAFT"));
            var files = Directory.GetFiles(CraftSerializer.CraftDirectory, "*.craft.json").OrderByDescending(File.GetLastWriteTime).ToList();
            if (files.Count == 0) { var l = new Label("No saved craft yet."); l.style.color = HudStyle.Dim; loadPanel.Add(l); }
            foreach (var f in files)
            {
                string path = f;
                loadPanel.Add(Btn(Path.GetFileName(f).Replace(".craft.json", ""), () => { vab.Load(path); loadPanel.style.display = DisplayStyle.None; }));
            }
            loadPanel.Add(Btn("Close", () => loadPanel.style.display = DisplayStyle.None));
            loadPanel.style.display = DisplayStyle.Flex;
        }

        void Refresh()
        {
            if (readouts == null) return;
            if (nameField.value != vab.Craft.Name) nameField.SetValueWithoutNotify(vab.Craft.Name);
            symLabel.text = $"×{vab.Symmetry}";
            heldLabel.text = vab.Held != null ? $"Placing: {vab.Held.Title}\nClick a green node or a surface · R rotate · right-click cancel · Shift-click repeat" : "Click a part to pick it up. Select a placed part and press Delete to remove it.";
            status.text = vab.Status;

            readouts.Clear();
            var v = vab.Vacuum; var sl = vab.SeaLevel;
            void Row(string k, string val) { HudStyle.Row(readouts, k).text = val; }
            Row("Parts", vab.Craft.Parts.Count.ToString());
            Row("Total mass", $"{v.TotalMass / 1000:N2} t");
            Row("Dry mass", $"{v.DryMass / 1000:N2} t");
            Row("Propellant", $"{v.PropellantMass / 1000:N2} t");
            Row("Δv vacuum / sea level", $"{v.TotalDeltaV:N0} / {sl.TotalDeltaV:N0} m/s");
            if (sl.Stages.Count > 0) Row("Lift-off TWR", $"{sl.Stages[0].StartTwr:F2}");
            Row("CoM height", $"{v.CenterOfMass[1] - (vab.platformTop.y - vab.craftRoot.position.y):F1} m");

            staging.Clear();
            for (int i = 0; i < v.Stages.Count; i++)
            {
                var s = v.Stages[i]; var s2 = i < sl.Stages.Count ? sl.Stages[i] : null;
                var box = new VisualElement();
                box.style.backgroundColor = new Color(1, 1, 1, 0.04f);
                box.style.borderTopLeftRadius = box.style.borderTopRightRadius = box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 6;
                box.style.marginTop = 6; box.style.paddingLeft = 8; box.style.paddingRight = 8; box.style.paddingTop = 5; box.style.paddingBottom = 6;
                var head = new Label($"STAGE {s.Stage}   Δv {s.DeltaV:N0} m/s{(s2 != null ? $" ({s2.DeltaV:N0} ASL)" : "")}");
                head.style.color = HudStyle.Accent; head.style.fontSize = 12; head.style.unityFontStyleAndWeight = FontStyle.Bold;
                box.Add(head);
                var info = new Label(s.EngineCount > 0 ? $"TWR {s.StartTwr:F2} vac{(s2 != null ? $" / {s2.StartTwr:F2} ASL" : "")} · burn {s.BurnTime:N0} s · Isp {s.Isp:F0} s" : (s.Jettisons ? "separation only" : "no engines"));
                info.style.color = HudStyle.Dim; info.style.fontSize = 11; box.Add(info);
                var chips = new VisualElement(); chips.style.flexDirection = FlexDirection.Row; chips.style.flexWrap = Wrap.Wrap; chips.style.marginTop = 3;
                var seenGroups = new System.Collections.Generic.HashSet<int>();
                foreach (var p in vab.Craft.Parts.Where(p => p.Stage == s.Stage))
                {
                    if (p.SymmetryGroup >= 0 && !seenGroups.Add(p.SymmetryGroup)) continue;
                    int count = p.SymmetryGroup >= 0 ? vab.Craft.Parts.Count(q => q.SymmetryGroup == p.SymmetryGroup) : 1;
                    var d = vab.Lookup(p.PartId);
                    int id = p.Id;
                    var chip = Btn((count > 1 ? $"{count}× " : "") + d.Title, () => vab.Select(id));
                    chip.style.fontSize = 11; chip.style.marginTop = 2;
                    if (vab.Selected == id) chip.style.backgroundColor = new Color(0.36f, 0.78f, 1f, 0.35f);
                    chips.Add(chip);
                }
                box.Add(chips);
                staging.Add(box);
            }
        }
    }
}
