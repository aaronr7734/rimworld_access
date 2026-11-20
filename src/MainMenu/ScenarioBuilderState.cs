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
    /// Domain-model layer for the Scenario Builder (Page_ScenarioEditor), backing
    /// <see cref="RimWorldAccess.Shell.ScenarioEditorScreenScope"/>. Owns no navigation,
    /// presentation, or text-edit state — purely the row SOURCE: <see cref="BuildPartsTree"/>
    /// and <see cref="ExtractPartFields"/> reflect the live scenario into
    /// <see cref="PartTreeItem"/>/<see cref="PartField"/>, and every <c>SetValue</c> closure
    /// IS the mutation vehicle the scope's rows invoke. Expand state lives on the domain
    /// objects and survives a rebuild via BuildPartsTree's by-reference carry-over.
    /// </summary>
    public static partial class ScenarioBuilderState
    {
        public static bool IsActive { get; private set; }

        private static List<PartTreeItem> partsHierarchy = new List<PartTreeItem>();

        /// <summary>
        /// Read-only view of the current parts tree — the row source for the scope, which
        /// needs no expansion-tracking set of its own.
        /// </summary>
        internal static List<PartTreeItem> PartsHierarchy => partsHierarchy;

        /// <summary>
        /// A list item within a list-based part (e.g. PawnKindCount in KindDefs).
        /// </summary>
        public class ListItemData
        {
            public string Label { get; set; }
            public List<PartField> Fields { get; set; } = new List<PartField>();
            public int Index { get; set; }
            public object ItemReference { get; set; }
            public bool IsExpanded { get; set; }
        }

        private static Scenario currentScenario;
        private static Page_ScenarioEditor currentPage;
        private static bool isDirty;

        /// <summary>
        /// ScenPart_CreateIncident is `internal` in Assembly-CSharp, the one vanilla ScenPart
        /// type ExtractPartFields cannot name with <c>typeof</c>; cached by name so the
        /// dispatch stays an exact Type comparison.
        /// </summary>
        private static readonly Type ScenPartCreateIncidentType = AccessTools.TypeByName("RimWorld.ScenPart_CreateIncident");

        public static Scenario CurrentScenario => currentScenario;

        /// <summary>
        /// Replaces the scenario under edit (Load/Randomize Seed), so ScenarioBuilderActions
        /// can fold its results back in without the field becoming internal.
        /// </summary>
        internal static void SetCurrentScenario(Scenario scenario) => currentScenario = scenario;

        internal static Page_ScenarioEditor CurrentPage => currentPage;

        public static void SetDirty()
        {
            isDirty = true;
        }

        public static void ResetDirty()
        {
            isDirty = false;
        }

        /// <summary>
        /// A part in the parts tree with its editable fields.
        /// </summary>
        public class PartTreeItem
        {
            public ScenPart Part { get; set; }
            public string Label { get; set; }
            public string Summary { get; set; }
            public List<PartField> Fields { get; set; } = new List<PartField>();
            public bool IsExpanded { get; set; }
            public int IndentLevel { get; set; }

            public bool IsListPart { get; set; }
            public List<ListItemData> ListItems { get; set; } = new List<ListItemData>();
        }

        /// <summary>
        /// An editable field within a part.
        /// </summary>
        public class PartField
        {
            public string Name { get; set; }
            public FieldType Type { get; set; }
            public string CurrentValue { get; set; }
            public object Data { get; set; } // For dropdown: list of options
            public Action<object> SetValue { get; set; }
            /// <summary>
            /// Optional read-back after SetValue; the stored value may differ from the
            /// requested one after validation or clamping.
            /// </summary>
            public Func<object> GetValue { get; set; }
            public Func<float> LiveSliderValue { get; set; }
            public bool SliderIsRangeMax { get; set; }
            /// <summary>
            /// Quantity fields: display as a percentage even when the max range exceeds 1.
            /// </summary>
            public bool IsPercentDisplay { get; set; }
            /// <summary>
            /// Quantity fields with float[] Data: display without decimal places.
            /// </summary>
            public bool IsIntegerDisplay { get; set; }
            /// <summary>
            /// Checkbox fields: the language-independent state driving the announcement and
            /// the toggle, so CurrentValue can be localized freely.
            /// </summary>
            public bool BoolValue { get; set; }
            /// <summary>
            /// Text fields: vanilla's own control is a multi-line <c>Widgets.TextArea</c>
            /// rather than a single-line entry, so the row opens a multi-line edit session.
            /// </summary>
            public bool MultiLine { get; set; }
        }

        /// <summary>
        /// <see cref="FieldType.ReadOnly"/> is for values this editor can present but not
        /// soundly edit — a field on a modded ScenPart with no vanilla-sourced bound or
        /// validator to cite. Navigable and announced like any other field, but never opens
        /// an editor.
        /// </summary>
        public enum FieldType { Dropdown, Quantity, Text, Checkbox, ReadOnly }

        public static void Open(Scenario scenario, Page_ScenarioEditor page)
        {
            currentScenario = scenario;
            currentPage = page;
            isDirty = false;

            BuildPartsTree();

            IsActive = true;

            // The opening announcement is ScenarioEditorScreenScope.OnFocus's job.
        }

        public static void Close()
        {
            IsActive = false;
            currentScenario = null;
            currentPage = null;
            partsHierarchy.Clear();

            // Child states must be closed explicitly or re-entry lands mid-flow. The save/load
            // pickers are real windows now; the WindowStack owns their lifecycle.
            if (ScenarioBuilderAddPartState.IsActive)
            {
                ScenarioBuilderAddPartState.Cancel();
            }
        }

        /// <summary>Rebuilds the parts tree from the current scenario over ALL parts, as the
        /// game's own editor does, carrying expand state by ScenPart reference and item index.</summary>
        public static void BuildPartsTree()
        {
            var expandedParts = new HashSet<ScenPart>();
            var expandedListItems = new Dictionary<ScenPart, HashSet<int>>();

            foreach (var existing in partsHierarchy)
            {
                if (existing.IsExpanded)
                    expandedParts.Add(existing.Part);

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

            foreach (ScenPart part in currentScenario.AllParts)
            {
                // ScenPart_PlanetLayer.DoEditInterface draws nothing while hide is set, so no row exists.
                if (part is ScenPart_PlanetLayer hideablePart && hideablePart.hide)
                    continue;

                var item = new PartTreeItem
                {
                    Part = part,
                    Label = part.Label,
                    Summary = GetPartSummary(part, currentScenario),
                    IsExpanded = expandedParts.Contains(part),
                    IndentLevel = 0
                };

                if (ScenPartListItemManager.IsListBasedPart(part))
                {
                    item.IsListPart = true;
                    item.ListItems = ScenPartListItemManager.ExtractListItems(part);

                    if (expandedListItems.TryGetValue(part, out var expandedIndices))
                    {
                        foreach (var listItem in item.ListItems)
                        {
                            if (expandedIndices.Contains(listItem.Index))
                                listItem.IsExpanded = true;
                        }
                    }

                    // List parts still carry plain fields of their own (pawnChoiceCount).
                    item.Fields = ExtractPartFields(part);
                }
                else
                {
                    item.Fields = ExtractPartFields(part);
                }

                partsHierarchy.Add(item);
            }
        }

        /// <summary>
        /// A summary of the part's current values. Prefers GetSummaryListEntries(), which is
        /// per-part, over Summary(), which aggregates across all parts.
        /// </summary>
        private static string GetPartSummary(ScenPart part, Scenario currentScenario)
        {
            try
            {
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
                            return string.Join(", ", entriesList);
                        }
                    }
                }

                // Fall back to Summary(), which is localized and already appends its own
                // duration text — never duplicate that here.
                part.summarized = false;
                string summary = part.Summary(currentScenario);

                if (!string.IsNullOrEmpty(summary))
                {
                    // SummaryWithList format: "\nIntro:\n   -Item1\n   -Item2".
                    if (summary.StartsWith("\n"))
                    {
                        var lines = summary.Split('\n')
                            .Where(l => l.TrimStart().StartsWith("-"))
                            .Select(l => l.TrimStart(' ', '-'))
                            .ToList();
                        if (lines.Count > 0)
                            return string.Join(", ", lines);
                    }

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
        /// A part's display name with its summary for context, e.g. "Scattered Randomly
        /// (packaged survival meal x 7)".
        /// </summary>
        internal static string GetPartDisplayName(PartTreeItem partItem)
        {
            string label = StripTrailingPunctuation(partItem.Label);
            string summaryText = StripTrailingPunctuation(partItem.Summary);
            if (!string.IsNullOrEmpty(summaryText))
            {
                return $"{label} ({summaryText})";
            }
            return label;
        }

        #region Actions (Hotkeys)

        public static bool IsDirty()
        {
            return isDirty;
        }

        #endregion

    }
}
