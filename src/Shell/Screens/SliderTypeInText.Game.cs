using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>Editing and speech rules shared by every scope that types into a slider's type-in box.</summary>
    internal static class SliderTypeInText
    {
        /// <summary>
        /// The box's owner has its own parse and clamp and may deliberately accept values past
        /// the slider's ends, so the only refusal is text that is no number at all.
        /// </summary>
        internal static readonly TextFieldSpec EditSpec = new TextFieldSpec(
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

        private static readonly Regex NumberToken = new Regex(@"-?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        /// <summary>Only a character that can open a number types into the box; letters stay typeahead.</summary>
        internal static bool StartsNumber(char c)
        {
            return char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == ',';
        }

        /// <summary>Whether <paramref name="text"/> renders <paramref name="value"/> as one of its numbers.</summary>
        internal static bool HasNumber(string text, float value)
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

        /// <summary>The value as the box itself shows it.</summary>
        internal static string Format(float value)
        {
            Func<float, string> format = SliderTypeInFocus.BoxValueFormat;
            return format != null ? format(value) : value.ToString("0.###", CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Adds the box's number to a slider's spoken value where the label or value renders the
        /// control differently (a volume reading "100%" over a raw 1), so the player types what a
        /// sighted player would see in the box.
        /// </summary>
        internal static void ApplyBoxValue(ElementDescription d, float box)
        {
            if (HasNumber(d.Label, box) || HasNumber(d.Value, box))
            {
                return;
            }
            string spoken = Format(box);
            d.Value = string.IsNullOrEmpty(d.Value)
                ? "RimWorldAccess.UI.GenericWindow.TypeInBoxValue".Translate(spoken).ToString()
                : "RimWorldAccess.UI.GenericWindow.ValueWithTypeInBox".Translate(d.Value, spoken).ToString();
        }
    }
}
