using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Plays a preview footstep clip when the user adjusts a per-category volume slider
    /// in the options menu. The pan is randomised each call so holding the arrow key
    /// produces footsteps that move around the stereo field — letting the user audition
    /// how the sound will feel when multiple pawns are active in gameplay.
    /// </summary>
    public static class FootstepPreview
    {
        private const float MinIntervalSeconds = 0.08f;

        // Representative mech size for preview. Picks a roughly medium mech so the
        // high-pass cutoff and pitch match what a typical enemy/player mech sounds like.
        private const float MechPreviewBodySize = 1.5f;

        private static float lastPlayTime = -999f;

        public static void Play(FootstepAudioCategory category)
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastPlayTime < MinIntervalSeconds) return;
            lastPlayTime = now;

            float volume = FootstepClassifier.GetVolume(category);
            if (volume <= 0f) return;

            string path = GetPath(category);
            float pitchMultiplier = GetPitch(category);
            float baseMultiplier = GetBaseMultiplier(category);
            float highPassCutoff = IsMechPreview(category)
                ? FootstepClassifier.GetMechHighPassCutoffForSize(MechPreviewBodySize)
                : 0f;

            float pan = Random.Range(-1f, 1f);
            float finalVolume = Mathf.Clamp(volume * baseMultiplier, 0f, 1.35f);

            FootstepSoundBank.PlayPreview(path, finalVolume, pan, pitchMultiplier, highPassCutoff);
        }

        // UndraftedMechs is the only category guaranteed to be mechs. Hostiles is a
        // mixed bag but we pick a humanlike representative for its preview — that's
        // what you hear for most raids anyway.
        private static bool IsMechPreview(FootstepAudioCategory c) =>
            c == FootstepAudioCategory.UndraftedMechs;

        private static string GetPath(FootstepAudioCategory category)
        {
            switch (category)
            {
                case FootstepAudioCategory.UndraftedMechs:
                    return "Sounds/mechanoid";

                case FootstepAudioCategory.TamedAnimals:
                case FootstepAudioCategory.WildAnimals:
                    return "Sounds/animal/light";

                default:
                    return "Sounds/human/dirt";
            }
        }

        private static float GetPitch(FootstepAudioCategory category)
        {
            if (IsMechPreview(category))
                return FootstepClassifier.GetMechPitchMultiplierForSize(MechPreviewBodySize);

            switch (category)
            {
                case FootstepAudioCategory.TamedAnimals:
                case FootstepAudioCategory.WildAnimals: return 1.1f;
                default: return 1.0f;
            }
        }

        private static float GetBaseMultiplier(FootstepAudioCategory category)
        {
            switch (category)
            {
                case FootstepAudioCategory.TamedAnimals:
                case FootstepAudioCategory.WildAnimals: return 0.7f;
                case FootstepAudioCategory.UndraftedMechs: return 1.3f;
                default: return 1.0f;
            }
        }
    }
}
