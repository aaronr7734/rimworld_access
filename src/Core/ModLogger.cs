using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Static logger for mod-wide logging using RimWorld's native logging system.
    /// </summary>
    public static class ModLogger
    {
        private const string Prefix = "[RimWorld Access] ";

        private const int MaxLogsPerContext = 10;
        private static readonly Dictionary<string, int> limitedErrorCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> limitedMessageCounts = new Dictionary<string, int>();

        /// <summary>Log a message to RimWorld's log.</summary>
        public static void Msg(string message)
        {
            Log.Message(Prefix + message);
        }

        /// <summary>Log a warning to RimWorld's log.</summary>
        public static void Warning(string message)
        {
            Log.Warning(Prefix + message);
        }

        /// <summary>
        /// Log an error to RimWorld's log.
        /// </summary>
        public static void Error(string message)
        {
            Log.Error(Prefix + message);
        }

        /// <summary>
        /// Log an exception, capped per context so a failure inside a per-frame or per-row
        /// path cannot flood the log.
        /// </summary>
        public static void LimitedError(string context, Exception ex)
        {
            if (WithinCap(limitedErrorCounts, context))
            {
                Log.Error(Prefix + context + ": " + ex);
            }
        }

        /// <summary>
        /// Log an exception the same way <see cref="LimitedError"/> does — same
        /// per-context cap, same text — but at message level, for a failure that
        /// is expected rather than a defect. Vanilla auto-opens the debug log
        /// window for any error logged before game data finishes loading, dev
        /// mode or not (Verse/Log.cs), so a startup path with benign failures
        /// must not report them as errors.
        /// </summary>
        public static void LimitedMessage(string context, Exception ex)
        {
            if (WithinCap(limitedMessageCounts, context))
            {
                Log.Message(Prefix + context + ": " + ex);
            }
        }

        /// <summary>A fault in another mod's own assemblies (a type, file or image it cannot load): its mismatch with this game version, not our defect.</summary>
        public static bool IsForeignAssemblyFault(Exception ex)
        {
            return IsAssemblyLoadFault(ex) || IsAssemblyLoadFault(ex?.InnerException);
        }

        private static bool IsAssemblyLoadFault(Exception ex)
        {
            return ex is TypeLoadException || ex is ReflectionTypeLoadException || ex is BadImageFormatException
                || ex is FileNotFoundException || ex is FileLoadException;
        }

        private static bool WithinCap(Dictionary<string, int> counts, string context)
        {
            counts.TryGetValue(context, out int count);
            if (count >= MaxLogsPerContext)
            {
                return false;
            }
            counts[context] = count + 1;
            return true;
        }
    }
}
