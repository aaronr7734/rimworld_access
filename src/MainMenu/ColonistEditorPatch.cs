using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Page_ConfigureStartingPawns))]
    [HarmonyPatch("DoWindowContents")]
    public class ColonistEditorPatch
    {
        private static bool patchActive = false;
        private static bool hasAnnouncedTitle = false;

        // Prefix: Initialize state and handle keyboard input
        static void Prefix(Page_ConfigureStartingPawns __instance, Rect rect)
        {
            try
            {
                // Initialize navigation state
                ColonistEditorNavigationState.Initialize();

                // Announce window title and initial colonist once
                if (!hasAnnouncedTitle)
                {
                    string pageTitle = "Create Characters";
                    List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
                    if (pawns != null && pawns.Count > 0)
                    {
                        Pawn firstPawn = pawns[0];
                        string name = firstPawn.Name is NameTriple triple
                            ? $"{triple.First} '{triple.Nick}' {triple.Last}"
                            : firstPawn.LabelShort;
                        TolkHelper.Speak($"{pageTitle} - {name} - {firstPawn.story.TitleCap} (Age {firstPawn.ageTracker.AgeBiologicalYears}). Use arrows to navigate, Tab for sections, R to randomize, H for help.");
                    }
                    else
                    {
                        TolkHelper.Speak(pageTitle);
                    }
                    hasAnnouncedTitle = true;
                }

                // Sync current pawn index with the page
                int pagePawnIndex = (int)AccessTools.Field(typeof(Page_ConfigureStartingPawns), "curPawnIndex").GetValue(__instance);
                if (pagePawnIndex != ColonistEditorNavigationState.CurrentPawnIndex)
                {
                    // Page changed the pawn, update our state
                    // For now, we'll let our navigation take precedence
                }

                // Handle text input when in text edit mode
                if (Event.current.type == EventType.KeyDown && ColonistEditorNavigationState.IsInNameTextEditMode())
                {
                    KeyCode keyCode = Event.current.keyCode;

                    if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter)
                    {
                        ColonistEditorNavigationState.SaveTextEdit();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.Escape)
                    {
                        ColonistEditorNavigationState.CancelTextEdit();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.Backspace)
                    {
                        ColonistEditorNavigationState.HandleBackspace();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (Event.current.character != '\0')
                    {
                        ColonistEditorNavigationState.HandleTextInput(Event.current.character);
                        Event.current.Use();
                        patchActive = true;
                    }
                }
                // Handle keyboard input
                else if (Event.current.type == EventType.KeyDown)
                {
                    KeyCode keyCode = Event.current.keyCode;

                    if (keyCode == KeyCode.UpArrow)
                    {
                        ColonistEditorNavigationState.NavigateUp();
                        UpdatePagePawnIndex(__instance);
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.DownArrow)
                    {
                        ColonistEditorNavigationState.NavigateDown();
                        UpdatePagePawnIndex(__instance);
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.RightArrow)
                    {
                        ColonistEditorNavigationState.DrillIn();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.LeftArrow)
                    {
                        ColonistEditorNavigationState.DrillOut();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.R)
                    {
                        ColonistEditorNavigationState.RandomizePawn();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.H)
                    {
                        // Help - show current mode
                        string help = ColonistEditorNavigationState.GetCurrentModeDescription();
                        TolkHelper.Speak(help);
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.I)
                    {
                        // Info card
                        ColonistEditorNavigationState.OpenInfoCard();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.E)
                    {
                        // Edit name
                        ColonistEditorNavigationState.EnterNameEditMode();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.Space)
                    {
                        // Begin swap selection
                        ColonistEditorNavigationState.BeginPawnSwap();
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.A)
                    {
                        // Add new pawn
                        ColonistEditorNavigationState.AddNewPawn();
                        UpdatePagePawnIndex(__instance);
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.Delete)
                    {
                        // Remove pawn
                        ColonistEditorNavigationState.RemoveCurrentPawn();
                        UpdatePagePawnIndex(__instance);
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter)
                    {
                        // Check mode and handle accordingly
                        if (ColonistEditorNavigationState.IsInSwapMode())
                        {
                            // Confirm swap
                            ColonistEditorNavigationState.ConfirmPawnSwap();
                        }
                        else if (ColonistEditorNavigationState.IsInNameEditMode())
                        {
                            // In name edit mode, enter text edit or save
                            ColonistEditorNavigationState.EnterTextEditMode();
                        }
                        else
                        {
                            // Begin game
                            if (ColonistEditorNavigationState.BeginGame())
                            {
                                // Use reflection to call protected DoNext() method
                                AccessTools.Method(typeof(Page_ConfigureStartingPawns), "DoNext").Invoke(__instance, null);
                            }
                        }
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.Escape)
                    {
                        // Handle escape based on current mode
                        if (ColonistEditorNavigationState.IsInInfoCardMode())
                        {
                            ColonistEditorNavigationState.CloseInfoCard();
                        }
                        else if (ColonistEditorNavigationState.IsInSwapMode())
                        {
                            ColonistEditorNavigationState.CancelPawnSwap();
                        }
                        else if (ColonistEditorNavigationState.IsInNameEditMode())
                        {
                            ColonistEditorNavigationState.CancelTextEdit();
                        }
                        Event.current.Use();
                        patchActive = true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in ColonistEditorPatch Prefix: {ex}");
            }
        }

        private static void UpdatePagePawnIndex(Page_ConfigureStartingPawns instance)
        {
            AccessTools.Field(typeof(Page_ConfigureStartingPawns), "curPawnIndex")
                .SetValue(instance, ColonistEditorNavigationState.CurrentPawnIndex);
        }

        // Postfix: Draw visual indicator
        static void Postfix(Page_ConfigureStartingPawns __instance, Rect rect)
        {
            try
            {
                if (!patchActive) return;

                // Draw mode indicator at the top
                Rect indicatorRect = new Rect(rect.x + 10f, rect.y + 10f, 700f, 50f);

                Widgets.DrawBoxSolid(indicatorRect, new Color(0.2f, 0.2f, 0.2f, 0.8f));

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;

                string modeText = ColonistEditorNavigationState.GetCurrentModeDescription();
                Widgets.Label(indicatorRect.ContractedBy(5f), modeText + "\nPress H for help");

                Text.Anchor = TextAnchor.UpperLeft;

                // Highlight current pawn in the list
                DrawPawnHighlight(rect);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in ColonistEditorPatch Postfix: {ex}");
            }
        }

        private static void DrawPawnHighlight(Rect rect)
        {
            try
            {
                int currentPawnIndex = ColonistEditorNavigationState.CurrentPawnIndex;
                int startingPawnCount = Find.GameInitData.startingPawnCount;

                // Calculate highlight position
                // Pawn list is on the left, 140px wide
                // Each pawn entry is 60px tall
                // There's a 22px label "Starting Pawns Selected"

                float leftPanelX = rect.x;
                float leftPanelY = rect.y + 45f; // After title
                float pawnEntryHeight = 60f;
                float labelHeight = 22f;

                // Calculate Y offset
                float yOffset = labelHeight + (currentPawnIndex * pawnEntryHeight);

                // Account for "Starting Pawns Left Behind" label if applicable
                if (currentPawnIndex >= startingPawnCount)
                {
                    yOffset += labelHeight; // Extra label
                }

                Rect highlightRect = new Rect(
                    leftPanelX + 4f,
                    leftPanelY + yOffset + 4f,
                    132f,
                    56f
                );

                // Draw highlight border - different colors for selected vs deselected
                Color highlightColor;
                if (currentPawnIndex < startingPawnCount)
                {
                    // Selected pawn - green highlight
                    highlightColor = new Color(0.2f, 0.8f, 0.2f, 0.6f);
                }
                else
                {
                    // Deselected pawn - gray highlight
                    highlightColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
                }

                Widgets.DrawBox(highlightRect, 2);

                // Also draw a filled background with the color
                Color bgColor = highlightColor;
                bgColor.a = 0.2f;
                Widgets.DrawBoxSolid(highlightRect, bgColor);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error drawing pawn highlight: {ex}");
            }
        }

        public static void ResetAnnouncement()
        {
            hasAnnouncedTitle = false;
        }
    }

    // Separate patch to reset state when page opens
    [HarmonyPatch(typeof(Page_ConfigureStartingPawns), "PreOpen")]
    public class ColonistEditorPatch_PreOpen
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            ColonistEditorPatch.ResetAnnouncement();
            ColonistEditorNavigationState.Reset();
        }
    }
}
