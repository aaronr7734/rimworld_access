using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Text;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Page_SelectStartingSite))]
    [HarmonyPatch("DoWindowContents")]
    public class StartingSitePatch
    {
        private static bool patchActive = false;
        private static bool hasAnnouncedTitle = false;

        // Prefix: Initialize state and handle keyboard input
        static void Prefix(Page_SelectStartingSite __instance, Rect rect)
        {
            try
            {
                // Initialize navigation state
                StartingSiteNavigationState.Initialize();

                // Announce window title and initial selection once
                if (!hasAnnouncedTitle)
                {
                    string pageTitle = "Select Starting Site";
                    TolkHelper.Speak($"{pageTitle} - Arrow keys to navigate, Control+arrows to jump by biome, Space for basic info, I for detailed info menu, F for factions, Enter to validate selection");
                    hasAnnouncedTitle = true;
                }

                // Auto-read current tile after a short delay (to not override title announcement)
                if (!StartingSiteNavigationState.HasReadCurrentTile)
                {
                    StartingSiteNavigationState.ReadCurrentTile();
                }

                // Handle keyboard input
                if (Event.current.type == EventType.KeyDown)
                {
                    KeyCode keyCode = Event.current.keyCode;
                    bool menuOpen = StartingSiteNavigationState.IsMenuOpen;

                    if (keyCode == KeyCode.R)
                    {
                        // Select random starting site
                        StartingSiteNavigationState.SelectRandomTile();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.Space)
                    {
                        // Read basic information about current tile
                        StartingSiteNavigationState.ReadCurrentTile();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.I)
                    {
                        // Open additional info menu or navigate down if already open
                        StartingSiteNavigationState.OpenAdditionalInfoMenu();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.K || (keyCode == KeyCode.I && Event.current.shift))
                    {
                        // Navigate menu up
                        if (menuOpen)
                        {
                            StartingSiteNavigationState.NavigateMenu(-1);
                            Event.current.Use();
                            patchActive = true;
                        }
                    }
                    else if (keyCode == KeyCode.F)
                    {
                        // Open factions tab
                        Find.WindowStack.Add(new Dialog_FactionDuringLanding());
                        TolkHelper.Speak("Opened faction relations dialog.");
                        Event.current.Use();
                        patchActive = true;
                    }
                    // Note: Enter key is handled by OnAcceptKeyPressed patch instead
                    else if (keyCode == KeyCode.Escape)
                    {
                        // Close menu if open
                        if (menuOpen)
                        {
                            StartingSiteNavigationState.CloseMenu();
                            Event.current.Use();
                            patchActive = true;
                        }
                    }
                    else if (keyCode == KeyCode.UpArrow)
                    {
                        if (menuOpen)
                        {
                            // Navigate menu up
                            StartingSiteNavigationState.NavigateMenu(-1);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (Event.current.control)
                        {
                            // Control + Up: Jump to next biome north
                            StartingSiteNavigationState.JumpToNextBiomeInDirection(Direction8Way.North);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else
                        {
                            // Move north
                            StartingSiteNavigationState.MoveInDirection(Direction8Way.North);
                            Event.current.Use();
                            patchActive = true;
                        }
                    }
                    else if (keyCode == KeyCode.DownArrow)
                    {
                        if (menuOpen)
                        {
                            // Navigate menu down
                            StartingSiteNavigationState.NavigateMenu(1);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (Event.current.control)
                        {
                            // Control + Down: Jump to next biome south
                            StartingSiteNavigationState.JumpToNextBiomeInDirection(Direction8Way.South);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else
                        {
                            // Move south
                            StartingSiteNavigationState.MoveInDirection(Direction8Way.South);
                            Event.current.Use();
                            patchActive = true;
                        }
                    }
                    else if (keyCode == KeyCode.LeftArrow)
                    {
                        if (!menuOpen)
                        {
                            if (Event.current.control)
                            {
                                // Control + Left: Jump to next biome west
                                StartingSiteNavigationState.JumpToNextBiomeInDirection(Direction8Way.West);
                                Event.current.Use();
                                patchActive = true;
                            }
                            else
                            {
                                // Move west
                                StartingSiteNavigationState.MoveInDirection(Direction8Way.West);
                                Event.current.Use();
                                patchActive = true;
                            }
                        }
                    }
                    else if (keyCode == KeyCode.RightArrow)
                    {
                        if (!menuOpen)
                        {
                            if (Event.current.control)
                            {
                                // Control + Right: Jump to next biome east
                                StartingSiteNavigationState.JumpToNextBiomeInDirection(Direction8Way.East);
                                Event.current.Use();
                                patchActive = true;
                            }
                            else
                            {
                                // Move east
                                StartingSiteNavigationState.MoveInDirection(Direction8Way.East);
                                Event.current.Use();
                                patchActive = true;
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in StartingSitePatch Prefix: {ex}");
            }
        }

        // Add a method to reset state when page is opened
        [HarmonyPatch(typeof(Page_SelectStartingSite), "PreOpen")]
        [HarmonyPostfix]
        static void PreOpen_Postfix()
        {
            hasAnnouncedTitle = false;
            StartingSiteNavigationState.Reset();
        }

        // Patch OnAcceptKeyPressed to handle Enter key based on context
        [HarmonyPatch(typeof(Page_SelectStartingSite), "OnAcceptKeyPressed")]
        [HarmonyPrefix]
        static bool OnAcceptKeyPressed_Prefix()
        {
            // If menu is open, handle menu interaction only - don't advance page
            if (StartingSiteNavigationState.IsMenuOpen)
            {
                StartingSiteNavigationState.ReadSelectedMenuItem();
                return false; // Skip original method
            }

            // If menu is closed, validate the tile
            PlanetTile tile = Find.WorldInterface.SelectedTile;
            if (!tile.Valid)
            {
                TolkHelper.Speak("No tile selected. Use arrow keys to navigate to a tile first.");
                return false; // Don't proceed
            }

            // Check if tile is valid for settlement
            StringBuilder reason = new StringBuilder();
            bool isValid = TileFinder.IsValidTileForNewSettlement(tile, reason, forGravship: false);

            if (!isValid)
            {
                // Tile is invalid - explain why and don't proceed
                string errorMessage = "Cannot settle here: " + reason.ToString();
                TolkHelper.Speak(errorMessage, SpeechPriority.High);
                return false; // Skip original method - don't advance
            }

            // Tile is valid - allow game to proceed normally
            return true; // Allow original method to run and advance to next page
        }

        // Postfix: Draw help text and menu overlay
        static void Postfix(Page_SelectStartingSite __instance, Rect rect)
        {
            try
            {
                if (!patchActive) return;

                bool menuOpen = StartingSiteNavigationState.IsMenuOpen;

                if (menuOpen)
                {
                    // Draw menu overlay
                    Rect menuRect = new Rect(10f, 50f, 700f, 200f);
                    Widgets.DrawBoxSolid(menuRect, new Color(0.1f, 0.1f, 0.1f, 0.95f));

                    Text.Font = GameFont.Medium;
                    Text.Anchor = TextAnchor.UpperCenter;
                    Rect titleRect = new Rect(menuRect.x, menuRect.y + 5f, menuRect.width, 30f);
                    Widgets.Label(titleRect, "Additional Information Menu");

                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.UpperLeft;

                    Rect contentRect = menuRect.ContractedBy(10f);
                    contentRect.y += 35f;
                    contentRect.height -= 35f;

                    string currentItem = StartingSiteNavigationState.GetCurrentMenuItemName();
                    int selectedIndex = StartingSiteNavigationState.SelectedMenuIndex;
                    int totalItems = StartingSiteNavigationState.MenuItemCount;

                    string menuContent = $"Selected: {currentItem}\n" +
                                       $"Item {selectedIndex + 1} of {totalItems}\n\n" +
                                       "Controls:\n" +
                                       "  Up/Down Arrows: Navigate menu items\n" +
                                       "  Enter: Read detailed information\n" +
                                       "  Escape: Close menu";

                    Widgets.Label(contentRect, menuContent);
                    Text.Anchor = TextAnchor.UpperLeft;
                }
                else
                {
                    // Draw help text at the top of the screen
                    Rect helpRect = new Rect(10f, 50f, 700f, 80f);

                    Widgets.DrawBoxSolid(helpRect, new Color(0.2f, 0.2f, 0.2f, 0.8f));

                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.UpperLeft;

                    string helpText = "Starting Site Selection:\n" +
                                    "Arrow Keys: Navigate map  |  Control+Arrows: Jump by biome  |  Space: Read basic info\n" +
                                    "I: Additional info menu  |  F: Faction relations  |  R: Random site  |  Enter: Validate";

                    Widgets.Label(helpRect.ContractedBy(5f), helpText);
                    Text.Anchor = TextAnchor.UpperLeft;
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in StartingSitePatch Postfix: {ex}");
            }
        }
    }
}
