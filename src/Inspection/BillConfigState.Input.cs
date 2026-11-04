using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    public static partial class BillConfigState
    {
        #region The Row Surface (what BillConfigScope asks about the row under its cursor)

        /// <summary>How many fields are currently shown; the scope's content-region item count.</summary>
        internal static int RowCount => visibleFields.Count;

        /// <summary>
        /// The focused field as a composed element description. The field's VALUE stays inside
        /// the label, since every value-carrying label here is a whole-phrase key that
        /// interpolates it; filling the Value datum too would speak it twice. Only the
        /// CheckboxLabeled rows split, their suffix being what the Check datum says. Position is
        /// left unset for the scope to fill.
        /// </summary>
        internal static Shell.ElementDescription DescribeRow(int index)
        {
            FieldDescriptor field = RowAt(index);
            if (field == null)
            {
                return new Shell.ElementDescription();
            }
            return new Shell.ElementDescription
            {
                Label = field.GetLabel(),
                Role = field.Role,
                Check = field.GetCheck != null ? field.GetCheck() : null,
                ReadOnly = !field.IsEditable,
                // Enter on a count row opens its own text field rather than the proceed button.
                EntersEditOnAccept = field.TakesTypedNumber,
                Extras = field.GetDescription != null ? field.GetDescription() : null,
            };
        }

        /// <summary>
        /// What typeahead matches: the field name alone, never its value, so a query cannot be
        /// caught by whatever digits a row's number happens to contain.
        /// </summary>
        internal static string RowSearchText(int index)
        {
            FieldDescriptor field = RowAt(index);
            return field == null ? "" : field.GetSearchLabel();
        }

        /// <summary>Whether Left/Right steps this row's value (as opposed to opening a submenu on Enter).</summary>
        internal static bool RowAdjustsValue(int index)
        {
            FieldDescriptor field = RowAt(index);
            return field != null && field.IsEditable && field.Adjust != null;
        }

        /// <summary>Whether Enter on this row opens exact numeric entry.</summary>
        internal static bool RowTakesTypedNumber(int index)
        {
            FieldDescriptor field = RowAt(index);
            return field != null && field.TakesTypedNumber;
        }

        /// <summary>The row's current value as the digits an exact-entry session starts from.</summary>
        internal static string TypedNumberSeed(int index)
        {
            FieldDescriptor field = RowAt(index);
            return field != null && field.GetNumericText != null ? field.GetNumericText() : "";
        }

        /// <summary>
        /// Writes a typed whole number through the field's own commit method, which carries that
        /// field's vanilla-harvested clamp. Runs once, on the text session's Enter-confirm.
        /// </summary>
        internal static void ApplyTypedNumber(int index, int value)
        {
            FieldDescriptor field = RowAt(index);
            if (field != null && field.ApplyNumeric != null)
            {
                field.ApplyNumeric(value);
                ResetDialogEditBuffers();
            }
        }

        /// <summary>
        /// Left/Right (step 1) and the Shift/Ctrl/Shift+Ctrl Up/Down variants (10/100/1000).
        /// Read-only rows and submenu-backed rows each answer with their own refusal.
        /// </summary>
        internal static void AdjustValue(int index, int direction, int multiplier = 1)
        {
            FieldDescriptor field = RowAt(index);
            if (field == null)
            {
                return;
            }
            if (!field.IsEditable)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.NotAdjustable".Loc(), SpeechPriority.High);
                return;
            }
            if (field.Adjust == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.UseEnterToOpenSubmenu".Loc());
                return;
            }

            field.Adjust(direction, multiplier);
            ResetDialogEditBuffers();
        }

        /// <summary>Enter on a row that does not take a typed number: run its action, or say which keys do adjust it.</summary>
        internal static void ExecuteRow(int index)
        {
            FieldDescriptor field = RowAt(index);
            if (field == null)
            {
                return;
            }
            if (field.Execute == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.UseLeftRightToAdjust".Loc());
                return;
            }

            field.Execute();
        }

        /// <summary>Shift+Home / Shift+End: drop or raise the focused field's value to its bound.</summary>
        internal static void JumpToBound(int index, bool toMinimum)
        {
            FieldDescriptor field = RowAt(index);
            Action jump = field == null ? null : (toMinimum ? field.JumpMin : field.JumpMax);
            if (jump == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.FieldNotAdjustable".Loc());
                return;
            }

            jump();
            ResetDialogEditBuffers();
        }

        private static readonly string[] DialogEditBufferNames =
        {
            "repeatCountEditBuffer", "targetCountEditBuffer", "unpauseCountEditBuffer",
        };

        private static readonly AccessTools.FieldRef<Dialog_BillConfig, string>[] DialogEditBuffers =
            DialogEditBufferNames.Select(name => AccessTools.FieldRefAccess<Dialog_BillConfig, string>(name)).ToArray();

        // Clears the dialog's stale count text; TextFieldNumeric reseeds a null buffer from the live value.
        private static void ResetDialogEditBuffers()
        {
            if (openedDialog is Dialog_BillConfig dialog)
            {
                foreach (AccessTools.FieldRef<Dialog_BillConfig, string> buffer in DialogEditBuffers)
                {
                    buffer(dialog) = null;
                }
            }
        }

        private static FieldDescriptor RowAt(int index)
        {
            return index >= 0 && index < visibleFields.Count ? visibleFields[index] : null;
        }

        #endregion

        #region Execute Actions

        private static void ExecuteSuspendToggle()
        {
            bill.suspended = !bill.suspended;
            RowsChanged();
        }

        private static void ExecuteUnpauseBill()
        {
            bill.paused = false;
            RowsChanged();
        }

        private static void ExecutePauseWhenSatisfiedToggle()
        {
            bill.pauseWhenSatisfied = !bill.pauseWhenSatisfied;
            // MUTATION-C: mirrors Dialog_BillConfig.cs:207-209's unpause>=target
            // ceiling clamp (no gated setter exists); applied at the checkbox's
            // true-transition since our menu isn't redrawn every frame like
            // vanilla's listing.
            if (bill.pauseWhenSatisfied && bill.unpauseWhenYouHave >= bill.targetCount)
            {
                bill.unpauseWhenYouHave = bill.targetCount - 1;
            }
            RowsChanged();
        }

        private static void ExecuteIncludeEquippedToggle()
        {
            bill.includeEquipped = !bill.includeEquipped;
            RowsChanged();
        }

        private static void ExecuteIncludeTaintedToggle()
        {
            bill.includeTainted = !bill.includeTainted;
            RowsChanged();
        }

        private static void ExecuteLimitToAllowedStuffToggle()
        {
            bill.limitToAllowedStuff = !bill.limitToAllowedStuff;
            RowsChanged();
        }

        #endregion

        #region Value Adjustment Methods

        private static void AdjustRepeatCount(int direction, int multiplier = 1)
        {
            int step = direction * multiplier;
            int oldValue = bill.repeatCount;
            bill.repeatCount = Mathf.Max(1, bill.repeatCount + step);

            if (bill.repeatCount == oldValue)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }
            if (bill.repeatCount == 1 && direction < 0)
            {
                NumericStepperHelper.SpeakValueAtMinimum("1");
            }
            else
            {
                SpeakValueChange(bill.repeatCount.ToString());
            }
        }

        // MUTATION-C: mirrors Dialog_BillConfig.DoWindowContents' targetCount
        // IntEntry followed by its unconditional unpauseWhenYouHave delta-shift
        // (Bill_Production.targetCount/unpauseWhenYouHave are bare public
        // fields vanilla writes directly from the ITab; no gated setter
        // exists). Every site that changes bill.targetCount routes the old
        // value through here so the target/unpause gap is preserved exactly
        // like vanilla, instead of only clamping down when it's exceeded.
        private static void ApplyTargetCountDelta(int oldTargetCount)
        {
            bill.unpauseWhenYouHave = Mathf.Max(0, bill.unpauseWhenYouHave + (bill.targetCount - oldTargetCount));

            // MUTATION-C: mirrors Dialog_BillConfig.cs:207-209's unpause>=target
            // ceiling clamp (no gated setter exists on either field). Vanilla
            // re-applies this every redraw regardless of how the violation
            // arose; folding it into this single choke point (every
            // targetCount-changing call site already routes through here)
            // keeps all of them in sync instead of only clamping down when a
            // caller remembered to re-check it itself.
            if (bill.pauseWhenSatisfied && bill.unpauseWhenYouHave >= bill.targetCount)
            {
                bill.unpauseWhenYouHave = bill.targetCount - 1;
            }
        }

        private static void AdjustTargetCount(int direction, int multiplier = 1)
        {
            // Vanilla's IntEntry steps by the recipe's targetCountAdjustment, not a flat 1
            // (Dialog_BillConfig.cs:150).
            int step = direction * multiplier * bill.recipe.targetCountAdjustment;
            int oldValue = bill.targetCount;
            // MUTATION-C: floor of 0, not 1 — Listing_Standard.IntEntry's
            // own inline clamp defaults to min=0 (no min arg is passed at
            // Dialog_BillConfig.cs:150), and the ITab card's own +/- stepper
            // (Bill_Production.DoConfigInterface) floors the same way
            // (Mathf.Max(0, targetCount - num2)). No vanilla path floors
            // targetCount at 1.
            bill.targetCount = Mathf.Max(0, bill.targetCount + step);
            ApplyTargetCountDelta(oldValue);

            if (bill.targetCount == oldValue)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }
            if (bill.targetCount == 0 && direction < 0)
            {
                NumericStepperHelper.SpeakValueAtMinimum("0");
            }
            else if (bill.targetCount >= 999999)
            {
                // 999999 is only the "Infinite" display threshold; vanilla's IntEntry has no cap.
                TolkHelper.Speak("Infinite".Loc());
            }
            else
            {
                SpeakValueChange(bill.targetCount.ToString());
            }
        }

        private static void AdjustUnpauseAt(int direction, int multiplier = 1)
        {
            // Vanilla steps unpauseWhenYouHave by targetCountAdjustment too
            // (Dialog_BillConfig.cs:206).
            int step = direction * multiplier * bill.recipe.targetCountAdjustment;
            int oldValue = bill.unpauseWhenYouHave;
            int maxValue = bill.targetCount - 1;
            // MUTATION-C: mirrors Dialog_BillConfig.cs:206's IntEntry(min=0 default)
            // combined with its own :207-209 targetCount-1 ceiling; no gated setter
            // exists.
            bill.unpauseWhenYouHave = Mathf.Clamp(bill.unpauseWhenYouHave + step, 0, maxValue);

            if (bill.unpauseWhenYouHave == oldValue)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }
            if (bill.unpauseWhenYouHave == 0 && direction < 0)
            {
                NumericStepperHelper.SpeakValueAtMinimum("0");
            }
            else if (bill.unpauseWhenYouHave == maxValue && direction > 0)
            {
                NumericStepperHelper.SpeakValueAtMaximum(bill.unpauseWhenYouHave.ToString());
            }
            else
            {
                SpeakValueChange(bill.unpauseWhenYouHave.ToString());
            }
        }

        private static void AdjustSkillRangeMin(int direction)
        {
            int oldMin = bill.allowedSkillRange.min;
            int newMin = Mathf.Clamp(oldMin + direction, 0, bill.allowedSkillRange.max);
            if (newMin == oldMin)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }
            bill.allowedSkillRange = new IntRange(newMin, bill.allowedSkillRange.max);
            SpeakValueChange(newMin.ToString());
        }

        private static void AdjustSkillRangeMax(int direction)
        {
            int oldMax = bill.allowedSkillRange.max;
            int newMax = Mathf.Clamp(oldMax + direction, bill.allowedSkillRange.min, SkillRecord.MaxLevel);
            if (newMax == oldMax)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }
            bill.allowedSkillRange = new IntRange(bill.allowedSkillRange.min, newMax);
            SpeakValueChange(newMax.ToString());
        }

        #region Jump-to-Min/Max Actions

        // Each method early-returns at its own edge so a jump with nowhere to go says so.

        private static void JumpRepeatCountToMin()
        {
            if (bill.repeatCount == 1)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                return;
            }
            bill.repeatCount = 1;
            NumericStepperHelper.SpeakValueAtMinimum("1");
        }

        private static void JumpRepeatCountToMax()
        {
            TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.NoMaximumLimit".Loc());
        }

        private static void JumpTargetCountToMin()
        {
            // Floor is 0, not 1 — see AdjustTargetCount's MUTATION-C marker.
            if (bill.targetCount == 0)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                return;
            }
            int oldTarget = bill.targetCount;
            // MUTATION-C: floor 0 per Listing_Standard.IntEntry's default min
            // (Dialog_BillConfig.cs:150).
            bill.targetCount = 0;
            ApplyTargetCountDelta(oldTarget);
            NumericStepperHelper.SpeakValueAtMinimum("0");
        }

        private static void JumpTargetCountToMax()
        {
            if (bill.targetCount >= 999999)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                return;
            }
            // MUTATION-C: 999999 is vanilla's own "Infinite" display threshold
            // (Dialog_BillConfig.cs:142), not a real cap — used here
            // as this jump's landing value, same as typing it in.
            int oldTarget = bill.targetCount;
            bill.targetCount = 999999;
            ApplyTargetCountDelta(oldTarget);
            NumericStepperHelper.SpeakValueAtMaximum("Infinite".Translate());
        }

        private static void JumpUnpauseAtToMin()
        {
            if (bill.unpauseWhenYouHave == 0)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                return;
            }
            // MUTATION-C: mirrors Dialog_BillConfig.cs:206's IntEntry(min=0
            // default) combined with its own :207-209 targetCount-1 ceiling;
            // no gated setter exists.
            bill.unpauseWhenYouHave = 0;
            NumericStepperHelper.SpeakValueAtMinimum("0");
        }

        private static void JumpUnpauseAtToMax()
        {
            int maxValue = bill.targetCount - 1;
            if (bill.unpauseWhenYouHave == maxValue)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                return;
            }
            // MUTATION-C: mirrors Dialog_BillConfig.cs:206's IntEntry(min=0
            // default) combined with its own :207-209 targetCount-1 ceiling;
            // no gated setter exists.
            bill.unpauseWhenYouHave = maxValue;
            NumericStepperHelper.SpeakValueAtMaximum(maxValue.ToString());
        }

        private static void JumpIngredientRadiusToMin()
        {
            if (bill.ingredientSearchRadius <= 3f)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                return;
            }
            bill.ingredientSearchRadius = 3f;
            NumericStepperHelper.SpeakValueAtMinimum("3");
        }

        private static void JumpIngredientRadiusToMax()
        {
            if (bill.ingredientSearchRadius >= 999f)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                return;
            }
            bill.ingredientSearchRadius = 999f;
            NumericStepperHelper.SpeakValueAtMaximum("Unlimited".Translate());
        }

        private static void JumpSkillRangeMinToMin()
        {
            if (bill.allowedSkillRange.min == 0)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                return;
            }
            bill.allowedSkillRange = new IntRange(0, bill.allowedSkillRange.max);
            NumericStepperHelper.SpeakValueAtMinimum("0");
        }

        private static void JumpSkillRangeMinToMax()
        {
            if (bill.allowedSkillRange.min == bill.allowedSkillRange.max)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                return;
            }
            bill.allowedSkillRange = new IntRange(bill.allowedSkillRange.max, bill.allowedSkillRange.max);
            NumericStepperHelper.SpeakValueAtMaximum(bill.allowedSkillRange.max.ToString());
        }

        private static void JumpSkillRangeMaxToMin()
        {
            if (bill.allowedSkillRange.max == bill.allowedSkillRange.min)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                return;
            }
            bill.allowedSkillRange = new IntRange(bill.allowedSkillRange.min, bill.allowedSkillRange.min);
            NumericStepperHelper.SpeakValueAtMinimum(bill.allowedSkillRange.min.ToString());
        }

        private static void JumpSkillRangeMaxToMax()
        {
            if (bill.allowedSkillRange.max == SkillRecord.MaxLevel)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                return;
            }
            bill.allowedSkillRange = new IntRange(bill.allowedSkillRange.min, SkillRecord.MaxLevel);
            NumericStepperHelper.SpeakValueAtMaximum(SkillRecord.MaxLevel.ToString());
        }

        #endregion

        #endregion

        #region Apply-Numeric-Value Actions

        // Exact-entry commits clamp as that field's IntEntry does; the session's exit re-announces.

        private static void ApplyRepeatCountValue(int value)
        {
            bill.repeatCount = Mathf.Max(1, value);
        }

        private static void ApplyTargetCountValue(int value)
        {
            int oldTarget = bill.targetCount;
            // Floor 0 and no upper bound — see AdjustTargetCount's MUTATION-C marker. A value
            // above the 999999 "Infinite" threshold is kept as typed, like vanilla's text field.
            bill.targetCount = Mathf.Max(0, value);
            ApplyTargetCountDelta(oldTarget);
        }

        private static void ApplyUnpauseAtValue(int value)
        {
            // MUTATION-C: mirrors Dialog_BillConfig.cs:206's IntEntry(min=0
            // default) combined with its own :207-209 targetCount-1 ceiling;
            // no gated setter exists.
            bill.unpauseWhenYouHave = Mathf.Clamp(value, 0, bill.targetCount - 1);
        }

        private static void ApplyIngredientRadiusValue(int value)
        {
            // Valid range is 3-100, and anything over 100 is vanilla's unlimited (999).
            bill.ingredientSearchRadius = value > 100 ? 999f : Mathf.Clamp(value, 3, 100);
        }

        private static void ApplySkillRangeMinValue(int value)
        {
            bill.allowedSkillRange = new IntRange(
                Mathf.Clamp(value, 0, bill.allowedSkillRange.max), bill.allowedSkillRange.max);
        }

        private static void ApplySkillRangeMaxValue(int value)
        {
            bill.allowedSkillRange = new IntRange(bill.allowedSkillRange.min,
                Mathf.Clamp(value, bill.allowedSkillRange.min, SkillRecord.MaxLevel));
        }

        #endregion

        #region Text Input Methods (Bill Rename)

        // The controller registers with TextInputManager, so the dispatcher's text-session
        // branch handles every key.
        private static void StartTextInput()
        {
            // The length cap and NameIsValid come from the dialog this state opens; its
            // constructors only assign fields, so this throwaway never reaches the WindowStack.
            var spec = TextFieldSpec.ForRimWorldDialog(new Dialog_RenameBill(bill));
            billRenameController.Begin(bill.RenamableLabel, spec, OnBillRenameConfirm, OnBillRenameCancel, replaceOnType: true);
        }

        private static void OnBillRenameConfirm(string newName)
        {
            bill.RenamableLabel = newName;
            BillsMenuState.RefreshMenuItems();
            TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.RenamedTo".Loc(newName));
            RowsChanged();
        }

        private static void OnBillRenameCancel()
        {
            TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.RenameCancelled".Loc());
        }

        #endregion

        #region Range Edit Integration

        /// <summary>
        /// The range sub-editor closed. Every step was already written into the bill, so this
        /// only re-reads the row labels.
        /// </summary>
        private static void RangeEditClosed()
        {
            RowsChanged();
        }

        #endregion

        private static void AdjustIngredientRadius(int direction, int multiplier = 1)
        {
            float oldValue = bill.ingredientSearchRadius;

            if (bill.ingredientSearchRadius >= 999f)
            {
                if (direction < 0)
                {
                    bill.ingredientSearchRadius = 100f;
                    SpeakValueChange("100");
                }
                else
                {
                    MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Maximum);
                }
                return;
            }

            // The radius range is only 3-100, so the shared multipliers are remapped.
            float step;
            if (multiplier >= 1000)
            {
                if (direction > 0)
                {
                    if (bill.ingredientSearchRadius >= 100f)
                    {
                        bill.ingredientSearchRadius = 999f;
                        NumericStepperHelper.SpeakValueAtMaximum("Unlimited".Translate());
                    }
                    else
                    {
                        bill.ingredientSearchRadius = 100f;
                        SpeakValueChange("100");
                    }
                }
                else
                {
                    bill.ingredientSearchRadius = 3f;
                    NumericStepperHelper.SpeakValueAtMinimum("3");
                }
                return;
            }
            else if (multiplier >= 100)
            {
                step = direction * 25f;
            }
            else
            {
                step = direction * multiplier;
            }

            bill.ingredientSearchRadius = Mathf.Clamp(bill.ingredientSearchRadius + step, 3f, 100f);

            if (bill.ingredientSearchRadius >= 100f && direction > 0 && oldValue >= 100f)
            {
                bill.ingredientSearchRadius = 999f;
                NumericStepperHelper.SpeakValueAtMaximum("Unlimited".Translate());
                return;
            }

            if (bill.ingredientSearchRadius == oldValue)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }

            if (bill.ingredientSearchRadius == 3f && direction < 0)
            {
                NumericStepperHelper.SpeakValueAtMinimum("3");
            }
            else if (bill.ingredientSearchRadius >= 100f)
            {
                SpeakValueChange("100");
            }
            else
            {
                SpeakValueChange($"{bill.ingredientSearchRadius:F0}");
            }
        }
    }
}
