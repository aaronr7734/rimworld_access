using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Page_CreateWorldParams))]
    [HarmonyPatch("DoWindowContents")]
    public class WorldParamsPatch
    {
        private static bool hasAnnouncedTitle = false;

        // Section tracking for Tab switching between World Params and Factions
        private enum CreateWorldSection { WorldParams, Factions }
        private static CreateWorldSection currentSection = CreateWorldSection.WorldParams;

        // Expose for Harmony patches
        internal static bool IsInFactionsSection => currentSection == CreateWorldSection.Factions;

        static void Prefix(Page_CreateWorldParams __instance, Rect rect)
        {
            try
            {
                // Initialize navigation state with the instance (syncs game values)
                WorldParamsNavigationState.Initialize(__instance);
                FactionsNavigationState.Initialize(__instance);

                // Restore IMGUI focus to this page. After closing certain dialogs
                // (e.g., faction relations from site selection), IMGUI focus may be
                // lost to a deleted window, preventing KeyDown events from arriving.
                // Same pattern used in IdeologySelectionPatch and StartingPawnPatch.
                Find.WindowStack.Notify_ManuallySetFocus(__instance);

                // Announce window title once
                if (!hasAnnouncedTitle)
                {
                    string help = "RimWorldAccess.WorldParams.OpenHelp".Translate();
                    TolkHelper.Speak("RimWorldAccess.WorldParams.OpenInstructions".Loc(help));
                    hasAnnouncedTitle = true;
                }

                // Handle keyboard input
                if (Event.current.type == EventType.KeyDown)
                {
                    HandleKeyInput(__instance, Event.current);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in WorldParamsPatch Prefix: {ex}");
            }
        }

        private static void HandleKeyInput(Page_CreateWorldParams instance, Event evt)
        {
            // Don't handle input when info card is open - let InfoCardState handle it
            if (InfoCardState.IsActive)
                return;

            // Modal text-edit has absolute priority. UnifiedKeyboardPatch at priority -1.6
            // should already have consumed the event, but this is defensive — if the event
            // somehow reaches us, we must not process it while the seed (or any other)
            // controller is live.
            if (TextInputManager.IsActive)
                return;

            KeyCode keyCode = evt.keyCode;

            // ===== Tab switching between sections =====
            // Both Tab and Shift+Tab toggle between the two sections
            if (keyCode == KeyCode.Tab)
            {
                if (currentSection == CreateWorldSection.WorldParams)
                {
                    // Switch to Factions
                    currentSection = CreateWorldSection.Factions;
                    FactionsNavigationState.Activate();
                }
                else
                {
                    // Switch to World Params
                    currentSection = CreateWorldSection.WorldParams;
                    FactionsNavigationState.Deactivate();
                    TolkHelper.Speak("RimWorldAccess.WorldParams.Title".Loc());
                    WorldParamsNavigationState.AnnounceCurrentField();
                }
                evt.Use();
                return;
            }

            // ===== Route based on section =====
            if (currentSection == CreateWorldSection.Factions)
            {
                HandleFactionsInput(instance, evt);
                return;
            }

            // ===== World Params section handling below =====

            // Seed editing is driven by TextInputController in modal mode. When active,
            // UnifiedKeyboardPatch at priority -1.6 routes all keys to the controller and
            // marks the event Used, so HandleKeyInput above never reaches us for those frames.

            // Typeahead search handling - when search is active, Up/Down navigate matches
            if (WorldParamsNavigationState.HasActiveSearch)
            {
                if (keyCode == KeyCode.UpArrow)
                {
                    WorldParamsNavigationState.SelectPreviousMatch();
                    evt.Use();
                    return;
                }
                else if (keyCode == KeyCode.DownArrow)
                {
                    WorldParamsNavigationState.SelectNextMatch();
                    evt.Use();
                    return;
                }
                else if (keyCode == KeyCode.Escape)
                {
                    WorldParamsNavigationState.ClearTypeaheadSearch();
                    evt.Use();
                    return;
                }
                else if (keyCode == KeyCode.Backspace)
                {
                    if (WorldParamsNavigationState.HandleTypeaheadBackspace())
                    {
                        evt.Use();
                        return;
                    }
                }
            }

            // Standard navigation
            if (keyCode == KeyCode.UpArrow)
            {
                WorldParamsNavigationState.NavigateUp();
                evt.Use();
            }
            else if (keyCode == KeyCode.DownArrow)
            {
                WorldParamsNavigationState.NavigateDown();
                evt.Use();
            }
            else if (keyCode == KeyCode.Home)
            {
                WorldParamsNavigationState.NavigateHome();
                evt.Use();
            }
            else if (keyCode == KeyCode.End)
            {
                WorldParamsNavigationState.NavigateEnd();
                evt.Use();
            }
            else if (keyCode == KeyCode.LeftArrow)
            {
                WorldParamsNavigationState.ModifyCurrentValue(-1);
                evt.Use();
            }
            else if (keyCode == KeyCode.RightArrow)
            {
                WorldParamsNavigationState.ModifyCurrentValue(1);
                evt.Use();
            }
            else if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter)
            {
                // Enter key on Seed field starts text input mode
                if (WorldParamsNavigationState.IsOnSeedField())
                {
                    WorldParamsNavigationState.StartSeedEdit();
                    evt.Use();
                }
            }
            else if (keyCode == KeyCode.R)
            {
                // Randomize seed
                if (WorldParamsNavigationState.IsOnSeedField())
                {
                    WorldParamsNavigationState.RandomizeSeed();
                    evt.Use();
                }
            }
            else if (keyCode == KeyCode.Escape)
            {
                // Escape with active search clears search
                if (WorldParamsNavigationState.HasActiveSearch)
                {
                    WorldParamsNavigationState.ClearTypeaheadSearch();
                    evt.Use();
                }
                // Otherwise let the game handle Escape (go back)
            }
            else if (keyCode == KeyCode.Backspace)
            {
                // Backspace during search
                if (WorldParamsNavigationState.HandleTypeaheadBackspace())
                {
                    evt.Use();
                }
            }
            else if (evt.character != '\0' && !evt.control && !evt.alt && char.IsLetterOrDigit(evt.character))
            {
                // Typeahead search
                WorldParamsNavigationState.HandleTypeahead(evt.character);
                evt.Use();
            }
        }

        private static void HandleFactionsInput(Page_CreateWorldParams instance, Event evt)
        {
            KeyCode keyCode = evt.keyCode;
            bool alt = evt.alt;

            // ===== Add Menu overlay (highest priority) =====
            if (FactionsNavigationState.IsAddMenuOpen)
            {
                // Typeahead in add menu
                if (FactionsNavigationState.HasAddMenuTypeahead)
                {
                    if (keyCode == KeyCode.UpArrow)
                    {
                        FactionsNavigationState.SelectPreviousAddMenuMatch();
                        evt.Use();
                        return;
                    }
                    else if (keyCode == KeyCode.DownArrow)
                    {
                        FactionsNavigationState.SelectNextAddMenuMatch();
                        evt.Use();
                        return;
                    }
                    else if (keyCode == KeyCode.Escape)
                    {
                        FactionsNavigationState.ClearAddMenuTypeahead();
                        evt.Use();
                        return;
                    }
                    else if (keyCode == KeyCode.Backspace)
                    {
                        FactionsNavigationState.HandleAddMenuTypeaheadBackspace();
                        evt.Use();
                        return;
                    }
                }

                if (keyCode == KeyCode.Escape)
                {
                    FactionsNavigationState.CloseAddMenu();
                    evt.Use();
                    return;
                }
                else if (keyCode == KeyCode.UpArrow)
                {
                    FactionsNavigationState.AddMenuNavigateUp();
                    evt.Use();
                    return;
                }
                else if (keyCode == KeyCode.DownArrow)
                {
                    FactionsNavigationState.AddMenuNavigateDown();
                    evt.Use();
                    return;
                }
                else if (keyCode == KeyCode.Home)
                {
                    FactionsNavigationState.AddMenuNavigateHome();
                    evt.Use();
                    return;
                }
                else if (keyCode == KeyCode.End)
                {
                    FactionsNavigationState.AddMenuNavigateEnd();
                    evt.Use();
                    return;
                }
                else if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter)
                {
                    FactionsNavigationState.AddMenuConfirm();
                    evt.Use();
                    return;
                }
                else if (evt.character != '\0' && !evt.control && !alt && char.IsLetterOrDigit(evt.character))
                {
                    FactionsNavigationState.HandleAddMenuTypeahead(evt.character);
                    evt.Use();
                    return;
                }

                // Consume all other keys when add menu is open
                evt.Use();
                return;
            }

            // ===== Faction list typeahead active =====
            if (FactionsNavigationState.HasFactionListTypeahead)
            {
                if (keyCode == KeyCode.UpArrow)
                {
                    FactionsNavigationState.SelectPreviousFactionMatch();
                    evt.Use();
                    return;
                }
                else if (keyCode == KeyCode.DownArrow)
                {
                    FactionsNavigationState.SelectNextFactionMatch();
                    evt.Use();
                    return;
                }
                else if (keyCode == KeyCode.Escape)
                {
                    FactionsNavigationState.ClearFactionTypeahead();
                    evt.Use();
                    return;
                }
                else if (keyCode == KeyCode.Backspace)
                {
                    FactionsNavigationState.HandleFactionTypeaheadBackspace();
                    evt.Use();
                    return;
                }
            }

            // ===== Navigation =====
            if (keyCode == KeyCode.UpArrow)
            {
                FactionsNavigationState.NavigateUp();
                evt.Use();
                return;
            }
            else if (keyCode == KeyCode.DownArrow)
            {
                FactionsNavigationState.NavigateDown();
                evt.Use();
                return;
            }
            else if (keyCode == KeyCode.Home)
            {
                FactionsNavigationState.NavigateHome();
                evt.Use();
                return;
            }
            else if (keyCode == KeyCode.End)
            {
                FactionsNavigationState.NavigateEnd();
                evt.Use();
                return;
            }

            // ===== Actions =====
            if (keyCode == KeyCode.Delete)
            {
                FactionsNavigationState.DeleteSelectedFaction();
                evt.Use();
                return;
            }
            else if (keyCode == KeyCode.A && alt)
            {
                FactionsNavigationState.OpenAddMenu();
                evt.Use();
                return;
            }

            // ===== Typeahead =====
            if (evt.character != '\0' && !evt.control && !alt && char.IsLetterOrDigit(evt.character))
            {
                FactionsNavigationState.HandleFactionTypeahead(evt.character);
                evt.Use();
                return;
            }

            // ===== Backspace for typeahead =====
            if (keyCode == KeyCode.Backspace)
            {
                if (FactionsNavigationState.HandleFactionTypeaheadBackspace())
                {
                    evt.Use();
                    return;
                }
            }
        }

        // Postfix: Draw visual indicator
        static void Postfix(Page_CreateWorldParams __instance, Rect rect)
        {
            try
            {
                // Draw indicator of current field at top
                Rect indicatorRect = new Rect(rect.x + 10f, rect.y + 10f, 500f, 30f);
                string text;

                if (WorldParamsNavigationState.IsEditingSeed)
                {
                    text = "RimWorldAccess.WorldParams.SeedTypingIndicator".Translate(WorldParamsNavigationState.SeedInputBuffer);
                }
                else
                {
                    string fieldName = WorldParamsNavigationState.GetCurrentFieldName();
                    text = "RimWorldAccess.WorldParams.FieldIndicator".Translate(fieldName);
                }

                Widgets.DrawBoxSolid(indicatorRect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(indicatorRect, text);
                Text.Anchor = TextAnchor.UpperLeft;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in WorldParamsPatch Postfix: {ex}");
            }
        }

        public static void ResetAnnouncement()
        {
            hasAnnouncedTitle = false;
            currentSection = CreateWorldSection.WorldParams;
        }
    }

    // Separate patch to reset state when page opens
    [HarmonyPatch(typeof(Page_CreateWorldParams), "PreOpen")]
    public class WorldParamsPatch_PreOpen
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            WorldParamsPatch.ResetAnnouncement();
            WorldParamsNavigationState.Reset();
            FactionsNavigationState.Reset();
        }
    }

    /// <summary>
    /// Block Enter key from advancing to next page when in factions mode.
    /// </summary>
    [HarmonyPatch(typeof(Page), "OnAcceptKeyPressed")]
    public class WorldParamsPatch_OnAcceptKeyPressed
    {
        [HarmonyPrefix]
        public static bool Prefix(Page __instance)
        {
            // Only intercept for Page_CreateWorldParams
            if (!(__instance is Page_CreateWorldParams))
                return true;

            // Block Enter when add menu is open (we handle Enter to add faction)
            if (FactionsNavigationState.IsAddMenuOpen)
                return false;

            // Block Enter when in factions section (don't advance page)
            if (WorldParamsPatch.IsInFactionsSection)
                return false;

            return true; // Let original run for world params section
        }
    }

    /// <summary>
    /// Block Escape key from going back when in factions mode, add menu, or typeahead active.
    /// </summary>
    [HarmonyPatch(typeof(Page), "OnCancelKeyPressed")]
    public class WorldParamsPatch_OnCancelKeyPressed
    {
        [HarmonyPrefix]
        public static bool Prefix(Page __instance)
        {
            // Only intercept for Page_CreateWorldParams
            if (!(__instance is Page_CreateWorldParams))
                return true;

            // Block Escape when add menu is open (we handle Escape to close menu)
            if (FactionsNavigationState.IsAddMenuOpen)
                return false;

            // Block Escape when typeahead is active (we handle Escape to clear search)
            if (FactionsNavigationState.HasActiveTypeahead)
                return false;

            // Also check WorldParams typeahead
            if (WorldParamsNavigationState.HasActiveSearch)
                return false;

            return true; // Let original run to go back to previous page
        }
    }
}
