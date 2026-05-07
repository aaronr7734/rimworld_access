using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Grammar;

namespace RimWorldAccess
{
    public enum ValidationKind
    {
        Ok,
        TooLong,
        TooShort,
        DisallowedChars,
        GrammarSpecials,
        NotFilename,
        Custom,
    }

    public readonly struct ValidationResult
    {
        public ValidationKind Kind { get; }
        public int Limit { get; }
        public IReadOnlyList<char> BadChars { get; }
        public string CustomReason { get; }

        private ValidationResult(ValidationKind kind, int limit = 0, IReadOnlyList<char> badChars = null, string customReason = null)
        {
            Kind = kind;
            Limit = limit;
            BadChars = badChars;
            CustomReason = customReason;
        }

        public bool IsOk => Kind == ValidationKind.Ok;

        public static ValidationResult Ok() => new ValidationResult(ValidationKind.Ok);
        public static ValidationResult TooLong(int max) => new ValidationResult(ValidationKind.TooLong, limit: max);
        public static ValidationResult TooShort(int min) => new ValidationResult(ValidationKind.TooShort, limit: min);
        public static ValidationResult DisallowedChars(IReadOnlyList<char> chars) => new ValidationResult(ValidationKind.DisallowedChars, badChars: chars);
        public static ValidationResult GrammarSpecials() => new ValidationResult(ValidationKind.GrammarSpecials);
        public static ValidationResult NotFilename() => new ValidationResult(ValidationKind.NotFilename);
        public static ValidationResult Custom(string reason) => new ValidationResult(ValidationKind.Custom, customReason: reason);
    }

    public static class TextFieldValidator
    {
        public static ValidationResult Validate(string text, TextFieldSpec spec)
        {
            text = text ?? string.Empty;

            if (spec.MaxLength.HasValue && text.Length > spec.MaxLength.Value)
                return ValidationResult.TooLong(spec.MaxLength.Value);

            if (spec.MinLength.HasValue && text.Length < spec.MinLength.Value)
                return ValidationResult.TooShort(spec.MinLength.Value);

            if (spec.AllowedChars != null && text.Length > 0 && !spec.AllowedChars.IsMatch(text))
            {
                var bad = text.Where(c => !spec.AllowedChars.IsMatch(c.ToString())).Distinct().ToList();
                return ValidationResult.DisallowedChars(bad);
            }

            if (spec.ForbidGrammarSpecials && GrammarResolver.ContainsSpecialChars(text))
                return ValidationResult.GrammarSpecials();

            if (spec.MustBeFilename && !GenText.IsValidFilename(text))
                return ValidationResult.NotFilename();

            if (spec.CustomValidator != null)
            {
                var report = spec.CustomValidator(text);
                if (!report.Accepted)
                    return ValidationResult.Custom(report.Reason ?? string.Empty);
            }

            return ValidationResult.Ok();
        }

        /// <summary>
        /// Returns the localized speech string explaining a rejection. Used by
        /// <see cref="TextInputController.HandlePaste"/> and on commit failure.
        /// </summary>
        public static string AnnounceRejection(ValidationResult result, TextFieldSpec spec)
        {
            switch (result.Kind)
            {
                case ValidationKind.TooLong:
                    return "RimWorldAccess.TextInput.RejectedTooLong".Translate(result.Limit);
                case ValidationKind.TooShort:
                    if (result.Limit <= 1)
                        return "RimWorldAccess.TextInput.RejectedEmpty".Translate();
                    return "RimWorldAccess.TextInput.RejectedTooShort".Translate(result.Limit);
                case ValidationKind.DisallowedChars:
                    return "RimWorldAccess.TextInput.RejectedDisallowed".Translate(string.Join(" ", result.BadChars));
                case ValidationKind.GrammarSpecials:
                    return "RimWorldAccess.TextInput.RejectedSpecial".Translate();
                case ValidationKind.NotFilename:
                    return "RimWorldAccess.TextInput.RejectedFilename".Translate();
                case ValidationKind.Custom:
                    return "RimWorldAccess.TextInput.RejectedCustom".Translate(result.CustomReason);
                default:
                    return string.Empty;
            }
        }
    }
}
