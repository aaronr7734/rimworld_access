using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>Checkbox state including the indeterminate third state (vanilla multi-checkboxes).</summary>
    public enum CheckState
    {
        Unchecked,
        Checked,
        PartiallyChecked,
    }

    /// <summary>
    /// Which cursor axis a table-cell announcement reacts to: speak only the changed axis.
    /// Entry (table entered, sort applied, typeahead jump) resets the context and speaks both.
    /// </summary>
    public enum CellAxis
    {
        Entry,
        Row,
        Column,
    }

    /// <summary>
    /// Everything the composer needs to speak one focusable item, rendered in the fragment
    /// order Level → Label → Hotkey → Role → State → Extras → Hint → Position. All texts arrive
    /// already resolved and localized; this type is pure and never touches translation APIs.
    /// </summary>
    public sealed class ElementDescription
    {
        public string Label;

        /// <summary>Display-form shortcut ("Alt+M").</summary>
        public string Hotkey;

        public ElementRole Role = ElementRole.None;

        /// <summary>Only read for Checkbox.</summary>
        public CheckState? Check;

        /// <summary>Only read for RadioButton and Tab. Null = don't speak it.</summary>
        public bool? Selected;

        /// <summary>ComboBox/Slider/Stepper/TextField value, already formatted for speech.</summary>
        public string Value;

        /// <summary>TextField with no content — speaks the "blank" state word instead of Value.</summary>
        public bool ValueBlank;

        /// <summary>TreeItem branch state. Null = leaf (nothing spoken).</summary>
        public bool? Expanded;

        /// <summary>TreeItem indent level, 1-based. Null = don't speak it.</summary>
        public int? Level;

        public bool AtMinimum;

        public bool AtMaximum;

        public bool SearchMatch;

        public bool Disabled;

        /// <summary>
        /// Data the game shows but never lets the user edit or focus. Spoken as "read only" so
        /// the row stays navigable; the game's focus rules never gate presentation.
        /// </summary>
        public bool ReadOnly;

        /// <summary>
        /// Row whose Enter begins an edit/adjust session (numeric-entry sliders and steppers),
        /// so it owns Enter and is NOT inert for the double-press proceed seam
        /// (<see cref="DefaultAcceptInertness"/>). A plain slider owns Left/Right only.
        /// </summary>
        public bool EntersEditOnAccept;

        /// <summary>
        /// Row whose Enter has behavior no other channel expresses (activation branch, OnActivate
        /// hook, tree expand), so it owns Enter and is NOT inert for the double-press proceed seam
        /// (<see cref="DefaultAcceptInertness"/>) regardless of Role or ReadOnly. Never spoken.
        /// </summary>
        public bool KeepsAccept;

        /// <summary>1-based position within the container; spoken as "x of y" when both are set.</summary>
        public int? PositionIndex;
        public int? PositionCount;

        // Table-cell fields, read by ComposeCell only. All 1-based; the header row is row 1.

        public CellAxis Axis = CellAxis.Entry;

        /// <summary>Localized column header of the current cell's column.</summary>
        public string ColumnName;

        /// <summary>Localized column tooltip; spoken only when the column context changed.</summary>
        public string ColumnTooltip;

        /// <summary>1-based row position; spoken as "row x of y" when both set (verbosity-gated).</summary>
        public int? RowIndex;
        public int? RowCount;

        /// <summary>1-based column position; spoken as "column x of y" when both set (verbosity-gated).</summary>
        public int? ColumnIndex;
        public int? ColumnCount;

        /// <summary>Row 1 of every table: the cell IS the column header (sort lives here).</summary>
        public bool IsHeaderCell;

        /// <summary>Header cells: the column supports sorting ("sortable" is spoken when unsorted).</summary>
        public bool Sortable;

        /// <summary>Header cells: non-null when sorted by this column; the value is the direction.</summary>
        public bool? SortDescending;

        /// <summary>Verbose tail (description, tooltip). Spoken last.</summary>
        public string Extras;

        /// <summary>
        /// Interaction hint for genuinely non-standard interactions only (the role implies its
        /// own keys); gated by the global "interaction hints" option.
        /// </summary>
        public string Hint;
    }
}
