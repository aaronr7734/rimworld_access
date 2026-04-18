using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    public static class MechsMenuState
    {
        public static bool IsActive { get; private set; } = false;

        private static List<Pawn> mechsList = new List<Pawn>();
        private static TabularMenuHelper<Pawn> tableHelper;

        // Submenu state
        private enum SubmenuType { None, ControlGroup, WorkMode, AllowedArea }
        private static SubmenuType activeSubmenu = SubmenuType.None;
        private static int submenuSelectedIndex = 0;
        private static List<object> submenuOptions = new List<object>();
        private static TypeaheadSearchHelper submenuTypeahead = new TypeaheadSearchHelper();

        // Last applied area for bulk operations (null = Unrestricted)
        private static Area lastAppliedArea = null;

        public static TypeaheadSearchHelper Typeahead => tableHelper?.Typeahead;
        public static TypeaheadSearchHelper SubmenuTypeahead => submenuTypeahead;
        public static int CurrentMechIndex => tableHelper?.CurrentRowIndex ?? 0;
        public static int SubmenuSelectedIndex => submenuSelectedIndex;
        public static bool IsInSubmenu => activeSubmenu != SubmenuType.None;

        public static void Open()
        {
            if (IsActive) return;

            if (!ModsConfig.BiotechActive)
            {
                TolkHelper.Speak("Biotech DLC required");
                return;
            }

            if (!GuardHelper.RequireMap()) return;

            // Get all player mechs with overseers (matches MainTabWindow_Mechs.Pawns)
            mechsList = Find.CurrentMap.mapPawns.PawnsInFaction(Faction.OfPlayer)
                .Where(p => p.RaceProps.IsMechanoid && p.OverseerSubject != null)
                .ToList();

            if (mechsList.Count == 0)
            {
                TolkHelper.Speak("No mechs found");
                return;
            }

            // Initialize column defs for game-native sorting
            MechsMenuHelper.InitColumnDefs();

            // Apply default sort matching PawnTable_Mechs.LabelSortFunction:
            // Overseer → ControlGroupIndex → KindLabel → Label
            mechsList = mechsList
                .OrderBy(p => p.GetOverseer()?.thingIDNumber ?? int.MaxValue)
                .ThenBy(p => p.GetMechControlGroup()?.Index ?? int.MaxValue)
                .ThenBy(p => p.KindLabel)
                .ThenBy(p => p.Label)
                .ToList();

            // Initialize table helper
            tableHelper = new TabularMenuHelper<Pawn>(
                getColumnCount: MechsMenuHelper.GetTotalColumnCount,
                getItemLabel: MechsMenuHelper.GetMechName,
                getColumnName: MechsMenuHelper.GetColumnName,
                getColumnValue: MechsMenuHelper.GetColumnValue,
                sortByColumn: (items, col, desc) => MechsMenuHelper.SortMechsByColumn(items.ToList(), col, desc),
                getColumnTooltip: (pawn, col) => MechsMenuHelper.GetColumnTooltip(pawn, col),
                isColumnSortable: MechsMenuHelper.IsColumnSortable
            );

            tableHelper.Reset();
            tableHelper.SetDefaultOrder(mechsList);
            activeSubmenu = SubmenuType.None;
            IsActive = true;

            SoundDefOf.TabOpen.PlayOneShotOnCamera();

            string announcement = $"Mechs menu, {mechsList.Count} mechs";
            TolkHelper.Speak(announcement);
            AnnounceCurrentCell(includeMechName: true);
        }

        public static void Close(bool silent = false)
        {
            IsActive = false;
            activeSubmenu = SubmenuType.None;
            mechsList.Clear();
            tableHelper?.ClearSearch();

            Find.DesignatorManager?.Deselect();

            if (!silent)
            {
                SoundDefOf.TabClose.PlayOneShotOnCamera();
                TolkHelper.Speak("Mechs menu closed");
            }
        }

        // === Navigation ===

        public static void SelectNextMech()
        {
            if (mechsList.Count == 0) return;
            tableHelper.SelectNextRow(mechsList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeMechName: true);
        }

        public static void SelectPreviousMech()
        {
            if (mechsList.Count == 0) return;
            tableHelper.SelectPreviousRow(mechsList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeMechName: true);
        }

        public static void SelectNextColumn()
        {
            tableHelper.SelectNextColumn();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeMechName: false);
        }

        public static void SelectPreviousColumn()
        {
            tableHelper.SelectPreviousColumn();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeMechName: false);
        }

        private static void AnnounceCurrentCell(bool includeMechName = true)
        {
            if (mechsList.Count == 0) return;

            Pawn currentMech = mechsList[tableHelper.CurrentRowIndex];
            string announcement = tableHelper.BuildCellAnnouncement(currentMech, mechsList.Count, includeMechName);
            TolkHelper.Speak(announcement);
        }

        public static void OpenInfoCard()
        {
            Pawn mech = null;
            if (mechsList != null && mechsList.Count > 0)
            {
                mech = mechsList[tableHelper.CurrentRowIndex];
            }
            if (mech != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(mech));
            }
            else
            {
                InfoCardState.SpeakNoInfoCardAvailable();
            }
        }

        // === Cell Interaction ===

        public static void InteractWithCurrentCell()
        {
            if (mechsList.Count == 0) return;

            Pawn currentMech = mechsList[tableHelper.CurrentRowIndex];
            int col = tableHelper.CurrentColumnIndex;

            if (!MechsMenuHelper.IsColumnInteractive(col))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentCell(includeMechName: false);
                return;
            }

            switch ((MechsMenuHelper.ColumnType)col)
            {
                case MechsMenuHelper.ColumnType.Name:
                    JumpToMechOnMap(currentMech);
                    break;
                case MechsMenuHelper.ColumnType.Draft:
                    ToggleDraft(currentMech);
                    break;
                case MechsMenuHelper.ColumnType.AutoRepair:
                    ToggleAutoRepair(currentMech);
                    break;
                case MechsMenuHelper.ColumnType.ControlGroup:
                    OpenControlGroupSubmenu(currentMech);
                    break;
                case MechsMenuHelper.ColumnType.WorkMode:
                    OpenWorkModeSubmenu(currentMech);
                    break;
                case MechsMenuHelper.ColumnType.AllowedArea:
                    OpenAllowedAreaSubmenu(currentMech);
                    break;
                default:
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentCell(includeMechName: false);
                    break;
            }
        }

        private static void JumpToMechOnMap(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null)
            {
                TolkHelper.Speak("Mech not on map", SpeechPriority.High);
                return;
            }

            IntVec3 position = pawn.Position;
            Close();

            MapNavigationState.CurrentCursorPosition = position;
            Find.CameraDriver?.JumpToCurrentMapLoc(position);

            string mechName = pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.LabelCap.ToString();
            MapNavigationState.SpeakJumpedTo(mechName);
        }

        private static void ToggleDraft(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive || !pawn.IsColonyMechPlayerControlled || !pawn.Spawned)
            {
                TolkHelper.Speak("Cannot draft this mech", SpeechPriority.High);
                return;
            }

            AcceptanceReport canDraft = MechanitorUtility.CanDraftMech(pawn);
            if (!canDraft)
            {
                string reason = canDraft.Reason.NullOrEmpty() ? "Cannot draft" : canDraft.Reason;
                TolkHelper.Speak(reason, SpeechPriority.High);
                return;
            }

            pawn.drafter.Drafted = !pawn.Drafted;

            if (pawn.Drafted)
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            else
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();

            AnnounceCurrentCell(includeMechName: false);
            ResortAfterEdit();
        }

        private static void ToggleAutoRepair(Pawn pawn)
        {
            CompMechRepairable comp = pawn.GetComp<CompMechRepairable>();
            if (comp == null)
            {
                TolkHelper.Speak("No auto-repair component", SpeechPriority.High);
                return;
            }

            comp.autoRepair = !comp.autoRepair;

            if (comp.autoRepair)
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            else
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();

            AnnounceCurrentCell(includeMechName: false);
            ResortAfterEdit();
        }

        // === Submenu: Control Group ===

        private static void OpenControlGroupSubmenu(Pawn pawn)
        {
            if (pawn.IsGestating())
            {
                TolkHelper.Speak("Gestating".Translate().Resolve(), SpeechPriority.High);
                return;
            }

            Pawn overseer = pawn.GetOverseer();
            if (overseer?.mechanitor == null)
            {
                TolkHelper.Speak("No overseer", SpeechPriority.High);
                return;
            }

            var groups = overseer.mechanitor.controlGroups;
            MechanitorControlGroup currentGroup = pawn.GetMechControlGroup();

            submenuOptions.Clear();
            foreach (var group in groups)
                submenuOptions.Add(group);

            submenuSelectedIndex = 0;
            if (currentGroup != null)
            {
                int idx = groups.IndexOf(currentGroup);
                if (idx >= 0)
                    submenuSelectedIndex = idx;
            }

            activeSubmenu = SubmenuType.ControlGroup;
            submenuTypeahead.ClearSearch();
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        // === Submenu: Work Mode ===

        private static void OpenWorkModeSubmenu(Pawn pawn)
        {
            MechanitorControlGroup group = pawn.GetMechControlGroup();
            if (group == null)
            {
                TolkHelper.Speak("No control group", SpeechPriority.High);
                return;
            }

            var modes = DefDatabase<MechWorkModeDef>.AllDefsListForReading
                .OrderBy(d => d.uiOrder).ToList();

            submenuOptions.Clear();
            submenuOptions.AddRange(modes.Cast<object>());

            submenuSelectedIndex = 0;
            int currentIdx = modes.IndexOf(group.WorkMode);
            if (currentIdx >= 0)
                submenuSelectedIndex = currentIdx;

            activeSubmenu = SubmenuType.WorkMode;
            submenuTypeahead.ClearSearch();
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        // === Submenu: Allowed Area ===

        private const string ManageAreasMarker = "ManageAreas";

        private static void OpenAllowedAreaSubmenu(Pawn pawn)
        {
            List<Area> areas = MechsMenuHelper.GetAvailableAreas();

            submenuOptions.Clear();
            submenuOptions.Add(ManageAreasMarker);
            submenuOptions.Add(null); // Unrestricted
            submenuOptions.AddRange(areas.Cast<object>());

            submenuSelectedIndex = 1; // Default to "Unrestricted"

            if (pawn.playerSettings?.AreaRestrictionInPawnCurrentMap != null)
            {
                for (int i = 0; i < areas.Count; i++)
                {
                    if (areas[i] == pawn.playerSettings.AreaRestrictionInPawnCurrentMap)
                    {
                        submenuSelectedIndex = i + 2; // +2 for ManageAreas and null
                        break;
                    }
                }
            }

            activeSubmenu = SubmenuType.AllowedArea;
            submenuTypeahead.ClearSearch();
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        // === Submenu Navigation ===

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
            ApplySubmenuSelection();
        }

        public static void SubmenuCancel()
        {
            CloseSubmenu();
        }

        public static List<string> GetSubmenuOptionLabels()
        {
            var labels = new List<string>();
            foreach (var option in submenuOptions)
                labels.Add(GetSubmenuOptionText(option));
            return labels;
        }

        private static string GetSubmenuOptionText(object option)
        {
            if (option is string s && s == ManageAreasMarker)
                return "ManageAreas".Translate().Resolve();

            if (option == null)
                return "Unrestricted".Translate().Resolve();

            if (option is MechanitorControlGroup group)
            {
                Pawn currentMech = mechsList.Count > 0 ? mechsList[tableHelper.CurrentRowIndex] : null;
                MechanitorControlGroup currentGroup = currentMech?.GetMechControlGroup();
                string label = group.LabelIndexWithWorkMode;
                return group == currentGroup ? $"{label} (current)" : label;
            }

            if (option is MechWorkModeDef mode)
            {
                Pawn currentMech = mechsList.Count > 0 ? mechsList[tableHelper.CurrentRowIndex] : null;
                MechanitorControlGroup currentGroup = currentMech?.GetMechControlGroup();
                string label = mode.LabelCap.Resolve();
                return currentGroup?.WorkMode == mode ? $"{label} (current)" : label;
            }

            if (option is Area area)
                return area.Label;

            return "Unknown";
        }

        public static void SetSubmenuSelectedIndex(int index)
        {
            if (index >= 0 && index < submenuOptions.Count)
                submenuSelectedIndex = index;
        }

        public static void SubmenuHandleTypeahead(char c)
        {
            var labels = GetSubmenuOptionLabels();
            if (submenuTypeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    submenuSelectedIndex = newIndex;
                    AnnounceSubmenuWithSearch();
                }
            }
            else
            {
                submenuTypeahead.SpeakNoMatches();
            }
        }

        public static void SubmenuHandleBackspace()
        {
            if (!submenuTypeahead.HasActiveSearch)
                return;

            var labels = GetSubmenuOptionLabels();
            if (submenuTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                    submenuSelectedIndex = newIndex;
                AnnounceSubmenuWithSearch();
            }
        }

        public static void AnnounceSubmenuWithSearch()
        {
            if (submenuOptions.Count == 0) return;

            object selectedOption = submenuOptions[submenuSelectedIndex];
            string optionText = GetSubmenuOptionText(selectedOption);
            string position = MenuHelper.FormatPosition(submenuSelectedIndex, submenuOptions.Count);

            string announcement = $"{optionText}. {position}";

            if (submenuTypeahead.HasActiveSearch)
            {
                announcement += submenuTypeahead.BuildSearchContextSuffix();
            }

            TolkHelper.Speak(announcement);
        }

        private static void AnnounceSubmenuOption()
        {
            if (submenuOptions.Count == 0) return;

            object selectedOption = submenuOptions[submenuSelectedIndex];
            string optionText = GetSubmenuOptionText(selectedOption);

            // Add description for work modes
            string extra = "";
            if (selectedOption is MechWorkModeDef mode && !mode.description.NullOrEmpty())
                extra = $". {mode.description}";

            string announcement = $"{optionText}{extra} ({MenuHelper.FormatPosition(submenuSelectedIndex, submenuOptions.Count)})";
            TolkHelper.Speak(announcement);
        }

        private static void ApplySubmenuSelection()
        {
            if (mechsList.Count == 0 || submenuOptions.Count == 0) return;

            Pawn currentMech = mechsList[tableHelper.CurrentRowIndex];
            object selectedOption = submenuOptions[submenuSelectedIndex];

            switch (activeSubmenu)
            {
                case SubmenuType.ControlGroup:
                    if (selectedOption is MechanitorControlGroup group)
                    {
                        MechanitorControlGroup currentGroup = currentMech.GetMechControlGroup();
                        if (group == currentGroup)
                        {
                            TolkHelper.Speak("Already in this group");
                            return;
                        }
                        group.Assign(currentMech);
                    }
                    break;

                case SubmenuType.WorkMode:
                    if (selectedOption is MechWorkModeDef mode)
                    {
                        MechanitorControlGroup mechGroup = currentMech.GetMechControlGroup();
                        if (mechGroup == null) break;

                        if (mechGroup.WorkMode == mode)
                        {
                            TolkHelper.Speak("Already set to this mode");
                            return;
                        }

                        mechGroup.SetWorkMode(mode);
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        CloseSubmenu();
                        TolkHelper.Speak($"Work mode set to {mode.LabelCap} for control group {mechGroup.Index}");
                        AnnounceCurrentCell(includeMechName: false);
                        ResortAfterEdit();
                        return;
                    }
                    break;

                case SubmenuType.AllowedArea:
                    if (selectedOption is string marker && marker == ManageAreasMarker)
                    {
                        int savedMechIndex = tableHelper.CurrentRowIndex;
                        int savedColumn = tableHelper.CurrentColumnIndex;
                        Map currentMap = Find.CurrentMap;
                        CloseSubmenu();

                        Action returnToSubmenuCallback = () => {
                            if (savedMechIndex < mechsList.Count)
                            {
                                tableHelper.CurrentRowIndex = savedMechIndex;
                                tableHelper.CurrentColumnIndex = savedColumn;
                                OpenAllowedAreaSubmenu(mechsList[savedMechIndex]);
                                submenuSelectedIndex = 0;
                                AnnounceSubmenuOption();
                            }
                        };

                        Action reopenManageAreasCallback = () => {
                            Find.DesignatorManager?.Deselect();

                            if (currentMap != null && Find.CurrentMap == currentMap)
                                WindowlessAreaState.Open(currentMap, returnToSubmenuCallback);
                            else
                                returnToSubmenuCallback();
                        };

                        WindowlessAreaState.Open(currentMap, reopenManageAreasCallback);
                        return;
                    }

                    if (currentMech.playerSettings != null)
                    {
                        Area area = selectedOption as Area;
                        currentMech.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                        lastAppliedArea = area;
                    }
                    break;
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
            CloseSubmenu();
            AnnounceCurrentCell(includeMechName: false);
            ResortAfterEdit();
        }

        private static void CloseSubmenu()
        {
            activeSubmenu = SubmenuType.None;
            submenuOptions.Clear();
            submenuSelectedIndex = 0;
            submenuTypeahead.ClearSearch();
        }

        // === Sorting ===

        private static void ResortAfterEdit()
        {
            var resorted = tableHelper.ResortAfterEdit(mechsList);
            if (resorted != null)
            {
                mechsList = resorted.ToList();
                string announcement = "Now at " + tableHelper.BuildCellAnnouncement(
                    mechsList[tableHelper.CurrentRowIndex], mechsList.Count, includeItemName: true);
                TolkHelper.Speak(announcement);
            }
        }

        public static void ToggleSortByCurrentColumn()
        {
            var result = tableHelper.ToggleSortByCurrentColumn(mechsList, out string direction, out bool sortCleared);

            if (result == null)
            {
                string colName = tableHelper.GetCurrentColumnName();
                TolkHelper.Speak($"{colName} cannot be sorted");
                return;
            }

            mechsList = result.ToList();

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

            AnnounceCurrentCell(includeMechName: true);
        }

        #region Typeahead Search

        public static List<string> GetItemLabels()
        {
            return tableHelper.GetItemLabels(mechsList);
        }

        public static void SetCurrentMechIndex(int index)
        {
            if (index >= 0 && index < mechsList.Count)
                tableHelper.CurrentRowIndex = index;
        }

        public static void HandleTypeahead(char c)
        {
            if (tableHelper.HandleTypeahead(c, mechsList, out _))
            {
                AnnounceWithSearch();
            }
            else
            {
                tableHelper.Typeahead.SpeakNoMatches();
            }
        }

        public static void HandleBackspace()
        {
            if (!tableHelper.Typeahead.HasActiveSearch)
                return;

            tableHelper.HandleBackspace(mechsList, out _);
            AnnounceWithSearch();
        }

        public static void AnnounceWithSearch()
        {
            if (mechsList.Count == 0)
            {
                TolkHelper.Speak("No mechs");
                return;
            }

            Pawn currentMech = mechsList[tableHelper.CurrentRowIndex];
            string announcement = tableHelper.BuildCellAnnouncementWithSearch(currentMech, mechsList.Count);
            TolkHelper.Speak(announcement);
        }

        public static void JumpToFirst()
        {
            if (mechsList.Count == 0) return;

            tableHelper.JumpToFirst(mechsList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeMechName: true);
        }

        public static void JumpToLast()
        {
            if (mechsList.Count == 0) return;

            tableHelper.JumpToLast(mechsList.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentCell(includeMechName: true);
        }

        #endregion

        #region Painting

        public static void PaintDown()
        {
            PaintSingleStep(forward: true);
        }

        public static void PaintUp()
        {
            PaintSingleStep(forward: false);
        }

        private static void PaintSingleStep(bool forward)
        {
            if (activeSubmenu != SubmenuType.None) return;
            if (mechsList.Count <= 1) return;

            int col = tableHelper.CurrentColumnIndex;
            if (!MechsMenuHelper.CanPaintColumn(col))
            {
                MenuHelper.SpeakCannotPaintColumn();
                return;
            }

            Pawn sourcePawn = mechsList[tableHelper.CurrentRowIndex];
            var columnType = (MechsMenuHelper.ColumnType)col;

            // AllowedArea painting
            if (columnType == MechsMenuHelper.ColumnType.AllowedArea)
            {
                lastAppliedArea = sourcePawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
                if (forward)
                    tableHelper.SelectNextRow(mechsList.Count);
                else
                    tableHelper.SelectPreviousRow(mechsList.Count);

                Pawn target = mechsList[tableHelper.CurrentRowIndex];
                string areaName = lastAppliedArea?.Label ?? "Unrestricted".Translate().Resolve();
                string position = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, mechsList.Count);
                Area targetArea = target.playerSettings?.AreaRestrictionInPawnCurrentMap;

                if (targetArea == lastAppliedArea)
                {
                    TolkHelper.Speak($"{target.LabelShort}: already set to {areaName}. {position}");
                }
                else
                {
                    if (target.playerSettings != null)
                        target.playerSettings.AreaRestrictionInPawnCurrentMap = lastAppliedArea;
                    SoundDefOf.Designate_DragStandard_Changed_NoCam.PlayOneShotOnCamera();
                    TolkHelper.Speak($"{target.LabelShort}: {areaName} applied. {position}");
                }
                return;
            }

            // ControlGroup painting
            if (columnType == MechsMenuHelper.ColumnType.ControlGroup)
            {
                MechanitorControlGroup sourceGroup = sourcePawn.GetMechControlGroup();
                if (sourceGroup == null)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("No control group to paint");
                    return;
                }

                if (forward)
                    tableHelper.SelectNextRow(mechsList.Count);
                else
                    tableHelper.SelectPreviousRow(mechsList.Count);

                Pawn target = mechsList[tableHelper.CurrentRowIndex];
                string position = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, mechsList.Count);

                if (target.IsGestating())
                {
                    TolkHelper.Speak($"{target.LabelShort}: gestating, cannot assign. {position}");
                    return;
                }

                // Check same overseer
                if (target.GetOverseer() != sourcePawn.GetOverseer())
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak($"{target.LabelShort}: different overseer, cannot paint. {position}");
                    return;
                }

                MechanitorControlGroup targetGroup = target.GetMechControlGroup();
                if (targetGroup == sourceGroup)
                {
                    TolkHelper.Speak($"{target.LabelShort}: already in group {sourceGroup.Index}. {position}");
                    return;
                }

                sourceGroup.Assign(target);
                SoundDefOf.Click.PlayOneShotOnCamera();
                TolkHelper.Speak($"{target.LabelShort}: assigned to group {sourceGroup.Index}. {position}");
                return;
            }

            // Boolean columns (Draft, AutoRepair)
            bool brushValue = MechsMenuHelper.GetPaintableValue(sourcePawn, col);
            if (forward)
                tableHelper.SelectNextRow(mechsList.Count);
            else
                tableHelper.SelectPreviousRow(mechsList.Count);
            Pawn targetPawn = mechsList[tableHelper.CurrentRowIndex];

            string colName = MechsMenuHelper.GetColumnName(col);
            string valueLabel = MechsMenuHelper.GetPaintValueLabel(col, brushValue);
            string pos = MenuHelper.FormatPosition(tableHelper.CurrentRowIndex, mechsList.Count);

            bool targetValue = MechsMenuHelper.GetPaintableValue(targetPawn, col);
            if (targetValue == brushValue)
            {
                TolkHelper.Speak($"{targetPawn.LabelShort}: {colName} already {valueLabel}. {pos}");
                return;
            }

            bool applied = MechsMenuHelper.SetPaintableValue(targetPawn, col, brushValue);
            SoundDef sound = applied
                ? MechsMenuHelper.GetPaintSound(col, brushValue)
                : SoundDefOf.ClickReject;
            sound.PlayOneShotOnCamera();

            if (applied)
                TolkHelper.Speak($"{targetPawn.LabelShort}: {colName} {valueLabel}. {pos}");
            else
                TolkHelper.Speak($"{targetPawn.LabelShort}: cannot apply {colName}. {pos}");
        }

        public static void PaintToLast()
        {
            PaintBulk(towardFirst: false, entireColumn: false);
        }

        public static void PaintToFirst()
        {
            PaintBulk(towardFirst: true, entireColumn: false);
        }

        public static void PaintEntireColumn(bool towardFirst)
        {
            PaintBulk(towardFirst, entireColumn: true);
        }

        private static void PaintBulk(bool towardFirst, bool entireColumn)
        {
            if (activeSubmenu != SubmenuType.None) return;
            if (mechsList.Count == 0) return;

            int col = tableHelper.CurrentColumnIndex;
            if (!MechsMenuHelper.CanPaintColumn(col))
            {
                MenuHelper.SpeakCannotPaintColumn();
                return;
            }

            var columnType = (MechsMenuHelper.ColumnType)col;
            int currentRow = tableHelper.CurrentRowIndex;
            string colName = MechsMenuHelper.GetColumnName(col);

            int startRow, endRow;
            if (entireColumn)
            {
                startRow = 0;
                endRow = mechsList.Count - 1;
            }
            else if (towardFirst)
            {
                startRow = 0;
                endRow = currentRow;
            }
            else
            {
                startRow = currentRow;
                endRow = mechsList.Count - 1;
            }

            // AllowedArea bulk painting
            if (columnType == MechsMenuHelper.ColumnType.AllowedArea)
            {
                Pawn source = mechsList[currentRow];
                lastAppliedArea = source.playerSettings?.AreaRestrictionInPawnCurrentMap;
                string areaName = lastAppliedArea?.Label ?? "Unrestricted".Translate().Resolve();

                var changedNames = new List<string>();
                for (int i = startRow; i <= endRow; i++)
                {
                    Pawn pawn = mechsList[i];
                    if (pawn.playerSettings == null) continue;
                    if (pawn.playerSettings.AreaRestrictionInPawnCurrentMap != lastAppliedArea)
                    {
                        pawn.playerSettings.AreaRestrictionInPawnCurrentMap = lastAppliedArea;
                        changedNames.Add(pawn.LabelShort);
                    }
                }

                tableHelper.CurrentRowIndex = towardFirst ? startRow : endRow;
                BulkSoundQueue.Queue(changedNames.Count, SoundDefOf.Designate_DragStandard_Changed_NoCam);

                string announcement = changedNames.Count > 0
                    ? $"Painted {colName} {areaName} for {string.Join(", ", changedNames)}"
                    : $"All already set to {areaName}";
                TolkHelper.Speak(announcement);
                return;
            }

            // ControlGroup bulk painting
            if (columnType == MechsMenuHelper.ColumnType.ControlGroup)
            {
                Pawn source = mechsList[currentRow];
                MechanitorControlGroup sourceGroup = source.GetMechControlGroup();
                if (sourceGroup == null)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("No control group to paint");
                    return;
                }
                Pawn sourceOverseer = source.GetOverseer();

                var changedNames = new List<string>();
                for (int i = startRow; i <= endRow; i++)
                {
                    Pawn pawn = mechsList[i];
                    if (pawn.IsGestating()) continue;
                    if (pawn.GetOverseer() != sourceOverseer) continue;
                    if (pawn.GetMechControlGroup() == sourceGroup) continue;
                    sourceGroup.Assign(pawn);
                    changedNames.Add(pawn.LabelShort);
                }

                tableHelper.CurrentRowIndex = towardFirst ? startRow : endRow;
                BulkSoundQueue.Queue(changedNames.Count, SoundDefOf.Click);

                string announcement = changedNames.Count > 0
                    ? $"Painted {colName} group {sourceGroup.Index} for {string.Join(", ", changedNames)}"
                    : $"All already in group {sourceGroup.Index} (or different overseer)";
                TolkHelper.Speak(announcement);
                return;
            }

            // Boolean columns (Draft, AutoRepair)
            Pawn brushPawn = mechsList[currentRow];
            bool brushValue = MechsMenuHelper.GetPaintableValue(brushPawn, col);
            string valueLabel = MechsMenuHelper.GetPaintValueLabel(col, brushValue);

            var changed = new List<string>();
            for (int i = startRow; i <= endRow; i++)
            {
                Pawn pawn = mechsList[i];
                bool current = MechsMenuHelper.GetPaintableValue(pawn, col);
                if (current != brushValue)
                {
                    if (MechsMenuHelper.SetPaintableValue(pawn, col, brushValue))
                        changed.Add(pawn.LabelShort);
                }
            }

            tableHelper.CurrentRowIndex = towardFirst ? startRow : endRow;
            SoundDef bulkSound = MechsMenuHelper.GetPaintSound(col, brushValue);
            BulkSoundQueue.Queue(changed.Count, bulkSound);

            string bulkAnnouncement = changed.Count > 0
                ? $"Painted {colName} {valueLabel} for {string.Join(", ", changed)}"
                : $"All already {valueLabel}";
            TolkHelper.Speak(bulkAnnouncement);
        }

        #endregion
    }
}
