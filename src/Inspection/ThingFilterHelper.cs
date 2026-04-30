using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared utilities for ThingFilter navigation across all three implementations
    /// (ThingFilterNavigationState, ThingFilterMenuState, StorageSettingsMenuState).
    /// Provides tri-state allowance logic, visibility checks, and summary formatting.
    /// </summary>
    public static class ThingFilterHelper
    {
        /// <summary>
        /// Tri-state allowance matching vanilla's MultiCheckboxState behavior.
        /// </summary>
        public enum CategoryAllowanceState
        {
            NoneAllowed,
            SomeAllowed,
            AllAllowed
        }

        /// <summary>
        /// Detailed summary of a category's allowance state for smart announcements.
        /// </summary>
        public struct CategorySummary
        {
            public CategoryAllowanceState State;
            public int TotalVisible;
            public int AllowedCount;
            public int DisallowedCount;
            public List<string> AllowedNames;
            public List<string> DisallowedNames;
        }

        /// <summary>
        /// Computes the tri-state allowance for a category, matching vanilla's AllowanceStateOf().
        /// Only counts visible ThingDefs (not special filters) for the state determination.
        /// </summary>
        public static CategoryAllowanceState GetAllowanceState(
            ThingCategoryDef catDef, ThingFilter filter, Func<ThingDef, bool> isVisible)
        {
            int visibleCount = 0;
            int allowedCount = 0;

            foreach (ThingDef td in catDef.DescendantThingDefs)
            {
                if (isVisible(td))
                {
                    visibleCount++;
                    if (filter.Allows(td))
                        allowedCount++;
                }
            }

            if (allowedCount == 0)
                return CategoryAllowanceState.NoneAllowed;
            if (allowedCount == visibleCount)
                return CategoryAllowanceState.AllAllowed;
            return CategoryAllowanceState.SomeAllowed;
        }

        /// <summary>
        /// Computes detailed category summary including exception names for announcements.
        /// Collects up to maxNames+1 names for each side to determine whether to list or count.
        /// </summary>
        public static CategorySummary GetCategorySummary(
            ThingCategoryDef catDef, ThingFilter filter, Func<ThingDef, bool> isVisible,
            int maxNames = 10)
        {
            var summary = new CategorySummary
            {
                AllowedNames = new List<string>(),
                DisallowedNames = new List<string>()
            };

            foreach (ThingDef td in catDef.DescendantThingDefs)
            {
                if (!isVisible(td))
                    continue;

                summary.TotalVisible++;

                if (filter.Allows(td))
                {
                    summary.AllowedCount++;
                    if (summary.AllowedNames.Count < maxNames)
                        summary.AllowedNames.Add(td.LabelCap);
                }
                else
                {
                    summary.DisallowedCount++;
                    if (summary.DisallowedNames.Count < maxNames)
                        summary.DisallowedNames.Add(td.LabelCap);
                }
            }

            if (summary.AllowedCount == 0)
                summary.State = CategoryAllowanceState.NoneAllowed;
            else if (summary.DisallowedCount == 0)
                summary.State = CategoryAllowanceState.AllAllowed;
            else
                summary.State = CategoryAllowanceState.SomeAllowed;

            return summary;
        }

        /// <summary>
        /// Formats a category summary for screen reader announcement.
        /// Reports from the minority perspective:
        /// - "disallowed, except for: item1, item2" (minority allowed, ≤maxNames)
        /// - "allowed, except for: item1, item2" (minority disallowed, ≤maxNames)
        /// - "disallowed, except for 15 items" (too many to list)
        /// </summary>
        public static string FormatCategorySummary(CategorySummary summary)
        {
            if (summary.AllowedCount == 0)
                return "disallowed";
            if (summary.DisallowedCount == 0)
                return "allowed";

            if (summary.AllowedCount <= summary.DisallowedCount)
            {
                if (summary.AllowedCount <= 10)
                    return "disallowed, except: " + string.Join(", ", summary.AllowedNames);
                else
                    return $"disallowed, except for {summary.AllowedCount} items";
            }
            else
            {
                if (summary.DisallowedCount <= 10)
                    return "allowed, except: " + string.Join(", ", summary.DisallowedNames);
                else
                    return $"allowed, except for {summary.DisallowedCount} items";
            }
        }

        /// <summary>
        /// Vanilla-matching visibility check for ThingDefs.
        /// Mirrors Listing_TreeThingFilter.Visible(ThingDef).
        /// </summary>
        public static bool IsVisible(ThingDef td, ThingFilter parentFilter)
        {
            if (!td.PlayerAcquirable)
                return false;
            if (td.virtualDefParent != null)
                return false;
            if (Find.HiddenItemsManager.Hidden(td))
                return false;
            if (parentFilter != null)
            {
                if (!parentFilter.Allows(td))
                    return false;
                if (parentFilter.IsAlwaysDisallowedDueToSpecialFilters(td))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Checks if a category has any visible descendants.
        /// </summary>
        public static bool IsVisibleCategory(TreeNode_ThingCategory node, ThingFilter parentFilter)
        {
            foreach (ThingDef td in node.catDef.DescendantThingDefs)
            {
                if (IsVisible(td, parentFilter))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a special filter is visible given a parent filter, matching
        /// vanilla's Listing_TreeThingFilter.CalculateHiddenSpecialFilters: a
        /// special filter is hidden when no descendant ThingDef allowed by the
        /// parent filter can ever be matched by its Worker (e.g. "Allow rotten"
        /// on the bionic-eye recipe — there's nothing rottable to apply it to).
        /// </summary>
        /// <summary>
        /// Vanilla-matching visibility check for a special filter against a
        /// containing category and a parent filter, mirroring
        /// `Listing_TreeThingFilter.Visible(SpecialThingFilterDef, TreeNode_ThingCategory)`
        /// + `CalculateHiddenSpecialFilters`. The current filter is also passed
        /// because vanilla short-circuits to "visible" when
        /// `filter.OnlySpecialFilters` is true.
        /// </summary>
        public static bool IsVisibleSpecialFilter(SpecialThingFilterDef f, TreeNode_ThingCategory node,
            ThingFilter currentFilter, ThingFilter parentFilter)
        {
            if (parentFilter != null && !parentFilter.Allows(f))
                return false;
            if (currentFilter != null && currentFilter.OnlySpecialFilters)
                return true;
            if (parentFilter != null && parentFilter.hiddenSpecialFilters != null
                && parentFilter.hiddenSpecialFilters.Contains(f))
                return false;
            if (f.Worker == null || node == null)
                return true;

            // For each descendant ThingDef of the current category, ask the
            // worker if it could ever match. Restrict to defs the parent
            // filter actually allows — same scoping vanilla uses.
            foreach (ThingDef td in node.catDef.DescendantThingDefs)
            {
                if (parentFilter != null && !parentFilter.Allows(td))
                    continue;
                if (f.Worker.CanEverMatch(td))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Backwards-compatible overload. Resolves the category to the parent
        /// filter's DisplayRootCategory (or null when no parent filter).
        /// </summary>
        public static bool IsVisibleSpecialFilter(SpecialThingFilterDef f, ThingFilter parentFilter)
        {
            var node = parentFilter?.DisplayRootCategory;
            return IsVisibleSpecialFilter(f, node, currentFilter: null, parentFilter);
        }
    }
}
