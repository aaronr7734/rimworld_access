using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// State handler for the Needs tab in the inspection menu.
    /// Manages hierarchical navigation through needs and thoughts.
    /// </summary>
    public static class NeedsTabState
    {
        private enum MenuLevel
        {
            SectionMenu,      // Level 1: Choose section (Needs/Thoughts)
            NeedsList,        // Level 2a: List all needs
            NeedDetail,       // Level 3a: View need details
            ThoughtsList,     // Level 2b: List all thoughts
            ThoughtDetail     // Level 3b: View thought details
        }

        private static bool isActive = false;
        private static Pawn currentPawn = null;

        private static MenuLevel currentLevel = MenuLevel.SectionMenu;
        private static int sectionIndex = 0;
        private static List<string> sections = new List<string>();

        // Needs
        private static List<NeedsTabHelper.NeedInfo> needs = new List<NeedsTabHelper.NeedInfo>();
        private static int needIndex = 0;

        // Thoughts
        private static List<NeedsTabHelper.ThoughtInfo> thoughts = new List<NeedsTabHelper.ThoughtInfo>();
        private static int thoughtIndex = 0;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the needs tab for a pawn.
        /// </summary>
        public static void Open(Pawn pawn)
        {
            if (pawn == null)
                return;

            currentPawn = pawn;
            isActive = true;
            currentLevel = MenuLevel.SectionMenu;
            sectionIndex = 0;

            // Build sections based on pawn type
            sections.Clear();
            sections.Add("Needs");
            if (pawn.needs?.mood != null) // Only humanlike pawns have mood/thoughts
            {
                sections.Add("Mood & Thoughts");
            }

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the needs tab.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentPawn = null;
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Handles keyboard input.
        /// </summary>
        public static bool HandleInput(Event evt)
        {
            if (!isActive || evt.type != EventType.KeyDown)
                return false;

            KeyCode key = evt.keyCode;

            // Handle Escape - go back or close
            if (key == KeyCode.Escape)
            {
                evt.Use();
                GoBack();
                return true;
            }

            // Handle arrow keys
            if (key == KeyCode.UpArrow)
            {
                evt.Use();
                SelectPrevious();
                return true;
            }

            if (key == KeyCode.DownArrow)
            {
                evt.Use();
                SelectNext();
                return true;
            }

            // Handle Enter - drill down
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                evt.Use();
                DrillDown();
                return true;
            }

            return false;
        }

        private static void SelectNext()
        {
            switch (currentLevel)
            {
                case MenuLevel.SectionMenu:
                    sectionIndex = MenuHelper.SelectNext(sectionIndex, sections.Count);
                    break;

                case MenuLevel.NeedsList:
                    if (needs.Count > 0)
                        needIndex = MenuHelper.SelectNext(needIndex, needs.Count);
                    break;

                case MenuLevel.ThoughtsList:
                    if (thoughts.Count > 0)
                        thoughtIndex = MenuHelper.SelectNext(thoughtIndex, thoughts.Count);
                    break;
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        private static void SelectPrevious()
        {
            switch (currentLevel)
            {
                case MenuLevel.SectionMenu:
                    sectionIndex = MenuHelper.SelectPrevious(sectionIndex, sections.Count);
                    break;

                case MenuLevel.NeedsList:
                    if (needs.Count > 0)
                        needIndex = MenuHelper.SelectPrevious(needIndex, needs.Count);
                    break;

                case MenuLevel.ThoughtsList:
                    if (thoughts.Count > 0)
                        thoughtIndex = MenuHelper.SelectPrevious(thoughtIndex, thoughts.Count);
                    break;
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        private static void DrillDown()
        {
            switch (currentLevel)
            {
                case MenuLevel.SectionMenu:
                    string section = sections[sectionIndex];
                    if (section == "Needs")
                    {
                        needs = NeedsTabHelper.GetNeeds(currentPawn);
                        if (needs.Count == 0)
                        {
                            TolkHelper.Speak("No needs information available");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.NeedsList;
                        needIndex = 0;
                    }
                    else if (section == "Mood & Thoughts")
                    {
                        thoughts = NeedsTabHelper.GetThoughts(currentPawn);
                        if (thoughts.Count == 0)
                        {
                            TolkHelper.Speak("No thoughts");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.ThoughtsList;
                        thoughtIndex = 0;
                    }
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.NeedsList:
                    if (needIndex >= 0 && needIndex < needs.Count)
                    {
                        currentLevel = MenuLevel.NeedDetail;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                    }
                    break;

                case MenuLevel.ThoughtsList:
                    if (thoughtIndex >= 0 && thoughtIndex < thoughts.Count)
                    {
                        currentLevel = MenuLevel.ThoughtDetail;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                    }
                    break;
            }
        }

        private static void GoBack()
        {
            switch (currentLevel)
            {
                case MenuLevel.SectionMenu:
                    Close();
                    TolkHelper.Speak("Closed Needs tab");
                    break;

                case MenuLevel.NeedsList:
                case MenuLevel.ThoughtsList:
                    currentLevel = MenuLevel.SectionMenu;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.NeedDetail:
                    currentLevel = MenuLevel.NeedsList;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.ThoughtDetail:
                    currentLevel = MenuLevel.ThoughtsList;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;
            }
        }

        private static void AnnounceCurrentSelection()
        {
            var sb = new StringBuilder();

            switch (currentLevel)
            {
                case MenuLevel.SectionMenu:
                    sb.AppendLine($"Needs - {sections[sectionIndex]}");
                    sb.AppendLine($"Section {MenuHelper.FormatPosition(sectionIndex, sections.Count)}");
                    sb.AppendLine("Press Enter to open");
                    break;

                case MenuLevel.NeedsList:
                    if (needIndex >= 0 && needIndex < needs.Count)
                    {
                        var need = needs[needIndex];
                        sb.AppendLine($"{need.Label}: {need.Percentage:F0}%{need.Arrow}");
                        sb.AppendLine($"Need {MenuHelper.FormatPosition(needIndex, needs.Count)}");
                        sb.AppendLine("Press Enter for details");
                    }
                    break;

                case MenuLevel.NeedDetail:
                    if (needIndex >= 0 && needIndex < needs.Count)
                    {
                        var need = needs[needIndex];
                        sb.AppendLine(need.DetailedInfo);
                    }
                    break;

                case MenuLevel.ThoughtsList:
                    if (thoughtIndex >= 0 && thoughtIndex < thoughts.Count)
                    {
                        var thought = thoughts[thoughtIndex];
                        string effectStr = thought.MoodEffect >= 0 ? $"+{thought.MoodEffect:F0}" : $"{thought.MoodEffect:F0}";
                        string stackStr = thought.StackCount > 1 ? $" x{thought.StackCount}" : "";
                        sb.AppendLine($"{thought.Label}{stackStr}: {effectStr}");
                        sb.AppendLine($"Thought {MenuHelper.FormatPosition(thoughtIndex, thoughts.Count)}");
                        sb.AppendLine("Press Enter for details");
                    }
                    break;

                case MenuLevel.ThoughtDetail:
                    if (thoughtIndex >= 0 && thoughtIndex < thoughts.Count)
                    {
                        var thought = thoughts[thoughtIndex];
                        sb.AppendLine(thought.DetailedInfo);
                    }
                    break;
            }

            TolkHelper.Speak(sb.ToString());
        }
    }
}
