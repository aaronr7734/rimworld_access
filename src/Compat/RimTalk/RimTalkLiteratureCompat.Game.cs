using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade for the Literature extension (cj.rimtalk.literature), which rewrites book
    /// synopses, art titles/descriptions and flavor text in place on vanilla surfaces our readers
    /// already reach. Covers the two surfaces where an in-flight async rewrite is reachable
    /// mid-generation: <see cref="BookAdapter"/>'s and <see cref="ArtAdapter"/>'s inspection-tree
    /// children, via <see cref="IsBookGenerating"/>/<see cref="IsArtGenerating"/>. No
    /// <see cref="AsyncTextStability"/> pending-interest is needed — both adapters rebuild lazily
    /// when their category node is revisited with <c>Children.Count == 0</c>, so leaving the marker
    /// uncached while generating makes the next visit re-check for free.
    /// </summary>
    internal static class RimTalkLiteratureCompat
    {
        private const string PackageId = "cj.rimtalk.literature";

        private static readonly MethodInfo bookTryGetKeyMethod;
        private static readonly MethodInfo pendingBookContainsMethod;
        private static readonly MethodInfo artTryGetKeyMethod;
        private static readonly MethodInfo pendingArtContainsMethod;
        private static readonly FieldInfo questPendingField;

        private static readonly bool ready;
        private static readonly bool questSignalReady;

        public static bool Ready => ready;

        static RimTalkLiteratureCompat()
        {
            Type bookKeyProviderType = AccessTools.TypeByName("RimTalk_LiteratureExpansion.storage.BookKeyProvider");
            Type bookKeyType = AccessTools.TypeByName("RimTalk_LiteratureExpansion.storage.BookKey");
            Type pendingBookQueueType = AccessTools.TypeByName("RimTalk_LiteratureExpansion.scanner.queue.PendingBookQueue");
            Type artKeyProviderType = AccessTools.TypeByName("RimTalk_LiteratureExpansion.storage.ArtKeyProvider");
            Type artKeyType = AccessTools.TypeByName("RimTalk_LiteratureExpansion.storage.ArtKey");
            Type pendingArtQueueType = AccessTools.TypeByName("RimTalk_LiteratureExpansion.scanner.queue.PendingArtQueue");
            Type questRewriterType = AccessTools.TypeByName("RimTalk_LiteratureExpansion.events.QuestDescriptionRewriter");

            if (bookKeyProviderType != null && bookKeyType != null)
            {
                // The 2-arg overload; a 3-arg (Thing, Map, out BookKey) sibling also exists, so the
                // parameter list must be exact.
                bookTryGetKeyMethod = AccessTools.Method(bookKeyProviderType, "TryGetKey",
                    new[] { typeof(Thing), bookKeyType.MakeByRefType() });
            }
            if (pendingBookQueueType != null && bookKeyType != null)
            {
                pendingBookContainsMethod = AccessTools.Method(pendingBookQueueType, "Contains", new[] { bookKeyType });
            }
            if (artKeyProviderType != null && artKeyType != null)
            {
                artTryGetKeyMethod = AccessTools.Method(artKeyProviderType, "TryGetKey",
                    new[] { typeof(Thing), artKeyType.MakeByRefType() });
            }
            if (pendingArtQueueType != null && artKeyType != null)
            {
                pendingArtContainsMethod = AccessTools.Method(pendingArtQueueType, "Contains", new[] { artKeyType });
            }

            ready = bookTryGetKeyMethod != null && pendingBookContainsMethod != null
                && artTryGetKeyMethod != null && pendingArtContainsMethod != null;

            if (bookKeyProviderType != null && !ready)
            {
                ModLogger.Warning("Literature compat: BookKeyProvider/PendingBookQueue/ArtKeyProvider/PendingArtQueue reflection surface not fully resolved -- book/art generation-in-progress detection disabled, descriptions will read live text only.");
            }

            if (questRewriterType != null)
            {
                // Dictionary<int, PendingQuestRewrite> keyed by quest.id. The value type is private,
                // but Dictionary<,> always implements non-generic IDictionary, so IsQuestGenerating
                // can call Contains(quest.id) without naming it.
                questPendingField = AccessTools.Field(questRewriterType, "Pending");
            }
            questSignalReady = questPendingField != null && typeof(IDictionary).IsAssignableFrom(questPendingField.FieldType);

            if (questRewriterType != null && !questSignalReady)
            {
                ModLogger.Warning("Literature compat: QuestDescriptionRewriter.Pending not resolved -- quest-description generation-in-progress detection disabled for this mod's own addendum (Quests' own signal, if present, still applies).");
            }
        }

        /// <summary>
        /// True while Literature's own <c>QuestDescriptionRewriter.Pending</c> queue holds this
        /// quest's id. False when the mod isn't loaded, its reflection didn't resolve, or the quest
        /// has settled. <see cref="RimTalkQuestsCompat.IsQuestDescriptionGenerating"/> combines it
        /// with the Quests extension's own signal.
        /// </summary>
        public static bool IsQuestGenerating(Quest quest)
        {
            if (!questSignalReady || quest == null)
            {
                return false;
            }
            try
            {
                var pending = questPendingField.GetValue(null) as IDictionary;
                return pending != null && pending.Contains(quest.id);
            }
            catch (Exception ex)
            {
                ModLogger.Warning("Literature compat: reading QuestDescriptionRewriter.Pending threw, treating as not generating: " + ex);
                return false;
            }
        }

        public static void Register()
        {
            if (!ModsConfig.IsActive(PackageId))
            {
                return;
            }
        }

        /// <summary>
        /// Applied by <see cref="RimTalkModule"/> rather than declaratively: each target is resolved
        /// by name and patched only when it exists, so a player without the Literature addon never
        /// trips Harmony's null-target throw.
        /// </summary>
        public static void ApplyPatches(Harmony harmony)
        {
            if (!ModsConfig.IsActive(PackageId))
            {
                return;
            }

            TryPatch(harmony, BookCacheClearTarget(),
                postfix: new HarmonyMethod(typeof(RimTalkLiteratureBookCacheClearPatch), nameof(RimTalkLiteratureBookCacheClearPatch.Postfix)));

            TryPatch(harmony, ArtCacheClearTarget(),
                postfix: new HarmonyMethod(typeof(RimTalkLiteratureArtCacheClearPatch), nameof(RimTalkLiteratureArtCacheClearPatch.Postfix)));

            TryPatch(harmony, MapArtScanTarget(),
                postfix: new HarmonyMethod(typeof(RimTalkLiteratureMapArtScanCountPatch), nameof(RimTalkLiteratureMapArtScanCountPatch.Postfix)));

            TryPatch(harmony, SettingsDrawTarget(),
                prefix: new HarmonyMethod(typeof(RimTalkLiteratureSettingsDrawRescanAnnouncePatch), nameof(RimTalkLiteratureSettingsDrawRescanAnnouncePatch.Prefix)),
                postfix: new HarmonyMethod(typeof(RimTalkLiteratureSettingsDrawRescanAnnouncePatch), nameof(RimTalkLiteratureSettingsDrawRescanAnnouncePatch.Postfix)));
        }

        private static void TryPatch(Harmony harmony, MethodBase target, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            if (target == null)
            {
                return;
            }
            try
            {
                harmony.Patch(target, prefix: prefix, postfix: postfix);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Literature compat: patching {target.DeclaringType?.Name}.{target.Name} failed: {ex.Message}");
            }
        }

        internal static MethodBase BookCacheClearTarget()
        {
            Type type = AccessTools.TypeByName("RimTalk_LiteratureExpansion.storage.BookSynopsisCache");
            return type != null ? AccessTools.Method(type, "Clear") : null;
        }

        internal static MethodBase ArtCacheClearTarget()
        {
            Type type = AccessTools.TypeByName("RimTalk_LiteratureExpansion.storage.ArtDescriptionCache");
            return type != null ? AccessTools.Method(type, "Clear") : null;
        }

        internal static MethodBase MapArtScanTarget()
        {
            Type type = AccessTools.TypeByName("RimTalk_LiteratureExpansion.scanner.MapArtScanner");
            return type != null ? AccessTools.Method(type, "Scan", new[] { typeof(Map) }) : null;
        }

        internal static MethodBase SettingsDrawTarget()
        {
            Type windowType = AccessTools.TypeByName("RimTalk_LiteratureExpansion.settings.LiteratureSettingsWindow");
            Type settingsType = AccessTools.TypeByName("RimTalk_LiteratureExpansion.settings.LiteratureSettings");
            if (windowType == null || settingsType == null)
            {
                return null;
            }
            return AccessTools.Method(windowType, "Draw", new[] { typeof(Rect), settingsType });
        }

        /// <summary>
        /// True while Literature's <c>PendingBookQueue</c> holds this book's key. False — never
        /// blocking a real read — when the mod isn't loaded, its reflection didn't resolve, or the
        /// thing has no valid key.
        /// </summary>
        public static bool IsBookGenerating(Thing book)
        {
            if (!ready || book == null || ThingUtility.DestroyedOrNull(book))
            {
                return false;
            }
            object[] args = { book, null };
            bool found;
            try
            {
                found = (bool)bookTryGetKeyMethod.Invoke(null, args);
            }
            catch (Exception ex)
            {
                ModLogger.Warning("Literature compat: BookKeyProvider.TryGetKey threw, treating as not generating: " + ex);
                return false;
            }
            if (!found || args[1] == null)
            {
                return false;
            }
            return (bool)pendingBookContainsMethod.Invoke(null, new[] { args[1] });
        }

        /// <summary>As <see cref="IsBookGenerating"/>, for Literature's <c>PendingArtQueue</c>.</summary>
        public static bool IsArtGenerating(Thing artThing)
        {
            if (!ready || artThing == null || ThingUtility.DestroyedOrNull(artThing))
            {
                return false;
            }
            object[] args = { artThing, null };
            bool found;
            try
            {
                found = (bool)artTryGetKeyMethod.Invoke(null, args);
            }
            catch (Exception ex)
            {
                ModLogger.Warning("Literature compat: ArtKeyProvider.TryGetKey threw, treating as not generating: " + ex);
                return false;
            }
            if (!found || args[1] == null)
            {
                return false;
            }
            return (bool)pendingArtContainsMethod.Invoke(null, new[] { args[1] });
        }

        // Settings page: the three debug buttons the mod itself acknowledges only via Log.Message.

        /// <summary>Monotonic count of MapArtScanner.Scan calls, any trigger (button, hourly cadence, map load).</summary>
        private static int totalArtScans;

        /// <summary>Snapshot of <see cref="totalArtScans"/> at the start of the settings page's own Draw call.</summary>
        private static int drawEntryArtScans;

        internal static void NoteMapArtScanned()
        {
            totalArtScans++;
        }

        internal static void BeginSettingsDrawScanSnapshot()
        {
            drawEntryArtScans = totalArtScans;
        }

        /// <summary>
        /// Fires after the settings page's Draw returns. Scan runs once per map synchronously
        /// inside the Rescan button's click handler, so the snapshot delta is exactly the map count
        /// this press scanned, unaffected by hourly or map-load scans.
        /// </summary>
        internal static void AnnounceRescanIfBatched()
        {
            int scanned = totalArtScans - drawEntryArtScans;
            if (scanned > 0)
            {
                TolkHelper.SpeakData(scanned == 1
                    ? "RimWorldAccess.Compat.Literature.RescanRequestedOne".Translate(scanned)
                    : "RimWorldAccess.Compat.Literature.RescanRequestedMany".Translate(scanned));
            }
        }
    }
}
