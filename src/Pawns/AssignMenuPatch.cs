using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch that draws a visual overlay when the assign menu is active.
    /// Keyboard input routing is handled by UnifiedKeyboardPatch.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class AssignMenuOverlayPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!AssignMenuState.IsActive)
                return;

            DrawMenuOverlay();
        }

        private static void DrawMenuOverlay()
        {
            float screenWidth = UI.screenWidth;

            float overlayWidth = 700f;
            float overlayHeight = 130f;
            float overlayX = (screenWidth - overlayWidth) / 2f;
            float overlayY = 20f;

            Rect overlayRect = new Rect(overlayX, overlayY, overlayWidth, overlayHeight);

            Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            Widgets.DrawBoxSolid(overlayRect, backgroundColor);
            Widgets.DrawBox(overlayRect, 2);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            string title = "Assign Menu";

            string instructions1 = "Up/Down: Navigate Pawns | Left/Right: Navigate Columns | Enter: Change Value";
            string instructions2 = "]: Context Menu | Shift+Up/Down: Paint | Alt+S: Sort | Alt+I: Info Card | Esc: Close";

            Rect titleRect = new Rect(overlayX, overlayY + 15f, overlayWidth, 30f);
            Rect instructions1Rect = new Rect(overlayX, overlayY + 50f, overlayWidth, 25f);
            Rect instructions2Rect = new Rect(overlayX, overlayY + 80f, overlayWidth, 25f);

            Widgets.Label(titleRect, title);

            Text.Font = GameFont.Tiny;
            Widgets.Label(instructions1Rect, instructions1);
            Widgets.Label(instructions2Rect, instructions2);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }

    /// <summary>
    /// Harmony patch to intercept the Assign tab opening and replace it with our accessible version.
    /// Since MainTabWindow_Assign doesn't override DoWindowContents, we patch the base class
    /// MainTabWindow_PawnTable.DoWindowContents and check the instance type.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_PawnTable), nameof(MainTabWindow_PawnTable.DoWindowContents))]
    public static class AssignWindowInterceptPatch
    {
        private static bool hasIntercepted = false;

        [HarmonyPrefix]
        public static bool Prefix(MainTabWindow_PawnTable __instance)
        {
            // Only intercept Assign windows, not other PawnTable windows
            if (!(__instance is MainTabWindow_Assign))
            {
                return true; // Let other windows proceed normally
            }

            // Only intercept once per window opening
            if (!hasIntercepted)
            {
                hasIntercepted = true;

                // If on the world map, switch to colony map first
                if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
                {
                    CameraJumper.TryHideWorld();
                    MapNavigationState.RestoreCursorForCurrentMap();
                }

                // Open our windowless version instead
                AssignMenuState.Open();

                // Close the window that was just opened
                Find.WindowStack.TryRemove(typeof(MainTabWindow_Assign), doCloseSound: false);

                // Reset flag to allow for future opens
                hasIntercepted = false;

                // Return false to prevent the original DoWindowContents from executing
                return false;
            }

            return true;
        }
    }
}
