#if DEBUG
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace RimWorldAccess.DevBridge
{
    /// <summary>
    /// Compiles and executes arbitrary C# script text against the live game using Roslyn
    /// scripting. The script body runs with the game's assemblies referenced and common
    /// namespaces imported, so it can call <c>Find.*</c>, <c>DefDatabase&lt;T&gt;</c>,
    /// <c>"key".Translate()</c>, etc. directly. The trailing expression's value is returned.
    ///
    /// MUST be invoked on the main thread (see <see cref="MainThreadDispatcher"/>) because the
    /// script touches non-thread-safe game state. Compilation also happens here, briefly
    /// blocking the frame - acceptable for a dev-only tool.
    /// </summary>
    internal static class RoslynEvaluator
    {
        private static ScriptOptions options;
        private static bool initialized;
        private static string initError;

        private static void EnsureInit()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                // Reference every loaded assembly that lives on disk. This pulls in
                // Assembly-CSharp (RimWorld), the Unity assemblies, Harmony, and our own
                // mod DLL, so scripts can reach anything the running game can.
                var references = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .Where(a =>
                    {
                        try { return !string.IsNullOrEmpty(a.Location); }
                        catch { return false; }
                    })
                    .ToList();

                options = ScriptOptions.Default
                    .WithReferences(references)
                    .WithImports(
                        "System",
                        "System.Linq",
                        "System.Collections",
                        "System.Collections.Generic",
                        "System.Text",
                        "Verse",
                        "RimWorld",
                        "RimWorld.Planet",
                        "UnityEngine",
                        "RimWorldAccess");
            }
            catch (Exception e)
            {
                initError = e.ToString();
            }
        }

        internal static bool IsReady
        {
            get { EnsureInit(); return options != null; }
        }

        internal static string InitError
        {
            get { EnsureInit(); return initError; }
        }

        /// <summary>
        /// Evaluate a C# script. Returns a result string prefixed with "OK\n" on success or
        /// "ERROR\n" on failure (compile or runtime). Any text the script writes to
        /// Console.Out/Error during evaluation is captured and included.
        /// </summary>
        internal static string Eval(string code)
        {
            EnsureInit();
            if (options == null)
                return "ERROR\nRoslyn failed to initialize:\n" + initError;

            var captured = new StringWriter();
            TextWriter prevOut = Console.Out;
            TextWriter prevErr = Console.Error;
            try
            {
                Console.SetOut(captured);
                Console.SetError(captured);

                object result = CSharpScript.EvaluateAsync(code, options)
                    .GetAwaiter().GetResult();

                var sb = new StringBuilder("OK\n");
                string output = captured.ToString();
                if (output.Length > 0)
                    sb.Append(output).Append(output.EndsWith("\n") ? "" : "\n");
                sb.Append(FormatResult(result));
                return sb.ToString();
            }
            catch (CompilationErrorException ce)
            {
                return "ERROR (compile)\n" + string.Join("\n", ce.Diagnostics.Select(d => d.ToString()));
            }
            catch (Exception e)
            {
                string output = captured.ToString();
                return "ERROR\n" + (output.Length > 0 ? output + "\n" : "") + e;
            }
            finally
            {
                Console.SetOut(prevOut);
                Console.SetError(prevErr);
            }
        }

        /// <summary>
        /// Render a result value for the HTTP response. Enumerables (other than strings) are
        /// expanded one-per-line and capped so a huge DefDatabase dump can't flood the wire.
        /// </summary>
        private static string FormatResult(object result)
        {
            if (result == null)
                return "(null)";

            if (result is string s)
                return s;

            if (result is IEnumerable en && !(result is IDictionary))
            {
                const int cap = 500;
                var sb = new StringBuilder();
                int count = 0;
                foreach (object item in en)
                {
                    if (count >= cap)
                    {
                        sb.Append("... (truncated at ").Append(cap).Append(" items)\n");
                        break;
                    }
                    sb.Append(item == null ? "(null)" : item.ToString()).Append('\n');
                    count++;
                }
                if (count == 0)
                    return "(empty sequence)";
                return sb.ToString().TrimEnd('\n');
            }

            return result.ToString();
        }
    }
}
#endif
