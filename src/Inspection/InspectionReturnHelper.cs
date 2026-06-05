namespace RimWorldAccess
{
    /// <summary>
    /// When a submenu closes, re-announce the topmost still-active parent menu so
    /// the user lands back where they launched from. Falls back to a supplied
    /// message when no parent menu is active (submenu was opened outside the
    /// inspection flow — e.g. from the scanner or a zone context).
    /// </summary>
    public static class InspectionReturnHelper
    {
        public static void AnnounceParentOrFallback(string fallback)
        {
            if (BillConfigState.IsActive) { BillConfigState.Reannounce(); return; }
            if (BillsMenuState.IsActive)  { BillsMenuState.Reannounce();  return; }
            if (WindowlessInspectionState.IsActive)
            {
                WindowlessInspectionState.ReannounceCurrentSelection();
                return;
            }
            if (!string.IsNullOrEmpty(fallback))
                TolkHelper.SpeakData(fallback);
        }
    }
}
