using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflects per-Type field constraints out of vanilla RimWorld dialogs so our
    /// <see cref="TextFieldSpec"/> mirrors what the underlying dialog would accept on
    /// commit. Caches per Type to avoid repeat reflection.
    /// </summary>
    public static class RimWorldDialogIntrospector
    {
        private static readonly Dictionary<Type, Func<Window, TextFieldSpec>> Builders = new Dictionary<Type, Func<Window, TextFieldSpec>>();

        /// <summary>
        /// Build (or look up) a TextFieldSpec for the given dialog instance. Falls back to
        /// a permissive 28-char spec if no specialized extractor matches.
        /// </summary>
        public static TextFieldSpec Extract(Window dialog)
        {
            if (dialog == null) return new TextFieldSpec(labelKey: "RimWorldAccess.TextInput.LabelDefault", maxLength: 28);

            Type type = dialog.GetType();
            for (Type cur = type; cur != null && cur != typeof(Window); cur = cur.BaseType)
            {
                if (Builders.TryGetValue(cur, out var cached))
                    return cached(dialog);

                if (IsDialogRename(cur))
                {
                    var builder = BuildDialogRenameExtractor(cur);
                    Builders[cur] = builder;
                    return builder(dialog);
                }

                if (cur == typeof(Dialog_GiveName) || cur.IsSubclassOf(typeof(Dialog_GiveName)))
                {
                    var builder = BuildDialogGiveNameExtractor(cur);
                    Builders[cur] = builder;
                    return builder(dialog);
                }
            }

            return new TextFieldSpec(labelKey: "RimWorldAccess.TextInput.LabelDefault", maxLength: 28);
        }

        private static bool IsDialogRename(Type t)
        {
            return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dialog_Rename<>);
        }

        private static Func<Window, TextFieldSpec> BuildDialogRenameExtractor(Type renameType)
        {
            var maxLenProp = AccessTools.Property(renameType, "MaxNameLength");
            var validateMethod = AccessTools.Method(renameType, "NameIsValid", new[] { typeof(string) });

            return dialog =>
            {
                int maxLen = (int)maxLenProp.GetValue(dialog, null);
                Func<string, AcceptanceReport> custom = null;
                if (validateMethod != null)
                {
                    custom = name => (AcceptanceReport)validateMethod.Invoke(dialog, new object[] { name });
                }
                return new TextFieldSpec(
                    labelKey: "RimWorldAccess.TextInput.LabelDefault",
                    maxLength: maxLen,
                    minLength: 1,
                    customValidator: custom);
            };
        }

        private static Func<Window, TextFieldSpec> BuildDialogGiveNameExtractor(Type giveNameType)
        {
            var firstCharLimitProp = AccessTools.Property(giveNameType, "FirstCharLimit");
            var isValidNameMethod = AccessTools.Method(giveNameType, "IsValidName", new[] { typeof(string) });

            return dialog =>
            {
                int maxLen = firstCharLimitProp != null ? (int)firstCharLimitProp.GetValue(dialog, null) : 64;
                Func<string, AcceptanceReport> custom = null;
                if (isValidNameMethod != null)
                {
                    custom = name => (bool)isValidNameMethod.Invoke(dialog, new object[] { name })
                        ? AcceptanceReport.WasAccepted
                        : AcceptanceReport.WasRejected;
                }
                bool isFaction = dialog is Dialog_NamePlayerFaction;
                bool isSettlement = dialog is Dialog_NamePlayerSettlement;
                return new TextFieldSpec(
                    labelKey: isFaction
                        ? "RimWorldAccess.TextInput.LabelFaction"
                        : isSettlement
                            ? "RimWorldAccess.TextInput.LabelSettlement"
                            : "RimWorldAccess.TextInput.LabelDefault",
                    maxLength: maxLen,
                    minLength: 1,
                    forbidGrammarSpecials: true,
                    mustBeFilename: isFaction,
                    customValidator: custom);
            };
        }

        /// <summary>
        /// Spec for a single Dialog_NamePawn.NameContext field (first / nick / last).
        /// AND-combines per-field max length with <see cref="CharacterCardUtility.ValidNameRegex"/>.
        /// </summary>
        public static TextFieldSpec ForPawnNameField(int maximumNameLength, string labelKey)
        {
            return new TextFieldSpec(
                labelKey: labelKey,
                maxLength: maximumNameLength,
                minLength: 1,
                allowedChars: CharacterCardUtility.ValidNameRegex);
        }
    }
}
