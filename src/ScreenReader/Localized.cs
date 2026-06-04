using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// A string that has been produced by the translation system and is therefore
    /// safe to announce to a screen reader in any language.
    /// </summary>
    /// <remarks>
    /// The constructor is <c>internal</c> and there is deliberately NO implicit
    /// conversion from <see cref="string"/>, so the only way to obtain a
    /// <see cref="Localized"/> is the <see cref="LocalizationExtensions.Loc(string)"/>
    /// family — which always routes through <c>.Translate()</c>. Because
    /// <see cref="TolkHelper.Speak(Localized, SpeechPriority)"/> accepts only this
    /// type, a raw English literal cannot be spoken by accident: it simply will not
    /// compile. Text that is intentionally not translatable (numbers, proper names,
    /// game-supplied labels) must be spoken through
    /// <see cref="TolkHelper.SpeakData(string, SpeechPriority)"/> instead.
    /// </remarks>
    public readonly struct Localized
    {
        private readonly TaggedString inner;

        internal Localized(TaggedString translated)
        {
            inner = translated;
        }

        /// <summary>
        /// The text handed to the screen reader. Uses the historical
        /// <see cref="TaggedString"/>-to-<see cref="string"/> conversion (which strips
        /// markup tags) so the spoken output matches the pre-existing behavior exactly.
        /// </summary>
        internal string SpokenText => inner;

        /// <summary>Capitalizes the first letter, preserving the translation source.</summary>
        public Localized CapitalizeFirst() => new Localized(inner.CapitalizeFirst());

        /// <summary>Lower-cases the text, preserving the translation source.</summary>
        public Localized ToLower() => new Localized(inner.ToLower());

        /// <summary>Trims surrounding whitespace, preserving the translation source.</summary>
        public Localized Trim() => new Localized(inner.Trim());

        public override string ToString() => inner.ToString();
    }

    /// <summary>
    /// Extension methods that turn a translation key into a speakable
    /// <see cref="Localized"/> value. These are the drop-in replacement for
    /// <c>.Loc(...)</c> at <see cref="TolkHelper.Speak(Localized, SpeechPriority)"/>
    /// call sites.
    /// </summary>
    public static class LocalizationExtensions
    {
        /// <summary>Translates this key and wraps the result as a <see cref="Localized"/>.</summary>
        public static Localized Loc(this string key) => new Localized(key.Translate());

        /// <summary>Translates this key with the given arguments and wraps the result.</summary>
        public static Localized Loc(this string key, params NamedArgument[] args) =>
            new Localized(key.Translate(args));
    }
}
