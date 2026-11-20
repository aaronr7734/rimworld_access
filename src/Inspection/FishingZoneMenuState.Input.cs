using System;
using System.Collections.Generic;
using Verse;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;

namespace RimWorldAccess
{
    public static partial class FishingZoneMenuState
    {
        #region Row Capabilities

        /// <summary>
        /// Whether Left/Right on this row adjusts a VALUE. False for the read-only rows, the
        /// repeat-mode combo (a dropdown, not a slider) and the fish-species branch and its
        /// children, where Left/Right belong to the tree's own expand/collapse grammar.
        /// </summary>
        internal static bool RowAdjustsValue(InspectionTreeItem item)
        {
            switch (RowOf(item)?.Kind)
            {
                case MenuItemType.RepeatCount:
                case MenuItemType.TargetCount:
                case MenuItemType.UnpauseAt:
                case MenuItemType.MinimumPopulation:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether Enter on this row opens exact numeric entry. The three whole-number counts
        /// only: the minimum-population row is a percentage slider vanilla itself gives no
        /// text field, so it keeps arrows alone.
        /// </summary>
        internal static bool RowTakesTypedNumber(InspectionTreeItem item)
        {
            switch (RowOf(item)?.Kind)
            {
                case MenuItemType.RepeatCount:
                case MenuItemType.TargetCount:
                case MenuItemType.UnpauseAt:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool TryGetSliderValue(InspectionTreeItem item, out float value)
        {
            value = 0f;
            if (RowOf(item)?.Kind != MenuItemType.MinimumPopulation || targetPopulationPctField == null || fishingZone == null)
                return false;
            value = (float)targetPopulationPctField.GetValue(fishingZone);
            return true;
        }

        /// <summary>The row's current value as the digits an exact-entry session starts from.</summary>
        internal static string TypedNumberSeed(InspectionTreeItem item)
        {
            switch (RowOf(item)?.Kind)
            {
                case MenuItemType.RepeatCount:
                    return repeatCountField != null ? ((int)repeatCountField.GetValue(fishingZone)).ToString() : "";
                case MenuItemType.TargetCount:
                    return targetCountField != null ? ((int)targetCountField.GetValue(fishingZone)).ToString() : "";
                case MenuItemType.UnpauseAt:
                    return unpauseAtCountField != null ? ((int)unpauseAtCountField.GetValue(fishingZone)).ToString() : "";
                default:
                    return "";
            }
        }

        #endregion

        #region Value Adjustment

        /// <summary>
        /// Left/Right (step 1) and the Shift/Ctrl/Shift+Ctrl Up/Down variants (10/100/1000)
        /// on a value row. Only ever called for a row <see cref="RowAdjustsValue"/> accepts.
        /// </summary>
        internal static void AdjustValue(InspectionTreeItem item, int direction, int multiplier = 1)
        {
            switch (RowOf(item)?.Kind)
            {
                case MenuItemType.RepeatCount:
                    AdjustRepeatCount(direction, multiplier);
                    break;

                case MenuItemType.TargetCount:
                    AdjustTargetCount(direction, multiplier);
                    break;

                case MenuItemType.UnpauseAt:
                    AdjustUnpauseAt(direction, multiplier);
                    break;

                case MenuItemType.MinimumPopulation:
                    AdjustMinPopulation(direction, multiplier);
                    break;
            }
        }

        /// <summary>
        /// The repeat-mode combo's picker (Enter/Space): a combo box is a dropdown, not a
        /// slider. The options are vanilla's own: every FishRepeatMode value in declaration
        /// order, exactly as ITab_Fishing.FillTab's own float menu lists them.
        /// </summary>
        private static void OpenRepeatModePicker()
        {
            if (repeatModeField == null || fishRepeatModeType == null)
                return;

            var options = new List<FloatMenuOption>();
            foreach (object mode in Enum.GetValues(fishRepeatModeType))
            {
                object captured = mode;
                options.Add(new FloatMenuOption(RepeatModeName(captured), delegate { ApplyRepeatMode(captured); }));
            }
            if (options.Count == 0)
                return;
            WindowlessFloatMenuState.Open(options, false, announceSelection: false);
        }

        private static void ApplyRepeatMode(object newMode)
        {
            // MUTATION-C: mirrors RimWorld.ITab_Fishing.FillTab's repeat-mode
            // FloatMenuOption delegate; inline delegate, no callable vanilla method.
            // Vanilla resets all four accompanying fields before assigning the new
            // mode, then rechecks the pause state.
            if (targetCountField != null) SetZoneField(targetCountField, 1);
            if (repeatCountField != null) SetZoneField(repeatCountField, 100);
            if (pauseWhenSatisfiedField != null) SetZoneField(pauseWhenSatisfiedField, false);
            if (unpauseAtCountField != null) SetZoneField(unpauseAtCountField, 50);
            SetZoneField(repeatModeField, newMode);
            RecheckPausedDueToResourceCount();

            RebuildRows(); // The mode's own count rows appear/disappear.
        }

        private static void AdjustRepeatCount(int direction, int multiplier)
        {
            if (repeatCountField == null)
                return;

            int step = direction * multiplier;
            int oldValue = (int)repeatCountField.GetValue(fishingZone);
            int newValue = Mathf.Max(RepeatCountMinimum, oldValue + step);

            if (newValue == oldValue)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }

            SetZoneField(repeatCountField, newValue);

            if (newValue == RepeatCountMinimum && direction < 0)
            {
                NumericStepperHelper.SpeakValueAtMinimum(RepeatCountMinimum.ToString());
            }
            else
            {
                TolkHelper.SpeakData(newValue.ToString());
            }
        }

        private static void AdjustTargetCount(int direction, int multiplier)
        {
            if (targetCountField == null)
                return;

            int step = direction * multiplier;
            int oldValue = (int)targetCountField.GetValue(fishingZone);
            int newValue = Mathf.Max(TargetCountMinimum, oldValue + step);

            if (newValue == oldValue)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }

            SetZoneField(targetCountField, newValue);

            ShiftUnpauseAtWithTargetCount(oldValue, newValue);

            // Mirrors ITab_Fishing.FillTab: targetCount changed, so recheck the pause state.
            RecheckPausedDueToResourceCount();

            if (newValue == TargetCountMinimum && direction < 0)
            {
                NumericStepperHelper.SpeakValueAtMinimum(TargetCountMinimum.ToString());
            }
            else
            {
                TolkHelper.SpeakData(newValue.ToString());
            }
        }

        private static void AdjustUnpauseAt(int direction, int multiplier)
        {
            if (unpauseAtCountField == null || targetCountField == null)
                return;

            int step = direction * multiplier;
            int oldValue = (int)unpauseAtCountField.GetValue(fishingZone);
            int maxValue = UnpauseAtMaximum();
            int newValue = Mathf.Clamp(oldValue + step, UnpauseAtMinimum, maxValue);

            if (newValue == oldValue)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }

            SetZoneField(unpauseAtCountField, newValue);

            if (newValue == UnpauseAtMinimum && direction < 0)
            {
                NumericStepperHelper.SpeakValueAtMinimum(UnpauseAtMinimum.ToString());
            }
            else if (newValue == maxValue && direction > 0)
            {
                NumericStepperHelper.SpeakValueAtMaximum(newValue.ToString());
            }
            else
            {
                TolkHelper.SpeakData(newValue.ToString());
            }
        }

        private static void AdjustMinPopulation(int direction, int multiplier)
        {
            if (targetPopulationPctField == null)
                return;

            // Step by 5% (0.05) per unit, 10% for shift
            float stepSize = multiplier >= 10 ? 0.10f : 0.05f;
            float step = direction * stepSize;
            float oldValue = (float)targetPopulationPctField.GetValue(fishingZone);

            int maxPop = MaxPopulation();
            float minPct = MinPopulationPct(maxPop);

            float newValue = Mathf.Clamp(oldValue + step, minPct, 1.0f);

            // Round to nearest 5%
            newValue = Mathf.Round(newValue * 20f) / 20f;
            newValue = Mathf.Clamp(newValue, minPct, 1.0f);

            if (Mathf.Approximately(newValue, oldValue))
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }

            SetZoneField(targetPopulationPctField, newValue);

            int pctInt = Mathf.RoundToInt(newValue * 100f);
            int fishAtThreshold = Mathf.RoundToInt(newValue * maxPop);
            string valueLabel = FormatMinPopulationValue(pctInt, fishAtThreshold);

            if (Mathf.Approximately(newValue, minPct) && direction < 0)
            {
                NumericStepperHelper.SpeakValueAtMinimum(valueLabel);
            }
            else if (Mathf.Approximately(newValue, 1.0f) && direction > 0)
            {
                NumericStepperHelper.SpeakValueAtMaximum(valueLabel);
            }
            else
            {
                TolkHelper.SpeakData(valueLabel);
            }
        }

        #endregion

        #region Value Bounds

        // The bounds every value row is held to, each harvested from vanilla's own fishing
        // tab and reused by the arrow steps, the Shift+Home/End jumps and exact numeric
        // entry alike, so no path can drift from another.

        /// <summary>MUTATION-C: mirrors ITab_Fishing.FillTab's repeatCount IntEntry floor.</summary>
        private const int RepeatCountMinimum = 1;

        /// <summary>MUTATION-C: mirrors ITab_Fishing.FillTab's targetCount IntEntry floor.</summary>
        private const int TargetCountMinimum = 1;

        /// <summary>MUTATION-C: mirrors ITab_Fishing.FillTab's unpauseAtCount IntEntry floor.</summary>
        private const int UnpauseAtMinimum = 0;

        /// <summary>
        /// MUTATION-C: mirrors ITab_Fishing.FillTab's unpause ceiling — the restart amount is
        /// always at least one fish below the target it restarts under.
        /// </summary>
        private static int UnpauseAtMaximum()
        {
            return (int)targetCountField.GetValue(fishingZone) - 1;
        }

        private static int MaxPopulation()
        {
            WaterBody waterBody = GetWaterBody();
            return waterBody != null ? Mathf.RoundToInt(waterBody.MaxPopulation) : 100;
        }

        /// <summary>
        /// Minimum mirrors ITab_Fishing.FillTab's own slider bound: 10 fish worth of
        /// population, with no additional floor.
        /// </summary>
        private static float MinPopulationPct(int maxPop)
        {
            return maxPop > 0 ? 10f / maxPop : 0.1f;
        }

        #endregion

        #region Min/Max Jumps

        /// <summary>Shift+Home on a value row: drop the focused field to its minimum.</summary>
        internal static void JumpToMin(InspectionTreeItem item)
        {
            switch (RowOf(item)?.Kind)
            {
                case MenuItemType.RepeatCount:
                    if (repeatCountField != null)
                    {
                        int current = (int)repeatCountField.GetValue(fishingZone);
                        if (current == RepeatCountMinimum)
                        {
                            MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                            return;
                        }
                        SetZoneField(repeatCountField, RepeatCountMinimum);
                        NumericStepperHelper.SpeakValueAtMinimum(RepeatCountMinimum.ToString());
                    }
                    break;

                case MenuItemType.TargetCount:
                    if (targetCountField != null)
                    {
                        int current = (int)targetCountField.GetValue(fishingZone);
                        if (current == TargetCountMinimum)
                        {
                            MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                            return;
                        }
                        SetZoneField(targetCountField, TargetCountMinimum);
                        ShiftUnpauseAtWithTargetCount(current, TargetCountMinimum);
                        // Mirrors ITab_Fishing.FillTab: targetCount changed, so recheck the pause state.
                        RecheckPausedDueToResourceCount();
                        NumericStepperHelper.SpeakValueAtMinimum(TargetCountMinimum.ToString());
                    }
                    break;

                case MenuItemType.UnpauseAt:
                    if (unpauseAtCountField != null)
                    {
                        int current = (int)unpauseAtCountField.GetValue(fishingZone);
                        if (current == UnpauseAtMinimum)
                        {
                            MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                            return;
                        }
                        SetZoneField(unpauseAtCountField, UnpauseAtMinimum);
                        NumericStepperHelper.SpeakValueAtMinimum(UnpauseAtMinimum.ToString());
                    }
                    break;

                case MenuItemType.MinimumPopulation:
                    if (targetPopulationPctField != null)
                    {
                        int maxPop = MaxPopulation();
                        float minPct = MinPopulationPct(maxPop);

                        float current = (float)targetPopulationPctField.GetValue(fishingZone);
                        if (Mathf.Approximately(current, minPct))
                        {
                            MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                            return;
                        }
                        SetZoneField(targetPopulationPctField, minPct);
                        NumericStepperHelper.SpeakValueAtMinimum(FormatMinPopulationValue(
                            Mathf.RoundToInt(minPct * 100f), Mathf.RoundToInt(minPct * maxPop)));
                    }
                    break;

                default:
                    TolkHelper.Speak("RimWorldAccess.Inspection.Fishing.Action.FieldNotAdjustable".Loc());
                    break;
            }
        }

        /// <summary>Shift+End on a value row: raise the focused field to its maximum.</summary>
        internal static void JumpToMax(InspectionTreeItem item)
        {
            switch (RowOf(item)?.Kind)
            {
                // Vanilla's IntEntry has no upper bound for either count.
                case MenuItemType.RepeatCount:
                case MenuItemType.TargetCount:
                    TolkHelper.Speak("RimWorldAccess.Inspection.Fishing.Action.NoMaximumLimit".Loc());
                    break;

                case MenuItemType.UnpauseAt:
                    if (unpauseAtCountField != null && targetCountField != null)
                    {
                        int maxValue = UnpauseAtMaximum();
                        int current = (int)unpauseAtCountField.GetValue(fishingZone);
                        if (current == maxValue)
                        {
                            MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                            return;
                        }
                        SetZoneField(unpauseAtCountField, maxValue);
                        NumericStepperHelper.SpeakValueAtMaximum(maxValue.ToString());
                    }
                    break;

                case MenuItemType.MinimumPopulation:
                    if (targetPopulationPctField != null)
                    {
                        float current = (float)targetPopulationPctField.GetValue(fishingZone);
                        if (Mathf.Approximately(current, 1.0f))
                        {
                            MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                            return;
                        }
                        SetZoneField(targetPopulationPctField, 1.0f);
                        int maxPop = MaxPopulation();
                        NumericStepperHelper.SpeakValueAtMaximum(FormatMinPopulationValue(100, maxPop));
                    }
                    break;

                default:
                    TolkHelper.Speak("RimWorldAccess.Inspection.Fishing.Action.FieldNotAdjustable".Loc());
                    break;
            }
        }

        #endregion

        #region Action Execution

        /// <summary>
        /// Enter on a row that is neither a tree branch nor an exact-entry field: the two
        /// checkbox toggles and the repeat-mode picker act, a fish row opens its info card,
        /// and anything else says which keys its own value answers to.
        /// </summary>
        internal static void ExecuteRow(InspectionTreeItem item)
        {
            switch (RowOf(item)?.Kind)
            {
                case MenuItemType.AllowToggle:
                    ToggleAllow();
                    break;

                case MenuItemType.PauseWhenSatisfied:
                    TogglePauseWhenSatisfied();
                    break;

                case MenuItemType.RepeatMode:
                    OpenRepeatModePicker();
                    break;

                case MenuItemType.FishSpeciesItem:
                    OpenFishInfoCard(item);
                    break;

                default:
                    TolkHelper.Speak("RimWorldAccess.Inspection.Fishing.Action.UseLeftRightToAdjust".Loc());
                    break;
            }
        }

        private static void ToggleAllow()
        {
            if (allowedField == null)
                return;

            bool current = allowedProp != null ? (bool)allowedProp.GetValue(fishingZone) : (bool)allowedField.GetValue(fishingZone);
            SetZoneField(allowedField, !current);
            // The re-announcement speaks the new checked/unchecked state via the row's own
            // Role.Checkbox — a separate "Fishing allowed"/"forbidden" phrase would repeat it.
            RebuildRows();
        }

        private static void TogglePauseWhenSatisfied()
        {
            if (pauseWhenSatisfiedField == null)
                return;

            bool current = (bool)pauseWhenSatisfiedField.GetValue(fishingZone);
            SetZoneField(pauseWhenSatisfiedField, !current);

            // Ensure unpause threshold is valid
            if (!current && unpauseAtCountField != null && targetCountField != null)
            {
                int unpauseAt = (int)unpauseAtCountField.GetValue(fishingZone);
                int targetCount = (int)targetCountField.GetValue(fishingZone);
                if (unpauseAt >= targetCount)
                {
                    SetZoneField(unpauseAtCountField, targetCount - 1);
                }
            }

            // Mirrors ITab_Fishing.FillTab: pauseWhenSatisfied changed, so recheck the pause state.
            RecheckPausedDueToResourceCount();

            // Same reasoning as ToggleAllow, plus the unpause row itself appears/disappears.
            RebuildRows();
        }

        /// <summary>
        /// Opens the info card for the focused fish species (Enter on a fish row, or Alt+I
        /// from anywhere in the menu).
        /// </summary>
        internal static void OpenFishInfoCard(InspectionTreeItem item)
        {
            FishRowData row = RowOf(item);
            if (row == null || row.Kind != MenuItemType.FishSpeciesItem || row.Fish == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Fishing.Action.SelectFishSpeciesFirst".Loc());
                return;
            }

            Find.WindowStack.Add(new Dialog_InfoCard(row.Fish));
            TolkHelper.Speak("RimWorldAccess.Inspection.Fishing.Action.OpeningInfoCard".Loc(row.Fish.LabelCap));
        }

        #endregion

        #region Exact Numeric Entry

        /// <summary>
        /// Writes a typed whole number into the focused count, clamped by the same bounds the
        /// arrow steps honor. Runs once, on the text session's Enter-confirm, and speaks
        /// nothing itself: the session re-announces the row on exit, which now carries the
        /// value (see <see cref="TextFieldEditSession"/>'s "exit re-announces the row" rule).
        /// </summary>
        internal static void ApplyTypedNumber(InspectionTreeItem item, int value)
        {
            switch (RowOf(item)?.Kind)
            {
                case MenuItemType.RepeatCount:
                    if (repeatCountField != null)
                    {
                        SetZoneField(repeatCountField, Mathf.Max(RepeatCountMinimum, value));
                    }
                    break;

                case MenuItemType.TargetCount:
                    if (targetCountField != null)
                    {
                        int oldValue = (int)targetCountField.GetValue(fishingZone);
                        SetZoneField(targetCountField, Mathf.Max(TargetCountMinimum, value));
                        ShiftUnpauseAtWithTargetCount(oldValue, (int)targetCountField.GetValue(fishingZone));
                        // Mirrors ITab_Fishing.FillTab: targetCount changed, so recheck the pause state.
                        RecheckPausedDueToResourceCount();
                    }
                    break;

                case MenuItemType.UnpauseAt:
                    if (unpauseAtCountField != null && targetCountField != null)
                    {
                        SetZoneField(unpauseAtCountField,
                            Mathf.Clamp(value, UnpauseAtMinimum, UnpauseAtMaximum()));
                    }
                    break;
            }
        }

        #endregion
    }
}
