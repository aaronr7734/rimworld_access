using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the state for the windowless food policy management interface.
    /// Provides keyboard navigation for creating, editing, and managing food policies.
    /// </summary>
    public static class WindowlessFoodPolicyState
    {
        private static bool isActive = false;
        private static FoodPolicy selectedPolicy = null;
        private static int selectedPolicyIndex = 0;
        private static List<FoodPolicy> allPolicies = new List<FoodPolicy>();

        // Navigation state
        public enum NavigationMode
        {
            PolicyList,      // Navigating the list of policies
            PolicyActions,   // Selecting actions (New, Rename, Delete, etc.)
            FilterEdit       // Editing the filter configuration
        }

        private static NavigationMode currentMode = NavigationMode.PolicyList;
        private static int selectedActionIndex = 0;

        // Available actions
        private static readonly string[] policyActions = new string[]
        {
            "New Policy",
            "Rename Policy",
            "Duplicate Policy",
            "Delete Policy",
            "Set as Default",
            "Edit Filter",
            "Close"
        };

        public static bool IsActive => isActive;
        public static FoodPolicy SelectedPolicy => selectedPolicy;
        public static NavigationMode CurrentMode => currentMode;

        /// <summary>
        /// Opens the food policy management interface.
        /// </summary>
        public static void Open(FoodPolicy initialPolicy = null)
        {
            isActive = true;
            currentMode = NavigationMode.PolicyList;
            selectedActionIndex = 0;

            LoadPolicies();

            // Select the initial policy if provided
            if (initialPolicy != null && allPolicies.Contains(initialPolicy))
            {
                selectedPolicyIndex = allPolicies.IndexOf(initialPolicy);
                selectedPolicy = initialPolicy;
            }
            else if (allPolicies.Count > 0)
            {
                selectedPolicyIndex = 0;
                selectedPolicy = allPolicies[0];
            }

            UpdateClipboard();
        }

        /// <summary>
        /// Closes the food policy management interface.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            selectedPolicy = null;
            selectedPolicyIndex = 0;
            allPolicies.Clear();
            currentMode = NavigationMode.PolicyList;

            TolkHelper.Speak("Food policy manager closed");
        }

        /// <summary>
        /// Loads all food policies from the game database.
        /// </summary>
        private static void LoadPolicies()
        {
            allPolicies.Clear();
            if (Current.Game?.foodRestrictionDatabase != null)
            {
                allPolicies = Current.Game.foodRestrictionDatabase.AllFoodRestrictions.ToList();
            }
        }

        /// <summary>
        /// Moves selection to the next policy in the list.
        /// </summary>
        public static void SelectNextPolicy()
        {
            if (allPolicies.Count == 0)
                return;

            selectedPolicyIndex = MenuHelper.SelectNext(selectedPolicyIndex, allPolicies.Count);
            selectedPolicy = allPolicies[selectedPolicyIndex];
            UpdateClipboard();
        }

        /// <summary>
        /// Moves selection to the previous policy in the list.
        /// </summary>
        public static void SelectPreviousPolicy()
        {
            if (allPolicies.Count == 0)
                return;

            selectedPolicyIndex = MenuHelper.SelectPrevious(selectedPolicyIndex, allPolicies.Count);
            selectedPolicy = allPolicies[selectedPolicyIndex];
            UpdateClipboard();
        }

        /// <summary>
        /// Switches from policy list to actions mode.
        /// </summary>
        public static void EnterActionsMode()
        {
            if (currentMode == NavigationMode.PolicyList)
            {
                currentMode = NavigationMode.PolicyActions;
                selectedActionIndex = 0;
                UpdateClipboard();
            }
        }

        /// <summary>
        /// Returns to policy list mode from actions or filter mode.
        /// </summary>
        public static void ReturnToPolicyList()
        {
            currentMode = NavigationMode.PolicyList;
            UpdateClipboard();
        }

        /// <summary>
        /// Moves to the next action in the actions menu.
        /// </summary>
        public static void SelectNextAction()
        {
            selectedActionIndex = MenuHelper.SelectNext(selectedActionIndex, policyActions.Length);
            UpdateClipboard();
        }

        /// <summary>
        /// Moves to the previous action in the actions menu.
        /// </summary>
        public static void SelectPreviousAction()
        {
            selectedActionIndex = MenuHelper.SelectPrevious(selectedActionIndex, policyActions.Length);
            UpdateClipboard();
        }

        /// <summary>
        /// Executes the currently selected action.
        /// </summary>
        public static void ExecuteAction()
        {
            if (currentMode == NavigationMode.PolicyActions)
            {
                string action = policyActions[selectedActionIndex];

                switch (action)
                {
                    case "New Policy":
                        CreateNewPolicy();
                        break;
                    case "Rename Policy":
                        RenamePolicy();
                        break;
                    case "Duplicate Policy":
                        DuplicatePolicy();
                        break;
                    case "Delete Policy":
                        DeletePolicy();
                        break;
                    case "Set as Default":
                        SetAsDefault();
                        break;
                    case "Edit Filter":
                        EditFilter();
                        break;
                    case "Close":
                        Close();
                        break;
                }
            }
        }

        /// <summary>
        /// Creates a new food policy.
        /// </summary>
        private static void CreateNewPolicy()
        {
            if (Current.Game?.foodRestrictionDatabase != null)
            {
                FoodPolicy newPolicy = Current.Game.foodRestrictionDatabase.MakeNewFoodRestriction();
                LoadPolicies();
                selectedPolicyIndex = allPolicies.IndexOf(newPolicy);
                selectedPolicy = newPolicy;
                TolkHelper.Speak($"Created new food policy: {newPolicy.label}");
            }
        }

        /// <summary>
        /// Opens the rename dialog for the selected policy.
        /// </summary>
        private static void RenamePolicy()
        {
            if (selectedPolicy != null)
            {
                Find.WindowStack.Add(new Dialog_RenamePolicy(selectedPolicy));
                TolkHelper.Speak($"Rename policy: {selectedPolicy.label}. Enter new name and press Enter.");
            }
        }

        /// <summary>
        /// Duplicates the selected policy.
        /// </summary>
        private static void DuplicatePolicy()
        {
            if (selectedPolicy != null && Current.Game?.foodRestrictionDatabase != null)
            {
                FoodPolicy newPolicy = Current.Game.foodRestrictionDatabase.MakeNewFoodRestriction();
                newPolicy.label = selectedPolicy.label + " (copy)";
                newPolicy.CopyFrom(selectedPolicy);
                LoadPolicies();
                selectedPolicyIndex = allPolicies.IndexOf(newPolicy);
                selectedPolicy = newPolicy;
                TolkHelper.Speak($"Duplicated policy: {newPolicy.label}");
            }
        }

        /// <summary>
        /// Deletes the selected policy with confirmation.
        /// </summary>
        private static void DeletePolicy()
        {
            if (selectedPolicy == null)
                return;

            if (Current.Game?.foodRestrictionDatabase != null)
            {
                AcceptanceReport result = Current.Game.foodRestrictionDatabase.TryDelete(selectedPolicy);
                if (result.Accepted)
                {
                    string deletedName = selectedPolicy.label;
                    LoadPolicies();

                    // Select another policy
                    if (allPolicies.Count > 0)
                    {
                        selectedPolicyIndex = 0;
                        selectedPolicy = allPolicies[0];
                    }
                    else
                    {
                        selectedPolicy = null;
                        selectedPolicyIndex = 0;
                    }

                    TolkHelper.Speak($"Deleted policy: {deletedName}");
                }
                else
                {
                    TolkHelper.Speak($"Cannot delete: {result.Reason}", SpeechPriority.High);
                }
            }
        }

        /// <summary>
        /// Sets the selected policy as the default.
        /// </summary>
        private static void SetAsDefault()
        {
            if (selectedPolicy != null && Current.Game?.foodRestrictionDatabase != null)
            {
                Current.Game.foodRestrictionDatabase.SetDefault(selectedPolicy);
                TolkHelper.Speak($"Set {selectedPolicy.label} as default food policy");
            }
        }

        /// <summary>
        /// Opens the filter editor (keyboard accessible).
        /// </summary>
        private static void EditFilter()
        {
            if (selectedPolicy != null)
            {
                currentMode = NavigationMode.FilterEdit;

                // Use the Foods category tree as the root - this shows only actual food items
                TreeNode_ThingCategory rootNode = ThingCategoryDefOf.Foods.treeNode;

                // Activate filter navigation - no quality or hitpoints for food
                ThingFilterNavigationState.Activate(selectedPolicy.filter, rootNode, showQuality: false, showHitPoints: false);

                TolkHelper.Speak($"Editing filter for {selectedPolicy.label}. Use arrows to navigate, Space to toggle, Enter to expand/collapse categories.");
            }
        }

        /// <summary>
        /// Updates the clipboard with the current selection.
        /// </summary>
        private static void UpdateClipboard()
        {
            if (currentMode == NavigationMode.PolicyList)
            {
                if (selectedPolicy != null)
                {
                    bool isDefault = Current.Game?.foodRestrictionDatabase?.DefaultFoodRestriction() == selectedPolicy;
                    string defaultMarker = isDefault ? " (default)" : "";
                    TolkHelper.Speak($"{selectedPolicy.label}{defaultMarker}. {MenuHelper.FormatPosition(selectedPolicyIndex, allPolicies.Count)}. Press Tab for actions.");
                }
                else
                {
                    TolkHelper.Speak("No food policies available. Press Tab to create one.");
                }
            }
            else if (currentMode == NavigationMode.PolicyActions)
            {
                string action = policyActions[selectedActionIndex];
                TolkHelper.Speak($"{action}. {MenuHelper.FormatPosition(selectedActionIndex, policyActions.Length)}. Press Enter to execute, Tab/Shift+Tab or arrows to navigate, Escape to return to policy list.");
            }
        }
    }
}
