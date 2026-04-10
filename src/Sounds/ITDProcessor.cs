using UnityEngine;

namespace RimWorldAccess
{
    public class ITDProcessor : MonoBehaviour
    {
        private const int BufferSize = 32;
        private const float MaxITDSeconds = 0.000437f;

        private float[] delayBufferLeft;
        private float[] delayBufferRight;
        private int writeIndex;

        private float delaySamplesLeft;
        private float delaySamplesRight;
        private bool itdEnabled;

        public void SetPan(float pan)
        {
            float maxDelaySamples = MaxITDSeconds * AudioSettings.outputSampleRate;
            float delaySamples = maxDelaySamples * Mathf.Abs(pan);

            if (pan > 0f)
            {
                delaySamplesLeft = delaySamples;
                delaySamplesRight = 0f;
            }
            else if (pan < 0f)
            {
                delaySamplesLeft = 0f;
                delaySamplesRight = delaySamples;
            }
            else
            {
                delaySamplesLeft = 0f;
                delaySamplesRight = 0f;
            }
        }

        public void SetEnabled(bool enabled)
        {
            itdEnabled = enabled;
            if (!enabled)
            {
                delaySamplesLeft = 0f;
                delaySamplesRight = 0f;
            }
        }

        private void Awake()
        {
            delayBufferLeft = new float[BufferSize];
            delayBufferRight = new float[BufferSize];
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!itdEnabled || channels < 2) return;

            float delayL = delaySamplesLeft;
            float delayR = delaySamplesRight;

            if (delayL < 0.001f && delayR < 0.001f) return;

            for (int i = 0; i < data.Length; i += channels)
            {
                delayBufferLeft[writeIndex] = data[i];
                delayBufferRight[writeIndex] = data[i + 1];

                if (delayL > 0.001f)
                {
                    data[i] = ReadDelayed(delayBufferLeft, writeIndex, delayL);
                }

                if (delayR > 0.001f)
                {
                    data[i + 1] = ReadDelayed(delayBufferRight, writeIndex, delayR);
                }

                writeIndex = (writeIndex + 1) % BufferSize;
            }
        }

        private static float ReadDelayed(float[] buffer, int currentWriteIndex, float delaySamples)
        {
            int intDelay = (int)delaySamples;
            float frac = delaySamples - intDelay;

            int idx0 = (currentWriteIndex - intDelay + BufferSize) % BufferSize;
            int idx1 = (idx0 - 1 + BufferSize) % BufferSize;

            return buffer[idx0] * (1f - frac) + buffer[idx1] * frac;
        }
    }
}
