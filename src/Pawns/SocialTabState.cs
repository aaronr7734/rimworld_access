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
    /// State handler for the Social tab in the inspection menu.
    /// Manages hierarchical navigation through ideology, relations, and social interactions.
    /// </summary>
    public static class SocialTabState
    {
        private enum MenuLevel
        {
            SectionMenu,         // Level 1: Choose section
            IdeologyInfo,        // Level 2a: View ideology info
            RelationsList,       // Level 2b: List relations
            RelationDetail,      // Level 3b: View relation details
            SocialLogList,       // Level 2c: List social interactions
        }

        private static bool isActive = false;
        private static Pawn currentPawn = null;

        private static MenuLevel currentLevel = MenuLevel.SectionMenu;
        private static int sectionIndex = 0;
        private static List<string> sections = new List<string>();

        // Ideology
        private static SocialTabHelper.IdeologyInfo ideologyInfo = null;

        // Relations
        private static List<SocialTabHelper.RelationInfo> relations = new List<SocialTabHelper.RelationInfo>();
        private static int relationIndex = 0;

        // Social log
        private static List<SocialTabHelper.SocialInteractionInfo> socialLog = new List<SocialTabHelper.SocialInteractionInfo>();
        private static int logIndex = 0;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the social tab for a pawn.
        /// </summary>
        public static void Open(Pawn pawn)
        {
            if (pawn == null)
                return;

            currentPawn = pawn;
            isActive = true;
            currentLevel = MenuLevel.SectionMenu;
            sectionIndex = 0;

            // Build sections based on what's available
            sections.Clear();
            if (ModsConfig.IdeologyActive && pawn.ideo != null)
            {
                sections.Add("Ideology & Role");
            }
            sections.Add("Relations");
            sections.Add("Social Interactions");

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the social tab.
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

                case MenuLevel.RelationsList:
                    if (relations.Count > 0)
                        relationIndex = MenuHelper.SelectNext(relationIndex, relations.Count);
                    break;

                case MenuLevel.SocialLogList:
                    if (socialLog.Count > 0)
                        logIndex = MenuHelper.SelectNext(logIndex, socialLog.Count);
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

                case MenuLevel.RelationsList:
                    if (relations.Count > 0)
                        relationIndex = MenuHelper.SelectPrevious(relationIndex, relations.Count);
                    break;

                case MenuLevel.SocialLogList:
                    if (socialLog.Count > 0)
                        logIndex = MenuHelper.SelectPrevious(logIndex, socialLog.Count);
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
                    if (section == "Ideology & Role")
                    {
                        ideologyInfo = SocialTabHelper.GetIdeologyInfo(currentPawn);
                        if (ideologyInfo == null)
                        {
                            TolkHelper.Speak("No ideology information available");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.IdeologyInfo;
                    }
                    else if (section == "Relations")
                    {
                        relations = SocialTabHelper.GetRelations(currentPawn);
                        if (relations.Count == 0)
                        {
                            TolkHelper.Speak("No relations");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.RelationsList;
                        relationIndex = 0;
                    }
                    else if (section == "Social Interactions")
                    {
                        socialLog = SocialTabHelper.GetSocialInteractions(currentPawn);
                        if (socialLog.Count == 0)
                        {
                            TolkHelper.Speak("No recent social interactions");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.SocialLogList;
                        logIndex = 0;
                    }
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.RelationsList:
                    if (relationIndex >= 0 && relationIndex < relations.Count)
                    {
                        currentLevel = MenuLevel.RelationDetail;
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
                    TolkHelper.Speak("Closed Social tab");
                    break;

                case MenuLevel.IdeologyInfo:
                case MenuLevel.RelationsList:
                case MenuLevel.SocialLogList:
                    currentLevel = MenuLevel.SectionMenu;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.RelationDetail:
                    currentLevel = MenuLevel.RelationsList;
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
                    sb.AppendLine($"Social - {sections[sectionIndex]}");
                    sb.AppendLine($"Section {MenuHelper.FormatPosition(sectionIndex, sections.Count)}");
                    sb.AppendLine("Press Enter to open");
                    break;

                case MenuLevel.IdeologyInfo:
                    if (ideologyInfo != null)
                    {
                        sb.AppendLine($"Ideology: {ideologyInfo.IdeoName}");
                        sb.AppendLine($"Certainty: {ideologyInfo.Certainty:P0}");
                        if (!string.IsNullOrEmpty(ideologyInfo.RoleName))
                        {
                            sb.AppendLine($"Role: {ideologyInfo.RoleName}");
                        }
                        sb.AppendLine();
                        sb.AppendLine(ideologyInfo.CertaintyDetails);
                        if (!string.IsNullOrEmpty(ideologyInfo.RoleDetails))
                        {
                            sb.AppendLine();
                            sb.AppendLine(ideologyInfo.RoleDetails);
                        }
                    }
                    break;

                case MenuLevel.RelationsList:
                    if (relationIndex >= 0 && relationIndex < relations.Count)
                    {
                        var relation = relations[relationIndex];
                        string relationsStr = relation.Relations.Count > 0 ? string.Join(", ", relation.Relations) : "Acquaintance";
                        sb.AppendLine($"{relation.OtherPawnName} ({relationsStr})");
                        sb.AppendLine($"My opinion: {relation.MyOpinion:+0;-0;0}");
                        sb.AppendLine($"Their opinion: {relation.TheirOpinion:+0;-0;0}");
                        sb.AppendLine($"Situation: {relation.Situation}");
                        sb.AppendLine($"Relation {MenuHelper.FormatPosition(relationIndex, relations.Count)}");
                        sb.AppendLine("Press Enter for details");
                    }
                    break;

                case MenuLevel.RelationDetail:
                    if (relationIndex >= 0 && relationIndex < relations.Count)
                    {
                        var relation = relations[relationIndex];
                        sb.AppendLine(relation.DetailedInfo);
                    }
                    break;

                case MenuLevel.SocialLogList:
                    if (logIndex >= 0 && logIndex < socialLog.Count)
                    {
                        var interaction = socialLog[logIndex];

                        // Show interaction label if available, otherwise use type
                        string interactionName = !string.IsNullOrEmpty(interaction.InteractionLabel)
                            ? interaction.InteractionLabel
                            : interaction.InteractionType;

                        sb.AppendLine($"{interactionName} - {interaction.Timestamp} ago");
                        sb.AppendLine();
                        sb.AppendLine(interaction.Description);
                        sb.AppendLine();

                        if (interaction.IsFaded)
                        {
                            sb.AppendLine("[Old interaction]");
                            sb.AppendLine();
                        }

                        sb.AppendLine($"Interaction {MenuHelper.FormatPosition(logIndex, socialLog.Count)}");
                    }
                    break;
            }

            TolkHelper.Speak(sb.ToString());
        }
    }
}
