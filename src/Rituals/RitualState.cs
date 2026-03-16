using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Main state management for keyboard navigation in Dialog_BeginRitual.
    /// Provides three navigation modes: RoleList, PawnSelection, and QualityStats.
    /// Works generically with all ritual types.
    /// </summary>
    public static class RitualState
    {
        #region Navigation Modes

        public enum NavigationMode
        {
            RoleList,        // Main view - navigate roles
            PawnSelection,   // Submenu - select pawn for role
            QualityStats     // Tab view - quality factors
        }

        #endregion

        #region State Fields

        private static bool isActive = false;
        private static NavigationMode currentMode = NavigationMode.RoleList;
        private static Dialog_BeginRitual currentDialog = null;
        private static RitualRoleAssignments currentAssignments = null;
        private static Precept_Ritual currentRitual = null;
        private static TargetInfo currentTarget;

        // Role list navigation
        private static List<RitualRoleListItem> roleItems = new List<RitualRoleListItem>();
        private static int roleIndex = 0;

        // Pawn selection navigation
        private static List<RitualPawnListItem> pawnItems = new List<RitualPawnListItem>();
        private static int pawnIndex = 0;
        private static RitualRoleListItem selectedRole = null;

        // Quality stats navigation
        private static List<RitualQualityStatItem> qualityItems = new List<RitualQualityStatItem>();
        private static int qualityIndex = 0;

        // Saved position for Tab toggle (CaravanFormationState pattern)
        private static NavigationMode savedMode = NavigationMode.RoleList;
        private static int savedIndex = 0;

        // Typeahead search
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // One-time instruction flag
        private static bool qualityInstructionsShown = false;

        // Reflection accessors (cached)
        private static System.Reflection.FieldInfo assignmentsField;
        private static System.Reflection.FieldInfo ritualField;
        private static System.Reflection.FieldInfo targetField;

        #endregion

        #region Public Properties

        public static bool IsActive => isActive;

        public static NavigationMode CurrentNavigationMode => currentMode;

        public static bool HasActiveTypeahead => typeahead.HasActiveSearch;

        #endregion

        #region Lifecycle Methods

        /// <summary>
        /// Opens the ritual state for keyboard navigation.
        /// Called from RitualPatch when Dialog_BeginRitual opens.
        /// </summary>
        public static void Open(Dialog_BeginRitual dialog)
        {
            if (dialog == null) return;

            try
            {
                // Cache reflection accessors
                if (assignmentsField == null)
                {
                    assignmentsField = AccessTools.Field(typeof(Dialog_BeginRitual), "assignments");
                }
                if (ritualField == null)
                {
                    ritualField = AccessTools.Field(typeof(Dialog_BeginRitual), "ritual");
                }
                if (targetField == null)
                {
                    targetField = AccessTools.Field(typeof(Dialog_BeginRitual), "target");
                }

                // Extract data from dialog
                currentAssignments = assignmentsField?.GetValue(dialog) as RitualRoleAssignments;
                currentRitual = ritualField?.GetValue(dialog) as Precept_Ritual;
                currentTarget = targetField != null ? (TargetInfo)targetField.GetValue(dialog) : TargetInfo.Invalid;

                if (currentAssignments == null)
                {
                    Log.Warning("[RitualState] Could not get RitualRoleAssignments from dialog");
                    return;
                }

                // Initialize state
                isActive = true;
                currentDialog = dialog;
                currentMode = NavigationMode.RoleList;
                roleIndex = 0;
                typeahead.ClearSearch();

                // Build role list
                RitualTreeBuilder.BuildRoleList(currentAssignments, currentTarget, currentRitual, roleItems, currentDialog);

                // Announce opening with description and expected quality
                string ritualName = RitualTreeBuilder.GetRitualLabel(currentRitual);
                int roleCount = roleItems.Count;

                // Get description if available
                string description = GetRitualDescription(dialog);

                // Get extra explanation text (behavior details, requirements, etc.)
                string extraExplanation = GetExtraExplanation(dialog);

                // Get expected quality summary
                string qualitySummary = GetExpectedQualitySummary(dialog);

                // Get any blocking issues
                string warnings = GetBlockingIssues(dialog);

                var sb = new System.Text.StringBuilder();
                sb.Append($"Begin {ritualName}.");

                if (!string.IsNullOrEmpty(description))
                {
                    sb.Append($" {description}");
                }

                if (!string.IsNullOrEmpty(extraExplanation))
                {
                    sb.Append($" {extraExplanation}");
                }

                if (!string.IsNullOrEmpty(qualitySummary))
                {
                    sb.Append($" {qualitySummary}");
                }

                if (!string.IsNullOrEmpty(warnings))
                {
                    sb.Append($" Warning: {warnings}");
                }

                sb.Append($" {roleCount} {(roleCount == 1 ? "role" : "roles")}.");
                sb.Append(" Up/Down to navigate, Enter to assign pawns, Tab for quality stats, Alt+S to start.");

                TolkHelper.Speak(sb.ToString());

                // Announce first role
                if (roleItems.Count > 0)
                {
                    AnnounceCurrentRole();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RitualState] Error opening: {ex.Message}");
                isActive = false;
            }
        }

        /// <summary>
        /// Closes the ritual state.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            currentAssignments = null;
            currentRitual = null;
            currentMode = NavigationMode.RoleList;
            roleIndex = 0;
            pawnIndex = 0;
            qualityIndex = 0;
            selectedRole = null;
            typeahead.ClearSearch();
            roleItems.Clear();
            pawnItems.Clear();
            qualityItems.Clear();
        }

        #endregion

        #region Input Handling

        /// <summary>
        /// Handles keyboard input. Returns true if input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive) return false;

            // Tab key - toggle Quality Stats view (works in all modes)
            // Shift+Tab also returns from quality stats (like going "back")
            if (key == KeyCode.Tab && !ctrl && !alt)
            {
                if (shift && currentMode == NavigationMode.QualityStats)
                {
                    // Shift+Tab in quality stats = return to previous mode
                    ToggleQualityStatsView();
                    return true;
                }
                else if (!shift)
                {
                    // Tab without shift = toggle quality stats view
                    ToggleQualityStatsView();
                    return true;
                }
            }

            // Escape handling varies by mode
            if (key == KeyCode.Escape && !shift && !ctrl && !alt)
            {
                // Clear typeahead first
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentItem();
                    return true;
                }

                // Handle by mode
                if (currentMode == NavigationMode.PawnSelection)
                {
                    ExitPawnSelection(cancelled: true);
                    return true;
                }
                if (currentMode == NavigationMode.QualityStats)
                {
                    ToggleQualityStatsView(); // Return to saved mode
                    return true;
                }

                // In RoleList mode, let the game handle Escape (closes dialog)
                return false;
            }

            // Route to current mode handler
            switch (currentMode)
            {
                case NavigationMode.RoleList:
                    return HandleRoleListInput(key, shift, ctrl, alt);
                case NavigationMode.PawnSelection:
                    return HandlePawnSelectionInput(key, shift, ctrl, alt);
                case NavigationMode.QualityStats:
                    return HandleQualityStatsInput(key, shift, ctrl, alt);
            }

            return false;
        }

        private static bool HandleRoleListInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            switch (key)
            {
                case KeyCode.UpArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        roleIndex = MenuHelper.SelectPrevious(roleIndex, roleItems.Count);
                        typeahead.ClearSearch();
                        AnnounceCurrentRole();
                        return true;
                    }
                    break;

                case KeyCode.DownArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        roleIndex = MenuHelper.SelectNext(roleIndex, roleItems.Count);
                        typeahead.ClearSearch();
                        AnnounceCurrentRole();
                        return true;
                    }
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (!shift && !ctrl && !alt)
                    {
                        EnterPawnSelection();
                        return true;
                    }
                    break;

                case KeyCode.Space:
                    if (!shift && !ctrl && !alt)
                    {
                        // Space toggles gravship checkboxes in the role list
                        if (roleIndex >= 0 && roleIndex < roleItems.Count &&
                            roleItems[roleIndex].Type == RitualRoleListItem.ItemType.GravshipCheckbox)
                        {
                            ToggleGravshipCheckbox();
                            return true;
                        }
                    }
                    break;

                case KeyCode.S:
                    if (alt && !shift && !ctrl)
                    {
                        StartRitual();
                        return true;
                    }
                    break;

                case KeyCode.Home:
                    if (!shift && !ctrl && !alt)
                    {
                        if (roleItems.Count > 0)
                        {
                            roleIndex = 0;
                            typeahead.ClearSearch();
                            AnnounceCurrentRole();
                        }
                        return true;
                    }
                    break;

                case KeyCode.End:
                    if (!shift && !ctrl && !alt)
                    {
                        if (roleItems.Count > 0)
                        {
                            roleIndex = roleItems.Count - 1;
                            typeahead.ClearSearch();
                            AnnounceCurrentRole();
                        }
                        return true;
                    }
                    break;
            }

            // Typeahead search
            if (HandleTypeahead(key, shift, ctrl, alt, roleItems.Select(r => r.Label).ToList(), ref roleIndex, AnnounceCurrentRole))
            {
                return true;
            }

            // Block all unhandled keys (modal)
            return true;
        }

        private static bool HandlePawnSelectionInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            switch (key)
            {
                case KeyCode.UpArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        pawnIndex = MenuHelper.SelectPrevious(pawnIndex, pawnItems.Count);
                        typeahead.ClearSearch();
                        AnnounceCurrentPawn();
                        return true;
                    }
                    break;

                case KeyCode.DownArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        pawnIndex = MenuHelper.SelectNext(pawnIndex, pawnItems.Count);
                        typeahead.ClearSearch();
                        AnnounceCurrentPawn();
                        return true;
                    }
                    break;

                case KeyCode.Space:
                    if (!shift && !ctrl && !alt)
                    {
                        TogglePawnAssignment();
                        return true;
                    }
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (!shift && !ctrl && !alt)
                    {
                        ExitPawnSelection(cancelled: false);
                        return true;
                    }
                    break;

                case KeyCode.I:
                    if (alt && !shift && !ctrl)
                    {
                        InspectCurrentPawn();
                        return true;
                    }
                    break;

                case KeyCode.H:
                    if (alt && !shift && !ctrl)
                    {
                        ShowPawnHealth();
                        return true;
                    }
                    break;

                case KeyCode.M:
                    if (alt && !shift && !ctrl)
                    {
                        ShowPawnMood();
                        return true;
                    }
                    break;

                case KeyCode.N:
                    if (alt && !shift && !ctrl)
                    {
                        ShowPawnNeeds();
                        return true;
                    }
                    break;

                case KeyCode.G:
                    if (alt && !shift && !ctrl)
                    {
                        ShowPawnGear();
                        return true;
                    }
                    break;

                case KeyCode.Home:
                    if (!shift && !ctrl && !alt)
                    {
                        if (pawnItems.Count > 0)
                        {
                            pawnIndex = 0;
                            typeahead.ClearSearch();
                            AnnounceCurrentPawn();
                        }
                        return true;
                    }
                    break;

                case KeyCode.End:
                    if (!shift && !ctrl && !alt)
                    {
                        if (pawnItems.Count > 0)
                        {
                            pawnIndex = pawnItems.Count - 1;
                            typeahead.ClearSearch();
                            AnnounceCurrentPawn();
                        }
                        return true;
                    }
                    break;
            }

            // Typeahead search for pawn names
            if (HandleTypeahead(key, shift, ctrl, alt, pawnItems.Select(p => p.Pawn.LabelShort).ToList(), ref pawnIndex, AnnounceCurrentPawn))
            {
                return true;
            }

            // Block all unhandled keys (modal)
            return true;
        }

        private static bool HandleQualityStatsInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            switch (key)
            {
                case KeyCode.UpArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        qualityIndex = MenuHelper.SelectPrevious(qualityIndex, qualityItems.Count);
                        AnnounceCurrentQualityStat();
                        return true;
                    }
                    break;

                case KeyCode.DownArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        qualityIndex = MenuHelper.SelectNext(qualityIndex, qualityItems.Count);
                        AnnounceCurrentQualityStat();
                        return true;
                    }
                    break;

                case KeyCode.I:
                    if (alt && !shift && !ctrl)
                    {
                        OpenQualityStatBreakdown();
                        return true;
                    }
                    break;

                case KeyCode.Home:
                    if (!shift && !ctrl && !alt)
                    {
                        if (qualityItems.Count > 0)
                        {
                            qualityIndex = 0;
                            AnnounceCurrentQualityStat();
                        }
                        return true;
                    }
                    break;

                case KeyCode.End:
                    if (!shift && !ctrl && !alt)
                    {
                        if (qualityItems.Count > 0)
                        {
                            qualityIndex = qualityItems.Count - 1;
                            AnnounceCurrentQualityStat();
                        }
                        return true;
                    }
                    break;
            }

            // Block all unhandled keys (modal)
            return true;
        }

        private static bool HandleTypeahead(KeyCode key, bool shift, bool ctrl, bool alt, List<string> labels, ref int currentIndex, Action announceAction)
        {
            if (ctrl || alt) return false;

            // Backspace to remove last character
            if (key == KeyCode.Backspace)
            {
                if (typeahead.HasActiveSearch)
                {
                    int newIndex;
                    if (typeahead.ProcessBackspace(labels, out newIndex))
                    {
                        if (newIndex >= 0 && newIndex != currentIndex)
                        {
                            currentIndex = newIndex;
                            announceAction?.Invoke();
                        }
                        else if (newIndex < 0 && !typeahead.HasActiveSearch)
                        {
                            TolkHelper.Speak("Search cleared");
                        }
                        return true;
                    }
                }
                return false;
            }

            // Letter/number keys for search
            char? c = KeyCodeToChar(key, shift);
            if (c.HasValue)
            {
                int newIndex;
                if (typeahead.ProcessCharacterInput(c.Value, labels, out newIndex))
                {
                    if (newIndex >= 0 && newIndex != currentIndex)
                    {
                        currentIndex = newIndex;
                        announceAction?.Invoke();
                    }
                }
                else
                {
                    // No matches found
                    TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
                }
                return true;
            }

            return false;
        }

        private static char? KeyCodeToChar(KeyCode key, bool shift)
        {
            // Letters
            if (key >= KeyCode.A && key <= KeyCode.Z)
            {
                char c = (char)('a' + (key - KeyCode.A));
                return shift ? char.ToUpper(c) : c;
            }

            // Numbers
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                return (char)('0' + (key - KeyCode.Alpha0));
            }
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
            {
                return (char)('0' + (key - KeyCode.Keypad0));
            }

            // Space
            if (key == KeyCode.Space)
            {
                return ' ';
            }

            return null;
        }

        #endregion

        #region Mode Transitions

        private static void ToggleGravshipCheckbox()
        {
            if (roleItems.Count == 0 || roleIndex < 0 || roleIndex >= roleItems.Count)
                return;

            var item = roleItems[roleIndex];
            if (item.Type != RitualRoleListItem.ItemType.GravshipCheckbox)
                return;

            if (currentDialog == null || string.IsNullOrEmpty(item.FieldName))
                return;

            // Toggle the field on the dialog via reflection
            var field = AccessTools.Field(currentDialog.GetType(), item.FieldName);
            if (field == null) return;

            bool newValue = !item.CheckboxValue;
            field.SetValue(currentDialog, newValue);
            item.CheckboxValue = newValue;

            TolkHelper.Speak($"{item.Label}: {(newValue ? "checked" : "unchecked")}.");
        }

        private static void EnterPawnSelection()
        {
            if (roleItems.Count == 0 || roleIndex < 0 || roleIndex >= roleItems.Count)
            {
                TolkHelper.Speak("No role selected.");
                return;
            }

            var role = roleItems[roleIndex];

            // Handle gravship checkbox toggle instead of pawn selection
            if (role.Type == RitualRoleListItem.ItemType.GravshipCheckbox)
            {
                ToggleGravshipCheckbox();
                return;
            }

            if (role.IsLocked)
            {
                TolkHelper.Speak("Role is locked. All assigned pawns are required by the ritual.");
                return;
            }

            selectedRole = role;

            // Build pawn list
            var allRoles = currentAssignments?.AllRolesForReading ?? new List<RitualRole>();
            RitualTreeBuilder.BuildPawnList(currentAssignments, currentTarget, role, allRoles, pawnItems);

            if (pawnItems.Count == 0)
            {
                TolkHelper.Speak("No eligible pawns for this role.");
                selectedRole = null;
                return;
            }

            currentMode = NavigationMode.PawnSelection;
            pawnIndex = 0;
            typeahead.ClearSearch();

            TolkHelper.Speak($"Selecting pawn for {role.Label}. {pawnItems.Count} {(pawnItems.Count == 1 ? "candidate" : "candidates")}. " +
                "Space to toggle selection, Enter to confirm, Escape to cancel.");
            AnnounceCurrentPawn();
        }

        private static void ExitPawnSelection(bool cancelled)
        {
            currentMode = NavigationMode.RoleList;
            selectedRole = null;
            pawnItems.Clear();
            pawnIndex = 0;
            typeahead.ClearSearch();

            // Refresh role list to show updated counts
            RitualTreeBuilder.BuildRoleList(currentAssignments, currentTarget, currentRitual, roleItems, currentDialog);

            if (cancelled)
            {
                TolkHelper.Speak("Cancelled. Returned to role list.");
            }
            else
            {
                TolkHelper.Speak("Selection confirmed. Returned to role list.");
            }

            AnnounceCurrentRole();
        }

        private static void ToggleQualityStatsView()
        {
            if (currentMode == NavigationMode.QualityStats)
            {
                // Return to saved mode
                currentMode = savedMode;
                if (savedMode == NavigationMode.RoleList)
                {
                    roleIndex = savedIndex;
                    TolkHelper.Speak("Returned to role list.");
                    AnnounceCurrentRole();
                }
                else if (savedMode == NavigationMode.PawnSelection)
                {
                    pawnIndex = savedIndex;
                    TolkHelper.Speak($"Returned to pawn selection for {selectedRole?.Label ?? "role"}.");
                    AnnounceCurrentPawn();
                }
            }
            else
            {
                // Save current mode and switch to quality stats
                savedMode = currentMode;
                savedIndex = currentMode == NavigationMode.RoleList ? roleIndex : pawnIndex;

                // Build quality stats list
                BuildQualityStatsList();

                currentMode = NavigationMode.QualityStats;
                qualityIndex = 0;

                // Announce with instructions on first open
                if (!qualityInstructionsShown)
                {
                    TolkHelper.Speak($"Quality stats. {qualityItems.Count} factors. Up/Down to navigate, Alt+I for breakdown, Tab to return.");
                    qualityInstructionsShown = true;
                }
                else
                {
                    TolkHelper.Speak($"Quality stats. {qualityItems.Count} factors.");
                }

                if (qualityItems.Count > 0)
                {
                    AnnounceCurrentQualityStat();
                }
            }
        }

        #endregion

        #region Actions

        private static void TogglePawnAssignment()
        {
            if (pawnItems.Count == 0 || pawnIndex < 0 || pawnIndex >= pawnItems.Count)
            {
                return;
            }

            var pawnItem = pawnItems[pawnIndex];
            var pawn = pawnItem.Pawn;

            if (pawnItem.IsForced)
            {
                TolkHelper.Speak($"{pawn.LabelShort} is forced for this role and cannot be changed.");
                return;
            }

            if (pawnItem.DisabledReason != null && !pawnItem.IsAssigned)
            {
                TolkHelper.Speak($"Cannot assign {pawn.LabelShort}. {pawnItem.DisabledReason}");
                return;
            }

            try
            {
                if (pawnItem.IsAssigned)
                {
                    // Unassign
                    if (selectedRole.Type == RitualRoleListItem.ItemType.Role)
                    {
                        currentAssignments.TryUnassignAnyRole(pawn);
                        TolkHelper.Speak($"{pawn.LabelShort} deselected.");
                    }
                    else if (selectedRole.Type == RitualRoleListItem.ItemType.Spectators)
                    {
                        currentAssignments.RemoveParticipant(pawn);
                        TolkHelper.Speak($"{pawn.LabelShort} deselected.");
                    }
                }
                else
                {
                    // Assign
                    if (selectedRole.Type == RitualRoleListItem.ItemType.Role && selectedRole.Roles != null && selectedRole.Roles.Count > 0)
                    {
                        var firstRole = selectedRole.Roles[0];
                        if (currentAssignments.TryAssign(pawn, firstRole, out _))
                        {
                            TolkHelper.Speak($"{pawn.LabelShort} selected.");
                        }
                        else
                        {
                            TolkHelper.Speak($"Cannot assign {pawn.LabelShort}.");
                        }
                    }
                    else if (selectedRole.Type == RitualRoleListItem.ItemType.Spectators)
                    {
                        if (currentAssignments.TryAssignSpectate(pawn))
                        {
                            TolkHelper.Speak($"{pawn.LabelShort} selected.");
                        }
                        else
                        {
                            TolkHelper.Speak($"Cannot assign {pawn.LabelShort}.");
                        }
                    }
                }

                // Refresh pawn list
                var allRoles = currentAssignments?.AllRolesForReading ?? new List<RitualRole>();
                RitualTreeBuilder.BuildPawnList(currentAssignments, currentTarget, selectedRole, allRoles, pawnItems);

                // Keep index in bounds
                if (pawnIndex >= pawnItems.Count)
                {
                    pawnIndex = Math.Max(0, pawnItems.Count - 1);
                }

                // Announce current position after list reorder
                if (pawnItems.Count > 0 && pawnIndex >= 0 && pawnIndex < pawnItems.Count)
                {
                    var currentPawn = pawnItems[pawnIndex];
                    TolkHelper.Speak($"Now at: {currentPawn.Pawn.LabelShort}");
                }

                // Notify dialog that assignments changed (for quality recalculation)
                NotifyAssignmentsChanged();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RitualState] Error toggling pawn assignment: {ex.Message}");
            }
        }

        private static void InspectCurrentPawn()
        {
            if (pawnItems.Count == 0 || pawnIndex < 0 || pawnIndex >= pawnItems.Count)
            {
                return;
            }

            var pawn = pawnItems[pawnIndex].Pawn;
            if (pawn != null)
            {
                Dialog_InfoCard infoCard = new Dialog_InfoCard(pawn);
                Find.WindowStack.Add(infoCard);
            }
        }

        private static void ShowPawnHealth()
        {
            if (pawnItems.Count == 0 || pawnIndex < 0 || pawnIndex >= pawnItems.Count)
            {
                return;
            }

            var pawn = pawnItems[pawnIndex].Pawn;
            if (pawn != null)
            {
                string healthInfo = PawnInfoHelper.GetHealthInfo(pawn);
                TolkHelper.Speak(healthInfo);
            }
        }

        private static void ShowPawnMood()
        {
            if (pawnItems.Count == 0 || pawnIndex < 0 || pawnIndex >= pawnItems.Count)
            {
                return;
            }

            var pawn = pawnItems[pawnIndex].Pawn;
            if (pawn != null)
            {
                string moodInfo = PawnInfoHelper.GetMoodInfo(pawn);
                TolkHelper.Speak(moodInfo);
            }
        }

        private static void ShowPawnNeeds()
        {
            if (pawnItems.Count == 0 || pawnIndex < 0 || pawnIndex >= pawnItems.Count)
            {
                return;
            }

            var pawn = pawnItems[pawnIndex].Pawn;
            if (pawn != null)
            {
                string needsInfo = PawnInfoHelper.GetNeedsInfo(pawn);
                TolkHelper.Speak(needsInfo);
            }
        }

        private static void ShowPawnGear()
        {
            if (pawnItems.Count == 0 || pawnIndex < 0 || pawnIndex >= pawnItems.Count)
            {
                return;
            }

            var pawn = pawnItems[pawnIndex].Pawn;
            if (pawn != null)
            {
                string gearInfo = PawnInfoHelper.GetGearInfo(pawn);
                TolkHelper.Speak(gearInfo);
            }
        }

        private static void OpenQualityStatBreakdown()
        {
            if (qualityItems.Count == 0 || qualityIndex < 0 || qualityIndex >= qualityItems.Count)
            {
                return;
            }

            var item = qualityItems[qualityIndex];

            if (!string.IsNullOrEmpty(item.Explanation))
            {
                StatBreakdownState.Open(item.Label, item.Explanation);
            }
            else if (!string.IsNullOrEmpty(item.Tooltip))
            {
                // Use tooltip as simple breakdown
                TolkHelper.Speak($"{item.Label}. {item.Tooltip}");
            }
            else
            {
                TolkHelper.Speak("No detailed breakdown available for this factor.");
            }
        }

        private static void StartRitual()
        {
            if (currentDialog == null)
            {
                TolkHelper.Speak("No ritual dialog open.");
                return;
            }

            try
            {
                // Check for blocking issues first
                var blockingIssuesMethod = AccessTools.Method(typeof(Dialog_BeginRitual), "BlockingIssues");
                if (blockingIssuesMethod != null)
                {
                    var issues = blockingIssuesMethod.Invoke(currentDialog, null) as IEnumerable<string>;
                    if (issues != null && issues.Any())
                    {
                        var issueList = issues.ToList();
                        TolkHelper.Speak($"Cannot start ritual. {string.Join(". ", issueList)}");
                        return;
                    }
                }

                // Deactivate state before calling OnAcceptKeyPressed
                // (the game will close the dialog)
                isActive = false;

                // Trigger the dialog's accept action
                currentDialog.OnAcceptKeyPressed();

                // Check if dialog is still open (validation failed)
                if (Find.WindowStack.IsOpen(currentDialog))
                {
                    isActive = true; // Restore state
                    // The game's validation error messages should have been shown
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RitualState] Error starting ritual: {ex.Message}");
                isActive = true; // Restore state on error
            }
        }

        #endregion

        #region Announcements

        private static void AnnounceCurrentItem()
        {
            switch (currentMode)
            {
                case NavigationMode.RoleList:
                    AnnounceCurrentRole();
                    break;
                case NavigationMode.PawnSelection:
                    AnnounceCurrentPawn();
                    break;
                case NavigationMode.QualityStats:
                    AnnounceCurrentQualityStat();
                    break;
            }
        }

        private static void AnnounceCurrentRole()
        {
            if (roleItems.Count == 0)
            {
                TolkHelper.Speak("No roles available.");
                return;
            }

            if (roleIndex < 0 || roleIndex >= roleItems.Count)
            {
                roleIndex = 0;
            }

            var item = roleItems[roleIndex];

            string announcement;
            if (item.Type == RitualRoleListItem.ItemType.GravshipCheckbox)
            {
                announcement = RitualStatFormatter.FormatCheckboxAnnouncement(item);
            }
            else
            {
                announcement = RitualStatFormatter.FormatRoleAnnouncement(item, currentAssignments);
            }

            string position = MenuHelper.FormatPosition(roleIndex, roleItems.Count);

            if (!string.IsNullOrEmpty(position))
            {
                announcement += $" {position}";
            }

            TolkHelper.Speak(announcement);
        }

        private static void AnnounceCurrentPawn()
        {
            if (pawnItems.Count == 0)
            {
                TolkHelper.Speak("No pawns available.");
                return;
            }

            if (pawnIndex < 0 || pawnIndex >= pawnItems.Count)
            {
                pawnIndex = 0;
            }

            var item = pawnItems[pawnIndex];
            string announcement = RitualStatFormatter.FormatPawnAnnouncement(item);
            string position = MenuHelper.FormatPosition(pawnIndex, pawnItems.Count);

            if (!string.IsNullOrEmpty(position))
            {
                announcement += $" {position}";
            }

            TolkHelper.Speak(announcement);
        }

        private static void AnnounceCurrentQualityStat()
        {
            if (qualityItems.Count == 0)
            {
                TolkHelper.Speak("No quality factors available.");
                return;
            }

            if (qualityIndex < 0 || qualityIndex >= qualityItems.Count)
            {
                qualityIndex = 0;
            }

            var item = qualityItems[qualityIndex];
            string announcement = RitualStatFormatter.FormatQualityFactor(item);
            string position = MenuHelper.FormatPosition(qualityIndex, qualityItems.Count);

            if (!string.IsNullOrEmpty(position))
            {
                announcement += $" {position}";
            }

            TolkHelper.Speak(announcement);
        }

        #endregion

        #region Helper Methods

        private static void BuildQualityStatsList()
        {
            qualityItems.Clear();

            if (currentDialog == null) return;

            try
            {
                // Get quality factors via PopulateQualityFactors method
                // This is a protected method on Dialog_BeginLordJob that returns List<QualityFactor>
                // and outputs the quality range via an out parameter
                var populateMethod = AccessTools.Method(typeof(Dialog_BeginRitual), "PopulateQualityFactors");

                if (populateMethod != null)
                {
                    // Call with out parameter: PopulateQualityFactors(out FloatRange qualityRange)
                    object[] args = new object[] { null }; // out parameter placeholder
                    var qualityFactors = populateMethod.Invoke(currentDialog, args) as List<QualityFactor>;
                    var qualityRange = (FloatRange)args[0];

                    if (qualityFactors != null && qualityFactors.Count > 0)
                    {
                        // Build our list (this clears qualityItems first)
                        RitualTreeBuilder.BuildQualityStatsList(qualityFactors, qualityRange.min, qualityRange.max, qualityItems);
                    }
                    else
                    {
                        qualityItems.Add(new RitualQualityStatItem
                        {
                            Label = "No quality factors",
                            Change = "",
                            IsPresent = true,
                            IsPositive = true,
                            IsUncertain = false,
                            Tooltip = "This ritual does not have quality factors that affect its outcome.",
                            Explanation = null
                        });
                    }

                    // Add ritual location info at the top (INSERT at position 0)
                    // Include room name if available
                    if (currentTarget.IsValid)
                    {
                        string locationText = GetLocationDescription();
                        if (!string.IsNullOrEmpty(locationText))
                        {
                            qualityItems.Insert(0, new RitualQualityStatItem
                            {
                                Label = "Location",
                                Change = locationText,
                                IsInformational = true, // No bonus/penalty indicator
                                Tooltip = null,
                                Explanation = null
                            });
                        }
                    }

                    // Add ideology development points if applicable (only for Fluid ideologies)
                    AddIdeologyDevelopmentPoints();
                }
                else
                {
                    Log.Warning("[RitualState] Could not find PopulateQualityFactors method");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RitualState] Error building quality stats: {ex.Message}");

                // Add fallback item
                qualityItems.Add(new RitualQualityStatItem
                {
                    Label = "Quality stats unavailable",
                    Change = "",
                    IsPresent = false,
                    IsPositive = true,
                    IsUncertain = false,
                    Tooltip = ex.Message,
                    Explanation = null
                });
            }
        }

        private static void AddIdeologyDevelopmentPoints()
        {
            // Only show development points for Fluid ideologies
            if (currentRitual?.ideo == null || currentRitual.ideo.Fluid != true)
                return;

            try
            {
                // Get the outcomeEffect to access outcome chances
                var outcomeEffect = currentRitual.outcomeEffect;
                if (outcomeEffect?.def?.outcomeChances == null || outcomeEffect.def.outcomeChances.Count == 0)
                    return;

                // Get development points curve
                var devPointsCurve = IdeoDevelopmentUtility.GetDevelopmentPointsOverOutcomeIndexCurveForRitual(currentRitual.ideo, currentRitual);
                if (devPointsCurve == null)
                    return;

                // Add a header item for development points
                qualityItems.Add(new RitualQualityStatItem
                {
                    Label = "Development Points",
                    Change = "(Fluid ideology)",
                    IsPresent = true,
                    IsPositive = true,
                    IsUncertain = false,
                    Tooltip = "Points awarded to your fluid ideology based on ritual outcome.",
                    Explanation = null
                });

                // Add each outcome level with its points
                var outcomeChances = outcomeEffect.def.outcomeChances;
                for (int i = 0; i < outcomeChances.Count; i++)
                {
                    var outcome = outcomeChances[i];
                    float points = devPointsCurve.Evaluate(i);
                    qualityItems.Add(new RitualQualityStatItem
                    {
                        Label = $"  {outcome.label}",
                        Change = points.ToStringWithSign(),
                        IsPresent = true,
                        IsPositive = points >= 0,
                        IsUncertain = false,
                        Tooltip = null,
                        Explanation = null
                    });
                }
            }
            catch
            {
                // Ignore errors - development points are optional info
            }
        }

        private static void NotifyAssignmentsChanged()
        {
            // The dialog should automatically recalculate quality when assignments change
            // But we can force it by invalidating cached values if needed
            try
            {
                var notifyMethod = AccessTools.Method(typeof(RitualRoleAssignments), "Notify_AssignmentsChanged");
                if (notifyMethod != null)
                {
                    notifyMethod.Invoke(currentAssignments, null);
                }
            }
            catch
            {
                // Ignore - not all versions may have this method
            }
        }

        /// <summary>
        /// Gets the ritual description for the opening announcement.
        /// </summary>
        private static string GetRitualDescription(Dialog_BeginRitual dialog)
        {
            try
            {
                // Access the DescriptionLabel property
                var descProp = AccessTools.Property(typeof(Dialog_BeginRitual), "DescriptionLabel");
                if (descProp != null)
                {
                    var desc = descProp.GetValue(dialog)?.ToString();
                    if (!string.IsNullOrEmpty(desc))
                    {
                        return desc;
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
            return null;
        }

        /// <summary>
        /// Gets the extra explanation text (behavior details, requirements, etc.).
        /// </summary>
        private static string GetExtraExplanation(Dialog_BeginRitual dialog)
        {
            try
            {
                // Access the ExtraExplanationLabel property
                var extraProp = AccessTools.Property(typeof(Dialog_BeginRitual), "ExtraExplanationLabel");
                if (extraProp != null)
                {
                    var extra = extraProp.GetValue(dialog)?.ToString();
                    if (!string.IsNullOrEmpty(extra))
                    {
                        // Clean up any newlines for screen reader
                        return extra.Replace("\n\n", " ").Replace("\n", " ");
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
            return null;
        }

        /// <summary>
        /// Gets the expected quality summary.
        /// </summary>
        private static string GetExpectedQualitySummary(Dialog_BeginRitual dialog)
        {
            try
            {
                // Call PopulateQualityFactors to get the quality range
                var populateMethod = AccessTools.Method(typeof(Dialog_BeginRitual), "PopulateQualityFactors");
                if (populateMethod != null)
                {
                    object[] args = new object[] { null };
                    populateMethod.Invoke(dialog, args);
                    var qualityRange = (FloatRange)args[0];

                    if (Math.Abs(qualityRange.min - qualityRange.max) < 0.01f)
                    {
                        return $"Expected quality: {qualityRange.min.ToStringPercent("F0")}.";
                    }
                    return $"Expected quality: {qualityRange.min.ToStringPercent("F0")} to {qualityRange.max.ToStringPercent("F0")}.";
                }
            }
            catch
            {
                // Ignore errors
            }
            return null;
        }

        /// <summary>
        /// Gets any blocking issues or warnings.
        /// </summary>
        private static string GetBlockingIssues(Dialog_BeginRitual dialog)
        {
            var allIssues = new List<string>();

            try
            {
                // Call BlockingIssues method - these are actual blockers
                var blockingMethod = AccessTools.Method(typeof(Dialog_BeginRitual), "BlockingIssues");
                if (blockingMethod != null)
                {
                    var issues = blockingMethod.Invoke(dialog, null) as IEnumerable<string>;
                    if (issues != null)
                    {
                        allIssues.AddRange(issues);
                    }
                }

                // Check SleepingWarning property - warning that some participants are asleep
                var sleepingProp = AccessTools.Property(typeof(Dialog_BeginRitual), "SleepingWarning");
                if (sleepingProp != null)
                {
                    var sleeping = sleepingProp.GetValue(dialog)?.ToString();
                    if (!string.IsNullOrEmpty(sleeping))
                    {
                        allIssues.Add(sleeping);
                    }
                }

                // Check for drafted pawns among participants
                if (currentAssignments != null)
                {
                    bool hasDrafted = currentAssignments.Participants.Any(p => p.Drafted);
                    if (hasDrafted)
                    {
                        allIssues.Add("Some participants are drafted.");
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return allIssues.Count > 0 ? string.Join(" ", allIssues) : null;
        }

        /// <summary>
        /// Gets a location description including room name if available.
        /// Example: "Wooden double bed (in bedroom)" or just "Wooden double bed"
        /// </summary>
        private static string GetLocationDescription()
        {
            if (!currentTarget.IsValid)
                return null;

            var parts = new List<string>();

            // Add the thing label if there is one
            if (currentTarget.Thing != null)
            {
                parts.Add(currentTarget.Thing.LabelShortCap);
            }

            // Try to get room information
            if (currentTarget.HasThing || currentTarget.Cell.IsValid)
            {
                try
                {
                    Map map = currentTarget.Map;
                    IntVec3 cell = currentTarget.Cell;

                    if (map != null && cell.IsValid && cell.InBounds(map))
                    {
                        Room room = cell.GetRoom(map);
                        if (room != null && room.ProperRoom && !room.PsychologicallyOutdoors)
                        {
                            string roomLabel = room.GetRoomRoleLabel();
                            if (!string.IsNullOrEmpty(roomLabel))
                            {
                                parts.Add($"in {roomLabel}");
                            }
                        }
                        else if (room != null && room.PsychologicallyOutdoors)
                        {
                            parts.Add("outdoors");
                        }
                    }
                }
                catch
                {
                    // Ignore room detection errors
                }
            }

            // If we have no thing but have a valid cell, at least mention coordinates
            if (parts.Count == 0 && currentTarget.Cell.IsValid)
            {
                return $"Cell ({currentTarget.Cell.x}, {currentTarget.Cell.z})";
            }

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        #endregion
    }
}
