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
    /// LLM generation. The roster columns, sort, filters and selection are the shared
    /// <see cref="PersonaDirectorRosterTable"/>; the row's default Activate toggles that pawn.
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

        private const int ColTalk = PersonaDirectorRosterTable.ColumnCount;
        private const int ColQuickGen = ColTalk + 1;
        private const int ColDeepEdit = ColTalk + 2;

        private readonly Window dialog;
        private readonly TextFieldEditSession session = new TextFieldEditSession();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly PersonaDirectorRosterTable table;

        public PersonaDirectorBatchScope(Window dialog)
        {
            this.dialog = dialog;
            table = new PersonaDirectorRosterTable(() => PersonaDirectorCompat.Roster(dialog), RefreshModel);

            RegisterPopTeardown(session.CancelIfActive);
            Claim("rimTalkPersonaDirectorBatch.infoCard", e => table.OpenInfoCard(CurrentPawnRow()),
                when: () => Model.RegionIndex == PawnsRegion);
        }

        public override string Name
        {
            get { return "rimtalk-persona-director-batch"; }
        }

        private object Roster
        {
            get { return table.Roster; }
        }

        private int CurrentPawnRow()
        {
            TableModel model = Model.CurrentTable;
            return model == null ? -1 : model.Rows.Index - 1;
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
            table.Refresh();
        }

        protected override int ContentItemCount(int region)
        {
            return region == TextFieldsRegion ? 2 : table.Count;
        }

        protected override int ContentColumnCount(int region)
        {
            return region == PawnsRegion ? ColDeepEdit + 1 : 0;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region != PawnsRegion) return null;
            switch (column)
            {
                case ColTalk: return new TableColumnInfo(H("RPD_Batch_Button_Talk"));
                case ColQuickGen: return new TableColumnInfo(H("RPD_Batch_Button_QuickGen"));
                case ColDeepEdit: return new TableColumnInfo(H("RPD_Batch_Button_DeepEdit"));
                default: return PersonaDirectorRosterTable.ColumnInfo(column);
            }
        }

        private static string H(string key)
        {
            return Translator.Translate(key);
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            Pawn pawn = table.PawnAt(row);
            if (region != PawnsRegion || pawn == null) return "";
            if (column < PersonaDirectorRosterTable.ColumnCount) return table.CellText(row, column);
            return IsGenerating(pawn) ? H("RPD_Batch_Status_Generating") : H(ButtonKey(column));
        }

        protected override string ContentCellTip(int region, int row, int column)
        {
            return region == PawnsRegion ? table.CellTip(row, column) : null;
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

            d.Label = table.CellText(index, PersonaDirectorRosterTable.ColName);
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == TextFieldsRegion)
            {
                BeginEditTextField((TextFieldItem)index);
                return;
            }
            table.ToggleSelection(index);
            AnnounceCurrentItem();
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            Pawn pawn = table.PawnAt(row);
            if (region != PawnsRegion || pawn == null) return false;

            switch (column)
            {
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
                    return table.ActivateCell(row, column);
            }
        }

        protected override bool TryGetLiveSort(int region, out int column, out bool descending)
        {
            column = -1;
            descending = false;
            return region == PawnsRegion && table.TryGetLiveSort(out column, out descending);
        }

        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            return region == PawnsRegion ? table.ApplySort(column, cycle, currentRow) : -1;
        }

        // Text fields. Scene Notes is DirectorSettings.directorNotes, shared with the notes
        // editor; Search is the roster's own field.

        private void BeginEditTextField(TextFieldItem item)
        {
            string current = item == TextFieldItem.SceneNotes
                ? PersonaDirectorCompat.DirectorNotesText()
                : table.SearchText;
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
            table.ApplySearch(value);
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
                actions.Add(table.SelectAllFiltersAction("rimTalkPersonaDirectorBatch.selectAll"));
                actions.Add(table.RefreshAction("rimTalkPersonaDirectorBatch.refresh"));
                actions.Add(new ScreenAction(PromptSlotLabel(), OpenPromptSlotMenu, "rimTalkPersonaDirectorBatch.promptSlot"));
                table.AddFilterActions(actions, "rimTalkPersonaDirectorBatch.filter.");
                actions.Add(table.SelectAllListedAction());
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
