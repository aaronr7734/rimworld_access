using HarmonyLib;
using UnityEngine;
using Verse;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch for UIRoot.UIRootOnGUI to handle keyboard input for FloatMenu navigation.
    /// Follows the pattern used by PawnInfoPatch.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public class FloatMenuInputPatch
    {
        static void Prefix()
        {
            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            // Check if we have an active FloatMenu
            FloatMenu currentMenu = GetCurrentFloatMenu();
            if (currentMenu == null)
            {
                // No menu open, reset state
                if (FloatMenuNavigationState.HasActiveMenu)
                {
                    MelonLoader.MelonLogger.Msg("[FloatMenu DEBUG] No menu found, resetting state");
                    FloatMenuNavigationState.Reset();
                }
                return;
            }

            MelonLoader.MelonLogger.Msg($"[FloatMenu DEBUG] UIRootOnGUI detected key: {Event.current.keyCode}");

            // Register this menu if not already tracked
            if (FloatMenuNavigationState.CurrentMenu == null)
            {
                MelonLoader.MelonLogger.Msg("[FloatMenu DEBUG] Registering new menu in UIRootOnGUI");
                FloatMenuNavigationState.CurrentMenu = currentMenu;
            }

            // Only handle keyboard for the currently tracked menu
            if (FloatMenuNavigationState.CurrentMenu != currentMenu)
            {
                MelonLoader.MelonLogger.Msg("[FloatMenu DEBUG] Switching to different menu");
                FloatMenuNavigationState.CurrentMenu = currentMenu;
            }

            // Get the options list using reflection
            List<FloatMenuOption> options = Traverse.Create(currentMenu).Field("options").GetValue<List<FloatMenuOption>>();
            MelonLoader.MelonLogger.Msg($"[FloatMenu DEBUG] UIRootOnGUI options count: {options?.Count ?? 0}");
            if (options == null || options.Count == 0)
                return;

            // Validate current selection
            FloatMenuNavigationState.ValidateSelection(options.Count, (index) => !options[index].Disabled);

            bool handled = false;
            KeyCode key = Event.current.keyCode;

            // Handle arrow keys for navigation
            if (key == KeyCode.DownArrow)
            {
                MelonLoader.MelonLogger.Msg("[FloatMenu DEBUG] Down arrow detected");
                int newIndex = FloatMenuNavigationState.SelectNext(options.Count, (index) => !options[index].Disabled);
                MelonLoader.MelonLogger.Msg($"[FloatMenu DEBUG] New index after down: {newIndex}");
                if (newIndex >= 0)
                {
                    // Announce the new selection
                    string optionText = options[newIndex].Label;
                    if (options[newIndex].Disabled)
                    {
                        optionText += " (unavailable)";
                    }
                    MelonLoader.MelonLogger.Msg($"[FloatMenu DEBUG] Announcing: {optionText}");
                    ClipboardHelper.CopyToClipboard(optionText);
                    FloatMenuNavigationState.LastAnnouncedText = optionText;
                }
                handled = true;
            }
            else if (key == KeyCode.UpArrow)
            {
                MelonLoader.MelonLogger.Msg("[FloatMenu DEBUG] Up arrow detected");
                int newIndex = FloatMenuNavigationState.SelectPrevious(options.Count, (index) => !options[index].Disabled);
                MelonLoader.MelonLogger.Msg($"[FloatMenu DEBUG] New index after up: {newIndex}");
                if (newIndex >= 0)
                {
                    // Announce the new selection
                    string optionText = options[newIndex].Label;
                    if (options[newIndex].Disabled)
                    {
                        optionText += " (unavailable)";
                    }
                    MelonLoader.MelonLogger.Msg($"[FloatMenu DEBUG] Announcing: {optionText}");
                    ClipboardHelper.CopyToClipboard(optionText);
                    FloatMenuNavigationState.LastAnnouncedText = optionText;
                }
                handled = true;
            }
            // Handle Enter to select current option
            else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                MelonLoader.MelonLogger.Msg("[FloatMenu DEBUG] Enter key detected");
                int selectedIndex = FloatMenuNavigationState.SelectedIndex;
                MelonLoader.MelonLogger.Msg($"[FloatMenu DEBUG] Current selected index: {selectedIndex}");
                if (selectedIndex >= 0 && selectedIndex < options.Count)
                {
                    FloatMenuOption selectedOption = options[selectedIndex];
                    MelonLoader.MelonLogger.Msg($"[FloatMenu DEBUG] Selected option: {selectedOption.Label}, Disabled: {selectedOption.Disabled}");

                    // Only activate if not disabled
                    if (!selectedOption.Disabled)
                    {
                        // Get whether this menu gives colonist orders
                        bool givesColonistOrders = Traverse.Create(currentMenu).Field("givesColonistOrders").GetValue<bool>();

                        MelonLoader.MelonLogger.Msg($"[FloatMenu DEBUG] Calling Chosen() on option: {selectedOption.Label}");
                        // Call the Chosen method to execute the option's action
                        selectedOption.Chosen(givesColonistOrders, currentMenu);

                        // Close the menu
                        Find.WindowStack.TryRemove(currentMenu);

                        // Reset state
                        FloatMenuNavigationState.Reset();
                    }
                    else
                    {
                        // Announce that option is disabled
                        ClipboardHelper.CopyToClipboard(selectedOption.Label + " - unavailable");
                    }
                }
                handled = true;
            }
            // Handle Escape to close menu
            else if (key == KeyCode.Escape)
            {
                Find.WindowStack.TryRemove(currentMenu);
                FloatMenuNavigationState.Reset();
                ClipboardHelper.CopyToClipboard("Menu closed");
                handled = true;
            }

            // If we handled the key, consume the event
            if (handled)
            {
                Event.current.Use();
            }
        }

        private static FloatMenu GetCurrentFloatMenu()
        {
            if (Find.WindowStack == null)
                return null;

            // Get the top-most FloatMenu from the window stack
            foreach (var window in Find.WindowStack.Windows)
            {
                if (window is FloatMenu floatMenu)
                {
                    return floatMenu;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Harmony patch for FloatMenu.DoWindowContents to handle keyboard navigation and visual highlighting.
    /// Prefix handles keyboard input BEFORE vanilla code, Postfix draws highlights AFTER.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenu))]
    [HarmonyPatch("DoWindowContents")]
    public class FloatMenuRenderPatch
    {
        /// <summary>
        /// Prefix runs BEFORE vanilla DoWindowContents, allowing us to consume keyboard events
        /// before the invisible buttons process them.
        /// </summary>
        static void Prefix(FloatMenu __instance, Rect rect)
        {
            // Register this menu if not already tracked
            if (FloatMenuNavigationState.CurrentMenu == null)
            {
                MelonLoader.MelonLogger.Msg("[FloatMenu DEBUG] Registering menu in Prefix");
                FloatMenuNavigationState.CurrentMenu = __instance;
            }

            // Only process for the currently tracked menu
            if (FloatMenuNavigationState.CurrentMenu != __instance)
                return;

            // Get the options list using reflection
            List<FloatMenuOption> options = Traverse.Create(__instance).Field("options").GetValue<List<FloatMenuOption>>();
            if (options == null || options.Count == 0)
                return;

            // Validate current selection
            FloatMenuNavigationState.ValidateSelection(options.Count, (index) => !options[index].Disabled);

            // Handle keyboard input BEFORE vanilla code processes it
            if (Event.current.type == EventType.KeyDown)
            {
                KeyCode key = Event.current.keyCode;
                MelonLoader.MelonLogger.Msg($"[FloatMenu DEBUG] Prefix detected key: {key}");

                bool handled = false;
                int selectedIndex = FloatMenuNavigationState.SelectedIndex;

                if (key == KeyCode.DownArrow)
                {
                    MelonLoader.MelonLogger.Msg("[FloatMenu DEBUG] Down arrow in Prefix");
                    int newIndex = FloatMenuNavigationState.SelectNext(options.Count, (index) => !options[index].Disabled);
                    if (newIndex >= 0)
                    {
                        string optionText = options[newIndex].Label;
                        ClipboardHelper.CopyToClipboard(optionText);
                        FloatMenuNavigationState.LastAnnouncedText = optionText;
                    }
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    MelonLoader.MelonLogger.Msg("[FloatMenu DEBUG] Up arrow in Prefix");
                    int newIndex = FloatMenuNavigationState.SelectPrevious(options.Count, (index) => !options[index].Disabled);
                    if (newIndex >= 0)
                    {
                        string optionText = options[newIndex].Label;
                        ClipboardHelper.CopyToClipboard(optionText);
                        FloatMenuNavigationState.LastAnnouncedText = optionText;
                    }
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    MelonLoader.MelonLogger.Msg("[FloatMenu DEBUG] Enter in Prefix");
                    if (selectedIndex >= 0 && selectedIndex < options.Count)
                    {
                        FloatMenuOption selectedOption = options[selectedIndex];
                        if (!selectedOption.Disabled)
                        {
                            bool givesColonistOrders = Traverse.Create(__instance).Field("givesColonistOrders").GetValue<bool>();
                            MelonLoader.MelonLogger.Msg($"[FloatMenu DEBUG] Executing option: {selectedOption.Label}");
                            selectedOption.Chosen(givesColonistOrders, __instance);
                            Find.WindowStack.TryRemove(__instance);
                            FloatMenuNavigationState.Reset();
                        }
                    }
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    MelonLoader.MelonLogger.Msg("[FloatMenu DEBUG] Escape in Prefix");
                    Find.WindowStack.TryRemove(__instance);
                    FloatMenuNavigationState.Reset();
                    ClipboardHelper.CopyToClipboard("Menu closed");
                    handled = true;
                }

                // Consume the event so vanilla code doesn't process it
                if (handled)
                {
                    MelonLoader.MelonLogger.Msg("[FloatMenu DEBUG] Event consumed in Prefix");
                    Event.current.Use();
                }
            }
        }

        /// <summary>
        /// Postfix runs AFTER vanilla DoWindowContents, allowing us to draw visual highlights
        /// and auto-announce the first option.
        /// </summary>
        static void Postfix(FloatMenu __instance, Rect rect)
        {
            // Only process for the currently tracked menu
            if (FloatMenuNavigationState.CurrentMenu != __instance)
                return;

            // Get the options list using reflection
            List<FloatMenuOption> options = Traverse.Create(__instance).Field("options").GetValue<List<FloatMenuOption>>();
            if (options == null || options.Count == 0)
                return;

            int selectedIndex = FloatMenuNavigationState.SelectedIndex;

            // Validate selection is in bounds
            if (selectedIndex < 0 || selectedIndex >= options.Count)
            {
                FloatMenuNavigationState.ValidateSelection(options.Count, (index) => !options[index].Disabled);
                selectedIndex = FloatMenuNavigationState.SelectedIndex;
            }

            // Calculate the rect of the selected option
            Rect selectedOptionRect = CalculateOptionRect(__instance, options, selectedIndex, rect);

            if (selectedOptionRect != Rect.zero)
            {
                // Draw highlight on the selected option
                Color highlightColor = new Color(1f, 1f, 0f, 0.3f);
                Widgets.DrawBoxSolid(selectedOptionRect, highlightColor);
                Widgets.DrawBox(selectedOptionRect, 2);
            }

            // Auto-announce on first render if not already announced
            if (string.IsNullOrEmpty(FloatMenuNavigationState.LastAnnouncedText) && selectedIndex >= 0 && selectedIndex < options.Count)
            {
                string optionText = options[selectedIndex].Label;
                if (options[selectedIndex].Disabled)
                {
                    optionText += " (unavailable)";
                }
                MelonLoader.MelonLogger.Msg($"[FloatMenu DEBUG] Announcing first option: {optionText}");
                ClipboardHelper.CopyToClipboard(optionText);
                FloatMenuNavigationState.LastAnnouncedText = optionText;
            }
        }

        private static Rect CalculateOptionRect(FloatMenu menu, List<FloatMenuOption> options, int targetIndex, Rect menuRect)
        {
            if (targetIndex < 0 || targetIndex >= options.Count)
                return Rect.zero;

            // Get necessary values from the menu using reflection/Traverse
            bool usingScrollbar = Traverse.Create(menu).Property("UsingScrollbar").GetValue<bool>();
            float maxViewHeight = Traverse.Create(menu).Property("MaxViewHeight").GetValue<float>();
            float columnWidth = Traverse.Create(menu).Property("ColumnWidth").GetValue<float>();
            Vector2 scrollPosition = Traverse.Create(menu).Field("scrollPosition").GetValue<Vector2>();

            Vector2 currentPosition = Vector2.zero;
            const float optionSpacing = -1f;

            // Adjust for scrollbar if present
            if (usingScrollbar)
            {
                currentPosition -= scrollPosition;
            }

            // Iterate through options to find the target's position
            for (int i = 0; i < options.Count; i++)
            {
                FloatMenuOption option = options[i];
                float requiredHeight = option.RequiredHeight;

                // Check if we need to move to next column
                if (currentPosition.y + requiredHeight + optionSpacing > maxViewHeight)
                {
                    currentPosition.y = 0f;
                    currentPosition.x += columnWidth + optionSpacing;

                    // Adjust for scroll position again when starting new column
                    if (usingScrollbar)
                    {
                        currentPosition.y -= scrollPosition.y;
                    }
                }

                // If this is our target option, return its rect
                if (i == targetIndex)
                {
                    Rect optionRect = new Rect(currentPosition.x, currentPosition.y, columnWidth, requiredHeight);

                    // Transform to screen coordinates
                    optionRect.x += menuRect.x;
                    optionRect.y += menuRect.y;

                    return optionRect;
                }

                // Move to next option position
                currentPosition.y += requiredHeight + optionSpacing;
            }

            return Rect.zero;
        }
    }

    /// <summary>
    /// Harmony patch for FloatMenu.PostClose to clean up state.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenu))]
    [HarmonyPatch("PostClose")]
    public class FloatMenuClosePatch
    {
        static void Postfix(FloatMenu __instance)
        {
            // Only reset if this was the tracked menu
            if (FloatMenuNavigationState.CurrentMenu == __instance)
            {
                FloatMenuNavigationState.Reset();
            }
        }
    }
}
