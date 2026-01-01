using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_FormCaravan to enable keyboard navigation.
    /// Activates CaravanFormationState when the dialog opens and handles keyboard input.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_FormCaravan))]
    public static class CaravanFormationPatch
    {
        /// <summary>
        /// Patch for PostOpen to activate keyboard navigation when the dialog opens.
        /// </summary>
        [HarmonyPatch("PostOpen")]
        [HarmonyPostfix]
        public static void PostOpen_Postfix(Dialog_FormCaravan __instance)
        {
            CaravanFormationState.Open(__instance);
        }

        /// <summary>
        /// Patch for PostClose to deactivate keyboard navigation when the dialog closes.
        /// Don't close if we're in destination selection mode (dialog was temporarily removed).
        /// </summary>
        [HarmonyPatch("PostClose")]
        [HarmonyPostfix]
        public static void PostClose_Postfix()
        {
            // Don't close state if we're choosing a destination - we'll reopen the dialog
            if (CaravanFormationState.IsChoosingDestination)
                return;

            CaravanFormationState.Close();
        }

        /// <summary>
        /// Patch for DoWindowContents to draw visual indicators.
        /// Keyboard input is handled by UnifiedKeyboardPatch at Normal priority on UIRootOnGUI,
        /// which runs BEFORE DoWindowContents for proper input handling.
        /// </summary>
        [HarmonyPatch("DoWindowContents")]
        [HarmonyPostfix]
        public static void DoWindowContents_Postfix(Dialog_FormCaravan __instance, Rect inRect)
        {
            if (!CaravanFormationState.IsActive)
                return;

            // Draw visual indicator that keyboard mode is active
            DrawKeyboardModeIndicator(inRect);
        }

        /// <summary>
        /// Draws a visual indicator at the top of the dialog showing that keyboard mode is active.
        /// </summary>
        private static void DrawKeyboardModeIndicator(Rect inRect)
        {
            // Draw indicator in top-left corner
            float indicatorWidth = 250f;
            float indicatorHeight = 30f;
            Rect indicatorRect = new Rect(inRect.x + 10f, inRect.y + 10f, indicatorWidth, indicatorHeight);

            // Draw background
            Color backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.85f);
            Widgets.DrawBoxSolid(indicatorRect, backgroundColor);

            // Draw border
            Color borderColor = new Color(0.4f, 0.6f, 1.0f, 1.0f);
            Widgets.DrawBox(indicatorRect, 1);

            // Draw text
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(indicatorRect, "Keyboard Mode Active");

            // Reset text settings
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            // Draw instructions below the indicator
            float instructionsY = indicatorRect.yMax + 5f;
            float instructionsWidth = 500f;
            float instructionsHeight = 60f;
            Rect instructionsRect = new Rect(inRect.x + 10f, instructionsY, instructionsWidth, instructionsHeight);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;

            string instructions = "Tabs: Pawns, Items, Travel Supplies, Stats | Left/Right: Switch tabs\n" +
                                "Up/Down: Navigate | Enter or +/-: Adjust quantity\n" +
                                "Alt+D: Destination | Alt+T: Send | Alt+R: Reset | Escape: Cancel";

            Widgets.Label(instructionsRect, instructions);

            // Reset text settings
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
