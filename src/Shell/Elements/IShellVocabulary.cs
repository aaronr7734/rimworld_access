namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The state words the composer can ask for. One word per key in the
    /// Shell.State.* translation table — never re-worded per screen.
    /// </summary>
    public enum ElementStateWord
    {
        Checked,
        Unchecked,
        PartiallyChecked,
        Selected,
        NotSelected,
        Expanded,
        Collapsed,
        Blank,
        Disabled,
        ReadOnly,
        AtMinimum,
        AtMaximum,
        ColumnHeader,
        Sortable,
        SortedAscending,
        SortedDescending,
        SearchMatch,
    }

    /// <summary>
    /// Resolves role/state vocabulary to spoken words. The game implementation
    /// (TranslatedShellVocabulary, *.Game.cs) reads the Shell.Role.*/
    /// Shell.State.* Keyed XML; tests supply a fixed-word fake. Keeping the
    /// words behind this seam is what lets the composer stay PURE.
    /// </summary>
    public interface IShellVocabulary
    {
        /// <summary>The spoken role word; empty string = this role is silent (MenuItem, TreeItem, TableCell, None).</summary>
        string RoleWord(ElementRole role);

        string Word(ElementStateWord word);

        /// <summary>"3 of 7" position phrase.</summary>
        string Position(int index, int count);

        /// <summary>"tab 1 of 2" phrase; the bare ordinal is ambiguous beside an item's own.</summary>
        string TabPosition(int index, int count);

        /// <summary>"level 2" tree-depth phrase.</summary>
        string Level(int level);

        /// <summary>"row 3 of 7" table phrase (the header row is row 1).</summary>
        string RowPosition(int index, int count);

        /// <summary>"column 2 of 8" table phrase.</summary>
        string ColumnPosition(int index, int count);

        /// <summary>"table, 5 columns, 7 rows" table-entry phrase (row count includes the header row).</summary>
        string TableDimensions(int columns, int rows);

        /// <summary>"5 tabs" screen-entry phrase.</summary>
        string TabCount(int count);
    }
}
