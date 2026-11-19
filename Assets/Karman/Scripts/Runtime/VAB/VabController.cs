using System;
using System.Collections.Generic;
using System.Linq;
using Karman.Core.Vehicles;
using Karman.Persistence;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Karman.VAB
{
    /// <summary>Marks a part visual so raycasts map back to its craft instance.</summary>
    public sealed class PartTag : MonoBehaviour { public int instanceId; }

    /// <summary>
    /// Vehicle Assembly Building logic: owns the <see cref="Craft"/> being edited, builds part visuals, handles
    /// node snapping, surface attachment with radial symmetry, selection, deletion, undo/redo, staging and files.
    /// All engineering numbers come from <see cref="StagingAnalysis"/> on the same craft data the flight will use.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class VabController : MonoBehaviour
    {
        public Camera cam;
        public Transform craftRoot;
        public Karman.Vehicles.PartModelCatalog catalog;
        public Material ghostOk, ghostBad, selectionMaterial;
        public Transform comMarker, cotMarker;
        public float snapPixels = 48f;
        public Vector3 platformTop = new Vector3(0, 0, 0);

        public List<PartDefinition> Definitions { get; private set; }
        public Func<string, PartDefinition> Lookup { get; private set; }
        public Craft Craft { get; private set; } = new Craft { Name = "Untitled" };
        public CraftAnalysis Vacuum { get; private set; } = new CraftAnalysis();
        public CraftAnalysis SeaLevel { get; private set; } = new CraftAnalysis();
        public PartDefinition Held { get; private set; }
        public int Selected { get; private set; } = -1;
        public int Symmetry { get; private set; } = 1;
        public bool ManualStaging { get; set; }
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public Func<Vector2, bool> IsPointerOverUi = _ => false;
        public event Action Changed;
        public string Status { get; private set; } = "";

        readonly Dictionary<int, GameObject> visuals = new Dictionary<int, GameObject>();
        readonly Stack<string> undo = new Stack<string>(), redo = new Stack<string>();
        GameObject heldVisual;
        Quaternion heldRotation = Quaternion.identity;
        Placement pending;
        static readonly int[] SymmetryLevels = { 1, 2, 3, 4, 6, 8 };

        struct Placement
        {
            public bool valid; public int parentId; public string parentNode, node;
            public Vector3 position; public Quaternion rotation; public bool surface;
        }

        void Awake()
        {
            Definitions = PartLibrary.CreateStarter();
            Lookup = PartLibrary.Lookup(Definitions);
        }

        void Start() => LoadSample();

        // ------------------------------------------------------------------ commands

        public void NewCraft()
        {
            Snapshot();
            Craft = new Craft { Name = "Untitled" };
            Selected = -1; ManualStaging = false;
            Rebuild();
        }

        public void LoadSample()
        {
            var c = new Craft { Name = "Kármán I (Gemini launcher)", Description = "Two-stage kerolox launcher with a Gemini spacecraft." };
            var pod = c.Add(Lookup("pod.gemini"), -1, null, null);
            var adapter = c.Add(Lookup("adapter.37to30"), pod.Id, "bottom", "top");
            var upper = c.Add(Lookup("tank.kerolox.37x8"), adapter.Id, "bottom", "top");
            var mvac = c.Add(Lookup("engine.kr1v"), upper.Id, "bottom", "top");
            var dec = c.Add(Lookup("decoupler.37"), mvac.Id, "bottom", "top");
            var lower = c.Add(Lookup("tank.kerolox.37x32"), dec.Id, "bottom", "top");
            c.Add(Lookup("engine.kr1.cluster9"), lower.Id, "bottom", "top");
            LayoutStack(c);
            undo.Clear(); redo.Clear();
            Craft = c; Selected = -1; ManualStaging = false;
            Rebuild();
        }

        public void LoadLunarSample()
        {
            Snapshot();
            Craft = SampleCrafts.HeavyLunar(Lookup);
            Selected = -1; ManualStaging = false;
            Rebuild();
        }

        /// <summary>Recompute stack-node poses from the tree (used for generated crafts).</summary>
        void LayoutStack(Craft c)
        {
            foreach (var p in c.Parts)
            {
                if (p.ParentId < 0) { p.Position = new float[3]; continue; }
                var parent = c.Get(p.ParentId);
                var pn = Lookup(parent.PartId).Node(p.ParentNode); var cn = Lookup(p.PartId).Node(p.Node);
                if (pn == null || cn == null) continue;
                Vector3 pos = ToV(parent.Position) + ToQ(parent.Rotation) * ToV(pn.Position) - ToV(cn.Position);
                p.Position = FromV(pos);
            }
        }

        public void PickUp(PartDefinition def)
        {
            DropHeld();
            Held = def;
            heldRotation = Quaternion.identity;
            heldVisual = CreateVisual(def, -1);
            SetMaterial(heldVisual, ghostOk);
            foreach (var col in heldVisual.GetComponentsInChildren<Collider>()) col.enabled = false;
            Selected = -1;
            Changed?.Invoke();
        }

        public void DropHeld()
        {
            if (heldVisual) Destroy(heldVisual);
            heldVisual = null; Held = null; pending = default;
        }

        public void CycleSymmetry()
        {
            int i = Array.IndexOf(SymmetryLevels, Symmetry);
            Symmetry = SymmetryLevels[(i + 1) % SymmetryLevels.Length];
            Changed?.Invoke();
        }

        public void Undo()
        {
            if (undo.Count == 0) return;
            redo.Push(CraftSerializer.ToJson(Craft));
            Craft = CraftSerializer.FromJson(undo.Pop());
            Selected = -1; Rebuild();
        }

        public void Redo()
        {
            if (redo.Count == 0) return;
            undo.Push(CraftSerializer.ToJson(Craft));
            Craft = CraftSerializer.FromJson(redo.Pop());
            Selected = -1; Rebuild();
        }

        public void DeleteSelected()
        {
            if (Selected < 0) return;
            Snapshot();
            var p = Craft.Get(Selected);
            var group = p.SymmetryGroup >= 0 ? Craft.Parts.Where(q => q.SymmetryGroup == p.SymmetryGroup).Select(q => q.Id).ToList() : new List<int> { p.Id };
            foreach (var id in group) if (Craft.Get(id) != null) Craft.RemoveSubtree(id);
            Selected = -1;
            Rebuild();
        }

        public void Select(int id) { Selected = id; RefreshSelection(); Changed?.Invoke(); }

        /// <summary>Move the selected part (and its symmetry group) one stage earlier (+1) or later (−1).</summary>
        public void ShiftStage(int delta)
        {
            if (Selected < 0) return;
            var p = Craft.Get(Selected);
            if (p.Stage < 0) return;
            Snapshot();
            int target = Math.Max(0, p.Stage + delta);
            foreach (var q in Craft.Parts.Where(q => q.Id == p.Id || (p.SymmetryGroup >= 0 && q.SymmetryGroup == p.SymmetryGroup))) q.Stage = target;
            // Compact stage numbers.
            var used = Craft.Parts.Where(q => q.Stage >= 0).Select(q => q.Stage).Distinct().OrderBy(v => v).ToList();
            foreach (var q in Craft.Parts) if (q.Stage >= 0) q.Stage = used.IndexOf(q.Stage);
            ManualStaging = true;
            Rebuild();
        }

        public void ResetStaging() { Snapshot(); ManualStaging = false; Rebuild(); }

        public string Save()
        {
            string path = CraftSerializer.Save(Craft);
            Status = "Saved " + System.IO.Path.GetFileName(path);
            Changed?.Invoke();
            return path;
        }

        public void Load(string path)
        {
            try
            {
                var c = CraftSerializer.Load(path);
                Snapshot();
                Craft = c; Selected = -1; ManualStaging = true;
                Status = "Loaded " + c.Name;
                Rebuild();
            }
            catch (Exception e) { Status = "Load failed: " + e.Message; Changed?.Invoke(); }
        }

        /// <summary>Hand the current craft to the flight scene, standing on LC-39A.</summary>
        public void Launch()
        {
            if (Craft.RootId < 0) { Status = "Nothing to launch"; Changed?.Invoke(); return; }
            if (!Craft.Parts.Any(p => Lookup(p.PartId).Engine != null && p.Stage >= 0)) { Status = "No staged engines: add an engine or press Auto in staging"; Changed?.Invoke(); return; }
            Karman.Vehicles.LaunchRequest.Craft = Craft.DeepClone();
            UnityEngine.SceneManagement.SceneManager.LoadScene("Flight");
        }

        public void Rename(string name) { Craft.Name = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim(); Changed?.Invoke(); }

        void Snapshot()
        {
            undo.Push(CraftSerializer.ToJson(Craft));
            redo.Clear();
            if (undo.Count > 200) { var keep = undo.Reverse().Skip(undo.Count - 200).ToList(); undo.Clear(); foreach (var s in keep) undo.Push(s); }
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            bool ctrl = kb.leftCtrlKey.isPressed || kb.leftCommandKey.isPressed || kb.rightCommandKey.isPressed;
            if (ctrl && kb.zKey.wasPressedThisFrame) { if (kb.shiftKey.isPressed) Redo(); else Undo(); }
            if (ctrl && kb.yKey.wasPressedThisFrame) Redo();
            if (ctrl && kb.sKey.wasPressedThisFrame) Save();
            if (!ctrl && kb.xKey.wasPressedThisFrame) CycleSymmetry();
            if ((kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) && Held == null) DeleteSelected();
            if (kb.escapeKey.wasPressedThisFrame) { DropHeld(); Selected = -1; RefreshSelection(); Changed?.Invoke(); }
            if (Held != null && kb.rKey.wasPressedThisFrame) heldRotation = Quaternion.AngleAxis(90, Vector3.up) * heldRotation;

            Vector2 mp = mouse.position.ReadValue();
            bool overUi = IsPointerOverUi(mp);
            if (Held != null)
            {
                UpdateHeld(mp);
                if (mouse.rightButton.wasPressedThisFrame && !overUi) DropHeld();
                else if (mouse.leftButton.wasPressedThisFrame && !overUi && pending.valid) Place();
            }
            else if (mouse.leftButton.wasPressedThisFrame && !overUi)
            {
                var ray = cam.ScreenPointToRay(mp);
                if (Physics.Raycast(ray, out var hit, 2000f) && hit.collider.GetComponentInParent<PartTag>() is PartTag tag)
                {
                    if (kb.altKey.isPressed || mouse.leftButton.isPressed && kb.shiftKey.isPressed) { }
                    Select(tag.instanceId);
                }
                else Select(-1);
            }
        }

        void UpdateHeld(Vector2 mp)
        {
            pending = FindPlacement(mp);
            if (pending.valid)
            {
                heldVisual.transform.SetPositionAndRotation(craftRoot.TransformPoint(pending.position), craftRoot.rotation * pending.rotation);
                SetMaterial(heldVisual, ghostOk);
            }
            else
            {
                var ray = cam.ScreenPointToRay(mp);
                var plane = new Plane(cam.transform.forward * -1, craftRoot.position + Vector3.up * 10);
                if (plane.Raycast(ray, out float t)) heldVisual.transform.SetPositionAndRotation(ray.GetPoint(t), craftRoot.rotation * heldRotation);
                SetMaterial(heldVisual, ghostBad);
            }
        }

        Placement FindPlacement(Vector2 mp)
        {
            var best = new Placement();
            if (Craft.RootId < 0)
            {
                // First part becomes the root, standing on the platform.
                best.valid = true; best.parentId = -1; best.rotation = heldRotation;
                best.position = Vector3.zero;
                return best;
            }
            float bestD = snapPixels;
            foreach (var p in Craft.Parts)
            {
                var def = Lookup(p.PartId);
                Quaternion pr = ToQ(p.Rotation); Vector3 pp = ToV(p.Position);
                foreach (var n in def.Nodes)
                {
                    if (NodeOccupied(p, n.Id)) continue;
                    Vector3 nodeLocal = pp + pr * ToV(n.Position);
                    Vector3 nodeDir = pr * ToV(n.Direction);
                    Vector3 sp = cam.WorldToScreenPoint(craftRoot.TransformPoint(nodeLocal));
                    if (sp.z <= 0) continue;
                    float d = Vector2.Distance(mp, sp);
                    if (d >= bestD) continue;
                    foreach (var hn in Held.Nodes)
                    {
                        Vector3 hd = heldRotation * ToV(hn.Direction);
                        if (Vector3.Dot(hd, nodeDir) > -0.9f) continue;
                        bestD = d;
                        best = new Placement
                        {
                            valid = true, parentId = p.Id, parentNode = n.Id, node = hn.Id, rotation = heldRotation,
                            position = nodeLocal - heldRotation * ToV(hn.Position),
                        };
                    }
                }
            }
            if (best.valid || !Held.AllowSurfaceAttach) return best;

            var ray = cam.ScreenPointToRay(mp);
            if (Physics.Raycast(ray, out var hit, 2000f) && hit.collider.GetComponentInParent<PartTag>() is PartTag tag)
            {
                Vector3 localHit = craftRoot.InverseTransformPoint(hit.point);
                Vector3 n = craftRoot.InverseTransformDirection(hit.normal); n.y = 0;
                if (n.sqrMagnitude < 0.01f) return best;
                n.Normalize();
                var parent = Craft.Get(tag.instanceId);
                var pdef = Lookup(parent.PartId);
                Vector3 axisPoint = ToV(parent.Position);
                Vector3 surface;
                if (pdef.Id == "decoupler.radial")
                {
                    // Strap-ons mount on the separator's outer face, at its height.
                    n = ToQ(parent.Rotation) * Vector3.right;
                    surface = axisPoint + n * 0.36f;
                }
                else
                {
                    // Snap onto the parent's cylinder so symmetric copies line up exactly.
                    surface = new Vector3(axisPoint.x, localHit.y, axisPoint.z) + n * (pdef.Diameter / 2);
                }
                best = new Placement
                {
                    valid = true, surface = true, parentId = parent.Id, parentNode = "", node = "surface",
                    rotation = Quaternion.LookRotation(Vector3.Cross(n, Vector3.up), Vector3.up),
                    position = surface + n * Held.SurfaceAttachOffset,
                };
            }
            return best;
        }

        bool NodeOccupied(PartInstance p, string nodeId)
        {
            if (p.ParentId >= 0 && p.Node == nodeId) return true;
            foreach (var c in Craft.Children(p.Id)) if (c.ParentNode == nodeId) return true;
            return false;
        }

        void Place()
        {
            var keep = Held;
            Commit(Held, pending, Symmetry);
            DropHeld();
            Rebuild();
            if (Keyboard.current != null && Keyboard.current.shiftKey.isPressed) PickUp(keep); // shift-click keeps placing
        }

        /// <summary>Scripted radial attachment (automation/tests): angle around the vertical axis, height in craft space.</summary>
        public void AttachRadial(PartDefinition def, int parentId, float angleDeg, float localY, int symmetry)
        {
            var parent = Craft.Get(parentId); var pdef = Lookup(parent.PartId);
            Vector3 axis = ToV(parent.Position);
            float a = angleDeg * Mathf.Deg2Rad;
            Vector3 n = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            Vector3 surface;
            if (pdef.Id == "decoupler.radial") { n = ToQ(parent.Rotation) * Vector3.right; surface = axis + n * 0.36f; }
            else surface = new Vector3(axis.x, localY, axis.z) + n * (pdef.Diameter / 2);
            var pl = new Placement
            {
                valid = true, surface = true, parentId = parentId, parentNode = "", node = "surface",
                rotation = Quaternion.LookRotation(Vector3.Cross(n, Vector3.up), Vector3.up), position = surface + n * def.SurfaceAttachOffset,
            };
            Commit(def, pl, symmetry);
            Rebuild();
        }

        void Commit(PartDefinition def, Placement pl, int symmetry)
        {
            Snapshot();
            var Held = def; int Symmetry = symmetry;
            var first = AddPart(Held, pl.parentId, pl.parentNode, pl.node, pl.position, pl.rotation);
            if (pl.surface && Symmetry > 1)
            {
                int group = Craft.NextId * 1000 + Symmetry;
                first.SymmetryGroup = group;
                var parent = Craft.Get(pl.parentId);
                for (int i = 1; i < Symmetry; i++)
                {
                    var rot = Quaternion.AngleAxis(360f / Symmetry * i, Vector3.up);
                    Vector3 pos = rot * pl.position;
                    int parentId = pl.parentId;
                    if (parent.SymmetryGroup >= 0)
                    {
                        Vector3 target = rot * ToV(parent.Position);
                        parentId = Craft.Parts.Where(q => q.SymmetryGroup == parent.SymmetryGroup)
                            .OrderBy(q => (ToV(q.Position) - target).sqrMagnitude).First().Id;
                    }
                    var copy = AddPart(Held, parentId, "", "surface", pos, rot * pl.rotation);
                    copy.SymmetryGroup = group;
                }
            }
            Status = $"Placed {Held.Title}{(pl.surface && Symmetry > 1 ? $" ×{Symmetry}" : "")}";
        }

        PartInstance AddPart(PartDefinition def, int parentId, string parentNode, string node, Vector3 pos, Quaternion rot)
        {
            var p = Craft.Add(def, parentId, parentNode, node);
            p.Position = FromV(pos);
            p.Rotation = new[] { rot.x, rot.y, rot.z, rot.w };
            return p;
        }

        // ------------------------------------------------------------------ visuals

        public void Rebuild()
        {
            if (!ManualStaging) AutoStaging.Apply(Craft, Lookup);
            foreach (var v in visuals.Values) if (v) Destroy(v);
            visuals.Clear();
            foreach (var p in Craft.Parts)
            {
                var go = CreateVisual(Lookup(p.PartId), p.Id);
                go.transform.localPosition = ToV(p.Position);
                go.transform.localRotation = ToQ(p.Rotation);
                visuals[p.Id] = go;
            }
            // Stand the craft on the launch platform: lowest point at platformTop.
            float minY = 0;
            bool anyPart = false;
            foreach (var p in Craft.Parts)
            {
                var d = Lookup(p.PartId);
                float bottom = p.Position[1] - d.Height / 2;
                minY = anyPart ? Mathf.Min(minY, bottom) : bottom; anyPart = true;
            }
            craftRoot.position = platformTop - Vector3.up * minY;
            Vacuum = StagingAnalysis.Analyse(Craft, Lookup, 0);
            SeaLevel = StagingAnalysis.Analyse(Craft, Lookup, 101_325);
            if (comMarker) { comMarker.gameObject.SetActive(Craft.RootId >= 0); comMarker.localPosition = new Vector3((float)Vacuum.CenterOfMass[0], (float)Vacuum.CenterOfMass[1], (float)Vacuum.CenterOfMass[2]); }
            if (cotMarker) { cotMarker.gameObject.SetActive(Vacuum.Stages.Count > 0 && Vacuum.Stages[0].Thrust > 0); cotMarker.localPosition = new Vector3((float)Vacuum.CenterOfThrust[0], (float)Vacuum.CenterOfThrust[1], (float)Vacuum.CenterOfThrust[2]); }
            RefreshSelection();
            Changed?.Invoke();
        }

        GameObject CreateVisual(PartDefinition def, int instanceId)
        {
            GameObject go = catalog.Create(def, craftRoot);
            if (instanceId >= 0)
            {
                go.AddComponent<PartTag>().instanceId = instanceId;
                var col = go.AddComponent<CapsuleCollider>();
                col.direction = 1; col.radius = def.Diameter / 2; col.height = Mathf.Max(def.Height, def.Diameter);
                col.center = Vector3.zero;
                if (def.Category == PartCategory.Coupling && def.Id == "decoupler.radial") { col.radius = 0.3f; col.center = new Vector3(0.18f, 0, 0); }
            }
            return go;
        }

        readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();

        void RefreshSelection()
        {
            foreach (var kv in visuals)
            {
                bool sel = Selected >= 0 && (kv.Key == Selected || InSameGroup(kv.Key, Selected));
                foreach (var r in kv.Value.GetComponentsInChildren<Renderer>())
                {
                    if (!originalMaterials.ContainsKey(r)) originalMaterials[r] = r.sharedMaterials;
                    if (sel) r.sharedMaterials = originalMaterials[r].Concat(new[] { selectionMaterial }).ToArray();
                    else r.sharedMaterials = originalMaterials[r];
                }
            }
        }

        bool InSameGroup(int a, int b)
        {
            var pa = Craft.Get(a); var pb = Craft.Get(b);
            return pa != null && pb != null && pa.SymmetryGroup >= 0 && pa.SymmetryGroup == pb.SymmetryGroup;
        }

        static void SetMaterial(GameObject go, Material m)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;
            }
        }

        public Bounds CraftBounds()
        {
            var b = new Bounds(craftRoot.position + Vector3.up * 20, Vector3.one * 10);
            bool any = false;
            foreach (var v in visuals.Values)
                foreach (var r in v.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            return b;
        }

        static Vector3 ToV(float[] a) => new Vector3(a[0], a[1], a[2]);
        static float[] FromV(Vector3 v) => new[] { v.x, v.y, v.z };
        static Quaternion ToQ(float[] a) => a != null && a.Length == 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;
    }
}
