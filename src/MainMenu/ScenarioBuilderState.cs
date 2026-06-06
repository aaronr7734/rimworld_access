using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State class for the Scenario Builder (Page_ScenarioEditor).
    /// Manages navigation between metadata fields and scenario parts tree.
    /// </summary>
    public static class ScenarioBuilderState
    {
        public static bool IsActive { get; private set; }

        // Sections of the builder
        public enum Section { Metadata, Parts }
        private static Section currentSection = Section.Metadata;

        // Metadata field indices
        private static int metadataIndex = 0;
        private const int MetadataFieldCount = 3; // Title, Summary, Description

        // Text editing state — driven by the unified TextInputManager pipeline.
        private static readonly TextInputController metadataController = new TextInputController();
        private static string editingFieldName = "";

        // Parts tree state - proper treeview pattern
        private static List<PartTreeItem> partsHierarchy = new List<PartTreeItem>(); // Root items (parts)
        private static TreeNavigationHelper partsTreeNav = new TreeNavigationHelper("ScenarioBuilderParts");

        /// <summary>
        /// Represents domain-specific data for a tree item (stored in InspectionTreeItem.Data).
        /// </summary>
        public class PartTreeItemData
        {
            public PartTreeItem ParentPart { get; set; } // The part this item belongs to (null for parts)
            public PartField Field { get; set; } // The field (null for parts)
            public PartTreeItem AsPart { get; set; } // If this is a part, the PartTreeItem

            // List item support (3-level hierarchy for list-based parts)
            public bool IsListItem { get; set; } // True if this is an item in a list (level 1)
            public int ListItemIndex { get; set; } // Index within the parent list
            public bool IsAddAction { get; set; } // True if this is an "Add New Item" action
            public object ListItemReference { get; set; } // Reference to the actual list item object
            public ListItemData ListItemData { get; set; } // The ListItemData for list items

            public bool IsPart => AsPart != null;
            public bool IsField => Field != null;
        }

        /// <summary>
        /// Helper to extract PartTreeItemData from an InspectionTreeItem.
        /// </summary>
        private static PartTreeItemData GetItemData(InspectionTreeItem item)
        {
            return item?.Data as PartTreeItemData;
        }

        /// <summary>
        /// Represents a list item within a list-based part (e.g., PawnKindCount in KindDefs).
        /// </summary>
        public class ListItemData
        {
            public string Label { get; set; }
            public List<PartField> Fields { get; set; } = new List<PartField>();
            public int Index { get; set; }
            public object ItemReference { get; set; }
            public bool IsExpanded { get; set; }
        }

        // Reference to the current scenario and editor page
        private static Scenario currentScenario;
        private static Page_ScenarioEditor currentPage;

        // For tracking changes
        private static bool isDirty;

        static ScenarioBuilderState()
        {
            partsTreeNav.FormatItemAnnouncement = FormatTreeItemAnnouncement;
            partsTreeNav.FormatSearchAnnouncement = FormatTreeSearchAnnouncement;
            partsTreeNav.AnnounceChildCounts = true;
        }

        /// <summary>
        /// Gets whether we're in text editing mode.
        /// </summary>
        public static bool IsEditingText => TextInputManager.Active == metadataController;

        /// <summary>
        /// Gets the current scenario being edited.
        /// </summary>
        public static Scenario CurrentScenario => currentScenario;

        /// <summary>
        /// Gets the current section.
        /// </summary>
        public static Section CurrentSection => currentSection;

        /// <summary>
        /// Marks the scenario as having unsaved changes.
        /// </summary>
        public static void SetDirty()
        {
            isDirty = true;
        }

        /// <summary>
        /// Resets the dirty flag (after saving or loading).
        /// </summary>
        public static void ResetDirty()
        {
            isDirty = false;
        }

        /// <summary>
        /// Represents a part in the parts tree with its editable fields.
        /// </summary>
        public class PartTreeItem
        {
            public ScenPart Part { get; set; }
            public string Label { get; set; }
            public string Summary { get; set; }
            public List<PartField> Fields { get; set; } = new List<PartField>();
            public bool IsExpanded { get; set; }
            public int IndentLevel { get; set; }

            // List-based part support
            public bool IsListPart { get; set; }
            public List<ListItemData> ListItems { get; set; } = new List<ListItemData>();
        }

        /// <summary>
        /// Represents an editable field within a part.
        /// </summary>
        public class PartField
        {
            public string Name { get; set; }
            public FieldType Type { get; set; }
            public string CurrentValue { get; set; }
            public object Data { get; set; } // For dropdown: list of options
            public Action<object> SetValue { get; set; } // Callback to set the value
            /// <summary>
            /// Optional callback to get the actual value after SetValue (for validation/clamping).
            /// If set, this is called after SetValue to get the real stored value which may differ
            /// from the requested value due to validation constraints.
            /// </summary>
            public Func<object> GetValue { get; set; }
            /// <summary>
            /// For Quantity fields: if true, the value is displayed as a percentage (value * 100 + "%")
            /// even if the max range is greater than 1. Used for stat factors that can go above 100%.
            /// </summary>
            public bool IsPercentDisplay { get; set; }
            /// <summary>
            /// For Quantity fields with float[] Data: if true, display as integer (no decimal places)
            /// even though the underlying value is a float. Used for days that are whole numbers.
            /// </summary>
            public bool IsIntegerDisplay { get; set; }
            /// <summary>
            /// For Checkbox fields: the actual boolean state. This language-independent value
            /// drives the checked/unchecked announcement and the toggle, so the display string
            /// (CurrentValue) can be localized without breaking state detection.
            /// </summary>
            public bool BoolValue { get; set; }
        }

        public enum FieldType { Dropdown, Quantity, Text, Checkbox }

        /// <summary>
        /// Opens the scenario builder state with the given scenario.
        /// </summary>
        public static void Open(Scenario scenario, Page_ScenarioEditor page)
        {
            currentScenario = scenario;
            currentPage = page;
            isDirty = false;

            currentSection = Section.Metadata;
            metadataIndex = 0;
            if (TextInputManager.Active == metadataController) TextInputManager.Clear();

            BuildPartsTree();
            RebuildPartsInspectionTree();

            IsActive = true;

            // Announce the builder opening
            string title = currentScenario?.name ?? (string)"RimWorldAccess.ScenarioBuilder.NewScenarioFallback".Translate();
            TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.OpenInstructions".Loc(title));
        }

        /// <summary>
        /// Closes the scenario builder state.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            if (TextInputManager.Active == metadataController) TextInputManager.Clear();
            currentScenario = null;
            currentPage = null;
            partsHierarchy.Clear();
            partsTreeNav.Reset();

            // Close any active child states to prevent broken state on re-entry
            if (ScenarioBuilderAddPartState.IsActive)
            {
                ScenarioBuilderAddPartState.Close(selectPart: false);
            }
            if (ScenarioBuilderPartEditState.IsActive)
            {
                ScenarioBuilderPartEditState.Close(applyChanges: false);
            }
            if (WindowlessScenarioLoadState.IsActive)
            {
                WindowlessScenarioLoadState.Close();
            }
            if (WindowlessScenarioSaveState.IsActive)
            {
                WindowlessScenarioSaveState.Close();
            }
            if (WindowlessScenarioDeleteConfirmState.IsActive)
            {
                WindowlessScenarioDeleteConfirmState.Cancel();
            }
        }

        /// <summary>
        /// Builds the parts tree from the current scenario.
        /// Iterates through ALL parts (matching the game's editor behavior).
        /// Preserves expanded state of parts when rebuilding.
        /// </summary>
        public static void BuildPartsTree()
        {
            // Remember which parts were expanded (by ScenPart reference)
            var expandedParts = new HashSet<ScenPart>();
            // Remember which list items were expanded (by part reference + item index)
            var expandedListItems = new Dictionary<ScenPart, HashSet<int>>();

            foreach (var existing in partsHierarchy)
            {
                if (existing.IsExpanded)
                    expandedParts.Add(existing.Part);

                // Track expanded list items
                if (existing.IsListPart && existing.ListItems != null)
                {
                    var expandedIndices = new HashSet<int>();
                    foreach (var listItem in existing.ListItems)
                    {
                        if (listItem.IsExpanded)
                            expandedIndices.Add(listItem.Index);
                    }
                    if (expandedIndices.Count > 0)
                        expandedListItems[existing.Part] = expandedIndices;
                }
            }

            partsHierarchy.Clear();

            if (currentScenario == null)
                return;

            // Match game's ScenarioUI.DrawScenarioEditInterface - iterate ALL parts
            foreach (ScenPart part in currentScenario.AllParts)
            {
                var item = new PartTreeItem
                {
                    Part = part,
                    Label = part.Label,
                    Summary = GetPartSummary(part),
                    IsExpanded = expandedParts.Contains(part), // Preserve expanded state
                    IndentLevel = 0
                };

                // Check if this is a list-based part
                if (IsListBasedPart(part))
                {
                    item.IsListPart = true;
                    item.ListItems = ExtractListItems(part);

                    // Restore expanded state for list items
                    if (expandedListItems.TryGetValue(part, out var expandedIndices))
                    {
                        foreach (var listItem in item.ListItems)
                        {
                            if (expandedIndices.Contains(listItem.Index))
                                listItem.IsExpanded = true;
                        }
                    }

                    // For list parts, still extract regular fields (like pawnChoiceCount)
                    item.Fields = ExtractPartFields(part);
                }
                else
                {
                    // Extract editable fields from the part
                    item.Fields = ExtractPartFields(part);
                }

                partsHierarchy.Add(item);
            }
        }

        /// <summary>
        /// Builds an InspectionTreeItem tree from partsHierarchy and initializes the TreeNavigationHelper.
        /// Preserves selected index when rebuilding.
        /// Single-field parts are shown directly without needing to expand.
        /// List-based parts have 3 levels: Part -> List Items -> Item Fields
        /// </summary>
        private static void RebuildPartsInspectionTree()
        {
            int previousIndex = partsTreeNav.SelectedIndex;

            var root = new InspectionTreeItem
            {
                Label = "Parts",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            foreach (var part in partsHierarchy)
            {
                // Build label with summary for better context (e.g., "Start With - Silver x50")
                string partLabel = string.IsNullOrEmpty(part.Summary)
                    ? part.Label
                    : (string)"RimWorldAccess.ScenarioBuilder.PartLabelWithSummary".Translate(part.Label, part.Summary);

                // LIST-BASED PARTS: Special 3-level hierarchy
                if (part.IsListPart)
                {
                    // Calculate total child count: list items + regular fields + add action
                    int childCount = part.ListItems.Count + part.Fields.Count + 1; // +1 for "Add New Item"
                    bool hasChildren = childCount > 0;

                    var partNode = new InspectionTreeItem
                    {
                        Label = partLabel,
                        IndentLevel = 0,
                        IsExpandable = hasChildren,
                        IsExpanded = part.IsExpanded,
                        Parent = root,
                        Data = new PartTreeItemData { AsPart = part }
                    };

                    // Add regular fields first (like pawnChoiceCount)
                    foreach (var field in part.Fields)
                    {
                        partNode.Children.Add(new InspectionTreeItem
                        {
                            Label = $"{field.Name}: {field.CurrentValue}",
                            IndentLevel = 1,
                            IsExpandable = false,
                            IsExpanded = false,
                            Parent = partNode,
                            Data = new PartTreeItemData { ParentPart = part, Field = field }
                        });
                    }

                    // Add list items (level 1)
                    foreach (var listItem in part.ListItems)
                    {
                        var listItemNode = new InspectionTreeItem
                        {
                            Label = listItem.Label,
                            IndentLevel = 1,
                            IsExpandable = listItem.Fields.Count > 0,
                            IsExpanded = listItem.IsExpanded,
                            Parent = partNode,
                            Data = new PartTreeItemData
                            {
                                ParentPart = part,
                                IsListItem = true,
                                ListItemIndex = listItem.Index,
                                ListItemReference = listItem.ItemReference,
                                ListItemData = listItem
                            }
                        };

                        // Add list item fields (level 2)
                        foreach (var field in listItem.Fields)
                        {
                            listItemNode.Children.Add(new InspectionTreeItem
                            {
                                Label = $"{field.Name}: {field.CurrentValue}",
                                IndentLevel = 2,
                                IsExpandable = false,
                                IsExpanded = false,
                                Parent = listItemNode,
                                Data = new PartTreeItemData
                                {
                                    ParentPart = part,
                                    Field = field,
                                    ListItemIndex = listItem.Index,
                                    ListItemData = listItem
                                }
                            });
                        }

                        partNode.Children.Add(listItemNode);
                    }

                    // Add "Add New Item" action at the end
                    partNode.Children.Add(new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Action,
                        Label = (string)"RimWorldAccess.ScenarioBuilder.AddNewItemLabel".Translate(),
                        IndentLevel = 1,
                        IsExpandable = false,
                        IsExpanded = false,
                        Parent = partNode,
                        Data = new PartTreeItemData { ParentPart = part, IsAddAction = true }
                    });

                    root.Children.Add(partNode);
                }
                // SPECIAL CASE: Parts with exactly 1 field are shown directly as that field
                else if (part.Fields.Count == 1 && !part.IsListPart)
                {
                    var field = part.Fields[0];
                    root.Children.Add(new InspectionTreeItem
                    {
                        Label = $"{partLabel}: {field.CurrentValue}",
                        IndentLevel = 0,
                        IsExpandable = false,
                        IsExpanded = false,
                        Parent = root,
                        Data = new PartTreeItemData { ParentPart = part, Field = field, AsPart = part }
                    });
                }
                // Parts with 0 fields (headers/non-editable) or 2+ fields (needs expand)
                else
                {
                    var partNode = new InspectionTreeItem
                    {
                        Label = partLabel,
                        IndentLevel = 0,
                        IsExpandable = part.Fields.Count > 1, // Only expandable if 2+ fields
                        IsExpanded = part.IsExpanded,
                        Parent = root,
                        Data = new PartTreeItemData { AsPart = part }
                    };

                    // Add fields as children
                    foreach (var field in part.Fields)
                    {
                        partNode.Children.Add(new InspectionTreeItem
                        {
                            Label = $"{field.Name}: {field.CurrentValue}",
                            IndentLevel = 1,
                            IsExpandable = false,
                            IsExpanded = false,
                            Parent = partNode,
                            Data = new PartTreeItemData { ParentPart = part, Field = field }
                        });
                    }

                    root.Children.Add(partNode);
                }
            }

            partsTreeNav.Initialize(root, previousIndex);
        }

        /// <summary>
        /// Gets a summary of the part's current values.
        /// Uses GetSummaryListEntries() for parts that support it (to avoid aggregation issues),
        /// falls back to Summary() for other parts.
        /// </summary>
        private static string GetPartSummary(ScenPart part)
        {
            try
            {
                // Try to get individual summary entries for this specific part.
                // Parts like ScenPart_StartingThing_Defined use GetSummaryListEntries()
                // to provide per-part summaries, while Summary() aggregates all parts.

                // Check common tags used by scenario parts
                string[] tagsToCheck = new string[]
                {
                    "PlayerStartsWith",   // Starting items, animals, mechs, things near start
                    "DisallowBuilding",   // Disallowed buildings
                    "MapScatteredWith",   // Scattered items anywhere
                    "PermaGameCondition", // Permanent game conditions
                    "CreateIncident",     // Scheduled incidents
                    "DisableIncident"     // Disabled incidents
                };

                foreach (var tag in tagsToCheck)
                {
                    var entries = part.GetSummaryListEntries(tag);
                    if (entries != null)
                    {
                        var entriesList = entries.ToList();
                        if (entriesList.Count > 0)
                        {
                            // Join multiple entries (usually just one per part)
                            return string.Join(", ", entriesList);
                        }
                    }
                }

                // Handle ScenPart_GameCondition to include "days" unit
                if (part.GetType().Name == "ScenPart_GameCondition")
                {
                    var durationField = part.GetType().GetField("durationDays", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (durationField != null)
                    {
                        float days = (float)durationField.GetValue(part);
                        return $"{days:F0} days";
                    }
                }

                // No list entries found - fall back to Summary()
                // Reset summarized flag to ensure we get fresh data
                part.summarized = false;
                string summary = part.Summary(currentScenario);

                if (!string.IsNullOrEmpty(summary))
                {
                    // Handle SummaryWithList format (starts with newline + intro)
                    if (summary.StartsWith("\n"))
                    {
                        // Parse the list format: "\nIntro:\n   -Item1\n   -Item2"
                        var lines = summary.Split('\n')
                            .Where(l => l.TrimStart().StartsWith("-"))
                            .Select(l => l.TrimStart(' ', '-'))
                            .ToList();
                        if (lines.Count > 0)
                            return string.Join(", ", lines);
                    }

                    // Clean up regular summaries - take first line only
                    int newlineIndex = summary.IndexOf('\n');
                    if (newlineIndex > 0)
                        summary = summary.Substring(0, newlineIndex);
                    return summary.Trim();
                }
            }
            catch
            {
                // Ignore errors in summary generation
            }
            return "";
        }

        /// <summary>
        /// Extracts editable fields from a ScenPart using reflection.
        /// </summary>
        private static List<PartField> ExtractPartFields(ScenPart part)
        {
            var fields = new List<PartField>();
            Type partType = part.GetType();

            // Check for common field patterns in ScenParts
            // Each ScenPart subclass has its own set of editable fields

            // Handle ScenPart_PlayerFaction (always present)
            var factionDefField = partType.GetField("factionDef", BindingFlags.NonPublic | BindingFlags.Instance);
            if (factionDefField != null && factionDefField.FieldType == typeof(FactionDef))
            {
                var currentFaction = (FactionDef)factionDefField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Faction".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentFaction?.LabelCap ?? "None".Translate(),
                    Data = GetPlayerFactionOptions(),
                    SetValue = (val) => factionDefField.SetValue(part, val)
                });
            }

            // Handle ScenPart_PlanetLayer (surface layer)
            // Note: ScenPart_PlanetLayerFixed has CanEdit = false, check for that property
            var layerField = partType.GetField("layer", BindingFlags.Public | BindingFlags.Instance);
            if (layerField != null && layerField.FieldType == typeof(PlanetLayerDef))
            {
                // Check if this part type has CanEdit property set to false
                var canEditProp = partType.GetProperty("CanEdit", BindingFlags.NonPublic | BindingFlags.Instance);
                bool canEdit = canEditProp == null || (bool)canEditProp.GetValue(part);

                if (canEdit)
                {
                    var currentLayer = (PlanetLayerDef)layerField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.PlanetLayer".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentLayer?.LabelCap ?? "None".Translate(),
                        Data = GetPlanetLayerOptions(),
                        SetValue = (val) => layerField.SetValue(part, val)
                    });
                }
                // If CanEdit is false, don't add field - part will show as read-only
            }

            // Handle ScenPart_PlanetLayer - tag field (unique layer identifier)
            var tagField = partType.GetField("tag", BindingFlags.Public | BindingFlags.Instance);
            if (tagField != null && tagField.FieldType == typeof(string) &&
                partType.Name == "ScenPart_PlanetLayer")
            {
                var currentTag = (string)tagField.GetValue(part) ?? "";
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.LayerTag".Translate(),
                    Type = FieldType.Text,
                    CurrentValue = string.IsNullOrEmpty(currentTag) ? (string)"RimWorldAccess.ScenarioBuilder.Value.Empty".Translate() :
                        (currentTag.Length > 60 ? currentTag.Substring(0, 60) + "..." : currentTag),
                    Data = currentTag,
                    SetValue = (val) => tagField.SetValue(part, val)
                });
            }

            // Handle ScenPart_PlanetLayer - settingsDef field
            var settingsDefField = partType.GetField("settingsDef", BindingFlags.Public | BindingFlags.Instance);
            if (settingsDefField != null && partType.Name == "ScenPart_PlanetLayer")
            {
                var currentSettings = settingsDefField.GetValue(part);
                string currentLabel = (string)"None".Translate();
                if (currentSettings != null)
                {
                    var labelProp = currentSettings.GetType().GetProperty("LabelCap");
                    if (labelProp != null)
                        currentLabel = labelProp.GetValue(currentSettings)?.ToString() ?? "None".Translate();
                }
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.LayerSettings".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentLabel,
                    Data = GetPlanetLayerSettingsOptions(),
                    SetValue = (val) => settingsDefField.SetValue(part, val)
                });
            }

            // Handle ScenPart_PlayerPawnsArriveMethod - arrival method enum
            var arriveMethodField = partType.GetField("method", BindingFlags.NonPublic | BindingFlags.Instance);
            if (arriveMethodField != null && arriveMethodField.FieldType == typeof(PlayerPawnsArriveMethod))
            {
                var currentMethod = (PlayerPawnsArriveMethod)arriveMethodField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.ArrivalMethod".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentMethod.ToStringHuman(),
                    Data = GetArriveMethodOptions(),
                    SetValue = (val) => arriveMethodField.SetValue(part, val)
                });
            }

            // Handle ScenPart_ThingCount and derived classes (Start With items)
            // Check for thingDef field (the item being given)
            var thingDefField = partType.GetField("thingDef", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (thingDefField != null && thingDefField.FieldType == typeof(ThingDef))
            {
                var currentThingDef = (ThingDef)thingDefField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Item".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentThingDef?.LabelCap ?? "None".Translate(),
                    Data = GetStartingThingOptions(part),
                    SetValue = (val) =>
                    {
                        thingDefField.SetValue(part, val);
                        // Also reset stuff to default when item changes
                        var stuffField = partType.GetField("stuff", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                        if (stuffField != null && val is ThingDef td)
                        {
                            stuffField.SetValue(part, GenStuff.DefaultStuffFor(td));
                        }
                    }
                });

                // If the thing is made from stuff, add a stuff selector
                if (currentThingDef != null && currentThingDef.MadeFromStuff)
                {
                    var stuffField = partType.GetField("stuff", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (stuffField != null)
                    {
                        var currentStuff = (ThingDef)stuffField.GetValue(part);
                        fields.Add(new PartField
                        {
                            Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Material".Translate(),
                            Type = FieldType.Dropdown,
                            CurrentValue = currentStuff?.LabelCap ?? "Default".Translate(),
                            Data = GetStuffOptions(currentThingDef),
                            SetValue = (val) => stuffField.SetValue(part, val)
                        });
                    }
                }

                // If the thing can have quality, add a quality selector
                if (currentThingDef != null && currentThingDef.HasComp(typeof(CompQuality)))
                {
                    var qualityField = partType.GetField("quality", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (qualityField != null)
                    {
                        var currentQuality = qualityField.GetValue(part);
                        string qualityLabel = (string)"Default".Translate();
                        if (currentQuality != null && currentQuality is QualityCategory q)
                        {
                            qualityLabel = q.GetLabel().CapitalizeFirst();
                        }
                        fields.Add(new PartField
                        {
                            Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Quality".Translate(),
                            Type = FieldType.Dropdown,
                            CurrentValue = qualityLabel,
                            Data = GetQualityOptions(),
                            SetValue = (val) => qualityField.SetValue(part, val)
                        });
                    }
                }
            }

            // Try to find count/amount fields (common pattern)
            var countField = partType.GetField("count", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (countField != null && countField.FieldType == typeof(int))
            {
                int currentValue = (int)countField.GetValue(part);
                // Game uses Widgets.TextFieldNumeric with min=1, max=1E+09 (1 billion)
                // We use int.MaxValue since it's an int field
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Count".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = currentValue.ToString(),
                    Data = new int[] { 1, int.MaxValue },
                    SetValue = (val) => countField.SetValue(part, val)
                });
            }

            // Try to find chance fields
            var chanceField = partType.GetField("chance", BindingFlags.NonPublic | BindingFlags.Instance);
            if (chanceField != null && chanceField.FieldType == typeof(float))
            {
                float currentValue = (float)chanceField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Chance".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = $"{currentValue * 100:F0}%",
                    Data = new float[] { 0f, 1f },
                    SetValue = (val) => chanceField.SetValue(part, val)
                });
            }

            // Handle ScenPart_ForcedHediff - hediff field
            var hediffField = partType.GetField("hediff", BindingFlags.NonPublic | BindingFlags.Instance);
            if (hediffField != null && hediffField.FieldType == typeof(HediffDef))
            {
                var currentHediff = (HediffDef)hediffField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Condition".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentHediff?.LabelCap ?? "None".Translate(),
                    Data = GetHediffOptions(),
                    SetValue = (val) => hediffField.SetValue(part, val)
                });
            }

            // Handle ScenPart_ForcedHediff - severityRange field
            var severityRangeField = partType.GetField("severityRange", BindingFlags.NonPublic | BindingFlags.Instance);
            if (severityRangeField != null && severityRangeField.FieldType == typeof(FloatRange))
            {
                var currentRange = (FloatRange)severityRangeField.GetValue(part);

                // Min severity field
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MinimumSeverity".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = $"{(currentRange.min * 100):F0}%",
                    Data = new float[] { 0f, 1f },
                    SetValue = (val) =>
                    {
                        var range = (FloatRange)severityRangeField.GetValue(part);
                        float newMin = Convert.ToSingle(val);
                        if (newMin > range.max) newMin = range.max;
                        severityRangeField.SetValue(part, new FloatRange(newMin, range.max));
                    }
                });

                // Max severity field
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MaximumSeverity".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = $"{(currentRange.max * 100):F0}%",
                    Data = new float[] { 0f, 1f },
                    SetValue = (val) =>
                    {
                        var range = (FloatRange)severityRangeField.GetValue(part);
                        float newMax = Convert.ToSingle(val);
                        if (newMax < range.min) newMax = range.min;
                        severityRangeField.SetValue(part, new FloatRange(range.min, newMax));
                    }
                });
            }

            // Handle ScenPart_PawnModifier - context field
            var contextField = partType.GetField("context", BindingFlags.NonPublic | BindingFlags.Instance);
            if (contextField != null && contextField.FieldType == typeof(PawnGenerationContext))
            {
                var currentContext = (PawnGenerationContext)contextField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Affects".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentContext.ToStringHuman(),
                    Data = GetPawnGenerationContextOptions(),
                    SetValue = (val) => contextField.SetValue(part, val)
                });
            }

            // Try to find PawnKindDef field (for animals)
            var animalKindField = partType.GetField("animalKind", BindingFlags.NonPublic | BindingFlags.Instance);
            if (animalKindField != null && animalKindField.FieldType == typeof(PawnKindDef))
            {
                var currentKind = (PawnKindDef)animalKindField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.AnimalType".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentKind?.LabelCap ?? (string)"RimWorldAccess.ScenarioBuilder.Value.RandomPet".Translate(),
                    Data = GetAnimalOptions(),
                    SetValue = (val) => animalKindField.SetValue(part, val)
                });
            }

            // Handle ScenPart_ForcedTrait - trait with degree selection
            if (partType.Name == "ScenPart_ForcedTrait")
            {
                var traitField = partType.GetField("trait", BindingFlags.NonPublic | BindingFlags.Instance);
                var degreeField = partType.GetField("degree", BindingFlags.NonPublic | BindingFlags.Instance);

                if (traitField != null && degreeField != null)
                {
                    var currentTrait = traitField.GetValue(part) as TraitDef;
                    int currentDegree = (int)degreeField.GetValue(part);

                    // Build current display value
                    string currentLabel = (string)"None".Translate();
                    if (currentTrait != null)
                    {
                        var degreeData = currentTrait.DataAtDegree(currentDegree);
                        currentLabel = degreeData?.LabelCap ?? currentTrait.LabelCap;
                    }

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Trait".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentLabel,
                        Data = GetTraitWithDegreeOptions(),
                        SetValue = (val) => {
                            if (val is TraitDegreeData tdd)
                            {
                                // Find the parent TraitDef
                                foreach (var td in DefDatabase<TraitDef>.AllDefs)
                                {
                                    if (td.degreeDatas.Contains(tdd))
                                    {
                                        traitField.SetValue(part, td);
                                        degreeField.SetValue(part, tdd.degree);
                                        break;
                                    }
                                }
                            }
                        }
                    });
                }
            }

            // Handle ScenPart_CreateIncident - incident timing and repeat
            if (partType.Name == "ScenPart_CreateIncident")
            {
                // incident field (inherited from IncidentBase)
                var incidentField = partType.GetField("incident", BindingFlags.NonPublic | BindingFlags.Instance);
                if (incidentField != null)
                {
                    var currentIncident = incidentField.GetValue(part) as IncidentDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Incident".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentIncident?.LabelCap ?? "None".Translate(),
                        Data = GetIncidentDefOptions(),
                        SetValue = (val) => incidentField.SetValue(part, val)
                    });
                }

                // minDays
                var minDaysField = partType.GetField("minDays", BindingFlags.NonPublic | BindingFlags.Instance);
                if (minDaysField != null)
                {
                    float currentMin = (float)minDaysField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MinimumDays".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = currentMin.ToString("F0"),
                        Data = new float[] { 0f, 1000f },
                        IsIntegerDisplay = true,
                        SetValue = (val) => minDaysField.SetValue(part, Convert.ToSingle(val))
                    });
                }

                // maxDays
                var maxDaysField = partType.GetField("maxDays", BindingFlags.NonPublic | BindingFlags.Instance);
                if (maxDaysField != null)
                {
                    float currentMax = (float)maxDaysField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MaximumDays".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = currentMax.ToString("F0"),
                        Data = new float[] { 0f, 1000f },
                        IsIntegerDisplay = true,
                        SetValue = (val) => maxDaysField.SetValue(part, Convert.ToSingle(val))
                    });
                }

                // repeat
                var repeatField = partType.GetField("repeat", BindingFlags.NonPublic | BindingFlags.Instance);
                if (repeatField != null)
                {
                    bool currentRepeat = (bool)repeatField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Repeat".Translate(),
                        Type = FieldType.Checkbox,
                        CurrentValue = BoolDisplay(currentRepeat),
                        BoolValue = currentRepeat,
                        Data = null,
                        SetValue = (val) => repeatField.SetValue(part, val is bool b && b)
                    });
                }
            }
            // Try to find IncidentDef field (generic handler for other incident parts)
            // Exclude ScenPart_DisableIncident since it has its own specific handler below
            else if (partType.Name != "ScenPart_DisableIncident")
            {
                var incidentField = partType.GetField("incident", BindingFlags.NonPublic | BindingFlags.Instance);
                if (incidentField != null && incidentField.FieldType == typeof(IncidentDef))
                {
                    var currentIncident = (IncidentDef)incidentField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Incident".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentIncident?.LabelCap ?? "None".Translate(),
                        Data = GetIncidentOptions(),
                        SetValue = (val) => incidentField.SetValue(part, val)
                    });
                }
            }

            // Try to find ResearchProjectDef field
            var researchField = partType.GetField("project", BindingFlags.NonPublic | BindingFlags.Instance);
            if (researchField != null && researchField.FieldType == typeof(ResearchProjectDef))
            {
                var currentProject = (ResearchProjectDef)researchField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Research".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = currentProject?.LabelCap ?? "None".Translate(),
                    Data = GetResearchOptions(),
                    SetValue = (val) => researchField.SetValue(part, val)
                });
            }

            // Handle ScenPart_ConfigPage_ConfigureStartingPawns - pawn count and choice pool
            // These two fields are linked: pawnChoiceCount must be >= pawnCount
            var pawnCountField = partType.GetField("pawnCount", BindingFlags.Public | BindingFlags.Instance);
            var pawnChoiceCountField = partType.GetField("pawnChoiceCount", BindingFlags.Public | BindingFlags.Instance);

            if (pawnCountField != null && pawnCountField.FieldType == typeof(int))
            {
                int currentValue = (int)pawnCountField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.StartingPawns".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = currentValue.ToString(),
                    Data = new int[] { 1, 10 }, // Game max is 10
                    SetValue = (val) => {
                        int newPawnCount = Convert.ToInt32(val);
                        pawnCountField.SetValue(part, newPawnCount);
                        // Game enforces pawnChoiceCount >= pawnCount, so bump it up if needed
                        if (pawnChoiceCountField != null)
                        {
                            int choiceCount = (int)pawnChoiceCountField.GetValue(part);
                            if (choiceCount < newPawnCount)
                            {
                                pawnChoiceCountField.SetValue(part, newPawnCount);
                            }
                        }
                    }
                });
            }

            // Handle pawnChoiceCount - how many pawns to choose from (must be >= pawnCount)
            if (pawnChoiceCountField != null && pawnChoiceCountField.FieldType == typeof(int))
            {
                int currentValue = (int)pawnChoiceCountField.GetValue(part);
                int pawnCount = 1;
                if (pawnCountField != null)
                    pawnCount = (int)pawnCountField.GetValue(part);

                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.PawnChoicePool".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = currentValue.ToString(),
                    Data = new int[] { pawnCount, 10 }, // Minimum is current pawnCount
                    SetValue = (val) => {
                        int newChoiceCount = Convert.ToInt32(val);
                        // Enforce minimum of pawnCount
                        int currentPawnCount = pawnCountField != null ? (int)pawnCountField.GetValue(part) : 1;
                        if (newChoiceCount < currentPawnCount)
                            newChoiceCount = currentPawnCount;
                        pawnChoiceCountField.SetValue(part, newChoiceCount);
                    }
                });
            }

            // Handle ScenPart_ConfigPage_ConfigureStartingPawns - allowedDevelopmentalStages (Biotech DLC)
            if (ModsConfig.BiotechActive && partType.Name == "ScenPart_ConfigPage_ConfigureStartingPawns")
            {
                var devStagesField = partType.GetField("allowedDevelopmentalStages", BindingFlags.Public | BindingFlags.Instance);
                if (devStagesField != null)
                {
                    var currentStagesObj = devStagesField.GetValue(part);
                    int currentStages = Convert.ToInt32(currentStagesObj);

                    // DevelopmentalStage is a flags enum: Newborn=1, Baby=2, Child=4, Adult=8
                    bool allowBabies = (currentStages & 2) != 0;
                    bool allowChildren = (currentStages & 4) != 0;
                    bool allowAdults = (currentStages & 8) != 0;

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.AllowBabies".Translate(),
                        Type = FieldType.Checkbox,
                        CurrentValue = BoolDisplay(allowBabies),
                        BoolValue = allowBabies,
                        SetValue = (val) => {
                            bool enable = val is bool b && b;
                            int stages = Convert.ToInt32(devStagesField.GetValue(part));
                            if (enable) stages |= 2; else stages &= ~2;
                            devStagesField.SetValue(part, Enum.ToObject(devStagesField.FieldType, stages));
                        }
                    });

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.AllowChildren".Translate(),
                        Type = FieldType.Checkbox,
                        CurrentValue = BoolDisplay(allowChildren),
                        BoolValue = allowChildren,
                        SetValue = (val) => {
                            bool enable = val is bool b && b;
                            int stages = Convert.ToInt32(devStagesField.GetValue(part));
                            if (enable) stages |= 4; else stages &= ~4;
                            devStagesField.SetValue(part, Enum.ToObject(devStagesField.FieldType, stages));
                        }
                    });

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.AllowAdults".Translate(),
                        Type = FieldType.Checkbox,
                        CurrentValue = BoolDisplay(allowAdults),
                        BoolValue = allowAdults,
                        SetValue = (val) => {
                            bool enable = val is bool b && b;
                            int stages = Convert.ToInt32(devStagesField.GetValue(part));
                            if (enable) stages |= 8; else stages &= ~8;
                            devStagesField.SetValue(part, Enum.ToObject(devStagesField.FieldType, stages));
                        }
                    });
                }
            }

            // Handle ScenPart_PawnFilter_Age - age range (min and max as separate fields)
            var ageRangeField = partType.GetField("allowedAgeRange", BindingFlags.Public | BindingFlags.Instance);
            if (ageRangeField != null && ageRangeField.FieldType == typeof(IntRange))
            {
                var currentRange = (IntRange)ageRangeField.GetValue(part);
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MinimumAge".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = currentRange.min.ToString(),
                    Data = new int[] { 15, 120 }, // Game limits from DoEditInterface
                    SetValue = (val) =>
                    {
                        var range = (IntRange)ageRangeField.GetValue(part);
                        range.min = (int)val;
                        // Ensure min doesn't exceed max - 4 (game requires 4 year gap)
                        if (range.min > range.max - 4)
                            range.min = range.max - 4;
                        ageRangeField.SetValue(part, range);
                    },
                    GetValue = () => ((IntRange)ageRangeField.GetValue(part)).min
                });
                fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MaximumAge".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = currentRange.max.ToString(),
                    Data = new int[] { 19, 120 }, // Game min-max is 19
                    SetValue = (val) =>
                    {
                        var range = (IntRange)ageRangeField.GetValue(part);
                        range.max = (int)val;
                        // Ensure max is at least min + 4 (game requires 4 year gap)
                        if (range.max < range.min + 4)
                            range.max = range.min + 4;
                        ageRangeField.SetValue(part, range);
                    },
                    GetValue = () => ((IntRange)ageRangeField.GetValue(part)).max
                });
            }

            // Handle ScenPart_StartingMech (Biotech DLC) - mech type and overseen chance
            if (ModsConfig.BiotechActive && partType.Name == "ScenPart_StartingMech")
            {
                // Mech kind selection
                var mechKindField = partType.GetField("mechKind", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mechKindField != null)
                {
                    var currentKind = mechKindField.GetValue(part);
                    string currentLabel = (string)"Random".Translate();
                    if (currentKind != null)
                    {
                        var labelProp = currentKind.GetType().GetProperty("LabelCap");
                        currentLabel = labelProp?.GetValue(currentKind)?.ToString() ?? "Random".Translate();
                    }
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MechType".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentLabel,
                        Data = GetStartingMechOptions(),
                        SetValue = (val) => mechKindField.SetValue(part, val)
                    });
                }

                // Overseen by mechanitor chance
                var mechChanceField = partType.GetField("overseenByPlayerPawnChance", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mechChanceField != null)
                {
                    float currentChance = (float)mechChanceField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MechanitorOversightChance".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = $"{currentChance * 100:F0}%",
                        Data = new float[] { 0f, 1f },
                        SetValue = (val) => mechChanceField.SetValue(part, Convert.ToSingle(val))
                    });
                }
            }

            // Handle ScenPart_GameStartDialog - dialog text (multi-line text field)
            if (partType.Name == "ScenPart_GameStartDialog")
            {
                var textField = partType.GetField("text", BindingFlags.NonPublic | BindingFlags.Instance);
                if (textField != null && textField.FieldType == typeof(string))
                {
                    var fullText = (string)textField.GetValue(part) ?? "";

                    // Create truncated display value (for tree view)
                    string displayText = fullText;
                    if (fullText.Contains("\n"))
                    {
                        int newlineIndex = fullText.IndexOf('\n');
                        displayText = fullText.Substring(0, Math.Min(newlineIndex, 60)) + "...";
                    }
                    else if (fullText.Length > 60)
                    {
                        displayText = fullText.Substring(0, 60) + "...";
                    }

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.DialogText".Translate(),
                        Type = FieldType.Text,
                        CurrentValue = string.IsNullOrEmpty(displayText) ? (string)"RimWorldAccess.ScenarioBuilder.Value.Empty".Translate() : displayText,
                        Data = fullText, // Store full text for editing
                        SetValue = (val) => textField.SetValue(part, val)
                    });
                }
            }

            // Handle ScenPart_DisableIncident - incident to disable
            if (partType.Name == "ScenPart_DisableIncident")
            {
                var incidentField = partType.GetField("incident", BindingFlags.NonPublic | BindingFlags.Instance);
                if (incidentField != null)
                {
                    var currentIncident = incidentField.GetValue(part) as IncidentDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Incident".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentIncident?.LabelCap ?? "None".Translate(),
                        Data = GetIncidentDefOptions(), // Reuse existing helper
                        SetValue = (val) => incidentField.SetValue(part, val)
                    });
                }
            }

            // Handle ScenPart_StatFactor - stat modifier
            if (partType.Name == "ScenPart_StatFactor")
            {
                // stat field
                var statField = partType.GetField("stat", BindingFlags.NonPublic | BindingFlags.Instance);
                if (statField != null)
                {
                    var currentStat = statField.GetValue(part) as StatDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Stat".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentStat?.LabelCap ?? "None".Translate(),
                        Data = GetStatDefOptions(),
                        SetValue = (val) => statField.SetValue(part, val)
                    });
                }

                // factor field (displayed as percentage, stored as decimal)
                // Game allows 0-100 (0% to 10000%), where 1.0 = 100% (normal)
                var factorField = partType.GetField("factor", BindingFlags.NonPublic | BindingFlags.Instance);
                if (factorField != null)
                {
                    float currentFactor = (float)factorField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Factor".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = $"{currentFactor * 100:F0}%",
                        Data = new float[] { 0f, 100f }, // 0-10000% range in game
                        SetValue = (val) => factorField.SetValue(part, Convert.ToSingle(val)),
                        IsPercentDisplay = true // Display as percentage even though max > 1
                    });
                }
            }

            // Handle ScenPart_PermaGameCondition - permanent game condition
            if (partType.Name == "ScenPart_PermaGameCondition")
            {
                var conditionField = partType.GetField("gameCondition", BindingFlags.NonPublic | BindingFlags.Instance);
                if (conditionField != null)
                {
                    var currentCondition = conditionField.GetValue(part) as GameConditionDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Condition".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentCondition?.LabelCap ?? "None".Translate(),
                        Data = GetPermanentGameConditionOptions(),
                        SetValue = (val) => conditionField.SetValue(part, val)
                    });
                }
            }

            // Handle ScenPart_DisallowBuilding - building to disallow
            if (partType.Name == "ScenPart_DisallowBuilding")
            {
                var buildingField = partType.GetField("building", BindingFlags.NonPublic | BindingFlags.Instance);
                if (buildingField != null)
                {
                    var currentBuilding = buildingField.GetValue(part) as ThingDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Building".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentBuilding?.LabelCap ?? "None".Translate(),
                        Data = GetBuildableThingDefOptions(),
                        SetValue = (val) => buildingField.SetValue(part, val)
                    });
                }
            }

            // Handle ScenPart_SetNeedLevel - set pawn need levels
            if (partType.Name == "ScenPart_SetNeedLevel")
            {
                // need field
                var needField = partType.GetField("need", BindingFlags.NonPublic | BindingFlags.Instance);
                if (needField != null)
                {
                    var currentNeed = needField.GetValue(part) as NeedDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Need".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentNeed?.LabelCap ?? "None".Translate(),
                        Data = GetNeedDefOptions(),
                        SetValue = (val) => needField.SetValue(part, val)
                    });
                }

                // levelRange field (FloatRange - min and max level)
                var levelRangeField = partType.GetField("levelRange", BindingFlags.NonPublic | BindingFlags.Instance);
                if (levelRangeField != null)
                {
                    var currentRange = (FloatRange)levelRangeField.GetValue(part);

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MinimumLevel".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = $"{currentRange.min * 100:F0}%",
                        Data = new float[] { 0f, 1f },
                        SetValue = (val) => {
                            var range = (FloatRange)levelRangeField.GetValue(part);
                            float newMin = Convert.ToSingle(val);
                            if (newMin > range.max) newMin = range.max;
                            levelRangeField.SetValue(part, new FloatRange(newMin, range.max));
                        }
                    });

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MaximumLevel".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = $"{currentRange.max * 100:F0}%",
                        Data = new float[] { 0f, 1f },
                        SetValue = (val) => {
                            var range = (FloatRange)levelRangeField.GetValue(part);
                            float newMax = Convert.ToSingle(val);
                            if (newMax < range.min) newMax = range.min;
                            levelRangeField.SetValue(part, new FloatRange(range.min, newMax));
                        }
                    });
                }
            }

            // Handle ScenPart_GameCondition - temporary game condition with duration
            if (partType.Name == "ScenPart_GameCondition")
            {
                // Note: The condition type comes from def.gameCondition, not a field
                // We only expose durationDays which is editable
                var durationField = partType.GetField("durationDays", BindingFlags.NonPublic | BindingFlags.Instance);
                if (durationField != null)
                {
                    float currentDuration = (float)durationField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.DurationDays".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = currentDuration.ToString("F0"),
                        Data = new float[] { 0f, 1000f },
                        IsIntegerDisplay = true,
                        SetValue = (val) => durationField.SetValue(part, Convert.ToSingle(val))
                    });
                }
            }

            // Handle ScenPart_OnPawnDeathExplode - explosion on pawn death
            if (partType.Name == "ScenPart_OnPawnDeathExplode")
            {
                // radius field
                var radiusField = partType.GetField("radius", BindingFlags.NonPublic | BindingFlags.Instance);
                if (radiusField != null)
                {
                    float currentRadius = (float)radiusField.GetValue(part);
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.ExplosionRadius".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = currentRadius.ToString("F1"),
                        Data = new float[] { 0.1f, 50f },
                        SetValue = (val) => radiusField.SetValue(part, Convert.ToSingle(val))
                    });
                }

                // damage field (limited to Bomb and Flame in vanilla)
                var damageField = partType.GetField("damage", BindingFlags.NonPublic | BindingFlags.Instance);
                if (damageField != null)
                {
                    var currentDamage = damageField.GetValue(part) as DamageDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.DamageType".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentDamage?.LabelCap ?? "None".Translate(),
                        Data = GetExplosionDamageDefOptions(),
                        SetValue = (val) => damageField.SetValue(part, val)
                    });
                }
            }

            // Handle ScenPart_DisableQuest - quest to disable
            if (partType.Name == "ScenPart_DisableQuest")
            {
                var questDefField = partType.GetField("questDef", BindingFlags.Public | BindingFlags.Instance);
                if (questDefField != null)
                {
                    var currentQuest = questDefField.GetValue(part) as QuestScriptDef;
                    // LabelCap returns null when label is empty, use readable defName
                    string questLabel;
                    if (currentQuest == null)
                    {
                        questLabel = (string)"None".Translate();
                    }
                    else if (currentQuest.label.NullOrEmpty())
                    {
                        questLabel = GenText.SplitCamelCase(currentQuest.defName).Replace("_", " ");
                    }
                    else
                    {
                        questLabel = (string)currentQuest.LabelCap;
                    }
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Quest".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = questLabel,
                        Data = GetQuestScriptDefOptions(),
                        SetValue = (val) => questDefField.SetValue(part, val)
                    });
                }
            }

            // Handle ScenPart_CreateQuest - quest to create
            if (partType.Name == "ScenPart_CreateQuest")
            {
                var questDefField = partType.GetField("questDef", BindingFlags.NonPublic | BindingFlags.Instance);
                if (questDefField != null)
                {
                    var currentQuest = questDefField.GetValue(part) as QuestScriptDef;
                    // LabelCap returns null when label is empty, use readable defName
                    string questLabel;
                    if (currentQuest == null)
                    {
                        questLabel = (string)"None".Translate();
                    }
                    else if (currentQuest.label.NullOrEmpty())
                    {
                        questLabel = GenText.SplitCamelCase(currentQuest.defName).Replace("_", " ");
                    }
                    else
                    {
                        questLabel = (string)currentQuest.LabelCap;
                    }
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Quest".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = questLabel,
                        Data = GetQuestScriptDefOptions(),
                        SetValue = (val) => questDefField.SetValue(part, val)
                    });
                }
            }

            // Handle ScenPart_ForcedMap - forced map generator and layer
            if (partType.Name == "ScenPart_ForcedMap")
            {
                // mapGenerator field
                var mapGenField = partType.GetField("mapGenerator", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mapGenField != null)
                {
                    var currentMapGen = mapGenField.GetValue(part) as MapGeneratorDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MapGenerator".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentMapGen?.LabelCap ?? "None".Translate(),
                        Data = GetMapGeneratorDefOptions(),
                        SetValue = (val) => mapGenField.SetValue(part, val)
                    });
                }

                // layerDef field
                var layerDefField = partType.GetField("layerDef", BindingFlags.NonPublic | BindingFlags.Instance);
                if (layerDefField != null)
                {
                    var currentLayer = layerDefField.GetValue(part) as PlanetLayerDef;
                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.PlanetLayer".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentLayer?.LabelCap ?? "None".Translate(),
                        Data = GetPlanetLayerOptions(),
                        SetValue = (val) => layerDefField.SetValue(part, val)
                    });
                }
            }

            // Handle ScenPart_MonolithGeneration - monolith generation method (Anomaly DLC)
            if (ModsConfig.AnomalyActive && partType.Name == "ScenPart_MonolithGeneration")
            {
                var methodField = partType.GetField("method", BindingFlags.NonPublic | BindingFlags.Instance);
                if (methodField != null)
                {
                    var currentMethod = methodField.GetValue(part);
                    string currentLabel = currentMethod?.ToString() ?? "None".Translate();

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.GenerationMethod".Translate(),
                        Type = FieldType.Dropdown,
                        CurrentValue = currentLabel,
                        Data = GetMonolithGenerationMethodOptions(),
                        SetValue = (val) => methodField.SetValue(part, val)
                    });
                }
            }

            // Handle ScenPart_AutoActivateMonolith - auto activation delay (Anomaly DLC)
            if (ModsConfig.AnomalyActive && partType.Name == "ScenPart_AutoActivateMonolith")
            {
                var delayTicksField = partType.GetField("delayTicks", BindingFlags.NonPublic | BindingFlags.Instance);
                if (delayTicksField != null)
                {
                    int currentTicks = (int)delayTicksField.GetValue(part);
                    float currentDays = currentTicks / 60000f; // Convert ticks to days

                    fields.Add(new PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.ActivationDelayDays".Translate(),
                        Type = FieldType.Quantity,
                        CurrentValue = currentDays.ToString("F0"),
                        Data = new float[] { 0f, 1000f },
                        IsIntegerDisplay = true,
                        SetValue = (val) => {
                            float days = Convert.ToSingle(val);
                            int ticks = (int)(days * 60000f);
                            delayTicksField.SetValue(part, ticks);
                        }
                    });
                }
            }

            // If no fields found, it's a part that can't be edited directly
            return fields;
        }

        #region Option Lists for Dropdowns

        private static List<(string label, object value)> GetPlayerFactionOptions()
        {
            var options = new List<(string, object)>();
            foreach (var faction in DefDatabase<FactionDef>.AllDefs.Where(f => f.isPlayer).OrderBy(f => f.label))
            {
                options.Add((faction.LabelCap, faction));
            }
            return options;
        }

        private static List<(string label, object value)> GetPlanetLayerOptions()
        {
            var options = new List<(string, object)>();
            foreach (var layer in DefDatabase<PlanetLayerDef>.AllDefs.OrderBy(l => l.label))
            {
                options.Add((layer.LabelCap, layer));
            }
            return options;
        }

        private static List<(string label, object value)> GetPlanetLayerSettingsOptions()
        {
            var options = new List<(string, object)>();
            var settingsDefType = AccessTools.TypeByName("RimWorld.PlanetLayerSettingsDef");
            if (settingsDefType != null)
            {
                var defDatabaseType = typeof(DefDatabase<>).MakeGenericType(settingsDefType);
                var allDefsProperty = defDatabaseType.GetProperty("AllDefs", BindingFlags.Public | BindingFlags.Static);
                if (allDefsProperty != null)
                {
                    var allDefs = allDefsProperty.GetValue(null) as System.Collections.IEnumerable;
                    if (allDefs != null)
                    {
                        foreach (var def in allDefs)
                        {
                            var labelProp = def.GetType().GetProperty("LabelCap");
                            string label = labelProp?.GetValue(def)?.ToString() ?? def.ToString();
                            options.Add((label, def));
                        }
                    }
                }
            }
            return options.OrderBy(o => o.Item1).ToList();
        }

        private static List<(string label, object value)> GetAnimalOptions()
        {
            var options = new List<(string, object)>();
            options.Add(((string)"RimWorldAccess.ScenarioBuilder.Value.RandomPet".Translate(), null));
            foreach (var kind in DefDatabase<PawnKindDef>.AllDefs.Where(k => k.RaceProps.Animal).OrderBy(k => k.label))
            {
                options.Add((kind.LabelCap, kind));
            }
            return options;
        }

        private static List<(string label, object value)> GetTraitOptions()
        {
            var options = new List<(string, object)>();
            foreach (var trait in DefDatabase<TraitDef>.AllDefs.OrderBy(t => t.label))
            {
                options.Add((trait.label.CapitalizeFirst(), trait));
            }
            return options;
        }

        private static List<(string label, object value)> GetTraitWithDegreeOptions()
        {
            var options = new List<(string, object)>();
            foreach (var trait in DefDatabase<TraitDef>.AllDefs.OrderBy(t => t.label))
            {
                foreach (var degreeData in trait.degreeDatas)
                {
                    string label = degreeData.LabelCap;
                    if (string.IsNullOrEmpty(label))
                        label = trait.LabelCap;
                    options.Add((label, degreeData));
                }
            }
            return options.OrderBy(o => o.Item1).ToList();
        }

        private static List<(string label, object value)> GetIncidentOptions()
        {
            var options = new List<(string, object)>();
            foreach (var incident in DefDatabase<IncidentDef>.AllDefs.Where(i => i.targetTags != null).OrderBy(i => i.label))
            {
                options.Add((incident.LabelCap, incident));
            }
            return options;
        }

        private static List<(string label, object value)> GetIncidentDefOptions()
        {
            var options = new List<(string, object)>();
            // Match game's DoIncidentEditInterface - all incidents are available
            foreach (var incident in DefDatabase<IncidentDef>.AllDefs.OrderBy(i => i.label))
            {
                options.Add((incident.LabelCap, incident));
            }
            return options;
        }

        private static List<(string label, object value)> GetResearchOptions()
        {
            var options = new List<(string, object)>();
            foreach (var project in DefDatabase<ResearchProjectDef>.AllDefs.OrderBy(r => r.label))
            {
                options.Add((project.LabelCap, project));
            }
            return options;
        }

        private static List<(string label, object value)> GetArriveMethodOptions()
        {
            var options = new List<(string, object)>();
            foreach (PlayerPawnsArriveMethod method in Enum.GetValues(typeof(PlayerPawnsArriveMethod)))
            {
                // Skip Gravship if Odyssey DLC isn't active
                if (method == PlayerPawnsArriveMethod.Gravship && !ModsConfig.OdysseyActive)
                    continue;
                options.Add((method.ToStringHuman(), method));
            }
            return options;
        }

        private static List<(string label, object value)> GetStartingThingOptions(ScenPart part)
        {
            var options = new List<(string, object)>();

            // Use reflection to call PossibleThingDefs() if it exists (ScenPart_ThingCount has it)
            var possibleThingsMethod = part.GetType().GetMethod("PossibleThingDefs",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            IEnumerable<ThingDef> possibleThings;
            if (possibleThingsMethod != null)
            {
                possibleThings = (IEnumerable<ThingDef>)possibleThingsMethod.Invoke(part, null);
            }
            else
            {
                // Fallback: get all items and minifiable buildings (same as default PossibleThingDefs)
                possibleThings = DefDatabase<ThingDef>.AllDefs
                    .Where(d => (d.category == ThingCategory.Item && d.scatterableOnMapGen && !d.destroyOnDrop)
                             || (d.category == ThingCategory.Building && d.Minifiable));
            }

            foreach (var thing in possibleThings.OrderBy(t => t.label))
            {
                options.Add((thing.LabelCap, thing));
            }
            return options;
        }

        private static List<(string label, object value)> GetStuffOptions(ThingDef thingDef)
        {
            var options = new List<(string, object)>();
            if (thingDef == null || !thingDef.MadeFromStuff)
                return options;

            foreach (var stuff in GenStuff.AllowedStuffsFor(thingDef).OrderBy(s => s.label))
            {
                options.Add((stuff.LabelCap, stuff));
            }
            return options;
        }

        private static List<(string label, object value)> GetQualityOptions()
        {
            var options = new List<(string, object)>();
            // Add "Default" option (null value for QualityCategory?)
            options.Add(((string)"Default".Translate(), null));

            foreach (QualityCategory quality in QualityUtility.AllQualityCategories)
            {
                options.Add((quality.GetLabel().CapitalizeFirst(), quality));
            }
            return options;
        }

        private static List<(string label, object value)> GetHediffOptions()
        {
            var options = new List<(string, object)>();
            // Match the game's filter: only hediffs with scenarioCanAdd = true
            foreach (var hediff in DefDatabase<HediffDef>.AllDefs
                .Where(h => h.scenarioCanAdd)
                .OrderBy(h => h.label))
            {
                options.Add((hediff.LabelCap, hediff));
            }
            return options;
        }

        private static List<(string label, object value)> GetPawnGenerationContextOptions()
        {
            var options = new List<(string, object)>();
            foreach (PawnGenerationContext context in Enum.GetValues(typeof(PawnGenerationContext)))
            {
                options.Add((context.ToStringHuman(), context));
            }
            return options;
        }

        /// <summary>
        /// Localized "Yes"/"No" display string for a boolean checkbox value (reuses vanilla keys).
        /// </summary>
        internal static string BoolDisplay(bool value) =>
            value ? (string)"Yes".Translate() : (string)"No".Translate();

        /// <summary>
        /// Localized one-word hint describing how a field is edited (dropdown/quantity/checkbox/text).
        /// </summary>
        private static string TypeHint(FieldType type)
        {
            switch (type)
            {
                case FieldType.Dropdown: return (string)"RimWorldAccess.ScenarioBuilder.Announce.TypeHintDropdown".Translate();
                case FieldType.Quantity: return (string)"RimWorldAccess.ScenarioBuilder.Announce.TypeHintQuantity".Translate();
                case FieldType.Checkbox: return (string)"RimWorldAccess.ScenarioBuilder.Announce.TypeHintCheckbox".Translate();
                default: return (string)"RimWorldAccess.ScenarioBuilder.Announce.TypeHintText".Translate();
            }
        }

        /// <summary>Localized "1 field" / "N fields".</summary>
        private static string FieldCount(int n) => n == 1
            ? (string)"RimWorldAccess.ScenarioBuilder.Announce.FieldCountOne".Translate()
            : (string)"RimWorldAccess.ScenarioBuilder.Announce.FieldCountMany".Translate(n);

        /// <summary>Localized "1 item" / "N items".</summary>
        private static string ItemCount(int n) => n == 1
            ? (string)"RimWorldAccess.ScenarioBuilder.Announce.ItemCountOne".Translate()
            : (string)"RimWorldAccess.ScenarioBuilder.Announce.ItemCountMany".Translate(n);

        /// <summary>Localized "checked" / "unchecked" word for a boolean checkbox state.</summary>
        private static string CheckState(bool value) => value
            ? (string)"RimWorldAccess.ScenarioBuilder.PartEdit.Checked".Translate()
            : (string)"RimWorldAccess.ScenarioBuilder.PartEdit.Unchecked".Translate();

        /// <summary>Localized list-item label "{count}x {label}" with an optional "(Required)" suffix.</summary>
        private static string ListItemCountLabel(int count, string label, bool required)
        {
            string baseLabel = (string)"RimWorldAccess.ScenarioBuilder.ListItemCount".Translate(count, label);
            return required
                ? baseLabel + (string)"RimWorldAccess.ScenarioBuilder.RequiredSuffix".Translate()
                : baseLabel;
        }

        /// <summary>
        /// Localized display word for a planet-layer-connection zoom mode. The argument is the
        /// language-independent C# enum name. Prefers the game's own key, then our keys.
        /// </summary>
        private static string ZoomModeDisplay(string enumName)
        {
            string vanillaKey = "ScenPart_PlanetLayerConnections_" + enumName;
            if (vanillaKey.CanTranslate())
                return (string)vanillaKey.Translate();
            switch (enumName)
            {
                case "ZoomIn": return (string)"RimWorldAccess.ScenarioBuilder.ZoomIn".Translate();
                case "ZoomOut": return (string)"RimWorldAccess.ScenarioBuilder.ZoomOut".Translate();
                default: return (string)"None".Translate();
            }
        }

        private static List<(string label, object value)> GetStartingMechOptions()
        {
            var options = new List<(string, object)>();
            options.Add(((string)"Random".Translate(), null));

            foreach (var kind in DefDatabase<PawnKindDef>.AllDefs
                .Where(k => k.RaceProps != null && k.RaceProps.IsMechanoid)
                .OrderBy(k => k.label))
            {
                options.Add((kind.LabelCap, kind));
            }
            return options;
        }

        private static List<(string label, object value)> GetStatDefOptions()
        {
            var options = new List<(string, object)>();
            foreach (var stat in DefDatabase<StatDef>.AllDefs
                .Where(s => !s.forInformationOnly && s.CanShowWithLoadedMods())
                .OrderBy(s => s.label))
            {
                options.Add((stat.LabelCap, stat));
            }
            return options;
        }

        private static List<(string label, object value)> GetPermanentGameConditionOptions()
        {
            var options = new List<(string, object)>();
            foreach (var condition in DefDatabase<GameConditionDef>.AllDefs
                .Where(c => c.canBePermanent)
                .OrderBy(c => c.label))
            {
                options.Add((condition.LabelCap, condition));
            }
            return options;
        }

        private static List<(string label, object value)> GetBuildableThingDefOptions()
        {
            var options = new List<(string, object)>();
            foreach (var thing in DefDatabase<ThingDef>.AllDefs
                .Where(t => t.category == ThingCategory.Building && t.BuildableByPlayer)
                .OrderBy(t => t.label))
            {
                options.Add((thing.LabelCap, thing));
            }
            return options;
        }

        private static List<(string label, object value)> GetNeedDefOptions()
        {
            var options = new List<(string, object)>();
            foreach (var need in DefDatabase<NeedDef>.AllDefs
                .Where(n => n.major)
                .OrderBy(n => n.label))
            {
                options.Add((need.LabelCap, need));
            }
            return options;
        }

        private static List<(string label, object value)> GetExplosionDamageDefOptions()
        {
            var options = new List<(string, object)>();
            // Game limits to Bomb and Flame damage types for this part
            var bomb = DefDatabase<DamageDef>.GetNamedSilentFail("Bomb");
            var flame = DefDatabase<DamageDef>.GetNamedSilentFail("Flame");

            if (bomb != null) options.Add((bomb.LabelCap, bomb));
            if (flame != null) options.Add((flame.LabelCap, flame));

            return options;
        }

        private static List<(string label, object value)> GetQuestScriptDefOptions()
        {
            var options = new List<(string, object)>();
            foreach (var quest in DefDatabase<QuestScriptDef>.AllDefs
                .Where(q => q.IsRootAny)
                .OrderBy(q => q.label ?? q.defName))
            {
                // LabelCap returns null when label is empty, use readable defName
                string displayLabel;
                if (quest.label.NullOrEmpty())
                {
                    displayLabel = GenText.SplitCamelCase(quest.defName).Replace("_", " ");
                }
                else
                {
                    displayLabel = (string)quest.LabelCap;
                }
                options.Add((displayLabel, quest));
            }
            return options;
        }

        private static List<(string label, object value)> GetMapGeneratorDefOptions()
        {
            var options = new List<(string, object)>();
            foreach (var mapGen in DefDatabase<MapGeneratorDef>.AllDefs
                .Where(m => m.validScenarioMap)
                .OrderBy(m => m.label))
            {
                options.Add((mapGen.LabelCap, mapGen));
            }
            return options;
        }

        private static List<(string label, object value)> GetMonolithGenerationMethodOptions()
        {
            var options = new List<(string, object)>();

            // MonolithGenerationMethod is an enum in RimWorld namespace
            var enumType = AccessTools.TypeByName("RimWorld.MonolithGenerationMethod");
            if (enumType != null)
            {
                foreach (var value in Enum.GetValues(enumType))
                {
                    string label = value.ToString();
                    // Try to get translated label if available
                    string translationKey = $"MonolithGenerationMethod_{label}";
                    if (translationKey.CanTranslate())
                        label = translationKey.Translate();
                    options.Add((label, value));
                }
            }
            return options;
        }

        /// <summary>
        /// Gets available PawnKindDef options for starting pawn kind selection.
        /// Only includes humanlike kinds from player factions.
        /// </summary>
        private static List<(string label, object value)> GetPawnKindDefOptions()
        {
            var options = new List<(string, object)>();
            foreach (var kind in DefDatabase<PawnKindDef>.AllDefs
                .Where(k => k.RaceProps.Humanlike && k.defaultFactionDef != null && k.defaultFactionDef.isPlayer)
                .OrderBy(k => k.label))
            {
                options.Add((kind.LabelCap, kind));
            }
            return options;
        }

        /// <summary>
        /// Gets available XenotypeDef options for xenotype selection (Biotech DLC).
        /// </summary>
        private static List<(string label, object value)> GetXenotypeDefOptions()
        {
            var options = new List<(string, object)>();
            if (!ModsConfig.BiotechActive)
                return options;

            foreach (var xenotype in DefDatabase<XenotypeDef>.AllDefs.OrderBy(x => x.label))
            {
                options.Add((xenotype.LabelCap, xenotype));
            }
            return options;
        }

        /// <summary>
        /// Gets available MutantDef options for mutant selection (Anomaly DLC).
        /// Includes "None" option and only mutants with showInScenarioEditor = true.
        /// </summary>
        private static List<(string label, object value)> GetMutantDefOptions()
        {
            var options = new List<(string, object)>();
            if (!ModsConfig.AnomalyActive)
                return options;

            options.Add(("None".Translate().CapitalizeFirst(), null));

            foreach (var mutant in DefDatabase<MutantDef>.AllDefs
                .Where(m => m.showInScenarioEditor)
                .OrderBy(m => m.label))
            {
                options.Add((mutant.LabelCap, mutant));
            }
            return options;
        }

        /// <summary>
        /// Gets available tag options for planet layer connections.
        /// Returns tags from other PlanetLayer parts that aren't already connected.
        /// </summary>
        private static List<(string label, object value)> GetAvailableTagOptions(ScenPart part, List<object> existingConnections)
        {
            var options = new List<(string, object)>();
            var usedTags = new HashSet<string>();

            // Get tags already used in connections
            if (existingConnections != null)
            {
                foreach (var conn in existingConnections)
                {
                    var tagField = conn.GetType().GetField("tag", BindingFlags.Public | BindingFlags.Instance);
                    if (tagField != null)
                    {
                        var tag = tagField.GetValue(conn) as string;
                        if (!string.IsNullOrEmpty(tag))
                            usedTags.Add(tag);
                    }
                }
            }

            // Add "Remove" option first
            options.Add(("Remove".Translate().CapitalizeFirst(), "__REMOVE__"));

            // Find all other PlanetLayer parts and their tags
            if (currentScenario != null)
            {
                foreach (var otherPart in currentScenario.AllParts)
                {
                    if (otherPart == part) continue;
                    if (otherPart.GetType().Name != "ScenPart_PlanetLayer") continue;

                    var tagField = otherPart.GetType().GetField("tag", BindingFlags.Public | BindingFlags.Instance);
                    if (tagField != null)
                    {
                        var tag = tagField.GetValue(otherPart) as string;
                        if (!string.IsNullOrEmpty(tag) && !usedTags.Contains(tag))
                        {
                            options.Add((tag, tag));
                        }
                    }
                }
            }

            return options;
        }

        /// <summary>
        /// Gets zoom mode options for planet layer connections.
        /// </summary>
        private static List<(string label, object value)> GetZoomModeOptions()
        {
            var options = new List<(string, object)>();

            var zoomModeType = AccessTools.TypeByName("RimWorld.LayerConnection+ZoomMode");
            if (zoomModeType != null)
            {
                foreach (var value in Enum.GetValues(zoomModeType))
                {
                    options.Add((ZoomModeDisplay(value.ToString()), value));
                }
            }
            return options;
        }

        #endregion

        #region List-Based Part Extraction

        /// <summary>
        /// Checks if a part is a list-based part that needs special handling.
        /// </summary>
        private static bool IsListBasedPart(ScenPart part)
        {
            string typeName = part.GetType().Name;
            return typeName == "ScenPart_ConfigPage_ConfigureStartingPawns_KindDefs" ||
                   (ModsConfig.BiotechActive && typeName == "ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes") ||
                   (ModsConfig.AnomalyActive && typeName == "ScenPart_ConfigPage_ConfigureStartingPawns_Mutants") ||
                   typeName == "ScenPart_PlanetLayer";
        }

        /// <summary>
        /// Extracts list items from a list-based part.
        /// </summary>
        private static List<ListItemData> ExtractListItems(ScenPart part)
        {
            string typeName = part.GetType().Name;

            if (typeName == "ScenPart_ConfigPage_ConfigureStartingPawns_KindDefs")
                return ExtractKindDefsListItems(part);
            if (ModsConfig.BiotechActive && typeName == "ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes")
                return ExtractXenotypeListItems(part);
            if (ModsConfig.AnomalyActive && typeName == "ScenPart_ConfigPage_ConfigureStartingPawns_Mutants")
                return ExtractMutantListItems(part);
            if (typeName == "ScenPart_PlanetLayer")
                return ExtractLayerConnectionItems(part);

            return new List<ListItemData>();
        }

        /// <summary>
        /// Extracts list items from ScenPart_ConfigPage_ConfigureStartingPawns_KindDefs.
        /// </summary>
        private static List<ListItemData> ExtractKindDefsListItems(ScenPart part)
        {
            var listItems = new List<ListItemData>();
            var kindCountsField = part.GetType().GetField("kindCounts", BindingFlags.Public | BindingFlags.Instance);
            if (kindCountsField == null) return listItems;

            var kindCounts = kindCountsField.GetValue(part) as System.Collections.IList;
            if (kindCounts == null) return listItems;

            for (int i = 0; i < kindCounts.Count; i++)
            {
                var item = kindCounts[i];
                var kindDefField = item.GetType().GetField("kindDef", BindingFlags.Public | BindingFlags.Instance);
                var countField = item.GetType().GetField("count", BindingFlags.Public | BindingFlags.Instance);
                var requiredField = item.GetType().GetField("requiredAtStart", BindingFlags.Public | BindingFlags.Instance);

                var kindDef = kindDefField?.GetValue(item) as PawnKindDef;
                int count = countField != null ? (int)countField.GetValue(item) : 1;
                bool required = requiredField != null && (bool)requiredField.GetValue(item);

                string kindLabel = kindDef?.LabelCap ?? "Unknown".Translate().CapitalizeFirst();
                string label = ListItemCountLabel(count, kindLabel, required);

                var listItem = new ListItemData
                {
                    Label = label,
                    Index = i,
                    ItemReference = item,
                    IsExpanded = false,
                    Fields = new List<PartField>()
                };

                // Add fields for this item
                int capturedIndex = i;
                listItem.Fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Count".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = count.ToString(),
                    Data = new int[] { 1, 10 },
                    SetValue = (val) =>
                    {
                        var list = kindCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            countField.SetValue(entry, Convert.ToInt32(val));
                        }
                    }
                });

                listItem.Fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.PawnKind".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = kindLabel,
                    Data = GetPawnKindDefOptions(),
                    SetValue = (val) =>
                    {
                        var list = kindCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            kindDefField.SetValue(entry, val);
                        }
                    }
                });

                listItem.Fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.RequiredAtStart".Translate(),
                    Type = FieldType.Checkbox,
                    CurrentValue = BoolDisplay(required),
                    BoolValue = required,
                    Data = null,
                    SetValue = (val) =>
                    {
                        var list = kindCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            bool newVal = val is bool b && b;
                            requiredField.SetValue(entry, newVal);
                        }
                    }
                });

                listItems.Add(listItem);
            }

            return listItems;
        }

        /// <summary>
        /// Extracts list items from ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes.
        /// </summary>
        private static List<ListItemData> ExtractXenotypeListItems(ScenPart part)
        {
            var listItems = new List<ListItemData>();
            if (!ModsConfig.BiotechActive) return listItems;

            var xenotypeCountsField = part.GetType().GetField("xenotypeCounts", BindingFlags.Public | BindingFlags.Instance);
            if (xenotypeCountsField == null) return listItems;

            var xenotypeCounts = xenotypeCountsField.GetValue(part) as System.Collections.IList;
            if (xenotypeCounts == null) return listItems;

            for (int i = 0; i < xenotypeCounts.Count; i++)
            {
                var item = xenotypeCounts[i];
                var xenotypeField = item.GetType().GetField("xenotype", BindingFlags.Public | BindingFlags.Instance);
                var countField = item.GetType().GetField("count", BindingFlags.Public | BindingFlags.Instance);
                var requiredField = item.GetType().GetField("requiredAtStart", BindingFlags.Public | BindingFlags.Instance);

                var xenotype = xenotypeField?.GetValue(item) as XenotypeDef;
                int count = countField != null ? (int)countField.GetValue(item) : 1;
                bool required = requiredField != null && (bool)requiredField.GetValue(item);

                string xenoLabel = xenotype?.LabelCap ?? "Unknown".Translate().CapitalizeFirst();
                string label = ListItemCountLabel(count, xenoLabel, required);

                var listItem = new ListItemData
                {
                    Label = label,
                    Index = i,
                    ItemReference = item,
                    IsExpanded = false,
                    Fields = new List<PartField>()
                };

                int capturedIndex = i;
                listItem.Fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Count".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = count.ToString(),
                    Data = new int[] { 1, 10 },
                    SetValue = (val) =>
                    {
                        var list = xenotypeCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            countField.SetValue(entry, Convert.ToInt32(val));
                        }
                    }
                });

                listItem.Fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Xenotype".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = xenoLabel,
                    Data = GetXenotypeDefOptions(),
                    SetValue = (val) =>
                    {
                        var list = xenotypeCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            xenotypeField.SetValue(entry, val);
                        }
                    }
                });

                listItem.Fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.RequiredAtStart".Translate(),
                    Type = FieldType.Checkbox,
                    CurrentValue = BoolDisplay(required),
                    BoolValue = required,
                    Data = null,
                    SetValue = (val) =>
                    {
                        var list = xenotypeCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            bool newVal = val is bool b && b;
                            requiredField.SetValue(entry, newVal);
                        }
                    }
                });

                listItems.Add(listItem);
            }

            return listItems;
        }

        /// <summary>
        /// Extracts list items from ScenPart_ConfigPage_ConfigureStartingPawns_Mutants.
        /// </summary>
        private static List<ListItemData> ExtractMutantListItems(ScenPart part)
        {
            var listItems = new List<ListItemData>();
            if (!ModsConfig.AnomalyActive) return listItems;

            var mutantCountsField = part.GetType().GetField("mutantCounts", BindingFlags.Public | BindingFlags.Instance);
            if (mutantCountsField == null) return listItems;

            var mutantCounts = mutantCountsField.GetValue(part) as System.Collections.IList;
            if (mutantCounts == null) return listItems;

            for (int i = 0; i < mutantCounts.Count; i++)
            {
                var item = mutantCounts[i];
                var mutantField = item.GetType().GetField("mutant", BindingFlags.Public | BindingFlags.Instance);
                var countField = item.GetType().GetField("count", BindingFlags.Public | BindingFlags.Instance);
                var requiredField = item.GetType().GetField("requiredAtStart", BindingFlags.Public | BindingFlags.Instance);

                var mutant = mutantField?.GetValue(item) as MutantDef;
                int count = countField != null ? (int)countField.GetValue(item) : 1;
                bool required = requiredField != null && (bool)requiredField.GetValue(item);

                string mutantLabel = mutant?.LabelCap ?? "None".Translate().CapitalizeFirst();
                string label = ListItemCountLabel(count, mutantLabel, required);

                var listItem = new ListItemData
                {
                    Label = label,
                    Index = i,
                    ItemReference = item,
                    IsExpanded = false,
                    Fields = new List<PartField>()
                };

                int capturedIndex = i;
                listItem.Fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Count".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = count.ToString(),
                    Data = new int[] { 1, 10 },
                    SetValue = (val) =>
                    {
                        var list = mutantCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            countField.SetValue(entry, Convert.ToInt32(val));
                        }
                    }
                });

                listItem.Fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MutantType".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = mutantLabel,
                    Data = GetMutantDefOptions(),
                    SetValue = (val) =>
                    {
                        var list = mutantCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            mutantField.SetValue(entry, val);
                        }
                    }
                });

                listItem.Fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.RequiredAtStart".Translate(),
                    Type = FieldType.Checkbox,
                    CurrentValue = BoolDisplay(required),
                    BoolValue = required,
                    Data = null,
                    SetValue = (val) =>
                    {
                        var list = mutantCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            bool newVal = val is bool b && b;
                            requiredField.SetValue(entry, newVal);
                        }
                    }
                });

                listItems.Add(listItem);
            }

            return listItems;
        }

        /// <summary>
        /// Extracts list items from ScenPart_PlanetLayer connections.
        /// </summary>
        private static List<ListItemData> ExtractLayerConnectionItems(ScenPart part)
        {
            var listItems = new List<ListItemData>();

            var connectionsField = part.GetType().GetField("connections", BindingFlags.Public | BindingFlags.Instance);
            if (connectionsField == null) return listItems;

            var connections = connectionsField.GetValue(part) as System.Collections.IList;
            if (connections == null) return listItems;

            var connectionsList = new List<object>();
            foreach (var conn in connections)
                connectionsList.Add(conn);

            for (int i = 0; i < connections.Count; i++)
            {
                var item = connections[i];
                var tagField = item.GetType().GetField("tag", BindingFlags.Public | BindingFlags.Instance);
                var zoomModeField = item.GetType().GetField("zoomMode", BindingFlags.Public | BindingFlags.Instance);
                var fuelCostField = item.GetType().GetField("fuelCost", BindingFlags.Public | BindingFlags.Instance);

                string tag = tagField?.GetValue(item) as string ?? "";
                var zoomMode = zoomModeField?.GetValue(item);
                float fuelCost = fuelCostField != null ? (float)fuelCostField.GetValue(item) : 0f;

                // zoomMode.ToString() is the C# enum name (language-independent), used only to
                // decide presence; the user-facing zoom word is localized via ZoomModeDisplay.
                string zoomEnum = zoomMode?.ToString() ?? "None";
                string label = zoomEnum != "None"
                    ? (string)"RimWorldAccess.ScenarioBuilder.ConnectionToWithZoom".Translate(tag, ZoomModeDisplay(zoomEnum))
                    : (string)"RimWorldAccess.ScenarioBuilder.ConnectionTo".Translate(tag);

                var listItem = new ListItemData
                {
                    Label = label,
                    Index = i,
                    ItemReference = item,
                    IsExpanded = false,
                    Fields = new List<PartField>()
                };

                int capturedIndex = i;

                listItem.Fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.TargetLayer".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = tag,
                    Data = GetAvailableTagOptions(part, connectionsList),
                    SetValue = (val) =>
                    {
                        if (val?.ToString() == "__REMOVE__")
                        {
                            // Remove this connection
                            var list = connectionsField.GetValue(part) as System.Collections.IList;
                            if (list != null && capturedIndex < list.Count)
                            {
                                list.RemoveAt(capturedIndex);
                            }
                        }
                        else
                        {
                            var list = connectionsField.GetValue(part) as System.Collections.IList;
                            if (list != null && capturedIndex < list.Count)
                            {
                                var entry = list[capturedIndex];
                                tagField.SetValue(entry, val?.ToString());
                            }
                        }
                    }
                });

                listItem.Fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.ZoomMode".Translate(),
                    Type = FieldType.Dropdown,
                    CurrentValue = ZoomModeDisplay(zoomEnum),
                    Data = GetZoomModeOptions(),
                    SetValue = (val) =>
                    {
                        var list = connectionsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            zoomModeField.SetValue(entry, val);
                        }
                    }
                });

                listItem.Fields.Add(new PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.FuelCost".Translate(),
                    Type = FieldType.Quantity,
                    CurrentValue = fuelCost.ToString("F1"),
                    Data = new float[] { 0f, 10000f },
                    SetValue = (val) =>
                    {
                        var list = connectionsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            fuelCostField.SetValue(entry, Convert.ToSingle(val));
                        }
                    }
                });

                listItems.Add(listItem);
            }

            return listItems;
        }

        /// <summary>
        /// Adds a new item to a list-based part.
        /// </summary>
        private static void AddListItem(PartTreeItem partItem)
        {
            var part = partItem.Part;
            string typeName = part.GetType().Name;

            if (typeName == "ScenPart_ConfigPage_ConfigureStartingPawns_KindDefs")
            {
                var kindCountsField = part.GetType().GetField("kindCounts", BindingFlags.Public | BindingFlags.Instance);
                var kindCounts = kindCountsField?.GetValue(part) as System.Collections.IList;
                if (kindCounts != null)
                {
                    var kindCountType = AccessTools.TypeByName("RimWorld.PawnKindCount");
                    var newItem = Activator.CreateInstance(kindCountType);
                    var kindDefField = kindCountType.GetField("kindDef", BindingFlags.Public | BindingFlags.Instance);
                    var countField = kindCountType.GetField("count", BindingFlags.Public | BindingFlags.Instance);
                    kindDefField.SetValue(newItem, PawnKindDefOf.Colonist);
                    countField.SetValue(newItem, 1);
                    kindCounts.Add(newItem);
                    SetDirty();
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.AddedPawnKindEntry".Loc());
                }
            }
            else if (ModsConfig.BiotechActive && typeName == "ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes")
            {
                var xenotypeCountsField = part.GetType().GetField("xenotypeCounts", BindingFlags.Public | BindingFlags.Instance);
                var xenotypeCounts = xenotypeCountsField?.GetValue(part) as System.Collections.IList;
                if (xenotypeCounts != null)
                {
                    var xenoCountType = AccessTools.TypeByName("RimWorld.XenotypeCount");
                    var newItem = Activator.CreateInstance(xenoCountType);
                    var xenotypeField = xenoCountType.GetField("xenotype", BindingFlags.Public | BindingFlags.Instance);
                    var countField = xenoCountType.GetField("count", BindingFlags.Public | BindingFlags.Instance);
                    xenotypeField.SetValue(newItem, XenotypeDefOf.Baseliner);
                    countField.SetValue(newItem, 1);
                    xenotypeCounts.Add(newItem);
                    SetDirty();
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.AddedXenotypeEntry".Loc());
                }
            }
            else if (ModsConfig.AnomalyActive && typeName == "ScenPart_ConfigPage_ConfigureStartingPawns_Mutants")
            {
                var mutantCountsField = part.GetType().GetField("mutantCounts", BindingFlags.Public | BindingFlags.Instance);
                var mutantCounts = mutantCountsField?.GetValue(part) as System.Collections.IList;
                if (mutantCounts != null)
                {
                    var mutantCountType = AccessTools.TypeByName("RimWorld.MutantCount");
                    var newItem = Activator.CreateInstance(mutantCountType);
                    var countField = mutantCountType.GetField("count", BindingFlags.Public | BindingFlags.Instance);
                    countField.SetValue(newItem, 1);
                    // mutant field defaults to null which means "None"
                    mutantCounts.Add(newItem);
                    SetDirty();
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.AddedMutantEntry".Loc());
                }
            }
            else if (typeName == "ScenPart_PlanetLayer")
            {
                // For connections, we need to find available tags
                var connectionsField = part.GetType().GetField("connections", BindingFlags.Public | BindingFlags.Instance);
                var connections = connectionsField?.GetValue(part) as System.Collections.IList;
                if (connections != null)
                {
                    // Get unused tags
                    var usedTags = new HashSet<string>();
                    foreach (var conn in connections)
                    {
                        var tagField = conn.GetType().GetField("tag", BindingFlags.Public | BindingFlags.Instance);
                        var tag = tagField?.GetValue(conn) as string;
                        if (!string.IsNullOrEmpty(tag))
                            usedTags.Add(tag);
                    }

                    // Find first available tag from other PlanetLayer parts
                    string availableTag = null;
                    if (currentScenario != null)
                    {
                        foreach (var otherPart in currentScenario.AllParts)
                        {
                            if (otherPart == part) continue;
                            if (otherPart.GetType().Name != "ScenPart_PlanetLayer") continue;

                            var tagField = otherPart.GetType().GetField("tag", BindingFlags.Public | BindingFlags.Instance);
                            var tag = tagField?.GetValue(otherPart) as string;
                            if (!string.IsNullOrEmpty(tag) && !usedTags.Contains(tag))
                            {
                                availableTag = tag;
                                break;
                            }
                        }
                    }

                    if (availableTag == null)
                    {
                        TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.NoLayerTagsAvailable".Loc());
                        return;
                    }

                    var connectionType = AccessTools.TypeByName("RimWorld.LayerConnection");
                    var newItem = Activator.CreateInstance(connectionType);
                    var newTagField = connectionType.GetField("tag", BindingFlags.Public | BindingFlags.Instance);
                    newTagField.SetValue(newItem, availableTag);
                    connections.Add(newItem);
                    SetDirty();
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.AddedConnectionTo".Loc(availableTag));
                }
            }
        }

        /// <summary>
        /// Deletes a list item from a list-based part.
        /// </summary>
        private static void DeleteListItem(PartTreeItem partItem, int listItemIndex)
        {
            var part = partItem.Part;
            string typeName = part.GetType().Name;

            System.Collections.IList list = null;
            string itemType = (string)"RimWorldAccess.ScenarioBuilder.ItemType.Item".Translate();

            if (typeName == "ScenPart_ConfigPage_ConfigureStartingPawns_KindDefs")
            {
                var kindCountsField = part.GetType().GetField("kindCounts", BindingFlags.Public | BindingFlags.Instance);
                list = kindCountsField?.GetValue(part) as System.Collections.IList;
                itemType = (string)"RimWorldAccess.ScenarioBuilder.ItemType.PawnKindEntry".Translate();
            }
            else if (ModsConfig.BiotechActive && typeName == "ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes")
            {
                var xenotypeCountsField = part.GetType().GetField("xenotypeCounts", BindingFlags.Public | BindingFlags.Instance);
                list = xenotypeCountsField?.GetValue(part) as System.Collections.IList;
                itemType = (string)"RimWorldAccess.ScenarioBuilder.ItemType.XenotypeEntry".Translate();
            }
            else if (ModsConfig.AnomalyActive && typeName == "ScenPart_ConfigPage_ConfigureStartingPawns_Mutants")
            {
                var mutantCountsField = part.GetType().GetField("mutantCounts", BindingFlags.Public | BindingFlags.Instance);
                list = mutantCountsField?.GetValue(part) as System.Collections.IList;
                itemType = (string)"RimWorldAccess.ScenarioBuilder.ItemType.MutantEntry".Translate();
            }
            else if (typeName == "ScenPart_PlanetLayer")
            {
                var connectionsField = part.GetType().GetField("connections", BindingFlags.Public | BindingFlags.Instance);
                list = connectionsField?.GetValue(part) as System.Collections.IList;
                itemType = (string)"RimWorldAccess.ScenarioBuilder.ItemType.Connection".Translate();
            }

            if (list != null && listItemIndex >= 0 && listItemIndex < list.Count)
            {
                list.RemoveAt(listItemIndex);
                SetDirty();
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.RemovedItemType".Loc(itemType));
            }
        }

        #endregion

        #region Navigation

        /// <summary>
        /// Switches to the next section (Tab key).
        /// </summary>
        public static void NextSection()
        {
            if (IsEditingText)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.ConfirmOrCancelEditingFirst".Loc());
                return;
            }

            if (currentSection == Section.Metadata)
            {
                currentSection = Section.Parts;
                partsTreeNav.SetSelectedIndex(0);
                partsTreeNav.Typeahead.ClearSearch();
                MenuHelper.ResetLevel("ScenarioBuilderParts");
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartsSection".Loc());
                AnnounceCurrentTreeItem();
            }
            else
            {
                currentSection = Section.Metadata;
                metadataIndex = 0;
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.MetadataSection".Loc());
                AnnounceCurrentMetadataField();
            }
        }

        /// <summary>
        /// Switches to the previous section (Shift+Tab key).
        /// </summary>
        public static void PreviousSection()
        {
            // Same as NextSection since there are only 2 sections
            NextSection();
        }

        #endregion

        #region Metadata Navigation

        /// <summary>
        /// Moves to the next metadata field.
        /// </summary>
        public static void MetadataNext()
        {
            if (IsEditingText) return;

            metadataIndex = MenuHelper.SelectNext(metadataIndex, MetadataFieldCount);
            AnnounceCurrentMetadataField();
        }

        /// <summary>
        /// Moves to the previous metadata field.
        /// </summary>
        public static void MetadataPrevious()
        {
            if (IsEditingText) return;

            metadataIndex = MenuHelper.SelectPrevious(metadataIndex, MetadataFieldCount);
            AnnounceCurrentMetadataField();
        }

        /// <summary>
        /// Jumps to first metadata field.
        /// </summary>
        public static void MetadataHome()
        {
            if (IsEditingText) return;

            metadataIndex = 0;
            AnnounceCurrentMetadataField();
        }

        /// <summary>
        /// Jumps to last metadata field.
        /// </summary>
        public static void MetadataEnd()
        {
            if (IsEditingText) return;

            metadataIndex = MetadataFieldCount - 1;
            AnnounceCurrentMetadataField();
        }

        /// <summary>
        /// Announces the current metadata field.
        /// </summary>
        private static void AnnounceCurrentMetadataField()
        {
            if (currentScenario == null) return;

            string fieldName;
            string fieldValue;
            string hint = IsEditingText ? "" : " " + (string)"RimWorldAccess.ScenarioBuilder.PressEnterToEditHint".Translate();

            switch (metadataIndex)
            {
                case 0:
                    fieldName = "Title".Translate();
                    fieldValue = currentScenario.name ?? "";
                    break;
                case 1:
                    fieldName = "Summary".Translate();
                    fieldValue = currentScenario.summary ?? "";
                    break;
                case 2:
                    fieldName = "Description".Translate();
                    fieldValue = currentScenario.description ?? "";
                    break;
                default:
                    return;
            }

            string positionPart = MenuHelper.FormatPosition(metadataIndex, MetadataFieldCount);
            string text = $"{fieldName}: {(string.IsNullOrEmpty(fieldValue) ? (string)"RimWorldAccess.ScenarioBuilder.FieldEmptyValue".Translate() : fieldValue)}{hint}";

            if (!string.IsNullOrEmpty(positionPart))
            {
                text += $" ({positionPart})";
            }

            TolkHelper.SpeakData(text);
        }

        /// <summary>
        /// Begins editing the current metadata field via the unified TextInputController.
        /// Per-field max lengths mirror RimWorld's <c>TrimmedToLength</c> caps and
        /// ForbidGrammarSpecials blocks <c>[ ] { }</c> since these strings flow through
        /// GrammarResolver.
        /// </summary>
        public static void BeginEditMetadataField()
        {
            if (currentScenario == null) return;

            string current;
            int maxLen;
            string labelKey;
            switch (metadataIndex)
            {
                case 0:
                    editingFieldName = (string)"Title".Translate();
                    current = currentScenario.name ?? string.Empty;
                    maxLen = 55;
                    labelKey = "RimWorldAccess.TextInput.LabelScenarioTitle";
                    break;
                case 1:
                    editingFieldName = (string)"Summary".Translate();
                    current = currentScenario.summary ?? string.Empty;
                    maxLen = 300;
                    labelKey = "RimWorldAccess.TextInput.LabelScenarioSummary";
                    break;
                case 2:
                    editingFieldName = (string)"Description".Translate();
                    current = currentScenario.description ?? string.Empty;
                    maxLen = 1000;
                    labelKey = "RimWorldAccess.TextInput.LabelScenarioDescription";
                    break;
                default:
                    return;
            }

            // Title is single-line; Summary and Description are long-form narrative
            // fields rendered via Widgets.TextArea in vanilla and should support
            // Shift+Enter line breaks + Up/Down line navigation.
            bool multiLine = metadataIndex == 1 || metadataIndex == 2;
            var spec = new TextFieldSpec(
                labelKey: labelKey,
                maxLength: maxLen,
                minLength: 0,
                forbidGrammarSpecials: true,
                multiLine: multiLine);

            metadataController.Begin(current, spec, ApplyEditedMetadata, OnMetadataCancel, replaceOnType: true);
        }

        private static void ApplyEditedMetadata(string newValue)
        {
            if (currentScenario == null) return;
            switch (metadataIndex)
            {
                case 0: currentScenario.name = newValue; break;
                case 1: currentScenario.summary = newValue; break;
                case 2: currentScenario.description = newValue; break;
            }
            isDirty = true;
            TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.FieldSetTo".Loc(editingFieldName, string.IsNullOrEmpty(newValue) ? (string)"RimWorldAccess.ScenarioBuilder.FieldEmptyValue".Translate() : newValue));
        }

        private static void OnMetadataCancel()
        {
            TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.FieldEditCancelled".Loc(editingFieldName));
        }

        #endregion

        #region Parts TreeView Navigation

        /// <summary>
        /// Navigates up in the parts tree.
        /// </summary>
        public static void PartsNavigateUp()
        {
            if (partsTreeNav.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.NoParts".Loc());
                return;
            }

            partsTreeNav.SelectPrevious();
        }

        /// <summary>
        /// Navigates down in the parts tree.
        /// </summary>
        public static void PartsNavigateDown()
        {
            if (partsTreeNav.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.NoParts".Loc());
                return;
            }

            partsTreeNav.SelectNext();
        }

        /// <summary>
        /// Expands the current item or drills down to first child.
        /// Syncs expansion state to domain objects before delegating to TreeNavigationHelper.
        /// </summary>
        public static void ExpandOrDrillDown()
        {
            var selected = partsTreeNav.SelectedItem;
            if (selected == null) return;

            var data = GetItemData(selected);
            if (data == null) return;

            // Sync expansion state to domain objects when expanding
            partsTreeNav.OnBeforeExpand = (item) =>
            {
                var d = GetItemData(item);
                if (d != null)
                {
                    if (d.IsListItem && d.ListItemData != null)
                        d.ListItemData.IsExpanded = true;
                    else if (d.AsPart != null)
                        d.AsPart.IsExpanded = true;
                }
            };

            partsTreeNav.ExpandOrDrillDown();
        }

        /// <summary>
        /// Activates the current item (Enter key behavior).
        /// Edits fields, provides feedback for non-editable parts.
        /// </summary>
        public static void ActivateCurrentItem()
        {
            var selected = partsTreeNav.SelectedItem;
            if (selected == null) return;

            var data = GetItemData(selected);
            if (data == null) return;

            // Handle "Add New Item" action
            if (data.IsAddAction)
            {
                if (data.ParentPart != null)
                {
                    AddListItem(data.ParentPart);
                    BuildPartsTree();
                    RebuildPartsInspectionTree();
                    // Stay on the add action or move to new item
                    AnnounceCurrentTreeItem();
                }
                return;
            }

            // Handle list items (expandable containers)
            if (data.IsListItem && selected.IsExpandable)
            {
                ExpandOrDrillDown();
                return;
            }

            if (data.IsField)
            {
                // On a field - edit it
                EditCurrentField();
            }
            else if (data.IsPart && selected.IsExpandable)
            {
                // On expandable part - toggle expansion
                ExpandOrDrillDown();
            }
            else if (data.IsPart && !selected.IsExpandable)
            {
                // Part has no editable fields - provide feedback
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartHasNoFields".Loc());
            }
        }

        /// <summary>
        /// Collapses the current item or drills up to parent.
        /// Syncs collapse state to domain objects, then delegates to TreeNavigationHelper.
        /// </summary>
        public static void CollapseOrDrillUp()
        {
            var selected = partsTreeNav.SelectedItem;
            if (selected == null) return;

            var data = GetItemData(selected);
            if (data == null)
            {
                partsTreeNav.CollapseOrDrillUp();
                return;
            }

            // Sync collapse state to domain objects
            if (selected.IsExpandable && selected.IsExpanded)
            {
                if (data.IsListItem && data.ListItemData != null)
                    data.ListItemData.IsExpanded = false;
                else if (data.AsPart != null)
                    data.AsPart.IsExpanded = false;
            }

            partsTreeNav.CollapseOrDrillUp();
        }

        /// <summary>
        /// Handles Home key navigation.
        /// </summary>
        public static void PartsHandleHomeKey(bool ctrlPressed)
        {
            partsTreeNav.JumpToFirst(ctrlPressed);
        }

        /// <summary>
        /// Handles End key navigation.
        /// </summary>
        public static void PartsHandleEndKey(bool ctrlPressed)
        {
            partsTreeNav.JumpToLast(ctrlPressed);
        }

        /// <summary>
        /// WCAG tree view pattern: * key expands all siblings at the current level.
        /// Syncs expansion state to domain objects, then delegates to TreeNavigationHelper.
        /// </summary>
        public static void ExpandAllSiblings()
        {
            // Set up the before-expand callback to sync domain state
            partsTreeNav.OnBeforeExpand = (item) =>
            {
                var d = GetItemData(item);
                if (d != null)
                {
                    if (d.IsListItem && d.ListItemData != null)
                        d.ListItemData.IsExpanded = true;
                    else if (d.AsPart != null)
                        d.AsPart.IsExpanded = true;
                }
            };

            partsTreeNav.ExpandAllSiblings();
        }

        /// <summary>
        /// Strips trailing punctuation (periods, colons, exclamation marks) from a string.
        /// Prevents patterns like ". :" or ": :" when concatenating strings.
        /// </summary>
        private static string StripTrailingPunctuation(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.TrimEnd('.', ':', '!', '?', ',', ';');
        }

        /// <summary>
        /// Gets a display name for a part including its summary for context.
        /// Example: "Scattered Randomly (packaged survival meal x 7)"
        /// </summary>
        private static string GetPartDisplayName(PartTreeItem partItem)
        {
            string label = StripTrailingPunctuation(partItem.Label);
            string summaryText = StripTrailingPunctuation(partItem.Summary);
            if (!string.IsNullOrEmpty(summaryText))
            {
                return $"{label} ({summaryText})";
            }
            return label;
        }

        /// <summary>
        /// Announces the current tree item via the TreeNavigationHelper.
        /// </summary>
        private static void AnnounceCurrentTreeItem()
        {
            if (partsTreeNav.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.NoParts".Loc());
                return;
            }

            partsTreeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Custom announcement formatter for the parts tree items.
        /// </summary>
        private static string FormatTreeItemAnnouncement(InspectionTreeItem item)
        {
            var data = GetItemData(item);
            if (data == null) return item.Label;

            var (position, total) = partsTreeNav.GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            string announcement;

            // Handle "Add New Item" action
            if (data.IsAddAction)
            {
                announcement = (string)"RimWorldAccess.ScenarioBuilder.Announce.AddNewItem".Translate();
            }
            // Handle list items (level 1 in list-based parts)
            else if (data.IsListItem)
            {
                string itemLabel = StripTrailingPunctuation(item.Label);
                if (item.IsExpandable)
                {
                    string state = TreeNavigationHelper.GetExpansionStateWord(item);
                    int fieldCount = data.ListItemData?.Fields.Count ?? 0;
                    announcement = (string)"RimWorldAccess.ScenarioBuilder.Announce.ListItemExpandable".Translate(
                        itemLabel, state, FieldCount(fieldCount));
                }
                else
                {
                    announcement = (string)"RimWorldAccess.ScenarioBuilder.Announce.ListItemSimple".Translate(itemLabel);
                }
            }
            // Single-field items have BOTH AsPart and Field set - treat them as editable fields
            else if (data.IsPart && data.IsField)
            {
                var part = data.AsPart;
                var field = data.Field;
                string partLabel = StripTrailingPunctuation(part.Label);
                string fieldValue = StripTrailingPunctuation(field.CurrentValue);
                if (field.Type == FieldType.Checkbox)
                {
                    announcement = (string)"RimWorldAccess.ScenarioBuilder.Announce.CheckboxField".Translate(
                        partLabel, CheckState(field.BoolValue));
                }
                else
                {
                    announcement = (string)"RimWorldAccess.ScenarioBuilder.Announce.EditableField".Translate(
                        partLabel, fieldValue, TypeHint(field.Type));
                }
                if (field.Type == FieldType.Text && field.Data is string fullText && fullText.Length > 60)
                {
                    announcement += (string)"RimWorldAccess.ScenarioBuilder.Announce.ReadFullTextSuffix".Translate();
                }
            }
            else if (data.IsPart)
            {
                var part = data.AsPart;
                string partLabel = StripTrailingPunctuation(part.Label);
                string summaryText = StripTrailingPunctuation(part.Summary);
                string combinedLabel = string.IsNullOrEmpty(summaryText)
                    ? partLabel
                    : (string)"RimWorldAccess.ScenarioBuilder.Announce.LabelWithSummary".Translate(partLabel, summaryText);

                if (item.IsExpandable)
                {
                    string state = TreeNavigationHelper.GetExpansionStateWord(item);

                    int childCount;
                    string countStr;
                    if (part.IsListPart)
                    {
                        childCount = part.ListItems.Count + part.Fields.Count;
                        countStr = ItemCount(childCount);
                    }
                    else
                    {
                        childCount = part.Fields.Count;
                        countStr = FieldCount(childCount);
                    }
                    announcement = (string)"RimWorldAccess.ScenarioBuilder.Announce.PartExpandable".Translate(
                        combinedLabel, state, countStr);
                }
                else
                {
                    announcement = (string)"RimWorldAccess.ScenarioBuilder.Announce.PartReadOnly".Translate(combinedLabel);
                }
            }
            else if (data.IsField)
            {
                var field = data.Field;
                string fieldName = StripTrailingPunctuation(field.Name);
                string fieldValue = StripTrailingPunctuation(field.CurrentValue);
                if (field.Type == FieldType.Checkbox)
                {
                    announcement = (string)"RimWorldAccess.ScenarioBuilder.Announce.CheckboxField".Translate(
                        fieldName, CheckState(field.BoolValue));
                }
                else
                {
                    announcement = (string)"RimWorldAccess.ScenarioBuilder.Announce.EditableField".Translate(
                        fieldName, fieldValue, TypeHint(field.Type));
                }
                if (field.Type == FieldType.Text && field.Data is string fullText && fullText.Length > 60)
                {
                    announcement += (string)"RimWorldAccess.ScenarioBuilder.Announce.ReadFullTextSuffix".Translate();
                }
            }
            else
            {
                announcement = item.Label;
            }

            if (!string.IsNullOrEmpty(positionPart))
            {
                announcement += $" ({positionPart})";
            }

            announcement += MenuHelper.GetLevelSuffix("ScenarioBuilderParts", item.IndentLevel);

            return announcement;
        }

        /// <summary>
        /// Custom search announcement formatter for the parts tree.
        /// </summary>
        private static string FormatTreeSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            if (typeahead.HasActiveSearch)
            {
                return typeahead.BuildItemAnnouncement(item.Label);
            }
            return FormatTreeItemAnnouncement(item);
        }

        /// <summary>
        /// Reads the full text content of a Text field (for long content that's truncated in announcements).
        /// </summary>
        private static void ReadFullFieldText()
        {
            if (currentSection != Section.Parts) return;

            var selected = partsTreeNav.SelectedItem;
            if (selected == null)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.NoItemSelected".Loc());
                return;
            }

            var data = GetItemData(selected);

            // Check if this is a field item
            if (data != null && data.IsField && data.Field != null)
            {
                var field = data.Field;
                if (field.Type == FieldType.Text && field.Data is string fullText)
                {
                    if (string.IsNullOrEmpty(fullText))
                    {
                        TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.FieldEmpty".Loc());
                    }
                    else
                    {
                        TolkHelper.SpeakData(fullText);
                    }
                    return;
                }
            }

            // Not a text field
            TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.NotATextField".Loc());
        }

        /// <summary>
        /// Opens editor for the current field.
        /// </summary>
        public static void EditCurrentField()
        {
            var selected = partsTreeNav.SelectedItem;
            if (selected == null) return;

            var data = GetItemData(selected);
            if (data == null || !data.IsField) return;

            var field = data.Field;

            // Open the appropriate editor based on field type
            // For checkboxes, pass a simpler callback that doesn't re-announce
            // (checkbox toggle already announces the state change)
            if (field.Type == FieldType.Checkbox)
            {
                ScenarioBuilderPartEditState.Open(field, () =>
                {
                    // Just refresh the tree without re-announcing
                    BuildPartsTree();
                    RebuildPartsInspectionTree();
                });
            }
            else
            {
                ScenarioBuilderPartEditState.Open(field, () =>
                {
                    // Callback when edit is complete - refresh the tree and re-announce
                    BuildPartsTree();
                    RebuildPartsInspectionTree();
                    AnnounceCurrentTreeItem();
                });
            }
        }

        /// <summary>
        /// Deletes the current part or list item.
        /// </summary>
        public static void DeletePart()
        {
            if (partsTreeNav.Count == 0 || currentScenario == null) return;

            var selected = partsTreeNav.SelectedItem;
            if (selected == null) return;

            var data = GetItemData(selected);
            if (data == null) return;

            // Handle list items (delete from list, not the whole part)
            if (data.IsListItem)
            {
                PartTreeItem partItem = data.ParentPart;
                if (partItem != null && partItem.IsListPart)
                {
                    DeleteListItem(partItem, data.ListItemIndex);
                    BuildPartsTree();
                    RebuildPartsInspectionTree();
                    AnnounceCurrentTreeItem();
                    return;
                }
            }

            // Handle "Add New Item" action - can't delete this
            if (data.IsAddAction)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.CannotDeleteAddAction".Loc());
                return;
            }

            // Get the part (either the item itself or its parent)
            PartTreeItem partToDelete = data.IsPart ? data.AsPart : data.ParentPart;
            if (partToDelete == null) return;

            var part = partToDelete.Part;

            // Check if part can be removed
            if (!part.def.PlayerAddRemovable)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.PartCannotBeRemoved".Loc());
                return;
            }

            // Remember the part label before removing
            string label = partToDelete.Label;

            // Remove the part
            currentScenario.RemovePart(part);
            isDirty = true;
            BuildPartsTree();
            RebuildPartsInspectionTree();

            TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.RemovedLabel".Loc(label));
            AnnounceCurrentTreeItem();
        }

        /// <summary>
        /// Moves the current part up in the list.
        /// </summary>
        public static void MovePartUp()
        {
            if (partsTreeNav.Count == 0 || currentScenario == null) return;

            var selected = partsTreeNav.SelectedItem;
            if (selected == null) return;

            var data = GetItemData(selected);
            if (data == null) return;

            PartTreeItem partItem = data.IsPart ? data.AsPart : data.ParentPart;
            if (partItem == null) return;

            int partHierarchyIndex = partsHierarchy.IndexOf(partItem);
            if (partHierarchyIndex <= 0) return;

            var part = partItem.Part;
            string partName = StripTrailingPunctuation(part.Label);

            if (!currentScenario.CanReorder(part, ReorderDirection.Up))
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.CannotMoveUp".Loc(partName));
                return;
            }

            currentScenario.Reorder(part, ReorderDirection.Up);
            BuildPartsTree();
            RebuildPartsInspectionTree();

            // Find the new position of the part in the visible list
            for (int i = 0; i < partsTreeNav.VisibleItems.Count; i++)
            {
                var d = GetItemData(partsTreeNav.VisibleItems[i]);
                if (d != null && d.IsPart && d.AsPart.Part == part)
                {
                    partsTreeNav.SetSelectedIndex(i);
                    break;
                }
            }

            // Build context-aware announcement
            int newHierarchyIndex = partsHierarchy.FindIndex(p => p.Part == part);
            bool atTop = newHierarchyIndex == 0;
            bool atBottom = newHierarchyIndex == partsHierarchy.Count - 1;

            if (atTop)
            {
                if (!atBottom && partsHierarchy.Count > 1)
                {
                    string belowName = GetPartDisplayName(partsHierarchy[newHierarchyIndex + 1]);
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.MovedToTopAbove".Loc(partName, belowName));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.MovedToTopOfList".Loc(partName));
                }
            }
            else
            {
                string aboveName = GetPartDisplayName(partsHierarchy[newHierarchyIndex - 1]);
                if (!atBottom)
                {
                    string belowName = GetPartDisplayName(partsHierarchy[newHierarchyIndex + 1]);
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.MovedUpBetween".Loc(partName, aboveName, belowName));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.MovedUpBelow".Loc(partName, aboveName));
                }
            }
        }

        /// <summary>
        /// Moves the current part down in the list.
        /// </summary>
        public static void MovePartDown()
        {
            if (partsTreeNav.Count == 0 || currentScenario == null) return;

            var selected = partsTreeNav.SelectedItem;
            if (selected == null) return;

            var data = GetItemData(selected);
            if (data == null) return;

            PartTreeItem partItem = data.IsPart ? data.AsPart : data.ParentPart;
            if (partItem == null) return;

            int partHierarchyIndex = partsHierarchy.IndexOf(partItem);
            if (partHierarchyIndex >= partsHierarchy.Count - 1) return;

            var part = partItem.Part;
            string partName = StripTrailingPunctuation(part.Label);

            if (!currentScenario.CanReorder(part, ReorderDirection.Down))
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.CannotMoveDown".Loc(partName));
                return;
            }

            currentScenario.Reorder(part, ReorderDirection.Down);
            BuildPartsTree();
            RebuildPartsInspectionTree();

            // Find the new position of the part in the visible list
            for (int i = 0; i < partsTreeNav.VisibleItems.Count; i++)
            {
                var d = GetItemData(partsTreeNav.VisibleItems[i]);
                if (d != null && d.IsPart && d.AsPart.Part == part)
                {
                    partsTreeNav.SetSelectedIndex(i);
                    break;
                }
            }

            // Build context-aware announcement
            int newHierarchyIndex = partsHierarchy.FindIndex(p => p.Part == part);
            bool atTop = newHierarchyIndex == 0;
            bool atBottom = newHierarchyIndex == partsHierarchy.Count - 1;

            if (atBottom)
            {
                if (!atTop && partsHierarchy.Count > 1)
                {
                    string aboveName = GetPartDisplayName(partsHierarchy[newHierarchyIndex - 1]);
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.MovedToBottomBelow".Loc(partName, aboveName));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.MovedToBottomOfList".Loc(partName));
                }
            }
            else
            {
                string belowName = GetPartDisplayName(partsHierarchy[newHierarchyIndex + 1]);
                if (!atTop)
                {
                    string aboveName = GetPartDisplayName(partsHierarchy[newHierarchyIndex - 1]);
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.MovedDownBetween".Loc(partName, aboveName, belowName));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.MovedDownAbove".Loc(partName, belowName));
                }
            }
        }

        #endregion

        #region Parts Typeahead

        /// <summary>
        /// Gets whether parts typeahead is active.
        /// </summary>
        public static bool PartsHasActiveSearch => partsTreeNav.HasActiveSearch;

        /// <summary>
        /// Handles typeahead search in parts list.
        /// </summary>
        public static bool HandlePartsTypeahead(char character)
        {
            if (partsTreeNav.Count == 0)
                return false;

            var labels = partsTreeNav.VisibleItems.Select(item => item.Label).ToList();

            if (partsTreeNav.Typeahead.ProcessCharacterInput(character, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    partsTreeNav.SetSelectedIndex(newIndex);
                    AnnouncePartsWithSearch();
                }
            }
            else
            {
                partsTreeNav.Typeahead.SpeakNoMatches();
            }

            return true;
        }

        /// <summary>
        /// Handles backspace in parts typeahead.
        /// </summary>
        public static bool HandlePartsTypeaheadBackspace()
        {
            if (!partsTreeNav.HasActiveSearch)
                return false;

            var labels = partsTreeNav.VisibleItems.Select(item => item.Label).ToList();

            if (partsTreeNav.Typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    partsTreeNav.SetSelectedIndex(newIndex);
                    AnnouncePartsWithSearch();
                }
            }

            return true;
        }

        /// <summary>
        /// Clears the parts typeahead search.
        /// </summary>
        public static bool ClearPartsTypeahead()
        {
            if (partsTreeNav.Typeahead.ClearSearchAndAnnounce())
            {
                AnnounceCurrentTreeItem();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Navigates to next typeahead match.
        /// </summary>
        public static bool SelectNextPartsMatch()
        {
            if (!partsTreeNav.HasActiveSearch)
                return false;

            int next = partsTreeNav.Typeahead.GetNextMatch(partsTreeNav.SelectedIndex);
            if (next >= 0)
            {
                partsTreeNav.SetSelectedIndex(next);
                AnnouncePartsWithSearch();
            }
            return true;
        }

        /// <summary>
        /// Navigates to previous typeahead match.
        /// </summary>
        public static bool SelectPreviousPartsMatch()
        {
            if (!partsTreeNav.HasActiveSearch)
                return false;

            int prev = partsTreeNav.Typeahead.GetPreviousMatch(partsTreeNav.SelectedIndex);
            if (prev >= 0)
            {
                partsTreeNav.SetSelectedIndex(prev);
                AnnouncePartsWithSearch();
            }
            return true;
        }

        private static void AnnouncePartsWithSearch()
        {
            var selected = partsTreeNav.SelectedItem;
            if (selected == null) return;

            if (partsTreeNav.HasActiveSearch)
            {
                var data = GetItemData(selected);
                string label = (data != null && data.IsPart) ? data.AsPart.Label : selected.Label;
                TolkHelper.SpeakData(partsTreeNav.Typeahead.BuildItemAnnouncement(label));
            }
            else
            {
                AnnounceCurrentTreeItem();
            }
        }

        #endregion

        #region Actions (Hotkeys)

        /// <summary>
        /// Opens the add part menu (Alt+A).
        /// </summary>
        public static void OpenAddPartMenu()
        {
            if (currentScenario == null) return;

            ScenarioBuilderAddPartState.Open(currentScenario, (ScenPartDef selectedDef) =>
            {
                if (selectedDef != null)
                {
                    // Add the part
                    ScenPart newPart = ScenarioMaker.MakeScenPart(selectedDef);
                    newPart.Randomize();

                    // Access the internal parts list via reflection
                    var partsField = AccessTools.Field(typeof(Scenario), "parts");
                    var partsList = partsField.GetValue(currentScenario) as List<ScenPart>;
                    partsList?.Add(newPart);
                    isDirty = true;

                    // Refresh and select the new part
                    BuildPartsTree();
                    RebuildPartsInspectionTree();

                    // Find the new part in the visible list (it's the last part at level 0)
                    for (int i = partsTreeNav.VisibleItems.Count - 1; i >= 0; i--)
                    {
                        var d = GetItemData(partsTreeNav.VisibleItems[i]);
                        if (d != null && d.IsPart)
                        {
                            partsTreeNav.SetSelectedIndex(i);
                            break;
                        }
                    }

                    currentSection = Section.Parts;

                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.AddedPressEnterToEdit".Loc(selectedDef.LabelCap));
                }
            });
        }

        /// <summary>
        /// Opens the load scenario dialog (Alt+L).
        /// </summary>
        public static void OpenLoadDialog()
        {
            WindowlessScenarioLoadState.Open((Scenario loadedScenario) =>
            {
                if (loadedScenario != null && currentPage != null)
                {
                    // Update the editor with the loaded scenario
                    AccessTools.Field(typeof(Page_ScenarioEditor), "curScen").SetValue(currentPage, loadedScenario);
                    AccessTools.Field(typeof(Page_ScenarioEditor), "seedIsValid").SetValue(currentPage, false);

                    currentScenario = loadedScenario;
                    isDirty = false;
                    BuildPartsTree();
                    RebuildPartsInspectionTree();
                    currentSection = Section.Metadata;
                    metadataIndex = 0;
                    partsTreeNav.SetSelectedIndex(0);

                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.LoadedScenario".Loc(loadedScenario.name));
                }
            });
        }

        /// <summary>
        /// Opens the save scenario dialog (Alt+S).
        /// </summary>
        public static void OpenSaveDialog()
        {
            if (currentScenario == null) return;

            WindowlessScenarioSaveState.Open(currentScenario, () =>
            {
                isDirty = false;
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.ScenarioSaved".Loc());
            });
        }

        /// <summary>
        /// Randomizes the scenario seed (Alt+R).
        /// </summary>
        public static void RandomizeSeed()
        {
            if (currentPage == null) return;

            // Call the private RandomizeSeedAndScenario method
            var method = AccessTools.Method(typeof(Page_ScenarioEditor), "RandomizeSeedAndScenario");
            if (method != null)
            {
                method.Invoke(currentPage, null);

                // Refresh our reference to the scenario
                currentScenario = (Scenario)AccessTools.Field(typeof(Page_ScenarioEditor), "curScen").GetValue(currentPage);
                isDirty = true;
                BuildPartsTree();
                RebuildPartsInspectionTree();
                partsTreeNav.SetSelectedIndex(0);

                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.RandomizedNew".Loc(currentScenario?.name ?? (string)"RimWorldAccess.ScenarioBuilder.NewScenarioFallback".Translate()));
            }
        }

        /// <summary>
        /// Checks if the scenario has been modified.
        /// </summary>
        public static bool IsDirty()
        {
            return isDirty;
        }

        #endregion

        #region Input Handling

        /// <summary>
        /// Handles keyboard input for the scenario builder.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            // Text-editing mode is handled by TextInputManager at priority -1.6 in
            // UnifiedKeyboardPatch — that path absorbs Enter/Escape/Backspace/chars and
            // never reaches HandleInput. Nothing to dispatch here.

            // Alt key combinations (global hotkeys)
            if (alt)
            {
                switch (key)
                {
                    case KeyCode.L:
                        OpenLoadDialog();
                        return true;
                    case KeyCode.S:
                        OpenSaveDialog();
                        return true;
                    case KeyCode.R:
                        RandomizeSeed();
                        return true;
                    case KeyCode.A:
                        OpenAddPartMenu();
                        return true;
                }
            }

            // Tab to switch sections
            if (key == KeyCode.Tab)
            {
                if (shift)
                    PreviousSection();
                else
                    NextSection();
                return true;
            }

            // Section-specific navigation
            if (currentSection == Section.Metadata)
            {
                return HandleMetadataInput(key, shift, ctrl);
            }
            else
            {
                return HandlePartsInput(key, shift, ctrl);
            }
        }

        private static bool HandleMetadataInput(KeyCode key, bool shift, bool ctrl)
        {
            switch (key)
            {
                case KeyCode.UpArrow:
                    MetadataPrevious();
                    return true;
                case KeyCode.DownArrow:
                    MetadataNext();
                    return true;
                case KeyCode.Home:
                    MetadataHome();
                    return true;
                case KeyCode.End:
                    MetadataEnd();
                    return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    BeginEditMetadataField();
                    return true;
            }

            return false;
        }

        private static bool HandlePartsInput(KeyCode key, bool shift, bool ctrl)
        {
            // Handle typeahead backspace
            if (key == KeyCode.Backspace && partsTreeNav.HasActiveSearch)
            {
                HandlePartsTypeaheadBackspace();
                return true;
            }

            // Handle escape to clear search
            if (key == KeyCode.Escape)
            {
                if (partsTreeNav.HasActiveSearch)
                {
                    ClearPartsTypeahead();
                    return true;
                }
                // Let escape pass through to close the builder
                return false;
            }

            // Ctrl+Up/Down for reordering (check first to avoid conflict with regular navigation)
            if (ctrl)
            {
                if (key == KeyCode.UpArrow)
                {
                    MovePartUp();
                    return true;
                }
                if (key == KeyCode.DownArrow)
                {
                    MovePartDown();
                    return true;
                }
            }

            switch (key)
            {
                case KeyCode.UpArrow:
                    if (partsTreeNav.HasActiveSearch)
                        SelectPreviousPartsMatch();
                    else
                        PartsNavigateUp();
                    return true;
                case KeyCode.DownArrow:
                    if (partsTreeNav.HasActiveSearch)
                        SelectNextPartsMatch();
                    else
                        PartsNavigateDown();
                    return true;
                case KeyCode.RightArrow:
                    // WCAG tree pattern: Right expands or drills down to first child
                    ExpandOrDrillDown();
                    return true;
                case KeyCode.LeftArrow:
                    // WCAG tree pattern: Left collapses or drills up to parent
                    CollapseOrDrillUp();
                    return true;
                case KeyCode.Home:
                    PartsHandleHomeKey(ctrl);
                    return true;
                case KeyCode.End:
                    PartsHandleEndKey(ctrl);
                    return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Space:
                    // Enter/Space activates the current item (edit field, expand, or feedback)
                    ActivateCurrentItem();
                    return true;
                case KeyCode.Delete:
                    DeletePart();
                    return true;
                case KeyCode.KeypadMultiply:
                    // WCAG tree pattern: * expands all siblings
                    ExpandAllSiblings();
                    return true;
                case KeyCode.Insert:
                    ReadFullFieldText();
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Handles character input for typeahead or text editing.
        /// </summary>
        public static bool HandleCharacterInput(char character)
        {
            // Don't handle characters if save/load dialog is open
            if (WindowlessScenarioSaveState.IsActive || WindowlessScenarioLoadState.IsActive)
                return false;

            // Text-edit characters never reach here — TextInputManager catches them at
            // priority -1.6. Treat IsEditingText as a no-op guard for parts typeahead.
            if (IsEditingText) return true;

            if (currentSection == Section.Parts)
            {
                // WCAG tree pattern: * expands all siblings
                if (character == '*')
                {
                    ExpandAllSiblings();
                    return true;
                }

                if (char.IsLetterOrDigit(character))
                {
                    return HandlePartsTypeahead(character);
                }
            }

            return false;
        }

        #endregion
    }
}
