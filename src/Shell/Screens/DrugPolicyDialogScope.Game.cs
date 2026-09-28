using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The <see cref="PolicyDialogScope"/> for <see cref="Dialog_ManageDrugPolicies"/>, whose
    /// <c>DoContentsRect</c> draws the drug grid rather than a ThingFilterUI panel. The grid is
    /// content region 1 of the one window; region 0 stays the policy list.
    ///
    /// The drug list is a real table over vanilla's own eight columns, in vanilla's own order, each
    /// carrying the tooltip vanilla attaches to it in DoColumnLabels. Every setting is edited in the
    /// grid itself: Up/Down move between drugs, Left/Right between columns, Space or Enter toggle a
    /// checkbox cell, and on a numeric or slider cell "+"/"-" step the value in place while Enter
    /// opens an in-place chooser (Up/Down step, digits type where the value is a plain number, Enter
    /// or Escape closes). Alt+I opens the focused drug's info card.
    ///
    /// ELEMENT SEMANTICS, with <c>Dialog_ManageDrugPolicies.DoEntryRow</c> as the authority: "Keep in
    /// inventory" is its <c>Widgets.TextFieldNumeric</c> bounded by
    /// <c>PawnUtility.GetMaxAllowedToPickUp</c>; the three usage columns are its <c>Widgets.Checkbox</c>
    /// calls; "Frequency" is its <c>FrequencyHorizontalSlider(0.1f, 25f)</c> and the two thresholds its
    /// <c>HorizontalSlider(0.01f, 1f)</c> calls, shown only while a drug is scheduled.
    ///
    /// WHY THE MUTATIONS ARE MUTATION-C, NOT A VANILLA WIDGET CALL: <c>Widgets.Checkbox</c> and the
    /// sliders are immediate-mode — they mutate their <c>ref</c> field and play their sound only when
    /// their invisible button sees a real mouse-down inside its rect during a draw pass. There is no
    /// delegate to invoke; forging that mouse event mid-draw is the window-pass ordering trap the
    /// doctrine forbids. So this writes the identical fields with the identical clamps and plays the
    /// identical sound (<c>Checkbox_TurnedOn/Off</c>, <c>DragSlider</c>) the widget would.
    ///
    /// The chooser is an in-place mode on THIS scope, never a second scope: the class remarks on
    /// <see cref="PolicyDialogScope"/> forbid layering a scope over the real window. While it is open
    /// the region reports zero columns (<see cref="ContentColumnCount"/>), which freezes Left/Right,
    /// and every operation runs off <see cref="chooseRow"/>/<see cref="chooseColumn"/> rather than the
    /// model cursor; <see cref="EndChoose"/> re-seats the cursor from those.
    /// </summary>
    public sealed class DrugPolicyDialogScope : PolicyDialogScope
    {
        private const int DrugsRegion = FirstContentsRegion;

        // Harvested from the vanilla threshold slider calls this screen stands in for.
        private const float MinThreshold = 0.01f;
        private const float MaxThreshold = 1f;

        private DrugPolicy policy;

        // In-place chooser state. All three are meaningful only while choosing.
        private bool choosing;
        private int chooseRow;
        private DrugColumn chooseColumn;
        private string numericBuffer = "";

        public DrugPolicyDialogScope(Window dialog) : base(dialog)
        {
            Claim("drugPolicy.settingDecrease", delegate { StepCell(-1); }, when: CanStep);
            Claim("drugPolicy.settingIncrease", delegate { StepCell(1); }, when: CanStep);
            Claim("drugPolicy.infoCard", delegate { OpenInfoCard(); },
                when: delegate { return Model.RegionIndex == DrugsRegion; });
            Claim(SharedMenuGrammar.Cancel, delegate { ShellFrameStamps.MarkCancelConsumed(); EndChoose(); },
                when: delegate { return choosing; });
            Claim(SharedMenuGrammar.SearchBackspace, delegate { BackspaceChoose(); },
                when: delegate { return choosing && numericBuffer.Length > 0; });
            RegisterPopTeardown(EndChooseSilently);
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        /// <summary>Escape closes the chooser first; only outside it does vanilla close the window.</summary>
        public override bool OwnsCancel
        {
            get { return choosing || base.OwnsCancel; }
        }

        // ------------------------------------------------------------------
        // Focus ring. Vanilla highlights the selected POLICY but nothing in the drug grid, so the
        // grid's focused cell is ringed from the geometry DrugPolicyRowDrawPatch records off
        // vanilla's own row method. While choosing, the ring holds the captured cell.
        // ------------------------------------------------------------------

        /// <summary>The drug row the ring belongs on, or -1 when the cursor is off any drug (the header, or another region).</summary>
        internal int FocusedDrugIndex
        {
            get
            {
                if (Model.RegionIndex != DrugsRegion)
                {
                    return -1;
                }
                return choosing ? chooseRow : DrugListDataRow();
            }
        }

        protected internal override Rect FocusedContentRect()
        {
            if (Model.RegionIndex != DrugsRegion)
            {
                return base.FocusedContentRect();
            }
            Rect cell = FocusedCellRect();
            if (cell.width > 0f && cell.height > 0f)
            {
                return cell;
            }
            return DrugPolicyRowDrawPatch.RowRect();
        }

        /// <summary>Empty on the name column or when the focused cell's widget is not drawn this frame, which sends the ring back to the whole row.</summary>
        private Rect FocusedCellRect()
        {
            int row = choosing ? chooseRow : DrugListDataRow();
            if (row < 0)
            {
                return default(Rect);
            }
            switch (choosing ? chooseColumn : CurrentColumn())
            {
                case DrugColumn.TakeToInventory:
                    return DrugPolicyRowDrawPatch.CellRect(DrugRowCell.TakeToInventory);
                case DrugColumn.ForAddiction:
                    return DrugPolicyRowDrawPatch.CellRect(DrugRowCell.AllowForAddiction);
                case DrugColumn.ForJoy:
                    return DrugPolicyRowDrawPatch.CellRect(DrugRowCell.AllowForJoy);
                case DrugColumn.Scheduled:
                    return DrugPolicyRowDrawPatch.CellRect(DrugRowCell.AllowScheduled);
                case DrugColumn.Frequency:
                    return DrugPolicyRowDrawPatch.CellRect(DrugRowCell.Frequency);
                case DrugColumn.MoodThreshold:
                    return DrugPolicyRowDrawPatch.CellRect(DrugRowCell.MoodThreshold);
                case DrugColumn.JoyThreshold:
                    return DrugPolicyRowDrawPatch.CellRect(DrugRowCell.JoyThreshold);
                default:
                    return default(Rect);
            }
        }

        // ------------------------------------------------------------------
        // Contents contract.
        // ------------------------------------------------------------------

        protected override int ContentsRegionCount
        {
            get { return 1; }
        }

        protected override string ContentsRegionName(int region)
        {
            return "DrugPolicyTitle".Translate().ToString();
        }

        protected override string TreeRegionLabel
        {
            get { return ContentsRegionName(DrugsRegion); }
        }

        protected override void RebuildContents()
        {
            policy = Selected as DrugPolicy;
        }

        // ------------------------------------------------------------------
        // Typeahead: the drug names, letters only.
        // ------------------------------------------------------------------

        protected override bool ContentRegionSearchable(int region)
        {
            return region == PoliciesRegion || region == DrugsRegion;
        }

        protected override bool TypeaheadAcceptsDigits
        {
            get { return false; }
        }

        // ------------------------------------------------------------------
        // Rows: one per drug, the name as the row's identity.
        // ------------------------------------------------------------------

        protected override int ContentItemCount(int region)
        {
            if (region != DrugsRegion)
            {
                return base.ContentItemCount(region);
            }
            return policy == null ? 0 : policy.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region != DrugsRegion)
            {
                return base.DescribeContentItem(region, index);
            }
            var d = new ElementDescription();
            if (policy != null && index >= 0 && index < policy.Count)
            {
                ThingDef drug = policy[index].drug;
                d.Label = drug != null ? drug.LabelCap.ToString() : "";
            }
            return d;
        }

        // ------------------------------------------------------------------
        // Table contract: vanilla's own eight columns, in vanilla's own order.
        // ------------------------------------------------------------------

        private enum DrugColumn
        {
            Name,
            TakeToInventory,
            ForAddiction,
            ForJoy,
            Scheduled,
            Frequency,
            MoodThreshold,
            JoyThreshold,
        }

        /// <summary>Zero while choosing flattens the region so Left/Right cannot leave the edited cell; eight otherwise.</summary>
        protected override int ContentColumnCount(int region)
        {
            if (region != DrugsRegion)
            {
                return base.ContentColumnCount(region);
            }
            return choosing ? 0 : 8;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region != DrugsRegion)
            {
                return base.ContentColumnInfo(region, column);
            }
            string label;
            string tip;
            switch ((DrugColumn)column)
            {
                case DrugColumn.TakeToInventory:
                    label = "TakeToInventoryColumnLabel".Translate();
                    tip = "TakeToInventoryColumnDesc".Translate();
                    break;
                case DrugColumn.ForAddiction:
                    SplitUsageTip("DrugUsageTipForAddiction", out label, out tip);
                    break;
                case DrugColumn.ForJoy:
                    SplitUsageTip("DrugUsageTipForJoy", out label, out tip);
                    break;
                case DrugColumn.Scheduled:
                    SplitUsageTip("DrugUsageTipScheduled", out label, out tip);
                    break;
                case DrugColumn.Frequency:
                    label = "FrequencyColumnLabel".Translate();
                    tip = "FrequencyColumnDesc".Translate();
                    break;
                case DrugColumn.MoodThreshold:
                    label = "MoodThresholdColumnLabel".Translate();
                    tip = "MoodThresholdColumnDesc".Translate();
                    break;
                case DrugColumn.JoyThreshold:
                    label = "JoyThresholdColumnLabel".Translate();
                    tip = "JoyThresholdColumnDesc".Translate();
                    break;
                default:
                    label = "DrugColumnLabel".Translate();
                    tip = "DrugNameColumnDesc".Translate();
                    break;
            }
            // Vanilla offers no sorting on this grid.
            return new TableColumnInfo(label, tip, false);
        }

        /// <summary>
        /// Cell text mirrors what vanilla DRAWS: a column vanilla leaves blank for this row reads as
        /// empty rather than as a default value.
        /// </summary>
        protected override string ContentCellText(int region, int row, int column)
        {
            if (region != DrugsRegion)
            {
                return base.ContentCellText(region, row, column);
            }
            if (policy == null || row < 0 || row >= policy.Count)
            {
                return "";
            }
            DrugPolicyEntry entry = policy[row];
            switch ((DrugColumn)column)
            {
                case DrugColumn.Name:
                    return entry.drug != null ? entry.drug.LabelCap.ToString() : "";
                case DrugColumn.TakeToInventory:
                    return entry.takeToInventory.ToString();
                case DrugColumn.ForAddiction:
                    return entry.drug.IsAddictiveDrug ? FormatCheck(entry.allowedForAddiction) : "";
                case DrugColumn.ForJoy:
                    return entry.drug.IsPleasureDrug ? FormatCheck(entry.allowedForJoy) : "";
                case DrugColumn.Scheduled:
                    return FormatCheck(entry.allowScheduled);
                case DrugColumn.Frequency:
                    return entry.allowScheduled ? FormatFrequency(entry.daysFrequency) : "";
                case DrugColumn.MoodThreshold:
                    return entry.allowScheduled ? FormatThreshold(entry.onlyIfMoodBelow) : "";
                case DrugColumn.JoyThreshold:
                    return entry.allowScheduled ? FormatThreshold(entry.onlyIfJoyBelow) : "";
                default:
                    return "";
            }
        }

        // ------------------------------------------------------------------
        // In-cell editing. Enter/Space on a checkbox toggles; Enter on a numeric or slider cell
        // opens the in-place chooser; Enter/Space while choosing closes it.
        // ------------------------------------------------------------------

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (region != DrugsRegion || policy == null || row < 0 || row >= policy.Count)
            {
                return false;
            }
            DrugPolicyEntry entry = policy[row];
            switch ((DrugColumn)column)
            {
                case DrugColumn.ForAddiction:
                    if (!entry.drug.IsAddictiveDrug)
                    {
                        return false;
                    }
                    ToggleCheckbox(row, ref entry.allowedForAddiction);
                    return true;
                case DrugColumn.ForJoy:
                    if (!entry.drug.IsPleasureDrug)
                    {
                        return false;
                    }
                    ToggleCheckbox(row, ref entry.allowedForJoy);
                    return true;
                case DrugColumn.Scheduled:
                    ToggleCheckbox(row, ref entry.allowScheduled);
                    return true;
                case DrugColumn.Frequency:
                case DrugColumn.MoodThreshold:
                case DrugColumn.JoyThreshold:
                    if (!entry.allowScheduled)
                    {
                        SpeakNeedsScheduled();
                        return true;
                    }
                    goto case DrugColumn.TakeToInventory;
                case DrugColumn.TakeToInventory:
                    BeginChoose(row, (DrugColumn)column);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Enter/Space while choosing (the region is flattened, so activation lands here) closes the chooser.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (choosing)
            {
                EndChoose();
                return;
            }
            base.ActivateContentItem(region, index);
        }

        // MUTATION-C: mirrors Dialog_ManageDrugPolicies.DoEntryRow's Widgets.Checkbox(ref
        // entry.allowedForAddiction/allowedForJoy/allowScheduled, ...) (Dialog_ManageDrugPolicies.cs:
        // 192,197,200), which flips the ref bool directly with no gated setter; the sound is the one
        // Widgets.ToggleInvisibleDraggable plays on that flip (Widgets.cs:1259,1263).
        private void ToggleCheckbox(int row, ref bool flag)
        {
            flag = !flag;
            (flag ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            SpeakCellValue(row, CurrentColumn());
        }

        // ------------------------------------------------------------------
        // Stepping: "+"/"-" in place, and Up/Down/Home/End while choosing.
        // ------------------------------------------------------------------

        /// <summary>Side-effect free: the "+"/"-" claims are always live while choosing, else only on an adjustable cell.</summary>
        private bool CanStep()
        {
            if (choosing)
            {
                return true;
            }
            if (Model.RegionIndex != DrugsRegion)
            {
                return false;
            }
            DrugPolicyEntry entry = CurrentDrugEntry();
            if (entry == null)
            {
                return false;
            }
            switch (CurrentColumn())
            {
                case DrugColumn.TakeToInventory:
                case DrugColumn.Frequency:
                case DrugColumn.MoodThreshold:
                case DrugColumn.JoyThreshold:
                    return true;
                default:
                    return false;
            }
        }

        private void SpeakNeedsScheduled()
        {
            string label = ContentColumnInfo(DrugsRegion, (int)DrugColumn.Scheduled).Label;
            TolkHelper.SpeakData("RimWorldAccess.DrugPolicy.NeedsScheduled".Translate(label).ToString());
        }

        /// <summary>One step of the focused numeric or slider cell; direction +1 raises the value, -1 lowers it.</summary>
        private void StepCell(int direction)
        {
            int row;
            DrugColumn column;
            if (choosing)
            {
                row = chooseRow;
                column = chooseColumn;
            }
            else
            {
                RefreshModel();
                row = DrugListDataRow();
                column = CurrentColumn();
            }
            if (policy == null || row < 0 || row >= policy.Count)
            {
                return;
            }
            numericBuffer = "";
            DrugPolicyEntry entry = policy[row];
            if (column != DrugColumn.TakeToInventory && !entry.allowScheduled)
            {
                SpeakNeedsScheduled();
                return;
            }
            bool changed;
            SoundDef sound;
            // MUTATION-C: TakeToInventory mirrors the bounds of DoEntryRow's Widgets.TextFieldNumeric(...,
            // 0f, PawnUtility.GetMaxAllowedToPickUp(entry.drug)) (Dialog_ManageDrugPolicies.cs:188);
            // Frequency and the thresholds hand-step over the bounds of the same method's
            // Widgets.FrequencyHorizontalSlider(0.1f, 25f) and Widgets.HorizontalSlider(0.01f, 1f) calls
            // (Dialog_ManageDrugPolicies.cs:204,206,208) — a drag slider has no discrete-step vehicle for
            // the keyboard, so the bounds are harvested and the step is ours. Sounds are the widget's own.
            switch (column)
            {
                case DrugColumn.TakeToInventory:
                {
                    int max = PawnUtility.GetMaxAllowedToPickUp(entry.drug);
                    int next = Mathf.Clamp(entry.takeToInventory + direction, 0, max);
                    changed = next != entry.takeToInventory;
                    entry.takeToInventory = next;
                    sound = SoundDefOf.Tick_Tiny;
                    break;
                }
                case DrugColumn.Frequency:
                {
                    float next = AdjustDrugFrequency(entry.daysFrequency, direction);
                    changed = next != entry.daysFrequency;
                    entry.daysFrequency = next;
                    sound = SoundDefOf.DragSlider;
                    break;
                }
                case DrugColumn.MoodThreshold:
                {
                    float next = Mathf.Clamp(entry.onlyIfMoodBelow + direction * 0.05f, MinThreshold, MaxThreshold);
                    changed = next != entry.onlyIfMoodBelow;
                    entry.onlyIfMoodBelow = next;
                    sound = SoundDefOf.DragSlider;
                    break;
                }
                case DrugColumn.JoyThreshold:
                {
                    float next = Mathf.Clamp(entry.onlyIfJoyBelow + direction * 0.05f, MinThreshold, MaxThreshold);
                    changed = next != entry.onlyIfJoyBelow;
                    entry.onlyIfJoyBelow = next;
                    sound = SoundDefOf.DragSlider;
                    break;
                }
                default:
                    return;
            }
            if (changed)
            {
                sound.PlayOneShotOnCamera();
            }
            SpeakCellValue(row, column);
        }

        // ------------------------------------------------------------------
        // The in-place chooser: opened with Enter, driven by Up/Down/Home/End/"+"/"-" and typed
        // digits, closed with Enter or Escape. Every change is already live in the entry.
        // ------------------------------------------------------------------

        private void BeginChoose(int row, DrugColumn column)
        {
            choosing = true;
            chooseRow = row;
            chooseColumn = column;
            numericBuffer = "";
            SoundDefOf.Click.PlayOneShotOnCamera();
            RefreshModel();
            SpeakCellValue(row, column);
        }

        private void EndChoose()
        {
            if (!choosing)
            {
                return;
            }
            choosing = false;
            numericBuffer = "";
            SoundDefOf.Click.PlayOneShotOnCamera();
            RefreshModel();
            TableModel table = Model.Table(DrugsRegion);
            if (table != null)
            {
                table.Rows.MoveTo(chooseRow + 1);
                table.MoveToColumn((int)chooseColumn);
            }
            AnnounceCurrentItem();
        }

        private void EndChooseSilently()
        {
            choosing = false;
            numericBuffer = "";
        }

        /// <summary>Up/Down step the value (Up raises); Home/End jump to the bottom/top of the range.</summary>
        protected override void MoveItem(int delta)
        {
            if (!choosing)
            {
                base.MoveItem(delta);
                return;
            }
            StepCell(delta < 0 ? 1 : -1);
        }

        protected override void MoveItemEdge(bool first)
        {
            if (!choosing)
            {
                base.MoveItemEdge(first);
                return;
            }
            StepCell(first ? -1000 : 1000);
        }

        /// <summary>Tab is inert while choosing so a region cycle cannot strand an open editor.</summary>
        protected override void MoveRegion(bool forward)
        {
            if (choosing)
            {
                return;
            }
            base.MoveRegion(forward);
        }

        /// <summary>While choosing, digits type an exact value on the columns that take one; all characters are consumed so typeahead never engages.</summary>
        public override bool HandleChar(char c)
        {
            if (!choosing)
            {
                return base.HandleChar(c);
            }
            if (char.IsDigit(c))
            {
                AppendChooseDigit(c);
            }
            return true;
        }

        private void AppendChooseDigit(char c)
        {
            if (chooseColumn != DrugColumn.TakeToInventory
                && chooseColumn != DrugColumn.MoodThreshold
                && chooseColumn != DrugColumn.JoyThreshold)
            {
                return; // Frequency is a labeled ladder, not a plain number: arrow-only.
            }
            if (numericBuffer.Length >= 6)
            {
                return;
            }
            numericBuffer += c;
            ApplyTypedChoose();
            SpeakCellValue(chooseRow, chooseColumn);
        }

        private void BackspaceChoose()
        {
            numericBuffer = numericBuffer.Substring(0, numericBuffer.Length - 1);
            ApplyTypedChoose();
            SpeakCellValue(chooseRow, chooseColumn);
        }

        // MUTATION-C: same clamps as StepCell, applied to a typed value — TakeToInventory as a whole
        // count, the thresholds as a whole percent of the slider's 0.01..1 range.
        private void ApplyTypedChoose()
        {
            if (policy == null || chooseRow < 0 || chooseRow >= policy.Count
                || !int.TryParse(numericBuffer, out int typed))
            {
                return;
            }
            DrugPolicyEntry entry = policy[chooseRow];
            switch (chooseColumn)
            {
                case DrugColumn.TakeToInventory:
                    entry.takeToInventory = Mathf.Clamp(typed, 0, PawnUtility.GetMaxAllowedToPickUp(entry.drug));
                    break;
                case DrugColumn.MoodThreshold:
                    entry.onlyIfMoodBelow = Mathf.Clamp(typed / 100f, MinThreshold, MaxThreshold);
                    break;
                case DrugColumn.JoyThreshold:
                    entry.onlyIfJoyBelow = Mathf.Clamp(typed / 100f, MinThreshold, MaxThreshold);
                    break;
            }
        }

        private void SpeakCellValue(int row, DrugColumn column)
        {
            string value = ContentCellText(DrugsRegion, row, (int)column);
            if (!string.IsNullOrEmpty(value))
            {
                TolkHelper.SpeakData(value);
            }
        }

        /// <summary>Opens the info card for the focused drug, mirroring vanilla's per-row InfoCardButton.</summary>
        private void OpenInfoCard()
        {
            DrugPolicyEntry entry = CurrentDrugEntry();
            if (entry != null)
            {
                InfoCardState.TryOpenInfoCardForDef(entry.drug);
            }
        }

        // ------------------------------------------------------------------
        // Cursor helpers.
        // ------------------------------------------------------------------

        /// <summary>
        /// The policy index the drug-list cursor points at: the table row cursor counts the header
        /// at index 0, so the data row is one less, matching vanilla's own 0-based DoEntryRow. -1
        /// off any drug.
        /// </summary>
        private int DrugListDataRow()
        {
            ListModel drugs = Model.Region(DrugsRegion);
            if (drugs == null)
            {
                return -1;
            }
            int dataRow = Model.Table(DrugsRegion) != null ? drugs.Index - 1 : drugs.Index;
            return dataRow >= 0 && policy != null && dataRow < policy.Count ? dataRow : -1;
        }

        private DrugColumn CurrentColumn()
        {
            TableModel table = Model.Table(DrugsRegion);
            return (DrugColumn)(table != null ? table.ColumnIndex : 0);
        }

        private DrugPolicyEntry CurrentDrugEntry()
        {
            int row = DrugListDataRow();
            return row >= 0 ? policy[row] : null;
        }

        // ------------------------------------------------------------------
        // Value formatting and stepping.
        // ------------------------------------------------------------------

        /// <summary>
        /// Steps daysFrequency through the discrete values vanilla's FrequencyHorizontalSlider
        /// produces: whole "every N days" values 1..25 at or above 1, and whole "N times per day"
        /// values 2..10 below it. Direction +1 moves toward less frequent, -1 toward more frequent,
        /// bottoming out at vanilla's 0.1 minFreq floor.
        /// </summary>
        // MUTATION-C: hand-copied discrete stepping over the same range vanilla's
        // Widgets.FrequencyHorizontalSlider(minFreq: 0.1f, maxFreq: 25f, roundToInt: true) covers by
        // continuous drag (Dialog_ManageDrugPolicies.cs:204); no discrete-step vehicle exists in
        // vanilla for keyboard-driven adjustment, so the bounds (0.1..25) are harvested from the
        // slider call and the step logic is hand-written to match its labeled value set.
        private static float AdjustDrugFrequency(float freq, int direction)
        {
            const float MinFreq = 0.1f;
            const float MaxFreq = 25f;

            if (freq >= 1f)
            {
                float newDays = Mathf.Round(freq) + direction;
                if (newDays < 1f)
                {
                    return 0.5f; // cross into "2 times a day"
                }
                return Mathf.Clamp(newDays, 1f, MaxFreq);
            }

            int timesPerDay = Mathf.RoundToInt(1f / freq);
            int newTimesPerDay = timesPerDay - direction;
            if (newTimesPerDay < 2)
            {
                return 1f; // cross into "every day"
            }
            int maxTimesPerDay = Mathf.RoundToInt(1f / MinFreq);
            newTimesPerDay = Mathf.Clamp(newTimesPerDay, 2, maxTimesPerDay);
            return 1f / newTimesPerDay;
        }

        /// <summary>Vanilla's Widgets.FrequencyHorizontalSlider label formatting.</summary>
        private static string FormatFrequency(float freq)
        {
            if (freq == 1f)
            {
                return "EveryDay".Translate();
            }
            if (freq < 1f)
            {
                return "TimesPerDay".Translate((1f / freq).ToString("0.##"));
            }
            return "EveryDays".Translate(freq.ToString("0.##"));
        }

        /// <summary>The label vanilla passes to the two threshold sliders: its "no requirement" string at the top of the range, a percentage below it.</summary>
        private static string FormatThreshold(float value)
        {
            if (value >= MaxThreshold)
            {
                return "NoDrugUseRequirement".Translate();
            }
            return value.ToStringPercent();
        }

        /// <summary>Checkbox state for a TABLE CELL, where no role word carries it.</summary>
        private static string FormatCheck(bool on)
        {
            return TranslatedShellVocabulary.Instance.Word(
                on ? ElementStateWord.Checked : ElementStateWord.Unchecked);
        }

        /// <summary>
        /// Vanilla stores the three usage checkbox tips as "title\n\nbody" and draws only an icon,
        /// so the first line is the only name the game has for these columns. Splits on the game's
        /// own separator; matches no translated text.
        /// </summary>
        private static void SplitUsageTip(string key, out string label, out string description)
        {
            string full = key.Translate().Resolve();
            int idx = full.IndexOf('\n');
            if (idx < 0)
            {
                label = full;
                description = null;
                return;
            }
            label = full.Substring(0, idx);
            description = full.Substring(idx + 1).TrimStart('\n', ' ');
        }
    }
}
