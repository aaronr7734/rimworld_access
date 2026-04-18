namespace RimWorldAccess
{
    /// <summary>
    /// State class for displaying mood information of the selected pawn.
    /// Triggered by Alt+M key combination.
    /// </summary>
    public static class MoodState
    {
        /// <summary>
        /// Displays mood information for the currently selected pawn.
        /// Shows mood level, mood description, and all thoughts affecting mood.
        /// In multi-select mode, opens a pawn picker menu with mood info for each pawn.
        /// </summary>
        public static void DisplayMoodInfo() =>
            PawnQuickInfo.Display(PawnInfoHelper.GetMoodInfo);
    }
}
