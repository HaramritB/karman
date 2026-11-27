using System;
using System.Linq;
using Karman.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Karman.Diagnostics
{
    /// <summary>
    /// Performance attribution for the automated runs: <c>-karmanDisable fog,volumetric,ssr,ao,contact,bloom,clouds,
    /// spotshadows,dirshadows,decals</c> switches the named features off after each scene loads, so the frame-time
    /// cost of each can be measured in the built player instead of guessed. Does nothing without the flag.
    /// </summary>
    public static class PerfToggles
    {
        static string[] disabled = Array.Empty<string>();
        public static string Summary => disabled.Length == 0 ? "none" : string.Join(",", disabled);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            string arg = CommandLine.Value("-karmanDisable");
            if (string.IsNullOrEmpty(arg)) return;
            disabled = arg.Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToArray();
            SceneManager.sceneLoaded += (_, __) => Apply();
            Apply();
        }

        /// <summary>True when the named feature was switched off on the command line.</summary>
        public static bool Off(string name) => disabled.Length > 0 && Array.IndexOf(disabled, name) >= 0;

        static void Apply()
        {
            foreach (var v in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
            {
                var p = v.profile;   // per-volume runtime copy; the asset on disk is untouched
                if (p == null) continue;
                if (p.TryGet(out Fog fog))
                {
                    if (Off("fog")) fog.enabled.Override(false);
                    if (Off("volumetric")) fog.enableVolumetricFog.Override(false);
                }
                if (Off("ssr") && p.TryGet(out ScreenSpaceReflection ssr)) ssr.enabled.Override(false);
                if (Off("ao") && p.TryGet(out ScreenSpaceAmbientOcclusion ao)) ao.intensity.Override(0f);
                if (Off("contact") && p.TryGet(out ContactShadows cs)) cs.enable.Override(false);
                if (Off("bloom") && p.TryGet(out Bloom bloom)) bloom.intensity.Override(0f);
                if (Off("clouds") && p.TryGet(out VolumetricClouds clouds)) clouds.enable.Override(false);
            }
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (Off("spotshadows") && l.type == LightType.Spot) l.shadows = LightShadows.None;
                if (Off("dirshadows") && l.type == LightType.Directional) l.shadows = LightShadows.None;
            }
            if (Off("decals"))
                foreach (var d in UnityEngine.Object.FindObjectsByType<DecalProjector>(FindObjectsInactive.Include)) d.enabled = false;
            Debug.Log($"[PerfToggles] disabled: {Summary}");
        }
    }
}
