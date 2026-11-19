using System.IO;
using System.Linq;
using Karman.Core.Vehicles;
using Karman.Persistence;
using Karman.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Karman.VAB
{
    /// <summary>
    /// VAB interface. A slim top bar (file, edit, symmetry, templates, launch), a compact parts list with category
    /// pills and search (full specs appear in a detail strip on hover), and an engineering column with three key
    /// figures and a collapsible staging editor. The vehicle stays the centre of the screen.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class VabHud : MonoBehaviour
    {
        public VabController vab;
        VisualElement root, catalog, readouts, staging, stagingBody, loadPanel, templates, leftPanel, rightPanel, topBar, cats;
        TextField nameField, search;
        Label symLabel, status, detailTitle, detailSpec, detailRef, stageToggle, dvTile, twrTile, massTile;
        PartCategory? filter;
        bool stagingOpen = true, markers;

        void Start()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            Build();
            vab.Changed += Refresh;
            vab.IsPointerOverUi = OverUi;
            SetMarkers(false);
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

        static Button Btn(string text, System.Action a, bool ghost = true)
        {
            var b = new Button(a) { text = text };
            b.clicked += Karman.Audio.UiSound.Click;
            HudStyle.Weight(b, HudStyle.Medium);
            Color idle = ghost ? new Color(1, 1, 1, 0f) : new Color(1, 1, 1, 0.07f);
            b.style.backgroundColor = idle;
            b.style.color = HudStyle.Text; b.style.fontSize = 12;
            HudStyle.Border(b, ghost ? 0 : 1, new Color(1, 1, 1, 0.1f));
            HudStyle.Radius(b, 8);
            b.style.paddingLeft = 10; b.style.paddingRight = 10; b.style.paddingTop = 6; b.style.paddingBottom = 6;
            b.style.marginLeft = 1; b.style.marginRight = 1;
            b.RegisterCallback<MouseEnterEvent>(_ => b.style.backgroundColor = new Color(1, 1, 1, 0.12f));
            b.RegisterCallback<MouseLeaveEvent>(_ => b.style.backgroundColor = idle);
            return b;
        }

        static VisualElement Divider()
        {
            var d = new VisualElement(); d.style.width = 1; d.style.height = 18; d.style.marginLeft = 8; d.style.marginRight = 8;
            d.style.backgroundColor = new Color(1, 1, 1, 0.12f);
            return d;
        }

        static void StyleField(TextField f, string placeholder)
        {
            var input = f.Q(TextField.textInputUssName);
            if (input != null)
            {
                input.style.backgroundColor = new Color(1, 1, 1, 0.06f);
                HudStyle.Border(input, 1, new Color(1, 1, 1, 0.1f));
                HudStyle.Radius(input, 8);
                input.style.color = HudStyle.Text; input.style.paddingLeft = 10; input.style.paddingTop = 5; input.style.paddingBottom = 5;
                if (HudStyle.Medium != null) input.style.unityFontDefinition = FontDefinition.FromFont(HudStyle.Medium);
            }
            f.style.marginLeft = 0; f.style.marginRight = 0;
            f.textEdition.placeholder = placeholder;
        }

        static string ShortName(PartCategory c) => c switch
        {
            PartCategory.Command => "Pods",
            PartCategory.Propellant => "Tanks",
            PartCategory.Engine => "Engines",
            PartCategory.Coupling => "Coupling",
            PartCategory.Structural => "Structure",
            PartCategory.Aerodynamic => "Aero",
            _ => c.ToString(),
        };

        void Build()
        {
            root.Clear();
            HudStyle.ApplyFont(root);

            // ---- Top bar
            topBar = new VisualElement();
            topBar.style.position = Position.Absolute; topBar.style.top = 16; topBar.style.left = 20; topBar.style.right = 20;
            topBar.style.flexDirection = FlexDirection.Row; topBar.style.alignItems = Align.Center;
            topBar.style.backgroundColor = HudStyle.Panel; HudStyle.Radius(topBar, 14); HudStyle.Pad(topBar, 14, 7);
            HudStyle.Border(topBar, 1, new Color(1, 1, 1, 0.07f));
            var brand = HudStyle.Weight(new Label("KÁRMÁN"), HudStyle.SemiBold); brand.style.color = HudStyle.Text; brand.style.fontSize = 13; brand.style.letterSpacing = 4;
            var sub = HudStyle.Caption("VEHICLE ASSEMBLY", HudStyle.Dim); sub.style.marginLeft = 10;
            topBar.Add(brand); topBar.Add(sub); topBar.Add(Divider());
            nameField = new TextField { value = vab.Craft.Name }; nameField.style.width = 280;
            StyleField(nameField, "Vehicle name");
            nameField.RegisterValueChangedCallback(e => vab.Rename(e.newValue));
            topBar.Add(nameField);
            topBar.Add(Divider());
            topBar.Add(Btn("New", vab.NewCraft));
            topBar.Add(Btn("Open", ToggleLoad));
            topBar.Add(Btn("Save", () => vab.Save()));
            topBar.Add(Divider());
            topBar.Add(Btn("Undo", vab.Undo));
            topBar.Add(Btn("Redo", vab.Redo));
            topBar.Add(Divider());
            var sym = Btn("Symmetry", vab.CycleSymmetry);
            symLabel = HudStyle.Chip("×1", HudStyle.Accent); symLabel.pickingMode = PickingMode.Ignore;
            sym.Add(symLabel); sym.style.flexDirection = FlexDirection.Row; sym.style.alignItems = Align.Center;
            topBar.Add(sym);
            topBar.Add(Btn("CoM / CoT", () => SetMarkers(!markers)));
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; topBar.Add(spacer);
            topBar.Add(Btn("Templates", ToggleTemplates, false));
            var launch = new Button(vab.Launch) { text = "Launch" };
            launch.clicked += Karman.Audio.UiSound.Click;
            HudStyle.Weight(launch, HudStyle.SemiBold);
            launch.style.backgroundColor = HudStyle.Accent; launch.style.color = new Color(0.02f, 0.05f, 0.08f); launch.style.fontSize = 13;
            HudStyle.Border(launch, 0, Color.clear); HudStyle.Radius(launch, 9); HudStyle.Pad(launch, 20, 7); launch.style.marginLeft = 8;
            topBar.Add(launch);
            root.Add(topBar);

            templates = HudStyle.PanelBox(260);
            templates.style.backgroundColor = HudStyle.PanelSolid;
            templates.style.position = Position.Absolute; templates.style.top = 66; templates.style.right = 120;
            templates.Add(HudStyle.Title("START FROM"));
            void Tpl(string t, string d, System.Action a)
            {
                var b = Btn(t, () => { a(); templates.style.display = DisplayStyle.None; });
                b.style.flexDirection = FlexDirection.Column; b.style.alignItems = Align.FlexStart; b.style.marginBottom = 4;
                var dl = new Label(d); dl.style.color = HudStyle.Dim; dl.style.fontSize = 11; dl.style.whiteSpace = WhiteSpace.Normal;
                b.Add(dl); templates.Add(b);
            }
            Tpl("Kármán I", "Two-stage Gemini launcher", vab.LoadSample);
            Tpl("Kármán V", "Heavy lift with lunar lander and transfer stage", () => vab.LoadLunarSample());
            templates.style.display = DisplayStyle.None;
            root.Add(templates);

            // ---- Parts (left)
            leftPanel = HudStyle.PanelBox(272);
            leftPanel.style.position = Position.Absolute; leftPanel.style.left = 20; leftPanel.style.top = 74; leftPanel.style.bottom = 20;
            leftPanel.Add(HudStyle.Title("PARTS"));
            search = new TextField(); StyleField(search, "Search parts");
            search.RegisterValueChangedCallback(_ => FillCatalog());
            leftPanel.Add(search);
            cats = new VisualElement(); cats.style.flexDirection = FlexDirection.Row; cats.style.flexWrap = Wrap.Wrap; cats.style.marginTop = 8; cats.style.marginBottom = 6;
            leftPanel.Add(cats);
            FillCats();
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; HudStyle.SlimScroll(scroll);
            catalog = scroll.contentContainer;
            leftPanel.Add(scroll);
            var detail = new VisualElement(); detail.style.borderTopWidth = 1; detail.style.borderTopColor = HudStyle.Rule; detail.style.paddingTop = 10; detail.style.marginTop = 6;
            detailTitle = HudStyle.Value("", 13);
            detailSpec = new Label(); detailSpec.style.color = HudStyle.Dim; detailSpec.style.fontSize = 11; detailSpec.style.whiteSpace = WhiteSpace.Normal; detailSpec.style.marginTop = 2;
            detailRef = new Label(); detailRef.style.color = HudStyle.Faint; detailRef.style.fontSize = 11; detailRef.style.whiteSpace = WhiteSpace.Normal; detailRef.style.marginTop = 4;
            detail.Add(detailTitle); detail.Add(detailSpec); detail.Add(detailRef);
            leftPanel.Add(detail);
            root.Add(leftPanel);
            FillCatalog();

            // ---- Engineering (right)
            rightPanel = HudStyle.PanelBox(300);
            rightPanel.style.position = Position.Absolute; rightPanel.style.right = 20; rightPanel.style.top = 74; rightPanel.style.bottom = 20;
            rightPanel.Add(HudStyle.Title("ENGINEERING"));
            var tiles = new VisualElement(); tiles.style.flexDirection = FlexDirection.Row; tiles.style.marginBottom = 8;
            VisualElement Tile(string cap, out Label value)
            {
                var t = new VisualElement(); t.style.flexGrow = 1; t.style.flexBasis = 0; t.style.marginRight = 6;
                t.style.backgroundColor = new Color(1, 1, 1, 0.045f); HudStyle.Radius(t, 10); HudStyle.Pad(t, 10, 8);
                var caption = HudStyle.Caption(cap); value = HudStyle.Value("—", 17);
                t.Add(caption); t.Add(value); return t;
            }
            tiles.Add(Tile("Δv VAC", out dvTile));
            tiles.Add(Tile("LIFT-OFF TWR", out twrTile));
            var mt = Tile("MASS", out massTile); mt.style.marginRight = 0; tiles.Add(mt);
            rightPanel.Add(tiles);
            readouts = new VisualElement(); rightPanel.Add(readouts);
            var stHead = new VisualElement(); stHead.style.flexDirection = FlexDirection.Row; stHead.style.alignItems = Align.Center; stHead.style.marginTop = 14;
            stageToggle = HudStyle.Title("STAGING"); stageToggle.style.marginBottom = 0; stageToggle.style.flexGrow = 1;
            stageToggle.RegisterCallback<ClickEvent>(_ => { stagingOpen = !stagingOpen; Refresh(); });
            stHead.Add(stageToggle);
            stHead.Add(Btn("▲", () => vab.ShiftStage(1))); stHead.Add(Btn("▼", () => vab.ShiftStage(-1))); stHead.Add(Btn("Auto", vab.ResetStaging));
            rightPanel.Add(stHead);
            var sscroll = new ScrollView(); sscroll.style.flexGrow = 1; HudStyle.SlimScroll(sscroll); staging = sscroll.contentContainer; stagingBody = sscroll;
            rightPanel.Add(sscroll);
            root.Add(rightPanel);

            // ---- Hint / status strip (bottom centre)
            status = new Label(); status.style.position = Position.Absolute; status.style.bottom = 26; status.style.left = 320; status.style.right = 340;
            status.style.unityTextAlign = TextAnchor.MiddleCenter; status.style.color = HudStyle.Text; status.style.fontSize = 12;
            status.style.whiteSpace = WhiteSpace.Normal;
            HudStyle.Shadow(status); status.pickingMode = PickingMode.Ignore;
            root.Add(status);

            loadPanel = HudStyle.PanelBox(380);
            loadPanel.style.backgroundColor = HudStyle.PanelSolid;
            loadPanel.style.position = Position.Absolute; loadPanel.style.top = 74; loadPanel.style.left = Length.Percent(50); loadPanel.style.translate = new Translate(Length.Percent(-50), 0);
            loadPanel.style.display = DisplayStyle.None;
            root.Add(loadPanel);
            Refresh();
        }

        void FillCats()
        {
            cats.Clear();
            void Pill(string text, PartCategory? c)
            {
                bool on = filter == c;
                var b = Btn(text, () => { filter = c; FillCats(); FillCatalog(); }, !on);
                b.style.fontSize = 11; b.style.paddingTop = 3; b.style.paddingBottom = 3; b.style.paddingLeft = 9; b.style.paddingRight = 9;
                b.style.marginBottom = 4; b.style.marginRight = 3; HudStyle.Radius(b, 11);
                if (on) { b.style.backgroundColor = new Color(HudStyle.Accent.r, HudStyle.Accent.g, HudStyle.Accent.b, 0.22f); b.style.color = HudStyle.Accent; }
                cats.Add(b);
            }
            Pill("All", null);
            foreach (PartCategory c in System.Enum.GetValues(typeof(PartCategory)))
                if (vab.Definitions.Any(d => d.Category == c)) Pill(ShortName(c), c);
        }

        static string Thrust(PartDefinition d) => d.Engine == null ? "" : d.Engine.VacuumThrust < 10_000 ? $"{d.Engine.VacuumThrust:N0} N" : $"{d.Engine.VacuumThrust / 1000:N0} kN";

        static string Spec(PartDefinition d) =>
            d.Engine != null ? $"{Thrust(d)}"
            : d.Resources.Count > 0 ? $"{d.Resources.Sum(r => r.Mass) / 1000:N1} t"
            : $"Ø {d.Diameter:F1} m";

        void ShowDetail(PartDefinition d)
        {
            if (d == null) { detailTitle.text = ""; detailSpec.text = "Hover a part for details, click to pick it up."; detailRef.text = ""; return; }
            detailTitle.text = d.Title;
            detailSpec.text = d.Engine != null ? $"{Thrust(d)} vacuum · Isp {d.Engine.SeaLevelIsp:F0} / {d.Engine.VacuumIsp:F0} s · {d.DryMass:N0} kg · Ø {d.Diameter:F2} m"
                : d.Resources.Count > 0 ? $"{d.Resources.Sum(r => r.Mass) / 1000:N1} t propellant · {d.DryMass:N0} kg dry · Ø {d.Diameter:F2} × {d.Height:F1} m"
                : $"{d.DryMass:N0} kg · Ø {d.Diameter:F2} m";
            detailRef.text = d.Reference;
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
                card.clicked += Karman.Audio.UiSound.Click;
                card.style.flexDirection = FlexDirection.Row; card.style.alignItems = Align.Center; card.style.justifyContent = Justify.SpaceBetween;
                card.style.backgroundColor = new Color(1, 1, 1, 0f);
                HudStyle.Radius(card, 8); HudStyle.Border(card, 0, Color.clear);
                card.style.marginBottom = 1; card.style.paddingTop = 7; card.style.paddingBottom = 7; card.style.paddingLeft = 10; card.style.paddingRight = 10;
                card.style.marginLeft = 0; card.style.marginRight = 0;
                var t = HudStyle.Weight(new Label(d.Title), HudStyle.Medium); t.style.color = HudStyle.Text; t.style.fontSize = 12; t.style.flexShrink = 1;
                t.style.unityTextAlign = TextAnchor.MiddleLeft; t.style.whiteSpace = WhiteSpace.NoWrap; t.style.overflow = Overflow.Hidden; t.style.textOverflow = TextOverflow.Ellipsis;
                var s = new Label(Spec(d)); s.style.color = HudStyle.Faint; s.style.fontSize = 10; s.style.marginLeft = 8; s.style.unityTextAlign = TextAnchor.MiddleRight; s.style.flexShrink = 0;
                card.Add(t); card.Add(s);
                card.RegisterCallback<MouseEnterEvent>(_ => { card.style.backgroundColor = new Color(1, 1, 1, 0.08f); ShowDetail(d); });
                card.RegisterCallback<MouseLeaveEvent>(_ => card.style.backgroundColor = new Color(1, 1, 1, 0f));
                catalog.Add(card);
            }
            ShowDetail(vab.Held);
        }

        void SetMarkers(bool on)
        {
            markers = on;
            foreach (var m in new[] { vab.comMarker, vab.cotMarker })
                if (m) foreach (var r in m.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        }

        void ToggleTemplates() => templates.style.display = templates.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;

        void ToggleLoad()
        {
            if (loadPanel.style.display == DisplayStyle.Flex) { loadPanel.style.display = DisplayStyle.None; return; }
            loadPanel.Clear();
            loadPanel.Add(HudStyle.Title("OPEN VEHICLE"));
            var files = Directory.GetFiles(CraftSerializer.CraftDirectory, "*.craft.json").OrderByDescending(File.GetLastWriteTime).ToList();
            if (files.Count == 0) { var l = new Label("No saved vehicles yet."); l.style.color = HudStyle.Dim; loadPanel.Add(l); }
            foreach (var f in files)
            {
                string path = f;
                var b = Btn(Path.GetFileName(f).Replace(".craft.json", ""), () => { vab.Load(path); loadPanel.style.display = DisplayStyle.None; });
                b.style.unityTextAlign = TextAnchor.MiddleLeft;
                loadPanel.Add(b);
            }
            var close = Btn("Close", () => loadPanel.style.display = DisplayStyle.None, false); close.style.marginTop = 8;
            loadPanel.Add(close);
            loadPanel.style.display = DisplayStyle.Flex;
        }

        void Refresh()
        {
            if (readouts == null) return;
            if (nameField.value != vab.Craft.Name) nameField.SetValueWithoutNotify(vab.Craft.Name);
            symLabel.text = $"×{vab.Symmetry}";
            status.text = !string.IsNullOrEmpty(vab.Status) ? vab.Status
                : vab.Held != null ? $"Placing {vab.Held.Title}  ·  click a green node or a surface  ·  R rotate  ·  right-click cancel  ·  Shift-click repeat"
                : "Pick a part from the list  ·  click a placed part to select it  ·  Delete removes  ·  right-drag orbit, scroll zoom";
            if (vab.Held != null) ShowDetail(vab.Held);

            var v = vab.Vacuum; var sl = vab.SeaLevel;
            dvTile.text = $"{v.TotalDeltaV:N0}";
            float twr = sl.Stages.Count > 0 ? (float)sl.Stages[0].StartTwr : 0;
            twrTile.text = sl.Stages.Count > 0 ? $"{twr:F2}" : "—";
            twrTile.style.color = sl.Stages.Count == 0 ? HudStyle.Text : twr < 1.0f ? HudStyle.Bad : twr < 1.15f ? HudStyle.Warn : HudStyle.Good;
            massTile.text = $"{v.TotalMass / 1000:N0} t";

            readouts.Clear();
            void Row(string k, string val) { HudStyle.Row(readouts, k).text = val; }
            Row("Parts", vab.Craft.Parts.Count.ToString());
            Row("Dry / propellant", $"{v.DryMass / 1000:N1} / {v.PropellantMass / 1000:N1} t");
            Row("Δv at sea level", $"{sl.TotalDeltaV:N0} m/s");
            Row("Centre of mass", $"{v.CenterOfMass[1] - (vab.platformTop.y - vab.craftRoot.position.y):F1} m");

            stageToggle.text = $"STAGING  {(stagingOpen ? "▾" : "▸")}   {v.Stages.Count} STAGES";
            stagingBody.style.display = stagingOpen ? DisplayStyle.Flex : DisplayStyle.None;
            staging.Clear();
            if (!stagingOpen) return;
            for (int i = 0; i < v.Stages.Count; i++)
            {
                var s = v.Stages[i]; var s2 = i < sl.Stages.Count ? sl.Stages[i] : null;
                var box = new VisualElement();
                box.style.backgroundColor = new Color(1, 1, 1, 0.04f); HudStyle.Radius(box, 10);
                box.style.marginTop = 6; HudStyle.Pad(box, 10, 8);
                var head = new VisualElement(); head.style.flexDirection = FlexDirection.Row; head.style.justifyContent = Justify.SpaceBetween;
                head.Add(HudStyle.Value($"Stage {s.Stage}", 12, HudStyle.Accent));
                head.Add(HudStyle.Value(s.EngineCount > 0 ? $"{s.DeltaV:N0} m/s" : "separation", 12, s.EngineCount > 0 ? HudStyle.Text : HudStyle.Dim));
                box.Add(head);
                if (s.EngineCount > 0)
                {
                    var info = new Label($"TWR {s.StartTwr:F2}{(s2 != null && i == 0 ? $" ({s2.StartTwr:F2} ASL)" : "")}  ·  {s.BurnTime:N0} s  ·  Isp {s.Isp:F0} s");
                    info.style.color = HudStyle.Dim; info.style.fontSize = 11; info.style.marginTop = 2; box.Add(info);
                }
                var chips = new VisualElement(); chips.style.flexDirection = FlexDirection.Row; chips.style.flexWrap = Wrap.Wrap; chips.style.marginTop = 4;
                var seenGroups = new System.Collections.Generic.HashSet<int>();
                foreach (var p in vab.Craft.Parts.Where(p => p.Stage == s.Stage))
                {
                    if (p.SymmetryGroup >= 0 && !seenGroups.Add(p.SymmetryGroup)) continue;
                    int count = p.SymmetryGroup >= 0 ? vab.Craft.Parts.Count(q => q.SymmetryGroup == p.SymmetryGroup) : 1;
                    var d = vab.Lookup(p.PartId);
                    int id = p.Id;
                    var chip = Btn((count > 1 ? $"{count}× " : "") + d.Title, () => vab.Select(id), false);
                    chip.style.fontSize = 10; chip.style.marginTop = 2; chip.style.marginRight = 3; HudStyle.Pad(chip, 7, 3); HudStyle.Radius(chip, 9);
                    if (vab.Selected == id) { chip.style.backgroundColor = new Color(0.38f, 0.8f, 1f, 0.28f); chip.style.color = HudStyle.Accent; }
                    chips.Add(chip);
                }
                box.Add(chips);
                staging.Add(box);
            }
        }
    }
}
