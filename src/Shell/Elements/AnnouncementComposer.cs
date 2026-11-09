using System.Collections.Generic;
using System.Text;

namespace RimWorldAccess.Shell
{
    /// <summary>Per-call verbosity switches, sourced from mod settings on the game side.</summary>
    public struct ComposeOptions
    {
        /// <summary>Speak "x of y" position fragments (the AnnouncePosition setting).</summary>
        public bool IncludePosition;

        /// <summary>Speak beginner interaction hints (the global interaction-hints setting).</summary>
        public bool IncludeHints;

        /// <summary>Speak "row x of y" / "column x of y" in tables (the AnnounceRowColumnPosition setting).</summary>
        public bool IncludeRowColumnPosition;

        /// <summary>Speak "level N" tree-depth fragments (the AnnounceLevels setting).</summary>
        public bool IncludeLevels;

        /// <summary>
        /// Suppresses the Label/Hotkey/Role/State/Extras fragments respectively. Opt-OUT by
        /// design: many call sites build a bare <c>default(ComposeOptions)</c>, and an opt-IN
        /// flag would mute their labels, hotkeys, and role/state.
        /// </summary>
        public bool SuppressLabel;
        public bool SuppressHotkey;
        public bool SuppressRole;
        public bool SuppressState;
        public bool SuppressExtras;

        /// <summary>
        /// Player-chosen fragment order; null means <see cref="AnnouncementFormat.DefaultOrder"/>.
        /// </summary>
        public IReadOnlyList<AnnouncementPart> PartOrder;

        public static ComposeOptions AllOn
        {
            get
            {
                ComposeOptions o;
                o.IncludePosition = true;
                o.IncludeHints = true;
                o.IncludeRowColumnPosition = true;
                o.IncludeLevels = true;
                o.SuppressLabel = false;
                o.SuppressHotkey = false;
                o.SuppressRole = false;
                o.SuppressState = false;
                o.SuppressExtras = false;
                o.PartOrder = null;
                return o;
            }
        }
    }

    /// <summary>
    /// Renders an ElementDescription into speech. The one place announcement grammar lives:
    /// default order Level → Label → Hotkey → Role → State → Extras → Hint → Position, hotkey
    /// right after the label so users can act immediately, verbose material last so they can
    /// stop listening. Fragments join with periods, the words within a state fragment with a
    /// comma; never newlines (screen readers pause too long on them).
    /// PURE: words come from IShellVocabulary; no game APIs.
    /// </summary>
    public static class AnnouncementComposer
    {
        /// <summary>
        /// Full focus announcement, in <paramref name="options"/>'s effective fragment order.
        /// If every part is suppressed but the item has a label, the label alone is spoken —
        /// verbosity settings must never lose an item entirely.
        /// </summary>
        public static string ComposeFocus(ElementDescription d, IShellVocabulary v, ComposeOptions options)
        {
            var fragments = new List<string>();
            IReadOnlyList<AnnouncementPart> order = options.PartOrder ?? AnnouncementFormat.DefaultOrder;

            for (int i = 0; i < order.Count; i++)
            {
                switch (order[i])
                {
                    case AnnouncementPart.Label:
                        if (!options.SuppressLabel && !string.IsNullOrEmpty(d.Label))
                            fragments.Add(d.Label);
                        break;
                    case AnnouncementPart.Hotkey:
                        if (!options.SuppressHotkey && !string.IsNullOrEmpty(d.Hotkey))
                            fragments.Add(d.Hotkey);
                        break;
                    case AnnouncementPart.Role:
                        if (!options.SuppressRole)
                        {
                            string role = v.RoleWord(d.Role);
                            if (!string.IsNullOrEmpty(role))
                                fragments.Add(role);
                        }
                        break;
                    case AnnouncementPart.State:
                        if (!options.SuppressState)
                        {
                            string state = ComposeState(d, v);
                            if (state.Length > 0)
                                fragments.Add(state);
                        }
                        break;
                    case AnnouncementPart.Level:
                        // Depth 1 is the baseline every hierarchy starts at; "level one" carries no information.
                        if (options.IncludeLevels && d.Level.HasValue && d.Level.Value > 1)
                            fragments.Add(v.Level(d.Level.Value));
                        break;
                    case AnnouncementPart.Position:
                        // A tab's ordinal names the strip it belongs to: bare, it lands next to
                        // the focused item's own ordinal and neither one can be told apart.
                        if (options.IncludePosition && d.PositionIndex.HasValue && d.PositionCount.HasValue)
                            fragments.Add(d.Role == ElementRole.Tab
                                ? v.TabPosition(d.PositionIndex.Value, d.PositionCount.Value)
                                : v.Position(d.PositionIndex.Value, d.PositionCount.Value));
                        break;
                    case AnnouncementPart.Extras:
                        if (!options.SuppressExtras && !string.IsNullOrEmpty(d.Extras))
                            fragments.Add(d.Extras);
                        break;
                    case AnnouncementPart.Hint:
                        if (options.IncludeHints && !string.IsNullOrEmpty(d.Hint))
                            fragments.Add(d.Hint);
                        break;
                }
            }

            string composed = Join(fragments);
            if (composed.Length == 0 && !string.IsNullOrEmpty(d.Label))
                return d.Label;
            return composed;
        }

        /// <summary>
        /// Changed-state-only announcement for toggles and value edits ("checked", "40
        /// percent"), never the full label — the user already knows where they are.
        /// </summary>
        public static string ComposeStateChange(ElementDescription d, IShellVocabulary v)
        {
            var stateWords = new List<string>();
            AppendStateWords(d, v, stateWords);
            return string.Join(", ", stateWords.ToArray());
        }

        /// <summary>
        /// Table-cell announcement under the delta doctrine: speak only the changed axis. A row
        /// move reads the row identity and the current column's value, a column move the column
        /// name, value and tooltip, entry both axes;
        /// header cells speak the column name, "column header", and the sort state. Position
        /// fragments come last, after the tooltips. This IS the mod-wide table grammar — screens
        /// never hand-build cell strings.
        /// </summary>
        public static string ComposeCell(ElementDescription d, IShellVocabulary v, ComposeOptions options)
        {
            if (d.IsHeaderCell)
                return ComposeHeaderCell(d, v, options);

            var fragments = new List<string>();
            bool columnContext = d.Axis != CellAxis.Row;
            bool rowContext = d.Axis != CellAxis.Column;

            // The identity column's value starts with the row label; speaking both repeats the
            // name ("Rosario: Rosario"). Keep the value — it carries the label plus any suffix.
            bool valueCarriesLabel = !string.IsNullOrEmpty(d.Label)
                && !string.IsNullOrEmpty(d.Value)
                && d.Value.StartsWith(d.Label);
            if (rowContext && !string.IsNullOrEmpty(d.Label) && !valueCarriesLabel)
                fragments.Add(d.Label);
            if (columnContext && !string.IsNullOrEmpty(d.ColumnName))
                fragments.Add(d.ColumnName);

            var words = new List<string>();
            // A cell that IS a control (a per-row button column) says so; RoleWord is empty
            // for the plain TableCell role.
            string cellRoleWord = v.RoleWord(d.Role);
            if (!string.IsNullOrEmpty(cellRoleWord))
                words.Add(cellRoleWord);
            AppendStateWords(d, v, words);
            if (d.Disabled)
                words.Add(v.Word(ElementStateWord.Disabled));
            // "Read only" is a control state; on a roleless label, heading or bar it would
            // mislead, since those were never adjustable.
            if (d.ReadOnly && d.Role != ElementRole.None)
                words.Add(v.Word(ElementStateWord.ReadOnly));
            if (words.Count > 0)
                fragments.Add(string.Join(", ", words.ToArray()));

            if (columnContext && ColumnTooltipAddsSomething(d))
                fragments.Add(d.ColumnTooltip);
            if (!string.IsNullOrEmpty(d.Extras))
                fragments.Add(d.Extras);
            AppendCellPositions(d, v, options, fragments, rowContext, columnContext);
            AppendHint(d, options, fragments);
            return Join(fragments);
        }

        /// <summary>A column tooltip that only repeats the column's name or the cell's own tooltip is dropped.</summary>
        private static bool ColumnTooltipAddsSomething(ElementDescription d)
        {
            string tip = d.ColumnTooltip?.Trim();
            if (string.IsNullOrEmpty(tip))
                return false;
            if (string.Equals(tip, d.ColumnName?.Trim(), System.StringComparison.OrdinalIgnoreCase))
                return false;
            return d.Extras == null || d.Extras.IndexOf(tip, System.StringComparison.Ordinal) < 0;
        }

        /// <summary>Header cell: "{column}. column header, sorted descending. {tip}. column 2 of 8".</summary>
        private static string ComposeHeaderCell(ElementDescription d, IShellVocabulary v, ComposeOptions options)
        {
            var fragments = new List<string>();
            if (!string.IsNullOrEmpty(d.ColumnName))
                fragments.Add(d.ColumnName);

            var words = new List<string> { v.Word(ElementStateWord.ColumnHeader) };
            if (d.SortDescending.HasValue)
            {
                words.Add(v.Word(d.SortDescending.Value
                    ? ElementStateWord.SortedDescending
                    : ElementStateWord.SortedAscending));
            }
            else if (d.Sortable)
            {
                words.Add(v.Word(ElementStateWord.Sortable));
            }
            fragments.Add(string.Join(", ", words.ToArray()));

            bool columnContext = d.Axis != CellAxis.Row;
            if (columnContext && ColumnTooltipAddsSomething(d))
                fragments.Add(d.ColumnTooltip);
            if (!string.IsNullOrEmpty(d.Extras))
                fragments.Add(d.Extras);
            AppendCellPositions(d, v, options, fragments, rowContext: d.Axis != CellAxis.Column, columnContext: columnContext);
            AppendHint(d, options, fragments);
            return Join(fragments);
        }

        private static void AppendCellPositions(ElementDescription d, IShellVocabulary v, ComposeOptions options,
            List<string> fragments, bool rowContext, bool columnContext)
        {
            if (!options.IncludePosition || !options.IncludeRowColumnPosition)
                return;
            if (rowContext && d.RowIndex.HasValue && d.RowCount.HasValue)
                fragments.Add(v.RowPosition(d.RowIndex.Value, d.RowCount.Value));
            if (columnContext && d.ColumnIndex.HasValue && d.ColumnCount.HasValue)
                fragments.Add(v.ColumnPosition(d.ColumnIndex.Value, d.ColumnCount.Value));
        }

        private static void AppendHint(ElementDescription d, ComposeOptions options, List<string> fragments)
        {
            if (options.IncludeHints && !string.IsNullOrEmpty(d.Hint))
                fragments.Add(d.Hint);
        }

        /// <summary>The state words ("checked", "40 percent", "disabled", "read only"), comma-joined; may be empty.</summary>
        private static string ComposeState(ElementDescription d, IShellVocabulary v)
        {
            var words = new List<string>();

            AppendStateWords(d, v, words);

            if (d.Disabled)
                words.Add(v.Word(ElementStateWord.Disabled));
            // Read-only is a control state; never spoken for a roleless element.
            if (d.ReadOnly && d.Role != ElementRole.None)
                words.Add(v.Word(ElementStateWord.ReadOnly));

            return string.Join(", ", words.ToArray());
        }

        private static void AppendStateWords(ElementDescription d, IShellVocabulary v, List<string> words)
        {
            if (d.Check.HasValue)
            {
                switch (d.Check.Value)
                {
                    case CheckState.Checked:
                        words.Add(v.Word(ElementStateWord.Checked));
                        break;
                    case CheckState.PartiallyChecked:
                        words.Add(v.Word(ElementStateWord.PartiallyChecked));
                        break;
                    default:
                        words.Add(v.Word(ElementStateWord.Unchecked));
                        break;
                }
            }

            if (d.Selected.HasValue)
                words.Add(v.Word(d.Selected.Value ? ElementStateWord.Selected : ElementStateWord.NotSelected));

            if (d.Expanded.HasValue)
                words.Add(v.Word(d.Expanded.Value ? ElementStateWord.Expanded : ElementStateWord.Collapsed));

            if (d.ValueBlank)
                words.Add(v.Word(ElementStateWord.Blank));
            else if (!string.IsNullOrEmpty(d.Value))
                words.Add(d.Value);

            if (d.AtMinimum)
                words.Add(v.Word(ElementStateWord.AtMinimum));
            if (d.AtMaximum)
                words.Add(v.Word(ElementStateWord.AtMaximum));
            if (d.SearchMatch)
                words.Add(v.Word(ElementStateWord.SearchMatch));
        }

        /// <summary>Joins fragments with ". ", skipping the period when one already ends in sentence punctuation.</summary>
        private static string Join(List<string> fragments)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < fragments.Count; i++)
            {
                string fragment = fragments[i].Trim();
                if (fragment.Length == 0)
                    continue;
                if (sb.Length > 0)
                {
                    char last = sb[sb.Length - 1];
                    if (last != '.' && last != '!' && last != '?' && last != ':')
                        sb.Append('.');
                    sb.Append(' ');
                }
                sb.Append(fragment);
            }
            return sb.ToString();
        }
    }
}
