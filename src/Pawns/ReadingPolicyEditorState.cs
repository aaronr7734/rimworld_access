using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    public static class ReadingPolicyEditorState
    {
        public enum Panel
        {
            BookTypes,
            BookEffects
        }

        private static bool isActive = false;
        private static ReadingPolicy policy = null;
        private static Panel currentPanel = Panel.BookTypes;
        private static System.Action onCloseCallback = null;

        // Saved positions for each panel (preserved across Tab switches)
        private static int savedBookTypesIndex = 0;
        private static int savedBookEffectsIndex = 0;

        // Cached global filter for book types (same approach as vanilla Dialog_ManageReadingPolicies)
        private static ThingFilter bookTypesGlobalFilter = null;

        public static bool IsActive => isActive;
        public static Panel CurrentPanel => currentPanel;

        private static ThingFilter BookTypesGlobalFilter
        {
            get
            {
                if (bookTypesGlobalFilter == null)
                {
                    bookTypesGlobalFilter = new ThingFilter();
                    foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs.Where(x => x.HasComp<CompBook>()))
                    {
                        bookTypesGlobalFilter.SetAllow(def, true);
                    }
                }
                return bookTypesGlobalFilter;
            }
        }

        public static void Open(ReadingPolicy readingPolicy, System.Action onClose)
        {
            isActive = true;
            policy = readingPolicy;
            currentPanel = Panel.BookTypes;
            onCloseCallback = onClose;
            savedBookTypesIndex = 0;
            savedBookEffectsIndex = 0;

            // Announce panel name before ThingFilter announces first item
            TolkHelper.SpeakData(GetCurrentPanelName());
            ActivateCurrentPanel(0);
        }

        public static void Close()
        {
            if (ThingFilterNavigationState.IsActive)
                ThingFilterNavigationState.Deactivate();

            isActive = false;
            policy = null;
            currentPanel = Panel.BookTypes;
            savedBookTypesIndex = 0;
            savedBookEffectsIndex = 0;

            var callback = onCloseCallback;
            onCloseCallback = null;
            callback?.Invoke();
        }

        public static void SwitchPanel()
        {
            if (policy == null) return;

            // Save current panel position
            SaveCurrentPanelPosition();

            // Switch panel
            if (currentPanel == Panel.BookTypes)
            {
                currentPanel = Panel.BookEffects;
                TolkHelper.SpeakData(GetCurrentPanelName());
                ActivateCurrentPanel(savedBookEffectsIndex);
            }
            else
            {
                currentPanel = Panel.BookTypes;
                TolkHelper.SpeakData(GetCurrentPanelName());
                ActivateCurrentPanel(savedBookTypesIndex);
            }
        }

        private static void SaveCurrentPanelPosition()
        {
            if (!ThingFilterNavigationState.IsActive) return;

            int currentIndex = ThingFilterNavigationState.GetCurrentIndex();
            if (currentPanel == Panel.BookTypes)
                savedBookTypesIndex = currentIndex;
            else
                savedBookEffectsIndex = currentIndex;
        }

        private static void ActivateCurrentPanel(int initialIndex)
        {
            // Deactivate previous filter if active
            if (ThingFilterNavigationState.IsActive)
                ThingFilterNavigationState.Deactivate();

            if (currentPanel == Panel.BookTypes)
            {
                // Book types panel: defFilter with global filter, no hit points, no quality
                // Matches vanilla: forceHideHitPointsConfig: true
                TreeNode_ThingCategory root = BookTypesGlobalFilter.DisplayRootCategory;
                ThingFilterNavigationState.Activate(policy.defFilter, BookTypesGlobalFilter, root, showQuality: false, showHitPoints: false, initialIndex);
            }
            else
            {
                // Book effects panel: effectFilter with BookEffects root, no hit points, no quality
                // Matches vanilla: forceHideHitPointsConfig: true, forceHideQualityConfig: true
                TreeNode_ThingCategory root = ThingCategoryDefOf.BookEffects.treeNode;
                ThingFilterNavigationState.Activate(policy.effectFilter, null, root, showQuality: false, showHitPoints: false, initialIndex);
            }
        }

        /// <summary>
        /// Gets the current panel display name using game category labels.
        /// </summary>
        public static string GetCurrentPanelName()
        {
            if (currentPanel == Panel.BookTypes)
            {
                // Use the root category label from the book types global filter
                var root = BookTypesGlobalFilter.DisplayRootCategory;
                return root?.catDef?.LabelCap
                    ?? DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Books")?.LabelCap.Resolve()
                    ?? "";
            }
            // Use the BookEffects ThingCategoryDef label
            return ThingCategoryDefOf.BookEffects.LabelCap;
        }
    }
}
