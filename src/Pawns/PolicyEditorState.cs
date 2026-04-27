using System.Linq;
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

        // Cached global filters matching vanilla's Dialog_ManageApparelPolicies/Dialog_ManageFoodPolicies
        private static ThingFilter apparelGlobalFilter = null;
        private static ThingFilter foodGlobalFilter = null;

        private static ThingFilter ApparelGlobalFilter
        {
            get
            {
                if (apparelGlobalFilter == null)
                {
                    apparelGlobalFilter = new ThingFilter();
                    apparelGlobalFilter.SetAllow(ThingCategoryDefOf.Apparel, true);
                }
                return apparelGlobalFilter;
            }
        }

        private static ThingFilter FoodGlobalFilter
        {
            get
            {
                if (foodGlobalFilter == null)
                {
                    foodGlobalFilter = new ThingFilter();
                    foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs.Where(
                        x => x.GetStatValueAbstract(StatDefOf.Nutrition) > 0f))
                    {
                        foodGlobalFilter.SetAllow(def, true);
                    }
                }
                return foodGlobalFilter;
            }
        }

        private static void OpenApparelEditor(ApparelPolicy policy)
        {
            // Apparel global filter: matches vanilla Dialog_ManageApparelPolicies
            TreeNode_ThingCategory rootNode = ApparelGlobalFilter.DisplayRootCategory;

            TolkHelper.Speak("RimWorldAccess.Pawns.Policy.EditAnnouncement".Translate("AssignTabEdit".Translate(), policy.label));
            ThingFilterNavigationState.Activate(policy.filter, ApparelGlobalFilter, rootNode, showQuality: true, showHitPoints: true);
        }

        private static void OpenFoodEditor(FoodPolicy policy)
        {
            // Food global filter: matches vanilla Dialog_ManageFoodPolicies
            TreeNode_ThingCategory rootNode = ThingCategoryDefOf.Foods.treeNode;

            TolkHelper.Speak("RimWorldAccess.Pawns.Policy.EditAnnouncement".Translate("AssignTabEdit".Translate(), policy.label));
            ThingFilterNavigationState.Activate(policy.filter, FoodGlobalFilter, rootNode, showQuality: false, showHitPoints: false);
        }

        private static void OpenDrugEditor(DrugPolicy policy)
        {
            TolkHelper.Speak("RimWorldAccess.Pawns.Policy.EditAnnouncement".Translate("AssignTabEdit".Translate(), policy.label));
            DrugPolicyEditorState.Open(policy, null);
        }

        private static void OpenReadingEditor(ReadingPolicy policy)
        {
            TolkHelper.Speak("RimWorldAccess.Pawns.Policy.EditAnnouncement".Translate("AssignTabEdit".Translate(), policy.label));
            ReadingPolicyEditorState.Open(policy, null);
        }
    }
}
