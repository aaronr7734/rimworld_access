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
        // NOTE: Most key handling here is duplicated in UnifiedKeyboardPatch at priority 0.55.
        // UnifiedKeyboardPatch runs OUTSIDE GUI.Window context and handles keys reliably even
        // when IMGUI focus is not properly established (e.g., after closing faction dialog).
        // This handler serves as a defensive fallback when GUI.Window focus is working normally.
        static void Prefix(Page_SelectStartingSite __instance, Rect rect)
        {
            try
            {
                // Initialize shared world navigation state on first frame
                if (!WorldNavigationState.IsActive)
                {
                    WorldNavigationState.Open(WorldNavContext.WorldGen);
                    StartingSiteContext.Open();
                }

                // Announce window title once
                if (!hasAnnouncedTitle)
                {
                    string pageTitle = "Select Starting Site";
                    TolkHelper.Speak($"{pageTitle} - Arrow keys to navigate, Control+arrows to jump by biome, " +
                        "Page Up/Down for scanner, Z to search, 1-5 for tile info, " +
                        "I for detailed info menu, F for factions, Enter to validate selection");
                    hasAnnouncedTitle = true;
                }

                // Handle keyboard input
                if (Event.current.type == EventType.KeyDown)
                {
                    KeyCode keyCode = Event.current.keyCode;
                    bool shift = Event.current.shift;
                    bool ctrl = Event.current.control;
                    bool alt = Event.current.alt;

                    // === Scanner search text input (highest priority) ===
                    // When search is active, capture letters/numbers/Enter/Escape/Backspace.
                    // This is a defensive fallback - UnifiedKeyboardPatch normally handles this
                    // at priority -0.2, but may not fire during world gen (ProgramState.Entry).
                    if (ScannerSearchState.IsActive)
                    {
                        if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter)
                        {
                            ScannerSearchState.ConfirmSearch();
                            Event.current.Use();
                            patchActive = true;
                            return;
                        }
                        if (keyCode == KeyCode.Escape)
                        {
                            ScannerSearchState.CancelSearch();
                            Event.current.Use();
                            patchActive = true;
                            return;
                        }
                        if (keyCode == KeyCode.Backspace)
                        {
                            ScannerSearchState.HandleBackspace();
                            Event.current.Use();
                            patchActive = true;
                            return;
                        }
                        if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z && !ctrl && !alt)
                        {
                            char c = shift ? (char)('A' + (keyCode - KeyCode.A)) : (char)('a' + (keyCode - KeyCode.A));
                            ScannerSearchState.HandleCharacter(c);
                            Event.current.Use();
                            patchActive = true;
                            return;
                        }
                        if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9 && !ctrl && !alt)
                        {
                            char c = (char)('0' + (keyCode - KeyCode.Alpha0));
                            ScannerSearchState.HandleCharacter(c);
                            Event.current.Use();
                            patchActive = true;
                            return;
                        }
                        // Arrow keys, PgUp/PgDn, Home/End, Space: pass through to navigation below
                    }

                    bool menuOpen = StartingSiteContext.IsMenuOpen;

                    // When I-menu is open, route Up/Down/Enter/Escape to menu and block other keys
                    if (menuOpen)
                    {
                        if (keyCode == KeyCode.UpArrow)
                        {
                            StartingSiteContext.NavigateMenu(-1);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.DownArrow)
                        {
                            StartingSiteContext.NavigateMenu(1);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter)
                        {
                            StartingSiteContext.ReadSelectedMenuItem();
                            Event.current.Use();
                            patchActive = true;
                        }
                        else if (keyCode == KeyCode.Escape)
                        {
                            StartingSiteContext.CloseMenu();
                            Event.current.Use();
                            patchActive = true;
                        }
                        return; // Block all other keys while menu is open
                    }

                    // Arrow keys: route to shared WorldNavigationState (3D compass)
                    if (keyCode == KeyCode.UpArrow || keyCode == KeyCode.DownArrow ||
                        keyCode == KeyCode.LeftArrow || keyCode == KeyCode.RightArrow)
                    {
                        if (ctrl)
                        {
                            // Ctrl+arrows: biome jump
                            StartingSiteContext.JumpToNextBiomeInDirection(keyCode);
                            Event.current.Use();
                            patchActive = true;
                        }
                        else
                        {
                            // Plain arrows: standard 3D compass navigation
                            WorldNavigationState.HandleArrowKey(keyCode);
                            Event.current.Use();
                            patchActive = true;
                        }
                    }
                    else if (keyCode == KeyCode.R && !shift && !ctrl && !alt)
                    {
                        StartingSiteContext.SelectRandomTile();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.Space && !shift && !ctrl && !alt)
                    {
                        // Re-announce current tile
                        WorldNavigationState.AnnounceTile();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.I && !shift && !ctrl && !alt)
                    {
                        // Open additional info menu
                        StartingSiteContext.OpenAdditionalInfoMenu();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.F && !shift && !ctrl && !alt)
                    {
                        Find.WindowStack.Add(new Dialog_FactionDuringLanding());
                        // Opening announcement handled by FactionLandingState via PostOpen patch
                        Event.current.Use();
                        patchActive = true;
                    }
                    // === Z key: activate scanner search ===
                    // Defensive fallback - also handled by UnifiedKeyboardPatch at priority 4.745
                    else if (keyCode == KeyCode.Z && !shift && !ctrl && !alt && !ScannerSearchState.IsActive)
                    {
                        ScannerSearchState.Activate(true);
                        // Block game's keybinding system from seeing Z
                        Event.current.keyCode = KeyCode.None;
                        Event.current.Use();
                        patchActive = true;
                    }
                    // Ctrl+Z clears the active search filter
                    // Defensive fallback - also handled by UnifiedKeyboardPatch at priority 4.745
                    else if (keyCode == KeyCode.Z && ctrl && !shift && !alt && !ScannerSearchState.IsActive && ScannerSearchState.HasActiveFilter)
                    {
                        ScannerSearchState.ClearActiveFilter();
                        Event.current.Use();
                        patchActive = true;
                    }
                    // === Number keys 1-5: tile info categories ===
                    // Defensive fallback - also handled by UnifiedKeyboardPatch at priority 5.45
                    else if (!shift && !ctrl && !alt)
                    {
                        int category = 0;
                        if (keyCode == KeyCode.Alpha1 || keyCode == KeyCode.Keypad1) category = 1;
                        else if (keyCode == KeyCode.Alpha2 || keyCode == KeyCode.Keypad2) category = 2;
                        else if (keyCode == KeyCode.Alpha3 || keyCode == KeyCode.Keypad3) category = 3;
                        else if (keyCode == KeyCode.Alpha4 || keyCode == KeyCode.Keypad4) category = 4;
                        else if (keyCode == KeyCode.Alpha5 || keyCode == KeyCode.Keypad5) category = 5;

                        if (category > 0)
                        {
                            WorldNavigationState.AnnounceTileInfoCategory(category);
                            Event.current.Use();
                            patchActive = true;
                        }
                    }
                    // Note: Scanner keys (PgUp/PgDn/Home/End) are handled by
                    // UnifiedKeyboardPatch at priority 0.5 before this patch runs
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in StartingSitePatch Prefix: {ex}");
            }
        }

        // Reset state when page is opened
        [HarmonyPatch(typeof(Page_SelectStartingSite), "PreOpen")]
        [HarmonyPostfix]
        static void PreOpen_Postfix()
        {
            // Ensure clean state in case PostClose didn't fire (e.g., page re-entered without closing)
            if (WorldNavigationState.IsActive)
            {
                WorldNavigationState.Close();
                StartingSiteContext.Close();
                WorldScannerState.Reset();
            }
            hasAnnouncedTitle = false;
            patchActive = false;
        }

        // Clean up when page is closed
        [HarmonyPatch(typeof(Page_SelectStartingSite), "PostClose")]
        [HarmonyPostfix]
        static void PostClose_Postfix()
        {
            WorldNavigationState.Close();
            StartingSiteContext.Close();
            WorldScannerState.Reset();
            hasAnnouncedTitle = false;
            patchActive = false;
        }

        // Patch OnAcceptKeyPressed to handle Enter key based on context
        [HarmonyPatch(typeof(Page_SelectStartingSite), "OnAcceptKeyPressed")]
        [HarmonyPrefix]
        static bool OnAcceptKeyPressed_Prefix()
        {
            // If scanner search is active, don't advance page - Enter confirms search
            if (ScannerSearchState.IsActive)
            {
                ScannerSearchState.ConfirmSearch();
                return false;
            }

            // If I-menu is open, handle menu interaction only - don't advance page
            if (StartingSiteContext.IsMenuOpen)
            {
                StartingSiteContext.ReadSelectedMenuItem();
                return false;
            }

            // Use shared navigation state's tile
            PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
            if (!tile.Valid)
            {
                TolkHelper.Speak("No tile selected. Use arrow keys to navigate to a tile first.");
                return false;
            }

            // Check if tile is valid for settlement
            StringBuilder reason = new StringBuilder();
            bool isValid = TileFinder.IsValidTileForNewSettlement(tile, reason, forGravship: false);

            if (!isValid)
            {
                string errorMessage = "Cannot settle here: " + reason.ToString();
                TolkHelper.Speak(errorMessage, SpeechPriority.High);
                return false;
            }

            // Tile is valid - allow game to proceed normally
            return true;
        }

        // Postfix: Draw help text and menu overlay
        static void Postfix(Page_SelectStartingSite __instance, Rect rect)
        {
            try
            {
                if (!patchActive) return;

                bool menuOpen = StartingSiteContext.IsMenuOpen;

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

                    string currentItem = StartingSiteContext.GetCurrentMenuItemName();
                    int selectedIndex = StartingSiteContext.SelectedMenuIndex;
                    int totalItems = StartingSiteContext.MenuItemCount;

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
                                    "Arrow Keys: Navigate  |  Ctrl+Arrows: Jump by biome  |  PgUp/PgDn: Scanner\n" +
                                    "1-5: Tile info  |  Z: Search  |  I: Info menu  |  F: Factions  |  R: Random  |  Enter: Validate";

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
