using System;
using System.Collections;
using UnityEngine;

namespace Karman.Diagnostics
{
    /// <summary>Frame-time measurement shared by the automated capture runs.</summary>
    public static class CaptureUtil
    {
        public struct FrameStats
        {
            public float AverageMs, WorstMs; public int Frames;
            public float Fps => AverageMs > 0 ? 1000f / AverageMs : 0;
        }

        /// <summary>
        /// Captures the frame being rendered now and waits until it has been taken. A screenshot is grabbed at the end
        /// of the frame, so changing the camera right after requesting it (in the same frame) captures the new view.
        /// </summary>
        public static IEnumerator Shot(string dir, string name)
        {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
            yield return null;
            yield return null;
        }

        /// <summary>Measures wall-clock frame times for <paramref name="seconds"/> and reports them.</summary>
        public static IEnumerator Measure(float seconds, Action<FrameStats> done)
        {
            yield return null;   // start on a frame boundary so the first sample is a whole frame
            float t0 = Time.realtimeSinceStartup, worst = 0; int frames = 0;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                yield return null;
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                frames++;
            }
            float elapsed = Time.realtimeSinceStartup - t0;
            done(new FrameStats { AverageMs = frames > 0 ? elapsed * 1000f / frames : 0, WorstMs = worst * 1000f, Frames = frames });
        }
    }
}
