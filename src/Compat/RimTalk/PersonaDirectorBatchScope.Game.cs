using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for PersonaDirector's <c>RimPersonaDirector.Window_BatchDirector</c>: a
    /// <c>PawnRosterList</c> table (multi-select, sortable Name/Race/Faction/Tag headers,
    /// category filters, search) with per-row Talk/Quick Gen/Deep Edit actions and batch/single
    /// LLM generation. The roster's selection column answers only raw mouse presses and drags
    /// (<c>Widgets.CheckboxDraw</c> plus <c>Mouse.IsOver</c>), so the row's default Activate
    /// toggles that one pawn, reaching the same set one row at a time.
    ///
    /// Everything else -- Switch to Simple, Auto Evolution (while experimental features are on),
    /// the Scene Notes Clear button, Select All, Refresh, the prompt-slot picker, the filter
    /// checkboxes, the select-every-listed-pawn header box, Batch Gen, and the Single/Batch mode
    /// toggle -- is DECLARED here. The filters draw a bare checkbox beside a label with its own
    /// ButtonInvisible, a shape the capture cannot pair, so each control reproduces its own
    /// click body.
    ///
    /// ASYNC GENERATION: Quick Gen and Batch Gen post into the same task fields the window's
    /// <c>UpdateAsyncTasks()</c> polls every draw, and its own success/fail messages are already
    /// spoken by the message bridge, so the mod's completion signal is the only one needed.
    ///
    /// ACCEPT/CANCEL: Window_BatchDirector overrides neither <c>OnAcceptKeyPressed</c> nor
    /// <c>OnCancelKeyPressed</c>, so <see cref="ScreenScope"/>'s own defaults are correct.
    /// </summary>
    internal sealed class PersonaDirectorBatchScope : ScreenScope
    {
        private const int TextFieldsRegion = 0;
        private const int PawnsRegion = 1;

        private enum TextFieldItem
        {
            SceneNotes,
            Search,
        }

        private const int ColSelected = 0;
        private const int ColName = 1;
        private const int ColRace = 2;
        private const int ColFaction = 3;
        private const int ColTag = 4;
        private const int ColTalk = 5;
        private const int ColQuickGen = 6;
        private const int ColDeepEdit = 7;

        private readonly Window dialog;
        private readonly TextFieldEditSession session = new TextFieldEditSession();
        private readonly List<Pawn> pawns = new List<Pawn>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        public PersonaDirectorBatchScope(Window dialog)
        {
            this.dialog = dialog;

            RegisterPopTeardown(session.CancelIfActive);
        }

        public override string Name
        {
            get { return "rimtalk-persona-director-batch"; }
        }

        private object Roster
        {
            get { return PersonaDirectorCompat.Roster(dialog); }
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == TextFieldsRegion
                ? "RimWorldAccess.Compat.RimTalk.PersonaDirector.TextFieldsRegionName".Translate()
                : "RimWorldAccess.Compat.RimTalk.PersonaDirector.PawnsRegionName".Translate();
        }

        protected override void RefreshContent()
        {
            pawns.Clear();
            object roster = Roster;
            if (roster != null)
            {
                pawns.AddRange(PersonaDirectorCompat.VisiblePawns(roster));
            }
        }

        protected override int ContentItemCount(int region)
        {
            return region == TextFieldsRegion ? 2 : pawns.Count;
        }

        protected override int ContentColumnCount(int region)
        {
            return region == PawnsRegion ? 8 : 0;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region != PawnsRegion) return null;
            switch (column)
            {
                case ColSelected: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.PersonaDirector.HeaderSelected".Translate());
                case ColName: return new TableColumnInfo(H("RPD_Batch_Header_Name"), null, true);
                case ColRace: return new TableColumnInfo(H("RPD_Batch_Header_Race"), null, true);
                case ColFaction: return new TableColumnInfo(H("RPD_Batch_Header_Faction"), null, true);
                case ColTag: return new TableColumnInfo(H("RPD_Batch_Header_Tag"), null, true);
                case ColTalk: return new TableColumnInfo(H("RPD_Batch_Button_Talk"));
                case ColQuickGen: return new TableColumnInfo(H("RPD_Batch_Button_QuickGen"));
                default: return new TableColumnInfo(H("RPD_Batch_Button_DeepEdit"));
            }
        }

        private static string H(string key)
        {
            return Translator.Translate(key);
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (region != PawnsRegion || row < 0 || row >= pawns.Count) return "";
            Pawn pawn = pawns[row];
            switch (column)
            {
                case ColSelected:
                    return IsSelected(pawn)
                        ? "RimWorldAccess.Compat.RimTalk.PersonaDirector.Selected".Translate()
                        : "RimWorldAccess.Compat.RimTalk.PersonaDirector.NotSelected".Translate();
                case ColName:
                    return pawn.LabelShortCap;
                case ColRace:
                    return pawn.genes != null && pawn.genes.Xenotype != null ? pawn.genes.XenotypeLabel : pawn.def.label;
                case ColFaction:
                    return pawn.Faction != null ? pawn.Faction.Name : H("RPD_Faction_None");
                case ColTag:
                    return PersonaDirectorCompat.PawnTag(pawn);
                default:
                    return IsGenerating(pawn) ? H("RPD_Batch_Status_Generating") : H(ButtonKey(column));
            }
        }

        /// <summary>The name cell's own hover tooltip, the pawn's current persona.</summary>
        protected override string ContentCellTip(int region, int row, int column)
        {
            if (region != PawnsRegion || column != ColName || row < 0 || row >= pawns.Count) return null;
            return TranslatorFormattedStringExtensions.Translate("RPD_Batch_PawnTooltip",
                PersonaDirectorCompat.CurrentPersonality(pawns[row]));
        }

        private static string ButtonKey(int column)
        {
            switch (column)
            {
                case ColTalk: return "RPD_Batch_Button_Talk";
                case ColQuickGen: return "RPD_Batch_Button_QuickGen";
                default: return "RPD_Batch_Button_DeepEdit";
            }
        }

        private bool IsSelected(Pawn pawn)
        {
            HashSet<Pawn> selected = PersonaDirectorCompat.SelectedPawns(Roster);
            return selected != null && selected.Contains(pawn);
        }

        /// <summary>Mirrors DrawPawnActions' own test for drawing "Generating" instead of the row's buttons.</summary>
        private bool IsGenerating(Pawn pawn)
        {
            IDictionary tasks = PersonaDirectorCompat.GenerationTasks(dialog);
            if (tasks != null && tasks.Contains(pawn) && tasks[pawn] is System.Threading.Tasks.Task single && !single.IsCompleted)
            {
                return true;
            }
            System.Threading.Tasks.Task batch = PersonaDirectorCompat.BatchTask(dialog);
            List<Pawn> batchPawns = PersonaDirectorCompat.BatchTaskPawns(dialog);
            return batch != null && !batch.IsCompleted && batchPawns != null && batchPawns.Contains(pawn);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            ElementDescription d = new ElementDescription();
            if (region == TextFieldsRegion)
            {
                d.Role = ElementRole.TextField;
                string value;
                if (index == (int)TextFieldItem.SceneNotes)
                {
                    d.Label = H("RPD_Batch_SceneNotes");
                    value = PersonaDirectorCompat.DirectorNotesText();
                }
                else
                {
                    d.Label = "RimWorldAccess.Compat.RimTalk.PersonaDirector.SearchLabel".Translate();
                    d.Extras = H("RPD_Batch_SearchTip");
                    value = PersonaDirectorCompat.SearchText(Roster);
                }
                if (string.IsNullOrEmpty(value)) d.ValueBlank = true; else d.Value = value;
                return d;
            }

            d.Label = ContentCellText(PawnsRegion, index, ColName);
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == TextFieldsRegion)
            {
                BeginEditTextField((TextFieldItem)index);
                return;
            }
            ToggleSelection(index);
        }

        /// <summary>MUTATION-C: mirrors DrawSelection's single-row paint (`_selectedPawns.Add`/`Remove`), which only a raw mouse press reaches.</summary>
        private void ToggleSelection(int index)
        {
            HashSet<Pawn> selected = PersonaDirectorCompat.SelectedPawns(Roster);
            if (selected == null || index < 0 || index >= pawns.Count) return;
            Pawn pawn = pawns[index];
            if (!selected.Add(pawn))
            {
                selected.Remove(pawn);
            }
            AnnounceCurrentItem();
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (region != PawnsRegion || row < 0 || row >= pawns.Count) return false;
            Pawn pawn = pawns[row];

            switch (column)
            {
                case ColName:
                    PersonaDirectorCompat.JumpToPawn(Roster, pawn);
                    RefreshModel();
                    return true;
                case ColTalk:
                    if (!IsGenerating(pawn)) PersonaDirectorCompat.OpenRimTalkDialog(pawn);
                    return true;
                case ColQuickGen:
                    if (!IsGenerating(pawn))
                    {
                        // MUTATION-C: mirrors the Quick Gen button's click body, which posts into the window's own task dictionary.
                        PersonaDirectorCompat.GenerationTasks(dialog)[pawn] = PersonaDirectorCompat.GeneratePersonalityTask(
                            PersonaDirectorCompat.BuildCharacterData(pawn), pawn.LabelShortCap, pawn);
                        RefreshModel();
                        AnnounceCurrentItem();
                    }
                    return true;
                case ColDeepEdit:
                    if (!IsGenerating(pawn)) PersonaDirectorCompat.OpenPersonaEditorPin(pawn);
                    return true;
                default:
                    return false;
            }
        }

        // Sort: the roster's own 2-state header toggle against this codebase's 3-state cycle;
        // Cleared maps back to its constructed default, Name ascending.

        protected override bool TryGetLiveSort(int region, out int column, out bool descending)
        {
            column = -1;
            descending = false;
            object roster = Roster;
            if (region != PawnsRegion || roster == null) return false;
            bool ascending;
            column = ColName + PersonaDirectorCompat.SortOrdinal(roster, out ascending);
            descending = !ascending;
            return true;
        }

        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            int ordinal = column - ColName;
            object roster = Roster;
            if (region != PawnsRegion || roster == null || ordinal < 0 || ordinal > 3) return -1;

            Pawn current = currentRow >= 0 && currentRow < pawns.Count ? pawns[currentRow] : null;
            if (cycle == SortCycleResult.Cleared)
            {
                PersonaDirectorCompat.SetSort(roster, 0, true);
            }
            else
            {
                PersonaDirectorCompat.SetSort(roster, ordinal, cycle == SortCycleResult.SortedAscending);
            }
            RefreshContent();

            int index = current == null ? 0 : pawns.IndexOf(current);
            return index >= 0 ? index : 0;
        }

        // Text fields. Scene Notes is DirectorSettings.directorNotes, shared with the notes
        // editor; Search is the roster's own field.

        private void BeginEditTextField(TextFieldItem item)
        {
            string current = item == TextFieldItem.SceneNotes
                ? PersonaDirectorCompat.DirectorNotesText()
                : PersonaDirectorCompat.SearchText(Roster);
            string label = item == TextFieldItem.SceneNotes
                ? H("RPD_Batch_SceneNotes")
                : (string)"RimWorldAccess.Compat.RimTalk.PersonaDirector.SearchLabel".Translate();

            TextFieldSpec spec = new TextFieldSpec(labelKey: "RimWorldAccess.TextInput.LabelDefault", maxLength: null, minLength: 0);
            session.EnterEdit(current, spec, label, value => ApplyTextField(item, value), AnnounceCurrentItem);
        }

        private void ApplyTextField(TextFieldItem item, string value)
        {
            if (item == TextFieldItem.SceneNotes)
            {
                PersonaDirectorCompat.SetDirectorNotes(value);
                return;
            }
            PersonaDirectorCompat.SetSearchText(Roster, value);
            RefreshModel();
        }

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(H("RPD_Mode_SwitchToSimple"), SwitchToSimple, "rimTalkPersonaDirectorBatch.switchToSimple"));
                if (PersonaDirectorCompat.ExperimentalEnabled())
                {
                    actions.Add(new ScreenAction(H("RPD_Tab_AutoEvolution"), () => PersonaDirectorCompat.SwitchToAutoEvolve(dialog)));
                }
                actions.Add(new ScreenAction(H("RPD_Button_Clear"), ClearNotes, "rimTalkPersonaDirectorBatch.clearNotes"));
                actions.Add(new ScreenAction(H("RPD_Batch_SelectAll"), SelectAllFilters, "rimTalkPersonaDirectorBatch.selectAll"));
                actions.Add(new ScreenAction(H("RPD_Batch_Refresh"), Refresh, "rimTalkPersonaDirectorBatch.refresh"));
                actions.Add(new ScreenAction(PromptSlotLabel(), OpenPromptSlotMenu, "rimTalkPersonaDirectorBatch.promptSlot"));

                object roster = Roster;
                IDictionary filters = PersonaDirectorCompat.BatchFilters();
                if (roster != null && filters != null)
                {
                    foreach (string key in PersonaDirectorCompat.AvailableFilterKeys(roster))
                    {
                        if (!filters.Contains(key)) continue;
                        string filterKey = key;
                        actions.Add(new ScreenAction(
                            H("RPD_Filter_" + filterKey),
                            () => ToggleFilter(filterKey),
                            "rimTalkPersonaDirectorBatch.filter." + filterKey,
                            check: (bool)filters[filterKey] ? CheckState.Checked : CheckState.Unchecked));
                    }
                }

                actions.Add(new ScreenAction(
                    "RimWorldAccess.Compat.RimTalk.PersonaDirector.SelectAllListed".Translate(),
                    ToggleAllListed,
                    check: AllListedSelected() ? CheckState.Checked : CheckState.Unchecked));
                actions.Add(new ScreenAction(BatchGenLabel(), BatchGen, "rimTalkPersonaDirectorBatch.batchGen"));
                actions.Add(new ScreenAction(ModeToggleLabel(), ToggleMode, "rimTalkPersonaDirectorBatch.modeToggle"));
                return actions;
            }
        }

        private string PromptSlotLabel()
        {
            IList presets = PersonaDirectorCompat.Presets();
            if (presets == null || presets.Count == 0) return "";
            int index = PersonaDirectorCompat.SelectedPresetIndex;
            if (index < 0 || index >= presets.Count) index = 0;
            return PersonaDirectorCompat.PromptPresetLabel(presets[index]);
        }

        private string BatchGenLabel()
        {
            HashSet<Pawn> selected = PersonaDirectorCompat.SelectedPawns(Roster);
            return TranslatorFormattedStringExtensions.Translate("RPD_Batch_Button_BatchGen", selected?.Count ?? 0);
        }

        private string ModeToggleLabel()
        {
            return H(PersonaDirectorCompat.BatchSendMode(dialog) ? "RPD_Batch_Button_ModeBatch" : "RPD_Batch_Button_ModeSingle");
        }

        private void SwitchToSimple()
        {
            dialog.Close(true);
            PersonaDirectorCompat.ToggleNotesWindow();
        }

        /// <summary>MUTATION-C: mirrors DrawNotesSection's Clear button (directorNotes is a bare string field with no gated setter).</summary>
        private void ClearNotes()
        {
            PersonaDirectorCompat.SetDirectorNotes("");
        }

        /// <summary>MUTATION-C: mirrors DrawFilters' Select All click (every available filter set to "any was off", then Refresh).</summary>
        private void SelectAllFilters()
        {
            object roster = Roster;
            IDictionary filters = PersonaDirectorCompat.BatchFilters();
            if (roster == null || filters == null) return;
            List<string> keys = PersonaDirectorCompat.AvailableFilterKeys(roster).Where(filters.Contains).ToList();
            bool value = keys.Any(key => !(bool)filters[key]);
            foreach (string key in keys)
            {
                filters[key] = value;
            }
            PersonaDirectorCompat.RefreshRoster(roster);
            RefreshModel();
        }

        /// <summary>MUTATION-C: mirrors DrawFilters' per-filter checkbox and label click (flip the BatchFilters entry, then Refresh).</summary>
        private void ToggleFilter(string key)
        {
            object roster = Roster;
            IDictionary filters = PersonaDirectorCompat.BatchFilters();
            if (roster == null || filters == null || !filters.Contains(key)) return;
            filters[key] = !(bool)filters[key];
            PersonaDirectorCompat.RefreshRoster(roster);
            RefreshModel();
        }

        private bool AllListedSelected()
        {
            HashSet<Pawn> selected = PersonaDirectorCompat.SelectedPawns(Roster);
            return pawns.Count > 0 && selected != null && pawns.All(selected.Contains);
        }

        /// <summary>MUTATION-C: mirrors DrawList's header checkbox (add or remove every listed pawn); the header box is a bare Widgets.Checkbox with no caption to address.</summary>
        private void ToggleAllListed()
        {
            HashSet<Pawn> selected = PersonaDirectorCompat.SelectedPawns(Roster);
            if (selected == null) return;
            bool select = !AllListedSelected();
            foreach (Pawn pawn in pawns)
            {
                if (select) selected.Add(pawn); else selected.Remove(pawn);
            }
        }

        private void Refresh()
        {
            object roster = Roster;
            if (roster == null) return;
            PersonaDirectorCompat.RefreshRoster(roster);
            RefreshModel();
        }

        /// <summary>MUTATION-C: mirrors the prompt-slot button's own FloatMenu (index 3, "Evolution", is reserved and skipped).</summary>
        private void OpenPromptSlotMenu()
        {
            IList presets = PersonaDirectorCompat.Presets();
            if (presets == null) return;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < presets.Count; i++)
            {
                if (i == 3) continue;
                int index = i;
                options.Add(new FloatMenuOption(PersonaDirectorCompat.PromptPresetLabel(presets[i]),
                    () => { PersonaDirectorCompat.SelectedPresetIndex = index; }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>MUTATION-C: reproduces DrawGlobalActions' Batch Gen click (batch-mode and single-mode branches), posting into the task fields the window's own UpdateAsyncTasks polls.</summary>
        private void BatchGen()
        {
            HashSet<Pawn> selected = PersonaDirectorCompat.SelectedPawns(Roster);
            if (selected == null) return;
            if (PersonaDirectorCompat.BatchSendMode(dialog))
            {
                if (PersonaDirectorCompat.BatchTask(dialog) == null && selected.Count > 0)
                {
                    PersonaDirectorCompat.StartBatchTask(dialog, selected.ToList());
                }
            }
            else
            {
                IDictionary tasks = PersonaDirectorCompat.GenerationTasks(dialog);
                foreach (Pawn pawn in selected)
                {
                    if (tasks.Contains(pawn)) continue;
                    tasks[pawn] = PersonaDirectorCompat.GeneratePersonalityTask(
                        PersonaDirectorCompat.BuildCharacterData(pawn), pawn.LabelShortCap, pawn);
                }
            }
            RefreshModel();
        }

        private void ToggleMode()
        {
            PersonaDirectorCompat.ToggleBatchSendMode(dialog);
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return false; }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return PersonaDirectorCompat.F12BatchLabel();
        }
    }
}
