using Verse;
using RimWorld;

namespace RimWorldAccess
{
    public static class PolicyEditorState
    {
        public enum PolicyType
        {
            Apparel,
            Food,
            Drug,
            Reading
        }

        private static bool isActive = false;
        private static PolicyType currentType;
        private static Policy currentPolicy = null;
        private static System.Action onCloseCallback = null;

        public static bool IsActive => isActive;
        public static PolicyType CurrentType => currentType;
        public static Policy CurrentPolicy => currentPolicy;

        /// <summary>
        /// Opens the content editor for the given policy, dispatching to the appropriate sub-editor.
        /// </summary>
        public static void Open(PolicyType type, Policy policy, System.Action onClose)
        {
            if (policy == null) return;

            isActive = true;
            currentType = type;
            currentPolicy = policy;
            onCloseCallback = onClose;

            switch (type)
            {
                case PolicyType.Apparel:
                    OpenApparelEditor((ApparelPolicy)policy);
                    break;
                case PolicyType.Food:
                    OpenFoodEditor((FoodPolicy)policy);
                    break;
                case PolicyType.Drug:
                    OpenDrugEditor((DrugPolicy)policy);
                    break;
                case PolicyType.Reading:
                    OpenReadingEditor((ReadingPolicy)policy);
                    break;
            }
        }

        /// <summary>
        /// Closes the content editor and returns to the caller.
        /// </summary>
        public static void Close()
        {
            // Deactivate whichever sub-editor is active
            if (ThingFilterNavigationState.IsActive)
                ThingFilterNavigationState.Deactivate();
            if (DrugPolicyEditorState.IsActive)
                DrugPolicyEditorState.Close();
            if (ReadingPolicyEditorState.IsActive)
                ReadingPolicyEditorState.Close();

            isActive = false;
            currentPolicy = null;

            var callback = onCloseCallback;
            onCloseCallback = null;
            callback?.Invoke();
        }

        /// <summary>
        /// Gets the translated title for the current policy type.
        /// </summary>
        public static string GetTitle()
        {
            switch (currentType)
            {
                case PolicyType.Apparel: return "ApparelPolicyTitle".Translate();
                case PolicyType.Food: return "FoodPolicyTitle".Translate();
                case PolicyType.Drug: return "DrugPolicyTitle".Translate();
                case PolicyType.Reading: return "ReadingPolicyTitle".Translate();
                default: return "";
            }
        }

        private static void OpenApparelEditor(ApparelPolicy policy)
        {
            // Apparel global filter: all apparel items
            // Matches vanilla Dialog_ManageApparelPolicies: shows quality and hit points
            ThingFilter globalFilter = new ThingFilter();
            globalFilter.SetAllow(ThingCategoryDefOf.Apparel, true);
            TreeNode_ThingCategory rootNode = globalFilter.DisplayRootCategory;

            TolkHelper.Speak($"{"AssignTabEdit".Translate()} {policy.label}");
            ThingFilterNavigationState.Activate(policy.filter, rootNode, showQuality: true, showHitPoints: true);
        }

        private static void OpenFoodEditor(FoodPolicy policy)
        {
            // Food uses Foods category tree as root, no hit points
            // Matches vanilla Dialog_ManageFoodPolicies: forceHideHitPointsConfig: true
            TreeNode_ThingCategory rootNode = ThingCategoryDefOf.Foods.treeNode;

            TolkHelper.Speak($"{"AssignTabEdit".Translate()} {policy.label}");
            ThingFilterNavigationState.Activate(policy.filter, rootNode, showQuality: false, showHitPoints: false);
        }

        private static void OpenDrugEditor(DrugPolicy policy)
        {
            TolkHelper.Speak($"{"AssignTabEdit".Translate()} {policy.label}");
            DrugPolicyEditorState.Open(policy, null);
        }

        private static void OpenReadingEditor(ReadingPolicy policy)
        {
            TolkHelper.Speak($"{"AssignTabEdit".Translate()} {policy.label}");
            ReadingPolicyEditorState.Open(policy, null);
        }
    }
}
