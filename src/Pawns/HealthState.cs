namespace RimWorldAccess
{
    /// <summary>
    /// State class for displaying health information of the pawn at the cursor position.
    /// Triggered by Alt+H key combination.
    /// </summary>
    public static class HealthState
    {
        /// <summary>
        /// Displays health information for the pawn at the current cursor position.
        /// Shows health state, conditions, bleeding, pain, and capacities.
        /// In multi-select mode, opens a pawn picker menu with health info for each pawn.
        /// </summary>
        public static void DisplayHealthInfo() =>
            PawnQuickInfo.Display(PawnInfoHelper.GetHealthInfo);
    }
}
