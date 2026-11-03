using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Speech priority levels for screen reader output.</summary>
    public enum SpeechPriority
    {
        Low,      // Don't interrupt (navigation)
        Normal,   // Interrupt low priority
        High      // Interrupt everything (errors, critical info)
    }

    /// <summary>
    /// Screen reader integration via Prism, with an optional Tolk fallback on Windows: a
    /// user-supplied Tolk.dll in the RimWorld save data folder takes precedence over Prism.
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
        private static bool speechSupported = false;
        private static bool brailleSupported = false;

        private static bool isInitialized = false;

        public static string DescribeBackend()
        {
            if (!isInitialized)
            {
                return "none";
            }
            return activeBackendName + " (speech " + (speechSupported ? "yes" : "no")
                + ", braille " + (brailleSupported ? "yes" : "no") + ")";
        }

        /// <summary>Initializes the screen reader library: Windows prefers a user-supplied Tolk.dll, else Prism.</summary>
        public static void Initialize()
        {
            // Initialize always runs on RimWorld's main thread; SpeakInternal's dedupe reads the
            // main-thread-only Time.frameCount and needs to know which thread that is.
            mainThreadManagedId = System.Threading.Thread.CurrentThread.ManagedThreadId;

            // SpeechSanitizer is linked game-free into the test project and cannot reference
            // TolkHelper directly; this delegate is its only route back to the log.
            SpeechSanitizer.ScrubReporter = ReportScrubbedSpeech;

            if (isInitialized)
            {
                return;
            }

            try
            {
            // A player-supplied Tolk.dll in the save data folder overrides Prism.
                if (NativeLibraryLoader.IsWindows)
                {
                    string tolkFolder = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimWorldAccess");
                    string tolkPath = Path.Combine(tolkFolder, "Tolk.dll");

                    if (File.Exists(tolkPath))
                    {
                        if (TryInitializeTolk(tolkFolder, tolkPath))
                        {
                            isInitialized = true;
                            return;
                        }
                        Log.Warning("[RimWorld Access] Tolk initialization failed, falling back to Prism");
                    }
                }

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

        /// <summary>Initializes Tolk from the given folder; false on any failure, leaving Prism as the fallback.</summary>
        private static bool TryInitializeTolk(string tolkFolder, string tolkPath)
        {
            try
            {
                // NVDA controller client is optional; a missing one is non-fatal.
                string nvdaPath = Path.Combine(tolkFolder, "nvdaControllerClient64.dll");
                if (File.Exists(nvdaPath))
                {
                    nvdaHandle = NativeLibraryLoader.LoadLibrary(nvdaPath);
                    if (nvdaHandle != IntPtr.Zero)
                    {
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

                tolkHandle = NativeLibraryLoader.LoadLibrary(tolkPath);
                if (tolkHandle == IntPtr.Zero)
                {
                    string error = NativeLibraryLoader.GetLastError();
                    Log.Error($"[RimWorld Access] Failed to load Tolk.dll: {error}");
                    CleanupTolk();
                    return false;
                }

                tolkLoad = NativeLibraryLoader.GetFunction<Tolk_LoadDelegate>(tolkHandle, "Tolk_Load");
                tolkUnload = NativeLibraryLoader.GetFunction<Tolk_UnloadDelegate>(tolkHandle, "Tolk_Unload");
                tolkIsLoaded = NativeLibraryLoader.GetFunction<Tolk_IsLoadedDelegate>(tolkHandle, "Tolk_IsLoaded");
                tolkOutput = NativeLibraryLoader.GetFunction<Tolk_OutputDelegate>(tolkHandle, "Tolk_Output");
                tolkDetectScreenReader = NativeLibraryLoader.GetFunction<Tolk_DetectScreenReaderDelegate>(tolkHandle, "Tolk_DetectScreenReader");
                tolkHasSpeech = NativeLibraryLoader.GetFunction<Tolk_HasSpeechDelegate>(tolkHandle, "Tolk_HasSpeech");
                tolkHasBraille = NativeLibraryLoader.GetFunction<Tolk_HasBrailleDelegate>(tolkHandle, "Tolk_HasBraille");
                tolkTrySAPI = NativeLibraryLoader.GetFunction<Tolk_TrySAPIDelegate>(tolkHandle, "Tolk_TrySAPI");

                bool nvdaRunning = false;
                if (nvdaTestIfRunning != null)
                {
                    try
                    {
                        int nvdaResult = nvdaTestIfRunning();
                        nvdaRunning = (nvdaResult == 0);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[RimWorld Access] Could not test NVDA directly: {ex.Message}");
                    }
                }

                tolkLoad();
                tolkTrySAPI(true);

                if (!tolkIsLoaded())
                {
                    Log.Warning("[RimWorld Access] Tolk loaded but no screen reader detected");
                    CleanupTolk();
                    return false;
                }

                useTolk = true;

                IntPtr namePtr = tolkDetectScreenReader();
                string screenReaderName = namePtr != IntPtr.Zero
                    ? Marshal.PtrToStringUni(namePtr)
                    : "Unknown";
                bool hasSpeech = tolkHasSpeech();
                bool hasBraille = tolkHasBraille();

                activeBackendName = "Tolk/" + screenReaderName;
                speechSupported = hasSpeech;
                brailleSupported = hasBraille;

                // Tolk sometimes reports SAPI while NVDA is running; talk to NVDA directly then.
                if (screenReaderName == "SAPI" && nvdaRunning)
                {
                    Log.Warning("[RimWorld Access] Tolk fell back to SAPI even though NVDA is running; switching to direct NVDA communication.");
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

        /// <summary>Cleans up Tolk resources after a failed initialization attempt.</summary>
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

        /// <summary>Initializes the Prism screen reader library, the default backend.</summary>
        private static void InitializePrism()
        {
            string modAssemblyPath = Assembly.GetExecutingAssembly().Location;
            string assemblyFolder = Path.GetDirectoryName(modAssemblyPath);

            string modRoot = Path.GetFullPath(Path.Combine(assemblyFolder, ".."));

            string libraryName = NativeLibraryLoader.GetNativeLibraryName("prism");
            string libraryPath = Path.Combine(modRoot, libraryName);

            // Must precede the load below; see SimdutfKernelWorkaround.Apply.
            string pinnedKernel = SimdutfKernelWorkaround.Apply(NativeLibraryLoader.IsWindows);
            if (pinnedKernel != null)
            {
                Log.Message($"[RimWorld Access] AVX-512 CPU detected; set {pinnedKernel}, avoiding Prism's faulty UTF-8 validation kernel");
            }

            if (!File.Exists(libraryPath))
            {
                Log.Error($"[RimWorld Access] {libraryName} not found at: {libraryPath}");
                throw new DllNotFoundException($"{libraryName} not found at: {libraryPath}");
            }

            prismLibraryHandle = NativeLibraryLoader.LoadLibrary(libraryPath);
            if (prismLibraryHandle == IntPtr.Zero)
            {
                string error = NativeLibraryLoader.GetLastError();
                foreach (string line in NativeLibraryLoader.GetLoadFailureDiagnostics(libraryPath))
                {
                    Log.Error($"[RimWorld Access] {line}");
                }
                string ldLibraryPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
                if (!string.IsNullOrEmpty(ldLibraryPath))
                {
                    Log.Message($"[RimWorld Access] LD_LIBRARY_PATH: {ldLibraryPath}");
                }
                throw new DllNotFoundException($"Failed to load {libraryName}: {error}");
            }

            PrismNative.LoadFunctions(prismLibraryHandle);

            PrismConfig config = PrismNative.prism_config_init();

            IntPtr registry = BuildRegistryWithMacaw();
            config.registry = registry;
            prismContext = PrismNative.prism_init(ref config);
            if (registry != IntPtr.Zero)
            {
                // prism_init retains the registry; this drops our own reference.
                PrismNative.prism_registry_release?.Invoke(registry);
            }
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

            PrismError initResult = PrismNative.prism_backend_initialize(prismBackend);
            if (initResult != PrismError.Ok && initResult != PrismError.AlreadyInitialized)
            {
                string errorMsg = PrismNative.GetErrorString(initResult);
                throw new Exception($"Backend initialization failed: {errorMsg}");
            }

            isInitialized = true;

            activeBackendName = PrismNative.ReadUtf8(PrismNative.prism_backend_name(prismBackend)) ?? "Unknown";
            ulong features = PrismNative.prism_backend_get_features(prismBackend);
            PrismBackendFeature featureFlags = (PrismBackendFeature)features;

            speechSupported = featureFlags.HasFlag(PrismBackendFeature.SupportsSpeak);
            brailleSupported = featureFlags.HasFlag(PrismBackendFeature.SupportsBraille);
        }

        /// <summary>
        /// Prism's own backends plus Macaw, from the plugin the macOS reader links at a fixed
        /// path; it outranks every reader Prism ships and reports itself unsupported while
        /// Macaw is not running. IntPtr.Zero leaves Prism its default registry.
        /// </summary>
        private static IntPtr BuildRegistryWithMacaw()
        {
            if (!NativeLibraryLoader.IsMacOS
                || PrismNative.prism_registry_builder_new == null
                || PrismNative.prism_registry_builder_add_library == null
                || PrismNative.prism_registry_freeze == null)
            {
                return IntPtr.Zero;
            }

            string pluginPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library/Application Support/Macaw/prism/libMacawPrismPlugin.dylib");
            if (!File.Exists(pluginPath))
            {
                return IntPtr.Zero;
            }

            IntPtr builder = PrismNative.prism_registry_builder_new();
            if (builder == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var (pathHandle, pathPointer) = PrismNative.MarshalUtf8(pluginPath);
            PrismError loaded;
            try
            {
                loaded = PrismNative.prism_registry_builder_add_library(builder, pathPointer, -1, IntPtr.Zero);
            }
            finally
            {
                PrismNative.FreeUtf8(pathHandle);
            }

            // A failed load leaves the builder untouched: the frozen registry is the default set.
            if (loaded != PrismError.Ok)
            {
                Log.Warning($"[RimWorld Access] Macaw Prism plugin not loaded: {PrismNative.GetErrorString(loaded)}");
            }

            return PrismNative.prism_registry_freeze(builder);
        }

        /// <summary>Shuts down the screen reader library; call during mod cleanup.</summary>
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
                    return;
                }

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
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error shutting down screen reader: {ex.Message}");
            }
        }

        /// <summary>Whether the screen reader backend is initialized and available.</summary>
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
        /// True when the active backend does not interrupt speech on key press: macOS
        /// AVSpeechSynthesizer queues, while VoiceOver and the Windows screen readers handle
        /// interruption themselves.
        /// </summary>
        public static bool ShouldInterruptOnKeyPress =>
            isInitialized && !useTolk && activeBackendName == "AVSpeech";

        // Last results of the stop/speak hot paths. Failures are logged only on a change of
        // error state, so a dead backend cannot flood the log with one warning per utterance.
        private static PrismError lastStopError = PrismError.Ok;
        private static PrismError lastSpeakError = PrismError.Ok;

        /// <summary>Stops any playing speech, for backends that do not interrupt on key press themselves.</summary>
        public static void StopSpeech()
        {
            if (!isInitialized || useTolk)
                return;

            try
            {
                if (PrismNative.prism_backend_stop == null)
                    return;

                PrismError result = PrismNative.prism_backend_stop(prismBackend);
                if (result != lastStopError)
                {
                    if (result != PrismError.Ok)
                    {
                        Log.Warning($"[RimWorld Access] Stopping speech failed: {PrismNative.GetErrorString(result)}");
                    }
                    lastStopError = result;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error stopping speech: {ex.Message}");
            }
        }

        /// <summary>
        /// Speaks a localized string — the preferred entry point. <see cref="Localized"/> can only
        /// be produced via <c>.Loc()</c>, so the compiler guarantees the text is translatable.
        /// </summary>
        public static void Speak(Localized text, SpeechPriority priority = SpeechPriority.Normal)
        {
            SpeakInternal(text.SpokenText, priority);
        }

        /// <summary>
        /// Speaks text that is intentionally not a translation key — numbers, proper names, or
        /// labels the game already localized.
        /// </summary>
        public static void SpeakData(string text, SpeechPriority priority = SpeechPriority.Normal)
        {
            SpeakInternal(text, priority);
        }

        /// <summary>Managed thread id captured by <see cref="Initialize"/>. -1 until then.</summary>
        private static int mainThreadManagedId = -1;

        /// <summary>Frame <see cref="spokenThisFrame"/> belongs to; -1 before any frame.</summary>
        private static int dedupeFrame = -1;

        /// <summary>
        /// Every distinct sanitized utterance already spoken during <see cref="dedupeFrame"/>, so
        /// same-frame repeats can be dropped: those are replacement-churn within one IMGUI pass,
        /// while a real repeat lands on a later frame. Past
        /// <see cref="MaxDedupeEntriesPerFrame"/> dedupe is skipped rather than the list grown.
        /// </summary>
        private static readonly System.Collections.Generic.List<string> spokenThisFrame =
            new System.Collections.Generic.List<string>();

        private const int MaxDedupeEntriesPerFrame = 64;

        /// <summary>Core speech implementation shared by all public Speak overloads.</summary>
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

            text = SpeechSanitizer.Sanitize(text);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            // Dedupe runs before any output, capture buffer, ring, or QA-trace recording: a
            // dropped duplicate was never spoken, so nothing may see it as having happened. Off
            // the main thread it is skipped rather than touching Time.frameCount.
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == mainThreadManagedId)
            {
                int frame = UnityEngine.Time.frameCount;
                if (frame != dedupeFrame)
                {
                    dedupeFrame = frame;
                    spokenThisFrame.Clear();
                }

                // Membership, not a last-utterance comparison: an interleaved A, B, A, B within
                // one frame must still catch both repeats.
                if (spokenThisFrame.Contains(text))
                {
                    // Suppression hides real defects, so every drop is visible in the
                    // diagnostic channels rather than trusted blind.
#if DEBUG
                    Log.Message("[RimWorld Access] Speech dropped (same-frame duplicate, frame " + frame + "): " + text);
#endif
                    RimWorldAccess.Shell.FlightRecorder.Record("speech-dropped", "same-frame duplicate, frame " + frame + ": " + text);
                    return;
                }

                if (spokenThisFrame.Count < MaxDedupeEntriesPerFrame)
                {
                    spokenThisFrame.Add(text);
                }
            }

#if DEBUG
            // Observe-only: speech must still reach the real backend below.
            if (captureBuffer != null)
            {
                captureBuffer.Add(text);
            }
            // The rolling ring is always recording, independent of the capture buffer, so
            // ShellDev.RecentSpeech can inspect what was said without a caller having bracketed it.
            RecordToSpeechRing(text);
#endif
            RimWorldAccess.Shell.FlightRecorder.Record("speech", text);

            try
            {
                bool interrupt = priority == SpeechPriority.High;

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

                if (PrismNative.prism_backend_output == null)
                {
                    Log.Warning("[RimWorld Access] Speak called but Prism is not initialized");
                    return;
                }

                var (handle, pointer) = PrismNative.MarshalUtf8(text);
                try
                {
                    PrismError result = PrismNative.prism_backend_output(prismBackend, pointer, interrupt);
                    if (result == PrismError.NotImplemented && PrismNative.prism_backend_speak != null)
                    {
                        result = PrismNative.prism_backend_speak(prismBackend, pointer, interrupt);
                    }
                    if (result != lastSpeakError)
                    {
                        if (result != PrismError.Ok)
                        {
                            Log.Warning($"[RimWorld Access] Speech output failed: {PrismNative.GetErrorString(result)} (text: {DescribeForLog(text)})");
                        }
                        else
                        {
                            Log.Message("[RimWorld Access] Speech output recovered.");
                        }
                        lastSpeakError = result;
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

        private static readonly object scrubReportLock = new object();
        private static readonly System.Collections.Generic.HashSet<string> reportedScrubbedText = new System.Collections.Generic.HashSet<string>();
        private const int MaxScrubReports = 20;
        private static bool scrubReportsSuppressed = false;

        /// <summary>
        /// Logs invalid speech text a scrub site had to clean (control characters, lone
        /// surrogates) so the culprit mod is identifiable. Once per distinct original string,
        /// capped per app run.
        /// </summary>
        internal static void ReportScrubbedSpeech(string reason, string original)
        {
            lock (scrubReportLock)
            {
                if (scrubReportsSuppressed || !reportedScrubbedText.Add(original))
                {
                    return;
                }
                if (reportedScrubbedText.Count > MaxScrubReports)
                {
                    scrubReportsSuppressed = true;
                    Log.Warning("[RimWorld Access] Further scrubbed-speech reports suppressed.");
                    return;
                }
                Log.Warning("[RimWorld Access] Scrubbed " + reason + " from speech text: " + DescribeForLog(original));
            }
        }

        /// <summary>
        /// Renders an utterance for a diagnostic log line, escaping non-ASCII and control
        /// characters so the exact bytes survive a player's pasted log.
        /// </summary>
        private static string DescribeForLog(string text)
        {
            const int maxChars = 160;
            var sb = new System.Text.StringBuilder(System.Math.Min(text.Length, maxChars) + 16);
            sb.Append('"');
            for (int i = 0; i < text.Length && i < maxChars; i++)
            {
                char c = text[i];
                if (c >= ' ' && c < '\x7F')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append("\\u").Append(((int)c).ToString("X4"));
                }
            }
            sb.Append('"');
            if (text.Length > maxChars)
            {
                sb.Append("… (").Append(text.Length).Append(" chars)");
            }
            return sb.ToString();
        }

#if DEBUG
        #region Dev-bridge capture sink

        /// <summary>Capture buffer; null doubles as the "not capturing" state.</summary>
        private static System.Collections.Generic.List<string> captureBuffer;

        /// <summary>True while a dev-bridge caller is capturing speech.</summary>
        public static bool IsCapturing => captureBuffer != null;

        /// <summary>
        /// Starts capturing every spoken utterance, discarding any buffer left by an unended
        /// capture. Captured lines are the SANITIZED text. Observe-only: capture never suppresses
        /// or alters speech.
        /// </summary>
        public static void BeginCapture()
        {
            captureBuffer = new System.Collections.Generic.List<string>();
        }

        /// <summary>
        /// Stops capturing and returns every line recorded since <see cref="BeginCapture"/>, in the
        /// order spoken; empty when no capture was in progress.
        /// </summary>
        public static string[] EndCapture()
        {
            if (captureBuffer == null)
            {
                return new string[0];
            }

            string[] captured = captureBuffer.ToArray();
            captureBuffer = null;
            return captured;
        }

        #endregion

        #region Dev-bridge speech ring (rolling utterance history)

        /// <summary>
        /// One recorded utterance: monotonic sequence number, Unity frame count,
        /// realtime-since-startup, and the sanitized text.
        /// </summary>
        internal readonly struct SpeechRingEntry
        {
            public readonly int Seq;
            public readonly int Frame;
            public readonly float RealtimeSinceStartup;
            public readonly string Text;

            public SpeechRingEntry(int seq, int frame, float realtimeSinceStartup, string text)
            {
                Seq = seq;
                Frame = frame;
                RealtimeSinceStartup = realtimeSinceStartup;
                Text = text;
            }
        }

        private const int SpeechRingCapacity = 200;
        private static readonly SpeechRingEntry[] speechRing = new SpeechRingEntry[SpeechRingCapacity];
        private static int speechRingCount;
        private static int speechRingNext;
        private static int speechRingTotalRecorded;

        /// <summary>Records one utterance into the rolling ring, independent of the capture sink.</summary>
        private static void RecordToSpeechRing(string text)
        {
            speechRingTotalRecorded++;
            speechRing[speechRingNext] = new SpeechRingEntry(
                speechRingTotalRecorded, UnityEngine.Time.frameCount, UnityEngine.Time.realtimeSinceStartup, text);
            speechRingNext = (speechRingNext + 1) % SpeechRingCapacity;
            if (speechRingCount < SpeechRingCapacity)
            {
                speechRingCount++;
            }
        }

        /// <summary>Every entry currently retained, oldest first (newest last).</summary>
        internal static SpeechRingEntry[] SpeechRingSnapshot()
        {
            var result = new SpeechRingEntry[speechRingCount];
            int start = speechRingCount < SpeechRingCapacity ? 0 : speechRingNext;
            for (int i = 0; i < speechRingCount; i++)
            {
                result[i] = speechRing[(start + i) % SpeechRingCapacity];
            }
            return result;
        }

        /// <summary>
        /// Monotonic count of every utterance ever recorded. Survives
        /// <see cref="ClearSpeechRing"/>, so a stamp taken before a clear stays meaningful.
        /// </summary>
        internal static int SpeechRingTotalRecorded
        {
            get { return speechRingTotalRecorded; }
        }

        /// <summary>Drops every retained entry; the monotonic counter above is untouched.</summary>
        internal static void ClearSpeechRing()
        {
            speechRingCount = 0;
            speechRingNext = 0;
        }

        #endregion
#endif
    }
}
