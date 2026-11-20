using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    public sealed partial class GenericWindowScope
    {
        // ---------------------------------------------------------------
        // Text edit (browse -> modal edit), via the shared session — see
        // WidgetCapture.RequestTextOverride's remarks for the injection design.
        // ---------------------------------------------------------------

        private void BeginEdit(PresentationRow row, bool announcePrompt)
        {
            editingIndex = row.ActivateCaptureIndex;
            editingTypeIn = false;
            session.EnterEdit(
                row.Source.Text,
                BuildEditSpec(row.Source),
                row.Label,
                ApplyEditedText,
                ReAnnounceRow,
                announcePrompt);
        }

        /// <summary>
        /// An enriched numeric row (TextFieldNumeric family — see
        /// CapturedWidget.NumericField) gets a spec whose CustomValidator
        /// enforces vanilla's own commit gate with a voice; everything else
        /// keeps the permissive spec. The applied edit itself still rides
        /// RequestTextOverride — vanilla's own re-parse/clamp
        /// (Widgets.ResolveParseNow) consumes whatever arrives, so nothing
        /// here ever writes the value directly.
        /// </summary>
        private static TextFieldSpec BuildEditSpec(CapturedWidget source)
        {
            if (source.NumericField)
            {
                bool isInt = source.NumericIsInt;
                float min = source.NumericMin;
                float max = source.NumericMax;
                return new TextFieldSpec(
                    labelKey: "RimWorldAccess.TextInput.LabelDefault",
                    maxLength: null,
                    minLength: 1,
                    customValidator: text => ValidateNumericText(text, isInt, min, max));
            }
            return source.MultiLine
                ? TextFieldSpec.MultiLineUnrestricted("RimWorldAccess.TextInput.LabelDefault")
                : TextFieldSpec.Unrestricted("RimWorldAccess.TextInput.LabelDefault");
        }

        /// <summary>
        /// Numeric commit gate, mirroring vanilla's own acceptance chain
        /// (decompiled Verse/Widgets.cs: IsPartiallyOrFullyTypedNumber :1940,
        /// IsFullyTypedNumber :1973, ResolveParseNow :1880) — never stricter,
        /// never looser. Required because vanilla handles a refused commit
        /// SILENTLY: a negative typed into a min>=0 field is dropped without a
        /// trace, and an out-of-range value is clamped with no announcement —
        /// both read as "nothing happened" to a screen reader user, so the
        /// refusal speaks here instead. Bounds are the CAPTURED call's own
        /// min/max (harvested, never literals); a percent field's bounds and
        /// text already live in the percent domain the player types in.
        /// </summary>
        private static AcceptanceReport ValidateNumericText(string text, bool isInt, float min, float max)
        {
            text = text ?? "";
            float parsed = 0f;
            bool numeric = VanillaAcceptsNumberShape(text, isInt, min);
            if (numeric)
            {
                if (isInt)
                {
                    int intParsed;
                    numeric = int.TryParse(text, out intParsed);
                    parsed = intParsed;
                }
                else
                {
                    numeric = float.TryParse(text, out parsed);
                }
            }
            if (!numeric)
            {
                return new AcceptanceReport(
                    "RimWorldAccess.UI.GenericWindow.NumericMustBeNumber".Translate().ToString());
            }
            if (parsed < min || parsed > max)
            {
                return new AcceptanceReport(
                    "RimWorldAccess.UI.GenericWindow.NumericOutOfRange".Translate(
                        FormatNumericBound(min, isInt), FormatNumericBound(max, isInt)).ToString());
            }
            return AcceptanceReport.WasAccepted;
        }

        /// <summary>
        /// The character/shape gates vanilla applies before it will buffer or
        /// commit a numeric string, mirrored check-for-check from decompiled
        /// Widgets.IsPartiallyOrFullyTypedNumber (:1940) and
        /// IsFullyTypedNumber (:1973). A string failing any of these never
        /// reaches vanilla's parse at all.
        /// </summary>
        private static bool VanillaAcceptsNumberShape(string s, bool isInt, float min)
        {
            if (s.Length == 0)
            {
                return false; // spec's minLength refuses first; defensive here.
            }
            if (s[0] == '-' && min >= 0f)
            {
                return false;
            }
            if (s.Length > 1 && s[s.Length - 1] == '-')
            {
                return false;
            }
            if (s == "00")
            {
                return false;
            }
            if (s.Length > 12)
            {
                return false;
            }
            if (isInt)
            {
                return ContainsOnlyChars(s, "-0123456789");
            }
            string[] parts = s.Split('.');
            if (parts.Length > 2)
            {
                return false;
            }
            if (!ContainsOnlyChars(parts[0], "-0123456789"))
            {
                return false;
            }
            if (parts.Length == 2 && (parts[1].Length == 0 || !ContainsOnlyChars(parts[1], "0123456789")))
            {
                return false;
            }
            return true;
        }

        private static bool ContainsOnlyChars(string s, string allowed)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (allowed.IndexOf(s[i]) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// A harvested bound rendered for speech: whole for int fields
        /// (vanilla rounds after its clamp), plain decimal for float fields —
        /// never scientific notation (the vanilla default max is 1E+09).
        /// </summary>
        private static string FormatNumericBound(float bound, bool isInt)
        {
            return isInt ? Mathf.RoundToInt(bound).ToString() : bound.ToString("0.####");
        }

        /// <summary>
        /// Mod-agnostic credential-field detector for the sensitive-value mask
        /// above: a captured TextField's fused caption containing one of these
        /// substrings (case-insensitive) is treated as holding a secret the
        /// player pasted in, never something to read back verbatim. Deliberately
        /// caption-based, not window-keyed -- any settings page's own "API Key"/
        /// "Access Token" field gets the same treatment with no per-mod list to
        /// maintain, matching the generic-first mod-support doctrine.
        /// </summary>
        private static readonly string[] SensitiveFieldLabelSubstrings =
        {
            "api key", "apikey", "access token", "api secret", "secret key",
        };

        private static bool IsSensitiveFieldLabel(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return false;
            }
            for (int i = 0; i < SensitiveFieldLabelSubstrings.Length; i++)
            {
                if (label.IndexOf(SensitiveFieldLabelSubstrings[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void ApplyEditedText(string value)
        {
            WidgetCapture.RequestTextOverride(editingIndex, value);
        }

        private void ReAnnounceRow()
        {
            WidgetCapture.ClearTextOverride();
            editingIndex = -1;
            editingTypeIn = false;
            pendingAnnounce = true;
            editExitAnnounceQueued = true;
        }

        /// <summary>
        /// Typing on a field row opens its edit session and feeds the character
        /// straight in — sighted players click the box and type — before the
        /// chassis typeahead sees it. Everything else is the shared engine's.
        /// </summary>
        public override bool HandleChar(char c)
        {
            if (!ScreenActive || session.Editing)
            {
                return false;
            }
            // A stepper anchored on a real field — IntEntry's center field, or a hand-rolled
            // spinner's own field — types like the field itself.
            int focused = RowIndex;
            PresentationRow row = focused >= 0 && focused < presentationRows.Count ? presentationRows[focused] : null;
            bool typableField = row != null
                && (row.Kind == WidgetKind.TextField
                    || (row.Kind == WidgetKind.Stepper
                        && row.Source != null
                        && (row.Source.StepperValueField || row.Source.Kind == WidgetKind.TextField)));
            if (typableField && !char.IsControl(c) && !char.IsWhiteSpace(c))
            {
                BeginEdit(row, announcePrompt: false);
                session.FeedChar(c);
                return true;
            }
            if (HasTypeIn(row) && SliderTypeInText.StartsNumber(c))
            {
                BeginTypeInEdit(row, announcePrompt: false);
                session.FeedChar(c);
                return true;
            }
            return base.HandleChar(c);
        }

    }
}
