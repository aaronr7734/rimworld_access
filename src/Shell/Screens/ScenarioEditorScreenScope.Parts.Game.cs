using System;
using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Steam;


namespace RimWorldAccess.Shell
{
    public sealed partial class ScenarioEditorScreenScope
    {
        // ------------------------------------------------------------------
        // Region 1 (edit mode ON): the parts tree. A part node's Data is its live ScenPart,
        // the stable identity the cross-rebuild preserver matches on.
        // ------------------------------------------------------------------

        private sealed class PartNode : InspectionTreeItem
        {
            public ScenarioBuilderState.PartTreeItem PartItem;
        }

        private sealed class ListItemNode : InspectionTreeItem
        {
            public ScenarioBuilderState.PartTreeItem PartItem;
            public ScenarioBuilderState.ListItemData ListItem;
        }

        private sealed class AddItemNode : InspectionTreeItem
        {
            public ScenarioBuilderState.PartTreeItem PartItem;
        }

        private sealed class FieldNode : InspectionTreeItem
        {
            public ScenarioBuilderState.PartField Field;
        }

        /// <summary>Fresh parts root from the state layer's just-rebuilt hierarchy.</summary>
        private InspectionTreeItem BuildPartsRoot()
        {
            ScenarioBuilderState.BuildPartsTree();
            var root = new InspectionTreeItem { IsExpandable = true, IsExpanded = true };
            foreach (ScenarioBuilderState.PartTreeItem part in ScenarioBuilderState.PartsHierarchy)
            {
                var partNode = new PartNode
                {
                    PartItem = part,
                    Data = part.Part,
                    Label = PartRowLabel(part),
                    IsExpandable = part.IsListPart || part.Fields.Count > 0,
                    IndentLevel = 0,
                    Parent = root,
                };
                foreach (ScenarioBuilderState.PartField field in part.Fields)
                {
                    partNode.Children.Add(new FieldNode { Field = field, Label = field.Name, IndentLevel = 1, Parent = partNode });
                }
                if (part.IsListPart)
                {
                    foreach (ScenarioBuilderState.ListItemData li in part.ListItems)
                    {
                        var liNode = new ListItemNode
                        {
                            PartItem = part,
                            ListItem = li,
                            Label = li.Label,
                            IsExpandable = li.Fields.Count > 0,
                            IndentLevel = 1,
                            Parent = partNode,
                        };
                        foreach (ScenarioBuilderState.PartField field in li.Fields)
                        {
                            liNode.Children.Add(new FieldNode { Field = field, Label = field.Name, IndentLevel = 2, Parent = liNode });
                        }
                        partNode.Children.Add(liNode);
                    }
                    partNode.Children.Add(new AddItemNode
                    {
                        PartItem = part,
                        Label = (string)"RimWorldAccess.ScenarioBuilder.AddNewItemLabel".Translate(),
                        IndentLevel = 1,
                        Parent = partNode,
                    });
                }
                root.Children.Add(partNode);
            }
            return root;
        }

        private static string PartRowLabel(ScenarioBuilderState.PartTreeItem part)
        {
            return string.IsNullOrEmpty(part.Summary)
                ? part.Label
                : (string)"RimWorldAccess.ScenarioBuilder.PartLabelWithSummary".Translate(part.Label, part.Summary);
        }

        protected override bool TryGetSliderBox(int region, int index, out float value, out bool high)
        {
            FieldNode box = SliderFieldAt(region, index);
            value = box != null ? box.Field.LiveSliderValue() : 0f;
            high = box != null && box.Field.SliderIsRangeMax;
            return box != null;
        }

        protected override bool HasOwnNumberEntry(int region, int index)
        {
            return SliderFieldAt(region, index)?.Field.Type == ScenarioBuilderState.FieldType.Quantity;
        }

        private FieldNode SliderFieldAt(int region, int index)
        {
            if (region != TreeRegionIndex || !editModeField(page))
                return null;
            int treeIndex = index - PrefixRowCountFor(region);
            IReadOnlyList<InspectionTreeItem> visible = partsPanel.Tree.Visible;
            if (treeIndex < 0 || treeIndex >= visible.Count)
                return null;
            return visible[treeIndex] is FieldNode field && field.Field.LiveSliderValue != null ? field : null;
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription();
            switch (item)
            {
                case AddItemNode _:
                    d.Label = item.Label;
                    d.Role = ElementRole.Button;
                    return d;
                case FieldNode field:
                    DescribeField(d, field.Field);
                    return d;
                default:
                    d.Label = item.Label;
                    if (item.IsExpandable)
                    {
                        d.Role = ElementRole.TreeItem;
                        d.Expanded = item.IsExpanded;
                    }
                    else
                    {
                        d.Role = ElementRole.None;
                        d.ReadOnly = true;
                    }
                    return d;
            }
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            switch (item)
            {
                case AddItemNode add:
                    ActivateAddListItem(add.PartItem);
                    return;
                case FieldNode field:
                    ActivateField(field.Field);
                    return;
                default:
                    // Enter toggles a branch; a leaf info row just re-reads.
                    if (item.IsExpandable)
                    {
                        PerformActivateExpandToggle(item);
                        return;
                    }
                    AnnounceCurrentItem();
                    return;
            }
        }

        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return item is PartNode || item is ListItemNode;
        }

        private static void DescribeField(ElementDescription d, ScenarioBuilderState.PartField field)
        {
            d.Label = field.Name;
            switch (field.Type)
            {
                case ScenarioBuilderState.FieldType.Dropdown:
                    d.Role = ElementRole.ComboBox;
                    d.Value = field.CurrentValue;
                    break;
                case ScenarioBuilderState.FieldType.Quantity:
                    d.Role = ElementRole.Stepper;
                    // Enter opens the exact-entry session (BeginQuantityEdit),
                    // so this row owns Enter rather than arming this screen's
                    // proceed offer (DefaultAcceptActionId).
                    d.EntersEditOnAccept = true;
                    d.Value = field.CurrentValue;
                    QuantityBounds(field, out double raw, out double min, out double max);
                    d.AtMinimum = raw <= min;
                    d.AtMaximum = raw >= max;
                    break;
                case ScenarioBuilderState.FieldType.Checkbox:
                    d.Role = ElementRole.Checkbox;
                    d.Check = field.BoolValue ? CheckState.Checked : CheckState.Unchecked;
                    break;
                case ScenarioBuilderState.FieldType.Text:
                    d.Role = ElementRole.TextField;
                    d.Value = field.CurrentValue;
                    d.ValueBlank = string.IsNullOrEmpty(field.Data as string);
                    break;
                default: // ReadOnly
                    d.Role = ElementRole.None;
                    d.ReadOnly = true;
                    d.Value = field.CurrentValue;
                    break;
            }
        }

        private void ActivateField(ScenarioBuilderState.PartField field)
        {
            switch (field.Type)
            {
                case ScenarioBuilderState.FieldType.Dropdown:
                    OpenDropdownPicker(field);
                    break;
                case ScenarioBuilderState.FieldType.Quantity:
                    BeginQuantityEdit(field);
                    break;
                case ScenarioBuilderState.FieldType.Checkbox:
                    ToggleCheckboxField(field);
                    break;
                case ScenarioBuilderState.FieldType.Text:
                    BeginTextFieldEdit(field);
                    break;
                default: // ReadOnly
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.FieldReadOnly".Loc());
                    break;
            }
        }

        private void ToggleCheckboxField(ScenarioBuilderState.PartField field)
        {
            bool newValue = !field.BoolValue;
            field.SetValue?.Invoke(newValue);
            ScenarioBuilderState.SetDirty();
            RebuildRegionTreePreservingState();
            var d = new ElementDescription { Check = newValue ? CheckState.Checked : CheckState.Unchecked };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        // ------------------------------------------------------------------
        // Dropdown fields: ComboBox row (Enter/Space open the picker; Left/Right
        // do not change the value — that is what separates a combo box from a
        // quantity stepper).
        // ------------------------------------------------------------------

        private static List<(string label, object value)> DropdownOptions(ScenarioBuilderState.PartField field)
        {
            return field.Data as List<(string label, object value)>;
        }

        private static int FindDropdownIndex(ScenarioBuilderState.PartField field, List<(string label, object value)> options)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].label == field.CurrentValue) return i;
            }
            return 0;
        }

        private void AdjustField(ScenarioBuilderState.PartField field, int direction, int stepMagnitude)
        {
            if (field.Type == ScenarioBuilderState.FieldType.Quantity)
            {
                StepQuantity(field, direction, stepMagnitude);
            }
        }

        private void OpenDropdownPicker(ScenarioBuilderState.PartField field)
        {
            List<(string label, object value)> options = DropdownOptions(field);
            if (options == null || options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartEdit.NoOptionsForField".Loc());
                return;
            }
            int current = FindDropdownIndex(field, options);
            var menuOptions = new List<FloatMenuOption>(options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                (string label, object value) opt = options[i];
                menuOptions.Add(new FloatMenuOption(opt.label, () =>
                {
                    field.SetValue?.Invoke(opt.value);
                    ScenarioBuilderState.SetDirty();
                    RebuildRegionTreePreservingState();
                    AnnounceQuantityOrComboStateChange(opt.label, false, false);
                }));
            }
            WindowlessFloatMenuState.Open(menuOptions, colonistOrders: false, startIndex: current, announceSelection: false);
        }

        // ------------------------------------------------------------------
        // Quantity fields: Stepper row (Left/Right +/- 1, Shift 10, Ctrl 100; Enter exact entry).
        // ------------------------------------------------------------------

        private static void QuantityBounds(ScenarioBuilderState.PartField field, out double raw, out double min, out double max)
        {
            if (field.Data is int[] ir)
            {
                min = ir[0]; max = ir[1];
                string s = (field.CurrentValue ?? "").Trim();
                raw = int.TryParse(s, out int iv) ? iv : min;
                return;
            }
            if (field.Data is float[] fr)
            {
                min = fr[0]; max = fr[1];
                string s = (field.CurrentValue ?? "").Replace("%", "").Trim();
                bool isPercent = field.IsPercentDisplay || max <= 1.0;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                {
                    raw = isPercent ? parsed / 100.0 : parsed;
                }
                else
                {
                    raw = min;
                }
                return;
            }
            min = 0; max = 100; raw = 0;
        }

        private void StepQuantity(ScenarioBuilderState.PartField field, int direction, int stepMagnitude)
        {
            QuantityBounds(field, out double raw, out double min, out double max);
            bool isFloat = field.Data is float[];
            bool isPercent = isFloat && (field.IsPercentDisplay || max <= 1.0);
            double delta = isPercent ? stepMagnitude * 0.01 : stepMagnitude;
            double newRaw = Math.Max(min, Math.Min(max, raw + direction * delta));

            if (isFloat)
            {
                field.SetValue?.Invoke((float)newRaw);
            }
            else
            {
                field.SetValue?.Invoke((int)Math.Round(newRaw));
            }
            ScenarioBuilderState.SetDirty();
            RebuildRegionTreePreservingState();

            // Re-read the SAME field after the rebuild — the setter (or a cross-field
            // clamp like pawnChoiceCount>=pawnCount) may have landed a different value
            // than requested; the honest post-mutation state is what BuildPartsTree just
            // re-derived from the live ScenPart via reflection.
            ScenarioBuilderState.PartField fresh = ReacquireField(field);
            if (fresh == null) return;
            QuantityBounds(fresh, out double freshRaw, out double freshMin, out double freshMax);
            AnnounceQuantityOrComboStateChange(fresh.CurrentValue, freshRaw <= freshMin, freshRaw >= freshMax);
        }

        private void AdjustQuantityFromChord(KeyEventSnapshot e)
        {
            ScenarioBuilderState.PartField field = CurrentField();
            if (field == null || field.Type != ScenarioBuilderState.FieldType.Quantity) return;
            int direction = e.Key == KeyCode.RightArrow ? 1 : -1;
            int magnitude = e.Ctrl ? 100 : (e.Shift ? 10 : 1);
            StepQuantity(field, direction, magnitude);
        }

        private void BeginQuantityEdit(ScenarioBuilderState.PartField field)
        {
            QuantityBounds(field, out double raw, out _, out _);
            bool isFloat = field.Data is float[];
            string current = isFloat
                ? (field.IsPercentDisplay || ((float[])field.Data)[1] <= 1.0 ? (raw * 100).ToString("F0") : raw.ToString(field.IsIntegerDisplay ? "F0" : "F1"))
                : raw.ToString("F0");
            var spec = TextFieldSpec.Unrestricted("RimWorldAccess.TextInput.LabelDefault");
            quantityEditSession.EnterEdit(current, spec, field.Name, value => ApplyQuantityEdit(field, value), () => FinishQuantityEdit(field));
        }

        private readonly TextFieldEditSession quantityEditSession = new TextFieldEditSession();

        private void ApplyQuantityEdit(ScenarioBuilderState.PartField field, string text)
        {
            QuantityBounds(field, out _, out double min, out double max);
            bool isFloat = field.Data is float[];
            bool isPercent = isFloat && (field.IsPercentDisplay || max <= 1.0);
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double typed))
            {
                return;
            }
            double raw = isPercent ? typed / 100.0 : typed;
            double clamped = Math.Max(min, Math.Min(max, raw));
            if (isFloat)
            {
                field.SetValue?.Invoke((float)clamped);
            }
            else
            {
                field.SetValue?.Invoke((int)Math.Round(clamped));
            }
            ScenarioBuilderState.SetDirty();
        }

        private void FinishQuantityEdit(ScenarioBuilderState.PartField field)
        {
            RebuildRegionTreePreservingState();
            AnnounceCurrentItem();
        }

        /// <summary>The field node under the cursor after a rebuild — PartField instances are
        /// recreated fresh each rebuild, so the pre-mutation reference is stale.</summary>
        private ScenarioBuilderState.PartField ReacquireField(ScenarioBuilderState.PartField stale)
        {
            return CurrentTreeItem() is FieldNode field ? field.Field : null;
        }

        // ------------------------------------------------------------------
        // Text fields.
        // ------------------------------------------------------------------

        private ScenarioBuilderState.PartField editingTextField;
        private readonly TextFieldEditSession textFieldEditSession = new TextFieldEditSession();

        private void BeginTextFieldEdit(ScenarioBuilderState.PartField field)
        {
            editingTextField = field;
            string current = field.Data as string ?? "";
            TextFieldSpec spec = field.MultiLine
                ? TextFieldSpec.MultiLineUnrestricted("RimWorldAccess.TextInput.LabelDefault")
                : TextFieldSpec.Unrestricted("RimWorldAccess.TextInput.LabelDefault");
            textFieldEditSession.EnterEdit(current, spec, field.Name, ApplyTextFieldEdit, FinishTextFieldEdit);
        }

        private void ApplyTextFieldEdit(string value)
        {
            editingTextField?.SetValue?.Invoke(value);
            ScenarioBuilderState.SetDirty();
        }

        private void FinishTextFieldEdit()
        {
            editingTextField = null;
            RebuildRegionTreePreservingState();
            AnnounceCurrentItem();
        }

        private void AnnounceQuantityOrComboStateChange(string value, bool atMin, bool atMax)
        {
            var d = new ElementDescription { Value = value, AtMinimum = atMin, AtMaximum = atMax };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        // ------------------------------------------------------------------
        // Structural part actions: delete, reorder, add list item.
        // ------------------------------------------------------------------

        /// <summary>The parts tree's node for <paramref name="identity"/>, or null after it was removed.</summary>
        private InspectionTreeItem FindPartNode(ScenPart identity)
        {
            InspectionTreeItem root = partsPanel.Tree.Root;
            if (root == null) return null;
            foreach (InspectionTreeItem child in root.Children)
            {
                if (ReferenceEquals(child.Data, identity)) return child;
            }
            return null;
        }

        private void MoveCursorToPart(ScenPart identity)
        {
            InspectionTreeItem node = FindPartNode(identity);
            if (node != null && TryRevealAndSelect(node))
            {
                SyncRegionFromCurrentTree();
            }
        }

        private void ActivateDelete()
        {
            RefreshModel();
            if (!EditModeOn()) return;
            InspectionTreeItem item = CurrentTreeItem();
            Scenario scen = curScenField(page);
            if (item == null || scen == null) return;

            if (item is ListItemNode listItem)
            {
                ScenPartListItemManager.DeleteListItem(listItem.PartItem, listItem.ListItem.Index);
                RebuildRegionTreePreservingState();
                MoveCursorToPart(listItem.PartItem.Part);
                AnnounceCurrentItem();
                return;
            }
            if (item is AddItemNode)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.CannotDeleteAddAction".Loc());
                return;
            }
            if (!(item is PartNode partNode))
            {
                return;
            }

            ScenPart part = partNode.PartItem.Part;
            // Vanilla gate: the same PlayerAddRemovable check that shows/hides the
            // delete icon in Listing_ScenEdit.cs:22.
            if (!part.def.PlayerAddRemovable)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartCannotBeRemoved".Loc());
                return;
            }
            string label = partNode.PartItem.Label;
            // Vehicle: Scenario.RemovePart (Scenario.cs:405-412).
            scen.RemovePart(part);
            ScenarioBuilderState.SetDirty();
            RebuildRegionTreePreservingState();
            TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.RemovedLabel".Loc(label));
            AnnounceCurrentItem();
        }

        private void ActivateReorder(int direction)
        {
            RefreshModel();
            if (!(CurrentTreeItem() is PartNode partNode)) return;

            Scenario scen = curScenField(page);
            if (scen == null) return;
            ScenPart part = partNode.PartItem.Part;
            string partName = ScenarioBuilderState.StripTrailingPunctuation(part.Label);
            ReorderDirection reorderDirection = direction > 0 ? ReorderDirection.Down : ReorderDirection.Up;

            // Vehicle: Scenario.CanReorder + Scenario.Reorder (Scenario.cs:414-452).
            if (!scen.CanReorder(part, reorderDirection))
            {
                string key = direction > 0 ? "RimWorldAccess.ScenarioBuilder.CannotMoveDown" : "RimWorldAccess.ScenarioBuilder.CannotMoveUp";
                TolkHelper.Speak(key.Loc(partName));
                return;
            }
            scen.Reorder(part, reorderDirection);
            ScenarioBuilderState.SetDirty();
            RebuildRegionTreePreservingState();
            MoveCursorToPart(part);
            AnnounceMoveResult(part, partName, direction);
        }

        /// <summary>Reuses the pre-retrofit positional announcement phrasing verbatim (top/bottom/between).</summary>
        private void AnnounceMoveResult(ScenPart part, string partName, int direction)
        {
            List<ScenarioBuilderState.PartTreeItem> hierarchy = ScenarioBuilderState.PartsHierarchy;
            int newIndex = hierarchy.FindIndex(p => p.Part == part);
            if (newIndex < 0) { AnnounceCurrentItem(); return; }
            bool atTop = newIndex == 0;
            bool atBottom = newIndex == hierarchy.Count - 1;

            if (direction < 0) // moved up
            {
                if (atTop)
                {
                    TolkHelper.Speak(!atBottom && hierarchy.Count > 1
                        ? "RimWorldAccess.ScenarioBuilder.MovedToTopAbove".Loc(partName, ScenarioBuilderState.GetPartDisplayName(hierarchy[newIndex + 1]))
                        : "RimWorldAccess.ScenarioBuilder.MovedToTopOfList".Loc(partName));
                }
                else
                {
                    string aboveName = ScenarioBuilderState.GetPartDisplayName(hierarchy[newIndex - 1]);
                    TolkHelper.Speak(!atBottom
                        ? "RimWorldAccess.ScenarioBuilder.MovedUpBetween".Loc(partName, aboveName, ScenarioBuilderState.GetPartDisplayName(hierarchy[newIndex + 1]))
                        : "RimWorldAccess.ScenarioBuilder.MovedUpBelow".Loc(partName, aboveName));
                }
            }
            else // moved down
            {
                if (atBottom)
                {
                    TolkHelper.Speak(!atTop && hierarchy.Count > 1
                        ? "RimWorldAccess.ScenarioBuilder.MovedToBottomBelow".Loc(partName, ScenarioBuilderState.GetPartDisplayName(hierarchy[newIndex - 1]))
                        : "RimWorldAccess.ScenarioBuilder.MovedToBottomOfList".Loc(partName));
                }
                else
                {
                    string belowName = ScenarioBuilderState.GetPartDisplayName(hierarchy[newIndex + 1]);
                    TolkHelper.Speak(!atTop
                        ? "RimWorldAccess.ScenarioBuilder.MovedDownBetween".Loc(partName, ScenarioBuilderState.GetPartDisplayName(hierarchy[newIndex - 1]), belowName)
                        : "RimWorldAccess.ScenarioBuilder.MovedDownAbove".Loc(partName, belowName));
                }
            }
            AnnounceCurrentItem();
        }

        private void ActivateAddListItem(ScenarioBuilderState.PartTreeItem part)
        {
            ScenPart identity = part.Part;
            ScenPartListItemManager.AddListItem(part);
            RebuildRegionTreePreservingState();
            // Land back on the Add row: the part node's last child.
            InspectionTreeItem partNode = FindPartNode(identity);
            InspectionTreeItem addRow = partNode != null && partNode.Children.Count > 0
                ? partNode.Children[partNode.Children.Count - 1]
                : null;
            if (addRow != null && TryRevealAndSelect(addRow))
            {
                SyncRegionFromCurrentTree();
            }
            AnnounceCurrentItem();
        }

        private void ActivateReadFullField()
        {
            RefreshModel();
            if (CurrentTreeItem() is FieldNode field
                && (field.Field.Type == ScenarioBuilderState.FieldType.Text || field.Field.Type == ScenarioBuilderState.FieldType.ReadOnly)
                && field.Field.Data is string fullText)
            {
                TolkHelper.SpeakData(string.IsNullOrEmpty(fullText)
                    ? (string)"RimWorldAccess.ScenarioBuilder.FieldEmpty".Translate()
                    : fullText);
                return;
            }
            TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.NotATextField".Loc());
        }

        // ------------------------------------------------------------------
        // Buttons region.
        // ------------------------------------------------------------------

        /// <summary>Enter double-press proceed (S4c): Next is this page's default/proceed button.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "scenarioBuilder.next"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Back".Translate(), BackAction, SharedMenuGrammar.Cancel));
                actions.Add(new ScreenAction("Next".Translate(), NextAction, "scenarioBuilder.next"));
                actions.Add(new ScreenAction("Load".Translate(), ActivateLoad, "scenarioBuilder.load"));
                actions.Add(new ScreenAction("Save".Translate(), ActivateSave, "scenarioBuilder.save"));
                actions.Add(new ScreenAction("RandomizeSeed".Translate(), ActivateRandomizeSeed, "scenarioBuilder.randomizeSeed"));
                Scenario scen = curScenField(page);
                if (editModeField(page))
                {
                    actions.Add(new ScreenAction("AddPart".Translate(), ActivateAddPart, "scenarioBuilder.addPart"));
                    if (scen != null && SteamManager.Initialized
                        && (scen.Category == ScenarioCategory.CustomLocal || scen.Category == ScenarioCategory.SteamWorkshop))
                    {
                        actions.Add(new ScreenAction((string)Workshop.UploadButtonLabel(scen.GetPublishedFileId()), ActivateUpload));
                    }
                }
                return actions;
            }
        }

        private void EscapeBack()
        {
            ShellFrameStamps.MarkCancelConsumed();
            BackAction();
        }

        private void BackAction()
        {
            // Mirrors Page.DoBottomButtons' Back branch (Page.cs:60-63): gate, then DoBack.
            // ScenarioBuilderDoBackPatch's own prefix on Page.DoBack (unchanged this slice)
            // still shows the unsaved-changes Save/Discard/Cancel dialog when dirty.
            BackRequested = true;
            try
            {
                if ((bool)canDoBackMethod.Invoke(page, null))
                    doBackMethod.Invoke(page, null);
            }
            finally
            {
                BackRequested = false;
            }
        }

        private void NextAction()
        {
            // Mirrors the Next branch (Page.cs:69-71): gate (CanDoNext also runs
            // CheckAllPartsCompatible and, if edited/needing save, the
            // ScenarioChangedSavePrompt dialog), then DoNext.
            AdvanceRequested = true;
            try
            {
                if ((bool)canDoNextMethod.Invoke(page, null))
                    doNextMethod.Invoke(page, null);
            }
            finally
            {
                AdvanceRequested = false;
            }
        }

    }
}
