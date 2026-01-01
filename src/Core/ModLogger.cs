using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Static logger for mod-wide logging using RimWorld's native logging system.
    /// </summary>
    public static class ModLogger
    {
        private const string Prefix = "[RimWorld Access] ";

        /// <summary>
        /// Log a message to RimWorld's log.
        /// </summary>
        public static void Msg(string message)
        {
            Log.Message(Prefix + message);
        }

        /// <summary>
        /// Log a debug message (shown as regular message in RimWorld).
        /// </summary>
        public static void Debug(string message)
        {
            Log.Message(Prefix + "[DEBUG] " + message);
        }

        /// <summary>
        /// Log a warning to RimWorld's log.
        /// </summary>
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
    }
}
