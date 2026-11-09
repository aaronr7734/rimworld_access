using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for PersonaDirector's <c>RimPersonaDirector.Window_AutoEvolveConfig</c>
    /// (only reachable while its experimental features are on). Four content regions follow the
    /// window top to bottom: the global settings and notes, the role and event triggers, the
    /// list controls above the roster, and the <c>PawnRosterList</c> table, whose leading columns
    /// are the shared <see cref="PersonaDirectorRosterTable"/> and whose trailing ones are this
    /// window's Status, Cycle, Next, Edit and History cells.
    ///
    /// Every control is DECLARED: the triggers and filters draw a bare checkbox beside a
    /// ButtonInvisible label, the batch buttons grey out through GUI.enabled (which button
    /// capture cannot see), and the per-row buttons live inside the scroll view.
    ///
    /// ACCEPT/CANCEL: the window overrides neither, so <see cref="ScreenScope"/>'s defaults
    /// stand; its own PostClose applies the history limit and saves.
    /// </summary>
    internal sealed class PersonaDirectorAutoEvolveScope : ScreenScope
    {
        private const int SettingsRegion = 0;
        private const int TriggersRegion = 1;
        private const int ListControlsRegion = 2;
        private const int PawnsRegion = 3;

        private enum SettingItem
        {
            Enable,
            DefaultEnabled,
            Notify,
            Speed,
            Notes,
        }

        private enum ListControlItem
        {
            Search,
            AdvancedPreset,
            Mode,
            SelectedCount,
            BatchInterval,
            HistoryLimit,
        }

        private const int ColStatus = PersonaDirectorRosterTable.ColumnCount;
        private const int ColCycle = ColStatus + 1;
        private const int ColNext = ColStatus + 2;
        private const int ColEdit = ColStatus + 3;
        private const int ColHistory = ColStatus + 4;

        /// <summary>One Triggers row: a group heading (Category and Field both null), a role category, or an event field.</summary>
        private sealed class TriggerRow
        {
            public string Label;
            public string Tip;
            public string Category;
            public FieldInfo Field;
        }

        private readonly Window window;
        private readonly TextFieldEditSession session = new TextFieldEditSession();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly List<TriggerRow> triggers = new List<TriggerRow>();
        private readonly PersonaDirectorRosterTable table;

        public PersonaDirectorAutoEvolveScope(Window window)
        {
            this.window = window;
            table = new PersonaDirectorRosterTable(() => PersonaDirectorAutoEvolveCompat.Roster(window), RefreshModel);

            RegisterPopTeardown(session.CancelIfActive);
            Claim("rimTalkPersonaDirectorAutoEvolve.infoCard", e => table.OpenInfoCard(CurrentPawnRow()),
                when: () => Model.RegionIndex == PawnsRegion);
        }

        public override string Name
        {
            get { return "rimtalk-persona-director-auto-evolve"; }
        }

        private static string T(string key)
        {
            return Translator.Translate(key);
        }

        private int CurrentPawnRow()
        {
            TableModel model = Model.CurrentTable;
            return model == null ? -1 : model.Rows.Index - 1;
        }

        protected override int ContentRegionCount
        {
            get { return 4; }
        }

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case SettingsRegion: return "RimWorldAccess.Compat.RimTalk.PersonaDirector.AutoEvolve.SettingsRegionName".Translate();
                case TriggersRegion: return T("RPD_AutoEvolve_Triggers");
                case ListControlsRegion: return "RimWorldAccess.Compat.RimTalk.PersonaDirector.AutoEvolve.ListControlsRegionName".Translate();
                default: return "RimWorldAccess.Compat.RimTalk.PersonaDirector.PawnsRegionName".Translate();
            }
        }

        /// <summary>The triggers footnote, drawn as a label and repeated as its own tooltip; spoken once.</summary>
        protected override string ContentRegionEntryDetail(int region)
        {
            return region == TriggersRegion ? T("RPD_AutoEvolve_TriggersTip") : null;
        }

        protected override void RefreshContent()
        {
            table.Refresh();
            triggers.Clear();
            triggers.Add(new TriggerRow { Label = T("RPD_AutoEvolve_RoleTriggers") });
            foreach (string id in PersonaDirectorAutoEvolveCompat.RoleCategoryIds())
            {
                triggers.Add(new TriggerRow
                {
                    Label = T("RPD_AutoGen_Category" + id),
                    Tip = T("RPD_AutoEvolve_RoleChangeTip"),
                    Category = id,
                });
            }
            triggers.Add(new TriggerRow { Label = T("RPD_AutoEvolve_EventTriggers") });
            foreach (KeyValuePair<string, FieldInfo> trigger in PersonaDirectorAutoEvolveCompat.EventTriggers)
            {
                triggers.Add(new TriggerRow { Label = T(trigger.Key), Tip = T(trigger.Key + "Tip"), Field = trigger.Value });
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case SettingsRegion: return (int)SettingItem.Notes + 1;
                case TriggersRegion: return triggers.Count;
                case ListControlsRegion: return (int)ListControlItem.HistoryLimit + 1;
                default: return table.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch (region)
            {
                case SettingsRegion: return DescribeSetting((SettingItem)index);
                case TriggersRegion: return DescribeTrigger(index);
                case ListControlsRegion: return DescribeListControl((ListControlItem)index);
                default: return new ElementDescription { Label = table.CellText(index, PersonaDirectorRosterTable.ColName) };
            }
        }

        private ElementDescription DescribeSetting(SettingItem item)
        {
            switch (item)
            {
                case SettingItem.Enable:
                    return Checkbox(T("RPD_AutoEvolve_Enable"), PersonaDirectorAutoEvolveCompat.GlobalEnabled(), T("RPD_AutoEvolve_EnableTooltip"));
                case SettingItem.DefaultEnabled:
                    return Checkbox(T("RPD_AutoEvolve_DefaultEnabled"), PersonaDirectorAutoEvolveCompat.DefaultEnabled(), T("RPD_AutoEvolve_DefaultEnabledTip"));
                case SettingItem.Notify:
                    return ComboBox(T("RPD_AutoEvolve_Notify"), T(NotifyKey(PersonaDirectorAutoEvolveCompat.NotifyOrdinal())), null);
                case SettingItem.Speed:
                    return new ElementDescription
                    {
                        Role = ElementRole.Button,
                        Label = PersonaDirectorAutoEvolveCompat.SpeedLabel(PersonaDirectorAutoEvolveCompat.SpeedOrdinal()),
                        Extras = T("RPD_AutoEvolve_SpeedProtectTooltip"),
                    };
                default:
                    return TextField(T("RPD_AutoEvolve_NotesLabel"), PersonaDirectorAutoEvolveCompat.Notes(), T("RPD_AutoEvolve_InternalPromptNotice"));
            }
        }

        private ElementDescription DescribeTrigger(int index)
        {
            TriggerRow row = triggers[index];
            if (row.Category == null && row.Field == null)
            {
                return new ElementDescription { Label = row.Label, ReadOnly = true };
            }
            bool on = row.Category != null
                ? PersonaDirectorAutoEvolveCompat.RoleTrigger(row.Category)
                : PersonaDirectorAutoEvolveCompat.EventTrigger(row.Field);
            return Checkbox(row.Label, on, row.Tip);
        }

        private ElementDescription DescribeListControl(ListControlItem item)
        {
            switch (item)
            {
                case ListControlItem.Search:
                    return TextField("RimWorldAccess.Compat.RimTalk.PersonaDirector.SearchLabel".Translate(), table.SearchText, T("RPD_Batch_SearchTip"));
                case ListControlItem.AdvancedPreset:
                    string preset = PersonaDirectorAutoEvolveCompat.EvolvePreset();
                    return ComboBox(T("RPD_AutoEvolve_AdvancedPreset"), string.IsNullOrEmpty(preset) ? T("RPD_Setting_NoneInternal") : preset,
                        T("RPD_AutoEvolve_AdvancedPresetTooltip"));
                case ListControlItem.Mode:
                    return ComboBox(T("RPD_AutoEvolve_Mode"), T(ModeKey(PersonaDirectorAutoEvolveCompat.ModeOrdinal())),
                        SpeechFlatten.ToSentences(T("RPD_AutoEvolve_ModeTooltip")));
                case ListControlItem.SelectedCount:
                    return new ElementDescription
                    {
                        Label = TranslatorFormattedStringExtensions.Translate("RPD_AutoEvolve_SelectedCount", SelectedVisibleCount()),
                        ReadOnly = true,
                    };
                case ListControlItem.BatchInterval:
                    return TextField(T("RPD_AutoEvolve_BatchInterval"), PersonaDirectorAutoEvolveCompat.BatchIntervalBuffer(window), T("RPD_AutoEvolve_BatchIntervalTip"));
                default:
                    return TextField(T("RPD_History_MaxRecords"), PersonaDirectorAutoEvolveCompat.HistoryLimitBuffer(window), T("RPD_History_MaxRecordsTip"));
            }
        }

        private static ElementDescription Checkbox(string label, bool on, string tip)
        {
            return new ElementDescription
            {
                Role = ElementRole.Checkbox,
                Label = label,
                Check = on ? CheckState.Checked : CheckState.Unchecked,
                Extras = tip,
            };
        }

        private static ElementDescription ComboBox(string label, string value, string tip)
        {
            return new ElementDescription { Role = ElementRole.ComboBox, Label = label, Value = value, Extras = tip };
        }

        private static ElementDescription TextField(string label, string value, string tip)
        {
            ElementDescription d = new ElementDescription { Role = ElementRole.TextField, Label = label, Extras = tip };
            if (string.IsNullOrEmpty(value)) d.ValueBlank = true; else d.Value = value;
            return d;
        }

        private static string NotifyKey(int ordinal)
        {
            return ordinal == 0 ? "RPD_AutoEvolve_NotifySilent" : "RPD_AutoEvolve_NotifyTopLeftMessage";
        }

        private static string ModeKey(int ordinal)
        {
            return ordinal == 0 ? "RPD_AutoEvolve_ModeAppend" : "RPD_AutoEvolve_ModeOverwrite";
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (region)
            {
                case SettingsRegion:
                    ActivateSetting((SettingItem)index);
                    return;
                case TriggersRegion:
                    ToggleTrigger(index);
                    return;
                case ListControlsRegion:
                    ActivateListControl((ListControlItem)index);
                    return;
                default:
                    table.ToggleSelection(index);
                    AnnounceCurrentItem();
                    return;
            }
        }

        private void ActivateSetting(SettingItem item)
        {
            switch (item)
            {
                case SettingItem.Enable:
                    PersonaDirectorAutoEvolveCompat.SetGlobalEnabled(window, !PersonaDirectorAutoEvolveCompat.GlobalEnabled());
                    AnnounceCurrentItem();
                    return;
                case SettingItem.DefaultEnabled:
                    PersonaDirectorAutoEvolveCompat.SetDefaultEnabled(window, !PersonaDirectorAutoEvolveCompat.DefaultEnabled());
                    AnnounceCurrentItem();
                    return;
                case SettingItem.Notify:
                    OpenNotifyMenu();
                    return;
                case SettingItem.Speed:
                    OpenSpeedMenu();
                    return;
                default:
                    BeginEdit(T("RPD_AutoEvolve_NotesLabel"), PersonaDirectorAutoEvolveCompat.Notes(), PersonaDirectorAutoEvolveCompat.SetNotes);
                    return;
            }
        }

        private void ToggleTrigger(int index)
        {
            TriggerRow row = triggers[index];
            if (row.Category != null)
            {
                PersonaDirectorAutoEvolveCompat.SetRoleTrigger(window, row.Category, !PersonaDirectorAutoEvolveCompat.RoleTrigger(row.Category));
            }
            else if (row.Field != null)
            {
                PersonaDirectorAutoEvolveCompat.SetEventTrigger(window, row.Field, !PersonaDirectorAutoEvolveCompat.EventTrigger(row.Field));
            }
            AnnounceCurrentItem();
        }

        private void ActivateListControl(ListControlItem item)
        {
            switch (item)
            {
                case ListControlItem.Search:
                    BeginEdit("RimWorldAccess.Compat.RimTalk.PersonaDirector.SearchLabel".Translate(), table.SearchText, table.ApplySearch);
                    return;
                case ListControlItem.AdvancedPreset:
                    OpenAdvancedPresetMenu();
                    return;
                case ListControlItem.Mode:
                    OpenModeMenu();
                    return;
                case ListControlItem.BatchInterval:
                    BeginEdit(T("RPD_AutoEvolve_BatchInterval"), PersonaDirectorAutoEvolveCompat.BatchIntervalBuffer(window),
                        value => PersonaDirectorAutoEvolveCompat.SetBatchIntervalBuffer(window, value));
                    return;
                case ListControlItem.HistoryLimit:
                    BeginEdit(T("RPD_History_MaxRecords"), PersonaDirectorAutoEvolveCompat.HistoryLimitBuffer(window),
                        value => PersonaDirectorAutoEvolveCompat.SetHistoryLimitBuffer(window, value));
                    return;
                default:
                    AnnounceCurrentItem();
                    return;
            }
        }

        private void BeginEdit(string label, string current, System.Action<string> apply)
        {
            TextFieldSpec spec = new TextFieldSpec(labelKey: "RimWorldAccess.TextInput.LabelDefault", maxLength: null, minLength: 0);
            session.EnterEdit(current, spec, label, apply, AnnounceCurrentItem);
        }

        // Menus. Each rebuilds the button's own FloatMenu; the options call the window's own setters.

        /// <summary>MUTATION-C: mirrors the Notify button's own FloatMenu (Silent, Top-Left Message), each option calling the window's SetNotify.</summary>
        private void OpenNotifyMenu()
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption(T(NotifyKey(0)), () => PersonaDirectorAutoEvolveCompat.SetNotify(window, 0)),
                new FloatMenuOption(T(NotifyKey(1)), () => PersonaDirectorAutoEvolveCompat.SetNotify(window, 1)),
            }));
        }

        /// <summary>MUTATION-C: mirrors the speed button's own FloatMenu (every SpeedProtection value in order), each option calling the window's SetSpeed.</summary>
        private void OpenSpeedMenu()
        {
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < 4; i++)
            {
                int ordinal = i;
                options.Add(new FloatMenuOption(PersonaDirectorAutoEvolveCompat.SpeedLabel(ordinal),
                    () => PersonaDirectorAutoEvolveCompat.SetSpeed(window, ordinal)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>MUTATION-C: mirrors the Mode button's own FloatMenu (Append, Overwrite), each option calling the window's SetMode.</summary>
        private void OpenModeMenu()
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption(T(ModeKey(0)), () => PersonaDirectorAutoEvolveCompat.SetMode(window, 0)),
                new FloatMenuOption(T(ModeKey(1)), () => PersonaDirectorAutoEvolveCompat.SetMode(window, 1)),
            }));
        }

        /// <summary>MUTATION-C: mirrors DrawAdvancedPresetSelector's FloatMenu (the internal prompt first, then RimTalk's named presets), each option calling the window's SetAdvancedPreset.</summary>
        private void OpenAdvancedPresetMenu()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(T("RPD_Setting_NoneInternal"), () => PersonaDirectorAutoEvolveCompat.SetAdvancedPreset(window, "")),
            };
            foreach (string name in PersonaDirectorAutoEvolveCompat.RimTalkPresetNames())
            {
                string presetName = name;
                options.Add(new FloatMenuOption(presetName, () => PersonaDirectorAutoEvolveCompat.SetAdvancedPreset(window, presetName)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // The pawn table.

        protected override int ContentColumnCount(int region)
        {
            return region == PawnsRegion ? ColHistory + 1 : 0;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region != PawnsRegion) return null;
            switch (column)
            {
                case ColStatus: return new TableColumnInfo(T("RPD_AutoEvolve_HeaderAuto"), T("RPD_AutoEvolve_HeaderAutoTip"));
                case ColCycle: return new TableColumnInfo(T("RPD_AutoEvolve_HeaderCycle"));
                case ColNext: return new TableColumnInfo(T("RPD_AutoEvolve_HeaderNext"));
                case ColEdit: return new TableColumnInfo(T("RPD_AutoEvolve_ButtonEdit"));
                case ColHistory: return new TableColumnInfo(T("RPD_AutoEvolve_ButtonHistory"));
                default: return PersonaDirectorRosterTable.ColumnInfo(column);
            }
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            Pawn pawn = table.PawnAt(row);
            if (region != PawnsRegion || pawn == null) return "";
            switch (column)
            {
                case ColStatus:
                    return TranslatedShellVocabulary.Instance.Word(
                        PersonaDirectorAutoEvolveCompat.AutoEnabled(pawn) ? ElementStateWord.Checked : ElementStateWord.Unchecked);
                case ColCycle:
                    return PersonaDirectorAutoEvolveCompat.DaysBuffer(pawn);
                case ColNext:
                    return PersonaDirectorAutoEvolveCompat.NextText(pawn);
                case ColEdit:
                    return T("RPD_AutoEvolve_ButtonEdit");
                case ColHistory:
                    return T("RPD_AutoEvolve_ButtonHistory");
                default:
                    return table.CellText(row, column);
            }
        }

        protected override string ContentCellTip(int region, int row, int column)
        {
            return region == PawnsRegion ? table.CellTip(row, column) : null;
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            Pawn pawn = table.PawnAt(row);
            if (region != PawnsRegion || pawn == null) return false;
            switch (column)
            {
                case ColStatus:
                    if (PersonaDirectorAutoEvolveCompat.WorldComponent() == null) return true;
                    PersonaDirectorAutoEvolveCompat.ApplyEnabled(window, new List<Pawn> { pawn }, !PersonaDirectorAutoEvolveCompat.AutoEnabled(pawn));
                    AnnounceCurrentCellStateChange();
                    return true;
                case ColCycle:
                    BeginEdit(T("RPD_AutoEvolve_HeaderCycle"), PersonaDirectorAutoEvolveCompat.DaysBuffer(pawn),
                        value => PersonaDirectorAutoEvolveCompat.SetDaysBuffer(pawn, value));
                    return true;
                case ColEdit:
                    PersonaDirectorCompat.OpenPersonaEditorPin(pawn);
                    return true;
                case ColHistory:
                    PersonaDirectorAutoEvolveCompat.OpenHistory(pawn);
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

        // The Buttons region.

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return false; }
        }

        private int SelectedVisibleCount()
        {
            return PersonaDirectorAutoEvolveCompat.SelectedVisiblePawns(table.Roster).Count;
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(T("RPD_Tab_ManualGeneration"), () => PersonaDirectorCompat.SwitchToBatch(window)));
                actions.Add(table.SelectAllFiltersAction());
                actions.Add(table.RefreshAction());
                table.AddFilterActions(actions);
                actions.Add(table.SelectAllListedAction());

                // DrawBatchToolbar's GUI.enabled gates.
                int count = SelectedVisibleCount();
                bool hasWorld = PersonaDirectorAutoEvolveCompat.WorldComponent() != null;
                int unused;
                bool intervalParses = int.TryParse(PersonaDirectorAutoEvolveCompat.BatchIntervalBuffer(window), out unused);
                actions.Add(new ScreenAction(T("RPD_AutoEvolve_EnableSelected"), () => ApplySelectedEnabled(true),
                    disabled: count == 0 || !hasWorld));
                actions.Add(new ScreenAction(T("RPD_AutoEvolve_DisableSelected"), () => ApplySelectedEnabled(false),
                    disabled: count == 0 || !hasWorld));
                actions.Add(new ScreenAction(T("RPD_AutoEvolve_ApplyInterval"), ApplySelectedInterval,
                    disabled: count == 0 || !hasWorld || !intervalParses));
                actions.Add(new ScreenAction(T("RPD_AutoEvolve_ClearSelection"), ClearSelection, disabled: count == 0));
                return actions;
            }
        }

        private void ApplySelectedEnabled(bool enabled)
        {
            PersonaDirectorAutoEvolveCompat.ApplyEnabled(window, PersonaDirectorAutoEvolveCompat.SelectedVisiblePawns(table.Roster), enabled);
            RefreshModel();
        }

        private void ApplySelectedInterval()
        {
            int days;
            if (!int.TryParse(PersonaDirectorAutoEvolveCompat.BatchIntervalBuffer(window), out days)) return;
            PersonaDirectorAutoEvolveCompat.ApplyInterval(window, PersonaDirectorAutoEvolveCompat.SelectedVisiblePawns(table.Roster), days);
            RefreshModel();
        }

        private void ClearSelection()
        {
            object roster = table.Roster;
            if (roster == null) return;
            PersonaDirectorAutoEvolveCompat.ClearSelection(roster);
            RefreshModel();
        }

        protected override string ComposeOpenAnnouncement()
        {
            return T("RPD_Tab_AutoEvolution");
        }
    }
}
