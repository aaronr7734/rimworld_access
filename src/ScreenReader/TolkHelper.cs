using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Speech priority levels for Tolk screen reader output.
    /// </summary>
    public enum SpeechPriority
    {
        Low,      // Don't interrupt (navigation)
        Normal,   // Interrupt low priority
        High      // Interrupt everything (errors, critical info)
    }

    /// <summary>
    /// Wrapper for the Tolk screen reader library.
    /// Provides direct screen reader integration via native API calls.
    /// </summary>
    public static class TolkHelper
    {
        #region P/Invoke Declarations

        // Windows kernel32 functions for explicit DLL loading
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string lpLibFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hLibModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        // Tolk function delegates for manual binding
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

        // NVDA Controller Client delegates
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int nvdaController_testIfRunningDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate int nvdaController_speakTextDelegate([MarshalAs(UnmanagedType.LPWStr)] string text);

        #endregion

        // Library handles
        private static IntPtr tolkHandle = IntPtr.Zero;
        private static IntPtr nvdaHandle = IntPtr.Zero;

        // Function pointers
        private static Tolk_LoadDelegate Tolk_Load;
        private static Tolk_UnloadDelegate Tolk_Unload;
        private static Tolk_IsLoadedDelegate Tolk_IsLoaded;
        private static Tolk_OutputDelegate Tolk_Output;
        private static Tolk_DetectScreenReaderDelegate Tolk_DetectScreenReader;
        private static Tolk_HasSpeechDelegate Tolk_HasSpeech;
        private static Tolk_HasBrailleDelegate Tolk_HasBraille;
        private static Tolk_TrySAPIDelegate Tolk_TrySAPI;
        private static nvdaController_testIfRunningDelegate nvdaController_testIfRunning;
        private static nvdaController_speakTextDelegate nvdaController_speakText;

        private static bool isInitialized = false;
        private static bool useDirectNVDA = false;

        /// <summary>
        /// Gets a delegate for a function from a loaded library.
        /// </summary>
        private static T GetFunction<T>(IntPtr library, string functionName) where T : Delegate
        {
            IntPtr procAddress = GetProcAddress(library, functionName);
            if (procAddress == IntPtr.Zero)
            {
                throw new Exception($"Could not find function '{functionName}' in library");
            }
            return Marshal.GetDelegateForFunctionPointer<T>(procAddress);
        }

        /// <summary>
        /// Initializes the Tolk screen reader library.
        /// Must be called before any other Tolk operations.
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
                // Native DLLs are in: Mods/RimWorldAccess/Tolk.dll
                string modAssemblyPath = Assembly.GetExecutingAssembly().Location;
                string assemblyFolder = Path.GetDirectoryName(modAssemblyPath);

                // Go up from Assemblies to mod root (one level up)
                string modRoot = Path.GetFullPath(Path.Combine(assemblyFolder, ".."));

                string tolkPath = Path.Combine(modRoot, "Tolk.dll");
                string nvdaPath = Path.Combine(modRoot, "nvdaControllerClient64.dll");

                Log.Message($"[RimWorld Access] Loading native DLLs from: {modRoot}");

                // Check if DLLs exist
                if (!File.Exists(tolkPath))
                {
                    Log.Error($"[RimWorld Access] Tolk.dll not found at: {tolkPath}");
                    throw new DllNotFoundException($"Tolk.dll not found at: {tolkPath}");
                }

                if (!File.Exists(nvdaPath))
                {
                    Log.Warning($"[RimWorld Access] nvdaControllerClient64.dll not found at: {nvdaPath}");
                    // Continue without NVDA direct support
                }

                // Load NVDA controller first (Tolk may depend on it being available)
                if (File.Exists(nvdaPath))
                {
                    nvdaHandle = LoadLibraryW(nvdaPath);
                    if (nvdaHandle == IntPtr.Zero)
                    {
                        int error = Marshal.GetLastWin32Error();
                        Log.Warning($"[RimWorld Access] Failed to load nvdaControllerClient64.dll (error {error})");
                    }
                    else
                    {
                        Log.Message("[RimWorld Access] Loaded nvdaControllerClient64.dll successfully");
                        // Get NVDA function pointers
                        try
                        {
                            nvdaController_testIfRunning = GetFunction<nvdaController_testIfRunningDelegate>(nvdaHandle, "nvdaController_testIfRunning");
                            nvdaController_speakText = GetFunction<nvdaController_speakTextDelegate>(nvdaHandle, "nvdaController_speakText");
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[RimWorld Access] Failed to get NVDA function pointers: {ex.Message}");
                        }
                    }
                }

                // Load Tolk
                tolkHandle = LoadLibraryW(tolkPath);
                if (tolkHandle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new DllNotFoundException($"Failed to load Tolk.dll (error code: {error})");
                }

                Log.Message("[RimWorld Access] Loaded Tolk.dll successfully");

                // Get Tolk function pointers
                Tolk_Load = GetFunction<Tolk_LoadDelegate>(tolkHandle, "Tolk_Load");
                Tolk_Unload = GetFunction<Tolk_UnloadDelegate>(tolkHandle, "Tolk_Unload");
                Tolk_IsLoaded = GetFunction<Tolk_IsLoadedDelegate>(tolkHandle, "Tolk_IsLoaded");
                Tolk_Output = GetFunction<Tolk_OutputDelegate>(tolkHandle, "Tolk_Output");
                Tolk_DetectScreenReader = GetFunction<Tolk_DetectScreenReaderDelegate>(tolkHandle, "Tolk_DetectScreenReader");
                Tolk_HasSpeech = GetFunction<Tolk_HasSpeechDelegate>(tolkHandle, "Tolk_HasSpeech");
                Tolk_HasBraille = GetFunction<Tolk_HasBrailleDelegate>(tolkHandle, "Tolk_HasBraille");
                Tolk_TrySAPI = GetFunction<Tolk_TrySAPIDelegate>(tolkHandle, "Tolk_TrySAPI");

                // Test NVDA directly first
                bool nvdaRunning = false;
                if (nvdaController_testIfRunning != null)
                {
                    try
                    {
                        int nvdaResult = nvdaController_testIfRunning();
                        nvdaRunning = (nvdaResult == 0);
                        Log.Message($"[RimWorld Access] Direct NVDA test: {(nvdaRunning ? "NVDA is running" : $"NVDA not detected (code: {nvdaResult})")}");
                    }
                    catch (Exception nvdaEx)
                    {
                        Log.Warning($"[RimWorld Access] Could not test NVDA directly: {nvdaEx.Message}");
                    }
                }

                // Load Tolk - it will try screen readers first
                Tolk_Load();

                // Enable SAPI fallback AFTER loading (only if no screen reader found)
                Tolk_TrySAPI(true);

                isInitialized = true;

                if (Tolk_IsLoaded())
                {
                    // Get screen reader name
                    IntPtr namePtr = Tolk_DetectScreenReader();
                    string screenReaderName = namePtr != IntPtr.Zero
                        ? Marshal.PtrToStringUni(namePtr)
                        : "Unknown";

                    bool hasSpeech = Tolk_HasSpeech();
                    bool hasBraille = Tolk_HasBraille();

                    Log.Message("[RimWorld Access] Tolk screen reader integration initialized successfully.");
                    Log.Message($"[RimWorld Access] Detected screen reader: {screenReaderName}");
                    Log.Message($"[RimWorld Access] Speech support: {hasSpeech}");
                    Log.Message($"[RimWorld Access] Braille support: {hasBraille}");

                    // If Tolk detected SAPI but we know NVDA is running, use direct NVDA communication
                    if (screenReaderName == "SAPI" && nvdaRunning)
                    {
                        Log.Warning("[RimWorld Access] Tolk fell back to SAPI even though NVDA is running.");
                        Log.Message("[RimWorld Access] Switching to direct NVDA communication mode.");
                        useDirectNVDA = true;
                    }
                }
                else
                {
                    Log.Warning("[RimWorld Access] Tolk initialized but no screen reader detected.");
                    Log.Warning("[RimWorld Access] Make sure a screen reader (NVDA, JAWS, etc.) is running before starting RimWorld.");
                }
            }
            catch (DllNotFoundException ex)
            {
                Log.Error($"[RimWorld Access] Failed to load native DLL: {ex.Message}");
                Log.Error("[RimWorld Access] Ensure Tolk.dll is in the mod's root folder (Mods/RimWorldAccess/)");
                throw;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Failed to initialize Tolk: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Shuts down the Tolk screen reader library.
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
                Tolk_Unload?.Invoke();
                isInitialized = false;

                // Free libraries
                if (tolkHandle != IntPtr.Zero)
                {
                    FreeLibrary(tolkHandle);
                    tolkHandle = IntPtr.Zero;
                }

                if (nvdaHandle != IntPtr.Zero)
                {
                    FreeLibrary(nvdaHandle);
                    nvdaHandle = IntPtr.Zero;
                }

                // Clear function pointers
                Tolk_Load = null;
                Tolk_Unload = null;
                Tolk_IsLoaded = null;
                Tolk_Output = null;
                Tolk_DetectScreenReader = null;
                Tolk_HasSpeech = null;
                Tolk_HasBraille = null;
                Tolk_TrySAPI = null;
                nvdaController_testIfRunning = null;
                nvdaController_speakText = null;

                Log.Message("[RimWorld Access] Tolk screen reader integration shut down.");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error shutting down Tolk: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if Tolk is initialized and a screen reader is detected.
        /// </summary>
        public static bool IsActive()
        {
            if (!isInitialized || Tolk_IsLoaded == null)
            {
                return false;
            }

            try
            {
                return Tolk_IsLoaded();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error checking Tolk status: {ex.Message}");
                return false;
            }
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

            if (!isInitialized || Tolk_Output == null)
            {
                Log.Warning("[RimWorld Access] Tolk.Speak called but Tolk is not initialized");
                return;
            }

            try
            {
                // If we're in direct NVDA mode, bypass Tolk
                if (useDirectNVDA && nvdaController_speakText != null)
                {
                    try
                    {
                        nvdaController_speakText(text);
                        return;
                    }
                    catch (Exception nvdaEx)
                    {
                        Log.Warning($"[RimWorld Access] Direct NVDA communication failed: {nvdaEx.Message}, falling back to Tolk");
                        useDirectNVDA = false; // Disable for future calls
                    }
                }

                // Determine interrupt behavior based on priority
                bool interrupt = priority == SpeechPriority.High;

                // Use Tolk_Output which handles both speech and braille
                Tolk_Output(text, interrupt);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error speaking text via Tolk: {ex.Message}");
            }
        }
    }
}
