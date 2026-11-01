using System;
using System.Text.RegularExpressions;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Immutable description of a single editable text field. Constraints are checked by
    /// <see cref="TextFieldValidator"/> on every paste attempt and on commit.
    /// </summary>
    public sealed class TextFieldSpec
    {
        public string LabelKey { get; }
        public int? MaxLength { get; }
        public int? MinLength { get; }
        public Regex AllowedChars { get; }
        public bool ForbidGrammarSpecials { get; }
        public bool MustBeFilename { get; }
        public bool MultiLine { get; }
        public Func<string, AcceptanceReport> CustomValidator { get; }

        /// <summary>
        /// The buffer is the game's to write, not the player's: every edit key refuses
        /// with a voice while navigation, selection and copy work as in any other field.
        /// See <see cref="ReadOnlyText"/>.
        /// </summary>
        public bool ReadOnly { get; }

        public TextFieldSpec(
            string labelKey,
            int? maxLength = null,
            int? minLength = 1,
            Regex allowedChars = null,
            bool forbidGrammarSpecials = false,
            bool mustBeFilename = false,
            bool multiLine = false,
            Func<string, AcceptanceReport> customValidator = null,
            bool readOnly = false)
        {
            LabelKey = labelKey;
            MaxLength = maxLength;
            MinLength = minLength;
            AllowedChars = allowedChars;
            ForbidGrammarSpecials = forbidGrammarSpecials;
            MustBeFilename = mustBeFilename;
            MultiLine = multiLine;
            CustomValidator = customValidator;
            ReadOnly = readOnly;
        }

        /// <summary>
        /// Spec for a vanilla RimWorld dialog (Dialog_Rename, Dialog_GiveName, etc.).
        /// Pulls MaxNameLength / FirstCharLimit / IsValidName / NameIsValid by reflection.
        /// </summary>
        public static TextFieldSpec ForRimWorldDialog(Window dialog)
        {
            return RimWorldDialogIntrospector.Extract(dialog);
        }

        /// <summary>
        /// Spec for a whole-number field standing in for one of vanilla's own
        /// <see cref="Verse.Listing_Standard.IntEntry"/> boxes: digits only, no sign, and no
        /// length cap. The absent cap is deliberate and matches the widget being mirrored —
        /// IntEntry bounds a count by VALUE through its own inline clamp, never by how many
        /// characters were typed — so the caller's gated commit owns the range and this owns
        /// only what the keyboard may put in the buffer.
        /// </summary>
        public static TextFieldSpec WholeNumber(string labelKey)
        {
            return new TextFieldSpec(labelKey: labelKey, minLength: 0, allowedChars: DigitsOnly);
        }

        private static readonly Regex DigitsOnly = new Regex("^[0-9]*$");

        /// <summary>Permissive spec — non-empty, no other rules.</summary>
        public static TextFieldSpec Unrestricted(string labelKey)
        {
            return new TextFieldSpec(labelKey: labelKey, maxLength: null, minLength: 1);
        }

        /// <summary>
        /// Permissive multi-line spec — non-empty, newlines allowed, no other rules.
        /// For scenario description, ideology narrative, and other long-form text fields
        /// that render via <c>Widgets.TextArea</c> in the game.
        /// </summary>
        public static TextFieldSpec MultiLineUnrestricted(string labelKey)
        {
            return new TextFieldSpec(labelKey: labelKey, maxLength: null, minLength: 0, multiLine: true);
        }

        /// <summary>
        /// Text the player reads but never writes — the dev log's message details and any
        /// other pane whose content belongs to the game. Multi-line, so Up/Down step lines
        /// exactly as they do in an editable long-form field.
        /// </summary>
        public static TextFieldSpec ReadOnlyText(string labelKey)
        {
            return new TextFieldSpec(labelKey: labelKey, maxLength: null, minLength: 0, multiLine: true, readOnly: true);
        }
    }
}
