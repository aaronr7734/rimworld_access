using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// What Need.DrawOnGUI shows on a need bar: the level, the change arrow, and the instant-level
    /// marker for where the need is heading (drawn only when CurInstantLevelPercentage is set).
    /// </summary>
    internal static class NeedBarText
    {
        public static string Describe(Need need)
        {
            var parts = new List<string> { need.CurLevelPercentage.ToStringPercent() };
            int arrow = need.GUIChangeArrow;
            if (arrow > 0)
            {
                parts.Add("RimWorldAccess.UI.GenericWindow.BarRising".Loc().ToString());
            }
            else if (arrow < 0)
            {
                parts.Add("RimWorldAccess.UI.GenericWindow.BarFalling".Loc().ToString());
            }
            float instant = need.CurInstantLevelPercentage;
            if (instant >= 0f)
            {
                parts.Add("RimWorldAccess.Inspection.Need.HeadingToward".Loc(instant.ToStringPercent()).ToString());
            }
            return string.Join(", ", parts);
        }
    }
}
