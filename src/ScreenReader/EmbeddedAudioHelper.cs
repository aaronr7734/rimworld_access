using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for loading and playing embedded audio resources.
    /// Audio files should be placed in the "Sounds" folder of the project
    /// and will be embedded into the DLL at compile time.
    /// </summary>
    public static class EmbeddedAudioHelper
    {
        private static Assembly assembly = Assembly.GetExecutingAssembly();

        /// <summary>
        /// Load an embedded audio file and return as Unity AudioClip.
        /// </summary>
        /// <param name="resourcePath">Path relative to Sounds folder, e.g. "navigate.wav"</param>
        /// <returns>AudioClip if successful, null otherwise</returns>
        public static AudioClip LoadEmbeddedAudio(string resourcePath)
        {
            try
            {
                // Construct the full resource name
                // Format: rimworld_access.Sounds.filename.extension
                // Note: MSBuild uses lowercase assembly name for embedded resources
                string resourceName = $"rimworld_access.Sounds.{resourcePath.Replace('/', '.').Replace('\\', '.')}";

                // Get the embedded resource stream
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        ModLogger.Error($"Embedded audio resource not found: {resourceName}");
                        return null;
                    }

                    // Read the audio data
                    byte[] audioData = new byte[stream.Length];
                    stream.Read(audioData, 0, audioData.Length);

                    // Determine audio format and load accordingly
                    string extension = Path.GetExtension(resourcePath).ToLower();

                    switch (extension)
                    {
                        case ".wav":
                            return LoadWAV(audioData, resourcePath);
                        case ".ogg":
                            // Unity can load OGG directly
                            return LoadOGG(audioData, resourcePath);
                        default:
                            ModLogger.Error($"Unsupported audio format: {extension}");
                            return null;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to load embedded audio '{resourcePath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load WAV file from byte array.
        /// </summary>
        private static AudioClip LoadWAV(byte[] wavData, string name)
        {
            // WAV format: RIFF header + chunks (fmt, JUNK, data, etc.)
            // This parser handles JUNK chunks and searches for fmt and data chunks

            if (wavData.Length < 44)
            {
                ModLogger.Error($"WAV file too small: {name}");
                return null;
            }

            // Verify RIFF header
            string riffHeader = System.Text.Encoding.ASCII.GetString(wavData, 0, 4);
            string waveHeader = System.Text.Encoding.ASCII.GetString(wavData, 8, 4);

            if (riffHeader != "RIFF" || waveHeader != "WAVE")
            {
                ModLogger.Error($"Invalid WAV file header in {name}");
                return null;
            }

            // Search for fmt chunk
            int fmtChunkOffset = FindChunk(wavData, "fmt ");
            if (fmtChunkOffset == -1)
            {
                ModLogger.Error($"Could not find fmt chunk in {name}");
                return null;
            }

            // Parse fmt chunk (offset + 8 to skip chunk ID and size)
            int channels = BitConverter.ToInt16(wavData, fmtChunkOffset + 10);
            int sampleRate = BitConverter.ToInt32(wavData, fmtChunkOffset + 12);
            int bitDepth = BitConverter.ToInt16(wavData, fmtChunkOffset + 22);

            if (bitDepth != 16)
            {
                ModLogger.Error($"Only 16-bit WAV supported. Got {bitDepth}-bit in {name}");
                return null;
            }

            // Search for data chunk
            int dataChunkOffset = FindChunk(wavData, "data");
            if (dataChunkOffset == -1)
            {
                ModLogger.Error($"Could not find data chunk in {name}");
                return null;
            }

            // Get data chunk size and calculate number of samples
            int dataSize = BitConverter.ToInt32(wavData, dataChunkOffset + 4);
            int dataStart = dataChunkOffset + 8; // Skip "data" and size
            int dataLength = dataSize / 2; // 16-bit = 2 bytes per sample

            // Extract PCM data
            float[] samples = new float[dataLength];
            for (int i = 0; i < dataLength; i++)
            {
                short sample = BitConverter.ToInt16(wavData, dataStart + i * 2);
                samples[i] = sample / 32768f; // Convert to float [-1, 1]
            }

            // Create Unity AudioClip
            AudioClip clip = AudioClip.Create(name, dataLength / channels, channels, sampleRate, false);
            clip.SetData(samples, 0);

            return clip;
        }

        /// <summary>
        /// Find a chunk in WAV file by its 4-character ID.
        /// Returns the offset of the chunk ID, or -1 if not found.
        /// </summary>
        private static int FindChunk(byte[] wavData, string chunkId)
        {
            // Start searching after the RIFF header (offset 12)
            int offset = 12;

            while (offset + 8 <= wavData.Length)
            {
                string currentChunkId = System.Text.Encoding.ASCII.GetString(wavData, offset, 4);
                int chunkSize = BitConverter.ToInt32(wavData, offset + 4);

                if (currentChunkId == chunkId)
                {
                    return offset;
                }

                // Move to next chunk (skip ID, size, and data)
                offset += 8 + chunkSize;

                // Chunks are word-aligned (even byte boundaries)
                if (chunkSize % 2 != 0)
                    offset++;
            }

            return -1; // Chunk not found
        }

        /// <summary>
        /// Load OGG file from byte array using RimWorld's existing loader.
        /// </summary>
        private static AudioClip LoadOGG(byte[] oggData, string name)
        {
            // RimWorld has RuntimeAudioClipLoader which supports OGG
            // However, it expects file paths, not byte arrays

            // Fallback: Write to temp file and let Unity load it
            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), $"rimworld_access_{name}");
                File.WriteAllBytes(tempPath, oggData);

                // Note: This is a simplified approach. Unity's WWW or UnityWebRequest
                // would normally be used for runtime audio loading, but those require
                // coroutines which are tricky with Harmony patches.

                ModLogger.Warning($"OGG loading not fully implemented. Use WAV format for embedded audio.");
                return null;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to load OGG: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Play an embedded audio file using Unity's AudioSource.
        /// This bypasses RimWorld's sound system entirely.
        /// </summary>
        /// <param name="resourcePath">Path to embedded resource, e.g. "navigate.wav"</param>
        /// <param name="volume">Volume level 0-1</param>
        public static void PlayEmbeddedSound(string resourcePath, float volume = 1f)
        {
            AudioClip clip = LoadEmbeddedAudio(resourcePath);
            if (clip == null)
            {
                ModLogger.Warning($"AudioClip is null for: {resourcePath}");
                return;
            }

            try
            {
                // Get or create an AudioSource on the camera
                Camera mainCamera = Find.Camera;
                if (mainCamera == null)
                {
                    ModLogger.Error("Main camera not found");
                    return;
                }

                AudioSource audioSource = mainCamera.GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = mainCamera.gameObject.AddComponent<AudioSource>();
                }

                audioSource.PlayOneShot(clip, volume);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to play sound: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
