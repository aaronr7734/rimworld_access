using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Formats announcements for Lord-job dialog views. Takes adapter-projected views,
    /// not dialog-specific types, so the same formatting drives ideology rituals,
    /// psychic rituals, and gravship launches.
    /// </summary>
    public static class RitualStatFormatter
    {
        public static string FormatRole(LordJobRoleView view)
        {
            var sb = new StringBuilder();
            sb.Append(view.Label);
            sb.Append(": ");

            if (view.MaxCount > 0)
            {
                sb.Append($"{view.AssignedCount} assigned of {view.MaxCount} max");
                if (view.IsRequired) sb.Append(", required");
            }
            else if (view.MaxCount == 0)
            {
                sb.Append($"{view.AssignedCount} assigned, optional");
            }
            else
            {
                sb.Append($"{view.AssignedCount} assigned");
            }

            if (view.IsLocked) sb.Append(", locked");
            sb.Append(".");

            if (!string.IsNullOrEmpty(view.ExtraInfoLine))
            {
                sb.Append(" ");
                sb.Append(view.ExtraInfoLine);
            }

            if (!string.IsNullOrEmpty(view.Tooltip))
            {
                sb.Append(" ");
                sb.Append(view.Tooltip);
            }

            return sb.ToString();
        }

        public static string FormatExtraToggle(LordJobExtraToggle toggle, bool includeTooltip = true)
        {
            var sb = new StringBuilder();
            sb.Append(toggle.Label);
            sb.Append(": ");
            sb.Append(toggle.Checked ? "checked" : "unchecked");
            sb.Append(".");

            if (includeTooltip && !string.IsNullOrEmpty(toggle.Tooltip))
            {
                sb.Append(" ");
                sb.Append(toggle.Tooltip);
            }
            return sb.ToString();
        }

        public static string FormatPawn(LordJobPawnView view)
        {
            // Order: name → status → disabled reason → suitability → tooltip.
            // Selection status comes second so screen-reader users hear whether the pawn is
            // already chosen before sitting through stats and descriptions.
            var parts = new List<string>();
            parts.Add(view.Pawn.LabelShort);

            if (view.IsForced)
                parts.Add("forced, cannot change");
            else if (view.IsAssigned)
                parts.Add("Selected");

            if (!string.IsNullOrEmpty(view.DisabledReason))
                parts.Add($"cannot assign: {view.DisabledReason}");

            if (!string.IsNullOrEmpty(view.SuitabilityLine))
                parts.Add(view.SuitabilityLine);

            if (!string.IsNullOrEmpty(view.Tooltip))
                parts.Add(view.Tooltip);

            return string.Join(". ", parts) + ".";
        }

        public static string FormatQualityRow(LordJobQualityRow row)
        {
            var sb = new StringBuilder();
            sb.Append(row.Label);
            sb.Append(": ");
            sb.Append(row.Change);
            sb.Append(".");

            if (!row.IsInformational)
            {
                bool changeHasContext = !string.IsNullOrEmpty(row.Change) &&
                    (row.Change.Contains("out of") || row.Change.Contains("(") || row.Change.Contains("/"));

                if (row.IsUncertain)
                {
                    sb.Append(" ");
                    sb.Append("RimWorldAccess.Rituals.Quality.UncertainOutcome".Translate());
                }
                else if (!changeHasContext)
                {
                    if (row.IsPresent)
                        sb.Append(row.IsPositive ? " Bonus." : " Penalty.");
                    else if (row.Quality != 0)
                        sb.Append(row.Quality > 0 ? " Bonus." : " Penalty.");
                    else
                        sb.Append(" Not met.");
                }
            }

            if (!string.IsNullOrEmpty(row.Tooltip))
            {
                sb.Append(" ");
                sb.Append(row.Tooltip);
            }
            return sb.ToString();
        }
    }
}
