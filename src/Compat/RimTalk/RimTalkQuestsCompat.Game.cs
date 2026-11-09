using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade for the Quests extension (rimtalk.quests,
    /// RimTalkQuests.dll). The sharpest async-text hazard in the whole
    /// family: <c>QuestDescriptionGenerator.CallRimTalkAI</c> reassigns the
    /// live <c>Quest.description</c> field on EVERY streamed chunk (many times
    /// a second) via <c>originalDescription + "\n\n───────────\n\n" + chunkText</c>,
    /// and applies the same concatenation once more, complete, when the stream
    /// finishes -- so the separator and the generated text are baked
    /// PERMANENTLY into the live description from then on (unlike Literature's
    /// single-shot addendum). The standing rule (see AsyncTextStability's own
    /// header): never announce partial streamed text.
    ///
    /// TWO INDEPENDENT MODS mutate the same <c>Quest.description</c> field this
    /// way -- Quests itself, and Literature's own quest-description addendum
    /// (<see cref="RimTalkLiteratureCompat.IsQuestGenerating"/>, whose own remarks
    /// hand this integration here rather than duplicating it there).
    /// <see cref="IsQuestDescriptionGenerating"/> folds both signals at the one
    /// shared read site; each probe self-disables when its mod is absent.
    ///
    /// SIGNAL PRECISION: the public <c>ProcessingCount</c> is just
    /// <c>_processingQuests.Count</c> -- a coarse mod-wide "something is
    /// generating" count. But <c>_processingQuests</c> ITSELF is a
    /// <c>private static readonly HashSet&lt;int&gt;</c> keyed by <c>quest.id</c>
    /// (added before the async call, removed in a <c>finally</c> block once it
    /// settles or fails), so a per-quest answer is available via reflection
    /// and used here in preference to the coarse count. <c>ProcessingCount</c>
    /// remains a documented fallback if that private field is ever renamed:
    /// every quest reads as "generating" while ANY quest anywhere is (imprecise,
    /// but self-disabling to false the moment nothing streams, same as never
    /// having the precise signal at all).
    /// </summary>
    internal static class RimTalkQuestsCompat
    {
        private const string PackageId = "rimtalk.quests";

        private static readonly FieldInfo processingQuestsField;
        private static readonly MethodInfo processingCountGetter;

        private static readonly bool preciseReady;
        private static readonly bool coarseReady;

        public static bool Ready => preciseReady || coarseReady;

        static RimTalkQuestsCompat()
        {
            Type generatorType = AccessTools.TypeByName("RimTalkQuests.Services.QuestDescriptionGenerator");
            if (generatorType != null)
            {
                processingQuestsField = AccessTools.Field(generatorType, "_processingQuests");
                PropertyInfo processingCountProperty = AccessTools.Property(generatorType, "ProcessingCount");
                processingCountGetter = processingCountProperty?.GetGetMethod();
            }

            preciseReady = processingQuestsField != null && typeof(HashSet<int>).IsAssignableFrom(processingQuestsField.FieldType);
            coarseReady = processingCountGetter != null;

            if (generatorType != null && !preciseReady && !coarseReady)
            {
                ModLogger.Warning("Quests compat: neither _processingQuests nor ProcessingCount resolved -- streaming quest descriptions will read live text only, with no wait-for-settled protection.");
            }
        }

        public static void Register()
        {
            if (!ModsConfig.IsActive(PackageId))
            {
                return;
            }
            string detail = preciseReady
                ? "per-quest generation signal active"
                : coarseReady
                    ? "coarse mod-wide generation signal only (per-quest field not found)"
                    : "generation-in-progress detection unavailable";
        }

        /// <summary>
        /// True while EITHER Quests' own streaming generator or Literature's quest-addendum
        /// queue is actively (re)writing <paramref name="quest"/>'s description right now.
        /// False (never blocks a real read) once both mods have settled or if neither is
        /// installed/resolved.
        /// </summary>
        public static bool IsQuestDescriptionGenerating(Quest quest)
        {
            if (quest == null)
            {
                return false;
            }
            return IsQuestsExtensionGenerating(quest) || RimTalkLiteratureCompat.IsQuestGenerating(quest);
        }

        private static bool IsQuestsExtensionGenerating(Quest quest)
        {
            if (preciseReady)
            {
                try
                {
                    var processingQuests = (HashSet<int>)processingQuestsField.GetValue(null);
                    return processingQuests != null && processingQuests.Contains(quest.id);
                }
                catch (Exception ex)
                {
                    ModLogger.Warning("Quests compat: reading _processingQuests threw, falling back to coarse signal: " + ex);
                }
            }
            if (coarseReady)
            {
                try
                {
                    return (int)processingCountGetter.Invoke(null, null) > 0;
                }
                catch (Exception ex)
                {
                    ModLogger.Warning("Quests compat: ProcessingCount getter threw, treating as not generating: " + ex);
                }
            }
            return false;
        }

        /// <summary>
        /// Read-only substitute for <c>quest.description.Resolve()</c> with no wait-for-settled
        /// registration: the marker while generating, the live (possibly still mid-stream on the
        /// very next read, by design) text otherwise. Used at read sites that are not a
        /// persistently-tracked cursor position (world-map tile summaries), where the normal
        /// "re-read next time this tile/row is visited" flow already recovers the settled text
        /// exactly like Literature's book/art adapters, so arming a fire-once callback would add
        /// complexity with no player-facing benefit.
        /// </summary>
        public static string GetDescriptionOrMarker(Quest quest)
        {
            if (quest == null)
            {
                return null;
            }
            return IsQuestDescriptionGenerating(quest)
                ? AsyncTextStability.StillBeingWrittenMarker()
                : quest.description.Resolve();
        }

        /// <summary>
        /// The gated read for a persistently-tracked cursor position (the quest list row and the
        /// quest detail view a screen reader's keyboard cursor actually sits on): marker now while
        /// generating, PLUS a fire-once re-announcement armed via <see cref="AsyncTextStability"/>
        /// the moment both mods' signals settle, provided <paramref name="isStillRelevant"/> still
        /// holds at that moment (the caller's "is the cursor still on this exact quest" check).
        /// Keyed on <c>quest.id</c> so Quests' and Literature's settling don't arm two competing
        /// interests for the same quest -- whichever read call happens last simply refreshes the one
        /// pending interest with a freshened closure, matching AsyncTextStabilityCore's own documented
        /// "revisited on a later frame" semantics.
        /// </summary>
        public static string GetDescriptionOrMarker(Quest quest, Func<bool> isStillRelevant, Action onSettledAnnounce)
        {
            if (quest == null)
            {
                return null;
            }
            string key = "RimTalkQuestsCompat.Quest:" + quest.id;
            return AsyncTextStability.GetTextOrMarker(
                () => IsQuestDescriptionGenerating(quest),
                key,
                () => quest.description.Resolve(),
                isStillRelevant,
                onSettledAnnounce != null ? (Action<string>)(_ => onSettledAnnounce()) : null);
        }

        private static readonly LazyReflectionGate regenerateGate =
            new LazyReflectionGate("Quests compat (regenerate button)", ResolveRegenerate);

        private static PropertyInfo modSettingsProperty;
        private static FieldInfo enableAIDescriptionsField;
        private static MethodInfo isAIServiceAvailableMethod;
        private static MethodInfo isProcessingMethod;
        private static MethodInfo regenerateMethod;

        private static bool ResolveRegenerate(ReflectionSurface surface)
        {
            Type modType = surface.Type("RimTalkQuests.RimTalkQuestsMod");
            Type settingsType = surface.Type("RimTalkQuests.QuestSettings");
            Type generatorType = surface.Type("RimTalkQuests.Services.QuestDescriptionGenerator");
            PropertyInfo settings = surface.Property(modType, "Settings");
            FieldInfo enable = surface.Field(settingsType, "enableAIDescriptions");
            MethodInfo available = surface.Method(generatorType, "IsAIServiceAvailable", Type.EmptyTypes);
            MethodInfo processing = surface.Method(generatorType, "IsProcessing", new[] { typeof(int) });
            MethodInfo regenerate = surface.Method(generatorType, "RegenerateQuestDescription", new[] { typeof(Quest) });
            if (!surface.Ready)
            {
                return false;
            }
            modSettingsProperty = settings;
            enableAIDescriptionsField = enable;
            isAIServiceAvailableMethod = available;
            isProcessingMethod = processing;
            regenerateMethod = regenerate;
            return true;
        }

        /// <summary>QuestTabWindowPatch's Regenerate button, gated and labeled as DoDescriptionPostfix draws it; the click is the generator's own RegenerateQuestDescription (vehicle A).</summary>
        public static bool TryGetRegenerateAction(Quest quest, out ScreenAction action)
        {
            action = null;
            if (quest == null || !ModsConfig.IsActive(PackageId) || !regenerateGate.Ensure())
            {
                return false;
            }
            try
            {
                object settings = modSettingsProperty.GetValue(null);
                if (settings == null || !(enableAIDescriptionsField.GetValue(settings) is bool enabled) || !enabled)
                {
                    return false;
                }
                bool available = (bool)isAIServiceAvailableMethod.Invoke(null, null);
                bool processing = (bool)isProcessingMethod.Invoke(null, new object[] { quest.id });
                string label = Translator.Translate(processing ? "RimTalkQuests.Regenerate.InProgress" : "RimTalkQuests.Regenerate");
                string reason = !available
                    ? (string)Translator.Translate("RimTalkQuests.Regenerate.Unavailable")
                    : processing ? label : null;
                action = new ScreenAction(label,
                    delegate { regenerateMethod.Invoke(null, new object[] { quest }); },
                    disabled: reason != null,
                    disabledReason: reason);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Quests compat: regenerate button", ex);
                return false;
            }
        }
    }
}
