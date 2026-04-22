using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Generic keyboard accessibility state for any Dialog_BeginLordJob subclass.
    /// Drives ideology rituals, psychic rituals, and gravship launches uniformly via
    /// an ILordJobDialogAdapter; the state class itself is dialog-agnostic.
    /// </summary>
    public static class LordJobDialogState
    {
        public enum NavigationMode
        {
            RoleList,
            PawnSelection,
            QualityStats,
        }

        private static bool isActive = false;
        private static ILordJobDialogAdapter adapter = null;
        private static NavigationMode currentMode = NavigationMode.RoleList;

        private static readonly List<LordJobRoleView> roleViews = new List<LordJobRoleView>();
        private static readonly List<LordJobExtraToggle> extraToggles = new List<LordJobExtraToggle>();
        private static int roleIndex = 0;

        private static readonly List<LordJobPawnView> pawnViews = new List<LordJobPawnView>();
        private static int pawnIndex = 0;
        private static LordJobRoleView selectedRole = null;

        private static readonly List<LordJobQualityRow> qualityRows = new List<LordJobQualityRow>();
        private static int qualityIndex = 0;

        private static NavigationMode savedMode = NavigationMode.RoleList;
        private static int savedIndex = 0;

        private static readonly TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static bool qualityInstructionsShown = false;

        // Warning diff tracking: re-announce only when the warning set changes after a toggle.
        private static readonly List<string> lastWarnings = new List<string>();

        public static bool IsActive => isActive;
        public static NavigationMode CurrentNavigationMode => currentMode;
        public static bool HasActiveTypeahead => typeahead.HasActiveSearch;

        public static void Open(Dialog_BeginLordJob dialog)
        {
            if (dialog == null) return;

            try
            {
                if (!LordJobAdapterFactory.TryCreate(dialog, out adapter))
                {
                    isActive = false;
                    return;
                }

                isActive = true;
                currentMode = NavigationMode.RoleList;
                roleIndex = 0;
                pawnIndex = 0;
                qualityIndex = 0;
                selectedRole = null;
                typeahead.ClearSearch();

                RebuildRoleAndToggleList();

                string name = adapter.LocalizedDialogName;
                int navItemCount = roleViews.Count + extraToggles.Count;

                string description = adapter.DescriptionText;
                string extraExplanation = adapter.ExtraExplanationText;
                string qualitySentence = adapter.ExpectedQualitySentence;

                lastWarnings.Clear();
                lastWarnings.AddRange(adapter.ComputeWarnings() ?? Enumerable.Empty<string>());

                var sb = new StringBuilder();
                sb.Append($"Begin {name}.");

                if (!string.IsNullOrEmpty(description))
                    sb.Append($" {description}");

                if (!string.IsNullOrEmpty(extraExplanation))
                    sb.Append($" {extraExplanation}");

                if (!string.IsNullOrEmpty(qualitySentence))
                    sb.Append($" {qualitySentence}");

                if (lastWarnings.Count > 0)
                    sb.Append($" Warning: {string.Join(" ", lastWarnings)}");

                int roleSlots = roleViews.Count;
                sb.Append($" {roleSlots} {(roleSlots == 1 ? "role" : "roles")}.");
                sb.Append(" Up/Down to navigate, Enter to assign pawns, Tab for quality stats, Alt+S to start.");

                TolkHelper.Speak(sb.ToString());

                if (TotalNavItemCount() > 0)
                {
                    AnnounceCurrentRoleOrToggle();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[LordJobDialogState] Error opening: {ex.Message}");
                isActive = false;
                adapter = null;
            }
        }

        public static void Close()
        {
            isActive = false;
            adapter = null;
            currentMode = NavigationMode.RoleList;
            roleIndex = 0;
            pawnIndex = 0;
            qualityIndex = 0;
            selectedRole = null;
            roleViews.Clear();
            extraToggles.Clear();
            pawnViews.Clear();
            qualityRows.Clear();
            lastWarnings.Clear();
            typeahead.ClearSearch();
        }

        public static string ClosingAnnouncement => adapter?.ClosingAnnouncement ?? "Dialog closed.";

        // === Input ===

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive) return false;

            if (key == KeyCode.Tab && !ctrl && !alt)
            {
                if (shift && currentMode == NavigationMode.QualityStats)
                {
                    ToggleQualityStatsView();
                    return true;
                }
                if (!shift)
                {
                    ToggleQualityStatsView();
                    return true;
                }
            }

            if (key == KeyCode.Escape && !shift && !ctrl && !alt)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentItem();
                    return true;
                }

                if (currentMode == NavigationMode.PawnSelection)
                {
                    ExitPawnSelection(cancelled: true);
                    return true;
                }
                if (currentMode == NavigationMode.QualityStats)
                {
                    ToggleQualityStatsView();
                    return true;
                }

                return false; // RoleList → let game close dialog
            }

            switch (currentMode)
            {
                case NavigationMode.RoleList: return HandleRoleListInput(key, shift, ctrl, alt);
                case NavigationMode.PawnSelection: return HandlePawnSelectionInput(key, shift, ctrl, alt);
                case NavigationMode.QualityStats: return HandleQualityStatsInput(key, shift, ctrl, alt);
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
                        roleIndex = MenuHelper.SelectPrevious(roleIndex, TotalNavItemCount());
                        typeahead.ClearSearch();
                        AnnounceCurrentRoleOrToggle();
                        return true;
                    }
                    break;

                case KeyCode.DownArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        roleIndex = MenuHelper.SelectNext(roleIndex, TotalNavItemCount());
                        typeahead.ClearSearch();
                        AnnounceCurrentRoleOrToggle();
                        return true;
                    }
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (!shift && !ctrl && !alt)
                    {
                        EnterPawnSelectionOrToggle();
                        return true;
                    }
                    break;

                case KeyCode.Space:
                    if (!shift && !ctrl && !alt)
                    {
                        if (IsCurrentNavItemExtraToggle())
                        {
                            ToggleCurrentExtraToggle();
                            return true;
                        }
                    }
                    break;

                case KeyCode.S:
                    if (alt && !shift && !ctrl)
                    {
                        StartLordJob();
                        return true;
                    }
                    break;

                case KeyCode.Home:
                    if (!shift && !ctrl && !alt)
                    {
                        if (TotalNavItemCount() > 0)
                        {
                            roleIndex = 0;
                            typeahead.ClearSearch();
                            AnnounceCurrentRoleOrToggle();
                        }
                        return true;
                    }
                    break;

                case KeyCode.End:
                    if (!shift && !ctrl && !alt)
                    {
                        int n = TotalNavItemCount();
                        if (n > 0)
                        {
                            roleIndex = n - 1;
                            typeahead.ClearSearch();
                            AnnounceCurrentRoleOrToggle();
                        }
                        return true;
                    }
                    break;
            }

            if (HandleTypeahead(key, shift, ctrl, alt, RoleListLabels(), roleIndex, i => roleIndex = i, AnnounceCurrentRoleOrToggle))
                return true;

            return true;
        }

        private static bool HandlePawnSelectionInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            switch (key)
            {
                case KeyCode.UpArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        pawnIndex = MenuHelper.SelectPrevious(pawnIndex, pawnViews.Count);
                        typeahead.ClearSearch();
                        AnnounceCurrentPawn();
                        return true;
                    }
                    break;

                case KeyCode.DownArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        pawnIndex = MenuHelper.SelectNext(pawnIndex, pawnViews.Count);
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
                    if (alt && !shift && !ctrl) { InspectCurrentPawn(); return true; }
                    break;
                case KeyCode.H:
                    if (alt && !shift && !ctrl) { ShowPawnInfo(p => PawnInfoHelper.GetHealthInfo(p)); return true; }
                    break;
                case KeyCode.M:
                    if (alt && !shift && !ctrl) { ShowPawnInfo(p => PawnInfoHelper.GetMoodInfo(p)); return true; }
                    break;
                case KeyCode.N:
                    if (alt && !shift && !ctrl) { ShowPawnInfo(p => PawnInfoHelper.GetNeedsInfo(p)); return true; }
                    break;
                case KeyCode.G:
                    if (alt && !shift && !ctrl) { ShowPawnInfo(p => PawnInfoHelper.GetGearInfo(p)); return true; }
                    break;

                case KeyCode.Home:
                    if (!shift && !ctrl && !alt)
                    {
                        if (pawnViews.Count > 0)
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
                        if (pawnViews.Count > 0)
                        {
                            pawnIndex = pawnViews.Count - 1;
                            typeahead.ClearSearch();
                            AnnounceCurrentPawn();
                        }
                        return true;
                    }
                    break;
            }

            if (HandleTypeahead(key, shift, ctrl, alt, pawnViews.Select(p => p.Pawn.LabelShort).ToList(), pawnIndex, i => pawnIndex = i, AnnounceCurrentPawn))
                return true;

            return true;
        }

        private static bool HandleQualityStatsInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            switch (key)
            {
                case KeyCode.UpArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        qualityIndex = MenuHelper.SelectPrevious(qualityIndex, qualityRows.Count);
                        AnnounceCurrentQualityRow();
                        return true;
                    }
                    break;

                case KeyCode.DownArrow:
                    if (!shift && !ctrl && !alt)
                    {
                        qualityIndex = MenuHelper.SelectNext(qualityIndex, qualityRows.Count);
                        AnnounceCurrentQualityRow();
                        return true;
                    }
                    break;

                case KeyCode.I:
                    if (alt && !shift && !ctrl) { OpenQualityRowBreakdown(); return true; }
                    break;

                case KeyCode.Home:
                    if (!shift && !ctrl && !alt)
                    {
                        if (qualityRows.Count > 0)
                        {
                            qualityIndex = 0;
                            AnnounceCurrentQualityRow();
                        }
                        return true;
                    }
                    break;

                case KeyCode.End:
                    if (!shift && !ctrl && !alt)
                    {
                        if (qualityRows.Count > 0)
                        {
                            qualityIndex = qualityRows.Count - 1;
                            AnnounceCurrentQualityRow();
                        }
                        return true;
                    }
                    break;
            }
            return true;
        }

        private static bool HandleTypeahead(KeyCode key, bool shift, bool ctrl, bool alt, List<string> labels, int currentIndex, Action<int> setIndex, Action announceAction)
        {
            if (ctrl || alt) return false;

            if (key == KeyCode.Backspace)
            {
                if (typeahead.HasActiveSearch)
                {
                    if (typeahead.ProcessBackspace(labels, out int newIndex))
                    {
                        if (newIndex >= 0 && newIndex != currentIndex)
                        {
                            setIndex(newIndex);
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

            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;
            bool isKeypad = key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9;

            if (isLetter || isNumber || isKeypad)
            {
                TypeaheadCharacterBuffer.RequestCharacter(c =>
                {
                    if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
                    {
                        if (newIndex >= 0 && newIndex != currentIndex)
                        {
                            setIndex(newIndex);
                            announceAction?.Invoke();
                        }
                    }
                    else
                    {
                        TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
                    }
                });
                return true;
            }

            return false;
        }

        // === Mode transitions ===

        private static void EnterPawnSelectionOrToggle()
        {
            int total = TotalNavItemCount();
            if (total == 0 || roleIndex < 0 || roleIndex >= total)
            {
                TolkHelper.Speak("No item selected.");
                return;
            }

            if (IsCurrentNavItemExtraToggle())
            {
                ToggleCurrentExtraToggle();
                return;
            }

            var role = roleViews[roleIndex];
            if (role.IsLocked)
            {
                TolkHelper.Speak("Role is locked. All assigned pawns are required.");
                return;
            }

            selectedRole = role;
            pawnViews.Clear();
            var built = adapter.BuildPawnList(role);
            if (built != null) pawnViews.AddRange(built);

            if (pawnViews.Count == 0)
            {
                TolkHelper.Speak("No eligible pawns for this role.");
                selectedRole = null;
                return;
            }

            currentMode = NavigationMode.PawnSelection;
            pawnIndex = 0;
            typeahead.ClearSearch();

            TolkHelper.Speak($"Selecting pawn for {role.Label}. {pawnViews.Count} {(pawnViews.Count == 1 ? "candidate" : "candidates")}. " +
                "Space to toggle selection, Enter to confirm, Escape to cancel.");
            AnnounceCurrentPawn();
        }

        private static void ExitPawnSelection(bool cancelled)
        {
            currentMode = NavigationMode.RoleList;
            selectedRole = null;
            pawnViews.Clear();
            pawnIndex = 0;
            typeahead.ClearSearch();

            RebuildRoleAndToggleList();

            TolkHelper.Speak(cancelled ? "Cancelled. Returned to role list." : "Selection confirmed. Returned to role list.");
            AnnounceCurrentRoleOrToggle();
        }

        private static void ToggleQualityStatsView()
        {
            if (currentMode == NavigationMode.QualityStats)
            {
                currentMode = savedMode;
                if (savedMode == NavigationMode.RoleList)
                {
                    roleIndex = savedIndex;
                    TolkHelper.Speak("Returned to role list.");
                    AnnounceCurrentRoleOrToggle();
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
                savedMode = currentMode;
                savedIndex = currentMode == NavigationMode.RoleList ? roleIndex : pawnIndex;

                BuildQualityRows();

                currentMode = NavigationMode.QualityStats;
                qualityIndex = 0;

                if (!qualityInstructionsShown)
                {
                    TolkHelper.Speak($"Quality stats. {qualityRows.Count} factors. Up/Down to navigate, Alt+I for breakdown, Tab to return.");
                    qualityInstructionsShown = true;
                }
                else
                {
                    TolkHelper.Speak($"Quality stats. {qualityRows.Count} factors.");
                }

                if (qualityRows.Count > 0) AnnounceCurrentQualityRow();
            }
        }

        // === Actions ===

        private static void TogglePawnAssignment()
        {
            if (pawnViews.Count == 0 || pawnIndex < 0 || pawnIndex >= pawnViews.Count) return;
            var pawn = pawnViews[pawnIndex];
            if (pawn?.Pawn == null) return;

            string failureReason = null;
            var result = adapter.ToggleAssignment(selectedRole, pawn, out failureReason);
            switch (result)
            {
                case AssignmentResult.Assigned:
                    TolkHelper.Speak($"{pawn.Pawn.LabelShort} selected.");
                    break;
                case AssignmentResult.Unassigned:
                    TolkHelper.Speak($"{pawn.Pawn.LabelShort} deselected.");
                    break;
                case AssignmentResult.BlockedForced:
                    TolkHelper.Speak($"{pawn.Pawn.LabelShort} is forced for this role and cannot be changed.");
                    return;
                case AssignmentResult.BlockedDisabled:
                    TolkHelper.Speak($"Cannot assign {pawn.Pawn.LabelShort}. {failureReason}");
                    return;
                case AssignmentResult.Failed:
                    TolkHelper.Speak($"Cannot assign {pawn.Pawn.LabelShort}.");
                    return;
            }

            // Refresh the pawn list to reflect new ordering / counts.
            pawnViews.Clear();
            var rebuilt = adapter.BuildPawnList(selectedRole);
            if (rebuilt != null) pawnViews.AddRange(rebuilt);

            if (pawnIndex >= pawnViews.Count)
                pawnIndex = Math.Max(0, pawnViews.Count - 1);

            if (pawnViews.Count > 0 && pawnIndex >= 0 && pawnIndex < pawnViews.Count)
            {
                TolkHelper.Speak($"Now at: {pawnViews[pawnIndex].Pawn.LabelShort}");
            }

            adapter.Notify_AssignmentsChanged();
            AnnounceWarningDiff();
        }

        private static void InspectCurrentPawn()
        {
            var pawn = CurrentPawnOrNull();
            if (pawn == null) return;
            Find.WindowStack.Add(new Dialog_InfoCard(pawn));
        }

        private static void ShowPawnInfo(Func<Pawn, string> info)
        {
            var pawn = CurrentPawnOrNull();
            if (pawn == null) return;
            string text = info?.Invoke(pawn);
            if (!string.IsNullOrEmpty(text)) TolkHelper.Speak(text);
        }

        private static Pawn CurrentPawnOrNull()
        {
            if (pawnViews.Count == 0 || pawnIndex < 0 || pawnIndex >= pawnViews.Count) return null;
            return pawnViews[pawnIndex].Pawn;
        }

        private static void OpenQualityRowBreakdown()
        {
            if (qualityRows.Count == 0 || qualityIndex < 0 || qualityIndex >= qualityRows.Count) return;
            var row = qualityRows[qualityIndex];
            if (!string.IsNullOrEmpty(row.Explanation))
                StatBreakdownState.Open(row.Label, row.Explanation);
            else if (!string.IsNullOrEmpty(row.Tooltip))
                TolkHelper.Speak($"{row.Label}. {row.Tooltip}");
            else
                TolkHelper.Speak("No detailed breakdown available for this factor.");
        }

        private static void StartLordJob()
        {
            if (adapter == null)
            {
                TolkHelper.Speak("No dialog open.");
                return;
            }

            try
            {
                if (!adapter.TryStart(out var blocking))
                {
                    TolkHelper.Speak($"Cannot start. {string.Join(". ", blocking)}");
                    return;
                }

                isActive = false;
                var dialog = adapter.Dialog;
                dialog.OnAcceptKeyPressed();
                if (Find.WindowStack.IsOpen(dialog))
                {
                    isActive = true; // dialog still open → validation failed elsewhere; restore state
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[LordJobDialogState] Start failed: {ex.Message}");
                isActive = true;
            }
        }

        // === Announcements ===

        private static void AnnounceCurrentItem()
        {
            switch (currentMode)
            {
                case NavigationMode.RoleList: AnnounceCurrentRoleOrToggle(); break;
                case NavigationMode.PawnSelection: AnnounceCurrentPawn(); break;
                case NavigationMode.QualityStats: AnnounceCurrentQualityRow(); break;
            }
        }

        private static void AnnounceCurrentRoleOrToggle()
        {
            int total = TotalNavItemCount();
            if (total == 0)
            {
                TolkHelper.Speak("No roles available.");
                return;
            }
            if (roleIndex < 0 || roleIndex >= total) roleIndex = 0;

            string text;
            if (roleIndex < roleViews.Count)
            {
                text = RitualStatFormatter.FormatRole(roleViews[roleIndex]);
            }
            else
            {
                int toggleIdx = roleIndex - roleViews.Count;
                text = RitualStatFormatter.FormatExtraToggle(extraToggles[toggleIdx]);
            }

            string position = MenuHelper.FormatPosition(roleIndex, total);
            if (!string.IsNullOrEmpty(position)) text += $" {position}";

            TolkHelper.Speak(text);
        }

        private static void AnnounceCurrentPawn()
        {
            if (pawnViews.Count == 0)
            {
                TolkHelper.Speak("No pawns available.");
                return;
            }
            if (pawnIndex < 0 || pawnIndex >= pawnViews.Count) pawnIndex = 0;

            string text = RitualStatFormatter.FormatPawn(pawnViews[pawnIndex]);
            string position = MenuHelper.FormatPosition(pawnIndex, pawnViews.Count);
            if (!string.IsNullOrEmpty(position)) text += $" {position}";
            TolkHelper.Speak(text);
        }

        private static void AnnounceCurrentQualityRow()
        {
            if (qualityRows.Count == 0)
            {
                TolkHelper.Speak("No quality factors available.");
                return;
            }
            if (qualityIndex < 0 || qualityIndex >= qualityRows.Count) qualityIndex = 0;

            string text = RitualStatFormatter.FormatQualityRow(qualityRows[qualityIndex]);
            string position = MenuHelper.FormatPosition(qualityIndex, qualityRows.Count);
            if (!string.IsNullOrEmpty(position)) text += $" {position}";
            TolkHelper.Speak(text);
        }

        private static void AnnounceWarningDiff()
        {
            var current = adapter.ComputeWarnings() ?? (IReadOnlyList<string>)Array.Empty<string>();
            // Diff: announce newly-added warnings; ignore removed (less useful, less chatty).
            var newOnes = current.Where(w => !lastWarnings.Contains(w)).ToList();
            lastWarnings.Clear();
            lastWarnings.AddRange(current);
            if (newOnes.Count > 0)
            {
                TolkHelper.Speak($"Warning: {string.Join(" ", newOnes)}");
            }
        }

        // === Quality row construction ===

        private static void BuildQualityRows()
        {
            qualityRows.Clear();
            if (adapter == null) return;

            // Location row at top.
            var target = adapter.Target;
            if (target.IsValid)
            {
                string loc = FormatLocation(target);
                if (!string.IsNullOrEmpty(loc))
                {
                    qualityRows.Add(new LordJobQualityRow
                    {
                        Label = "Location",
                        Change = loc,
                        IsInformational = true,
                    });
                }
            }

            try
            {
                var factors = adapter.GetQualityFactors(out var range);
                if (factors != null && factors.Count > 0)
                {
                    foreach (var f in factors.OrderBy(qf => qf.priority))
                    {
                        if (f == null) continue;
                        string label = f.label ?? "Unknown";
                        if (!string.IsNullOrEmpty(f.count)) label += $" ({f.count})";
                        qualityRows.Add(new LordJobQualityRow
                        {
                            Label = label,
                            Change = f.qualityChange ?? "0%",
                            Quality = f.quality,
                            IsPresent = f.present,
                            IsPositive = f.positive,
                            IsUncertain = f.uncertainOutcome,
                            Tooltip = f.toolTip,
                        });
                    }
                }

                // Expected quality summary row.
                qualityRows.Add(new LordJobQualityRow
                {
                    Label = "Expected Quality",
                    Change = FormatRange(range),
                    IsInformational = true,
                });

                // Adapter-supplied extra rows (e.g., Ideology dev points).
                foreach (var row in adapter.BuildExtraQualityRows())
                {
                    qualityRows.Add(row);
                }

                // Outcome chances (Triumph 60%, Boring 30%, Disaster 10%, etc.) — vanilla shows
                // these in the quality description block; we surface them as additional rows.
                var outcomes = adapter.BuildOutcomeChances(range);
                if (outcomes != null && outcomes.Count > 0)
                {
                    qualityRows.Add(new LordJobQualityRow
                    {
                        Label = "Outcome chances",
                        Change = "",
                        IsInformational = true,
                    });
                    foreach (var oc in outcomes)
                    {
                        qualityRows.Add(new LordJobQualityRow
                        {
                            Label = $"  {oc.Label}",
                            Change = oc.Percentage.ToStringPercent("F0"),
                            IsInformational = true,
                            Tooltip = oc.Tooltip,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[LordJobDialogState] Error building quality rows: {ex.Message}");
                qualityRows.Add(new LordJobQualityRow
                {
                    Label = "Quality stats unavailable",
                    Change = "",
                    Tooltip = ex.Message,
                });
            }
        }

        private static string FormatRange(FloatRange range)
        {
            if (Math.Abs(range.min - range.max) < 0.01f) return range.min.ToStringPercent("F0");
            return $"{range.min.ToStringPercent("F0")} to {range.max.ToStringPercent("F0")}";
        }

        private static string FormatLocation(TargetInfo target)
        {
            var parts = new List<string>();
            if (target.Thing != null) parts.Add(target.Thing.LabelShortCap);

            if (target.HasThing || target.Cell.IsValid)
            {
                try
                {
                    Map map = target.Map;
                    IntVec3 cell = target.Cell;
                    if (map != null && cell.IsValid && cell.InBounds(map))
                    {
                        Room room = cell.GetRoom(map);
                        if (room != null && room.ProperRoom && !room.PsychologicallyOutdoors)
                        {
                            string label = room.GetRoomRoleLabel();
                            if (!string.IsNullOrEmpty(label)) parts.Add($"in {label}");
                        }
                        else if (room != null && room.PsychologicallyOutdoors)
                        {
                            parts.Add("outdoors");
                        }
                    }
                }
                catch { /* tolerate */ }
            }

            if (parts.Count == 0 && target.Cell.IsValid)
                return $"Cell ({target.Cell.x}, {target.Cell.z})";

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        // === Utilities ===

        private static void RebuildRoleAndToggleList()
        {
            roleViews.Clear();
            extraToggles.Clear();

            var roles = adapter.BuildRoleList();
            if (roles != null) roleViews.AddRange(roles);

            var toggles = adapter.BuildExtraToggles();
            if (toggles != null) extraToggles.AddRange(toggles);
        }

        private static int TotalNavItemCount() => roleViews.Count + extraToggles.Count;

        private static bool IsCurrentNavItemExtraToggle()
        {
            return roleIndex >= roleViews.Count && roleIndex < TotalNavItemCount();
        }

        private static void ToggleCurrentExtraToggle()
        {
            int idx = roleIndex - roleViews.Count;
            if (idx < 0 || idx >= extraToggles.Count) return;
            var toggle = extraToggles[idx];
            if (adapter.ApplyExtraToggle(toggle))
            {
                TolkHelper.Speak($"{toggle.Label}: {(toggle.Checked ? "checked" : "unchecked")}.");
            }
        }

        private static List<string> RoleListLabels()
        {
            var labels = new List<string>(TotalNavItemCount());
            foreach (var r in roleViews) labels.Add(r.Label);
            foreach (var t in extraToggles) labels.Add(t.Label);
            return labels;
        }
    }
}
