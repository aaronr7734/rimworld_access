using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Defines how a tab should be handled for keyboard navigation.
    /// </summary>
    public enum TabHandlerType
    {
        /// <summary>Rich navigation with expandable sub-items (Health, Gear, Skills, etc.)</summary>
        RichNavigation,
        /// <summary>Opens a separate action menu (Bills, Storage, Prisoner, etc.)</summary>
        Action,
        /// <summary>Basic display using GetInspectString() for unknown tabs</summary>
        BasicInspectString
    }

    /// <summary>
    /// Contains information about a tab category for building the inspection tree.
    /// </summary>
    public class TabCategoryInfo
    {
        /// <summary>Display name of the category</summary>
        public string Name { get; set; }

        /// <summary>The underlying RimWorld tab (null for synthetic categories like Overview)</summary>
        public InspectTabBase Tab { get; set; }

        /// <summary>How this tab should be handled</summary>
        public TabHandlerType Handler { get; set; }

        /// <summary>Whether this is a known tab with rich support (vs fallback)</summary>
        public bool IsKnown { get; set; }

        /// <summary>The original category name used by existing helpers (for mapping)</summary>
        public string OriginalCategoryName { get; set; }
    }

    /// <summary>
    /// Enumerates an inspected object's visible tabs as TabCategoryInfo rows,
    /// resolving each tab through <see cref="InspectNodeRegistry"/> (rework
    /// §B.3 item 2 — the type-name string dictionaries and their BaseType.Name
    /// walks are gone). Unknown tabs — typically from mods — fall back to a
    /// GetInspectString presentation so they stay navigable.
    /// </summary>
    public static class TabRegistry
    {
        private sealed class ReachableHiddenTab
        {
            public Func<bool> Reachable;
            public Func<string> DisplayName;
        }

        private static readonly Dictionary<Type, ReachableHiddenTab> reachableHiddenTabs = new Dictionary<Type, ReachableHiddenTab>();

        /// <summary>Lists a Hidden tab that, like ITab_Genes, opens from a control elsewhere, while <paramref name="reachable"/> holds.</summary>
        public static void RegisterReachableHiddenTab(Type tabType, Func<bool> reachable, Func<string> displayName)
        {
            if (tabType != null && reachable != null && displayName != null)
            {
                reachableHiddenTabs[tabType] = new ReachableHiddenTab { Reachable = reachable, DisplayName = displayName };
            }
        }

        private static bool HiddenTabReachable(InspectTabBase tab)
        {
            ReachableHiddenTab entry;
            return reachableHiddenTabs.TryGetValue(tab.GetType(), out entry) && entry.Reachable();
        }

        /// <summary>
        /// Gets the user-friendly (translated) display name for a tab.
        /// Prefers the game's own labelKey so the user sees the label in their
        /// game language; registered tabs then render their category key via
        /// InspectionCategoryLocalizer; the last resort is the cleaned type name.
        /// </summary>
        public static string GetCategoryNameForTab(InspectTabBase tab)
        {
            if (tab == null)
                return "Unknown"; // l10n-exempt: dispatch token, localized for display via InspectionCategoryLocalizer

            // First priority: translate the tab's own labelKey (so Chinese users see Chinese, etc.)
            if (!string.IsNullOrEmpty(tab.labelKey))
            {
                try
                {
                    string translated = tab.labelKey.Translate().ToString();
                    if (!string.IsNullOrEmpty(translated) && translated != tab.labelKey)
                        return translated;
                }
                catch
                {
                    // Translation failed, fall through to the registry/type-name fallback
                }
            }

            if (InspectNodeRegistry.TryResolve(tab.GetType(), out InspectNodeAdapter adapter))
                return adapter.DisplayName(tab);

            ReachableHiddenTab hidden;
            if (reachableHiddenTabs.TryGetValue(tab.GetType(), out hidden))
                return hidden.DisplayName();

            // Last resort: use type name cleaned up
            return tab.GetType().Name.Replace("ITab_", "").Replace("_", " ");
        }

        /// <summary>
        /// Gets fallback information for a tab using GetInspectString().
        /// </summary>
        public static string GetFallbackInfo(Thing thing, InspectTabBase tab)
        {
            if (thing == null || tab == null)
                return "RimWorldAccess.Inspection.Category.NoInfo".Translate();

            try
            {
                string inspectString = thing.GetInspectString();

                if (!string.IsNullOrEmpty(inspectString))
                    return inspectString;

                string tabName = GetCategoryNameForTab(tab);
                return "RimWorldAccess.Inspection.Tree.TabNoTextContent".Translate(tabName);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error getting fallback info for tab {tab?.GetType()?.Name}: {ex.Message}");
                return "RimWorldAccess.Inspection.Tree.ErrorRetrievingInfo".Translate();
            }
        }

        /// <summary>
        /// Gets all visible tabs for a thing as TabCategoryInfo objects.
        /// </summary>
        public static List<TabCategoryInfo> GetTabCategories(Thing thing)
        {
            if (thing == null)
                return new List<TabCategoryInfo>();
            return BuildCategories(GetTabsSafe(thing), thing.LabelCap);
        }

        /// <summary>
        /// Gets all visible tabs for a zone as TabCategoryInfo objects.
        /// Zones have their own GetInspectTabs() implementation separate from Things.
        /// </summary>
        public static List<TabCategoryInfo> GetZoneTabCategories(Zone zone)
        {
            if (zone == null)
                return new List<TabCategoryInfo>();
            return BuildCategories(GetTabsSafe(zone), zone.label);
        }

        private static IEnumerable<InspectTabBase> GetTabsSafe(Thing thing)
        {
            try
            {
                return thing.GetInspectTabs();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error getting inspect tabs for {thing.LabelCap}: {ex.Message}");
                return null;
            }
        }

        private static IEnumerable<InspectTabBase> GetTabsSafe(Zone zone)
        {
            try
            {
                return zone.GetInspectTabs();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error getting inspect tabs for zone {zone.label}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Shared Thing/Zone enumeration: filters to visible tabs and resolves
        /// each through the adapter registry, falling back to a basic
        /// GetInspectString presentation for unregistered (mod) tabs.
        /// </summary>
        private static List<TabCategoryInfo> BuildCategories(IEnumerable<InspectTabBase> tabs, string ownerLabel)
        {
            var categories = new List<TabCategoryInfo>();
            if (tabs == null)
                return categories;

            // GetInspectTabs enumerates lazily, so a throw can surface
            // mid-iteration; keep whatever was gathered before it.
            var tabList = new List<InspectTabBase>();
            try
            {
                foreach (var tab in tabs)
                    tabList.Add(tab);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error getting tab categories for {ownerLabel}: {ex.Message}");
            }

            foreach (var tab in tabList)
            {
                if (tab == null)
                    continue;

                try
                {
                    // Skip hidden or invisible tabs (but allow ITab_Genes which is hidden in vanilla UI).
                    // Exact type match preserves the historical behavior: ITab_GenesPregnancy
                    // subclasses ITab_Genes but is not exempted from the Hidden check here.
                    bool isGeneTab = tab.GetType() == typeof(ITab_Genes);
                    if (!tab.IsVisible || (tab.Hidden && !isGeneTab && !HiddenTabReachable(tab)))
                        continue;

                    if (InspectNodeRegistry.TryResolve(tab.GetType(), out InspectNodeAdapter adapter))
                    {
                        categories.Add(new TabCategoryInfo
                        {
                            Name = adapter.DisplayName(tab),
                            Tab = tab,
                            Handler = adapter.Handler,
                            IsKnown = true,
                            OriginalCategoryName = adapter.CategoryKey
                        });
                    }
                    else
                    {
                        // Unknown (typically modded) tab: show its translated label
                        // and read its owner's inspect string.
                        string displayName = GetCategoryNameForTab(tab);
                        categories.Add(new TabCategoryInfo
                        {
                            Name = displayName,
                            Tab = tab,
                            Handler = TabHandlerType.BasicInspectString,
                            IsKnown = false,
                            OriginalCategoryName = displayName
                        });
                    }
                }
                catch (Exception tabEx)
                {
                    // Log per-tab error but continue processing other tabs
                    Log.Warning($"[RimWorld Access] Error processing tab {tab.GetType().Name} on {ownerLabel}: {tabEx.Message}");
                }
            }

            return categories;
        }
    }
}
