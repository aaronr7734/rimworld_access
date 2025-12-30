using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Intercepts dialog windows and replaces them with windowless keyboard-accessible versions.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), "Add")]
    public static class DialogInterceptionPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Window window)
        {
            // Skip if no window
            if (window == null)
                return true;

            // Check if this dialog should be intercepted
            if (!ShouldInterceptDialog(window))
                return true; // Allow normal behavior

            Log.Message($"RimWorld Access: Intercepting dialog {window.GetType().Name}");

            // Open windowless version
            WindowlessDialogState.Open(window);

            // Prevent the dialog from being added to the window stack
            return false;
        }

        private static bool ShouldInterceptDialog(Window window)
        {
            System.Type windowType = window.GetType();
            string typeName = windowType.Name;

            // Exclude Dialog_Trade - it's already working with keyboard navigation
            if (typeName == "Dialog_Trade")
                return false;

            // Exclude specific windows that don't need interception
            // These are complex UI windows that need custom handling
            if (typeName == "Dialog_BillConfig" ||
                typeName == "Dialog_MechBillConfig" ||
                typeName == "Dialog_FormCaravan" ||
                typeName == "Dialog_InfoCard" ||
                typeName == "Dialog_ManageDrugPolicies" ||
                typeName == "Dialog_ManageApparelPolicies" ||
                typeName == "Dialog_ManageFoodPolicies" ||
                typeName == "Dialog_ManageReadingPolicies" ||
                typeName == "Dialog_AutoSlaughter")
            {
                return false;
            }

            // Intercept Dialog_MessageBox (confirmations, warnings)
            if (window is Dialog_MessageBox)
                return true;

            // Intercept Dialog_NodeTree and all subclasses (quest dialogs, research completion, etc.)
            // Walk up the type hierarchy to check for Dialog_NodeTree in the inheritance chain
            if (IsDialogNodeTreeOrSubclass(windowType))
                return true;

            // Intercept all Dialog_Rename subclasses (zone rename, area rename, etc.)
            if (windowType.BaseType != null && windowType.BaseType.Name.StartsWith("Dialog_Rename"))
                return true;

            // Intercept all Dialog_GiveName subclasses (colony name, settlement name, etc.)
            if (windowType.BaseType != null && windowType.BaseType.Name == "Dialog_GiveName")
                return true;

            // Intercept Dialog_GiveName itself
            if (typeName == "Dialog_GiveName")
                return true;

            // Don't intercept other windows by default
            return false;
        }

        /// <summary>
        /// Checks if the given type is Dialog_NodeTree or a subclass of it.
        /// Walks up the entire type hierarchy to catch indirect subclasses.
        /// </summary>
        private static bool IsDialogNodeTreeOrSubclass(System.Type type)
        {
            if (type == null)
                return false;

            // Check current type name
            if (type.Name == "Dialog_NodeTree")
                return true;

            // Walk up the inheritance chain
            System.Type currentType = type.BaseType;
            while (currentType != null)
            {
                if (currentType.Name == "Dialog_NodeTree")
                    return true;
                currentType = currentType.BaseType;
            }

            return false;
        }
    }

    /// <summary>
    /// Handles keyboard input for windowless dialogs at the UIRoot level.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
    public static class WindowlessDialogInputPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.VeryHigh)]
        public static bool Prefix()
        {
            // Only process if windowless dialog is active
            if (!WindowlessDialogState.IsActive)
                return true;

            // Handle keyboard input
            Event current = Event.current;
            if (current.type == EventType.KeyDown)
            {
                bool consumed = WindowlessDialogState.HandleInput(current);
                if (consumed)
                {
                    // Prevent other systems from processing this event
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Draw visual overlay for windowless dialogs to show the user what's happening.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
    public static class WindowlessDialogDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!WindowlessDialogState.IsActive)
                return;

            // Draw a simple overlay to indicate keyboard mode is active
            Rect overlayRect = new Rect(10f, 10f, 300f, 60f);

            // Background
            Color prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(overlayRect, BaseContent.WhiteTex);

            // Text
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            string overlayText = "Keyboard Dialog Mode Active\nUse arrow keys to navigate";
            Widgets.Label(overlayRect, overlayText);

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = prevColor;
        }
    }

    /// <summary>
    /// Patches WindowStack.WindowsForcePause to account for windowless dialogs.
    /// This fixes a bug where the game would repeatedly try to open automatic letters
    /// because it didn't know a forcePause dialog was active (since we intercept and
    /// don't add them to the window stack).
    /// </summary>
    [HarmonyPatch(typeof(WindowStack))]
    [HarmonyPatch("WindowsForcePause", MethodType.Getter)]
    public static class WindowsForcePausePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            // If our windowless dialog system has an active forcePause dialog,
            // report that the windows force pause even though the dialog isn't
            // in the actual window stack
            if (WindowlessDialogState.ShouldForcePause)
            {
                __result = true;
            }
        }
    }
}
