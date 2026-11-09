using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Vanilla Aspirations Expanded (VAE,
    /// namespace VAspirE), mirroring the VpePsycastsTabAdapter idiom: every VAE type
    /// and member is resolved once, behind <see cref="Ready"/>. A missing TYPE means
    /// VAE isn't loaded (silent decline). A resolved type with a missing MEMBER is a
    /// genuine VAE update breaking this adapter (logged once via ModLogger.Error, then
    /// declined). Every public entry point below is wrapped in try/catch with a
    /// graceful decline so a VAE update can never break the Needs tree or growth
    /// moments for players without VAE installed.
    ///
    /// Consumers: <see cref="VaeNeedsCompat"/> (Fulfillment need aspirations) and
    /// <see cref="GrowthMomentState"/> (growth-moment aspiration choices).
    /// </summary>
    internal static class VaeCompat
    {
        private static readonly Type needType;
        private static readonly Type aspirationDefType;
        private static readonly Type letterType;
        private static readonly Type dialogType;
        private static readonly Type aspirationListType;

        private static readonly FieldInfo aspirationsField;
        private static readonly FieldInfo completedTicksField;
        private static readonly PropertyInfo numCompletedProperty;
        private static readonly MethodInfo tooltipForMethod;

        private static readonly FieldInfo aspirationChoicesField;
        private static readonly FieldInfo aspirationGainsCountField;
        private static readonly FieldInfo chosenAspirationsField;
        private static readonly FieldInfo dialogChosenAspirationsField;
        private static readonly MethodInfo makeAspirationChoicesMethod;

        private static readonly bool ready;

        public static bool Ready => ready;

        static VaeCompat()
        {
            var surface = new ReflectionSurface("VaeCompat");

            needType = surface.Type("VAspirE.Need_Fulfillment");
            aspirationDefType = surface.Type("VAspirE.AspirationDef");
            letterType = surface.Type("VAspirE.ChoiceLetter_GrowthMoment_Aspirations");
            dialogType = surface.Type("VAspirE.Dialog_GrowthMomentChoices_Aspirations");

            aspirationListType = aspirationDefType != null
                ? typeof(List<>).MakeGenericType(aspirationDefType)
                : null;

            aspirationsField = surface.Field(needType, "Aspirations");
            completedTicksField = surface.Field(needType, "completedTicks");
            numCompletedProperty = surface.Property(needType, "NumCompleted");
            tooltipForMethod = surface.Method(aspirationDefType, "TooltipFor", new[] { typeof(Pawn), typeof(int) });

            aspirationChoicesField = surface.Field(letterType, "aspirationChoices");
            aspirationGainsCountField = surface.Field(letterType, "aspirationGainsCount");
            chosenAspirationsField = surface.Field(letterType, "chosenAspirations");
            dialogChosenAspirationsField = surface.Field(dialogType, "chosenAspirations");
            makeAspirationChoicesMethod = aspirationListType != null
                ? surface.Method(letterType, "MakeAspirationChoices", new[] { aspirationListType })
                : null;

            ready = surface.Ready;
        }

        /// <summary>True when <paramref name="need"/> is VAE's Fulfillment need.</summary>
        public static bool IsFulfillmentNeed(Need need)
        {
            try
            {
                return ready && need != null && needType.IsInstanceOfType(need);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VaeCompat.IsFulfillmentNeed failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Number of aspirations the Fulfillment need has completed, or 0 on failure.</summary>
        public static int GetNumCompleted(Need need)
        {
            try
            {
                if (!IsFulfillmentNeed(need))
                    return 0;
                return (int)numCompletedProperty.GetValue(need);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VaeCompat.GetNumCompleted failed: {ex.Message}");
                return 0;
            }
        }

        public struct AspirationEntry
        {
            public string Label;
            public bool Complete;
            public string Tooltip;
        }

        /// <summary>
        /// Reads the Fulfillment need's aspirations paired with their completion state
        /// and per-aspiration tooltip text. Returns null on any failure.
        /// </summary>
        public static List<AspirationEntry> GetAspirations(Need need, Pawn pawn)
        {
            try
            {
                if (!IsFulfillmentNeed(need) || pawn == null)
                    return null;

                var aspirations = aspirationsField.GetValue(need) as IList;
                var completedTicks = completedTicksField.GetValue(need) as IList;
                if (aspirations == null || completedTicks == null)
                    return null;

                int count = Math.Min(aspirations.Count, completedTicks.Count);
                var entries = new List<AspirationEntry>();
                for (int i = 0; i < count; i++)
                {
                    object def = aspirations[i];
                    int tick = (int)completedTicks[i];
                    string label = ((Def)def).LabelCap;
                    string tooltip;
                    try
                    {
                        tooltip = (string)tooltipForMethod.Invoke(def, new object[] { pawn, tick });
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Error($"VaeCompat.GetAspirations tooltip failed for {label}: {ex.Message}");
                        tooltip = "";
                    }

                    entries.Add(new AspirationEntry { Label = label, Complete = tick != -1, Tooltip = tooltip });
                }

                return entries;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VaeCompat.GetAspirations failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>True when <paramref name="letter"/> is VAE's aspirations growth-moment letter.</summary>
        public static bool IsAspirationLetter(ChoiceLetter_GrowthMoment letter)
        {
            try
            {
                return ready && letter != null && letterType.IsInstanceOfType(letter);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VaeCompat.IsAspirationLetter failed: {ex.Message}");
                return false;
            }
        }

        public class AspirationChoice
        {
            public object Def;
            public string Label;
            public string Details;
        }

        /// <summary>
        /// Reads the aspiration choices offered by a VAE growth-moment letter, along
        /// with how many the pawn must pick. False (with both out params left at their
        /// defaults) unless the letter is VAE's aspirations letter with a non-empty
        /// offer.
        /// </summary>
        public static bool TryGetAspirationChoices(ChoiceLetter_GrowthMoment letter, out List<AspirationChoice> choices, out int gainsCount)
        {
            choices = null;
            gainsCount = 0;
            try
            {
                if (!IsAspirationLetter(letter))
                    return false;

                var rawChoices = aspirationChoicesField.GetValue(letter) as IList;
                if (rawChoices == null || rawChoices.Count == 0)
                    return false;

                int gains = (int)aspirationGainsCountField.GetValue(letter);
                if (gains <= 0)
                    return false;

                Pawn pawn = letter.pawn;
                var result = new List<AspirationChoice>();
                foreach (object def in rawChoices)
                {
                    string label = ((Def)def).LabelCap;
                    string details = "";
                    try
                    {
                        string tooltip = (string)tooltipForMethod.Invoke(def, new object[] { pawn, -1 });
                        details = FormatChoiceDetails(tooltip, label);
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Error($"VaeCompat.TryGetAspirationChoices details failed for {label}: {ex.Message}");
                    }

                    result.Add(new AspirationChoice { Def = def, Label = label, Details = details });
                }

                choices = result;
                gainsCount = gains;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VaeCompat.TryGetAspirationChoices failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// The open dialog's own in-progress aspiration selection — the list
        /// Dialog_GrowthMomentChoices_Aspirations.DrawAspirationChoices' checkbox and radio
        /// branches write and its OK button hands to MakeAspirationChoices. The caller mutates
        /// it in place, so the drawn marks follow a keyboard selection. False unless
        /// <paramref name="dialog"/> is VAE's aspirations dialog.
        /// </summary>
        public static bool TryGetChosenAspirations(Window dialog, out IList chosen)
        {
            chosen = null;
            try
            {
                if (!ready || dialog == null || !dialogType.IsInstanceOfType(dialog))
                    return false;

                chosen = dialogChosenAspirationsField.GetValue(dialog) as IList;
                return chosen != null;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VaeCompat.TryGetChosenAspirations failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Chosen aspiration labels for archive-view display, or null when absent/empty.</summary>
        public static List<string> GetChosenAspirationLabels(ChoiceLetter_GrowthMoment letter)
        {
            try
            {
                if (!IsAspirationLetter(letter))
                    return null;

                var chosen = chosenAspirationsField.GetValue(letter) as IList;
                if (chosen == null || chosen.Count == 0)
                    return null;

                var labels = new List<string>();
                foreach (object def in chosen)
                    labels.Add(((Def)def).LabelCap);
                return labels;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VaeCompat.GetChosenAspirationLabels failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Applies the player's aspiration selections to a VAE growth-moment letter.
        /// This is mutation vehicle A: it invokes the dialog's own OK-button apply
        /// method (Dialog_GrowthMomentChoices_Aspirations.DoWindowContents, OK branch)
        /// — letter.MakeAspirationChoices(chosenAspirations) is exactly what that
        /// button calls, before MakeChoices. The caller must mirror the dialog's
        /// CanCloseOverride count gate (selected count == aspirationGainsCount) before
        /// calling this.
        /// </summary>
        public static bool ApplyAspirationChoices(ChoiceLetter_GrowthMoment letter, IList chosenDefs)
        {
            try
            {
                if (!IsAspirationLetter(letter))
                    return false;

                IList typedList = (IList)Activator.CreateInstance(aspirationListType);
                if (chosenDefs != null)
                    foreach (object def in chosenDefs)
                        typedList.Add(def);

                makeAspirationChoicesMethod.Invoke(letter, new object[] { typedList });
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VaeCompat.ApplyAspirationChoices failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Flattens an AspirationDef.TooltipFor blob into a single spoken sentence for
        /// use as a growth-moment choice's Details: tags stripped, newlines flattened
        /// to sentence breaks (mirroring GrowthMomentState.BuildTraitItems's own
        /// flattening), then the leading tip-title line (which just repeats the
        /// aspiration's label) dropped.
        /// </summary>
        private static string FormatChoiceDetails(string tooltip, string label)
        {
            if (string.IsNullOrEmpty(tooltip))
                return "";

            // Null when the blob was tags and whitespace only.
            string flattened = SpeechFlatten.ToSentences(tooltip.StripTags()) ?? "";

            string labelPrefix = (label ?? "").StripTags().Trim() + ". ";
            if (flattened.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase))
                flattened = flattened.Substring(labelPrefix.Length);

            return flattened.Trim();
        }
    }
}
