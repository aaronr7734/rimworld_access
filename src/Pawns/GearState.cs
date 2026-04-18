namespace RimWorldAccess
{
    /// <summary>
    /// State class for displaying gear information of the pawn at the cursor position.
    /// Triggered by Alt+G key combination.
    /// </summary>
    public static class GearState
    {
        /// <summary>
        /// Displays gear information for the pawn at the current cursor position.
        /// Shows weapon being wielded and apparel being worn, with quality.
        /// In multi-select mode, opens a pawn picker menu with gear info for each pawn.
        /// </summary>
        public static void DisplayGearInfo() =>
            PawnQuickInfo.Display(PawnInfoHelper.GetGearInfo);
    }
}
