namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The tri-state arithmetic behind Colony Manager Redux's shortcut rows ("All", "Predators",
    /// "Stone"...), taken from the mod's own <c>Utilities.ToggleState</c> and the
    /// <c>Utilities.DrawToggle</c> overload a shortcut row draws with: the box is checked only when
    /// a non-empty subset is fully allowed, and a plain click allows the whole subset unless it is
    /// already fully allowed, in which case it disallows all of it.
    ///
    /// PURE: no Unity/Verse types, links into the test project.
    /// </summary>
    public static class CmrSubsetToggle
    {
        /// <summary>The box the mod draws for a subset: checked when all of a non-empty subset is allowed, unchecked when none of it is, partial in between.</summary>
        public static CheckState StateOf(int subsetCount, int allowedCount)
        {
            if (subsetCount > 0 && allowedCount >= subsetCount)
            {
                return CheckState.Checked;
            }
            return allowedCount <= 0 ? CheckState.Unchecked : CheckState.PartiallyChecked;
        }

        /// <summary>
        /// Whether a plain click turns the subset on: it does unless the box is checked, so a
        /// partially allowed subset fills up rather than emptying.
        /// </summary>
        public static bool ValueForNextClick(int subsetCount, int allowedCount)
        {
            return StateOf(subsetCount, allowedCount) != CheckState.Checked;
        }
    }
}
