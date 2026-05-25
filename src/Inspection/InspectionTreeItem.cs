using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Represents an item in the inspection tree that can be expanded/collapsed.
    /// </summary>
    public class InspectionTreeItem
    {
        public enum ItemType
        {
            Object,           // A thing/pawn/building at the cursor
            Category,         // A category like "Health", "Gear", "Overview"
            SubCategory,      // A sub-category like "Equipment", "Apparel"
            Item,             // An item in a list (gear item, skill)
            Action,           // An actionable item (Drop, Consume, etc.)
            DetailText        // Read-only detail text
        }

        public ItemType Type { get; set; }
        public string Label { get; set; }
        /// <summary>
        /// Short label shown when node is expanded (e.g., just the title).
        /// When set, Label contains the full summary (shown when collapsed)
        /// and ExpandedLabel contains the short form (shown when expanded).
        /// State change announcements (expand/collapse) always use this short form.
        /// If null, Label is used in all contexts (standard behavior).
        /// </summary>
        public string ExpandedLabel { get; set; }
        public string Description { get; set; }
        public string Tooltip { get; set; }
        public int IndentLevel { get; set; }
        public bool IsExpandable { get; set; }
        public bool IsExpanded { get; set; }
        /// <summary>
        /// Marks a node as a section heading within a parent's detail lines (e.g. a role's
        /// "Abilities" / "Requirements" headers). Used by TreeNavigationHelper's Page Up/Down
        /// to jump between sections. Default false.
        /// </summary>
        public bool IsSectionHeader { get; set; }
        public List<InspectionTreeItem> Children { get; set; }
        public InspectionTreeItem Parent { get; set; }  // Reference to parent item for upward navigation
        public object Data { get; set; }  // Associated data (Pawn, Building, SkillRecord, etc.)
        public Def LinkedDef { get; set; }  // Associated Def for Alt+I info card navigation
        public Action OnActivate { get; set; }  // Action to execute when Enter is pressed
        public Action OnDelete { get; set; }  // Action to execute when Delete is pressed (for canceling jobs, etc.)
        public Action OnInfo { get; set; }  // Action for Alt+I (custom info display, e.g. stat breakdown)

        public InspectionTreeItem()
        {
            Children = new List<InspectionTreeItem>();
            IsExpandable = false;
            IsExpanded = false;
            IndentLevel = 0;
        }

        /// <summary>
        /// Gets a flattened list of all visible items (respecting expansion state).
        /// </summary>
        public List<InspectionTreeItem> GetVisibleItems()
        {
            var result = new List<InspectionTreeItem>();
            result.Add(this);

            if (IsExpanded && Children.Count > 0)
            {
                foreach (var child in Children)
                {
                    result.AddRange(child.GetVisibleItems());
                }
            }

            return result;
        }
    }
}
