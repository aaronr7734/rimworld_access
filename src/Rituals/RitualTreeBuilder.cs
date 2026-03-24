using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Builds navigation lists for ritual role and pawn selection.
    /// </summary>
    public static class RitualTreeBuilder
    {
        /// <summary>
        /// Builds the role list from RitualRoleAssignments.
        /// Groups roles by mergeId and adds Spectators and Not Participating sections.
        /// </summary>
        public static void BuildRoleList(RitualRoleAssignments assignments, TargetInfo ritualTarget, Precept_Ritual ritual, List<RitualRoleListItem> items, Dialog_BeginRitual dialog = null)
        {
            items.Clear();

            if (assignments == null) return;

            // Add role groups using the game's grouping
            foreach (var roleGroup in assignments.RoleGroups())
            {
                var roleList = roleGroup.ToList();
                var firstRole = roleList[0];

                // Count assigned pawns across all roles in the group
                int assignedCount = 0;
                foreach (var role in roleList)
                {
                    assignedCount += assignments.AssignedPawns(role).Count();
                }

                // Sum max count (0 means unlimited for a single role)
                int maxCount = 0;
                bool hasUnlimited = false;
                foreach (var role in roleList)
                {
                    if (role.maxCount <= 0)
                    {
                        hasUnlimited = true;
                        break;
                    }
                    maxCount += role.maxCount;
                }
                if (hasUnlimited) maxCount = -1;

                // Check if required
                bool isRequired = roleList.Any(r => r.required);

                // Check if locked (all assigned pawns are forced)
                bool isLocked = CheckIfRoleLocked(assignments, roleList);

                // Get label - use CategoryLabelCap if available, otherwise LabelCap
                string categoryLabel = firstRole.CategoryLabelCap.ToString();
                string label = !string.IsNullOrEmpty(categoryLabel) ? categoryLabel : firstRole.LabelCap.ToString();

                items.Add(new RitualRoleListItem
                {
                    Type = RitualRoleListItem.ItemType.Role,
                    Roles = roleList,
                    Label = label,
                    AssignedCount = assignedCount,
                    MaxCount = maxCount,
                    IsRequired = isRequired,
                    IsLocked = isLocked
                });
            }

            // Add Spectators if allowed
            if (assignments.SpectatorsAllowed)
            {
                int spectatorCount = assignments.SpectatorsForReading.Count;

                // Get custom spectator label from ritual definition, fallback to "Spectators"
                string spectatorLabel = ritual?.behavior?.def?.spectatorsLabel;
                if (string.IsNullOrEmpty(spectatorLabel))
                {
                    spectatorLabel = "Spectators";
                }

                items.Add(new RitualRoleListItem
                {
                    Type = RitualRoleListItem.ItemType.Spectators,
                    Roles = null,
                    Label = spectatorLabel,
                    AssignedCount = spectatorCount,
                    MaxCount = -1, // Unlimited
                    IsRequired = false,
                    IsLocked = false
                });
            }

            // Note: "Not Participating" is intentionally NOT shown as a role.
            // Non-participating pawns who can spectate appear in the Spectators list as candidates.
            // Showing "Not Participating" as a selectable role is confusing because:
            // 1. You're not "selecting pawns to not participate" - they already aren't participating
            // 2. The action would be to add them as spectators, which is done via Spectators

            // Add gravship launch checkboxes at the bottom of the role list
            if (dialog != null && dialog.GetType().Name == "Dialog_BeginGravshipLaunch")
            {
                AppendGravshipCheckboxes(dialog, items);
            }
        }

        /// <summary>
        /// Appends gravship launch checkboxes to the role list.
        /// These appear at the bottom and are toggled with Enter/Space.
        /// </summary>
        private static void AppendGravshipCheckboxes(Dialog_BeginRitual dialog, List<RitualRoleListItem> items)
        {
            var dialogType = dialog.GetType();

            // Force visitors to leave
            AddCheckboxItem(dialog, dialogType, items,
                "forceVisitorsToLeave",
                "GravshipForceVisitorsToLeaveLabel",
                "GravshipForceVisitorsToLeaveTooltip");

            // Board colony animals
            AddCheckboxItem(dialog, dialogType, items,
                "boardColonyAnimals",
                "GravshipBoardColonyAnimalsLabel",
                "GravshipBoardColonyAnimalsTooltip");

            // Board colony mechs (only if Biotech is active)
            if (ModsConfig.BiotechActive)
            {
                AddCheckboxItem(dialog, dialogType, items,
                    "boardColonyMechs",
                    "GravshipBoardColonyMechsLabel",
                    "GravshipBoardColonyMechsTooltip");
            }
        }

        private static void AddCheckboxItem(Dialog_BeginRitual dialog, System.Type dialogType, List<RitualRoleListItem> items,
            string fieldName, string labelKey, string tooltipKey)
        {
            var field = AccessTools.Field(dialogType, fieldName);
            if (field == null) return;

            bool currentValue = (bool)field.GetValue(dialog);

            items.Add(new RitualRoleListItem
            {
                Type = RitualRoleListItem.ItemType.GravshipCheckbox,
                Roles = null,
                Label = labelKey.Translate(),
                FieldName = fieldName,
                TooltipKey = tooltipKey,
                CheckboxValue = currentValue,
                AssignedCount = 0,
                MaxCount = 0,
                IsRequired = false,
                IsLocked = false
            });
        }

        /// <summary>
        /// Builds the pawn list for a selected role.
        /// Shows assigned pawns first, then available candidates.
        /// </summary>
        public static void BuildPawnList(
            RitualRoleAssignments assignments,
            TargetInfo ritualTarget,
            RitualRoleListItem roleItem,
            List<RitualRole> allRoles,
            List<RitualPawnListItem> items)
        {
            items.Clear();

            if (assignments == null) return;

            var allPawns = new HashSet<Pawn>();
            var assignedPawns = new HashSet<Pawn>();
            var forcedPawns = new HashSet<Pawn>();

            // Collect pawns based on role type
            if (roleItem.Type == RitualRoleListItem.ItemType.Role && roleItem.Roles != null && roleItem.Roles.Count > 0)
            {
                // Get assigned pawns for all roles in this group
                foreach (var role in roleItem.Roles)
                {
                    foreach (var pawn in assignments.AssignedPawns(role))
                    {
                        allPawns.Add(pawn);
                        assignedPawns.Add(pawn);
                        if (assignments.Forced(pawn))
                        {
                            forcedPawns.Add(pawn);
                        }
                    }
                }

                // Get candidate pawns for the first role (they share candidates due to mergeId)
                var firstRole = roleItem.Roles[0];
                foreach (var pawn in assignments.CandidatesForRole(firstRole, ritualTarget, includeAssigned: false))
                {
                    allPawns.Add(pawn);
                }
            }
            else if (roleItem.Type == RitualRoleListItem.ItemType.Spectators)
            {
                // Current spectators
                foreach (var pawn in assignments.SpectatorsForReading)
                {
                    allPawns.Add(pawn);
                    assignedPawns.Add(pawn);
                }

                // ALL pawns who can spectate (not just auto-suggested candidates)
                // SpectatorCandidates() only returns auto-suggested pawns (e.g., love partners for childbirth)
                // But CanEverSpectate() returns all eligible pawns, which is what we want for manual selection
                foreach (var pawn in assignments.AllCandidatePawns)
                {
                    // Skip if already in our list
                    if (allPawns.Contains(pawn))
                        continue;

                    // Skip if pawn has a role (they're not spectators)
                    if (assignments.RoleForPawn(pawn) != null)
                        continue;

                    // Check if pawn can spectate at all
                    if (assignments.CanEverSpectate(pawn))
                    {
                        allPawns.Add(pawn);
                    }
                }
            }
            // Note: NotParticipating type is no longer used - see comment in BuildRoleList

            // Build list items, assigned first, then by name
            var orderedPawns = allPawns
                .OrderByDescending(p => assignedPawns.Contains(p))
                .ThenBy(p => p.LabelShort);

            foreach (var pawn in orderedPawns)
            {
                string disabledReason = null;

                if (roleItem.Type == RitualRoleListItem.ItemType.Role && roleItem.Roles != null && roleItem.Roles.Count > 0)
                {
                    // Check if pawn can be assigned to the first role
                    var firstRole = roleItem.Roles[0];
                    disabledReason = assignments.PawnNotAssignableReason(pawn, firstRole);
                }
                else if (roleItem.Type == RitualRoleListItem.ItemType.Spectators)
                {
                    disabledReason = assignments.PawnNotAssignableReason(pawn, null);
                }
                // Not Participating has no assignment restrictions

                // Get suitability info - only for roles, not spectators
                // Spectators don't have skill requirements
                string suitabilityInfo = null;
                if (roleItem.Type == RitualRoleListItem.ItemType.Role)
                {
                    suitabilityInfo = RitualStatFormatter.FormatPawnSuitability(pawn, allRoles);
                }

                items.Add(new RitualPawnListItem
                {
                    Pawn = pawn,
                    IsAssigned = assignedPawns.Contains(pawn),
                    IsForced = forcedPawns.Contains(pawn),
                    SuitabilityInfo = suitabilityInfo,
                    DisabledReason = disabledReason
                });
            }
        }

        /// <summary>
        /// Builds the quality stats list from the dialog's quality factors.
        /// </summary>
        public static void BuildQualityStatsList(
            List<QualityFactor> qualityFactors,
            float minQuality,
            float maxQuality,
            List<RitualQualityStatItem> items)
        {
            items.Clear();

            if (qualityFactors == null) return;

            // Sort by priority (lower = higher priority in display)
            var sortedFactors = qualityFactors
                .OrderBy(f => f.priority)
                .ToList();

            foreach (var factor in sortedFactors)
            {
                if (factor == null) continue;

                // Build label with count info if available (e.g., "Medical: 15")
                string label = factor.label ?? "Unknown";
                if (!string.IsNullOrEmpty(factor.count))
                {
                    label += $" ({factor.count})";
                }

                items.Add(new RitualQualityStatItem
                {
                    Label = label,
                    Change = factor.qualityChange ?? "0%",
                    Quality = factor.quality, // Numeric value for determining contribution
                    IsPresent = factor.present,
                    Tooltip = factor.toolTip, // From game's translation strings
                    Explanation = null,
                    IsPositive = factor.positive,
                    IsUncertain = factor.uncertainOutcome
                });
            }

            // Add summary item
            items.Add(new RitualQualityStatItem
            {
                Label = "Expected Quality",
                Change = FormatQualityRange(minQuality, maxQuality),
                IsInformational = true, // No bonus/penalty indicator
                Tooltip = "The overall expected quality of this ritual based on all factors.",
                Explanation = null
            });
        }

        /// <summary>
        /// Checks if all assigned pawns in a role group are forced (locked).
        /// </summary>
        private static bool CheckIfRoleLocked(RitualRoleAssignments assignments, List<RitualRole> roles)
        {
            var assignedPawns = new List<Pawn>();
            foreach (var role in roles)
            {
                assignedPawns.AddRange(assignments.AssignedPawns(role));
            }

            if (assignedPawns.Count == 0) return false;

            return assignedPawns.All(p => assignments.Forced(p));
        }

        /// <summary>
        /// Formats a quality range for display.
        /// </summary>
        private static string FormatQualityRange(float minQuality, float maxQuality)
        {
            if (Math.Abs(minQuality - maxQuality) < 0.01f)
            {
                return minQuality.ToStringPercent("F0");
            }
            return $"{minQuality.ToStringPercent("F0")} to {maxQuality.ToStringPercent("F0")}";
        }

        /// <summary>
        /// Gets the ritual label from a Dialog_BeginRitual.
        /// </summary>
        public static string GetRitualLabel(Precept_Ritual ritual)
        {
            if (ritual == null) return "Ritual";

            // Try to get the behavior label first (more descriptive)
            if (ritual.behavior?.def?.label != null)
            {
                return ritual.behavior.def.label.CapitalizeFirst();
            }

            // Fall back to ritual label
            return ritual.Label?.CapitalizeFirst() ?? "Ritual";
        }
    }
}
