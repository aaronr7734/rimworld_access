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
    /// State handler for the Character tab in the inspection menu.
    /// Manages hierarchical navigation through character information.
    /// </summary>
    public static class CharacterTabState
    {
        private enum MenuLevel
        {
            SectionMenu,          // Level 1: Choose section
            BasicInfo,            // Level 2a: View basic info
            BackstoriesList,      // Level 2b: List backstories
            BackstoryDetail,      // Level 3b: View backstory details
            TraitsList,           // Level 2c: List traits
            TraitDetail,          // Level 3c: View trait details
            IncapacitiesList,     // Level 2d: List incapacities
            IncapacityDetail,     // Level 3d: View incapacity details
            AbilitiesList,        // Level 2e: List abilities
            AbilityDetail         // Level 3e: View ability details
        }

        private static bool isActive = false;
        private static Pawn currentPawn = null;

        private static MenuLevel currentLevel = MenuLevel.SectionMenu;
        private static int sectionIndex = 0;
        private static List<string> sections = new List<string>();

        // Basic info
        private static CharacterTabHelper.BasicInfo basicInfo = null;

        // Backstories
        private static List<CharacterTabHelper.BackstoryInfo> backstories = new List<CharacterTabHelper.BackstoryInfo>();
        private static int backstoryIndex = 0;

        // Traits
        private static List<CharacterTabHelper.TraitInfo> traits = new List<CharacterTabHelper.TraitInfo>();
        private static int traitIndex = 0;

        // Incapacities
        private static List<CharacterTabHelper.IncapacityInfo> incapacities = new List<CharacterTabHelper.IncapacityInfo>();
        private static int incapacityIndex = 0;

        // Abilities
        private static List<CharacterTabHelper.AbilityInfo> abilities = new List<CharacterTabHelper.AbilityInfo>();
        private static int abilityIndex = 0;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the character tab for a pawn.
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
            sections.Add("Basic Info");
            if (pawn.story != null)
            {
                sections.Add("Backstories");
                sections.Add("Traits");
            }
            sections.Add("Incapacities");
            if (pawn.abilities != null && pawn.abilities.AllAbilitiesForReading.Count > 0)
            {
                sections.Add("Abilities");
            }

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the character tab.
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

                case MenuLevel.BackstoriesList:
                    if (backstories.Count > 0)
                        backstoryIndex = MenuHelper.SelectNext(backstoryIndex, backstories.Count);
                    break;

                case MenuLevel.TraitsList:
                    if (traits.Count > 0)
                        traitIndex = MenuHelper.SelectNext(traitIndex, traits.Count);
                    break;

                case MenuLevel.IncapacitiesList:
                    if (incapacities.Count > 0)
                        incapacityIndex = MenuHelper.SelectNext(incapacityIndex, incapacities.Count);
                    break;

                case MenuLevel.AbilitiesList:
                    if (abilities.Count > 0)
                        abilityIndex = MenuHelper.SelectNext(abilityIndex, abilities.Count);
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

                case MenuLevel.BackstoriesList:
                    if (backstories.Count > 0)
                        backstoryIndex = MenuHelper.SelectPrevious(backstoryIndex, backstories.Count);
                    break;

                case MenuLevel.TraitsList:
                    if (traits.Count > 0)
                        traitIndex = MenuHelper.SelectPrevious(traitIndex, traits.Count);
                    break;

                case MenuLevel.IncapacitiesList:
                    if (incapacities.Count > 0)
                        incapacityIndex = MenuHelper.SelectPrevious(incapacityIndex, incapacities.Count);
                    break;

                case MenuLevel.AbilitiesList:
                    if (abilities.Count > 0)
                        abilityIndex = MenuHelper.SelectPrevious(abilityIndex, abilities.Count);
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
                    if (section == "Basic Info")
                    {
                        basicInfo = CharacterTabHelper.GetBasicInfo(currentPawn);
                        currentLevel = MenuLevel.BasicInfo;
                    }
                    else if (section == "Backstories")
                    {
                        backstories = CharacterTabHelper.GetBackstories(currentPawn);
                        if (backstories.Count == 0)
                        {
                            TolkHelper.Speak("No backstories");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.BackstoriesList;
                        backstoryIndex = 0;
                    }
                    else if (section == "Traits")
                    {
                        traits = CharacterTabHelper.GetTraits(currentPawn);
                        if (traits.Count == 0)
                        {
                            TolkHelper.Speak("No traits");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.TraitsList;
                        traitIndex = 0;
                    }
                    else if (section == "Incapacities")
                    {
                        incapacities = CharacterTabHelper.GetIncapacities(currentPawn);
                        if (incapacities.Count == 0)
                        {
                            TolkHelper.Speak("No incapacities");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.IncapacitiesList;
                        incapacityIndex = 0;
                    }
                    else if (section == "Abilities")
                    {
                        abilities = CharacterTabHelper.GetAbilities(currentPawn);
                        if (abilities.Count == 0)
                        {
                            TolkHelper.Speak("No abilities");
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            return;
                        }
                        currentLevel = MenuLevel.AbilitiesList;
                        abilityIndex = 0;
                    }
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.BackstoriesList:
                    if (backstoryIndex >= 0 && backstoryIndex < backstories.Count)
                    {
                        currentLevel = MenuLevel.BackstoryDetail;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                    }
                    break;

                case MenuLevel.TraitsList:
                    if (traitIndex >= 0 && traitIndex < traits.Count)
                    {
                        currentLevel = MenuLevel.TraitDetail;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                    }
                    break;

                case MenuLevel.IncapacitiesList:
                    if (incapacityIndex >= 0 && incapacityIndex < incapacities.Count)
                    {
                        currentLevel = MenuLevel.IncapacityDetail;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        AnnounceCurrentSelection();
                    }
                    break;

                case MenuLevel.AbilitiesList:
                    if (abilityIndex >= 0 && abilityIndex < abilities.Count)
                    {
                        currentLevel = MenuLevel.AbilityDetail;
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
                    TolkHelper.Speak("Closed Character tab");
                    break;

                case MenuLevel.BasicInfo:
                case MenuLevel.BackstoriesList:
                case MenuLevel.TraitsList:
                case MenuLevel.IncapacitiesList:
                case MenuLevel.AbilitiesList:
                    currentLevel = MenuLevel.SectionMenu;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.BackstoryDetail:
                    currentLevel = MenuLevel.BackstoriesList;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.TraitDetail:
                    currentLevel = MenuLevel.TraitsList;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.IncapacityDetail:
                    currentLevel = MenuLevel.IncapacitiesList;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceCurrentSelection();
                    break;

                case MenuLevel.AbilityDetail:
                    currentLevel = MenuLevel.AbilitiesList;
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
                    sb.AppendLine($"Character - {sections[sectionIndex]}");
                    sb.AppendLine($"Section {MenuHelper.FormatPosition(sectionIndex, sections.Count)}");
                    sb.AppendLine("Press Enter to open");
                    break;

                case MenuLevel.BasicInfo:
                    if (basicInfo != null)
                    {
                        sb.AppendLine($"Name: {basicInfo.Name}");
                        sb.AppendLine($"Age: {basicInfo.BiologicalAge} years");
                        sb.AppendLine($"Gender: {basicInfo.Gender}");
                        sb.AppendLine($"Race: {basicInfo.Race}");
                        sb.AppendLine($"Faction: {basicInfo.Faction}");
                        if (!string.IsNullOrEmpty(basicInfo.Xenotype))
                        {
                            sb.AppendLine($"Xenotype: {basicInfo.Xenotype}");
                        }
                        if (!string.IsNullOrEmpty(basicInfo.Ideology))
                        {
                            sb.AppendLine($"Ideology: {basicInfo.Ideology}");
                        }
                        if (!string.IsNullOrEmpty(basicInfo.Role))
                        {
                            sb.AppendLine($"Role: {basicInfo.Role}");
                        }
                    }
                    break;

                case MenuLevel.BackstoriesList:
                    if (backstoryIndex >= 0 && backstoryIndex < backstories.Count)
                    {
                        var backstory = backstories[backstoryIndex];
                        sb.AppendLine($"{backstory.Title}");
                        sb.AppendLine($"Backstory {MenuHelper.FormatPosition(backstoryIndex, backstories.Count)}");
                        sb.AppendLine("Press Enter for details");
                    }
                    break;

                case MenuLevel.BackstoryDetail:
                    if (backstoryIndex >= 0 && backstoryIndex < backstories.Count)
                    {
                        var backstory = backstories[backstoryIndex];
                        sb.AppendLine(backstory.DetailedInfo);
                    }
                    break;

                case MenuLevel.TraitsList:
                    if (traitIndex >= 0 && traitIndex < traits.Count)
                    {
                        var trait = traits[traitIndex];
                        sb.AppendLine($"{trait.Label}");
                        sb.AppendLine($"Trait {MenuHelper.FormatPosition(traitIndex, traits.Count)}");
                        sb.AppendLine("Press Enter for details");
                    }
                    break;

                case MenuLevel.TraitDetail:
                    if (traitIndex >= 0 && traitIndex < traits.Count)
                    {
                        var trait = traits[traitIndex];
                        sb.AppendLine(trait.DetailedInfo);
                    }
                    break;

                case MenuLevel.IncapacitiesList:
                    if (incapacityIndex >= 0 && incapacityIndex < incapacities.Count)
                    {
                        var incapacity = incapacities[incapacityIndex];
                        sb.AppendLine($"{incapacity.Label}");
                        sb.AppendLine($"Incapacity {MenuHelper.FormatPosition(incapacityIndex, incapacities.Count)}");
                        sb.AppendLine("Press Enter for details");
                    }
                    break;

                case MenuLevel.IncapacityDetail:
                    if (incapacityIndex >= 0 && incapacityIndex < incapacities.Count)
                    {
                        var incapacity = incapacities[incapacityIndex];
                        sb.AppendLine(incapacity.DetailedInfo);
                    }
                    break;

                case MenuLevel.AbilitiesList:
                    if (abilityIndex >= 0 && abilityIndex < abilities.Count)
                    {
                        var ability = abilities[abilityIndex];
                        sb.AppendLine($"{ability.Label}");
                        sb.AppendLine($"Ability {MenuHelper.FormatPosition(abilityIndex, abilities.Count)}");
                        sb.AppendLine("Press Enter for details");
                    }
                    break;

                case MenuLevel.AbilityDetail:
                    if (abilityIndex >= 0 && abilityIndex < abilities.Count)
                    {
                        var ability = abilities[abilityIndex];
                        sb.AppendLine(ability.DetailedInfo);
                    }
                    break;
            }

            TolkHelper.Speak(sb.ToString());
        }
    }
}
