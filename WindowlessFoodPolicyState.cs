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

            ClipboardHelper.CopyToClipboard("Food policy manager closed");
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

            selectedPolicyIndex = (selectedPolicyIndex + 1) % allPolicies.Count;
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

            selectedPolicyIndex--;
            if (selectedPolicyIndex < 0)
                selectedPolicyIndex = allPolicies.Count - 1;

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
            selectedActionIndex = (selectedActionIndex + 1) % policyActions.Length;
            UpdateClipboard();
        }

        /// <summary>
        /// Moves to the previous action in the actions menu.
        /// </summary>
        public static void SelectPreviousAction()
        {
            selectedActionIndex--;
            if (selectedActionIndex < 0)
                selectedActionIndex = policyActions.Length - 1;
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
                ClipboardHelper.CopyToClipboard($"Created new food policy: {newPolicy.label}");
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
                ClipboardHelper.CopyToClipboard($"Rename policy: {selectedPolicy.label}. Enter new name and press Enter.");
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
                ClipboardHelper.CopyToClipboard($"Duplicated policy: {newPolicy.label}");
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

                    ClipboardHelper.CopyToClipboard($"Deleted policy: {deletedName}");
                }
                else
                {
                    ClipboardHelper.CopyToClipboard($"Cannot delete: {result.Reason}");
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
                ClipboardHelper.CopyToClipboard($"Set {selectedPolicy.label} as default food policy");
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

                ClipboardHelper.CopyToClipboard($"Editing filter for {selectedPolicy.label}. Use arrows to navigate, Space to toggle, Enter to expand/collapse categories.");
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
                    ClipboardHelper.CopyToClipboard($"Food policy {selectedPolicyIndex + 1}/{allPolicies.Count}: {selectedPolicy.label}{defaultMarker}. Press Tab for actions.");
                }
                else
                {
                    ClipboardHelper.CopyToClipboard("No food policies available. Press Tab to create one.");
                }
            }
            else if (currentMode == NavigationMode.PolicyActions)
            {
                string action = policyActions[selectedActionIndex];
                ClipboardHelper.CopyToClipboard($"Action {selectedActionIndex + 1}/{policyActions.Length}: {action}. Press Enter to execute, Tab/Shift+Tab or arrows to navigate, Escape to return to policy list.");
            }
        }
    }
}
