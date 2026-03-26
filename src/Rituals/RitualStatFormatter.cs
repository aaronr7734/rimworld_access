using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Formats announcements for ritual roles, pawns, and quality factors.
    /// Uses colon after attribute names and period after values for natural screen reader pauses.
    /// </summary>
    public static class RitualStatFormatter
    {
        /// <summary>
        /// Formats a role announcement for screen reader.
        /// Example: "Doctor: 1 assigned of 1 max, required."
        /// </summary>
        public static string FormatRoleAnnouncement(RitualRoleListItem item, RitualRoleAssignments assignments = null)
        {
            var sb = new StringBuilder();
            sb.Append(item.Label);
            sb.Append(": ");

            if (item.MaxCount > 0)
            {
                sb.Append($"{item.AssignedCount} assigned of {item.MaxCount} max");
                if (item.IsRequired)
                    sb.Append(", required");
            }
            else if (item.MaxCount == 0)
            {
                sb.Append($"{item.AssignedCount} assigned, optional");
            }
            else
            {
                // Unlimited (-1)
                sb.Append($"{item.AssignedCount} assigned");
            }

            if (item.IsLocked)
            {
                sb.Append(", locked");
            }

            sb.Append(".");

            // Add extra info from role (e.g., warnings for blinding targets with Wimp trait)
            if (item.Type == RitualRoleListItem.ItemType.Role && item.Roles != null && item.Roles.Count > 0 && assignments != null)
            {
                var firstRole = item.Roles[0];
                var assignedPawns = assignments.AssignedPawns(firstRole);
                string extraInfo = firstRole.ExtraInfoForDialog(assignedPawns);
                if (!string.IsNullOrEmpty(extraInfo))
                {
                    sb.Append($" {extraInfo}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats a gravship checkbox announcement for screen reader.
        /// Example: "Force visitors to leave: checked. Tooltip text."
        /// </summary>
        public static string FormatCheckboxAnnouncement(RitualRoleListItem item, bool includeTooltip = true)
        {
            var sb = new StringBuilder();
            sb.Append(item.Label);
            sb.Append(": ");
            sb.Append(item.CheckboxValue ? "checked" : "unchecked");
            sb.Append(".");

            if (includeTooltip && !string.IsNullOrEmpty(item.TooltipKey))
            {
                string tooltip = item.TooltipKey.Translate();
                if (!string.IsNullOrEmpty(tooltip))
                {
                    sb.Append($" {tooltip}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats a pawn announcement for screen reader.
        /// Example: "John: Medical: 15. Tend Quality: 145%. Selected."
        /// </summary>
        public static string FormatPawnAnnouncement(RitualPawnListItem item)
        {
            var parts = new List<string>();

            // Name
            parts.Add(item.Pawn.LabelShort);

            // Suitability stats
            if (!string.IsNullOrEmpty(item.SuitabilityInfo))
            {
                parts.Add(item.SuitabilityInfo);
            }

            // Status - only announce if selected or forced, nothing if unselected
            if (item.IsForced)
            {
                parts.Add("forced, cannot change");
            }
            else if (item.IsAssigned)
            {
                parts.Add("Selected");
            }

            if (item.DisabledReason != null)
            {
                parts.Add($"cannot assign: {item.DisabledReason}");
            }

            return string.Join(". ", parts) + ".";
        }

        /// <summary>
        /// Formats a quality factor announcement.
        /// Example: "Doctor's medicine skill (10): +12.5% quality."
        /// </summary>
        public static string FormatQualityFactor(RitualQualityStatItem item)
        {
            var sb = new StringBuilder();
            sb.Append(item.Label);
            sb.Append(": ");
            sb.Append(item.Change);
            sb.Append(".");

            // Skip status indicators for informational items (like Location)
            if (!item.IsInformational)
            {
                // Check if Change already provides context (e.g., "0% (out of +10%)")
                // In these cases, don't add redundant status
                bool changeHasContext = !string.IsNullOrEmpty(item.Change) &&
                    (item.Change.Contains("out of") || item.Change.Contains("(") || item.Change.Contains("/"));

                if (item.IsUncertain)
                {
                    sb.Append(" Uncertain outcome.");
                }
                else if (!changeHasContext)
                {
                    // Only add status when Change doesn't already explain the situation
                    if (item.IsPresent)
                    {
                        // Binary factors that are met (loved one present, indoors, etc.)
                        sb.Append(item.IsPositive ? " Bonus." : " Penalty.");
                    }
                    else if (item.Quality != 0)
                    {
                        // Scalar factors (age, skill) - they contribute but don't use "present"
                        sb.Append(item.Quality > 0 ? " Bonus." : " Penalty.");
                    }
                    else
                    {
                        // Truly not contributing (e.g., no loved one present, not indoors)
                        sb.Append(" Not met.");
                    }
                }
            }

            // Add tooltip/description if available (from game's translation strings)
            if (!string.IsNullOrEmpty(item.Tooltip))
            {
                sb.Append(" ");
                sb.Append(item.Tooltip);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats the overall quality summary.
        /// Example: "Expected Quality: 78%."
        /// </summary>
        public static string FormatQualitySummary(float minQuality, float maxQuality)
        {
            if (System.Math.Abs(minQuality - maxQuality) < 0.01f)
            {
                return $"Expected Quality: {minQuality.ToStringPercent("F0")}.";
            }
            return $"Expected Quality: {minQuality.ToStringPercent("F0")} to {maxQuality.ToStringPercent("F0")}.";
        }

        /// <summary>
        /// Formats pawn suitability information based on role requirements.
        /// Returns stats relevant to the ritual roles (e.g., Medical skill for doctor role).
        /// Example: "Medical: 15. Tend Quality: 145%."
        /// </summary>
        public static string FormatPawnSuitability(Pawn pawn, List<RitualRole> allRoles)
        {
            var stats = new List<string>();
            var processedStats = new HashSet<StatDef>();
            var processedSkills = new HashSet<SkillDef>();

            foreach (var role in allRoles)
            {
                if (role is RitualRoleColonist colonistRole)
                {
                    // Add used stat if present
                    if (colonistRole.usedStat != null && !processedStats.Contains(colonistRole.usedStat))
                    {
                        processedStats.Add(colonistRole.usedStat);

                        if (colonistRole.usedStat.Worker.IsDisabledFor(pawn))
                        {
                            stats.Add($"{colonistRole.usedStat.LabelCap}: Disabled");
                        }
                        else
                        {
                            string value = colonistRole.usedStat.Worker.ValueToStringFor(pawn);
                            stats.Add($"{colonistRole.usedStat.LabelCap}: {value}");
                        }
                    }

                    // Add used skill if present
                    if (colonistRole.usedSkill != null && !processedSkills.Contains(colonistRole.usedSkill) && pawn.skills != null)
                    {
                        processedSkills.Add(colonistRole.usedSkill);
                        var skill = pawn.skills.GetSkill(colonistRole.usedSkill);
                        if (skill != null)
                        {
                            if (skill.TotallyDisabled)
                            {
                                stats.Add($"{colonistRole.usedSkill.LabelCap}: Disabled");
                            }
                            else
                            {
                                stats.Add($"{colonistRole.usedSkill.LabelCap}: {skill.Level}");
                            }
                        }
                    }
                }
            }

            return stats.Count > 0 ? string.Join(". ", stats) : null;
        }

        /// <summary>
        /// Gets tooltip/description for a stat if available.
        /// </summary>
        public static string GetStatTooltip(StatDef stat)
        {
            if (stat == null) return null;
            return stat.description;
        }

        /// <summary>
        /// Gets tooltip/description for a skill if available.
        /// </summary>
        public static string GetSkillTooltip(SkillDef skill)
        {
            if (skill == null) return null;
            return skill.description;
        }
    }

    /// <summary>
    /// Represents an item in the role list for navigation.
    /// </summary>
    public class RitualRoleListItem
    {
        public enum ItemType { Role, Spectators, NotParticipating, GravshipCheckbox }

        public ItemType Type { get; set; }
        public List<RitualRole> Roles { get; set; } // null for Spectators/NotParticipating
        public string Label { get; set; }
        public int AssignedCount { get; set; }
        public int MaxCount { get; set; } // -1 for unlimited
        public bool IsRequired { get; set; }
        public bool IsLocked { get; set; } // All assigned pawns are forced

        // Gravship checkbox fields
        public string FieldName { get; set; }      // reflection field name on Dialog_BeginGravshipLaunch
        public string TooltipKey { get; set; }      // translation key for tooltip
        public bool CheckboxValue { get; set; }     // current checkbox state
    }

    /// <summary>
    /// Represents a pawn item in the pawn selection list.
    /// </summary>
    public class RitualPawnListItem
    {
        public Pawn Pawn { get; set; }
        public bool IsAssigned { get; set; }
        public bool IsForced { get; set; }
        public string SuitabilityInfo { get; set; } // "Medical: 15. Tend Quality: 145%"
        public string DisabledReason { get; set; }  // null if can be assigned
    }

    /// <summary>
    /// Represents a quality factor item for display.
    /// </summary>
    public class RitualQualityStatItem
    {
        public string Label { get; set; }
        public string Change { get; set; } // "+10%"
        public float Quality { get; set; } // Numeric quality value for determining contribution
        public bool IsPresent { get; set; }
        public bool IsPositive { get; set; } = true; // Whether this factor is beneficial
        public bool IsUncertain { get; set; } // Whether outcome is uncertain
        public bool IsInformational { get; set; } // Skip status indicators (for location, etc.)
        public string Tooltip { get; set; } // Description from game's translation strings
        public string Explanation { get; set; } // Full breakdown text for StatBreakdownState
    }
}
