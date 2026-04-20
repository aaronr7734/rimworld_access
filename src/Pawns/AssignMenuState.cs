using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    public static class AssignMenuState
    {
        public static bool IsActive { get; private set; } = false;

        private static List<Pawn> pawnsList = new List<Pawn>();
        private static TabularMenuHelper<Pawn> tableHelper;

        // Submenu state
        private static bool isInSubmenu = false;
        private static List<AssignMenuHelper.SubmenuOption> submenuOptions = new List<AssignMenuHelper.SubmenuOption>();
        private static int submenuSelectedIndex = 0;
        private static TypeaheadSearchHelper submenuTypeahead = new TypeaheadSearchHelper();

        // Pending restore for returning from policy editors
        private static int pendingRestorePawnIndex = -1;
        private static int pendingRestoreColIndex = -1;

        // Public accessors for input routing
        public static TypeaheadSearchHelper Typeahead => tableHelper?.Typeahead;
        public static TypeaheadSearchHelper SubmenuTypeahead => submenuTypeahead;
        public static int CurrentPawnIndex => tableHelper?.CurrentRowIndex ?? 0;
        public static int SubmenuSelectedIndex => submenuSelectedIndex;
        public static bool IsInSubmenu => isInSubmenu;
        public static bool HasActiveSearch => tableHelper?.Typeahead?.HasActiveSearch ?? false;

        // === Lifecycle ===

        public static void Open()
        {
            // Handle return from policy editor while already active
            if (IsActive && pendingRestorePawnIndex >= 0)
            {
                tableHelper.CurrentRowIndex = Math.Min(pendingRestorePawnIndex, pawnsList.Count - 1);
                tableHelper.CurrentColumnIndex = Math.Min(pendingRestoreColIndex, AssignMenuHelper.GetColumnCount() - 1);
                pendingRestorePawnIndex = -1;
                pendingRestoreColIndex = -1;

                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                AnnounceCurrentCell(includeItemName: true);
                return;
            }

            if (IsActive) return;

            if (Find.CurrentMap == null)
            {
                TolkHelper.Speak("No map loaded");
                return;
            }

            // Build active columns
            AssignMenuHelper.BuildActiveColumns();

            // Match vanilla MainTabWindow_Assign: all maps + caravans + travelling transporters,
            // free colonists, no babies.
            pawnsList = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists
                .Where(p => !p.DevelopmentalStage.Baby())
                .ToList();

            if (pawnsList.Count == 0)
            {
                TolkHelper.Speak("No colonists found");
                return;
            }

            // Initialize table helper
            tableHelper = new TabularMenuHelper<Pawn>(
                getColumnCount: AssignMenuHelper.GetColumnCount,
                getItemLabel: AssignMenuHelper.GetPawnLabel,
                getColumnName: AssignMenuHelper.GetColumnName,
                getColumnValue: AssignMenuHelper.GetColumnValue,
                sortByColumn: (items, col, desc) => AssignMenuHelper.SortByColumn(items.ToList(), col, desc),
                getColumnTooltip: (pawn, col) => AssignMenuHelper.GetColumnTooltip(pawn, col),
                isColumnSortable: AssignMenuHelper.IsColumnSortable
            );

            // Default order matches vanilla: colonist-bar order via playerSettings.displayOrder.
            pawnsList = PlayerPawnsDisplayOrderUtility.InOrder(pawnsList).ToList();
            tableHelper.Reset();
            tableHelper.SetDefaultOrder(pawnsList);

            isInSubmenu = false;
            IsActive = true;

            // Check for pending restore from policy editor return
            if (pendingRestorePawnIndex >= 0)
            {
                tableHelper.CurrentRowIndex = Math.Min(pendingRestorePawnIndex, pawnsList.Count - 1);
                tableHelper.CurrentColumnIndex = Math.Min(pendingRestoreColIndex, AssignMenuHelper.GetColumnCount() - 1);
                pendingRestorePawnIndex = -1;
                pendingRestoreColIndex = -1;

                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                AnnounceCurrentCell(includeItemName: true);
                return;
            }

            // Try to start on the currently selected pawn
            Pawn selectedPawn = PawnSelectionState.LastSelectedPawn;
            if (selectedPawn != null)
            {
                int idx = pawnsList.IndexOf(selectedPawn);
                if (idx >= 0)
                    tableHelper.CurrentRowIndex = idx;
            }

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            string tabLabel = DefDatabase<MainButtonDef>.GetNamed("Assign").LabelCap;
            TolkHelper.Speak($"{tabLabel}, {pawnsList.Count} colonists");
            AnnounceCurrentCell(includeItemName: true);
        }

        public static void Close()
        {
            IsActive = false;
            isInSubmenu = false;
            pawnsList.Clear();
            submenuOptions.Clear();
            submenuSelectedIndex = 0;
            submenuTypeahead.ClearSearch();
            tableHelper?.ClearSearch();

            SoundDefOf.TabClose.PlayOneShotOnCamera();
            string tabLabel = DefDatabase<MainButtonDef>.GetNamed("Assign").LabelCap;
            TolkHelper.Speak($"{tabLabel} closed");
        }

        public static void PrepareForPolicyEditorReturn()
        {
            pendingRestorePawnIndex = tableHelper?.CurrentRowIndex ?? 0;
            pendingRestoreColIndex = tableHelper?.CurrentColumnIndex ?? 0;
        }

        // === Row Navigation ===

        public static void SelectNextPawn()
        {
            if (pawnsList.Count == 0) return;
            tableHelper.SelectNextRow(pawnsList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeItemName: true, includeColumnName: false);
        }

        public static void SelectPreviousPawn()
        {
            if (pawnsList.Count == 0) return;
            tableHelper.SelectPreviousRow(pawnsList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeItemName: true, includeColumnName: false);
        }

        public static void JumpToFirst()
        {
            if (pawnsList.Count == 0) return;
            tableHelper.JumpToFirst(pawnsList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeItemName: true, includeColumnName: false);
        }

        public static void JumpToLast()
        {
            if (pawnsList.Count == 0) return;
            tableHelper.JumpToLast(pawnsList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeItemName: true, includeColumnName: false);
        }

        // === Column Navigation ===

        public static void SelectNextColumn()
        {
            tableHelper.SelectNextColumn();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeItemName: false);
        }

        public static void SelectPreviousColumn()
        {
            tableHelper.SelectPreviousColumn();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeItemName: false);
        }

        // === Sorting ===

        public static void ToggleSortByCurrentColumn()
        {
            var result = tableHelper.ToggleSortByCurrentColumn(pawnsList, out string direction, out bool sortCleared);

            if (result == null)
            {
                string colName = tableHelper.GetCurrentColumnName();
                TolkHelper.Speak($"{colName} cannot be sorted");
                return;
            }

            pawnsList = result.ToList();

            if (sortCleared)
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                TolkHelper.Speak("Sort cleared, default order");
            }
            else
            {
                string columnName = tableHelper.GetCurrentColumnName();
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                TolkHelper.Speak($"Sorted by {columnName} ({direction})");
            }

            AnnounceCurrentCell(includeItemName: true);
        }

        // === Cell Interaction ===

        public static void InteractWithCurrentCell()
        {
            if (pawnsList.Count == 0) return;

            Pawn currentPawn = pawnsList[tableHelper.CurrentRowIndex];
            int colIndex = tableHelper.CurrentColumnIndex;
            var colType = AssignMenuHelper.GetColumnType(colIndex);

            switch (colType)
            {
                case AssignMenuHelper.AssignColumnType.Name:
                    JumpToPawnOnMap(currentPawn);
                    break;

                case AssignMenuHelper.AssignColumnType.Ideo:
                case AssignMenuHelper.AssignColumnType.Xenotype:
                    // Display-only: re-announce
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentCell(includeItemName: false);
                    break;

                default:
                    if (AssignMenuHelper.IsColumnInteractive(colIndex))
                        OpenSubmenu(currentPawn, colIndex);
                    else
                    {
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        AnnounceCurrentCell(includeItemName: false);
                    }
                    break;
            }
        }

        private static void JumpToPawnOnMap(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null)
            {
                TolkHelper.Speak("Pawn not on map", SpeechPriority.High);
                return;
            }

            IntVec3 position = pawn.Position;
            Close();

            MapNavigationState.CurrentCursorPosition = position;
            Find.CameraDriver?.JumpToCurrentMapLoc(position);
            TolkHelper.Speak($"Jumped to {pawn.LabelShort}");
        }

        public static void OpenInfoCard()
        {
            if (pawnsList == null || pawnsList.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No info card available");
                return;
            }

            Pawn pawn = pawnsList[tableHelper.CurrentRowIndex];
            if (pawn != null)
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No info card available");
            }
        }

        // === Submenu System ===

        private static void OpenSubmenu(Pawn pawn, int colIndex)
        {
            var colType = AssignMenuHelper.GetColumnType(colIndex);

            // Check if column is usable for this specific pawn
            if (colType == AssignMenuHelper.AssignColumnType.MedicineCarry && pawn.inventoryStock == null)
            {
                TolkHelper.Speak("N/A", SpeechPriority.High);
                return;
            }
            if ((colType == AssignMenuHelper.AssignColumnType.HostilityResponse ||
                 colType == AssignMenuHelper.AssignColumnType.MedicalCare) && pawn.playerSettings == null)
            {
                TolkHelper.Speak("N/A", SpeechPriority.High);
                return;
            }

            submenuOptions = AssignMenuHelper.GetSubmenuOptions(colIndex, pawn);

            if (submenuOptions.Count == 0)
            {
                TolkHelper.Speak("No options available");
                return;
            }

            // Pre-select the current value
            submenuSelectedIndex = AssignMenuHelper.GetCurrentSubmenuIndex(colIndex, pawn, submenuOptions);
            submenuTypeahead.ClearSearch();
            isInSubmenu = true;

            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        public static void SubmenuSelectNext()
        {
            if (submenuOptions.Count == 0) return;
            submenuSelectedIndex = (submenuSelectedIndex + 1) % submenuOptions.Count;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        public static void SubmenuSelectPrevious()
        {
            if (submenuOptions.Count == 0) return;
            submenuSelectedIndex = (submenuSelectedIndex - 1 + submenuOptions.Count) % submenuOptions.Count;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        public static void SubmenuApply()
        {
            if (pawnsList.Count == 0 || submenuOptions.Count == 0) return;

            Pawn currentPawn = pawnsList[tableHelper.CurrentRowIndex];
            var option = submenuOptions[submenuSelectedIndex];

            option.Apply(currentPawn);

            SoundDefOf.Click.PlayOneShotOnCamera();

            string colName = AssignMenuHelper.GetColumnName(tableHelper.CurrentColumnIndex);
            TolkHelper.Speak($"{currentPawn.LabelShort}: {colName} set to {option.Label}");

            CloseSubmenu();
            ResortAfterEdit();
        }

        /// <summary>
        /// Re-sorts the list if currently sorted by the column that was just edited.
        /// Keeps cursor at same index and announces the new item at that position.
        /// </summary>
        private static void ResortAfterEdit()
        {
            var resorted = tableHelper.ResortAfterEdit(pawnsList);
            if (resorted != null)
            {
                pawnsList = resorted.ToList();
                string announcement = "Now at " + tableHelper.BuildCellAnnouncement(
                    pawnsList[tableHelper.CurrentRowIndex], pawnsList.Count, includeItemName: true);
                TolkHelper.Speak(announcement);
            }
        }

        public static void SubmenuCancel()
        {
            CloseSubmenu();
        }

        private static void CloseSubmenu()
        {
            isInSubmenu = false;
            submenuOptions.Clear();
            submenuSelectedIndex = 0;
            submenuTypeahead.ClearSearch();
        }

        public static void AnnounceSubmenuOption()
        {
            if (submenuOptions.Count == 0) return;

            var option = submenuOptions[submenuSelectedIndex];
            string position = MenuHelper.FormatPosition(submenuSelectedIndex, submenuOptions.Count);
            string announcement = $"{option.Label}. {position}";

            if (submenuTypeahead.HasActiveSearch)
            {
                announcement += $", match {submenuTypeahead.CurrentMatchPosition} of {submenuTypeahead.MatchCount} for '{submenuTypeahead.SearchBuffer}'";
            }

            TolkHelper.Speak(announcement);
        }

        public static List<string> GetSubmenuOptionLabels()
        {
            var labels = new List<string>();
            foreach (var option in submenuOptions)
                labels.Add(option.Label);
            return labels;
        }

        public static void SetSubmenuSelectedIndex(int index)
        {
            if (index >= 0 && index < submenuOptions.Count)
                submenuSelectedIndex = index;
        }

        // === Submenu Typeahead ===

        public static void SubmenuHandleTypeahead(char c)
        {
            var labels = GetSubmenuOptionLabels();
            if (submenuTypeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                    submenuSelectedIndex = newIndex;
                AnnounceSubmenuOption();
            }
            else
            {
                TolkHelper.Speak($"No matches for '{submenuTypeahead.LastFailedSearch}'");
            }
        }

        public static void SubmenuHandleBackspace()
        {
            if (!submenuTypeahead.HasActiveSearch) return;

            var labels = GetSubmenuOptionLabels();
            if (submenuTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                    submenuSelectedIndex = newIndex;
                AnnounceSubmenuOption();
            }
        }

        // === Policy Shortcuts ===

        public static bool HandlePolicyShortcut(AssignMenuHelper.PolicyAction action)
        {
            if (pawnsList.Count == 0) return false;

            int colIndex = tableHelper.CurrentColumnIndex;
            if (!AssignMenuHelper.IsColumnPolicyType(colIndex))
                return false;

            Pawn currentPawn = pawnsList[tableHelper.CurrentRowIndex];
            Policy policy = null;

            if (isInSubmenu && submenuOptions != null &&
                submenuSelectedIndex >= 0 && submenuSelectedIndex < submenuOptions.Count)
            {
                policy = submenuOptions[submenuSelectedIndex].PolicyObject;
            }

            Action editCallback = BuildEditCallback(colIndex, currentPawn, policy);
            Action refreshCallback = () => AnnounceCurrentCell(includeItemName: false);

            return AssignMenuHelper.ExecutePolicyAction(
                action, colIndex, currentPawn, refreshCallback, editCallback, policy);
        }

        private static Action BuildEditCallback(int colIndex, Pawn pawn, Policy policy)
        {
            var colType = AssignMenuHelper.GetColumnType(colIndex);
            PolicyEditorState.PolicyType? editorType = null;

            switch (colType)
            {
                case AssignMenuHelper.AssignColumnType.Outfit:
                    policy = policy ?? pawn.outfits?.CurrentApparelPolicy;
                    editorType = PolicyEditorState.PolicyType.Apparel;
                    break;
                case AssignMenuHelper.AssignColumnType.FoodRestriction:
                    policy = policy ?? pawn.foodRestriction?.CurrentFoodPolicy;
                    editorType = PolicyEditorState.PolicyType.Food;
                    break;
                case AssignMenuHelper.AssignColumnType.DrugPolicy:
                    policy = policy ?? pawn.drugs?.CurrentPolicy;
                    editorType = PolicyEditorState.PolicyType.Drug;
                    break;
                case AssignMenuHelper.AssignColumnType.ReadingPolicy:
                    policy = policy ?? pawn.reading?.CurrentPolicy;
                    editorType = PolicyEditorState.PolicyType.Reading;
                    break;
            }

            if (editorType == null || policy == null) return null;

            var capturedPolicy = policy;
            var capturedType = editorType.Value;
            return () =>
            {
                PrepareForPolicyEditorReturn();
                PolicyEditorState.Open(capturedType, capturedPolicy, () => AssignMenuState.Open());
            };
        }

        // === Context Menu ===

        public static void OpenContextMenu()
        {
            if (pawnsList.Count == 0) return;

            int colIndex = tableHelper.CurrentColumnIndex;

            if (!AssignMenuHelper.HasContextMenu(colIndex))
            {
                TolkHelper.Speak("No context menu for this column");
                return;
            }

            Pawn currentPawn = pawnsList[tableHelper.CurrentRowIndex];

            // Build edit callback for policy columns
            Action editCallback = null;
            var colType = AssignMenuHelper.GetColumnType(colIndex);

            switch (colType)
            {
                case AssignMenuHelper.AssignColumnType.Outfit:
                    editCallback = () =>
                    {
                        var policy = currentPawn.outfits?.CurrentApparelPolicy;
                        if (policy == null) return;
                        PrepareForPolicyEditorReturn();
                        PolicyEditorState.Open(PolicyEditorState.PolicyType.Apparel, policy, () =>
                        {
                            AssignMenuState.Open();
                        });
                    };
                    break;

                case AssignMenuHelper.AssignColumnType.FoodRestriction:
                    editCallback = () =>
                    {
                        var policy = currentPawn.foodRestriction?.CurrentFoodPolicy;
                        if (policy == null) return;
                        PrepareForPolicyEditorReturn();
                        PolicyEditorState.Open(PolicyEditorState.PolicyType.Food, policy, () =>
                        {
                            AssignMenuState.Open();
                        });
                    };
                    break;

                case AssignMenuHelper.AssignColumnType.DrugPolicy:
                    editCallback = () =>
                    {
                        var policy = currentPawn.drugs?.CurrentPolicy;
                        if (policy == null) return;
                        PrepareForPolicyEditorReturn();
                        PolicyEditorState.Open(PolicyEditorState.PolicyType.Drug, policy, () =>
                        {
                            AssignMenuState.Open();
                        });
                    };
                    break;

                case AssignMenuHelper.AssignColumnType.ReadingPolicy:
                    editCallback = () =>
                    {
                        var policy = currentPawn.reading?.CurrentPolicy;
                        if (policy == null) return;
                        PrepareForPolicyEditorReturn();
                        PolicyEditorState.Open(PolicyEditorState.PolicyType.Reading, policy, () =>
                        {
                            AssignMenuState.Open();
                        });
                    };
                    break;
            }

            // Build refresh callback
            Action refreshCallback = () =>
            {
                // Rebuild submenu options won't work since context menu closes submenu
                // Just re-announce the cell with potentially updated value
                AnnounceCurrentCell(includeItemName: false);
            };

            var options = AssignMenuHelper.GetContextMenuOptions(
                colIndex, currentPawn, refreshCallback, editCallback);

            if (options == null || options.Count == 0)
            {
                TolkHelper.Speak("No context menu for this column");
                return;
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        public static void OpenSubmenuContextMenu()
        {
            if (!isInSubmenu || submenuOptions.Count == 0) return;
            if (submenuSelectedIndex < 0 || submenuSelectedIndex >= submenuOptions.Count) return;

            var selectedOption = submenuOptions[submenuSelectedIndex];
            var policy = selectedOption.PolicyObject;
            if (policy == null)
            {
                TolkHelper.Speak("No context menu for this item");
                return;
            }

            int colIndex = tableHelper.CurrentColumnIndex;
            Pawn currentPawn = pawnsList[tableHelper.CurrentRowIndex];

            // Build edit callback targeting the highlighted policy
            Action editCallback = null;
            var colType = AssignMenuHelper.GetColumnType(colIndex);

            switch (colType)
            {
                case AssignMenuHelper.AssignColumnType.Outfit:
                    editCallback = () =>
                    {
                        PrepareForPolicyEditorReturn();
                        PolicyEditorState.Open(PolicyEditorState.PolicyType.Apparel, policy, () =>
                        {
                            AssignMenuState.Open();
                        });
                    };
                    break;

                case AssignMenuHelper.AssignColumnType.FoodRestriction:
                    editCallback = () =>
                    {
                        PrepareForPolicyEditorReturn();
                        PolicyEditorState.Open(PolicyEditorState.PolicyType.Food, policy, () =>
                        {
                            AssignMenuState.Open();
                        });
                    };
                    break;

                case AssignMenuHelper.AssignColumnType.DrugPolicy:
                    editCallback = () =>
                    {
                        PrepareForPolicyEditorReturn();
                        PolicyEditorState.Open(PolicyEditorState.PolicyType.Drug, policy, () =>
                        {
                            AssignMenuState.Open();
                        });
                    };
                    break;

                case AssignMenuHelper.AssignColumnType.ReadingPolicy:
                    editCallback = () =>
                    {
                        PrepareForPolicyEditorReturn();
                        PolicyEditorState.Open(PolicyEditorState.PolicyType.Reading, policy, () =>
                        {
                            AssignMenuState.Open();
                        });
                    };
                    break;
            }

            Action refreshCallback = () =>
            {
                AnnounceCurrentCell(includeItemName: false);
            };

            var options = AssignMenuHelper.GetContextMenuOptions(
                colIndex, currentPawn, refreshCallback, editCallback, policy);

            if (options == null || options.Count == 0)
            {
                TolkHelper.Speak("No context menu for this item");
                return;
            }

            // Close submenu before opening context menu
            isInSubmenu = false;
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        // === Paint (Shift+Up/Down) ===

        public static void PaintDown()
        {
            if (pawnsList.Count <= 1) return;

            int colIndex = tableHelper.CurrentColumnIndex;
            if (!AssignMenuHelper.CanPaintColumn(colIndex))
            {
                TolkHelper.Speak("Cannot paint this column");
                return;
            }

            Pawn sourcePawn = pawnsList[tableHelper.CurrentRowIndex];
            string sourceValue = AssignMenuHelper.GetColumnValue(sourcePawn, colIndex);

            tableHelper.SelectNextRow(pawnsList.Count);
            Pawn targetPawn = pawnsList[tableHelper.CurrentRowIndex];

            string targetValue = AssignMenuHelper.GetColumnValue(targetPawn, colIndex);
            string position = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, pawnsList.Count);

            if (sourceValue == targetValue)
            {
                string colName = AssignMenuHelper.GetColumnName(colIndex);
                TolkHelper.Speak($"{targetPawn.LabelShort}: {colName} already {targetValue}. {position}");
                return;
            }

            AssignMenuHelper.ApplyValueToPawn(sourcePawn, targetPawn, colIndex);
            SoundDefOf.Click.PlayOneShotOnCamera();
            string value = AssignMenuHelper.GetColumnValue(targetPawn, colIndex);
            TolkHelper.Speak($"{targetPawn.LabelShort}: {value} applied. {position}");
        }

        public static void PaintUp()
        {
            if (pawnsList.Count <= 1) return;

            int colIndex = tableHelper.CurrentColumnIndex;
            if (!AssignMenuHelper.CanPaintColumn(colIndex))
            {
                TolkHelper.Speak("Cannot paint this column");
                return;
            }

            Pawn sourcePawn = pawnsList[tableHelper.CurrentRowIndex];
            string sourceValue = AssignMenuHelper.GetColumnValue(sourcePawn, colIndex);

            tableHelper.SelectPreviousRow(pawnsList.Count);
            Pawn targetPawn = pawnsList[tableHelper.CurrentRowIndex];

            string targetValue = AssignMenuHelper.GetColumnValue(targetPawn, colIndex);
            string position = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, pawnsList.Count);

            if (sourceValue == targetValue)
            {
                string colName = AssignMenuHelper.GetColumnName(colIndex);
                TolkHelper.Speak($"{targetPawn.LabelShort}: {colName} already {targetValue}. {position}");
                return;
            }

            AssignMenuHelper.ApplyValueToPawn(sourcePawn, targetPawn, colIndex);
            SoundDefOf.Click.PlayOneShotOnCamera();
            string value = AssignMenuHelper.GetColumnValue(targetPawn, colIndex);
            TolkHelper.Speak($"{targetPawn.LabelShort}: {value} applied. {position}");
        }

        /// <summary>
        /// Bulk paints the current value from the current row to the last row.
        /// </summary>
        public static void PaintToLast()
        {
            PaintBulk(towardFirst: false, entireColumn: false);
        }

        /// <summary>
        /// Bulk paints the current value from the current row to the first row.
        /// </summary>
        public static void PaintToFirst()
        {
            PaintBulk(towardFirst: true, entireColumn: false);
        }

        /// <summary>
        /// Paints the current value to the entire column.
        /// </summary>
        public static void PaintEntireColumn(bool towardFirst)
        {
            PaintBulk(towardFirst, entireColumn: true);
        }

        private static void PaintBulk(bool towardFirst, bool entireColumn)
        {
            if (isInSubmenu) return;
            if (pawnsList.Count == 0) return;

            int col = tableHelper.CurrentColumnIndex;
            if (!AssignMenuHelper.CanPaintColumn(col))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("Cannot paint this column");
                return;
            }

            int currentRow = tableHelper.CurrentRowIndex;
            Pawn sourcePawn = pawnsList[currentRow];
            string sourceValue = AssignMenuHelper.GetColumnValue(sourcePawn, col);
            string colName = AssignMenuHelper.GetColumnName(col);

            int startRow, endRow;
            if (entireColumn)
            {
                startRow = 0;
                endRow = pawnsList.Count - 1;
            }
            else if (towardFirst)
            {
                startRow = 0;
                endRow = currentRow;
            }
            else
            {
                startRow = currentRow;
                endRow = pawnsList.Count - 1;
            }

            var changed = new List<string>();
            for (int i = startRow; i <= endRow; i++)
            {
                Pawn target = pawnsList[i];
                string beforeValue = AssignMenuHelper.GetColumnValue(target, col);
                if (beforeValue != sourceValue)
                {
                    AssignMenuHelper.ApplyValueToPawn(sourcePawn, target, col);
                    changed.Add(target.LabelShort);
                }
            }

            tableHelper.CurrentRowIndex = towardFirst ? startRow : endRow;

            if (changed.Count > 0)
            {
                BulkSoundQueue.Queue(changed.Count, SoundDefOf.Click);
                TolkHelper.Speak($"Painted {colName} to {sourceValue} for {MenuHelper.FormatNameList(changed)}");
            }
            else
            {
                TolkHelper.Speak($"{colName} already {sourceValue} for all colonists");
            }
        }

        // === Main Table Typeahead ===

        public static void HandleTypeahead(char c)
        {
            if (tableHelper.HandleTypeahead(c, pawnsList, out _))
            {
                AnnounceWithSearch();
            }
            else
            {
                TolkHelper.Speak($"No matches for '{tableHelper.Typeahead.LastFailedSearch}'");
            }
        }

        public static void HandleBackspace()
        {
            if (!tableHelper.Typeahead.HasActiveSearch) return;
            tableHelper.HandleBackspace(pawnsList, out _);
            AnnounceWithSearch();
        }

        public static List<string> GetItemLabels()
        {
            return tableHelper.GetItemLabels(pawnsList);
        }

        public static void SetCurrentPawnIndex(int index)
        {
            if (index >= 0 && index < pawnsList.Count)
                tableHelper.CurrentRowIndex = index;
        }

        // === Announcements ===

        public static void AnnounceCurrentCell(bool includeItemName, bool includeColumnName = true)
        {
            if (pawnsList.Count == 0) return;

            Pawn currentPawn = pawnsList[tableHelper.CurrentRowIndex];
            string announcement = tableHelper.BuildCellAnnouncement(currentPawn, pawnsList.Count, includeItemName, includeColumnName);
            TolkHelper.Speak(announcement);
        }

        public static void AnnounceWithSearch()
        {
            if (pawnsList.Count == 0)
            {
                TolkHelper.Speak("No colonists");
                return;
            }

            Pawn currentPawn = pawnsList[tableHelper.CurrentRowIndex];
            string announcement = tableHelper.BuildCellAnnouncementWithSearch(currentPawn, pawnsList.Count);
            TolkHelper.Speak(announcement);
        }
    }
}
