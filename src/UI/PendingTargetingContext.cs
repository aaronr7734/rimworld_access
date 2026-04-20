namespace RimWorldAccess
{
    /// <summary>
    /// Static context holder used to pass an action label across the boundary from
    /// "user activates a float-menu option" to "that option's inline action calls
    /// Find.Targeter.BeginTargeting(...)" (callback-based overloads).
    ///
    /// TargetingParameters carries no human-readable name, so we capture the option's
    /// localized label at the call site and consume it inside the BeginTargeting postfix.
    /// </summary>
    public static class PendingTargetingContext
    {
        private static string pendingLabel;

        public static void Set(string label)
        {
            pendingLabel = label;
        }

        public static string ConsumeLabel()
        {
            string label = pendingLabel;
            pendingLabel = null;
            return label;
        }

        public static void Clear()
        {
            pendingLabel = null;
        }
    }
}
