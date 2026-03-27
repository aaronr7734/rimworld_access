using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Speech priority levels for screen reader output.
    /// </summary>
    public enum SpeechPriority
    {
        Low,      // Don't interrupt (navigation)
        Normal,   // Interrupt low priority
        High      // Interrupt everything (errors, critical info)
    }

    /// <summary>
    /// Screen reader integration via the Prism library.
    /// Provides cross-platform speech output to NVDA, JAWS, VoiceOver, Orca, and others.
    /// </summary>
    public static class TolkHelper
    {
        // Native library handle
        private static IntPtr prismLibraryHandle = IntPtr.Zero;

        // Prism context and backend handles
        private static IntPtr prismContext = IntPtr.Zero;
        private static IntPtr prismBackend = IntPtr.Zero;

        private static bool isInitialized = false;

        /// <summary>
        /// Initializes the Prism screen reader library.
        /// Must be called before any other operations.
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized)
            {
                return;
            }

            try
            {
                // Get mod folder path
                // The assembly is in: Mods/RimWorldAccess/Assemblies/rimworld_access.dll
                // Native libraries are in: Mods/RimWorldAccess/
                string modAssemblyPath = Assembly.GetExecutingAssembly().Location;
                string assemblyFolder = Path.GetDirectoryName(modAssemblyPath);

                // Go up from Assemblies to mod root (one level up)
                string modRoot = Path.GetFullPath(Path.Combine(assemblyFolder, ".."));

                // Resolve platform-specific library name
                string libraryName = NativeLibraryLoader.GetNativeLibraryName("prism");
                string libraryPath = Path.Combine(modRoot, libraryName);

                string platformName = NativeLibraryLoader.IsWindows ? "Windows" :
                                      NativeLibraryLoader.IsMacOS ? "macOS" : "Linux";
                Log.Message($"[RimWorld Access] Platform: {platformName}, loading {libraryName} from: {modRoot}");

                // Check if library exists
                if (!File.Exists(libraryPath))
                {
                    Log.Error($"[RimWorld Access] {libraryName} not found at: {libraryPath}");
                    throw new DllNotFoundException($"{libraryName} not found at: {libraryPath}");
                }

                // Load the native library
                prismLibraryHandle = NativeLibraryLoader.LoadLibrary(libraryPath);
                if (prismLibraryHandle == IntPtr.Zero)
                {
                    string error = NativeLibraryLoader.GetLastError();
                    throw new DllNotFoundException($"Failed to load {libraryName}: {error}");
                }

                Log.Message($"[RimWorld Access] Loaded {libraryName} successfully");

                // Resolve all Prism function pointers
                PrismNative.LoadFunctions(prismLibraryHandle);

                // Initialize Prism context
                PrismConfig config = PrismNative.prism_config_init();
                prismContext = PrismNative.prism_init(ref config);
                if (prismContext == IntPtr.Zero)
                {
                    throw new Exception("prism_init returned null context");
                }

                // Auto-select the best available backend (screen reader > TTS)
                prismBackend = PrismNative.prism_registry_acquire_best(prismContext);
                if (prismBackend == IntPtr.Zero)
                {
                    throw new Exception("No screen reader or TTS backend available");
                }

                // Initialize the backend
                PrismError initResult = PrismNative.prism_backend_initialize(prismBackend);
                if (initResult != PrismError.Ok && initResult != PrismError.AlreadyInitialized)
                {
                    string errorMsg = PrismNative.GetErrorString(initResult);
                    throw new Exception($"Backend initialization failed: {errorMsg}");
                }

                isInitialized = true;

                // Log backend info
                string backendName = PrismNative.ReadUtf8(PrismNative.prism_backend_name(prismBackend)) ?? "Unknown";
                ulong features = PrismNative.prism_backend_get_features(prismBackend);
                PrismBackendFeature featureFlags = (PrismBackendFeature)features;

                Log.Message($"[RimWorld Access] Prism screen reader integration initialized successfully.");
                Log.Message($"[RimWorld Access] Active backend: {backendName}");
                Log.Message($"[RimWorld Access] Speech support: {featureFlags.HasFlag(PrismBackendFeature.SupportsSpeak)}");
                Log.Message($"[RimWorld Access] Braille support: {featureFlags.HasFlag(PrismBackendFeature.SupportsBraille)}");
                Log.Message($"[RimWorld Access] Output (speech+braille) support: {featureFlags.HasFlag(PrismBackendFeature.SupportsOutput)}");
            }
            catch (DllNotFoundException ex)
            {
                Log.Error($"[RimWorld Access] Failed to load native library: {ex.Message}");
                string expectedName = NativeLibraryLoader.GetNativeLibraryName("prism");
                Log.Error($"[RimWorld Access] Ensure {expectedName} is in the mod's root folder (Mods/RimWorldAccess/)");
                throw;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Failed to initialize Prism: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Shuts down the Prism screen reader library.
        /// Should be called during mod cleanup.
        /// </summary>
        public static void Shutdown()
        {
            if (!isInitialized)
            {
                return;
            }

            try
            {
                isInitialized = false;

                // Free the backend
                if (prismBackend != IntPtr.Zero)
                {
                    PrismNative.prism_backend_free?.Invoke(prismBackend);
                    prismBackend = IntPtr.Zero;
                }

                // Shut down the context
                if (prismContext != IntPtr.Zero)
                {
                    PrismNative.prism_shutdown?.Invoke(prismContext);
                    prismContext = IntPtr.Zero;
                }

                // Clear function pointers
                PrismNative.ClearFunctions();

                // Free the native library
                if (prismLibraryHandle != IntPtr.Zero)
                {
                    NativeLibraryLoader.FreeLibrary(prismLibraryHandle);
                    prismLibraryHandle = IntPtr.Zero;
                }

                Log.Message("[RimWorld Access] Prism screen reader integration shut down.");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error shutting down Prism: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if Prism is initialized and a backend is available.
        /// </summary>
        public static bool IsActive()
        {
            return isInitialized && prismBackend != IntPtr.Zero;
        }

        /// <summary>
        /// Sends text to the screen reader for speech output.
        /// </summary>
        /// <param name="text">The text to speak</param>
        /// <param name="priority">Speech priority level (determines interruption behavior)</param>
        public static void Speak(string text, SpeechPriority priority = SpeechPriority.Normal)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (!isInitialized || PrismNative.prism_backend_output == null)
            {
                Log.Warning("[RimWorld Access] Speak called but Prism is not initialized");
                return;
            }

            // Sanitize text: strip tags, fix punctuation, collapse whitespace
            text = SpeechSanitizer.Sanitize(text);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                // Determine interrupt behavior based on priority
                bool interrupt = priority == SpeechPriority.High;

                // Marshal string to UTF-8 for Prism
                var (handle, pointer) = PrismNative.MarshalUtf8(text);
                try
                {
                    // Use prism_backend_output which handles both speech and braille
                    PrismError result = PrismNative.prism_backend_output(prismBackend, pointer, interrupt);
                    if (result != PrismError.Ok)
                    {
                        // Fall back to speak-only if output isn't supported
                        if (result == PrismError.NotImplemented && PrismNative.prism_backend_speak != null)
                        {
                            PrismNative.prism_backend_speak(prismBackend, pointer, interrupt);
                        }
                    }
                }
                finally
                {
                    PrismNative.FreeUtf8(handle);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error speaking text via Prism: {ex.Message}");
            }
        }
    }
}
