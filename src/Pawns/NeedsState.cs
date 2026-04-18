namespace RimWorldAccess
{
    /// <summary>
    /// State class for displaying needs information of the pawn at the cursor position.
    /// Triggered by Alt+N key combination.
    /// </summary>
    public static class NeedsState
    {
        /// <summary>
        /// Displays needs information for the pawn at the current cursor position.
        /// Shows all needs with their current percentages and trends.
        /// In multi-select mode, opens a pawn picker menu with needs info for each pawn.
        /// </summary>
        public static void DisplayNeedsInfo() =>
            PawnQuickInfo.Display(PawnInfoHelper.GetNeedsInfo);
    }
}
