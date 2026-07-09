using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RimWorldAccess
{
    /// <summary>
    /// Cross-platform native library loader.
    /// Uses LoadLibraryW/GetProcAddress on Windows, dlopen/dlsym on macOS and Linux.
    /// </summary>
    public static class NativeLibraryLoader
    {
        #region Platform Detection

        private static readonly PlatformType currentPlatform = DetectPlatform();

        public static bool IsWindows => currentPlatform == PlatformType.Windows;
        public static bool IsMacOS => currentPlatform == PlatformType.MacOS;
        public static bool IsLinux => currentPlatform == PlatformType.Linux;

        private enum PlatformType
        {
            Windows,
            MacOS,
            Linux
        }

        private static PlatformType DetectPlatform()
        {
            var platform = Environment.OSVersion.Platform;

            if (platform == PlatformID.Win32NT || platform == PlatformID.Win32S ||
                platform == PlatformID.Win32Windows || platform == PlatformID.WinCE)
            {
                return PlatformType.Windows;
            }

            // PlatformID.Unix covers both Linux and macOS on Mono
            // PlatformID 6 (128) is also used by older Mono for Unix
            if (platform == PlatformID.Unix || platform == PlatformID.MacOSX || (int)platform == 128)
            {
                // Distinguish macOS from Linux
                if (Directory.Exists("/System/Library") && Directory.Exists("/Applications"))
                {
                    return PlatformType.MacOS;
                }
                return PlatformType.Linux;
            }

            // Default to Windows (safest for RimWorld's primary platform)
            return PlatformType.Windows;
        }

        #endregion

        #region Windows P/Invoke

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string lpLibFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Win32FreeLibrary(IntPtr hLibModule);

        // Renamed to avoid conflict with our public method
        [DllImport("kernel32.dll", EntryPoint = "FreeLibrary", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Win32FreeLib(IntPtr hLibModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        #endregion

        #region Unix P/Invoke

        // On macOS, Mono resolves "dl" to libdl.dylib. On Linux, Mono probes
        // "dl"/"libdl.so"/"libdl", but glibc 2.34+ distros only ship the
        // versioned SONAME libdl.so.2 (the bare libdl.so symlink comes from
        // dev packages), so the versioned name must be tried first there.
        private static class Libdl
        {
            [DllImport("dl", EntryPoint = "dlopen")]
            internal static extern IntPtr dlopen(string filename, int flags);

            [DllImport("dl", EntryPoint = "dlsym")]
            internal static extern IntPtr dlsym(IntPtr handle, string symbol);

            [DllImport("dl", EntryPoint = "dlclose")]
            internal static extern int dlclose(IntPtr handle);

            [DllImport("dl", EntryPoint = "dlerror")]
            internal static extern IntPtr dlerror();
        }

        private static class Libdl2
        {
            [DllImport("libdl.so.2", EntryPoint = "dlopen")]
            internal static extern IntPtr dlopen(string filename, int flags);

            [DllImport("libdl.so.2", EntryPoint = "dlsym")]
            internal static extern IntPtr dlsym(IntPtr handle, string symbol);

            [DllImport("libdl.so.2", EntryPoint = "dlclose")]
            internal static extern int dlclose(IntPtr handle);

            [DllImport("libdl.so.2", EntryPoint = "dlerror")]
            internal static extern IntPtr dlerror();
        }

        private static bool useVersionedLibdl = IsLinux;

        private static IntPtr unix_dlopen(string filename, int flags)
        {
            if (useVersionedLibdl)
            {
                try { return Libdl2.dlopen(filename, flags); }
                catch (DllNotFoundException) { useVersionedLibdl = false; }
                catch (EntryPointNotFoundException) { useVersionedLibdl = false; }
            }
            return Libdl.dlopen(filename, flags);
        }

        private static IntPtr unix_dlsym(IntPtr handle, string symbol)
        {
            if (useVersionedLibdl)
            {
                try { return Libdl2.dlsym(handle, symbol); }
                catch (DllNotFoundException) { useVersionedLibdl = false; }
                catch (EntryPointNotFoundException) { useVersionedLibdl = false; }
            }
            return Libdl.dlsym(handle, symbol);
        }

        private static int unix_dlclose(IntPtr handle)
        {
            if (useVersionedLibdl)
            {
                try { return Libdl2.dlclose(handle); }
                catch (DllNotFoundException) { useVersionedLibdl = false; }
                catch (EntryPointNotFoundException) { useVersionedLibdl = false; }
            }
            return Libdl.dlclose(handle);
        }

        private static IntPtr unix_dlerror()
        {
            if (useVersionedLibdl)
            {
                try { return Libdl2.dlerror(); }
                catch (DllNotFoundException) { useVersionedLibdl = false; }
                catch (EntryPointNotFoundException) { useVersionedLibdl = false; }
            }
            return Libdl.dlerror();
        }

        // dlopen flags
        private const int RTLD_NOW = 2;

        #endregion

        /// <summary>
        /// Loads a native library from the specified path.
        /// </summary>
        /// <returns>Handle to the loaded library, or IntPtr.Zero on failure.</returns>
        public static IntPtr LoadLibrary(string path)
        {
            if (IsWindows)
            {
                return LoadLibraryW(path);
            }
            else
            {
                return unix_dlopen(path, RTLD_NOW);
            }
        }

        /// <summary>
        /// Gets a function pointer from a loaded library.
        /// </summary>
        /// <returns>Function pointer, or IntPtr.Zero if not found.</returns>
        public static IntPtr GetSymbol(IntPtr library, string symbolName)
        {
            if (IsWindows)
            {
                return GetProcAddress(library, symbolName);
            }
            else
            {
                // Clear any previous error
                unix_dlerror();
                IntPtr symbol = unix_dlsym(library, symbolName);
                // Check for error (symbol could legitimately be zero)
                IntPtr error = unix_dlerror();
                if (error != IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }
                return symbol;
            }
        }

        /// <summary>
        /// Frees a loaded native library.
        /// </summary>
        /// <returns>True if successful.</returns>
        public static bool FreeLibrary(IntPtr library)
        {
            if (library == IntPtr.Zero)
            {
                return false;
            }

            if (IsWindows)
            {
                return Win32FreeLib(library);
            }
            else
            {
                return unix_dlclose(library) == 0;
            }
        }

        /// <summary>
        /// Gets a delegate for a function from a loaded native library.
        /// </summary>
        /// <typeparam name="T">Delegate type for the function signature.</typeparam>
        /// <param name="library">Handle to the loaded library.</param>
        /// <param name="functionName">Name of the exported function.</param>
        /// <returns>Delegate bound to the native function.</returns>
        /// <exception cref="Exception">Thrown if the function is not found.</exception>
        public static T GetFunction<T>(IntPtr library, string functionName) where T : Delegate
        {
            IntPtr procAddress = GetSymbol(library, functionName);
            if (procAddress == IntPtr.Zero)
            {
                throw new Exception($"Could not find function '{functionName}' in native library");
            }
            return Marshal.GetDelegateForFunctionPointer<T>(procAddress);
        }

        /// <summary>
        /// Resolves the platform-specific native library filename.
        /// </summary>
        /// <param name="baseName">Base name without extension (e.g., "prism").</param>
        /// <returns>Platform-specific filename (e.g., "prism.dll", "libprism.dylib", "libprism.so").</returns>
        public static string GetNativeLibraryName(string baseName)
        {
            if (IsWindows)
            {
                return baseName + ".dll";
            }
            else if (IsMacOS)
            {
                return "lib" + baseName + ".dylib";
            }
            else
            {
                return "lib" + baseName + ".so";
            }
        }

        /// <summary>
        /// Gets the last native error message (platform-specific).
        /// </summary>
        public static string GetLastError()
        {
            if (IsWindows)
            {
                int errorCode = Marshal.GetLastWin32Error();
                return $"Win32 error code: {errorCode}"; // l10n-exempt: dev/native diagnostic, never user-facing speech
            }
            else
            {
                IntPtr error = unix_dlerror();
                if (error != IntPtr.Zero)
                {
                    return Marshal.PtrToStringAnsi(error) ?? "Unknown error";
                }
                return "No error"; // l10n-exempt: dev/native diagnostic, never user-facing speech
            }
        }
    }
}
