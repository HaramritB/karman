using System;
using System.Collections.Generic;

namespace Karman.Core.Vehicles
{
    /// <summary>One placed part. Pose is relative to the craft root (metres / unit quaternion xyzw).</summary>
    [Serializable]
    public sealed class PartInstance
    {
        public int Id;
        public string PartId;
        public int ParentId = -1;           // -1 = root
        public string ParentNode;           // node on the parent ("" for surface attach)
        public string Node;                 // node on this part
        public float[] Position = new float[3];
        public float[] Rotation = { 0, 0, 0, 1 };
        public int SymmetryGroup = -1;
        public int Stage = -1;              // activation stage (engines/decouplers), -1 = none
        public List<ResourceCapacity> Resources = new List<ResourceCapacity>(); // current amounts

        public PartInstance Clone()
        {
            var c = (PartInstance)MemberwiseClone();
            c.Position = (float[])Position.Clone(); c.Rotation = (float[])Rotation.Clone();
            c.Resources = new List<ResourceCapacity>();
            foreach (var r in Resources) c.Resources.Add(new ResourceCapacity(r.Type, r.Mass));
            return c;
        }
    }

    /// <summary>A vehicle: a tree of parts rooted at <see cref="RootId"/>. Pure data, serialisable.</summary>
    [Serializable]
    public sealed class Craft
    {
        public const int CurrentFormatVersion = 1;
        public int FormatVersion = CurrentFormatVersion;
        public string Name = "Untitled";
        public string Description = "";
        public int RootId = -1;
        public int NextId = 1;
        public List<PartInstance> Parts = new List<PartInstance>();

        public PartInstance Get(int id) => Parts.Find(p => p.Id == id);
        public IEnumerable<PartInstance> Children(int id) { foreach (var p in Parts) if (p.ParentId == id) yield return p; }

        public PartInstance Add(PartDefinition def, int parentId, string parentNode, string node)
        {
            var p = new PartInstance { Id = NextId++, PartId = def.Id, ParentId = parentId, ParentNode = parentNode, Node = node };
            foreach (var r in def.Resources) p.Resources.Add(new ResourceCapacity(r.Type, r.Mass));
            Parts.Add(p);
            if (parentId < 0) RootId = p.Id;
            return p;
        }

        /// <summary>Removes a part and its whole subtree; returns removed parts (for undo).</summary>
        public List<PartInstance> RemoveSubtree(int id)
        {
            var removed = new List<PartInstance>();
            var stack = new Stack<int>(); stack.Push(id);
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                var p = Get(cur); if (p == null) continue;
                foreach (var c in Children(cur)) stack.Push(c.Id);
                removed.Add(p);
            }
            foreach (var p in removed) Parts.Remove(p);
            if (Get(RootId) == null) RootId = -1;
            return removed;
        }

        public IEnumerable<PartInstance> Subtree(int id)
        {
            var stack = new Stack<int>(); stack.Push(id);
            while (stack.Count > 0)
            {
                var p = Get(stack.Pop()); if (p == null) continue;
                yield return p;
                foreach (var c in Children(p.Id)) stack.Push(c.Id);
            }
        }

        public int MaxStage { get { int m = -1; foreach (var p in Parts) m = Math.Max(m, p.Stage); return m; } }

        public Craft DeepClone()
        {
            var c = (Craft)MemberwiseClone();
            c.Parts = new List<PartInstance>();
            foreach (var p in Parts) c.Parts.Add(p.Clone());
            return c;
        }
    }
}
