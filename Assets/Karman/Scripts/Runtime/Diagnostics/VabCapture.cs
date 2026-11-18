using System.Collections;
using System.IO;
using System.Linq;
using Karman.Simulation;
using Karman.VAB;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Karman.Diagnostics
{
    /// <summary>Automated VAB verification: sample craft, add strap-on boosters with 2× symmetry, save/reload, screenshots.</summary>
    public sealed class VabCapture : MonoBehaviour
    {
        public VabController vab;

        IEnumerator Start()
        {
            string dir = CommandLine.Value("-karmanCapture");
            if (string.IsNullOrEmpty(dir)) yield break;
            var cam = FindAnyObjectByType<VabCamera>();
            var log = new System.Text.StringBuilder();
            for (int i = 0; i < 60; i++) yield return null;
            yield return Shot(dir, "20_vab_sample", cam, 250, 6, log);

            // Strap two GS-63 boosters to the lower tank with radial separators (2× symmetry).
            var lower = vab.Craft.Parts.First(p => p.PartId == "tank.kerolox.37x32");
            float y = lower.Position[1] - 4f;
            vab.AttachRadial(vab.Lookup("decoupler.radial"), lower.Id, 0, y, 2);
            var sep = vab.Craft.Parts.Last(p => p.PartId == "decoupler.radial");
            vab.AttachRadial(vab.Lookup("srb.gem"), sep.Id, 0, y, 2);
            vab.Rename("Kármán I Heavy");
            yield return Shot(dir, "21_vab_boosters", cam, 285, 4, log);

            string path = vab.Save();
            double dvBefore = vab.Vacuum.TotalDeltaV; int partsBefore = vab.Craft.Parts.Count;
            vab.NewCraft();
            vab.Load(path);
            log.AppendLine($"save/load: {path} parts {partsBefore}->{vab.Craft.Parts.Count} dv {dvBefore:F3}->{vab.Vacuum.TotalDeltaV:F3}");
            vab.Undo(); vab.Redo();
            log.AppendLine($"undo/redo ok: parts {vab.Craft.Parts.Count}");
            foreach (var s in vab.Vacuum.Stages) log.AppendLine($"stage {s.Stage}: dv {s.DeltaV:F0} m/s, burn {s.BurnTime:F0} s, TWR {s.StartTwr:F2}, engines {s.EngineCount}, jettison {s.Jettisons}");
            log.AppendLine($"sea-level liftoff TWR {vab.SeaLevel.Stages[0].StartTwr:F2}, total dv vac {vab.Vacuum.TotalDeltaV:F0} / ASL {vab.SeaLevel.TotalDeltaV:F0}");
            vab.Select(vab.Craft.Parts.First(p => p.PartId == "srb.gem").Id);
            yield return Shot(dir, "22_vab_selected_srb", cam, 310, 14, log);
            File.WriteAllText(Path.Combine(dir, "vab_report.txt"), log.ToString());
            Karman.Vehicles.LaunchRequest.Craft = vab.Craft.DeepClone();
            SceneManager.LoadScene("Flight");
        }

        IEnumerator Shot(string dir, string name, VabCamera cam, float yaw, float pitch, System.Text.StringBuilder log)
        {
            cam.yaw = yaw; cam.pitch = pitch; cam.Frame(); cam.Snap();
            for (int i = 0; i < 30; i++) yield return null;
            float t0 = Time.realtimeSinceStartup; int frames = 0;
            while (Time.realtimeSinceStartup - t0 < 1.5f) { frames++; yield return null; }
            float ms = (Time.realtimeSinceStartup - t0) * 1000 / frames;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            log.AppendLine($"{name}: {ms:F2} ms/frame");
            yield return null; yield return null;
        }
    }
}
