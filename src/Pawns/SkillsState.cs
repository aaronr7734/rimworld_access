namespace RimWorldAccess
{
    /// <summary>
    /// State class for displaying top skills of the selected pawn.
    /// Triggered by Alt+K key combination.
    /// </summary>
    public static class SkillsState
    {
        /// <summary>
        /// Displays top 3 skills for the currently selected pawn.
        /// Shows skill name, level, and passion.
        /// In multi-select mode, opens a pawn picker menu with skills info for each pawn.
        /// </summary>
        public static void DisplaySkillsInfo() =>
            PawnQuickInfo.Display(PawnInfoHelper.GetTopSkillsInfo);
    }
}
