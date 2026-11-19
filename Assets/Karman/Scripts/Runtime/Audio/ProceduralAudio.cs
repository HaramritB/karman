using UnityEngine;

namespace Karman.Audio
{
    /// <summary>
    /// Original procedurally synthesised sounds (no third-party samples): rocket roar, wind, separation thump,
    /// touchdown thud, UI click and an ambient menu drone. Generated once at start-up.
    /// </summary>
    public static class ProceduralAudio
    {
        const int Rate = 44100;
        static AudioClip engine, wind, thump, thud, click, drone;
        public static AudioClip Engine => engine ??= MakeLoop("KarmanEngine", 4f, EngineSample);
        public static AudioClip Wind => wind ??= MakeLoop("KarmanWind", 4f, WindSample);
        public static AudioClip Thump => thump ??= MakeOneShot("KarmanThump", 1.2f, ThumpSample);
        public static AudioClip Thud => thud ??= MakeOneShot("KarmanThud", 0.8f, ThudSample);
        public static AudioClip Click => click ??= MakeOneShot("KarmanClick", 0.06f, (t, r) => Mathf.Sin(2 * Mathf.PI * 2100 * t) * Mathf.Exp(-t * 90) * 0.35f);
        public static AudioClip Drone => drone ??= MakeLoop("KarmanDrone", 12f, DroneSample);

        delegate float Sampler(float t, System.Random rng);

        static AudioClip MakeLoop(string name, float seconds, Sampler f) => Make(name, seconds, f, true);
        static AudioClip MakeOneShot(string name, float seconds, Sampler f) => Make(name, seconds, f, false);

        static AudioClip Make(string name, float seconds, Sampler f, bool loop)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            for (int i = 0; i < n; i++) data[i] = f(i / (float)Rate, rng);
            if (loop)
            {
                // Crossfade the ends so the loop is seamless.
                int fade = Rate / 4;
                for (int i = 0; i < fade; i++)
                {
                    float w = i / (float)fade;
                    data[i] = data[i] * w + data[n - fade + i] * (1 - w);
                }
                System.Array.Resize(ref data, n - fade);
                n -= fade;
            }
            float peak = 1e-6f; foreach (var s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
            for (int i = 0; i < n; i++) data[i] = data[i] / peak * 0.9f;
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Rocket roar: brown noise (deep rumble) + band-limited crackle bursts.
        static float brown, crackleEnv, lp1, lp2;
        static float EngineSample(float t, System.Random r)
        {
            float white = (float)r.NextDouble() * 2 - 1;
            brown = Mathf.Clamp(brown + white * 0.02f, -1, 1) * 0.998f;
            if (r.NextDouble() < 0.0015) crackleEnv = 1;
            crackleEnv *= 0.9985f;
            lp1 += (white - lp1) * 0.18f;
            float crackle = lp1 * crackleEnv;
            float rumble = Mathf.Sin(2 * Mathf.PI * 38 * t) * 0.15f + Mathf.Sin(2 * Mathf.PI * 57 * t + 1.3f) * 0.08f;
            return brown * 0.9f + crackle * 0.5f + rumble;
        }

        static float WindSample(float t, System.Random r)
        {
            float white = (float)r.NextDouble() * 2 - 1;
            lp2 += (white - lp2) * 0.06f;
            float gust = 0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 0.25f * t) * Mathf.Sin(2 * Mathf.PI * 0.11f * t + 2);
            return lp2 * gust;
        }

        static float ThumpSample(float t, System.Random r)
        {
            float white = (float)r.NextDouble() * 2 - 1;
            return (Mathf.Sin(2 * Mathf.PI * (60 - 30 * t) * t) * 0.9f + white * 0.4f * Mathf.Exp(-t * 25)) * Mathf.Exp(-t * 4);
        }

        static float ThudSample(float t, System.Random r)
        {
            float white = (float)r.NextDouble() * 2 - 1;
            return (Mathf.Sin(2 * Mathf.PI * 45 * t) + white * 0.25f) * Mathf.Exp(-t * 7);
        }

        // Ambient drone: slowly beating stacked fifths with a soft shimmer.
        static float DroneSample(float t, System.Random r)
        {
            float lfo = 0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * t / 12f);
            float s = Mathf.Sin(2 * Mathf.PI * 55f * t) * 0.5f + Mathf.Sin(2 * Mathf.PI * 82.4f * t) * 0.35f + Mathf.Sin(2 * Mathf.PI * 110.3f * t) * 0.25f * lfo
                    + Mathf.Sin(2 * Mathf.PI * 164.8f * t + Mathf.Sin(t * 0.7f)) * 0.12f * (1 - lfo);
            return s;
        }
    }
}
