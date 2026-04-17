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

        public TextFieldSpec(
            string labelKey,
            int? maxLength = null,
            int? minLength = 1,
            Regex allowedChars = null,
            bool forbidGrammarSpecials = false,
            bool mustBeFilename = false,
            bool multiLine = false,
            Func<string, AcceptanceReport> customValidator = null)
        {
            LabelKey = labelKey;
            MaxLength = maxLength;
            MinLength = minLength;
            AllowedChars = allowedChars;
            ForbidGrammarSpecials = forbidGrammarSpecials;
            MustBeFilename = mustBeFilename;
            MultiLine = multiLine;
            CustomValidator = customValidator;
        }

        /// <summary>
        /// Spec for an arbitrary IRenameable target. Mirrors <see cref="Dialog_Rename{T}"/>'s
        /// 28-character cap and non-empty requirement.
        /// </summary>
        public static TextFieldSpec ForIRenameable(IRenameable target, string labelKey = null)
        {
            return new TextFieldSpec(
                labelKey: labelKey ?? "RimWorldAccess.TextInput.LabelDefault",
                maxLength: 28,
                minLength: 1);
        }

        /// <summary>
        /// Spec for a vanilla RimWorld dialog (Dialog_Rename, Dialog_GiveName, etc.).
        /// Pulls MaxNameLength / FirstCharLimit / IsValidName / NameIsValid by reflection.
        /// </summary>
        public static TextFieldSpec ForRimWorldDialog(Window dialog)
        {
            return RimWorldDialogIntrospector.Extract(dialog);
        }

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
    }
}
