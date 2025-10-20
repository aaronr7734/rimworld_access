using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    // Patch to capture the actual menu lists with their actions
    [HarmonyPatch(typeof(OptionListingUtility), "DrawOptionListing")]
    public static class OptionListingCapturePatch
    {
        [HarmonyPrefix]
        public static void Prefix(List<ListableOption> optList)
        {
            // Only capture if we're in the main menu and the list is valid
            if (optList == null || optList.Count == 0)
                return;

            // Store the list in the capture buffer
            MainMenuAccessibilityPatch.CaptureMenuList(optList);
        }
    }

    [HarmonyPatch(typeof(MainMenuDrawer), "DoMainMenuControls")]
    public static class MainMenuAccessibilityPatch
    {
        private static bool initialized = false;
        public static List<ListableOption> cachedColumn0 = new List<ListableOption>();
        public static List<ListableOption> cachedColumn1 = new List<ListableOption>();
        private static int captureCount = 0;
        private static bool isCapturing = false;

        public static void CaptureMenuList(List<ListableOption> optList)
        {
            if (!isCapturing)
                return;

            // First call captures column 0, second call captures column 1
            if (captureCount == 0)
            {
                cachedColumn0 = new List<ListableOption>(optList);
                captureCount = 1;
            }
            else if (captureCount == 1)
            {
                cachedColumn1 = new List<ListableOption>(optList);
                captureCount = 2; // Mark as complete
            }
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            // Reset capture state at the beginning of DoMainMenuControls
            captureCount = 0;
            isCapturing = true;
        }

        [HarmonyPostfix]
        public static void Postfix(Rect rect, bool anyMapFiles)
        {
            // Stop capturing after menu is drawn
            isCapturing = false;

            // Initialize menu navigation state with captured lists
            if (cachedColumn0.Count > 0 && cachedColumn1.Count > 0)
            {
                if (!initialized)
                {
                    MenuNavigationState.Initialize(cachedColumn0, cachedColumn1);
                    MenuNavigationState.Reset();
                    initialized = true;
                }
                else
                {
                    // Update the lists each frame in case menu changes
                    MenuNavigationState.Initialize(cachedColumn0, cachedColumn1);
                }
            }

            // Handle keyboard input
            HandleKeyboardInput();

            // Draw highlight on selected item
            DrawSelectionHighlight(rect);
        }


        private static void HandleKeyboardInput()
        {
            if (Event.current.type != EventType.KeyDown)
                return;

            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.UpArrow:
                    MenuNavigationState.MoveUp();
                    Event.current.Use();
                    break;

                case KeyCode.DownArrow:
                    MenuNavigationState.MoveDown();
                    Event.current.Use();
                    break;

                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                    MenuNavigationState.SwitchColumn();
                    Event.current.Use();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    // Find the actual menu item and execute its action
                    ExecuteSelectedMenuItem(MenuNavigationState.CurrentColumn, MenuNavigationState.SelectedIndex);
                    Event.current.Use();
                    break;
            }
        }

        private static void ExecuteSelectedMenuItem(int column, int index)
        {
            ListableOption selected = MenuNavigationState.GetCurrentSelection();
            if (selected != null && selected.action != null)
            {
                Log.Message($"RimWorld Access: Executing menu item - {selected.label}");
                selected.action();
            }
        }

        private static void DrawSelectionHighlight(Rect menuRect)
        {
            // Calculate the position of the selected menu item
            int column = MenuNavigationState.CurrentColumn;
            int selectedIndex = MenuNavigationState.SelectedIndex;

            List<ListableOption> currentList = (column == 0) ? cachedColumn0 : cachedColumn1;

            if (selectedIndex < 0 || selectedIndex >= currentList.Count)
                return;

            // Calculate vertical position
            float yOffset = 0f;
            for (int i = 0; i < selectedIndex; i++)
            {
                yOffset += currentList[i].minHeight + 7f; // 7f is the spacing
            }

            // Calculate column offset
            float xOffset = (column == 0) ? 0f : (170f + 17f); // Column 0 width + gap
            float width = (column == 0) ? 170f : 145f;
            float height = currentList[selectedIndex].minHeight;

            // Create highlight rect relative to menu rect
            Rect highlightRect = new Rect(
                menuRect.x + xOffset,
                menuRect.y + yOffset + 17f, // 17f is the yMin offset from MainMenuDrawer
                width,
                height
            );

            // Draw highlight
            Widgets.DrawHighlight(highlightRect);
        }
    }
}
