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
        private const int RTLD_LAZY = 1;
        private const int RTLD_NOW = 2;

        #endregion

        // dlerror message captured immediately after a failed dlopen; libc's
        // per-thread dlerror state is reset by any successful dl call, and
        // Mono resolving a lazy P/Invoke import performs its own dl calls, so
        // reading dlerror later (e.g. from GetLastError) returns nothing.
        private static string lastUnixLoadError;

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
                // Force Mono to resolve the dlerror import (and clear stale
                // state) before dlopen, so the read below can't be wiped by
                // import resolution.
                unix_dlerror();
                IntPtr handle = unix_dlopen(path, RTLD_NOW);
                if (handle == IntPtr.Zero)
                {
                    IntPtr error = unix_dlerror();
                    lastUnixLoadError = error != IntPtr.Zero
                        ? Marshal.PtrToStringAnsi(error)
                        : null;
                }
                return handle;
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
        /// Diagnoses a failed library load on Linux by probing each of the
        /// library's declared dependencies (DT_NEEDED) individually. Returns
        /// one line per finding; empty when no problems are found or the
        /// format is not ELF (e.g. on macOS).
        /// </summary>
        public static System.Collections.Generic.List<string> GetLoadFailureDiagnostics(string path)
        {
            var lines = new System.Collections.Generic.List<string>();
            if (IsWindows)
            {
                return lines;
            }

            try
            {
                foreach (string needed in ReadElfNeededEntries(path))
                {
                    IntPtr probe = unix_dlopen(needed, RTLD_LAZY);
                    if (probe != IntPtr.Zero)
                    {
                        unix_dlclose(probe);
                        continue;
                    }
                    IntPtr error = unix_dlerror();
                    string reason = error != IntPtr.Zero
                        ? Marshal.PtrToStringAnsi(error)
                        : "unknown reason";
                    lines.Add($"Missing dependency: {needed} ({reason})");
                }
            }
            catch (Exception ex)
            {
                lines.Add($"Dependency scan failed: {ex.Message}");
            }
            return lines;
        }

        /// <summary>
        /// Reads the DT_NEEDED (required shared library) names from an ELF
        /// file's .dynamic section. Returns an empty list for non-ELF files.
        /// </summary>
        private static System.Collections.Generic.List<string> ReadElfNeededEntries(string path)
        {
            var needed = new System.Collections.Generic.List<string>();
            byte[] data = File.ReadAllBytes(path);

            // ELF magic + 64-bit little-endian class; anything else is not a
            // file we know how to scan.
            if (data.Length < 0x40 ||
                data[0] != 0x7F || data[1] != (byte)'E' || data[2] != (byte)'L' || data[3] != (byte)'F' ||
                data[4] != 2 || data[5] != 1)
            {
                return needed;
            }

            long shoff = BitConverter.ToInt64(data, 0x28);
            int shentsize = BitConverter.ToUInt16(data, 0x3A);
            int shnum = BitConverter.ToUInt16(data, 0x3C);

            long dynamicOff = 0, dynamicSize = 0, dynstrOff = 0;
            for (int i = 0; i < shnum; i++)
            {
                long off = shoff + (long)i * shentsize;
                uint type = BitConverter.ToUInt32(data, (int)off + 0x04);
                long sectionOff = BitConverter.ToInt64(data, (int)off + 0x18);
                long sectionSize = BitConverter.ToInt64(data, (int)off + 0x20);
                if (type == 6) // SHT_DYNAMIC
                {
                    dynamicOff = sectionOff;
                    dynamicSize = sectionSize;
                    // sh_link points at the section holding the dynamic strings
                    uint link = BitConverter.ToUInt32(data, (int)off + 0x28);
                    long linkHdr = shoff + (long)link * shentsize;
                    dynstrOff = BitConverter.ToInt64(data, (int)linkHdr + 0x18);
                }
            }
            if (dynamicOff == 0 || dynstrOff == 0)
            {
                return needed;
            }

            for (long off = dynamicOff; off < dynamicOff + dynamicSize; off += 16)
            {
                long tag = BitConverter.ToInt64(data, (int)off);
                long val = BitConverter.ToInt64(data, (int)off + 8);
                if (tag == 0) // DT_NULL: end of table
                {
                    break;
                }
                if (tag == 1) // DT_NEEDED: val is an offset into the string table
                {
                    long strOff = dynstrOff + val;
                    int end = (int)strOff;
                    while (end < data.Length && data[end] != 0)
                    {
                        end++;
                    }
                    needed.Add(System.Text.Encoding.ASCII.GetString(data, (int)strOff, end - (int)strOff));
                }
            }
            return needed;
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
                if (lastUnixLoadError != null)
                {
                    string captured = lastUnixLoadError;
                    lastUnixLoadError = null;
                    return captured;
                }
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
