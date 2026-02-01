using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Page_CreateWorldParams))]
    [HarmonyPatch("DoWindowContents")]
    public class WorldParamsPatch
    {
        private static bool hasAnnouncedTitle = false;

        static void Prefix(Page_CreateWorldParams __instance, Rect rect)
        {
            try
            {
                // Initialize navigation state with the instance (syncs game values)
                WorldParamsNavigationState.Initialize(__instance);

                // Announce window title once
                if (!hasAnnouncedTitle)
                {
                    string help = "Use Up/Down to navigate fields, Left/Right to change values. R to randomize seed.";
                    TolkHelper.Speak($"Create World. {help}");
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
            KeyCode keyCode = evt.keyCode;

            // Seed editing mode has priority
            if (WorldParamsNavigationState.IsEditingSeed)
            {
                HandleSeedEditing(keyCode, evt);
                return;
            }

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

        private static void HandleSeedEditing(KeyCode keyCode, Event evt)
        {
            if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter)
            {
                WorldParamsNavigationState.ConfirmSeedEdit();
                evt.Use();
            }
            else if (keyCode == KeyCode.Escape)
            {
                WorldParamsNavigationState.CancelSeedEdit();
                evt.Use();
            }
            else if (keyCode == KeyCode.Backspace)
            {
                WorldParamsNavigationState.RemoveCharFromSeedBuffer();
                evt.Use();
            }
            else if (evt.character != '\0' && !char.IsControl(evt.character))
            {
                WorldParamsNavigationState.AddCharToSeedBuffer(evt.character);
                evt.Use();
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
                    text = $"[Typing Seed: {WorldParamsNavigationState.SeedInputBuffer}] (Enter=Confirm, Esc=Cancel)";
                }
                else
                {
                    string fieldName = WorldParamsNavigationState.GetCurrentFieldName();
                    text = $"[Field: {fieldName}] (Arrow keys to navigate)";
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
        }
    }
}
