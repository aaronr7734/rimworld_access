using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace RimWorldAccess
{
    public static class ColonistEditorNavigationState
    {
        private static bool initialized = false;
        private static int currentPawnIndex = 0;
        private static NavigationMode currentMode = NavigationMode.PawnList;
        private static int currentSectionIndex = 0;
        private static int currentDetailIndex = 0;
        private static int currentNameField = 0; // 0=First, 1=Nick, 2=Last, 3=Save Changes
        private static string editingFirstName = "";
        private static string editingNickName = "";
        private static string editingLastName = "";
        private static NavigationMode previousMode = NavigationMode.PawnList; // Store mode before info card
        private static string currentTextInput = ""; // For text editing mode
        private static int swapSourceIndex = -1; // Index of pawn to swap
        private static int swapTargetIndex = 0; // Index being navigated for swap target

        private enum NavigationMode
        {
            PawnList,      // Navigating between pawns
            SectionList,   // Navigating between sections (Skills, Health, Relations, etc.)
            DetailView,    // Viewing details within a section
            NameEditMenu,  // Menu to select which name field to edit
            NameTextEdit,  // Actually typing in a name field
            InfoCard,      // Viewing info card
            SwapSelect     // Selecting which pawn to swap with
        }

        private enum Section
        {
            Biography,
            Skills,
            Health,
            Relations,
            Traits,
            Gear,
            IncapableOf
        }

        private static readonly List<Section> availableSections = new List<Section>
        {
            Section.Biography,
            Section.Skills,
            Section.Health,
            Section.Relations,
            Section.Traits,
            Section.Gear,
            Section.IncapableOf
        };

        public static void Initialize()
        {
            if (!initialized)
            {
                currentPawnIndex = 0;
                currentMode = NavigationMode.PawnList;
                currentSectionIndex = 0;
                currentDetailIndex = 0;
                initialized = true;
            }
        }

        public static void Reset()
        {
            initialized = false;
            currentPawnIndex = 0;
            currentMode = NavigationMode.PawnList;
            currentSectionIndex = 0;
            currentDetailIndex = 0;
            currentNameField = 0;
            editingFirstName = "";
            editingNickName = "";
            editingLastName = "";
            previousMode = NavigationMode.PawnList;
            swapSourceIndex = -1;
            swapTargetIndex = 0;
        }

        public static int CurrentPawnIndex => currentPawnIndex;
        public static bool IsInNameEditMode() => currentMode == NavigationMode.NameEditMenu || currentMode == NavigationMode.NameTextEdit;
        public static bool IsInNameTextEditMode() => currentMode == NavigationMode.NameTextEdit;
        public static bool IsInInfoCardMode() => currentMode == NavigationMode.InfoCard;
        public static bool IsInSwapMode() => currentMode == NavigationMode.SwapSelect;

        public static void NavigateUp()
        {
            switch (currentMode)
            {
                case NavigationMode.PawnList:
                    NavigatePawnUp();
                    break;
                case NavigationMode.SectionList:
                    NavigateSectionUp();
                    break;
                case NavigationMode.DetailView:
                    NavigateDetailUp();
                    break;
                case NavigationMode.NameEditMenu:
                    NavigateNameFields(true);
                    break;
                case NavigationMode.SwapSelect:
                    NavigateSwapTargetUp();
                    break;
            }
        }

        public static void NavigateDown()
        {
            switch (currentMode)
            {
                case NavigationMode.PawnList:
                    NavigatePawnDown();
                    break;
                case NavigationMode.SectionList:
                    NavigateSectionDown();
                    break;
                case NavigationMode.DetailView:
                    NavigateDetailDown();
                    break;
                case NavigationMode.NameEditMenu:
                    NavigateNameFields(false);
                    break;
                case NavigationMode.SwapSelect:
                    NavigateSwapTargetDown();
                    break;
            }
        }

        public static void EnterMode()
        {
            // Deprecated - functionality moved to DrillIn() via right arrow
            // Keeping method for backward compatibility but it does nothing
        }

        public static void DrillIn()
        {
            // Right arrow - enter section navigation from pawn list, or drill into details from section list
            if (currentMode == NavigationMode.PawnList)
            {
                currentMode = NavigationMode.SectionList;
                currentSectionIndex = 0;
                CopySectionToClipboard();
            }
            else if (currentMode == NavigationMode.SectionList)
            {
                currentMode = NavigationMode.DetailView;
                currentDetailIndex = 0;
                CopyDetailToClipboard();
            }
        }

        public static void DrillOut()
        {
            // Left arrow - go back
            if (currentMode == NavigationMode.InfoCard)
            {
                // Return from info card to previous mode
                currentMode = previousMode;
                if (currentMode == NavigationMode.DetailView)
                {
                    CopyDetailToClipboard();
                }
                else if (currentMode == NavigationMode.SectionList)
                {
                    CopySectionToClipboard();
                }
                else
                {
                    CopyPawnToClipboard();
                }
            }
            else if (currentMode == NavigationMode.DetailView)
            {
                currentMode = NavigationMode.SectionList;
                CopySectionToClipboard();
            }
            else if (currentMode == NavigationMode.SectionList)
            {
                currentMode = NavigationMode.PawnList;
                CopyPawnToClipboard();
            }
        }

        public static void CloseInfoCard()
        {
            if (currentMode != NavigationMode.InfoCard) return;

            // Return to previous mode
            currentMode = previousMode;
            if (currentMode == NavigationMode.DetailView)
            {
                CopyDetailToClipboard();
            }
            else if (currentMode == NavigationMode.SectionList)
            {
                CopySectionToClipboard();
            }
            else
            {
                CopyPawnToClipboard();
            }
        }

        public static void RandomizePawn()
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            if (currentPawnIndex < 0 || currentPawnIndex >= pawns.Count) return;

            StartingPawnUtility.RandomizePawn(currentPawnIndex);
            TolkHelper.Speak($"Randomized pawn {currentPawnIndex + 1}");
            CopyPawnToClipboard();
        }

        private static void NavigatePawnUp()
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            if (pawns.Count == 0) return;

            currentPawnIndex--;
            if (currentPawnIndex < 0)
                currentPawnIndex = pawns.Count - 1;

            CopyPawnToClipboard();
        }

        private static void NavigatePawnDown()
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            if (pawns.Count == 0) return;

            currentPawnIndex++;
            if (currentPawnIndex >= pawns.Count)
                currentPawnIndex = 0;

            CopyPawnToClipboard();
        }

        private static void NavigateSectionUp()
        {
            currentSectionIndex--;
            if (currentSectionIndex < 0)
                currentSectionIndex = availableSections.Count - 1;

            CopySectionToClipboard();
        }

        private static void NavigateSectionDown()
        {
            currentSectionIndex++;
            if (currentSectionIndex >= availableSections.Count)
                currentSectionIndex = 0;

            CopySectionToClipboard();
        }

        private static void NavigateDetailUp()
        {
            int maxDetails = GetDetailCountForCurrentSection();
            if (maxDetails == 0) return;

            currentDetailIndex--;
            if (currentDetailIndex < 0)
                currentDetailIndex = maxDetails - 1;

            CopyDetailToClipboard();
        }

        private static void NavigateDetailDown()
        {
            int maxDetails = GetDetailCountForCurrentSection();
            if (maxDetails == 0) return;

            currentDetailIndex++;
            if (currentDetailIndex >= maxDetails)
                currentDetailIndex = 0;

            CopyDetailToClipboard();
        }

        private static void CopyPawnToClipboard()
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            if (currentPawnIndex < 0 || currentPawnIndex >= pawns.Count) return;

            Pawn pawn = pawns[currentPawnIndex];
            bool isSelected = currentPawnIndex < Find.GameInitData.startingPawnCount;
            string status = isSelected ? "Starting" : "Left Behind";
            string selectedPrefix = isSelected ? "Selected " : "";

            string name = pawn.Name is NameTriple triple
                ? $"{triple.First} '{triple.Nick}' {triple.Last}"
                : pawn.LabelShort;

            string text = $"[Pawn {currentPawnIndex + 1}/{pawns.Count}] {selectedPrefix}{name} - {pawn.story.TitleCap} ({status}) - Age {pawn.ageTracker.AgeBiologicalYears}";

            TolkHelper.Speak(text);
        }

        private static void CopySectionToClipboard()
        {
            Section section = availableSections[currentSectionIndex];
            string sectionName = section.ToString();
            int detailCount = GetDetailCountForSection(section);

            string text = $"[Section] {sectionName} ({detailCount} items) - Press Right Arrow to view details, Left Arrow to go back";
            TolkHelper.Speak(text);
        }

        private static void CopyDetailToClipboard()
        {
            Section section = availableSections[currentSectionIndex];
            string detailText = GetDetailText(section, currentDetailIndex);

            TolkHelper.Speak(detailText);
        }

        private static int GetDetailCountForCurrentSection()
        {
            return GetDetailCountForSection(availableSections[currentSectionIndex]);
        }

        private static int GetDetailCountForSection(Section section)
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            if (currentPawnIndex < 0 || currentPawnIndex >= pawns.Count) return 0;

            Pawn pawn = pawns[currentPawnIndex];

            switch (section)
            {
                case Section.Biography:
                    return 1; // Just backstory

                case Section.Skills:
                    return pawn.skills?.skills?.Count ?? 0;

                case Section.Health:
                    return pawn.health?.hediffSet?.hediffs?.Count ?? 0;

                case Section.Relations:
                    return pawn.relations?.DirectRelations?.Count ?? 0;

                case Section.Traits:
                    return pawn.story?.traits?.allTraits?.Count ?? 0;

                case Section.Gear:
                    return pawn.equipment?.AllEquipmentListForReading?.Count ?? 0;

                case Section.IncapableOf:
                    return GetIncapableOfCount(pawn);

                default:
                    return 0;
            }
        }

        private static string GetDetailText(Section section, int index)
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            if (currentPawnIndex < 0 || currentPawnIndex >= pawns.Count)
                return "Invalid pawn index";

            Pawn pawn = pawns[currentPawnIndex];

            switch (section)
            {
                case Section.Biography:
                    return GetBiographyText(pawn);

                case Section.Skills:
                    return GetSkillText(pawn, index);

                case Section.Health:
                    return GetHealthText(pawn, index);

                case Section.Relations:
                    return GetRelationText(pawn, index);

                case Section.Traits:
                    return GetTraitText(pawn, index);

                case Section.Gear:
                    return GetGearText(pawn, index);

                case Section.IncapableOf:
                    return GetIncapableOfText(pawn, index);

                default:
                    return "Unknown section";
            }
        }

        private static string GetBiographyText(Pawn pawn)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"Name: {pawn.Name}");
            sb.AppendLine($"Gender: {pawn.gender}");
            sb.AppendLine($"Age: {pawn.ageTracker.AgeBiologicalYears}");

            if (pawn.story != null)
            {
                sb.AppendLine($"Childhood: {pawn.story.Childhood?.title ?? "None"}");
                if (pawn.story.Adulthood != null)
                {
                    sb.AppendLine($"Adulthood: {pawn.story.Adulthood.title}");
                }
            }

            return sb.ToString();
        }

        private static string GetSkillText(Pawn pawn, int index)
        {
            if (pawn.skills?.skills == null || index < 0 || index >= pawn.skills.skills.Count)
                return "Invalid skill index";

            SkillRecord skill = pawn.skills.skills[index];

            string passionText = "";
            if (skill.passion == Passion.Minor) passionText = " (Interested)";
            if (skill.passion == Passion.Major) passionText = " (Burning passion)";

            string disabledText = skill.TotallyDisabled ? " [DISABLED]" : "";

            return $"{skill.def.skillLabel}: Level {skill.Level}{passionText}{disabledText}";
        }

        private static string GetHealthText(Pawn pawn, int index)
        {
            if (pawn.health?.hediffSet?.hediffs == null || index < 0 || index >= pawn.health.hediffSet.hediffs.Count)
                return "Healthy";

            Hediff hediff = pawn.health.hediffSet.hediffs[index];
            string partText = hediff.Part != null ? $" on {hediff.Part.Label}" : "";

            return $"{hediff.LabelCap.StripTags()}{partText} - {hediff.SeverityLabel}";
        }

        private static string GetRelationText(Pawn pawn, int index)
        {
            if (pawn.relations?.DirectRelations == null || index < 0 || index >= pawn.relations.DirectRelations.Count)
                return "No relations";

            DirectPawnRelation relation = pawn.relations.DirectRelations[index];
            int opinion = pawn.relations.OpinionOf(relation.otherPawn);

            return $"{relation.otherPawn.LabelShort}: {relation.def.label} (Opinion: {opinion:+#;-#;0})";
        }

        private static string GetTraitText(Pawn pawn, int index)
        {
            if (pawn.story?.traits?.allTraits == null || index < 0 || index >= pawn.story.traits.allTraits.Count)
                return "No traits";

            Trait trait = pawn.story.traits.allTraits[index];
            return $"{trait.LabelCap.StripTags()}: {trait.TipString(pawn).StripTags()}";
        }

        private static string GetGearText(Pawn pawn, int index)
        {
            if (pawn.equipment?.AllEquipmentListForReading == null || index < 0 || index >= pawn.equipment.AllEquipmentListForReading.Count)
                return "No equipment";

            ThingWithComps equipment = pawn.equipment.AllEquipmentListForReading[index];
            return $"{equipment.LabelCap.StripTags()} - {equipment.DescriptionDetailed}";
        }

        private static int GetIncapableOfCount(Pawn pawn)
        {
            if (pawn?.story?.DisabledWorkTagsBackstoryAndTraits == null)
                return 0;

            int count = 0;
            foreach (WorkTags workTag in System.Enum.GetValues(typeof(WorkTags)))
            {
                if (workTag == WorkTags.None) continue;
                if ((pawn.story.DisabledWorkTagsBackstoryAndTraits & workTag) != 0)
                {
                    count++;
                }
            }
            return count;
        }

        private static string GetIncapableOfText(Pawn pawn, int index)
        {
            if (pawn?.story?.DisabledWorkTagsBackstoryAndTraits == null)
                return "Not incapable of anything";

            List<WorkTags> incapableTags = new List<WorkTags>();
            foreach (WorkTags workTag in System.Enum.GetValues(typeof(WorkTags)))
            {
                if (workTag == WorkTags.None) continue;
                if ((pawn.story.DisabledWorkTagsBackstoryAndTraits & workTag) != 0)
                {
                    incapableTags.Add(workTag);
                }
            }

            if (incapableTags.Count == 0)
                return "Not incapable of anything";

            if (index < 0 || index >= incapableTags.Count)
                return "Invalid index";

            WorkTags selectedTag = incapableTags[index];

            // Get the reason(s) for this incapability
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Incapable of: {selectedTag.LabelTranslated().CapitalizeFirst()}");
            sb.AppendLine();

            // List affected work types
            sb.AppendLine("Affected work types:");
            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefs)
            {
                if ((workType.workTags & selectedTag) != WorkTags.None)
                {
                    sb.AppendLine($"- {workType.pawnLabel}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("Reasons:");

            // Check backstories
            if (pawn.story.Childhood != null && (pawn.story.Childhood.workDisables & selectedTag) != 0)
            {
                sb.AppendLine($"- Childhood: {pawn.story.Childhood.title}");
            }
            if (pawn.story.Adulthood != null && (pawn.story.Adulthood.workDisables & selectedTag) != 0)
            {
                sb.AppendLine($"- Adulthood: {pawn.story.Adulthood.title}");
            }

            // Check traits
            if (pawn.story.traits?.allTraits != null)
            {
                foreach (Trait trait in pawn.story.traits.allTraits)
                {
                    if (trait.def.disabledWorkTags != WorkTags.None && (trait.def.disabledWorkTags & selectedTag) != 0)
                    {
                        sb.AppendLine($"- Trait: {trait.LabelCap.StripTags()}");
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        public static string GetCurrentModeDescription()
        {
            switch (currentMode)
            {
                case NavigationMode.PawnList:
                    return "Pawn List Mode - Up/Down:navigate | Right:sections | R:randomize | E:edit name | Space:swap | A:add | Del:remove | Enter:begin";
                case NavigationMode.SectionList:
                    return "Section Mode - Up/Down:navigate | Right:drill in | Left:back | I:info card";
                case NavigationMode.DetailView:
                    return "Detail Mode - Up/Down:navigate | Left:back | I:info card";
                case NavigationMode.NameEditMenu:
                    string[] menuItems = { "First Name", "Nickname", "Last Name", "Save Changes" };
                    return $"Name Edit Mode - Selected: {menuItems[currentNameField]} | Up/Down:navigate | Enter:select | Esc:cancel";
                case NavigationMode.NameTextEdit:
                    string[] fieldNames = { "First Name", "Nickname", "Last Name" };
                    return $"Text Edit Mode - Editing: {fieldNames[currentNameField]} | Type to edit | Enter:save | Esc:cancel";
                case NavigationMode.InfoCard:
                    return "Info Card Mode - Press Escape to close and return";
                case NavigationMode.SwapSelect:
                    return "Swap Selection Mode - Up/Down:navigate | Enter:confirm swap | Esc:cancel";
                default:
                    return "Unknown mode";
            }
        }

        // Name editing functionality
        public static void EnterNameEditMode()
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            if (currentPawnIndex < 0 || currentPawnIndex >= pawns.Count) return;

            Pawn pawn = pawns[currentPawnIndex];
            if (pawn.Name is NameTriple triple)
            {
                editingFirstName = triple.First;
                editingNickName = triple.Nick;
                editingLastName = triple.Last;
            }
            else
            {
                editingFirstName = pawn.LabelShort;
                editingNickName = pawn.LabelShort;
                editingLastName = "";
            }

            currentMode = NavigationMode.NameEditMenu;
            currentNameField = 0;
            CopyNameMenuToClipboard();
        }

        public static void NavigateNameFields(bool up)
        {
            if (currentMode != NavigationMode.NameEditMenu) return;

            if (up)
            {
                currentNameField--;
                if (currentNameField < 0) currentNameField = 3; // 0-3 for First, Nick, Last, Save
            }
            else
            {
                currentNameField++;
                if (currentNameField > 3) currentNameField = 0;
            }

            CopyNameMenuToClipboard();
        }

        // Enter text edit mode for a specific field
        public static void EnterTextEditMode()
        {
            if (currentMode != NavigationMode.NameEditMenu) return;

            // If on "Save Changes", just save
            if (currentNameField == 3)
            {
                SaveNameEdit();
                return;
            }

            // Otherwise, enter text edit mode for the selected field
            currentMode = NavigationMode.NameTextEdit;

            // Load the current value into the text input
            switch (currentNameField)
            {
                case 0: currentTextInput = editingFirstName; break;
                case 1: currentTextInput = editingNickName; break;
                case 2: currentTextInput = editingLastName; break;
            }

            CopyTextEditPromptToClipboard();
        }

        // Handle text input
        public static void HandleTextInput(char character)
        {
            if (currentMode != NavigationMode.NameTextEdit) return;

            // Allow alphanumeric, spaces, hyphens, apostrophes
            if (char.IsLetterOrDigit(character) || character == ' ' || character == '-' || character == '\'')
            {
                currentTextInput += character;
                CopyTextEditPromptToClipboard();
            }
        }

        // Handle backspace in text edit mode
        public static void HandleBackspace()
        {
            if (currentMode != NavigationMode.NameTextEdit) return;

            if (currentTextInput.Length > 0)
            {
                currentTextInput = currentTextInput.Substring(0, currentTextInput.Length - 1);
                CopyTextEditPromptToClipboard();
            }
        }

        // Save the current text input
        public static void SaveTextEdit()
        {
            if (currentMode != NavigationMode.NameTextEdit) return;

            // Save the text to the appropriate field
            switch (currentNameField)
            {
                case 0: editingFirstName = currentTextInput; break;
                case 1: editingNickName = currentTextInput; break;
                case 2: editingLastName = currentTextInput; break;
            }

            // Return to name menu
            currentMode = NavigationMode.NameEditMenu;
            currentTextInput = "";
            CopyNameMenuToClipboard();
        }

        // Cancel text edit
        public static void CancelTextEdit()
        {
            if (currentMode != NavigationMode.NameTextEdit)
            {
                // If in name menu, go back to pawn list
                if (currentMode == NavigationMode.NameEditMenu)
                {
                    CancelNameEdit();
                }
                return;
            }

            currentMode = NavigationMode.NameEditMenu;
            currentTextInput = "";
            CopyNameMenuToClipboard();
        }

        public static void SaveNameEdit()
        {
            if (currentMode != NavigationMode.NameEditMenu && currentMode != NavigationMode.NameTextEdit) return;

            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            if (currentPawnIndex < 0 || currentPawnIndex >= pawns.Count) return;

            Pawn pawn = pawns[currentPawnIndex];

            // Validate names (basic check)
            if (string.IsNullOrWhiteSpace(editingFirstName)) editingFirstName = "Unknown";
            if (string.IsNullOrWhiteSpace(editingLastName)) editingLastName = "Unknown";
            if (string.IsNullOrWhiteSpace(editingNickName)) editingNickName = editingFirstName;

            pawn.Name = new NameTriple(editingFirstName, editingNickName, editingLastName);

            currentMode = NavigationMode.PawnList;
            TolkHelper.Speak($"Name saved: {pawn.Name}");
        }

        public static void CancelNameEdit()
        {
            if (currentMode != NavigationMode.NameEditMenu) return;

            currentMode = NavigationMode.PawnList;
            TolkHelper.Speak("Name edit cancelled");
        }

        private static void CopyNameMenuToClipboard()
        {
            string[] menuItems = {
                $"First Name: {editingFirstName}",
                $"Nickname: {editingNickName}",
                $"Last Name: {editingLastName}",
                "Save Changes"
            };

            TolkHelper.Speak($"[Name Editor] {menuItems[currentNameField]} - Press Enter to edit, Escape to cancel");
        }

        private static void CopyTextEditPromptToClipboard()
        {
            string[] fieldNames = { "First Name", "Nickname", "Last Name" };
            TolkHelper.Speak($"[Editing {fieldNames[currentNameField]}] {currentTextInput}_ (Type to edit, Enter to save, Escape to cancel)");
        }

        // Info card functionality
        public static void OpenInfoCard()
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            if (currentPawnIndex < 0 || currentPawnIndex >= pawns.Count) return;

            Pawn pawn = pawns[currentPawnIndex];

            try
            {
                StringBuilder infoText = new StringBuilder();

                if (currentMode == NavigationMode.DetailView)
                {
                    Section section = availableSections[currentSectionIndex];

                    switch (section)
                    {
                        case Section.Biography:
                            // Get full biography info
                            infoText.AppendLine($"{pawn.Name}:");
                            infoText.AppendLine($"Gender: {pawn.gender}");
                            infoText.AppendLine($"Age: {pawn.ageTracker.AgeBiologicalYears} years");
                            infoText.AppendLine($"Title: {pawn.story.TitleCap}");
                            infoText.AppendLine();
                            if (pawn.story.Childhood != null)
                            {
                                infoText.AppendLine($"Childhood: {pawn.story.Childhood.title}");
                                infoText.AppendLine(pawn.story.Childhood.FullDescriptionFor(pawn));
                                infoText.AppendLine();
                            }
                            if (pawn.story.Adulthood != null)
                            {
                                infoText.AppendLine($"Adulthood: {pawn.story.Adulthood.title}");
                                infoText.AppendLine(pawn.story.Adulthood.FullDescriptionFor(pawn));
                            }
                            break;

                        case Section.Traits:
                            if (pawn.story?.traits?.allTraits != null && currentDetailIndex < pawn.story.traits.allTraits.Count)
                            {
                                Trait trait = pawn.story.traits.allTraits[currentDetailIndex];
                                infoText.AppendLine($"{trait.LabelCap.StripTags()}:");
                                infoText.AppendLine(trait.TipString(pawn).StripTags());
                            }
                            break;

                        case Section.Skills:
                            if (pawn.skills?.skills != null && currentDetailIndex < pawn.skills.skills.Count)
                            {
                                SkillRecord skill = pawn.skills.skills[currentDetailIndex];
                                infoText.AppendLine($"{skill.def.skillLabel}:");
                                infoText.AppendLine($"Level: {skill.Level}");
                                infoText.AppendLine($"Passion: {skill.passion}");
                                if (skill.TotallyDisabled)
                                {
                                    infoText.AppendLine("Status: DISABLED");
                                }
                                infoText.AppendLine();
                                infoText.AppendLine(skill.def.description);
                            }
                            break;

                        case Section.Health:
                            if (pawn.health?.hediffSet?.hediffs != null && currentDetailIndex < pawn.health.hediffSet.hediffs.Count)
                            {
                                Hediff hediff = pawn.health.hediffSet.hediffs[currentDetailIndex];
                                infoText.AppendLine($"{hediff.LabelCap.StripTags()}:");
                                if (hediff.Part != null)
                                {
                                    infoText.AppendLine($"Part: {hediff.Part.Label}");
                                }
                                infoText.AppendLine($"Severity: {hediff.SeverityLabel}");
                                infoText.AppendLine();
                                infoText.AppendLine(hediff.Description);
                            }
                            break;

                        case Section.Gear:
                            if (pawn.equipment?.AllEquipmentListForReading != null && currentDetailIndex < pawn.equipment.AllEquipmentListForReading.Count)
                            {
                                ThingWithComps equipment = pawn.equipment.AllEquipmentListForReading[currentDetailIndex];
                                infoText.AppendLine($"{equipment.LabelCap.StripTags()}:");
                                infoText.AppendLine(equipment.DescriptionDetailed);
                            }
                            break;

                        case Section.Relations:
                            if (pawn.relations?.DirectRelations != null && currentDetailIndex < pawn.relations.DirectRelations.Count)
                            {
                                DirectPawnRelation relation = pawn.relations.DirectRelations[currentDetailIndex];
                                int opinion = pawn.relations.OpinionOf(relation.otherPawn);
                                infoText.AppendLine($"{relation.otherPawn.LabelShort}:");
                                infoText.AppendLine($"Relation: {relation.def.label}");
                                infoText.AppendLine($"Opinion: {opinion:+#;-#;0}");
                                infoText.AppendLine();
                                infoText.AppendLine(relation.def.description);
                            }
                            break;

                        case Section.IncapableOf:
                            // Just show the same text as the detail view
                            infoText.AppendLine($"{pawn.Name} - Incapabilities:");
                            infoText.AppendLine(GetIncapableOfText(pawn, currentDetailIndex));
                            break;

                        default:
                            infoText.AppendLine($"{pawn.Name}:");
                            infoText.AppendLine("No detailed info available");
                            break;
                    }
                }
                else
                {
                    // Default: pawn summary
                    infoText.AppendLine($"{pawn.Name}:");
                    infoText.AppendLine($"Title: {pawn.story.TitleCap}");
                    infoText.AppendLine($"Age: {pawn.ageTracker.AgeBiologicalYears}");
                }

                infoText.AppendLine();
                infoText.AppendLine("[Press Escape to close info card and return]");

                // Store current mode and switch to InfoCard mode
                previousMode = currentMode;
                currentMode = NavigationMode.InfoCard;

                TolkHelper.Speak(infoText.ToString());
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error getting info card: {ex}");
                TolkHelper.Speak("Error getting info card", SpeechPriority.High);
            }
        }

        // Selection management
        // Note: This enters swap mode to let user select which pawn to swap with
        // The startingPawnCount never changes - it's set by the scenario
        public static void BeginPawnSwap()
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            if (currentPawnIndex < 0 || currentPawnIndex >= pawns.Count) return;

            int startingCount = Find.GameInitData.startingPawnCount;

            // Can't swap if there are no pawns on the other side
            if (startingCount <= 0 || startingCount >= pawns.Count)
            {
                TolkHelper.Speak("Cannot swap - no pawns on the other side of the boundary", SpeechPriority.High);
                return;
            }

            // Store the source pawn
            swapSourceIndex = currentPawnIndex;

            // Initialize target to the first eligible pawn on the other side
            if (currentPawnIndex < startingCount)
            {
                // Currently in starting roster, can swap with optional pawns
                swapTargetIndex = startingCount;
            }
            else
            {
                // Currently in optional roster, can swap with starting pawns
                swapTargetIndex = 0;
            }

            // Enter swap mode
            currentMode = NavigationMode.SwapSelect;

            // Announce the swap prompt
            CopySwapTargetToClipboard();
        }

        private static void NavigateSwapTargetUp()
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            int startingCount = Find.GameInitData.startingPawnCount;

            // Determine valid range based on source pawn location
            int minIndex, maxIndex;
            if (swapSourceIndex < startingCount)
            {
                // Source is in starting, navigate optional pawns
                minIndex = startingCount;
                maxIndex = pawns.Count - 1;
            }
            else
            {
                // Source is in optional, navigate starting pawns
                minIndex = 0;
                maxIndex = startingCount - 1;
            }

            swapTargetIndex--;
            if (swapTargetIndex < minIndex)
                swapTargetIndex = maxIndex;

            CopySwapTargetToClipboard();
        }

        private static void NavigateSwapTargetDown()
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            int startingCount = Find.GameInitData.startingPawnCount;

            // Determine valid range based on source pawn location
            int minIndex, maxIndex;
            if (swapSourceIndex < startingCount)
            {
                // Source is in starting, navigate optional pawns
                minIndex = startingCount;
                maxIndex = pawns.Count - 1;
            }
            else
            {
                // Source is in optional, navigate starting pawns
                minIndex = 0;
                maxIndex = startingCount - 1;
            }

            swapTargetIndex++;
            if (swapTargetIndex > maxIndex)
                swapTargetIndex = minIndex;

            CopySwapTargetToClipboard();
        }

        private static void CopySwapTargetToClipboard()
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            if (swapSourceIndex < 0 || swapSourceIndex >= pawns.Count) return;
            if (swapTargetIndex < 0 || swapTargetIndex >= pawns.Count) return;

            Pawn sourcePawn = pawns[swapSourceIndex];
            Pawn targetPawn = pawns[swapTargetIndex];

            string sourceName = sourcePawn.Name is NameTriple tripleSource
                ? $"{tripleSource.First} '{tripleSource.Nick}' {tripleSource.Last}"
                : sourcePawn.LabelShort;

            string targetName = targetPawn.Name is NameTriple tripleTarget
                ? $"{tripleTarget.First} '{tripleTarget.Nick}' {tripleTarget.Last}"
                : targetPawn.LabelShort;

            TolkHelper.Speak($"Swap {sourceName} with: {targetName} - {targetPawn.story.TitleCap} (Age {targetPawn.ageTracker.AgeBiologicalYears})");
        }

        public static void ConfirmPawnSwap()
        {
            if (currentMode != NavigationMode.SwapSelect) return;

            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            if (swapSourceIndex < 0 || swapSourceIndex >= pawns.Count) return;
            if (swapTargetIndex < 0 || swapTargetIndex >= pawns.Count) return;

            // Perform the swap
            Pawn sourcePawn = pawns[swapSourceIndex];
            Pawn targetPawn = pawns[swapTargetIndex];

            pawns[swapSourceIndex] = targetPawn;
            pawns[swapTargetIndex] = sourcePawn;

            // Return to pawn list mode, follow the swapped pawn
            currentMode = NavigationMode.PawnList;
            currentPawnIndex = swapTargetIndex;

            TolkHelper.Speak($"Swapped {sourcePawn.Name} with {targetPawn.Name}");
            CopyPawnToClipboard();
        }

        public static void CancelPawnSwap()
        {
            if (currentMode != NavigationMode.SwapSelect) return;

            currentMode = NavigationMode.PawnList;
            TolkHelper.Speak("Swap cancelled");
            CopyPawnToClipboard();
        }

        // Add new pawn
        public static void AddNewPawn()
        {
            try
            {
                StartingPawnUtility.AddNewPawn();
                List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
                currentPawnIndex = pawns.Count - 1; // Navigate to the new pawn
                TolkHelper.Speak($"Added new pawn. Total pawns: {pawns.Count}");
                CopyPawnToClipboard();
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error adding new pawn: {ex}");
                TolkHelper.Speak("Error adding new pawn", SpeechPriority.High);
            }
        }

        // Remove current pawn
        public static void RemoveCurrentPawn()
        {
            List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
            if (currentPawnIndex < 0 || currentPawnIndex >= pawns.Count) return;
            if (pawns.Count <= 1)
            {
                TolkHelper.Speak("Cannot remove last pawn", SpeechPriority.High);
                return;
            }

            try
            {
                Pawn pawn = pawns[currentPawnIndex];
                pawns.RemoveAt(currentPawnIndex);

                // Adjust starting count if needed
                if (currentPawnIndex < Find.GameInitData.startingPawnCount)
                {
                    Find.GameInitData.startingPawnCount--;
                }

                // Adjust current index
                if (currentPawnIndex >= pawns.Count)
                {
                    currentPawnIndex = pawns.Count - 1;
                }

                TolkHelper.Speak($"Removed pawn. Remaining: {pawns.Count}");
                CopyPawnToClipboard();
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error removing pawn: {ex}");
                TolkHelper.Speak("Error removing pawn", SpeechPriority.High);
            }
        }

        // Begin game
        public static bool BeginGame()
        {
            try
            {
                // Validate we have at least one starting pawn
                if (Find.GameInitData.startingPawnCount <= 0)
                {
                    TolkHelper.Speak("Error: No starting pawns selected. Use Space to select pawns.", SpeechPriority.High);
                    return false;
                }

                // Validate all pawns have names
                List<Pawn> pawns = Find.GameInitData.startingAndOptionalPawns;
                for (int i = 0; i < Find.GameInitData.startingPawnCount; i++)
                {
                    if (i >= pawns.Count) continue;
                    Pawn pawn = pawns[i];
                    if (pawn.Name == null || string.IsNullOrEmpty(pawn.Name.ToString()))
                    {
                        TolkHelper.Speak($"Error: Pawn {i + 1} has no name", SpeechPriority.High);
                        return false;
                    }
                }

                TolkHelper.Speak("Beginning game...");
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error beginning game: {ex}");
                TolkHelper.Speak("Error beginning game", SpeechPriority.High);
                return false;
            }
        }
    }
}
