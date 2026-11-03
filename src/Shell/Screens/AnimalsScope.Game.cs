using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for the colony animals table (vanilla's Animals tab: every tame
    /// colony animal, tabular by training/master/area columns, with per-column pickers for
    /// Master/Allowed Area/Medical Care). The mod owns no window here — the surface is the
    /// windowless <see cref="AnimalsMenuState"/> — so the scope rides the focus stack through
    /// <see cref="AnimalsScopeMirror"/>.
    ///
    /// ONE table region plus the automatic Buttons region holding this screen's single action,
    /// ManageAutoSlaughter. That action is a declared button rather than a claimed chord because
    /// Tab is the universal region-cycle key and the base's unconditional claim always wins a
    /// tied key.
    ///
    /// SUBMENU sub-mode (the three pickers): rather than a second region, the SAME region
    /// changes shape — <see cref="ContentColumnCount"/> reports 0 while a submenu is open, so
    /// the region becomes a flat option list and the row cursor is reused for the picked option.
    /// That reuse makes the original animal row unrecoverable from the model, so
    /// <see cref="submenuAnimalRow"/> remembers it explicitly. Submenu navigation and typeahead
    /// keep their own announcement pair: a bolt-on picker is not a table cell and sits outside
    /// the ComposeCell contract.
    ///
    /// All row/cursor/search/submenu state is instance state — popping the scope IS the reset.
    /// <see cref="AnimalsMenuState"/> survives only as the bridge <see cref="AnimalsMenuPatch"/>
    /// and the map-ambient guards read.
    /// </summary>
    public sealed class AnimalsScope : ScreenScope, IPawnTableFocusSource
    {
        private const int AnimalsRegion = 0;
        private const string ManageAreasMarker = "ManageAreas";

        private enum SubmenuType { None, Master, AllowedArea, MedicalCare }

        private readonly List<Pawn> animalsList = new List<Pawn>();
        private List<Pawn> defaultOrder;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        private SubmenuType activeSubmenu = SubmenuType.None;
        private readonly List<object> submenuOptions = new List<object>();
        private readonly TypeaheadSearchHelper submenuTypeahead = new TypeaheadSearchHelper();
        private int submenuAnimalRow = -1;
        private Area lastAppliedArea;

        private bool announcedOpen;

        public AnimalsScope()
        {
            Claim(SharedMenuGrammar.Cancel, delegate { HandleCancelKey(); });
            Claim(SharedMenuGrammar.SearchBackspace, delegate { HandleBackspaceKey(); },
                when: () => IsInSubmenu && submenuTypeahead.HasActiveSearch);

            Claim("animals.infoCard", delegate { OpenInfoCard(); }, when: BrowseMode);

            // Painting: Shift+Up/Down single-cell, Shift+Home/End bulk-to-edge, Ctrl+Shift+Home/
            // End entire column. No base ScreenScope equivalent; browse-only.
            Claim("animals.paintUp", delegate { PaintUp(); }, when: BrowseMode);
            Claim("animals.paintDown", delegate { PaintDown(); }, when: BrowseMode);
            Claim("animals.paintToFirst", delegate { PaintToFirst(); }, when: BrowseMode);
            Claim("animals.paintToLast", delegate { PaintToLast(); }, when: BrowseMode);
            Claim("animals.paintEntireColumn",
                delegate (KeyEventSnapshot e) { PaintEntireColumn(e.Key == KeyCode.Home); }, when: BrowseMode);
        }

        public override string Name
        {
            get { return "animals"; }
        }

        /// <summary>Shared cross-region typeahead for the animal table; the submenu picker keeps its own search (see HandleChar).</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Windowless modal: this scope owns Escape itself (close the menu / cancel a submenu / clear a search).</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        private bool IsInSubmenu
        {
            get { return activeSubmenu != SubmenuType.None; }
        }

        private static bool BrowseMode()
        {
            return true;
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Reuses vanilla's own Animals tab label — no new translation key.</summary>
        protected override string ContentRegionName(int region)
        {
            MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail("Animals");
            return def != null ? def.LabelCap.Resolve() : "Animals";
        }

        /// <summary>0 while a submenu is open (a flat option list), else the real animal-table column count.</summary>
        protected override int ContentColumnCount(int region)
        {
            return IsInSubmenu ? 0 : AnimalsMenuHelper.GetTotalColumnCount();
        }

        protected override int ContentItemCount(int region)
        {
            return IsInSubmenu ? submenuOptions.Count : animalsList.Count;
        }

        /// <summary>
        /// One-time population: the roster is snapshotted here and never silently re-fetched,
        /// since re-querying the map every refresh would discard an active sort or the cursor
        /// position. Rebuilds only when the cached list is empty. Submenu option lists are
        /// populated by their own Open* methods.
        /// </summary>
        protected override void RefreshContent()
        {
            if (animalsList.Count > 0)
                return;
            Map map = Find.CurrentMap;
            if (map == null)
                return;
            List<Pawn> initial = map.mapPawns.ColonyAnimals.ToList();
            if (initial.Count == 0)
                return;

            AnimalsMenuHelper.InitColumnDefs();
            // Vanilla's default order: name column, ascending.
            List<Pawn> sorted = AnimalsMenuHelper.SortAnimalsByColumn(initial, 0, false);
            animalsList.AddRange(sorted);
            defaultOrder = new List<Pawn>(animalsList);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (IsInSubmenu)
            {
                d.Label = index >= 0 && index < submenuOptions.Count ? GetSubmenuOptionText(submenuOptions[index]) : "";
                return d;
            }
            if (index >= 0 && index < animalsList.Count)
            {
                d.Label = AnimalsMenuHelper.GetNameWithKind(animalsList[index]);
            }
            return d;
        }

        /// <summary>
        /// Row default: in submenu mode Enter applies the highlighted option; in table mode this
        /// runs only for the display-only columns, which say they are read only.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (IsInSubmenu)
            {
                ApplySubmenuSelection(index);
                return;
            }
            if (index < 0 || index >= animalsList.Count)
                return;
            AnnounceCurrentCellReadOnly();
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            return new TableColumnInfo(
                AnimalsMenuHelper.GetColumnName(column),
                AnimalsMenuHelper.GetColumnTooltip(null, column),
                AnimalsMenuHelper.IsColumnSortable(column));
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (row < 0 || row >= animalsList.Count)
                return "";
            return AnimalsMenuHelper.GetColumnValue(animalsList[row], column);
        }

        protected override bool TryGetPawnCell(int region, int row, int column, out PawnColumnDef def, out Pawn pawn)
        {
            bool inRange = !IsInSubmenu && row >= 0 && row < animalsList.Count;
            def = inRange ? AnimalsMenuHelper.GetDef(column) : null;
            pawn = inRange ? animalsList[row] : null;
            return def != null;
        }

        /// <summary>Registry cell tips for Unknown-classified (typically modded) columns only; the curated columns speak through their own value phrasing.</summary>
        protected override string ContentCellTip(int region, int row, int column)
        {
            if (IsInSubmenu || row < 0 || row >= animalsList.Count)
                return null;
            return AnimalsMenuHelper.GetUnknownCellTip(animalsList[row], column);
        }

        /// <summary>Enter on an interactive column: designation checkboxes toggle, Master/AllowedArea/MedicalCare open a submenu, Name jumps to the animal — dispatched by the column's own worker-type classification, never its index.</summary>
        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (row < 0 || row >= animalsList.Count)
                return false;
            if (AnimalsMenuHelper.GetColumnType(column) == AnimalsMenuHelper.ColumnType.Unknown)
                return ActivatePawnCellTableless(region, row, column);
            if (!AnimalsMenuHelper.IsColumnInteractive(column))
                return false;

            Pawn pawn = animalsList[row];
            switch (AnimalsMenuHelper.GetColumnType(column))
            {
                case AnimalsMenuHelper.ColumnType.Name: JumpToAnimalOnMap(pawn); return true;
                case AnimalsMenuHelper.ColumnType.Trainable: ToggleTraining(pawn, column); return true;
                case AnimalsMenuHelper.ColumnType.SpecialTrainable: ToggleSpecialTrainable(pawn); return true;
                case AnimalsMenuHelper.ColumnType.FollowDrafted: ToggleFollowDrafted(pawn); return true;
                case AnimalsMenuHelper.ColumnType.FollowFieldwork: ToggleFollowFieldwork(pawn); return true;
                case AnimalsMenuHelper.ColumnType.AnimalDig: ToggleAnimalDig(pawn); return true;
                case AnimalsMenuHelper.ColumnType.AnimalForage: ToggleAnimalForage(pawn); return true;
                case AnimalsMenuHelper.ColumnType.Master: OpenMasterSubmenu(pawn); return true;
                case AnimalsMenuHelper.ColumnType.Sterile: ToggleSterilization(pawn); return true;
                case AnimalsMenuHelper.ColumnType.Slaughter: ToggleSlaughter(pawn); return true;
                case AnimalsMenuHelper.ColumnType.MedicalCare: OpenMedicalCareSubmenu(pawn); return true;
                case AnimalsMenuHelper.ColumnType.ReleaseToWild: ToggleReleaseToWild(pawn); return true;
                case AnimalsMenuHelper.ColumnType.AllowedArea: OpenAllowedAreaSubmenu(pawn); return true;
                default: return false; // Gender/Age/LifeStage/Pregnant/MentalState/Bond are display-only; Unknown has no bespoke reader.
            }
        }

        /// <summary>
        /// Re-order for a new sort state using the game's own column
        /// comparers (PawnColumnWorker.Compare via PawnColumnSortHelper);
        /// "cleared" restores the name-ascending default captured at open.
        /// </summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            Pawn currentPawn = currentRow >= 0 && currentRow < animalsList.Count ? animalsList[currentRow] : null;
            List<Pawn> reordered = cycle == SortCycleResult.Cleared
                ? new List<Pawn>(defaultOrder ?? animalsList)
                : AnimalsMenuHelper.SortAnimalsByColumn(animalsList, column, cycle == SortCycleResult.SortedDescending);

            animalsList.Clear();
            animalsList.AddRange(reordered);

            if (currentPawn == null)
                return 0;
            int index = animalsList.IndexOf(currentPawn);
            return index >= 0 ? index : 0;
        }

        /// <summary>The one action this screen has: vanilla's ManageAutoSlaughter dialog button (MainTabWindow_Animals.DoWindowContents).</summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                // No actionId: Tab means "cycle regions" at the ScreenScope level, so speaking it
                // as this button's hotkey would be misleading.
                actions.Add(new ScreenAction("ManageAutoSlaughter".Translate().ToString(), OpenAutoSlaughter));
                return actions;
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            animalsList.Clear();
            defaultOrder = null;
            TypeaheadReset();
            activeSubmenu = SubmenuType.None;
            submenuOptions.Clear();
            submenuTypeahead.ClearSearch();
            submenuAnimalRow = -1;
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            if (animalsList.Count == 0)
                return; // AnimalsMenuState.Open() already refused to open/announce for an empty roster.
            ScreenReturn.LandTableCursor(Name, Model.CurrentTable, animalsList);

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            string tabCount = TabCountFragment();
            string opening = "RimWorldAccess.Animals.Menu.OpeningTitle".Translate(animalsList.Count).ToString();
            TolkHelper.SpeakData(string.IsNullOrEmpty(tabCount) ? opening : tabCount + ". " + opening);
            AnnounceCurrentItem();
        }

        private void HandleCancelKey()
        {
            if (IsInSubmenu)
            {
                if (submenuTypeahead.HasActiveSearch)
                {
                    submenuTypeahead.ClearSearchAndAnnounce();
                    AnnounceSubmenuWithSearch();
                }
                else
                {
                    CloseSubmenuInternal();
                }
                return;
            }
            // The base typeahead claim, registered ahead of this one, handles Escape-clears-search,
            // so reaching here means no search is active.
            Close();
        }

        private void HandleBackspaceKey()
        {
            List<string> labels = submenuOptions.ConvertAll(GetSubmenuOptionText);
            if (submenuTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    RefreshModel();
                    Model.CurrentRegion?.MoveTo(newIndex);
                }
                AnnounceSubmenuWithSearch();
            }
        }

        /// <summary>
        /// The submenu picker keeps its own search — its options are a flat list outside the
        /// table contract; the main table rides the base cross-region engine.
        /// </summary>
        public override bool HandleChar(char c)
        {
            if (!IsInSubmenu)
                return base.HandleChar(c);
            if (!TypeaheadMatcher.AcceptsSearchChar(c, submenuTypeahead.HasActiveSearch))
                return false;
            List<string> labels = submenuOptions.ConvertAll(GetSubmenuOptionText);
            if (submenuTypeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    RefreshModel();
                    Model.CurrentRegion?.MoveTo(newIndex);
                    AnnounceSubmenuWithSearch();
                }
            }
            else
            {
                submenuTypeahead.SpeakNoMatches();
            }
            return true;
        }

        /// <summary>
        /// Up/Down: submenu mode steps through the options (wrapping, search-aware); table mode
        /// defers to the base row cursor.
        /// </summary>
        protected override void MoveItem(int delta)
        {
            if (IsInSubmenu)
            {
                SubmenuMove(delta);
                return;
            }
            base.MoveItem(delta);
        }

        /// <summary>
        /// Home/End: silent in submenu mode; table mode defers to the base.
        /// </summary>
        protected override void MoveItemEdge(bool first)
        {
            if (IsInSubmenu)
                return;
            base.MoveItemEdge(first);
        }

        private void OpenInfoCard()
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            int row = table == null ? -1 : table.Rows.Index - 1;
            Pawn animal = row >= 0 && row < animalsList.Count ? animalsList[row] : null;
            if (animal != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(animal));
            }
            else
            {
                InfoCardState.SpeakNoInfoCardAvailable();
            }
        }

        private void JumpToAnimalOnMap(Pawn pawn)
        {
            ScreenReturn.JumpToPawn(Name, MainTabWindowLink.Animals, Model.CurrentTable, Close,
                pawn, "RimWorldAccess.Animals.Menu.NotOnMap");
        }

        private void OpenAutoSlaughter()
        {
            if (Find.CurrentMap != null)
            {
                Find.WindowStack.Add(new Dialog_AutoSlaughter(Find.CurrentMap));
            }
        }

        /// <summary>
        /// Toggles the Slaughter designation through PawnColumnWorker_Slaughter's own SetValue
        /// (vehicle A, via PawnColumnMutationHelper — this scope has no live PawnTable to hand
        /// it). Notify_DesignationAdded is what raises the bonded- and venerated-animal warnings;
        /// ShouldConfirmDesignation opens the blocking non-player-faction confirm. `afterward`
        /// plays the sound and re-announces once the mutation lands, immediately or from that
        /// dialog's accept delegate.
        /// </summary>
        private void ToggleSlaughter(Pawn pawn)
        {
            if (pawn.Map == null)
                return;
            bool newValue = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Slaughter) == null;
            PawnColumnDef columnDef = DefDatabase<PawnColumnDef>.GetNamedSilentFail("Slaughter");
            PawnTable table = PawnColumnMutationHelper.CreateDetachedTable(PawnTableDefOf.Animals);
            PawnColumnMutationHelper.SetDesignatorValue(columnDef, pawn, newValue, table, afterward: delegate
            {
                (newValue ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
                AfterCellEdit();
            });
        }

        private void ToggleTraining(Pawn pawn, int columnIndex)
        {
            TrainableDef trainable = AnimalsMenuHelper.GetTrainableAtColumn(columnIndex);
            if (trainable == null || pawn.training == null)
                return;
            AcceptanceReport canTrain = pawn.training.CanAssignToTrain(trainable);
            if (!canTrain.Accepted)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Cell.CannotTrainInSkill".Loc(pawn.Name.ToStringShort, trainable.LabelCap), SpeechPriority.High);
                return;
            }
            bool currentlyWanted = pawn.training.GetWanted(trainable);
            pawn.training.SetWantedRecursive(trainable, !currentlyWanted);
            (currentlyWanted ? SoundDefOf.Checkbox_TurnedOff : SoundDefOf.Checkbox_TurnedOn).PlayOneShotOnCamera();
            AfterCellEdit();
        }

        private void ToggleFollowDrafted(Pawn pawn)
        {
            if (pawn.playerSettings == null)
                return;
            if (pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Cell.RequiresTraining".Loc(pawn.LabelShort, TrainableDefOf.Obedience.LabelCap), SpeechPriority.High);
                return;
            }
            pawn.playerSettings.followDrafted = !pawn.playerSettings.followDrafted;
            (pawn.playerSettings.followDrafted ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            AfterCellEdit();
        }

        private void ToggleFollowFieldwork(Pawn pawn)
        {
            if (pawn.playerSettings == null)
                return;
            if (pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Cell.RequiresTraining".Loc(pawn.LabelShort, TrainableDefOf.Obedience.LabelCap), SpeechPriority.High);
                return;
            }
            pawn.playerSettings.followFieldwork = !pawn.playerSettings.followFieldwork;
            (pawn.playerSettings.followFieldwork ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            AfterCellEdit();
        }

        private void ToggleAnimalDig(Pawn pawn)
        {
            if (pawn.playerSettings == null)
                return;
            if (pawn.training?.HasLearned(TrainableDefOf.Dig) != true)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Cell.HasNotLearned".Loc(pawn.LabelShort, TrainableDefOf.Dig.LabelCap), SpeechPriority.High);
                return;
            }
            pawn.playerSettings.animalDig = !pawn.playerSettings.animalDig;
            (pawn.playerSettings.animalDig ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            AfterCellEdit();
        }

        private void ToggleAnimalForage(Pawn pawn)
        {
            if (pawn.playerSettings == null)
                return;
            if (pawn.training?.HasLearned(TrainableDefOf.Forage) != true)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Cell.HasNotLearned".Loc(pawn.LabelShort, TrainableDefOf.Forage.LabelCap), SpeechPriority.High);
                return;
            }
            pawn.playerSettings.animalForage = !pawn.playerSettings.animalForage;
            (pawn.playerSettings.animalForage ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            AfterCellEdit();
        }

        private void ToggleSpecialTrainable(Pawn pawn)
        {
            List<TrainableDef> specialTrainables = AnimalsMenuHelper.GetSpecialTrainables(pawn);
            if (specialTrainables.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Cell.NoSpecialAbilities".Loc(pawn.LabelShort), SpeechPriority.High);
                return;
            }
            if (pawn.training == null)
                return;

            bool anyWanted = specialTrainables.Any(t => pawn.training.GetWanted(t));
            bool newState = !anyWanted;
            foreach (TrainableDef trainable in specialTrainables)
            {
                pawn.training.SetWantedRecursive(trainable, newState);
            }
            (newState ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            AfterCellEdit();
        }

        /// <summary>
        /// Toggles the Release-to-Wild designation through PawnColumnWorker_ReleaseAnimalToWild's
        /// own SetValue — see <see cref="ToggleSlaughter"/>. Unlike Slaughter's,
        /// Notify_DesignationAdded here ALWAYS fires the bonded-animal warning, and wraps it in
        /// vanilla's non-player-faction Dialog_Confirm when the designating faction differs from
        /// the animal's.
        /// </summary>
        private void ToggleReleaseToWild(Pawn pawn)
        {
            if (pawn.Map == null)
                return;
            bool newValue = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.ReleaseAnimalToWild) == null;
            PawnColumnDef columnDef = DefDatabase<PawnColumnDef>.GetNamedSilentFail("ReleaseAnimalToWild");
            PawnTable table = PawnColumnMutationHelper.CreateDetachedTable(PawnTableDefOf.Animals);
            PawnColumnMutationHelper.SetDesignatorValue(columnDef, pawn, newValue, table, afterward: delegate
            {
                (newValue ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
                AfterCellEdit();
            });
        }

        /// <summary>
        /// Schedules/cancels the Sterilize bill through PawnColumnMutationHelper.SetSterilizeValue,
        /// which mirrors PawnColumnWorker_Sterilize.SetValue's AnimalSterile/HomeFaction-confirm
        /// gate — Sterilize has no isolated vehicle the way Designator's
        /// ShouldConfirmDesignation/DesignationConfirmed split gives Slaughter and Release.
        /// </summary>
        private void ToggleSterilization(Pawn pawn)
        {
            if (AnimalsMenuHelper.IsAnimalSterilized(pawn))
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Cell.AlreadySterilized".Loc(pawn.LabelShort), SpeechPriority.High);
                return;
            }
            bool newValue = !AnimalsMenuHelper.HasSterilizationScheduled(pawn);
            PawnColumnMutationHelper.SetSterilizeValue(pawn, newValue, afterward: delegate
            {
                (newValue ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
                AfterCellEdit();
            });
        }

        /// <summary>
        /// Re-sorts in place when the table is actively sorted by the just-edited column, keeping
        /// the cursor at the same ROW POSITION rather than following the edited item, and speaks
        /// a "now at {cell}" phrase; otherwise speaks the edited cell through the shared composer.
        /// The wrapper phrase is hand-built because ScreenScope's cell-composing path is private
        /// and offers no hook to prefix a composed cell string.
        /// </summary>
        private void AfterCellEdit()
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table != null && table.HasActiveSort && table.SortColumnIndex == table.ColumnIndex)
            {
                List<Pawn> resorted = AnimalsMenuHelper.SortAnimalsByColumn(animalsList, table.ColumnIndex, table.SortDescending);
                animalsList.Clear();
                animalsList.AddRange(resorted);
                int currentRow = table.Rows.Index - 1;
                int clamped = Math.Min(currentRow, animalsList.Count - 1);
                if (clamped >= 0)
                {
                    table.Rows.MoveTo(clamped + 1);
                    string cellText = BuildCellText(clamped, includeItemName: true);
                    TolkHelper.SpeakData("RimWorldAccess.Animals.Sort.NowAt".Translate(cellText).ToString());
                    return;
                }
            }
            // Toggle doctrine: the cursor didn't move, so speak only the new state.
            AnnounceCurrentCellStateChange();
        }

        /// <summary>"{animal} - {column}: {value}" (or just the value when it echoes the animal name) — shared by <see cref="AfterCellEdit"/>.</summary>
        private string BuildCellText(int row, bool includeItemName)
        {
            if (row < 0 || row >= animalsList.Count)
                return "";
            Pawn pawn = animalsList[row];
            int column = Model.CurrentTable?.ColumnIndex ?? 0;
            string columnValue = AnimalsMenuHelper.GetColumnValue(pawn, column);
            if (!includeItemName)
                return columnValue;
            string itemLabel = AnimalsMenuHelper.GetNameWithKind(pawn);
            if (itemLabel == columnValue || columnValue.StartsWith(itemLabel))
                return columnValue;
            string columnName = AnimalsMenuHelper.GetColumnName(column);
            return $"{itemLabel} - {columnName}: {columnValue}";
        }

        private void OpenSubmenu(SubmenuType type, int selectedIndex)
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            submenuAnimalRow = table != null ? table.Rows.Index - 1 : -1;

            activeSubmenu = type;
            submenuTypeahead.ClearSearch();
            RefreshModel(); // rebuild the region as a flat option list (ContentColumnCount now 0)
            ListModel region = Model.CurrentRegion;
            if (region != null && !region.IsEmpty)
            {
                region.MoveTo(Mathf.Clamp(selectedIndex, 0, region.Count - 1));
            }
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        /// <summary>
        /// Builds the Master picker from TrainableUtility.MasterSelectButton_GenerateMenu, the
        /// generator vanilla's own Master dropdown calls, so the candidate list (all maps, not
        /// just this one), the CanBeMaster enable/disable and the eventual write all come from
        /// vanilla's generated options rather than a hand-copied skill check.
        /// </summary>
        private void OpenMasterSubmenu(Pawn pawn)
        {
            if (pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Cell.RequiresTrainingForMaster".Loc(pawn.LabelShort, TrainableDefOf.Obedience.LabelCap), SpeechPriority.High);
                return;
            }
            List<PawnColumnMutationHelper.MasterMenuOption> options = PawnColumnMutationHelper.GetMasterMenuElements(pawn);
            submenuOptions.Clear();
            submenuOptions.AddRange(options.Cast<object>());

            int selected = 0;
            Pawn currentMaster = pawn.playerSettings?.Master;
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Colonist == currentMaster) { selected = i; break; }
            }
            OpenSubmenu(SubmenuType.Master, selected);
        }

        private void OpenAllowedAreaSubmenu(Pawn pawn)
        {
            if (!PawnColumnMutationHelper.CanEditAllowedArea(pawn))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(PawnColumnMutationHelper.AllowedAreaNotice(pawn)
                    ?? "RimWorldAccess.Animals.Submenu.AreaNotAllowed".Translate(pawn.LabelShort).ToString(), SpeechPriority.High);
                return;
            }
            List<Area> areas = AnimalsMenuHelper.GetAvailableAreas();
            submenuOptions.Clear();
            submenuOptions.Add(ManageAreasMarker);
            submenuOptions.Add(null); // unrestricted
            submenuOptions.AddRange(areas.Cast<object>());

            int selected = 1; // default: Unrestricted, not Manage Areas
            if (pawn.playerSettings?.AreaRestrictionInPawnCurrentMap != null)
            {
                for (int i = 0; i < areas.Count; i++)
                {
                    if (areas[i] == pawn.playerSettings.AreaRestrictionInPawnCurrentMap) { selected = i + 2; break; }
                }
            }
            OpenSubmenu(SubmenuType.AllowedArea, selected);
        }

        private void OpenMedicalCareSubmenu(Pawn pawn)
        {
            List<MedicalCareCategory> levels = AnimalsMenuHelper.GetMedicalCareLevels();
            submenuOptions.Clear();
            submenuOptions.AddRange(levels.Cast<object>());

            int selected = 0;
            if (pawn.playerSettings != null)
            {
                for (int i = 0; i < levels.Count; i++)
                {
                    if (levels[i] == pawn.playerSettings.medCare) { selected = i; break; }
                }
            }
            OpenSubmenu(SubmenuType.MedicalCare, selected);
        }

        private void SubmenuMove(int delta)
        {
            if (submenuOptions.Count == 0)
                return;
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null)
                return;
            if (submenuTypeahead.HasActiveSearch && !submenuTypeahead.HasNoMatches)
            {
                int match = delta > 0 ? submenuTypeahead.GetNextMatch(region.Index) : submenuTypeahead.GetPreviousMatch(region.Index);
                if (match >= 0 && match < submenuOptions.Count)
                {
                    MenuHelper.SoundMatchMove(region.Index, match, delta);
                    region.MoveTo(match);
                    AnnounceSubmenuWithSearch();
                }
                return;
            }
            int count = submenuOptions.Count;
            int next = ((region.Index + delta) % count + count) % count;
            region.MoveTo(next);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceSubmenuOption();
        }

        private void ApplySubmenuSelection(int index)
        {
            if (submenuAnimalRow < 0 || submenuAnimalRow >= animalsList.Count || submenuOptions.Count == 0)
            {
                CloseSubmenuInternal();
                return;
            }
            Pawn currentAnimal = animalsList[submenuAnimalRow];
            object selectedOption = index >= 0 && index < submenuOptions.Count ? submenuOptions[index] : null;
            string rejectionMessage = null;

            switch (activeSubmenu)
            {
                case SubmenuType.Master:
                    // The write is vanilla's own generated FloatMenuOption.action, not a hand
                    // write of playerSettings.Master. A null Apply means vanilla drew the entry
                    // disabled; refuse as a disabled float-menu click would, but speak the reason
                    // vanilla's own label carries rather than no-opping silently.
                    if (selectedOption is PawnColumnMutationHelper.MasterMenuOption masterOption)
                    {
                        if (masterOption.Apply != null)
                        {
                            masterOption.Apply();
                        }
                        else
                        {
                            rejectionMessage = "RimWorldAccess.Animals.Submenu.MasterOptionDisabled".Translate(masterOption.Label).ToString();
                        }
                    }
                    break;

                case SubmenuType.AllowedArea:
                    if (selectedOption is string marker && marker == ManageAreasMarker)
                    {
                        Map currentMap = Find.CurrentMap;
                        CloseSubmenuInternal();
                        if (currentMap != null)
                        {
                            // Vanilla's own Manage areas... delegate (AreaUtility); ManageAreasScope drives it.
                            Find.WindowStack.Add(new Dialog_ManageAreas(currentMap));
                        }
                        return; // already closed the submenu above
                    }
                    // The full vanilla gate, not just a null playerSettings check: a pawn vanilla's
                    // own cell would draw nothing for is refused here too.
                    if (!PawnColumnMutationHelper.CanEditAllowedArea(currentAnimal))
                    {
                        rejectionMessage = "RimWorldAccess.Animals.Submenu.AreaNotAllowed".Translate(currentAnimal.LabelShort).ToString();
                        break;
                    }
                    Area selectedArea = selectedOption as Area;
                    // MUTATION-C: mirrors PawnColumnWorker_AllowedArea.DoCell's own bare-field write
                    // (RimWorld/PawnColumnWorker_AllowedArea.cs:30-53); no gated setter exists on
                    // AreaRestrictionInPawnCurrentMap. The CanEditAllowedArea gate above already
                    // refused this branch for a pawn vanilla's own cell would draw nothing for.
                    currentAnimal.playerSettings.AreaRestrictionInPawnCurrentMap = selectedArea;
                    lastAppliedArea = selectedArea;
                    break;

                case SubmenuType.MedicalCare:
                    if (currentAnimal.playerSettings != null && selectedOption is MedicalCareCategory medCare)
                    {
                        currentAnimal.playerSettings.medCare = medCare;
                    }
                    break;
            }

            if (rejectionMessage != null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(rejectionMessage, SpeechPriority.High);
            }
            else
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            CloseSubmenuInternal();
            AfterCellEdit();
        }

        private void CloseSubmenuInternal()
        {
            activeSubmenu = SubmenuType.None;
            submenuOptions.Clear();
            submenuTypeahead.ClearSearch();
            RefreshModel(); // rebuild the region back into table shape
            TableModel table = Model.CurrentTable;
            if (table != null && submenuAnimalRow >= 0 && submenuAnimalRow < animalsList.Count)
            {
                table.Rows.MoveTo(submenuAnimalRow + 1);
            }
            submenuAnimalRow = -1;
        }

        private static string GetSubmenuOptionText(object option)
        {
            if (option is string s && s == ManageAreasMarker)
            {
                return "RimWorldAccess.Animals.Submenu.ManageAreas".Translate().ToString();
            }
            if (option is PawnColumnMutationHelper.MasterMenuOption masterOption)
            {
                // Vanilla's own FloatMenuOption label already carries the "(None)" and "(skill too
                // low)" annotations verbatim.
                return masterOption.Label;
            }
            if (option == null)
            {
                return "None".Translate().Resolve();
            }
            if (option is Pawn colonist)
            {
                return colonist.LabelShort;
            }
            if (option is Area area)
            {
                return area.Label;
            }
            if (option is MedicalCareCategory medCare)
            {
                return medCare.GetLabel();
            }
            return "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();
        }

        private void AnnounceSubmenuOption()
        {
            if (submenuOptions.Count == 0)
                return;
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            int index = region == null ? -1 : region.Index;
            if (index < 0 || index >= submenuOptions.Count)
                return;
            string optionText = GetSubmenuOptionText(submenuOptions[index]);
            string announcement = "RimWorldAccess.Animals.Submenu.OptionAnnounce".Translate(
                optionText, MenuHelper.FormatPosition(index, submenuOptions.Count)).ToString();
            TolkHelper.SpeakData(announcement);
        }

        private void AnnounceSubmenuWithSearch()
        {
            if (submenuOptions.Count == 0)
                return;
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            int index = region == null ? -1 : region.Index;
            if (index < 0 || index >= submenuOptions.Count)
                return;
            string optionText = GetSubmenuOptionText(submenuOptions[index]);
            string position = MenuHelper.FormatPosition(index, submenuOptions.Count);
            string announcement = "RimWorldAccess.Animals.Submenu.Option".Translate(optionText, position).ToString();
            if (submenuTypeahead.HasActiveSearch)
            {
                announcement += submenuTypeahead.BuildSearchContextSuffix();
            }
            TolkHelper.SpeakData(announcement);
        }

        // AllowedArea, Master and MedicalCare carry their own paint semantics; they do not reduce
        // to the generic boolean brush.

        /// <summary>Next data-row index for painting: search-match-aware like row navigation, else a plain wrap.</summary>
        private int NextPaintRow(int currentRow, int delta)
        {
            int count = animalsList.Count;
            if (TypeaheadHasActiveSearch)
            {
                int matchRow = TypeaheadMatchRowInRegion(AnimalsRegion, currentRow, delta);
                if (matchRow >= 0 && matchRow < count)
                    return matchRow;
            }
            return ((currentRow + delta) % count + count) % count;
        }

        private void PaintUp()
        {
            PaintSingle(-1);
        }

        private void PaintDown()
        {
            PaintSingle(1);
        }

        private void PaintSingle(int delta)
        {
            if (IsInSubmenu || animalsList.Count <= 1)
                return;
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
                return;
            int col = table.ColumnIndex;
            if (!AnimalsMenuHelper.CanPaintColumn(col))
            {
                MenuHelper.SpeakCannotPaintColumn();
                return;
            }

            int sourceRow = table.Rows.Index - 1;
            Pawn sourcePawn = animalsList[sourceRow];
            AnimalsMenuHelper.ColumnType columnType = AnimalsMenuHelper.GetColumnType(col);

            int targetRow = NextPaintRow(sourceRow, delta);
            if (targetRow < 0 || targetRow >= animalsList.Count)
                return;

            if (columnType == AnimalsMenuHelper.ColumnType.AllowedArea)
            {
                lastAppliedArea = sourcePawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
                table.Rows.MoveTo(targetRow + 1);
                Pawn target = animalsList[targetRow];
                string areaName = lastAppliedArea?.Label ?? "NoAreaAllowed".Translate().Resolve();
                string position = MenuHelper.FormatPosition(targetRow, animalsList.Count);
                Area targetArea = target.playerSettings?.AreaRestrictionInPawnCurrentMap;
                if (targetArea == lastAppliedArea)
                {
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.AreaAlreadySet".Loc(target.LabelShort, areaName, position));
                }
                // The full vanilla gate, so the announcement never claims success for a write
                // vanilla itself would refuse.
                else if (PawnColumnMutationHelper.CanEditAllowedArea(target))
                {
                    // MUTATION-C: mirrors PawnColumnWorker_AllowedArea.DoCell's own bare-field write
                    // (RimWorld/PawnColumnWorker_AllowedArea.cs:30-53); no gated setter exists on
                    // AreaRestrictionInPawnCurrentMap. Reached only after the CanEditAllowedArea gate above.
                    target.playerSettings.AreaRestrictionInPawnCurrentMap = lastAppliedArea;
                    SoundDefOf.Designate_DragStandard_Changed_NoCam.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.AreaApplied".Loc(target.LabelShort, areaName, position));
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.AreaNotAllowed".Loc(target.LabelShort, position));
                }
                return;
            }

            if (columnType == AnimalsMenuHelper.ColumnType.Master)
            {
                Pawn sourceMaster = sourcePawn.playerSettings?.Master;
                string masterName = sourceMaster?.LabelShort ?? "None".Translate().Resolve();
                table.Rows.MoveTo(targetRow + 1);
                Pawn target = animalsList[targetRow];
                string position = MenuHelper.FormatPosition(targetRow, animalsList.Count);
                if (target.playerSettings == null || target.training?.HasLearned(TrainableDefOf.Obedience) != true)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.RequiresObedience".Loc(target.LabelShort, TrainableDefOf.Obedience.LabelCap, position));
                }
                else if (target.playerSettings.Master == sourceMaster)
                {
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.MasterAlready".Loc(target.LabelShort, masterName, position));
                }
                else
                {
                    // The write rides vanilla's generated FloatMenuOption.action for THIS target:
                    // CanBeMaster is per-animal, since a bond overrides the skill floor.
                    PawnColumnMutationHelper.MasterMenuOption? match = PawnColumnMutationHelper.FindMasterMenuOption(target, sourceMaster);
                    if (match == null || match.Value.Apply == null)
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.MasterNotAllowed".Loc(target.LabelShort, masterName, position));
                    }
                    else
                    {
                        match.Value.Apply();
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.MasterApplied".Loc(target.LabelShort, masterName, position));
                    }
                }
                return;
            }

            if (columnType == AnimalsMenuHelper.ColumnType.MedicalCare)
            {
                MedicalCareCategory sourceCare = sourcePawn.playerSettings?.medCare ?? MedicalCareCategory.NoCare;
                string careLabel = sourceCare.GetLabel();
                table.Rows.MoveTo(targetRow + 1);
                Pawn target = animalsList[targetRow];
                string position = MenuHelper.FormatPosition(targetRow, animalsList.Count);
                if (target.playerSettings == null)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.CannotSetMedicalCare".Loc(target.LabelShort, position));
                }
                else if (target.playerSettings.medCare == sourceCare)
                {
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.MedicalCareAlready".Loc(target.LabelShort, careLabel, position));
                }
                else
                {
                    target.playerSettings.medCare = sourceCare;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.MedicalCareApplied".Loc(target.LabelShort, careLabel, position));
                }
                return;
            }

            bool brushValue = AnimalsMenuHelper.GetPaintableValue(sourcePawn, col);
            table.Rows.MoveTo(targetRow + 1);
            Pawn targetPawn = animalsList[targetRow];
            string colName = AnimalsMenuHelper.GetColumnName(col);
            string valueLabel = AnimalsMenuHelper.GetPaintValueLabel(col, brushValue);
            string pos = MenuHelper.FormatPosition(targetRow, animalsList.Count);

            bool targetValue = AnimalsMenuHelper.GetPaintableValue(targetPawn, col);
            if (targetValue == brushValue)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.CellAlready".Loc(targetPawn.LabelShort, colName, valueLabel, pos));
                return;
            }
            bool applied = AnimalsMenuHelper.SetPaintableValue(targetPawn, col, brushValue);
            SoundDef sound = applied ? AnimalsMenuHelper.GetPaintSound(col, brushValue) : SoundDefOf.ClickReject;
            sound.PlayOneShotOnCamera();
            if (applied)
                TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.CellApplied".Loc(targetPawn.LabelShort, colName, valueLabel, pos));
            else
                TolkHelper.Speak("RimWorldAccess.Animals.Paint.Single.CellSkipped".Loc(targetPawn.LabelShort, colName, valueLabel, pos));
        }

        private void PaintToLast()
        {
            PaintBulk(towardFirst: false, entireColumn: false);
        }

        private void PaintToFirst()
        {
            PaintBulk(towardFirst: true, entireColumn: false);
        }

        private void PaintEntireColumn(bool towardFirst)
        {
            PaintBulk(towardFirst, entireColumn: true);
        }

        private void PaintBulk(bool towardFirst, bool entireColumn)
        {
            if (IsInSubmenu || animalsList.Count == 0)
                return;
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
                return;
            int col = table.ColumnIndex;
            if (!AnimalsMenuHelper.CanPaintColumn(col))
            {
                MenuHelper.SpeakCannotPaintColumn();
                return;
            }

            AnimalsMenuHelper.ColumnType columnType = AnimalsMenuHelper.GetColumnType(col);
            int currentRow = table.Rows.Index - 1;
            string colName = AnimalsMenuHelper.GetColumnName(col);

            int startRow, endRow;
            if (entireColumn)
            {
                startRow = 0;
                endRow = animalsList.Count - 1;
            }
            else if (towardFirst)
            {
                startRow = 0;
                endRow = currentRow;
            }
            else
            {
                startRow = currentRow;
                endRow = animalsList.Count - 1;
            }

            if (columnType == AnimalsMenuHelper.ColumnType.AllowedArea)
            {
                Pawn source = animalsList[currentRow];
                lastAppliedArea = source.playerSettings?.AreaRestrictionInPawnCurrentMap;
                string areaName = lastAppliedArea?.Label ?? "NoAreaAllowed".Translate().Resolve();

                var changedNames = new List<string>();
                for (int i = startRow; i <= endRow; i++)
                {
                    Pawn pawn = animalsList[i];
                    // Full vanilla gate (see PaintSingle's AllowedArea branch above).
                    if (!PawnColumnMutationHelper.CanEditAllowedArea(pawn)) continue;
                    if (pawn.playerSettings.AreaRestrictionInPawnCurrentMap != lastAppliedArea)
                    {
                        // MUTATION-C: mirrors PawnColumnWorker_AllowedArea.DoCell's own bare-field write
                        // (RimWorld/PawnColumnWorker_AllowedArea.cs:30-53); no gated setter exists on
                        // AreaRestrictionInPawnCurrentMap. Reached only after the CanEditAllowedArea gate above.
                        pawn.playerSettings.AreaRestrictionInPawnCurrentMap = lastAppliedArea;
                        changedNames.Add(pawn.LabelShort);
                    }
                }
                table.Rows.MoveTo((towardFirst ? startRow : endRow) + 1);

                if (changedNames.Count > 0)
                {
                    BulkSoundQueue.Queue(changedNames.Count, SoundDefOf.Designate_DragStandard_Changed_NoCam);
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Bulk.AreaApplied".Loc(areaName, MenuHelper.FormatNameList(changedNames)));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Bulk.AreaAlreadyAll".Loc(areaName));
                }
                return;
            }

            if (columnType == AnimalsMenuHelper.ColumnType.Master)
            {
                Pawn source = animalsList[currentRow];
                Pawn sourceMaster = source.playerSettings?.Master;
                string masterName = sourceMaster?.LabelShort ?? "None".Translate().Resolve();

                // Per-target vehicle A, as in PaintSingle. A colonist ineligible for a given
                // target is silently skipped, like the branches above.
                var changedNames = new List<string>();
                for (int i = startRow; i <= endRow; i++)
                {
                    Pawn pawn = animalsList[i];
                    if (pawn.playerSettings == null || pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
                        continue;
                    if (pawn.playerSettings.Master == sourceMaster)
                        continue;
                    PawnColumnMutationHelper.MasterMenuOption? match = PawnColumnMutationHelper.FindMasterMenuOption(pawn, sourceMaster);
                    if (match == null || match.Value.Apply == null)
                        continue;
                    match.Value.Apply();
                    changedNames.Add(pawn.LabelShort);
                }
                table.Rows.MoveTo((towardFirst ? startRow : endRow) + 1);

                if (changedNames.Count > 0)
                {
                    BulkSoundQueue.Queue(changedNames.Count, SoundDefOf.Click);
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Bulk.MasterApplied".Loc(masterName, MenuHelper.FormatNameList(changedNames)));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Bulk.MasterAlreadyAll".Loc(masterName));
                }
                return;
            }

            if (columnType == AnimalsMenuHelper.ColumnType.MedicalCare)
            {
                Pawn source = animalsList[currentRow];
                MedicalCareCategory sourceCare = source.playerSettings?.medCare ?? MedicalCareCategory.NoCare;
                string careLabel = sourceCare.GetLabel();

                var changedNames = new List<string>();
                for (int i = startRow; i <= endRow; i++)
                {
                    Pawn pawn = animalsList[i];
                    if (pawn.playerSettings == null) continue;
                    if (pawn.playerSettings.medCare != sourceCare)
                    {
                        pawn.playerSettings.medCare = sourceCare;
                        changedNames.Add(pawn.LabelShort);
                    }
                }
                table.Rows.MoveTo((towardFirst ? startRow : endRow) + 1);

                if (changedNames.Count > 0)
                {
                    BulkSoundQueue.Queue(changedNames.Count, SoundDefOf.Tick_High);
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Bulk.MedicalCareApplied".Loc(careLabel, MenuHelper.FormatNameList(changedNames)));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Bulk.MedicalCareAlreadyAll".Loc(careLabel));
                }
                return;
            }

            Pawn sourcePawn = animalsList[currentRow];
            bool brushValue = AnimalsMenuHelper.GetPaintableValue(sourcePawn, col);
            string valueLabel = AnimalsMenuHelper.GetPaintValueLabel(col, brushValue);
            SoundDef paintSound = AnimalsMenuHelper.GetPaintSound(col, brushValue);

            var changed = new List<string>();
            int skippedCount = 0;
            for (int i = startRow; i <= endRow; i++)
            {
                Pawn pawn = animalsList[i];
                bool currentValue = AnimalsMenuHelper.GetPaintableValue(pawn, col);
                if (currentValue != brushValue)
                {
                    if (AnimalsMenuHelper.SetPaintableValue(pawn, col, brushValue))
                        changed.Add(pawn.LabelShort);
                    else
                        skippedCount++;
                }
            }
            table.Rows.MoveTo((towardFirst ? startRow : endRow) + 1);

            if (changed.Count > 0)
            {
                BulkSoundQueue.Queue(changed.Count, paintSound);
                if (skippedCount > 0)
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Bulk.CellAppliedWithSkips".Loc(colName, valueLabel, MenuHelper.FormatNameList(changed), skippedCount));
                else
                    TolkHelper.Speak("RimWorldAccess.Animals.Paint.Bulk.CellApplied".Loc(colName, valueLabel, MenuHelper.FormatNameList(changed)));
            }
            else if (skippedCount > 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Animals.Paint.Bulk.CellSkippedAll".Loc(colName, valueLabel, skippedCount));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Paint.Bulk.CellAlreadyAll".Loc(colName, valueLabel));
            }
        }

        /// <summary>Alt+Shift+J routes on the real vanilla table drawing underneath, which this scope reads but was never attached to.</summary>
        protected override Window PointerSurface
        {
            get { return PawnTableFocusDriver.OpenTabWindowFor(this); }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            PawnTableExtrasRouter.AddRouteCandidates(
                0, animalsList, ContentColumnCount(0), AnimalsMenuHelper.GetDef, candidates, targets);
        }

        Type IPawnTableFocusSource.TabWindowType
        {
            get { return typeof(MainTabWindow_Animals); }
        }

        Pawn IPawnTableFocusSource.FocusedTablePawn
        {
            get
            {
                TableModel table = Model.CurrentTable;
                int row = table != null ? table.Rows.Index - 1 : -1;
                return row >= 0 && row < animalsList.Count ? animalsList[row] : null;
            }
        }

        PawnColumnDef IPawnTableFocusSource.FocusedTableColumn
        {
            get
            {
                TableModel table = Model.CurrentTable;
                return table != null ? AnimalsMenuHelper.GetDef(table.ColumnIndex) : null;
            }
        }

        bool IPawnTableFocusSource.FocusedOnHeaderRow
        {
            get
            {
                TableModel table = Model.CurrentTable;
                return table != null && table.Rows.Index == 0;
            }
        }

        private void Close()
        {
            AnimalsMenuState.Close();
        }
    }

    /// <summary>
    /// Keeps <see cref="AnimalsScope"/> in lockstep with
    /// <see cref="AnimalsMenuState.IsActive"/>, reconciled every OnGUI pass AFTER
    /// <see cref="GizmoScopeMirror"/> so F4 and the gizmo menu coexist.
    ///
    /// Stands down for the info card, the Manage-Areas drill-in and the area-shape placement that
    /// follows it, and the auto-slaughter dialog — all of which leave AnimalsMenuState.IsActive
    /// true underneath.
    /// </summary>
    internal static class AnimalsScopeMirror
    {
        private static readonly AnimalsScope scope = new AnimalsScope();

        public static void Reconcile()
        {
            // ForeignDialogWindowAbove covers the foreign-faction Dialog_Confirm the
            // ReleaseToWild/Sterilize columns can raise: a plain Window subclass, not a
            // Dialog_MessageBox, read by the generic window scope while this state stays active.
            if (AnimalsMenuState.IsActive
                && !InfoCardState.IsActive
                && !ShapePlacementState.IsActive
                && !ViewingModeState.IsActive
                && !AutoSlaughterState.IsActive
                && !ShellGuards.ForeignDialogWindowAbove())
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
