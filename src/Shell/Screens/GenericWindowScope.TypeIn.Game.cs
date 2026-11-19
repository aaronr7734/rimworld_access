using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    public sealed partial class GenericWindowScope
    {
        // ---------------------------------------------------------------
        // Slider type-in boxes (CapturedWidget.SliderTypeInOwner): a mod's number box for a
        // Slider or Range shows the same value as its owner, so it folds into the owner's row
        // (PresentationRow.TypeInCaptureIndex, per thumb on a Range) instead of standing as a
        // second stop, and typing a number on that row edits through the box. The box's owner
        // then parses, clamps and writes the value exactly as it does for mouse-typed input.
        // ---------------------------------------------------------------

        private bool editingTypeIn;

        /// <summary>Runs before Phase 1, which would otherwise hunt a caption for each box as a blank TextField.</summary>
        private void FoldSliderTypeIns(bool[] consumed, FusionResult[] fusion)
        {
            for (int i = 0; i < captureRows.Count; i++)
            {
                CapturedWidget row = captureRows[i];
                int owner = row.SliderTypeInOwner;
                if (row.Kind != WidgetKind.TextField || owner < 0 || owner >= i)
                {
                    continue;
                }
                WidgetKind ownerKind = captureRows[owner].Kind;
                if (ownerKind == WidgetKind.Range && row.SliderTypeInHigh)
                {
                    fusion[owner].TypeInHighIndex = i;
                }
                else if (ownerKind == WidgetKind.Range || ownerKind == WidgetKind.Slider)
                {
                    fusion[owner].TypeInIndex = i;
                }
                else
                {
                    continue;
                }
                consumed[i] = true;
            }
        }

        private bool HasTypeIn(PresentationRow row)
        {
            return row != null && row.TypeInCaptureIndex >= 0 && row.TypeInCaptureIndex < captureRows.Count;
        }

        private void BeginTypeInEdit(PresentationRow row, bool announcePrompt)
        {
            editingIndex = row.TypeInCaptureIndex;
            editingTypeIn = true;
            session.EnterEdit(
                captureRows[row.TypeInCaptureIndex].Text,
                TypeInEditSpec,
                row.Label,
                ApplyEditedText,
                ReAnnounceRow,
                announcePrompt);
        }

        /// <summary>
        /// A hover-only box appearing or vanishing above the edited one shifts its capture index
        /// between passes, so the override follows the focused row's own fold.
        /// </summary>
        private void RetargetTypeInEdit()
        {
            int focused = RowIndex;
            if (editingTypeIn && focused >= 0 && focused < presentationRows.Count
                && HasTypeIn(presentationRows[focused]))
            {
                editingIndex = presentationRows[focused].TypeInCaptureIndex;
            }
        }

        /// <summary>
        /// The box's owner has its own parse and clamp and may deliberately accept values past
        /// the slider's ends, so the only refusal is text that is no number at all.
        /// </summary>
        private static readonly TextFieldSpec TypeInEditSpec = new TextFieldSpec(
            labelKey: "RimWorldAccess.TextInput.LabelDefault",
            maxLength: null,
            minLength: 1,
            customValidator: text =>
            {
                float parsed;
                return float.TryParse(text ?? "", out parsed)
                    ? AcceptanceReport.WasAccepted
                    : new AcceptanceReport("RimWorldAccess.UI.GenericWindow.NumericMustBeNumber".Translate().ToString());
            });

        /// <summary>
        /// The box shows the control's raw value, which is what typing sets. Where the spoken label
        /// or value renders it differently (an option slider reading "100%" over index 3), the box's
        /// number is spoken too, so the player types what a sighted player would. A hover-only box
        /// hidden this pass shows the control's own value, so that is read instead.
        /// </summary>
        private void ApplyTypeInValue(ElementDescription d, PresentationRow row)
        {
            float box;
            if (!TryGetTypeInValue(row, out box) || HasNumber(d.Label, box) || HasNumber(d.Value, box))
            {
                return;
            }
            string spoken = box.ToString("0.###");
            d.Value = string.IsNullOrEmpty(d.Value)
                ? "RimWorldAccess.UI.GenericWindow.TypeInBoxValue".Translate(spoken).ToString()
                : "RimWorldAccess.UI.GenericWindow.ValueWithTypeInBox".Translate(d.Value, spoken).ToString();
        }

        private bool TryGetTypeInValue(PresentationRow row, out float value)
        {
            value = 0f;
            if (HasTypeIn(row))
            {
                return float.TryParse(captureRows[row.TypeInCaptureIndex].Text, out value);
            }
            CapturedWidget source = row?.Source;
            if (source == null || !source.TypeInBoxOffered)
            {
                return false;
            }
            if (row.Kind == WidgetKind.Range)
            {
                value = row.RangeHigh ? source.RangeHigh : source.RangeLow;
                return true;
            }
            if (row.Kind == WidgetKind.Slider)
            {
                value = source.SliderValue;
                return true;
            }
            return false;
        }

        /// <summary>Whether <paramref name="text"/> renders <paramref name="value"/> as one of its numbers.</summary>
        private static bool HasNumber(string text, float value)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            foreach (Match match in NumberToken.Matches(text))
            {
                float parsed;
                if (float.TryParse(match.Value, out parsed) && Mathf.Abs(parsed - value) < 0.0005f)
                {
                    return true;
                }
            }
            return false;
        }

        private static readonly Regex NumberToken = new Regex(@"-?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        /// <summary>Only a character that can open a number types into the box; letters stay typeahead.</summary>
        private static bool StartsNumber(char c)
        {
            return char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == ',';
        }
    }
}
