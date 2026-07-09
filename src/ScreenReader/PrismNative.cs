using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RimWorldAccess
{
    /// <summary>
    /// Error codes returned by Prism API functions.
    /// </summary>
    public enum PrismError
    {
        Ok = 0,
        NotInitialized,
        InvalidParam,
        NotImplemented,
        NoVoices,
        VoiceNotFound,
        SpeakFailure,
        MemoryFailure,
        RangeOutOfBounds,
        Internal,
        NotSpeaking,
        NotPaused,
        AlreadyPaused,
        InvalidUtf8,
        InvalidOperation,
        AlreadyInitialized,
        BackendNotAvailable,
        Unknown,
        InvalidAudioFormat,
        Count
    }

    /// <summary>
    /// Feature flags indicating backend capabilities.
    /// </summary>
    [Flags]
    public enum PrismBackendFeature : ulong
    {
        IsSupportedAtRuntime = 1UL << 0,
        SupportsSpeak = 1UL << 2,
        SupportsSpeakToMemory = 1UL << 3,
        SupportsBraille = 1UL << 4,
        SupportsOutput = 1UL << 5,
        SupportsIsSpeaking = 1UL << 6,
        SupportsStop = 1UL << 7,
        SupportsPause = 1UL << 8,
        SupportsResume = 1UL << 9,
        SupportsSetVolume = 1UL << 10,
        SupportsGetVolume = 1UL << 11,
        SupportsSetRate = 1UL << 12,
        SupportsGetRate = 1UL << 13,
        SupportsSetPitch = 1UL << 14,
        SupportsGetPitch = 1UL << 15,
        SupportsRefreshVoices = 1UL << 16,
        SupportsCountVoices = 1UL << 17,
        SupportsGetVoiceName = 1UL << 18,
        SupportsGetVoiceLanguage = 1UL << 19,
        SupportsGetVoice = 1UL << 20,
        SupportsSetVoice = 1UL << 21,
        SupportsGetChannels = 1UL << 22,
        SupportsGetSampleRate = 1UL << 23,
        SupportsGetBitDepth = 1UL << 24,
        PerformsSilenceTrimmingOnSpeak = 1UL << 25,
        PerformsSilenceTrimmingOnSpeakToMemory = 1UL << 26,
        SupportsSpeakSsml = 1UL << 27,
        SupportsSpeakToMemorySsml = 1UL << 28
    }

    /// <summary>
    /// Known Prism backend identifiers.
    /// </summary>
    public static class PrismBackendIds
    {
        public static readonly ulong Invalid = 0;
        public static readonly ulong SAPI = 0x1D6DF72422CEEE66;
        public static readonly ulong AVSpeech = 0x28E3429577805C24;
        public static readonly ulong VoiceOver = 0xCB4897961A754BCB;
        public static readonly ulong SpeechDispatcher = 0xE3D6F895D949EBFE;
        public static readonly ulong NVDA = 0x89CC19C5C4AC1A56;
        public static readonly ulong JAWS = 0xAC3D60E9BD84B53E;
        public static readonly ulong OneCore = 0x6797D32F0D994CB4;
        public static readonly ulong Orca = 0x10AA1FC05A17F96C;
        public static readonly ulong AndroidScreenReader = 0xD199C175AEEC494B;
        public static readonly ulong AndroidTTS = 0xBC175831BFE4E5CC;
        public static readonly ulong WebSpeech = 0x3572538D44D44A8F;
        public static readonly ulong UIA = 0x6238F019DB678F8E;
        public static readonly ulong ZDSR = 0x3D93C56C9E7F2A2E;
        public static readonly ulong ZoomText = 0xAE439D62DC7B1479;
    }

    /// <summary>
    /// PrismConfig struct matching the C header layout for Prism v0.17
    /// (PRISM_CONFIG_VERSION = 3). Returned by value from prism_config_init,
    /// so the managed layout must match the native one exactly: natural
    /// alignment (version padded to pointer alignment), 48 bytes on 64-bit.
    /// The C bool is bound as a byte to keep the struct blittable.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PrismConfig
    {
        public byte version;
        public IntPtr registry;
        public IntPtr availabilityCallback;
        public IntPtr availabilityUserdata;
        public uint availabilityPollIntervalMs;
        public uint availabilityDebounceSamples;
        public uint availabilityBackoffMaxMs;
        public byte availabilityAutoPowerManage;
    }

    /// <summary>
    /// Low-level P/Invoke bindings for the Prism screen reader library.
    /// All native calls go through NativeLibraryLoader for cross-platform support.
    /// </summary>
    public static class PrismNative
    {
        #region Function Delegates

        // Context management
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismConfig prism_config_init_delegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr prism_init_delegate(ref PrismConfig cfg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void prism_shutdown_delegate(IntPtr ctx);

        // Registry
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr prism_registry_acquire_best_delegate(IntPtr ctx);

        // Backend lifecycle
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void prism_backend_free_delegate(IntPtr backend);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr prism_backend_name_delegate(IntPtr backend);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate ulong prism_backend_get_features_delegate(IntPtr backend);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_initialize_delegate(IntPtr backend);

        // Speech
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_speak_delegate(IntPtr backend, IntPtr text, bool interrupt);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_output_delegate(IntPtr backend, IntPtr text, bool interrupt);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_braille_delegate(IntPtr backend, IntPtr text);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_stop_delegate(IntPtr backend);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_pause_delegate(IntPtr backend);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_resume_delegate(IntPtr backend);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_is_speaking_delegate(IntPtr backend, out bool outSpeaking);

        // Voice parameters
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_set_volume_delegate(IntPtr backend, float volume);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_get_volume_delegate(IntPtr backend, out float outVolume);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_set_rate_delegate(IntPtr backend, float rate);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_get_rate_delegate(IntPtr backend, out float outRate);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_set_pitch_delegate(IntPtr backend, float pitch);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate PrismError prism_backend_get_pitch_delegate(IntPtr backend, out float outPitch);

        // Utility
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr prism_error_string_delegate(PrismError error);

        #endregion

        #region Function Pointers

        public static prism_config_init_delegate prism_config_init;
        public static prism_init_delegate prism_init;
        public static prism_shutdown_delegate prism_shutdown;
        public static prism_registry_acquire_best_delegate prism_registry_acquire_best;
        public static prism_backend_free_delegate prism_backend_free;
        public static prism_backend_name_delegate prism_backend_name;
        public static prism_backend_get_features_delegate prism_backend_get_features;
        public static prism_backend_initialize_delegate prism_backend_initialize;
        public static prism_backend_speak_delegate prism_backend_speak;
        public static prism_backend_output_delegate prism_backend_output;
        public static prism_backend_braille_delegate prism_backend_braille;
        public static prism_backend_stop_delegate prism_backend_stop;
        public static prism_backend_pause_delegate prism_backend_pause;
        public static prism_backend_resume_delegate prism_backend_resume;
        public static prism_backend_is_speaking_delegate prism_backend_is_speaking;
        public static prism_backend_set_volume_delegate prism_backend_set_volume;
        public static prism_backend_get_volume_delegate prism_backend_get_volume;
        public static prism_backend_set_rate_delegate prism_backend_set_rate;
        public static prism_backend_get_rate_delegate prism_backend_get_rate;
        public static prism_backend_set_pitch_delegate prism_backend_set_pitch;
        public static prism_backend_get_pitch_delegate prism_backend_get_pitch;
        public static prism_error_string_delegate prism_error_string;

        #endregion

        /// <summary>
        /// Loads all Prism function pointers from the native library handle.
        /// </summary>
        public static void LoadFunctions(IntPtr libraryHandle)
        {
            prism_config_init = NativeLibraryLoader.GetFunction<prism_config_init_delegate>(libraryHandle, "prism_config_init");
            prism_init = NativeLibraryLoader.GetFunction<prism_init_delegate>(libraryHandle, "prism_init");
            prism_shutdown = NativeLibraryLoader.GetFunction<prism_shutdown_delegate>(libraryHandle, "prism_shutdown");
            prism_registry_acquire_best = NativeLibraryLoader.GetFunction<prism_registry_acquire_best_delegate>(libraryHandle, "prism_registry_acquire_best");
            prism_backend_free = NativeLibraryLoader.GetFunction<prism_backend_free_delegate>(libraryHandle, "prism_backend_free");
            prism_backend_name = NativeLibraryLoader.GetFunction<prism_backend_name_delegate>(libraryHandle, "prism_backend_name");
            prism_backend_get_features = NativeLibraryLoader.GetFunction<prism_backend_get_features_delegate>(libraryHandle, "prism_backend_get_features");
            prism_backend_initialize = NativeLibraryLoader.GetFunction<prism_backend_initialize_delegate>(libraryHandle, "prism_backend_initialize");
            prism_backend_speak = NativeLibraryLoader.GetFunction<prism_backend_speak_delegate>(libraryHandle, "prism_backend_speak");
            prism_backend_output = NativeLibraryLoader.GetFunction<prism_backend_output_delegate>(libraryHandle, "prism_backend_output");
            prism_backend_braille = NativeLibraryLoader.GetFunction<prism_backend_braille_delegate>(libraryHandle, "prism_backend_braille");
            prism_backend_stop = NativeLibraryLoader.GetFunction<prism_backend_stop_delegate>(libraryHandle, "prism_backend_stop");
            prism_backend_pause = NativeLibraryLoader.GetFunction<prism_backend_pause_delegate>(libraryHandle, "prism_backend_pause");
            prism_backend_resume = NativeLibraryLoader.GetFunction<prism_backend_resume_delegate>(libraryHandle, "prism_backend_resume");
            prism_backend_is_speaking = NativeLibraryLoader.GetFunction<prism_backend_is_speaking_delegate>(libraryHandle, "prism_backend_is_speaking");
            prism_backend_set_volume = NativeLibraryLoader.GetFunction<prism_backend_set_volume_delegate>(libraryHandle, "prism_backend_set_volume");
            prism_backend_get_volume = NativeLibraryLoader.GetFunction<prism_backend_get_volume_delegate>(libraryHandle, "prism_backend_get_volume");
            prism_backend_set_rate = NativeLibraryLoader.GetFunction<prism_backend_set_rate_delegate>(libraryHandle, "prism_backend_set_rate");
            prism_backend_get_rate = NativeLibraryLoader.GetFunction<prism_backend_get_rate_delegate>(libraryHandle, "prism_backend_get_rate");
            prism_backend_set_pitch = NativeLibraryLoader.GetFunction<prism_backend_set_pitch_delegate>(libraryHandle, "prism_backend_set_pitch");
            prism_backend_get_pitch = NativeLibraryLoader.GetFunction<prism_backend_get_pitch_delegate>(libraryHandle, "prism_backend_get_pitch");
            prism_error_string = NativeLibraryLoader.GetFunction<prism_error_string_delegate>(libraryHandle, "prism_error_string");
        }

        /// <summary>
        /// Clears all function pointers (called during shutdown).
        /// </summary>
        public static void ClearFunctions()
        {
            prism_config_init = null;
            prism_init = null;
            prism_shutdown = null;
            prism_registry_acquire_best = null;
            prism_backend_free = null;
            prism_backend_name = null;
            prism_backend_get_features = null;
            prism_backend_initialize = null;
            prism_backend_speak = null;
            prism_backend_output = null;
            prism_backend_braille = null;
            prism_backend_stop = null;
            prism_backend_pause = null;
            prism_backend_resume = null;
            prism_backend_is_speaking = null;
            prism_backend_set_volume = null;
            prism_backend_get_volume = null;
            prism_backend_set_rate = null;
            prism_backend_get_rate = null;
            prism_backend_set_pitch = null;
            prism_backend_get_pitch = null;
            prism_error_string = null;
        }

        #region UTF-8 Marshaling Helpers

        /// <summary>
        /// Converts a C# string to a pinned null-terminated UTF-8 byte array.
        /// Caller must free the returned handle with FreeUtf8().
        /// </summary>
        /// <returns>Tuple of (GCHandle for the pinned array, IntPtr to pass to native code).</returns>
        public static (GCHandle handle, IntPtr pointer) MarshalUtf8(string text)
        {
            if (text == null) text = string.Empty;
            byte[] utf8 = Encoding.UTF8.GetBytes(text + "\0");
            GCHandle pin = GCHandle.Alloc(utf8, GCHandleType.Pinned);
            return (pin, pin.AddrOfPinnedObject());
        }

        /// <summary>
        /// Frees a pinned UTF-8 string handle returned by MarshalUtf8.
        /// </summary>
        public static void FreeUtf8(GCHandle handle)
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        /// <summary>
        /// Reads a null-terminated UTF-8 string from a native pointer.
        /// </summary>
        public static string ReadUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            // Find string length by scanning for null terminator
            int length = 0;
            while (Marshal.ReadByte(ptr, length) != 0)
            {
                length++;
            }

            if (length == 0)
            {
                return string.Empty;
            }

            byte[] buffer = new byte[length];
            Marshal.Copy(ptr, buffer, 0, length);
            return Encoding.UTF8.GetString(buffer);
        }

        /// <summary>
        /// Gets a human-readable error message for a PrismError code.
        /// </summary>
        public static string GetErrorString(PrismError error)
        {
            if (prism_error_string == null)
            {
                return error.ToString();
            }

            IntPtr ptr = prism_error_string(error);
            return ReadUtf8(ptr) ?? error.ToString();
        }

        #endregion
    }
}
