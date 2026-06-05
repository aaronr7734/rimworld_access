using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;
using Verse.Steam;

namespace RimWorldAccess
{
    public static class ScenarioNavigationState
    {
        private static bool initialized = false;
        private static int selectedIndex = 0;
        private static List<Scenario> flatScenarioList = new List<Scenario>();
        public static bool DetailPanelActive { get; private set; } = false;

        // Track if "Scenario Builder" virtual entry is at the end of the list
        private static bool hasScenarioBuilderEntry = true;

        /// <summary>
        /// Gets whether the currently selected item is the Scenario Builder placeholder.
        /// The builder is always the last entry (index == actual scenario count).
        /// </summary>
        public static bool IsScenarioBuilderSelected =>
            hasScenarioBuilderEntry && selectedIndex == flatScenarioList.Count;

        /// <summary>
        /// Gets the total navigation count including the Scenario Builder entry.
        /// </summary>
        private static int TotalNavigationCount =>
            flatScenarioList.Count + (hasScenarioBuilderEntry ? 1 : 0);

        // Detail panel navigation using TreeNavigationHelper
        private static TreeNavigationHelper detailTreeNav = new TreeNavigationHelper("ScenarioDetails");

        // Typeahead search for scenario list
        private static TypeaheadSearchHelper listTypeaheadHelper = new TypeaheadSearchHelper();

        static ScenarioNavigationState()
        {
            detailTreeNav.FormatItemAnnouncement = FormatDetailAnnouncement;
            detailTreeNav.FormatSearchAnnouncement = FormatDetailSearchAnnouncement;
        }

        public static void Initialize(List<Scenario> scenarios)
        {
            if (!initialized || flatScenarioList.Count != scenarios.Count)
            {
                flatScenarioList = new List<Scenario>(scenarios);
                selectedIndex = 0;
                initialized = true;
            }
        }

        public static void Reset()
        {
            initialized = false;
            selectedIndex = 0;
            flatScenarioList.Clear();
            DetailPanelActive = false;
            detailTreeNav.Reset();
            listTypeaheadHelper.ClearSearch();
        }

        public static int SelectedIndex
        {
            get { return selectedIndex; }
        }

        public static Scenario SelectedScenario
        {
            get
            {
                if (flatScenarioList.Count == 0 || selectedIndex < 0 || selectedIndex >= flatScenarioList.Count)
                    return null;
                return flatScenarioList[selectedIndex];
            }
        }

        public static int ScenarioCount
        {
            get { return flatScenarioList.Count; }
        }

        public static void NavigateUp()
        {
            if (TotalNavigationCount == 0) return;

            listTypeaheadHelper.ClearSearch();
            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, TotalNavigationCount);
            AnnounceCurrentSelection();
        }

        public static void NavigateDown()
        {
            if (TotalNavigationCount == 0) return;

            listTypeaheadHelper.ClearSearch();
            selectedIndex = MenuHelper.SelectNext(selectedIndex, TotalNavigationCount);
            AnnounceCurrentSelection();
        }

        public static void NavigateHome()
        {
            if (TotalNavigationCount == 0) return;

            listTypeaheadHelper.ClearSearch();
            selectedIndex = 0;
            AnnounceCurrentSelection();
        }

        public static void NavigateEnd()
        {
            if (TotalNavigationCount == 0) return;

            listTypeaheadHelper.ClearSearch();
            selectedIndex = TotalNavigationCount - 1;
            AnnounceCurrentSelection();
        }

        // Scenario list typeahead support
        public static bool ListHasActiveSearch => listTypeaheadHelper.HasActiveSearch;

        public static bool HandleListTypeahead(char character)
        {
            if (flatScenarioList.Count == 0)
                return false;

            // Create labels list including "Scenario Builder" for search
            var labels = flatScenarioList.Select(s => s.name).ToList();
            if (hasScenarioBuilderEntry)
            {
                labels.Add("RimWorldAccess.ScenarioSelect.BuilderSearchLabel".Translate());
            }

            if (listTypeaheadHelper.ProcessCharacterInput(character, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceWithListSearch();
                }
            }
            else
            {
                listTypeaheadHelper.SpeakNoMatches();
            }

            return true;
        }

        public static bool HandleListTypeaheadBackspace()
        {
            if (!listTypeaheadHelper.HasActiveSearch)
                return false;

            // Create labels list including "Scenario Builder" for search
            var labels = flatScenarioList.Select(s => s.name).ToList();
            if (hasScenarioBuilderEntry)
            {
                labels.Add("RimWorldAccess.ScenarioSelect.BuilderSearchLabel".Translate());
            }

            if (listTypeaheadHelper.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceWithListSearch();
                }
            }

            return true;
        }

        public static bool ClearListTypeaheadSearch()
        {
            if (listTypeaheadHelper.ClearSearchAndAnnounce())
            {
                AnnounceCurrentSelection();
                return true;
            }
            return false;
        }

        public static bool SelectNextListMatch()
        {
            if (!listTypeaheadHelper.HasActiveSearch)
                return false;

            int next = listTypeaheadHelper.GetNextMatch(selectedIndex);
            if (next >= 0)
            {
                selectedIndex = next;
                AnnounceWithListSearch();
            }
            return true;
        }

        public static bool SelectPreviousListMatch()
        {
            if (!listTypeaheadHelper.HasActiveSearch)
                return false;

            int prev = listTypeaheadHelper.GetPreviousMatch(selectedIndex);
            if (prev >= 0)
            {
                selectedIndex = prev;
                AnnounceWithListSearch();
            }
            return true;
        }

        private static void AnnounceWithListSearch()
        {
            // Scenario Builder doesn't participate in search
            if (IsScenarioBuilderSelected)
            {
                AnnounceScenarioBuilder();
                return;
            }

            Scenario selected = SelectedScenario;
            if (selected == null) return;

            string categorySuffix = GetCategorySuffix(selected);

            if (listTypeaheadHelper.HasActiveSearch)
            {
                TolkHelper.SpeakData(listTypeaheadHelper.BuildItemAnnouncement(selected.name + categorySuffix));
            }
            else
            {
                AnnounceCurrentScenario();
            }
        }

        /// <summary>
        /// Announces the current selection (scenario or builder entry).
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            if (IsScenarioBuilderSelected)
            {
                AnnounceScenarioBuilder();
            }
            else
            {
                AnnounceCurrentScenario();
            }
        }

        /// <summary>
        /// Announces the Scenario Builder entry.
        /// </summary>
        private static void AnnounceScenarioBuilder()
        {
            string positionPart = MenuHelper.FormatPosition(selectedIndex, TotalNavigationCount);
            string text = "RimWorldAccess.ScenarioSelect.BuilderEntryLabel".Translate();

            if (!string.IsNullOrEmpty(positionPart))
            {
                text += "RimWorldAccess.ScenarioSelect.WithPositionSuffix".Translate(positionPart);
            }

            TolkHelper.SpeakData(text);
        }

        private static void AnnounceCurrentScenario()
        {
            Scenario selected = SelectedScenario;
            if (selected == null) return;

            string categorySuffix = GetCategorySuffix(selected);
            string positionPart = MenuHelper.FormatPosition(selectedIndex, TotalNavigationCount);

            string text = "RimWorldAccess.ScenarioSelect.NameSummary".Translate(selected.name, selected.summary, categorySuffix);

            // Append warning for invalid scenarios
            if (!selected.valid)
            {
                text += "RimWorldAccess.ScenarioSelect.WarningSuffix".Translate("ScenPart_Error".Translate());
            }

            // Add position if enabled
            if (!string.IsNullOrEmpty(positionPart))
            {
                text += "RimWorldAccess.ScenarioSelect.WithPositionSuffix".Translate(positionPart);
            }

            TolkHelper.SpeakData(text);
        }

        // Legacy method name for compatibility
        private static void CopySelectedToClipboard()
        {
            AnnounceCurrentSelection();
        }

        private static string GetCategorySuffix(Scenario scenario)
        {
            switch (scenario.Category)
            {
                case ScenarioCategory.FromDef:
                    return "RimWorldAccess.ScenarioSelect.CategoryBuiltIn".Translate();
                case ScenarioCategory.CustomLocal:
                    return "RimWorldAccess.ScenarioSelect.CategoryCustom".Translate();
                case ScenarioCategory.SteamWorkshop:
                    return "RimWorldAccess.ScenarioSelect.CategoryWorkshop".Translate();
                default:
                    return "";
            }
        }

        public static Scenario GetScenarioAtIndex(int index)
        {
            if (index < 0 || index >= flatScenarioList.Count)
                return null;
            return flatScenarioList[index];
        }

        public static void ToggleDetailPanel()
        {
            // Don't open detail panel for Scenario Builder entry
            if (IsScenarioBuilderSelected && !DetailPanelActive)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioSelect.NoDetailsForBuilder".Loc());
                return;
            }

            DetailPanelActive = !DetailPanelActive;
            if (DetailPanelActive)
            {
                var root = BuildDetailTree();

                if (root.Children.Count == 0)
                {
                    root.Children.Add(new InspectionTreeItem
                    {
                        Label = "RimWorldAccess.ScenarioSelect.NoAdditionalDetails".Translate(),
                        IndentLevel = 0,
                        IsExpandable = false,
                        Parent = root
                    });
                }

                detailTreeNav.Initialize(root);
                TolkHelper.Speak("RimWorldAccess.ScenarioSelect.DetailsLabel".Loc());
                detailTreeNav.ReannounceCurrentItem();
            }
            else
            {
                detailTreeNav.Reset();
                TolkHelper.Speak("RimWorldAccess.ScenarioSelect.ScenarioListLabel".Loc());
                CopySelectedToClipboard();
            }
        }

        /// <summary>
        /// Builds the detail tree from the scenario's parts.
        /// Processes each ScenPart directly rather than parsing concatenated text.
        /// </summary>
        private static InspectionTreeItem BuildDetailTree()
        {
            var root = new InspectionTreeItem
            {
                Label = "RimWorldAccess.ScenarioSelect.DetailsLabel".Translate(),
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            Scenario selected = SelectedScenario;
            if (selected == null) return root;

            var addedContent = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            // 1. Add description, extracting any "Note:" portion to add separately
            string noteFromDescription = null;
            if (!string.IsNullOrWhiteSpace(selected.description))
            {
                string desc = selected.description.Trim();

                // Check for "Note:" in description (often on its own line)
                int noteIndex = desc.IndexOf("\nNote:", System.StringComparison.OrdinalIgnoreCase);
                if (noteIndex < 0)
                    noteIndex = desc.IndexOf("\n\nNote:", System.StringComparison.OrdinalIgnoreCase);

                if (noteIndex >= 0)
                {
                    string mainDesc = desc.Substring(0, noteIndex).Trim();
                    noteFromDescription = desc.Substring(noteIndex).Trim();

                    root.Children.Add(new InspectionTreeItem
                    {
                        Label = "RimWorldAccess.ScenarioSelect.DescriptionLine".Translate(mainDesc),
                        IndentLevel = 0,
                        IsExpandable = false,
                        Parent = root
                    });
                    addedContent.Add(mainDesc);

                    // Add note as separate item
                    root.Children.Add(new InspectionTreeItem
                    {
                        Label = noteFromDescription,
                        IndentLevel = 0,
                        IsExpandable = false,
                        Parent = root
                    });
                    addedContent.Add(noteFromDescription);
                }
                else
                {
                    root.Children.Add(new InspectionTreeItem
                    {
                        Label = "RimWorldAccess.ScenarioSelect.DescriptionLine".Translate(desc),
                        IndentLevel = 0,
                        IsExpandable = false,
                        Parent = root
                    });
                    addedContent.Add(desc);
                }
            }

            // 2. Collect items by processing each ScenPart directly
            var startWithItems = new List<string>();      // From GetSummaryListEntries("PlayerStartsWith")
            var mapScatteredItems = new List<string>();   // From GetSummaryListEntries("MapScatteredWith")
            var createIncidentItems = new List<string>(); // From GetSummaryListEntries("CreateIncident")
            var disableIncidentItems = new List<string>(); // From GetSummaryListEntries("DisableIncident")
            var permaGameConditionItems = new List<string>(); // From GetSummaryListEntries("PermaGameCondition")
            string pawnCountLine = null;                   // From ConfigureStartingPawns.Summary()
            var researchLines = new List<string>();        // From StartingResearch.Summary()
            var otherSummaries = new List<string>();       // Everything else

            foreach (ScenPart part in selected.AllParts)
            {
                if (!part.visible)
                    continue;

                // Collect list entries directly (not from parsed text)
                foreach (string entry in part.GetSummaryListEntries("PlayerStartsWith"))
                {
                    if (!string.IsNullOrWhiteSpace(entry))
                        startWithItems.Add(entry);
                }

                foreach (string entry in part.GetSummaryListEntries("MapScatteredWith"))
                {
                    if (!string.IsNullOrWhiteSpace(entry))
                        mapScatteredItems.Add(entry);
                }

                foreach (string entry in part.GetSummaryListEntries("CreateIncident"))
                {
                    if (!string.IsNullOrWhiteSpace(entry))
                        createIncidentItems.Add(entry);
                }

                foreach (string entry in part.GetSummaryListEntries("DisableIncident"))
                {
                    if (!string.IsNullOrWhiteSpace(entry))
                        disableIncidentItems.Add(entry);
                }

                foreach (string entry in part.GetSummaryListEntries("PermaGameCondition"))
                {
                    if (!string.IsNullOrWhiteSpace(entry))
                        permaGameConditionItems.Add(entry);
                }

                // Categorize part by type name
                string partTypeName = part.GetType().Name;

                // Skip parts that contribute to lists - we use GetSummaryListEntries instead
                if (partTypeName.Contains("StartingThing") ||
                    partTypeName.Contains("StartingAnimal") ||
                    partTypeName.Contains("StartingMech") ||
                    partTypeName.Contains("ScatterThings") ||
                    partTypeName.Contains("CreateIncident") ||
                    partTypeName.Contains("DisableIncident") ||
                    partTypeName.Contains("GameCondition"))
                {
                    continue;
                }

                // Get the part's summary
                string summary = part.Summary(selected);
                if (string.IsNullOrWhiteSpace(summary))
                    continue;

                // Categorize by part type
                if (partTypeName.Contains("ConfigureStartingPawns"))
                {
                    // "Start with X people" - goes in treeview
                    pawnCountLine = summary.Trim();
                }
                else if (partTypeName.Contains("StartingResearch"))
                {
                    // "Start with research: X" - goes in treeview
                    researchLines.Add(summary.Trim());
                }
                else
                {
                    // Other parts (faction, notes, conditions, etc.)
                    // Split by newlines and add each non-duplicate line
                    foreach (string line in summary.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed))
                            continue;

                        // Skip if already added (handles duplicate notes)
                        if (addedContent.Contains(trimmed))
                            continue;

                        // Skip list items (they start with "   -")
                        if (trimmed.StartsWith("-") || line.StartsWith("   -"))
                            continue;

                        // Skip section headers (we build our own treeviews)
                        if (IsListHeader(trimmed))
                            continue;

                        // Fix grammar: "will be a X" -> "will be: X" (faction summary)
                        trimmed = CleanupFactionGrammar(trimmed);

                        otherSummaries.Add(trimmed);
                        addedContent.Add(trimmed);
                    }
                }
            }

            // 3. Add other summaries (faction, notes, etc.) as root items
            foreach (string line in otherSummaries)
            {
                root.Children.Add(new InspectionTreeItem
                {
                    Label = line,
                    IndentLevel = 0,
                    IsExpandable = false,
                    Parent = root
                });
            }

            // 4. Build "Start with" treeview if we have any content
            bool hasStartWithContent = pawnCountLine != null ||
                                       startWithItems.Count > 0 ||
                                       researchLines.Count > 0;
            if (hasStartWithContent)
            {
                var startWithSection = new InspectionTreeItem
                {
                    Label = "RimWorldAccess.ScenarioSelect.StartWith".Translate(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root
                };

                // Add pawn count first (e.g., "Start with 3 people")
                if (pawnCountLine != null)
                {
                    startWithSection.Children.Add(new InspectionTreeItem
                    {
                        Label = pawnCountLine,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = startWithSection
                    });
                }

                // Add starting items
                foreach (string label in startWithItems)
                {
                    startWithSection.Children.Add(new InspectionTreeItem
                    {
                        Label = label,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = startWithSection
                    });
                }

                // Add starting research
                foreach (string research in researchLines)
                {
                    startWithSection.Children.Add(new InspectionTreeItem
                    {
                        Label = research,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = startWithSection
                    });
                }

                root.Children.Add(startWithSection);
            }

            // 5. Build "Map is scattered with" treeview
            if (mapScatteredItems.Count > 0)
            {
                var mapScatteredSection = new InspectionTreeItem
                {
                    Label = "RimWorldAccess.ScenarioSelect.MapScatteredWith".Translate(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root
                };
                foreach (string label in mapScatteredItems)
                {
                    mapScatteredSection.Children.Add(new InspectionTreeItem
                    {
                        Label = label,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = mapScatteredSection
                    });
                }
                root.Children.Add(mapScatteredSection);
            }

            // 6. Build "Create incident" treeview
            if (createIncidentItems.Count > 0)
            {
                var createIncidentSection = new InspectionTreeItem
                {
                    Label = "RimWorldAccess.ScenarioSelect.CreateIncident".Translate(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root
                };
                foreach (string label in createIncidentItems)
                {
                    createIncidentSection.Children.Add(new InspectionTreeItem
                    {
                        Label = label,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = createIncidentSection
                    });
                }
                root.Children.Add(createIncidentSection);
            }

            // 7. Build "Disable incident" treeview
            if (disableIncidentItems.Count > 0)
            {
                var disableIncidentSection = new InspectionTreeItem
                {
                    Label = "RimWorldAccess.ScenarioSelect.DisableIncident".Translate(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root
                };
                foreach (string label in disableIncidentItems)
                {
                    disableIncidentSection.Children.Add(new InspectionTreeItem
                    {
                        Label = label,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = disableIncidentSection
                    });
                }
                root.Children.Add(disableIncidentSection);
            }

            // 8. Build "Permanent game condition" treeview
            if (permaGameConditionItems.Count > 0)
            {
                var permaGameConditionSection = new InspectionTreeItem
                {
                    Label = "RimWorldAccess.ScenarioSelect.PermanentGameCondition".Translate(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root
                };
                foreach (string label in permaGameConditionItems)
                {
                    permaGameConditionSection.Children.Add(new InspectionTreeItem
                    {
                        Label = label,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = permaGameConditionSection
                    });
                }
                root.Children.Add(permaGameConditionSection);
            }

            return root;
        }

        /// <summary>
        /// Checks if a line is a list header like "Start with:" or "Map is scattered with:".
        /// These headers are skipped because we build our own treeviews.
        /// </summary>
        private static bool IsListHeader(string line)
        {
            if (!line.EndsWith(":"))
                return false;

            string lineLower = line.ToLowerInvariant();
            return lineLower.StartsWith("start with") ||
                   lineLower.StartsWith("map is scattered") ||
                   lineLower.StartsWith("create incident") ||
                   lineLower.StartsWith("disable incident") ||
                   lineLower.StartsWith("permanent game condition");
        }

        /// <summary>
        /// Cleans up faction grammar in summary text.
        /// The game's translation says "Your faction will be a X" but proper nouns
        /// shouldn't use an article, so we change it to "Your faction will be: X".
        /// </summary>
        private static string CleanupFactionGrammar(string text)
        {
            // Match "will be a " followed by a faction name (starts with capital)
            const string pattern = "will be a ";
            int idx = text.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                // Replace "will be a X" with "will be: X"
                return text.Substring(0, idx) + "will be: " + text.Substring(idx + pattern.Length);
            }
            return text;
        }

        #region Detail Panel Navigation Wrappers (called by ScenarioSelectionPatch)

        public static void NavigateDetailUp()
        {
            detailTreeNav.SelectPrevious();
        }

        public static void NavigateDetailDown()
        {
            detailTreeNav.SelectNext();
        }

        /// <summary>
        /// Expands the current item if it's expandable and collapsed,
        /// or moves to the first child if already expanded.
        /// </summary>
        public static void ExpandOrDrillDown()
        {
            detailTreeNav.ExpandOrDrillDown();
        }

        /// <summary>
        /// Collapses the current item if it's expanded,
        /// or moves to the parent if on a child item.
        /// </summary>
        public static void CollapseOrDrillUp()
        {
            detailTreeNav.CollapseOrDrillUp();
        }

        /// <summary>
        /// Handles Home key navigation.
        /// Jumps to first sibling at current level, or absolute first with Ctrl.
        /// </summary>
        public static void HandleHomeKey(bool ctrlPressed)
        {
            detailTreeNav.JumpToFirst(ctrlPressed);
        }

        /// <summary>
        /// Handles End key navigation.
        /// Ctrl+End jumps to absolute last.
        /// </summary>
        public static void HandleEndKey(bool ctrlPressed)
        {
            detailTreeNav.JumpToLast(ctrlPressed);
        }

        // Typeahead search support for detail panel
        public static bool HasActiveSearch => detailTreeNav.HasActiveSearch;
        public static bool HasNoMatches => detailTreeNav.HasNoMatches;

        /// <summary>
        /// Processes a character input for typeahead search in the detail panel.
        /// </summary>
        public static bool HandleTypeahead(char character)
        {
            if (detailTreeNav.Count == 0)
                return false;

            var labels = detailTreeNav.VisibleItems.Select(item => item.Label).ToList();

            if (detailTreeNav.Typeahead.ProcessCharacterInput(character, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    detailTreeNav.SetSelectedIndex(newIndex);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceDetailWithSearch();
                }
            }
            else
            {
                detailTreeNav.Typeahead.SpeakNoMatches();
            }

            return true;
        }

        /// <summary>
        /// Handles backspace for typeahead search.
        /// </summary>
        public static bool HandleTypeaheadBackspace()
        {
            if (!detailTreeNav.HasActiveSearch)
                return false;

            var labels = detailTreeNav.VisibleItems.Select(item => item.Label).ToList();

            if (detailTreeNav.Typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    detailTreeNav.SetSelectedIndex(newIndex);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceDetailWithSearch();
                }
                return true;
            }

            return true;
        }

        /// <summary>
        /// Clears the typeahead search and announces.
        /// </summary>
        public static bool ClearTypeaheadSearch()
        {
            if (detailTreeNav.Typeahead.ClearSearchAndAnnounce())
            {
                detailTreeNav.ReannounceCurrentItem();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Moves to the next match in the current search.
        /// </summary>
        public static bool SelectNextMatch()
        {
            if (!detailTreeNav.HasActiveSearch)
                return false;

            int next = detailTreeNav.Typeahead.GetNextMatch(detailTreeNav.SelectedIndex);
            if (next >= 0)
            {
                detailTreeNav.SetSelectedIndex(next);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceDetailWithSearch();
            }
            return true;
        }

        /// <summary>
        /// Moves to the previous match in the current search.
        /// </summary>
        public static bool SelectPreviousMatch()
        {
            if (!detailTreeNav.HasActiveSearch)
                return false;

            int prev = detailTreeNav.Typeahead.GetPreviousMatch(detailTreeNav.SelectedIndex);
            if (prev >= 0)
            {
                detailTreeNav.SetSelectedIndex(prev);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceDetailWithSearch();
            }
            return true;
        }

        /// <summary>
        /// Announces the current detail item with search match information.
        /// </summary>
        private static void AnnounceDetailWithSearch()
        {
            var item = detailTreeNav.SelectedItem;
            if (item == null) return;

            if (detailTreeNav.HasActiveSearch)
            {
                TolkHelper.SpeakData(detailTreeNav.Typeahead.BuildItemAnnouncement(item.Label));
            }
            else
            {
                detailTreeNav.ReannounceCurrentItem();
            }
        }

        #endregion

        #region Detail Announcement Formatters

        private static string FormatDetailAnnouncement(InspectionTreeItem item)
        {
            var (position, total) = detailTreeNav.GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);

            string announcement = "";

            if (item.IsExpandable)
            {
                // Expandable items: "Label, collapsed/expanded, N items. position."
                string stateSuffix = TreeNavigationHelper.FormatExpansionSuffix(item, includeChildCount: true);
                string positionSection = string.IsNullOrEmpty(positionPart) ? "" : $" ({positionPart})";

                announcement = $"{item.Label}{stateSuffix}.{positionSection}";
            }
            else
            {
                // Regular items: "Label. position."
                string labelText = item.Label;
                bool labelEndsWithPunctuation = !string.IsNullOrEmpty(labelText) &&
                    (labelText.EndsWith(".") || labelText.EndsWith("!") || labelText.EndsWith("?"));

                string positionSection;
                if (string.IsNullOrEmpty(positionPart))
                {
                    positionSection = labelEndsWithPunctuation ? "" : ".";
                }
                else
                {
                    positionSection = labelEndsWithPunctuation ? $" ({positionPart})" : $". ({positionPart})";
                }

                announcement = $"{item.Label}{positionSection}";
            }

            // Add level suffix at the end (only announced when level changes)
            announcement += MenuHelper.GetLevelSuffix("ScenarioDetails", item.IndentLevel);

            return announcement;
        }

        private static string FormatDetailSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            if (typeahead.HasActiveSearch)
            {
                return typeahead.BuildItemAnnouncement(item.Label);
            }
            return FormatDetailAnnouncement(item);
        }

        #endregion

        /// <summary>
        /// Checks if the currently selected scenario can be deleted.
        /// Only CustomLocal scenarios can be deleted directly.
        /// </summary>
        /// <returns>True if the selected scenario is CustomLocal and can be deleted</returns>
        public static bool CanDeleteSelectedScenario()
        {
            Scenario selected = SelectedScenario;
            if (selected == null) return false;

            return selected.Category == ScenarioCategory.CustomLocal;
        }

        /// <summary>
        /// Checks if the currently selected scenario is from Steam Workshop.
        /// </summary>
        /// <returns>True if the selected scenario is from Steam Workshop</returns>
        public static bool IsWorkshopScenario()
        {
            Scenario selected = SelectedScenario;
            if (selected == null) return false;

            return selected.Category == ScenarioCategory.SteamWorkshop;
        }

        /// <summary>
        /// Shows confirmation dialog and deletes the selected custom scenario.
        /// After deletion, refreshes the list and adjusts selection.
        /// </summary>
        /// <param name="page">The Page_SelectScenario instance to update after deletion</param>
        public static void DeleteSelectedScenario(Page_SelectScenario page)
        {
            Scenario selected = SelectedScenario;
            if (selected == null || selected.Category != ScenarioCategory.CustomLocal)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioSelect.CannotDelete".Loc());
                return;
            }

            string fileName = selected.File?.Name ?? selected.name;
            string scenarioName = selected.name;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "ConfirmDelete".Translate(fileName),
                delegate
                {
                    // Delete the file
                    selected.File.Delete();

                    // Mark the scenario list as dirty to refresh
                    ScenarioLister.MarkDirty();

                    // Get updated list of scenarios
                    List<Scenario> allScenarios = new List<Scenario>();
                    allScenarios.AddRange(ScenarioLister.ScenariosInCategory(ScenarioCategory.FromDef).Where(s => s.showInUI));
                    allScenarios.AddRange(ScenarioLister.ScenariosInCategory(ScenarioCategory.CustomLocal).Where(s => s.showInUI));
                    allScenarios.AddRange(ScenarioLister.ScenariosInCategory(ScenarioCategory.SteamWorkshop).Where(s => s.showInUI));

                    // Adjust selection index if needed
                    if (selectedIndex >= allScenarios.Count)
                    {
                        selectedIndex = allScenarios.Count > 0 ? allScenarios.Count - 1 : 0;
                    }

                    // Reinitialize with the new list (force reinitialization)
                    initialized = false;
                    flatScenarioList = new List<Scenario>(allScenarios);
                    initialized = true;

                    // Update the page's current scenario selection
                    Scenario newSelection = SelectedScenario;
                    if (newSelection != null)
                    {
                        AccessTools.Field(typeof(Page_SelectScenario), "curScen").SetValue(page, newSelection);
                    }

                    TolkHelper.Speak("RimWorldAccess.ScenarioSelect.Deleted".Loc(scenarioName));

                    // Announce the new selection
                    if (newSelection != null)
                    {
                        AnnounceCurrentSelection();
                    }
                    else if (IsScenarioBuilderSelected)
                    {
                        AnnounceScenarioBuilder();
                    }
                },
                destructive: true
            ));
        }

        /// <summary>
        /// Shows confirmation dialog and unsubscribes from the selected Steam Workshop scenario.
        /// After unsubscription, refreshes the list and adjusts selection.
        /// </summary>
        /// <param name="page">The Page_SelectScenario instance to update after unsubscription</param>
        public static void UnsubscribeSelectedScenario(Page_SelectScenario page)
        {
            Scenario selected = SelectedScenario;
            if (selected == null || selected.Category != ScenarioCategory.SteamWorkshop)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioSelect.CannotUnsubscribe".Loc());
                return;
            }

            string fileName = selected.File?.Name ?? selected.name;
            string scenarioName = selected.name;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "ConfirmUnsubscribeFrom".Translate(fileName),
                delegate
                {
                    // Disable and unsubscribe using reflection (Workshop is internal)
                    selected.enabled = false;

                    // Call Workshop.Unsubscribe(WorkshopUploadable) via reflection
                    var workshopType = AccessTools.TypeByName("Verse.Steam.Workshop");
                    if (workshopType != null)
                    {
                        var unsubscribeMethod = AccessTools.Method(workshopType, "Unsubscribe", new[] { typeof(WorkshopUploadable) });
                        if (unsubscribeMethod != null)
                        {
                            unsubscribeMethod.Invoke(null, new object[] { selected });
                        }
                    }

                    // Mark the scenario list as dirty to refresh
                    ScenarioLister.MarkDirty();

                    // Get updated list of scenarios
                    List<Scenario> allScenarios = new List<Scenario>();
                    allScenarios.AddRange(ScenarioLister.ScenariosInCategory(ScenarioCategory.FromDef).Where(s => s.showInUI));
                    allScenarios.AddRange(ScenarioLister.ScenariosInCategory(ScenarioCategory.CustomLocal).Where(s => s.showInUI));
                    allScenarios.AddRange(ScenarioLister.ScenariosInCategory(ScenarioCategory.SteamWorkshop).Where(s => s.showInUI));

                    // Adjust selection index if needed
                    if (selectedIndex >= allScenarios.Count)
                    {
                        selectedIndex = allScenarios.Count > 0 ? allScenarios.Count - 1 : 0;
                    }

                    // Reinitialize with the new list (force reinitialization)
                    initialized = false;
                    flatScenarioList = new List<Scenario>(allScenarios);
                    initialized = true;

                    // Update the page's current scenario selection
                    Scenario newSelection = SelectedScenario;
                    if (newSelection != null)
                    {
                        AccessTools.Field(typeof(Page_SelectScenario), "curScen").SetValue(page, newSelection);
                    }

                    TolkHelper.Speak("RimWorldAccess.ScenarioSelect.UnsubscribedFrom".Loc(scenarioName));

                    // Announce the new selection
                    if (newSelection != null)
                    {
                        AnnounceCurrentSelection();
                    }
                    else if (IsScenarioBuilderSelected)
                    {
                        AnnounceScenarioBuilder();
                    }
                },
                destructive: true
            ));
        }
    }
}
