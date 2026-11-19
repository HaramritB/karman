using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Karman.Settings
{
    public enum QualityPreset { Low, Medium, High, Ultra }

    /// <summary>
    /// Player graphics/audio settings with four presets tuned for Apple silicon (High = 1080p60 target on an M3 Pro,
    /// Ultra adds resolution/shadow/terrain headroom for 1440p). Persisted in PlayerPrefs and applied at start-up.
    /// </summary>
    public static class QualitySettingsStore
    {
        public static QualityPreset Preset { get; private set; } = QualityPreset.High;
        public static float MasterVolume { get; private set; } = 0.8f;
        public static float TerrainSplitFactor { get; private set; } = 1.8f;
        public static bool Use1440p { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            Preset = (QualityPreset)PlayerPrefs.GetInt("karman.quality", (int)QualityPreset.High);
            MasterVolume = PlayerPrefs.GetFloat("karman.volume", 0.8f);
            Use1440p = PlayerPrefs.GetInt("karman.1440p", 0) == 1;
            Apply(Preset, false);
        }

        public static void SetVolume(float v) { MasterVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat("karman.volume", MasterVolume); }

        public static void SetResolution1440(bool on)
        {
            Use1440p = on; PlayerPrefs.SetInt("karman.1440p", on ? 1 : 0);
            if (!Application.isEditor) Screen.SetResolution(on ? 2560 : 1920, on ? 1440 : 1080, Screen.fullScreenMode);
        }

        public static void Apply(QualityPreset preset, bool save = true)
        {
            Preset = preset;
            if (save) PlayerPrefs.SetInt("karman.quality", (int)preset);
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float renderScale = 1, shadow = 250, atmoSteps = 32; int msaa = 4;
            switch (preset)
            {
                case QualityPreset.Low: renderScale = 0.75f; msaa = 1; shadow = 80; atmoSteps = 12; TerrainSplitFactor = 1.3f; break;
                case QualityPreset.Medium: renderScale = 0.85f; msaa = 2; shadow = 150; atmoSteps = 20; TerrainSplitFactor = 1.55f; break;
                case QualityPreset.High: renderScale = 1f; msaa = 4; shadow = 250; atmoSteps = 32; TerrainSplitFactor = 1.8f; break;
                case QualityPreset.Ultra: renderScale = 1f; msaa = 4; shadow = 400; atmoSteps = 48; TerrainSplitFactor = 2.3f; break;
            }
            if (urp != null) { urp.renderScale = renderScale; urp.msaaSampleCount = msaa; urp.shadowDistance = shadow; }
            Shader.SetGlobalFloat("_KarmanAtmoSteps", atmoSteps);
            foreach (var q in Object.FindObjectsByType<Karman.Rendering.TerrainQuadtree>(FindObjectsSortMode.None)) q.splitFactor = TerrainSplitFactor;
        }
    }
}
