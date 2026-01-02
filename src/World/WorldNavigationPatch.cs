using System.Linq;
using HarmonyLib;
using UnityEngine;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch for WorldInterface.HandleLowPriorityInput to add keyboard navigation for world map.
    /// Intercepts arrow key input to navigate between world tiles.
    /// Automatically opens/closes world navigation state when entering/leaving world view.
    /// </summary>
    [HarmonyPatch(typeof(WorldInterface))]
    [HarmonyPatch("HandleLowPriorityInput")]
    public static class WorldNavigationPatch
    {
        private static bool lastFrameWasWorldView = false;

        /// <summary>
        /// Prefix patch that intercepts keyboard input for world map navigation.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        public static void Prefix()
        {
            // If any accessibility menu is active, don't intercept - let UnifiedKeyboardPatch handle it
            if (KeyboardHelper.IsAnyAccessibilityMenuActive())
                return;

            // Detect if we're in world view
            bool isWorldView = Current.ProgramState == ProgramState.Playing &&
                              Find.World != null &&
                              Find.World.renderer != null &&
                              Find.World.renderer.wantedMode == WorldRenderMode.Planet;

            // Handle state transitions
            if (isWorldView && !lastFrameWasWorldView)
            {
                // Just entered world view
                WorldNavigationState.Open();
            }
            else if (!isWorldView && lastFrameWasWorldView)
            {
                // Just left world view
                WorldNavigationState.Close();
                WorldScannerState.Reset();
            }

            lastFrameWasWorldView = isWorldView;

            // Only process input if in world view and state is active
            if (!isWorldView || !WorldNavigationState.IsActive)
                return;

            // Skip world navigation input if caravan formation dialog is active
            // (the dialog handles its own input via CaravanFormationPatch)
            // BUT allow navigation when choosing destination
            if (CaravanFormationState.IsActive && !CaravanFormationState.IsChoosingDestination)
                return;

            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            KeyCode key = Event.current.keyCode;

            // Skip if no actual key
            if (key == KeyCode.None)
                return;

            // Check for modifier keys
            bool shift = Event.current.shift;
            bool ctrl = Event.current.control;
            bool alt = Event.current.alt;

            // Handle caravan stats viewer input first if it's active
            if (CaravanStatsState.IsActive)
            {
                if (CaravanStatsState.HandleInput(key))
                {
                    Event.current.Use();
                    return;
                }
            }

            // Handle arrow key navigation
            if (key == KeyCode.UpArrow || key == KeyCode.DownArrow ||
                key == KeyCode.LeftArrow || key == KeyCode.RightArrow)
            {
                WorldNavigationState.HandleArrowKey(key);
                Event.current.Use();
                return;
            }

            // ===== World Scanner Controls (Page Up/Down) =====
            // Page Down: Next item in current category
            if (key == KeyCode.PageDown && !shift && !alt)
            {
                if (ctrl)
                    WorldScannerState.NextCategory();
                else
                    WorldScannerState.NextItem();
                Event.current.Use();
                return;
            }

            // Page Up: Previous item in current category
            if (key == KeyCode.PageUp && !shift && !alt)
            {
                if (ctrl)
                    WorldScannerState.PreviousCategory();
                else
                    WorldScannerState.PreviousItem();
                Event.current.Use();
                return;
            }

            // Home: Jump to scanner item OR home settlement (Alt+Home for home)
            if (key == KeyCode.Home && !shift && !ctrl)
            {
                if (alt)
                    WorldNavigationState.JumpToHome();
                else
                    WorldScannerState.JumpToCurrent();
                Event.current.Use();
                return;
            }

            // End: Read scanner distance/direction OR jump to caravan (Alt+End for caravan)
            if (key == KeyCode.End && !shift && !ctrl)
            {
                if (alt)
                    WorldNavigationState.JumpToNearestCaravan();
                else
                    WorldScannerState.ReadDistanceAndDirection();
                Event.current.Use();
                return;
            }

            // Alt+J: Toggle auto-jump mode for scanner
            if (key == KeyCode.J && alt && !shift && !ctrl)
            {
                WorldScannerState.ToggleAutoJumpMode();
                Event.current.Use();
                return;
            }

            // Note: Comma and Period keys for caravan cycling are handled in UnifiedKeyboardPatch
            // at a higher priority to prevent colonist selection from intercepting them

            // Handle I key - show caravan stats (if caravan selected) or read detailed tile information
            if (key == KeyCode.I && !shift && !ctrl && !alt)
            {
                Caravan selectedCaravan = WorldNavigationState.GetSelectedCaravan();
                if (selectedCaravan != null)
                {
                    WorldNavigationState.ShowCaravanStats();
                }
                else
                {
                    WorldNavigationState.ReadDetailedTileInfo();
                }
                Event.current.Use();
                return;
            }

            // Handle number keys 1-5 for categorized tile information
            if (!shift && !ctrl && !alt)
            {
                int category = 0;
                if (key == KeyCode.Alpha1 || key == KeyCode.Keypad1) category = 1;
                else if (key == KeyCode.Alpha2 || key == KeyCode.Keypad2) category = 2;
                else if (key == KeyCode.Alpha3 || key == KeyCode.Keypad3) category = 3;
                else if (key == KeyCode.Alpha4 || key == KeyCode.Keypad4) category = 4;
                else if (key == KeyCode.Alpha5 || key == KeyCode.Keypad5) category = 5;

                if (category > 0)
                {
                    WorldNavigationState.AnnounceTileInfoCategory(category);
                    Event.current.Use();
                    return;
                }
            }

            // Handle C key - form caravan at selected settlement
            if (key == KeyCode.C && !shift && !ctrl && !alt)
            {
                WorldNavigationState.FormCaravanAtSelectedSettlement();
                Event.current.Use();
                return;
            }

            // Handle ] key - give orders to selected caravan
            if (key == KeyCode.RightBracket && !shift && !ctrl && !alt)
            {
                WorldNavigationState.GiveCaravanOrders();
                Event.current.Use();
                return;
            }

            // Handle Enter key - set caravan destination, inspect caravan, or enter settlement
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                if (CaravanFormationState.IsChoosingDestination)
                {
                    CaravanFormationState.SetDestination(WorldNavigationState.CurrentSelectedTile);
                    Event.current.Use();
                    return;
                }

                // Check if there's a player caravan at the current tile - show stats
                PlanetTile currentTile = WorldNavigationState.CurrentSelectedTile;
                if (currentTile.Valid && Find.WorldObjects != null)
                {
                    var caravanAtTile = Find.WorldObjects.ObjectsAt(currentTile)
                        .OfType<RimWorld.Planet.Caravan>()
                        .FirstOrDefault(c => c.Faction == RimWorld.Faction.OfPlayer);

                    if (caravanAtTile != null)
                    {
                        // Show caravan stats (same as I key)
                        CaravanStatsState.Open(caravanAtTile);
                        Event.current.Use();
                        return;
                    }

                    // Check for player settlement - could enter it
                    var settlementAtTile = Find.WorldObjects.ObjectsAt(currentTile)
                        .OfType<RimWorld.Planet.Settlement>()
                        .FirstOrDefault(s => s.Faction == RimWorld.Faction.OfPlayer && s.HasMap);

                    if (settlementAtTile != null)
                    {
                        // Enter the player's settlement
                        CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(settlementAtTile.Map.Center, settlementAtTile.Map));
                        TolkHelper.Speak($"Entering {settlementAtTile.Label}");
                        Event.current.Use();
                        return;
                    }
                }
            }

            // Handle Escape key - close caravan stats, cancel destination selection, or let RimWorld handle it
            if (key == KeyCode.Escape)
            {
                if (CaravanFormationState.IsChoosingDestination)
                {
                    CaravanFormationState.CancelDestinationSelection();
                    Event.current.Use();
                    return;
                }
                else if (CaravanStatsState.IsActive)
                {
                    CaravanStatsState.Close();
                    Event.current.Use();
                    return;
                }
                // Otherwise, let RimWorld's default behavior handle it (return to map)
            }
        }

        /// <summary>
        /// Postfix patch to draw visual highlight on selected tile.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Only draw if world navigation is active
            if (!WorldNavigationState.IsActive || !WorldNavigationState.IsInitialized)
                return;

            PlanetTile selectedTile = WorldNavigationState.CurrentSelectedTile;
            if (!selectedTile.Valid)
                return;

            // Draw highlight using RimWorld's world renderer
            // The game's WorldSelector already handles drawing selection highlights,
            // so we just need to ensure our tile is selected in the game's system
            // (which is already done in WorldNavigationState)

            // For additional visual feedback, we could draw text overlay
            DrawTileInfoOverlay(selectedTile);
        }

        /// <summary>
        /// Draws an overlay showing current tile information at the top of the screen.
        /// </summary>
        private static void DrawTileInfoOverlay(PlanetTile tile)
        {
            if (!tile.Valid)
                return;

            // Get screen dimensions
            float screenWidth = UI.screenWidth;

            // Create overlay rect (top-center of screen)
            float overlayWidth = 600f;
            float overlayHeight = 80f;
            float overlayX = (screenWidth - overlayWidth) / 2f;
            float overlayY = 20f;

            Rect overlayRect = new Rect(overlayX, overlayY, overlayWidth, overlayHeight);

            // Draw semi-transparent background
            Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            Widgets.DrawBoxSolid(overlayRect, backgroundColor);

            // Draw border
            Color borderColor = new Color(0.5f, 0.7f, 1.0f, 1.0f);
            Widgets.DrawBox(overlayRect, 2);

            // Draw text
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            // Get tile info
            string tileInfo = WorldInfoHelper.GetTileSummary(tile);
            string instructions = "Arrows: Navigate | PgUp/Dn: Scan Items | Ctrl+PgUp/Dn: Categories | Alt+Home: Home | Alt+End: Caravan | I: Details | C: Form | ]: Orders";

            Rect infoRect = new Rect(overlayX, overlayY + 15f, overlayWidth, 30f);
            Rect instructionsRect = new Rect(overlayX, overlayY + 45f, overlayWidth, 25f);

            Widgets.Label(infoRect, tileInfo);

            Text.Font = GameFont.Tiny;
            Widgets.Label(instructionsRect, instructions);

            // Reset text settings
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
