using System;
using System.Collections.Generic;

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
        public string Description { get; set; }
        public int IndentLevel { get; set; }
        public bool IsExpandable { get; set; }
        public bool IsExpanded { get; set; }
        public List<InspectionTreeItem> Children { get; set; }
        public InspectionTreeItem Parent { get; set; }  // Reference to parent item for upward navigation
        public object Data { get; set; }  // Associated data (Pawn, Building, SkillRecord, etc.)
        public Action OnActivate { get; set; }  // Action to execute when Enter is pressed
        public Action OnDelete { get; set; }  // Action to execute when Delete is pressed (for canceling jobs, etc.)

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
