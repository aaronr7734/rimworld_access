using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Shaping shared by everything that reads a host mod's own text: mods write multi-line
    /// styled tooltips, and announcements separate with periods, never newlines.
    /// </summary>
    internal static class CompatText
    {
        private static readonly Dictionary<string, string> modTitles = new Dictionary<string, string>();

        public static string ModTitle(string packageId, string fallback = null)
        {
            if (!modTitles.TryGetValue(packageId, out string title))
            {
                title = ModLister.GetActiveModWithIdentifier(packageId, ignorePostfix: true)?.Name ?? fallback ?? packageId;
                modTitles[packageId] = title;
            }
            return title;
        }

        /// <summary>The mod's own keys resolved through its own Keyed data; RWA keys keep using .Translate directly.</summary>
        public static string ModText(string key)
        {
            return Translator.Translate(key).Resolve();
        }

        public static string ModArgs(string key, params NamedArgument[] args)
        {
            return TranslatorFormattedStringExtensions.Translate(key, args).Resolve();
        }

        public static string ModAdjustedFor(string key, Pawn pawn, params NamedArgument[] args)
        {
            return TranslatorFormattedStringExtensions.Translate(key, args).AdjustedFor(pawn).Resolve();
        }

        /// <summary>The mod's own hover text for a control, flattened, resolved lazily -- only the focused row pays for it.</summary>
        public static Func<string> ModTip(string key)
        {
            return () => Flatten(ModText(key));
        }

        /// <summary>
        /// Joins fragments with ". ", skipping the period when a fragment already ends in sentence
        /// punctuation -- the same rule <see cref="AnnouncementComposer"/> applies between its own.
        /// </summary>
        public static string JoinSentences(List<string> parts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                string fragment = parts[i] == null ? "" : parts[i].Trim();
                if (fragment.Length == 0)
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    char last = sb[sb.Length - 1];
                    if (last != '.' && last != '!' && last != '?' && last != ':')
                    {
                        sb.Append('.');
                    }
                    sb.Append(' ');
                }
                sb.Append(fragment);
            }
            return sb.ToString();
        }

        // StripTags first: the mod styles warnings inline (<b>, color tags), and a screen reader
        // would read the markup as text.
        public static string Flatten(string text)
        {
            return string.IsNullOrEmpty(text)
                ? ""
                : GizmoTextUtility.FlattenNewlines(text.StripTags());
        }

        /// <summary>
        /// The text up to the first blank line, or all of it when there is none. A mod's tooltips
        /// lead with a summary/state paragraph and follow with explanation, so this split is
        /// structural (the key's own \n\n), never a match on translated words.
        /// </summary>
        public static string FirstParagraph(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            int split = text.IndexOf("\n\n", StringComparison.Ordinal);
            return split < 0 ? text : text.Substring(0, split);
        }

        /// <summary>The text after the first blank line, or all of it when there is none.</summary>
        public static string AfterFirstParagraph(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            int split = text.IndexOf("\n\n", StringComparison.Ordinal);
            return split < 0 ? text : text.Substring(split + 2);
        }

        /// <summary>
        /// The mod's own resolved label when its key exists, else the RWA fallback key.
        /// The existence test is the game's own (<see cref="Translator.CanTranslate"/>),
        /// never a comparison of translated output against the key.
        /// </summary>
        public static string ResolveOrFallback(string modKey, string rwaFallbackKey)
        {
            return modKey.CanTranslate() ? ModText(modKey) : rwaFallbackKey.Translate().Resolve();
        }
    }
}
