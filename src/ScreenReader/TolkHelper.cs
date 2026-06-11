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
    /// Screen reader integration via the Prism library, with optional Tolk fallback on Windows.
    /// If a user-supplied Tolk.dll is found in the RimWorld save data folder, Tolk is used instead
    /// of Prism. This supports Chinese players whose screen readers work with Tolk but not yet Prism.
    /// </summary>
    public static class TolkHelper
    {
        #region Tolk Delegates (Windows fallback)

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Tolk_LoadDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Tolk_UnloadDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool Tolk_IsLoadedDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate bool Tolk_OutputDelegate([MarshalAs(UnmanagedType.LPWStr)] string str, bool interrupt);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate IntPtr Tolk_DetectScreenReaderDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool Tolk_HasSpeechDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool Tolk_HasBrailleDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Tolk_TrySAPIDelegate(bool trySAPI);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int nvdaController_testIfRunningDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate int nvdaController_speakTextDelegate([MarshalAs(UnmanagedType.LPWStr)] string text);

        #endregion

        // Tolk fallback state (Windows only)
        private static bool useTolk = false;
        private static IntPtr tolkHandle = IntPtr.Zero;
        private static IntPtr nvdaHandle = IntPtr.Zero;
        private static bool useDirectNVDA = false;

        // Tolk function pointers
        private static Tolk_LoadDelegate tolkLoad;
        private static Tolk_UnloadDelegate tolkUnload;
        private static Tolk_IsLoadedDelegate tolkIsLoaded;
        private static Tolk_OutputDelegate tolkOutput;
        private static Tolk_DetectScreenReaderDelegate tolkDetectScreenReader;
        private static Tolk_HasSpeechDelegate tolkHasSpeech;
        private static Tolk_HasBrailleDelegate tolkHasBraille;
        private static Tolk_TrySAPIDelegate tolkTrySAPI;
        private static nvdaController_testIfRunningDelegate nvdaTestIfRunning;
        private static nvdaController_speakTextDelegate nvdaSpeakText;

        // Prism state
        private static IntPtr prismLibraryHandle = IntPtr.Zero;
        private static IntPtr prismContext = IntPtr.Zero;
        private static IntPtr prismBackend = IntPtr.Zero;
        private static string activeBackendName = null;

        private static bool isInitialized = false;

        /// <summary>
        /// Initializes the screen reader library.
        /// On Windows, checks for a user-supplied Tolk.dll first; falls back to Prism.
        /// On macOS/Linux, uses Prism directly.
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized)
            {
                return;
            }

            try
            {
                // Tolk fallback (Windows only)
                // Players can place Tolk.dll in the RimWorld save data folder to use Tolk
                // instead of Prism. This supports screen readers that Prism doesn't yet handle.
                if (NativeLibraryLoader.IsWindows)
                {
                    string tolkFolder = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimWorldAccess");
                    string tolkPath = Path.Combine(tolkFolder, "Tolk.dll");
                    Log.Message($"[RimWorld Access] Checking for Tolk.dll at: {tolkPath}");

                    if (File.Exists(tolkPath))
                    {
                        if (TryInitializeTolk(tolkFolder, tolkPath))
                        {
                            isInitialized = true;
                            return;
                        }
                        Log.Warning("[RimWorld Access] Tolk initialization failed, falling back to Prism");
                    }
                    else
                    {
                        Log.Message("[RimWorld Access] Tolk.dll not found, using Prism");
                    }
                }

                // Prism initialization (default path)
                InitializePrism();
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
                Log.Error($"[RimWorld Access] Failed to initialize screen reader: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Attempts to initialize Tolk from the given folder.
        /// Returns true on success, false on any failure (caller falls back to Prism).
        /// </summary>
        private static bool TryInitializeTolk(string tolkFolder, string tolkPath)
        {
            try
            {
                Log.Message($"[RimWorld Access] Found Tolk.dll, loading Tolk backend");

                // Optionally load NVDA controller client (non-fatal if missing)
                string nvdaPath = Path.Combine(tolkFolder, "nvdaControllerClient64.dll");
                if (File.Exists(nvdaPath))
                {
                    nvdaHandle = NativeLibraryLoader.LoadLibrary(nvdaPath);
                    if (nvdaHandle != IntPtr.Zero)
                    {
                        Log.Message("[RimWorld Access] Loaded nvdaControllerClient64.dll");
                        try
                        {
                            nvdaTestIfRunning = NativeLibraryLoader.GetFunction<nvdaController_testIfRunningDelegate>(nvdaHandle, "nvdaController_testIfRunning");
                            nvdaSpeakText = NativeLibraryLoader.GetFunction<nvdaController_speakTextDelegate>(nvdaHandle, "nvdaController_speakText");
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[RimWorld Access] Failed to get NVDA function pointers: {ex.Message}");
                        }
                    }
                    else
                    {
                        Log.Warning($"[RimWorld Access] Failed to load nvdaControllerClient64.dll: {NativeLibraryLoader.GetLastError()}");
                    }
                }

                // Load Tolk.dll
                tolkHandle = NativeLibraryLoader.LoadLibrary(tolkPath);
                if (tolkHandle == IntPtr.Zero)
                {
                    string error = NativeLibraryLoader.GetLastError();
                    Log.Error($"[RimWorld Access] Failed to load Tolk.dll: {error}");
                    CleanupTolk();
                    return false;
                }

                // Resolve Tolk function pointers
                tolkLoad = NativeLibraryLoader.GetFunction<Tolk_LoadDelegate>(tolkHandle, "Tolk_Load");
                tolkUnload = NativeLibraryLoader.GetFunction<Tolk_UnloadDelegate>(tolkHandle, "Tolk_Unload");
                tolkIsLoaded = NativeLibraryLoader.GetFunction<Tolk_IsLoadedDelegate>(tolkHandle, "Tolk_IsLoaded");
                tolkOutput = NativeLibraryLoader.GetFunction<Tolk_OutputDelegate>(tolkHandle, "Tolk_Output");
                tolkDetectScreenReader = NativeLibraryLoader.GetFunction<Tolk_DetectScreenReaderDelegate>(tolkHandle, "Tolk_DetectScreenReader");
                tolkHasSpeech = NativeLibraryLoader.GetFunction<Tolk_HasSpeechDelegate>(tolkHandle, "Tolk_HasSpeech");
                tolkHasBraille = NativeLibraryLoader.GetFunction<Tolk_HasBrailleDelegate>(tolkHandle, "Tolk_HasBraille");
                tolkTrySAPI = NativeLibraryLoader.GetFunction<Tolk_TrySAPIDelegate>(tolkHandle, "Tolk_TrySAPI");

                // Test NVDA directly first
                bool nvdaRunning = false;
                if (nvdaTestIfRunning != null)
                {
                    try
                    {
                        int nvdaResult = nvdaTestIfRunning();
                        nvdaRunning = (nvdaResult == 0);
                        Log.Message($"[RimWorld Access] Direct NVDA test: {(nvdaRunning ? "NVDA is running" : $"NVDA not detected (code: {nvdaResult})")}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[RimWorld Access] Could not test NVDA directly: {ex.Message}");
                    }
                }

                // Initialize Tolk
                tolkLoad();
                tolkTrySAPI(true);

                if (!tolkIsLoaded())
                {
                    Log.Warning("[RimWorld Access] Tolk loaded but no screen reader detected");
                    CleanupTolk();
                    return false;
                }

                useTolk = true;

                // Log backend info
                IntPtr namePtr = tolkDetectScreenReader();
                string screenReaderName = namePtr != IntPtr.Zero
                    ? Marshal.PtrToStringUni(namePtr)
                    : "Unknown";
                bool hasSpeech = tolkHasSpeech();
                bool hasBraille = tolkHasBraille();

                Log.Message("[RimWorld Access] Tolk screen reader integration initialized successfully.");
                Log.Message($"[RimWorld Access] Detected screen reader: {screenReaderName}");
                Log.Message($"[RimWorld Access] Speech support: {hasSpeech}");
                Log.Message($"[RimWorld Access] Braille support: {hasBraille}");

                // If Tolk detected SAPI but NVDA is actually running, use direct NVDA communication
                if (screenReaderName == "SAPI" && nvdaRunning)
                {
                    Log.Warning("[RimWorld Access] Tolk fell back to SAPI even though NVDA is running.");
                    Log.Message("[RimWorld Access] Switching to direct NVDA communication mode.");
                    useDirectNVDA = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Tolk initialization error: {ex.Message}");
                CleanupTolk();
                return false;
            }
        }

        /// <summary>
        /// Cleans up Tolk resources after a failed initialization attempt.
        /// </summary>
        private static void CleanupTolk()
        {
            useTolk = false;
            useDirectNVDA = false;

            if (tolkHandle != IntPtr.Zero)
            {
                NativeLibraryLoader.FreeLibrary(tolkHandle);
                tolkHandle = IntPtr.Zero;
            }

            if (nvdaHandle != IntPtr.Zero)
            {
                NativeLibraryLoader.FreeLibrary(nvdaHandle);
                nvdaHandle = IntPtr.Zero;
            }

            tolkLoad = null;
            tolkUnload = null;
            tolkIsLoaded = null;
            tolkOutput = null;
            tolkDetectScreenReader = null;
            tolkHasSpeech = null;
            tolkHasBraille = null;
            tolkTrySAPI = null;
            nvdaTestIfRunning = null;
            nvdaSpeakText = null;
        }

        /// <summary>
        /// Initializes the Prism screen reader library (default backend).
        /// </summary>
        private static void InitializePrism()
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
            Log.Message($"[RimWorld Access] Prism config version: {config.version}");
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
            activeBackendName = PrismNative.ReadUtf8(PrismNative.prism_backend_name(prismBackend)) ?? "Unknown";
            ulong features = PrismNative.prism_backend_get_features(prismBackend);
            PrismBackendFeature featureFlags = (PrismBackendFeature)features;

            Log.Message($"[RimWorld Access] Prism screen reader integration initialized successfully.");
            Log.Message($"[RimWorld Access] Active backend: {activeBackendName}");
            Log.Message($"[RimWorld Access] Speech support: {featureFlags.HasFlag(PrismBackendFeature.SupportsSpeak)}");
            Log.Message($"[RimWorld Access] Braille support: {featureFlags.HasFlag(PrismBackendFeature.SupportsBraille)}");
            Log.Message($"[RimWorld Access] Output (speech+braille) support: {featureFlags.HasFlag(PrismBackendFeature.SupportsOutput)}");
        }

        /// <summary>
        /// Shuts down the screen reader library.
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

                if (useTolk)
                {
                    tolkUnload?.Invoke();
                    CleanupTolk();
                    Log.Message("[RimWorld Access] Tolk screen reader integration shut down.");
                    return;
                }

                // Prism shutdown
                if (prismBackend != IntPtr.Zero)
                {
                    PrismNative.prism_backend_free?.Invoke(prismBackend);
                    prismBackend = IntPtr.Zero;
                }

                if (prismContext != IntPtr.Zero)
                {
                    PrismNative.prism_shutdown?.Invoke(prismContext);
                    prismContext = IntPtr.Zero;
                }

                PrismNative.ClearFunctions();

                if (prismLibraryHandle != IntPtr.Zero)
                {
                    NativeLibraryLoader.FreeLibrary(prismLibraryHandle);
                    prismLibraryHandle = IntPtr.Zero;
                }

                activeBackendName = null;

                Log.Message("[RimWorld Access] Prism screen reader integration shut down.");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error shutting down screen reader: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the screen reader backend is initialized and available.
        /// </summary>
        public static bool IsActive()
        {
            if (!isInitialized)
            {
                return false;
            }

            if (useTolk)
            {
                try
                {
                    return tolkIsLoaded?.Invoke() ?? false;
                }
                catch
                {
                    return false;
                }
            }

            return prismBackend != IntPtr.Zero;
        }

        /// <summary>
        /// True when the active backend doesn't handle speech interruption on key press.
        /// macOS AVSpeechSynthesizer queues speech but never interrupts it;
        /// VoiceOver and Windows screen readers handle interruption themselves.
        /// </summary>
        public static bool ShouldInterruptOnKeyPress =>
            isInitialized && !useTolk && activeBackendName == "AVSpeech";

        /// <summary>
        /// Stops any currently playing speech. Used to manually interrupt backends
        /// that don't interrupt on key press (e.g., AVSpeech on macOS).
        /// </summary>
        public static void StopSpeech()
        {
            if (!isInitialized || useTolk)
                return;

            try
            {
                PrismNative.prism_backend_stop?.Invoke(prismBackend);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error stopping speech: {ex.Message}");
            }
        }

        /// <summary>
        /// Speaks a localized string. This is the preferred entry point: the
        /// <see cref="Localized"/> type can only be produced via <c>.Loc()</c>
        /// (which routes through the translation system), so the compiler guarantees
        /// the text is translatable.
        /// </summary>
        /// <param name="text">The localized text to speak (e.g. <c>"MyKey".Loc(args)</c>)</param>
        /// <param name="priority">Speech priority level (determines interruption behavior)</param>
        public static void Speak(Localized text, SpeechPriority priority = SpeechPriority.Normal)
        {
            SpeakInternal(text.SpokenText, priority);
        }

        /// <summary>
        /// Speaks text that is intentionally not a translation key — numbers, proper
        /// names, or labels already localized by the game (e.g. <c>thing.LabelCap</c>).
        /// Use sparingly and only for genuine passthrough data; prefer
        /// <see cref="Speak(Localized, SpeechPriority)"/> for any authored prose.
        /// </summary>
        /// <param name="text">The passthrough text to speak</param>
        /// <param name="priority">Speech priority level (determines interruption behavior)</param>
        public static void SpeakData(string text, SpeechPriority priority = SpeechPriority.Normal)
        {
            SpeakInternal(text, priority);
        }

        /// <summary>
        /// Core speech implementation shared by all public Speak overloads.
        /// </summary>
        /// <param name="text">The text to speak</param>
        /// <param name="priority">Speech priority level (determines interruption behavior)</param>
        private static void SpeakInternal(string text, SpeechPriority priority = SpeechPriority.Normal)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (!isInitialized)
            {
                Log.Warning("[RimWorld Access] Speak called but screen reader is not initialized");
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
                bool interrupt = priority == SpeechPriority.High;

                // Tolk path (Windows fallback)
                if (useTolk)
                {
                    if (useDirectNVDA && nvdaSpeakText != null)
                    {
                        try
                        {
                            nvdaSpeakText(text);
                            return;
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[RimWorld Access] Direct NVDA communication failed: {ex.Message}, falling back to Tolk");
                            useDirectNVDA = false;
                        }
                    }

                    tolkOutput(text, interrupt);
                    return;
                }

                // Prism path (default)
                if (PrismNative.prism_backend_output == null)
                {
                    Log.Warning("[RimWorld Access] Speak called but Prism is not initialized");
                    return;
                }

                var (handle, pointer) = PrismNative.MarshalUtf8(text);
                try
                {
                    PrismError result = PrismNative.prism_backend_output(prismBackend, pointer, interrupt);
                    if (result != PrismError.Ok)
                    {
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
                Log.Error($"[RimWorld Access] Error speaking text: {ex.Message}");
            }
        }
    }
}
