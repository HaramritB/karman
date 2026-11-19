using UnityEngine;

namespace Karman.Audio
{
    /// <summary>Persistent 2D source for interface feedback and menu ambience.</summary>
    public static class UiSound
    {
        static AudioSource src, music;

        static void Ensure()
        {
            if (src != null) return;
            var go = new GameObject("UI Audio");
            Object.DontDestroyOnLoad(go);
            src = go.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0;
            music = go.AddComponent<AudioSource>(); music.playOnAwake = false; music.loop = true; music.spatialBlend = 0;
        }

        public static void Click() { Ensure(); src.PlayOneShot(ProceduralAudio.Click, 0.6f * Settings.QualitySettingsStore.MasterVolume); }

        public static void PlayAmbient(bool on)
        {
            Ensure();
            music.clip = ProceduralAudio.Drone;
            music.volume = 0.22f * Settings.QualitySettingsStore.MasterVolume;
            if (on && !music.isPlaying) music.Play();
            if (!on && music.isPlaying) music.Stop();
        }
    }
}
