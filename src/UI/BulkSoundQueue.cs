using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared sound queuing system for bulk painting operations.
    /// Schedules rapid-fire sounds with ease-in-ease-out timing for satisfying audio feedback.
    /// Used by Schedule, Animals, Wildlife, and Assign tab painting.
    /// </summary>
    public static class BulkSoundQueue
    {
        private static readonly List<float> scheduledTimes = new List<float>();
        private static SoundDef scheduledSound;
        private static int nextIndex = 0;
        private static int lastFrame = -1;
        private const float MinDelay = 0.02f;  // 20ms - fastest (middle of sequence)
        private const float MaxDelay = 0.08f;  // 80ms - slowest (start/end of sequence)

        /// <summary>
        /// Schedules N sounds with ease-in-ease-out timing.
        /// First sound plays immediately; subsequent sounds are spaced using a quadratic
        /// easing curve — slow at the start, fast in the middle, slow at the end.
        /// </summary>
        public static void Queue(int count, SoundDef sound)
        {
            scheduledTimes.Clear();
            nextIndex = 0;
            scheduledSound = sound;

            if (count <= 0) return;

            float now = Time.realtimeSinceStartup;
            scheduledTimes.Add(now); // first sound plays immediately

            if (count == 1) return;

            float cumulative = 0f;
            for (int i = 1; i < count; i++)
            {
                float t = (float)i / (count - 1); // 0 to 1
                // Quadratic ease: 1.0 at edges (t=0,1), 0.0 at middle (t=0.5)
                float ease = 4f * (t - 0.5f) * (t - 0.5f);
                float delay = MinDelay + (MaxDelay - MinDelay) * ease;
                cumulative += delay;
                scheduledTimes.Add(now + cumulative);
            }
        }

        /// <summary>
        /// Plays scheduled sounds when their time arrives.
        /// Called every frame from UnifiedKeyboardPatch.
        /// Limited to one sound per frame to avoid audio overlap.
        /// </summary>
        public static void Update()
        {
            if (nextIndex >= scheduledTimes.Count) return;
            if (scheduledSound == null) return;

            int currentFrame = Time.frameCount;
            if (currentFrame == lastFrame) return;

            float now = Time.realtimeSinceStartup;
            if (now >= scheduledTimes[nextIndex])
            {
                scheduledSound.PlayOneShotOnCamera();
                lastFrame = currentFrame;
                nextIndex++;

                // Skip any overdue sounds to keep timing in sync after frame spikes
                while (nextIndex < scheduledTimes.Count && now >= scheduledTimes[nextIndex])
                    nextIndex++;
            }
        }
    }
}
