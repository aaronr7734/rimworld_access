using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The live IShellVocabulary: role/state words from the Shell.Role.* and
    /// Shell.State.* Keyed tables (Languages/*/Keyed/RimWorldAccess_Shell.xml),
    /// position and level phrases reusing the established Menu.Position /
    /// Menu.LevelSuffix keys so the shell speaks exactly the wording players
    /// already know.
    ///
    /// Game-coupled (*.Game.cs — excluded from the test project); tests use a
    /// fixed-word fake instead.
    /// </summary>
    public sealed class TranslatedShellVocabulary : IShellVocabulary
    {
        public static readonly TranslatedShellVocabulary Instance = new TranslatedShellVocabulary();

        private TranslatedShellVocabulary()
        {
        }

        public string RoleWord(ElementRole role)
        {
            switch (role)
            {
                case ElementRole.Button:
                    return "RimWorldAccess.Shell.Role.Button".Translate();
                case ElementRole.Checkbox:
                    return "RimWorldAccess.Shell.Role.Checkbox".Translate();
                case ElementRole.RadioButton:
                    return "RimWorldAccess.Shell.Role.RadioButton".Translate();
                case ElementRole.ComboBox:
                    return "RimWorldAccess.Shell.Role.ComboBox".Translate();
                case ElementRole.Slider:
                    return "RimWorldAccess.Shell.Role.Slider".Translate();
                case ElementRole.Stepper:
                    return "RimWorldAccess.Shell.Role.Stepper".Translate();
                case ElementRole.TextField:
                    return "RimWorldAccess.Shell.Role.TextField".Translate();
                case ElementRole.Tab:
                    return "RimWorldAccess.Shell.Role.Tab".Translate();
                case ElementRole.Map:
                    return "RimWorldAccess.Shell.Role.Map".Translate();
                default:
                    // None, MenuItem, TreeItem, TableCell: deliberately silent —
                    // their container or state words carry the context.
                    return string.Empty;
            }
        }

        public string Word(ElementStateWord word)
        {
            switch (word)
            {
                case ElementStateWord.Checked:
                    return "RimWorldAccess.Shell.State.Checked".Translate();
                case ElementStateWord.Unchecked:
                    return "RimWorldAccess.Shell.State.Unchecked".Translate();
                case ElementStateWord.PartiallyChecked:
                    return "RimWorldAccess.Shell.State.PartiallyChecked".Translate();
                case ElementStateWord.Selected:
                    return "RimWorldAccess.Shell.State.Selected".Translate();
                case ElementStateWord.NotSelected:
                    return "RimWorldAccess.Shell.State.NotSelected".Translate();
                case ElementStateWord.Expanded:
                    return "RimWorldAccess.Shell.State.Expanded".Translate();
                case ElementStateWord.Collapsed:
                    return "RimWorldAccess.Shell.State.Collapsed".Translate();
                case ElementStateWord.Blank:
                    return "RimWorldAccess.Shell.State.Blank".Translate();
                case ElementStateWord.Disabled:
                    return "RimWorldAccess.Shell.State.Disabled".Translate();
                case ElementStateWord.ReadOnly:
                    return "RimWorldAccess.Shell.State.ReadOnly".Translate();
                case ElementStateWord.AtMinimum:
                    return "RimWorldAccess.Shell.State.AtMinimum".Translate();
                case ElementStateWord.AtMaximum:
                    return "RimWorldAccess.Shell.State.AtMaximum".Translate();
                case ElementStateWord.ColumnHeader:
                    return "RimWorldAccess.Shell.State.ColumnHeader".Translate();
                case ElementStateWord.Sortable:
                    return "RimWorldAccess.Shell.State.Sortable".Translate();
                case ElementStateWord.SortedAscending:
                    return "RimWorldAccess.Shell.State.SortedAscending".Translate();
                case ElementStateWord.SortedDescending:
                    return "RimWorldAccess.Shell.State.SortedDescending".Translate();
                case ElementStateWord.SearchMatch:
                    return "RimWorldAccess.Shell.State.SearchMatch".Translate();
                default:
                    return string.Empty;
            }
        }

        public string Position(int index, int count)
        {
            return "RimWorldAccess.Menu.Position".Translate(index, count);
        }

        public string TabPosition(int index, int count)
        {
            return "RimWorldAccess.Menu.TabPosition".Translate(index, count);
        }

        public string Level(int level)
        {
            // LevelSuffix ships with a leading space for suffix appends; as a
            // standalone fragment the composer supplies its own separators.
            return "RimWorldAccess.Menu.LevelSuffix".Translate(level).ToString().Trim();
        }

        public string RowPosition(int index, int count)
        {
            return "RimWorldAccess.Shell.Table.RowPosition".Translate(index, count);
        }

        public string ColumnPosition(int index, int count)
        {
            return "RimWorldAccess.Shell.Table.ColumnPosition".Translate(index, count);
        }

        public string TableDimensions(int columns, int rows)
        {
            return "RimWorldAccess.Shell.Table.Dimensions".Translate(columns, rows);
        }

        public string TabCount(int count)
        {
            return "RimWorldAccess.Shell.Screen.TabCount".Translate(count);
        }
    }
}
