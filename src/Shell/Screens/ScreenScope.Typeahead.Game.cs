using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The shared typeahead engine. A screen that opts in (<see cref="EnableTypeahead"/>) gets match
    /// navigation over ONE ranked search space spanning the whole screen, ranked in this order: the
    /// current region's rows first, then everywhere else; a match on a row's own name above one that
    /// only hit its description; a row whose ENTIRE name is what the player typed wins outright;
    /// controls above list items above read-only text; then the finer tiers (a complete word of the
    /// name beating a partial one, fewer words beating more, shorter beating longer). Table regions
    /// also search their column headers and cell values — see <see cref="TypeaheadEntry"/>. The region key
    /// is the only one that depends on where the cursor sits — <see cref="TypeaheadMatcher"/> ranks
    /// everything else from the label alone, so the same query over the same rows always lands on the
    /// same row.
    ///
    /// Grammar:
    /// - Typing jumps the cursor to the best match; a cross-region jump is
    ///   located for the user ("Now at Travel supplies. Herbal medicine…").
    /// - Up/Down cycle matches (across regions); Home/End jump to the
    ///   first/last match.
    /// - Enter ACTIVATES the match: the search ends and the row runs exactly
    ///   as it would with no search live. Shift+Enter SETTLES instead —
    ///   the search ends and the row is re-read, nothing runs.
    /// - Escape clears the search (the scope owns cancel while one is
    ///   active); Backspace edits it.
    /// - Region switches and sorts clear the search — both invalidate the
    ///   match list.
    ///
    /// The engine consumes letters, digits, and — only while a search is already active — space, so
    /// multi-word labels stay reachable while an idle Space still falls through to chord dispatch
    /// (layout-aware <c>Event.character</c>, per the no-KeyCode-ranges rule; the shared rule is
    /// <see cref="TypeaheadMatcher.AcceptsSearchChar"/>). Everything else falls through to chord
    /// dispatch. Screens whose unmodified letter chords must keep working stay opted out.
    /// </summary>
    public abstract partial class ScreenScope : ICharSink
    {
        private readonly TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private readonly List<TypeaheadEntry> typeaheadEntries = new List<TypeaheadEntry>();
        private readonly List<string> typeaheadLabels = new List<string>();
        private readonly List<TypeaheadCandidate> typeaheadCandidates = new List<TypeaheadCandidate>();

        /// <summary>
        /// One search-space entry. Column &lt; 0: a plain row. Row &lt; 0: a column HEADER — its jump
        /// moves the column cursor only, the row stays put. Both &gt;= 0: a cell VALUE (Band 1,
        /// never outranking a header or row name) whose jump moves both axes.
        /// </summary>
        private struct TypeaheadEntry
        {
            public int Region;
            public int Row;
            public int Column;
            public int Band;
            public bool IsAction;
            public TypeaheadCandidateKind Kind;
            public string Identity;
        }

        private int lastTypeaheadJumpEntry = -1;

        /// <summary>Opt-in for the shared typeahead. Off by default: the char sink would eat a scope's unmodified-letter claims.</summary>
        protected virtual bool EnableTypeahead
        {
            get { return false; }
        }

        /// <summary>Whether a content region's rows join the search space, and whether typing while ON that region starts a search. Stat/summary regions of values rather than named items override false. The toolbar is always searchable.</summary>
        protected virtual bool ContentRegionSearchable(int region)
        {
            return true;
        }

        /// <summary>
        /// Whether ONE content row joins the search space; true for every row by default. A screen
        /// whose region mixes named items with deep read-only detail lines overrides false for the
        /// detail rows, keeping the match set at the level the user searches by rather than burying
        /// real targets under stat lines. An excluded row is still navigable and still spoken.
        /// </summary>
        protected virtual bool ContentRowSearchable(int region, int row)
        {
            return true;
        }

        /// <summary>The text ONE content row is matched against, defaulting to its spoken label so what the user hears is what typing matches. A screen whose label carries composed state words ("expanded, 3 items") overrides this with the bare identity text, keeping those words out of the haystack.</summary>
        protected virtual string ContentRowSearchText(int region, int row)
        {
            ElementDescription d = DescribeContentItem(region, row);
            return d == null ? "" : (d.Label ?? "");
        }

        /// <summary>A content row's candidate kind, from its described role by default. A screen whose rows are uniform and costly to describe overrides this with the constant answer.</summary>
        protected virtual TypeaheadCandidateKind ContentRowSearchKind(int region, int row)
        {
            ElementDescription d = DescribeContentItem(region, row);
            return d == null ? TypeaheadCandidateKind.Item : TypeaheadMatcher.ClassifyRow(d.Role, d.ReadOnly);
        }

        /// <summary>A content row's stable identity for ranking, when the screen has one that outlives what the row currently displays (<see cref="TypeaheadCandidate.Identity"/>). Null by default; it never widens or narrows the match set.</summary>
        protected virtual string ContentRowSearchIdentity(int region, int row)
        {
            return null;
        }

        /// <summary>Whether digits join the search. Off for screens whose unmodified digit keys are commands (the work tables' 0-4 priority keys), which must fall through to chord dispatch.</summary>
        protected virtual bool TypeaheadAcceptsDigits
        {
            get { return true; }
        }

        /// <summary>Called before a typed character or Backspace rebuilds the search space. Screens whose row set must be re-synced against live game state override this; most screens' RefreshContent already covers it.</summary>
        protected virtual void OnTypeaheadWillSearch()
        {
        }

        /// <summary>
        /// Whether mid-word matches join the match set, ranked below every word-prefix
        /// tier (<see cref="TypeaheadMatcher"/>'s substring fallback). False everywhere by
        /// default: word-prefix ranking is what makes a match set learnable, and admitting
        /// substrings everywhere would bury real targets.
        ///
        /// True belongs to screens whose ROW LIST is itself the product of a substring filter,
        /// where prefix tiers cannot reach every visible row: a dialog filtering its own drawn
        /// list with a <c>Contains</c> predicate, or a screen over a foreign QuickSearchWidget,
        /// answering from that widget's live state. Read per search, so a live answer is honored
        /// on the next keystroke.
        /// </summary>
        protected virtual bool TypeaheadSubstringFallback
        {
            get { return false; }
        }

        /// <summary>Keep matches in row order rather than ranking them, for chronological lists where cycling should walk forward in time.</summary>
        protected virtual bool TypeaheadDocumentOrder
        {
            get { return false; }
        }

        /// <summary>
        /// The current search text, for a screen that must publish it somewhere the
        /// player can SEE — a dialog whose own search box is the visual half of this
        /// search, so a sighted viewer's filtered list stays the list the keyboard is
        /// walking. Read-only: the buffer belongs to the engine.
        /// </summary>
        protected string TypeaheadSearchText
        {
            get { return typeahead.SearchBuffer ?? ""; }
        }

        /// <summary>True while a typeahead search is active (for subclass OwnsCancel folds and claim guards).</summary>
        protected bool TypeaheadHasActiveSearch
        {
            get { return EnableTypeahead && typeahead.HasActiveSearch; }
        }

        private bool TypeaheadClaimable()
        {
            return TypeaheadHasActiveSearch;
        }

        /// <summary>Clears the search without announcing (region switch, sort, activation, Shift+Enter settle).</summary>
        protected void TypeaheadReset()
        {
            typeahead.ClearSearch();
        }

        public override ICharSink CharSink
        {
            get { return EnableTypeahead ? this : base.CharSink; }
        }

        /// <summary>
        /// One typed character: extend the search and jump to the best match.
        /// Non-alphanumeric characters and typing on a non-searchable region
        /// fall through to chord dispatch.
        /// </summary>
        public virtual bool HandleChar(char c)
        {
            if (!EnableTypeahead)
                return false;
            if (!TypeaheadMatcher.AcceptsSearchChar(c, typeahead.HasActiveSearch, TypeaheadAcceptsDigits))
                return false;
            // Read per search, not once: a screen sitting over a foreign filter answers
            // from that filter's live state (see TypeaheadSubstringFallback).
            typeahead.SubstringFallback = TypeaheadSubstringFallback;
            typeahead.DocumentOrder = TypeaheadDocumentOrder;
            OnTypeaheadWillSearch();
            RefreshModel();
            // The automatic regions are always searchable: ContentRegionSearchable speaks only
            // for a screen's own content indices, and a switch-style override answering false to
            // everything else must not make typing dead in the captured-extras region.
            if (!InActionsRegion() && !InExtrasRegion() && !ContentRegionSearchable(Model.RegionIndex))
                return false;
            BuildTypeaheadEntries();
            if (typeaheadLabels.Count == 0)
                return false;
            if (typeahead.ProcessCharacterInput(c, typeaheadLabels, typeaheadCandidates, out int match))
            {
                JumpToTypeaheadEntry(match);
            }
            else
            {
                typeahead.SpeakNoMatches();
            }
            return true;
        }

        private void TypeaheadBackspace()
        {
            typeahead.SubstringFallback = TypeaheadSubstringFallback;
            typeahead.DocumentOrder = TypeaheadDocumentOrder;
            OnTypeaheadWillSearch();
            RefreshModel();
            BuildTypeaheadEntries();
            if (!typeahead.ProcessBackspace(typeaheadLabels, typeaheadCandidates, out int match))
                return;
            if (match >= 0)
            {
                JumpToTypeaheadEntry(match);
            }
            else
            {
                // Buffer emptied: the search is over, re-read the row plainly.
                AnnounceCurrent(CellAxis.Row);
            }
        }

        private void TypeaheadEscapeClear()
        {
            // Stamp the frame so window and page routers know Escape was consumed here.
            ShellFrameStamps.MarkCancelConsumed();
            typeahead.ClearSearchAndAnnounce();
        }

        /// <summary>
        /// Shift+Enter during a search: end it and re-read the row, running nothing. Also the
        /// Enter path for a <see cref="TryHandleAcceptChord"/> override that settles rather than
        /// acts. Stamps the accept frame: Shift+Return still matches vanilla's Accept binding,
        /// since modifiers are not part of vanilla's bindings.
        /// </summary>
        protected void TypeaheadSettle()
        {
            ShellFrameStamps.MarkAcceptConsumed();
            DisarmDefaultAccept();
            TypeaheadReset();
            AnnounceCurrentItem();
        }

        /// <summary>Up/Down during an active search: next/previous match, wrapping, crossing regions.</summary>
        private bool TypeaheadMatchMove(int delta)
        {
            if (!TypeaheadHasActiveSearch || typeahead.MatchCount == 0)
                return false;
            RefreshModel();
            int current = CurrentTypeaheadEntryIndex();
            int match = delta > 0 ? typeahead.GetNextMatch(current) : typeahead.GetPreviousMatch(current);
            if (match < 0 || match >= typeaheadEntries.Count)
                return false;
            MenuHelper.SoundMatchMove(current, match, delta);
            JumpToTypeaheadEntry(match);
            return true;
        }

        /// <summary>Home/End during an active search: first/last match.</summary>
        private bool TypeaheadMatchEdge(bool first)
        {
            if (!TypeaheadHasActiveSearch || typeahead.MatchCount == 0)
                return false;
            RefreshModel();
            int match = first ? typeahead.GetFirstMatch() : typeahead.GetLastMatch();
            if (match < 0 || match >= typeaheadEntries.Count)
                return false;
            JumpToTypeaheadEntry(match);
            return true;
        }

        /// <summary>
        /// The next/previous match within one content region, as that region's DATA ROW index, or
        /// -1 when no match lands there. For features that walk matches without moving the cursor.
        /// </summary>
        protected int TypeaheadMatchRowInRegion(int region, int currentRow, int delta)
        {
            if (!TypeaheadHasActiveSearch || typeahead.MatchCount == 0)
                return -1;
            int index = -1;
            for (int i = 0; i < typeaheadEntries.Count; i++)
            {
                if (!typeaheadEntries[i].IsAction && typeaheadEntries[i].Region == region
                    && typeaheadEntries[i].Column < 0 && typeaheadEntries[i].Row == currentRow)
                {
                    index = i;
                    break;
                }
            }
            for (int step = 0; step < typeahead.MatchCount; step++)
            {
                index = delta > 0 ? typeahead.GetNextMatch(index) : typeahead.GetPreviousMatch(index);
                if (index < 0 || index >= typeaheadEntries.Count)
                    return -1;
                if (!typeaheadEntries[index].IsAction && typeaheadEntries[index].Region == region
                    && typeaheadEntries[index].Row >= 0)
                    return typeaheadEntries[index].Row;
            }
            return -1;
        }

        /// <summary>
        /// The whole-screen search space, rebuilt per keystroke: the current region first (the
        /// helper's priority group), then every other region in model order. Labels are the rows'
        /// identity labels, so what the player hears is what typing matches.
        /// </summary>
        private void BuildTypeaheadEntries()
        {
            typeaheadEntries.Clear();
            typeaheadLabels.Clear();
            typeaheadCandidates.Clear();
            lastTypeaheadJumpEntry = -1;

            int current = Model.RegionIndex;
            ScreenRegionLayout layout = RegionLayout();

            AddTypeaheadRegion(current, layout);
            for (int r = 0; r < layout.TotalRegions; r++)
            {
                if (r != current)
                {
                    AddTypeaheadRegion(r, layout);
                }
            }

            for (int i = 0; i < typeaheadEntries.Count; i++)
                typeaheadCandidates.Add(new TypeaheadCandidate(
                    typeaheadEntries[i].Region == current, typeaheadEntries[i].Kind,
                    typeaheadEntries[i].Identity, typeaheadEntries[i].Band));
        }

        private void AddTypeaheadRegion(int region, ScreenRegionLayout layout)
        {
            switch (layout.KindOf(region))
            {
                case ScreenRegionKind.Actions:
                    AddTypeaheadActions(region);
                    return;
                case ScreenRegionKind.Extras:
                    AddTypeaheadExtras(region);
                    return;
                case ScreenRegionKind.Content:
                    AddTypeaheadContent(region);
                    return;
                default:
                    return;
            }
        }

        private void AddTypeaheadActions(int region)
        {
            int actions = capturedButtons.Count + DeclaredActionCount();
            for (int i = 0; i < actions; i++)
            {
                ElementDescription d = DescribeActionRow(i);
                typeaheadEntries.Add(new TypeaheadEntry { Region = region, Row = i, Column = -1, IsAction = true, Kind = TypeaheadCandidateKind.Control });
                typeaheadLabels.Add(d.Label ?? "");
            }
        }

        /// <summary>
        /// The captured-extras rows, matched on LABELS only: an extras tooltip can be a paragraph
        /// of vanilla hover prose, and folding that in would let stray words outrank real row
        /// names. IsAction stays false — these are rows, not toolbar buttons.
        /// </summary>
        private void AddTypeaheadExtras(int region)
        {
            for (int i = 0; i < extrasRows.Count; i++)
            {
                ExtrasRow row = extrasRows[i];
                typeaheadEntries.Add(new TypeaheadEntry
                {
                    Region = region,
                    Row = i,
                    Column = -1,
                    IsAction = false,
                    Kind = TypeaheadMatcher.ClassifyRow(row.Role, row.ReadOnly || row.Member == null),
                });
                typeaheadLabels.Add(row.Label ?? "");
            }
        }

        private void AddTypeaheadContent(int region)
        {
            if (!ContentRegionSearchable(region))
                return;
            int columns = ContentColumnCount(region);
            for (int c = 0; c < columns; c++)
            {
                TableColumnInfo info = ContentColumnInfo(region, c);
                if (info == null || string.IsNullOrEmpty(info.Label))
                    continue;
                typeaheadEntries.Add(new TypeaheadEntry
                {
                    Region = region,
                    Row = -1,
                    Column = c,
                    IsAction = false,
                    Kind = TypeaheadCandidateKind.Control,
                });
                typeaheadLabels.Add(info.Label);
            }
            int count = ContentItemCount(region);
            for (int i = 0; i < count; i++)
            {
                if (!ContentRowSearchable(region, i))
                    continue;
                typeaheadEntries.Add(new TypeaheadEntry
                {
                    Region = region,
                    Row = i,
                    Column = -1,
                    IsAction = false,
                    Kind = ContentRowSearchKind(region, i),
                    Identity = ContentRowSearchIdentity(region, i),
                });
                typeaheadLabels.Add(ContentRowSearchText(region, i) ?? "");
            }
            if (columns > 0)
                AddTypeaheadCells(region, columns, count);
        }

        /// <summary>A cell repeating its own row's search text is skipped: the row entry covers it.</summary>
        private void AddTypeaheadCells(int region, int columns, int rows)
        {
            for (int i = 0; i < rows; i++)
            {
                if (!ContentRowSearchable(region, i))
                    continue;
                string rowText = ContentRowSearchText(region, i);
                for (int c = 0; c < columns; c++)
                {
                    string value = ContentCellText(region, i, c);
                    if (string.IsNullOrEmpty(value)
                        || string.Equals(value, rowText, System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    typeaheadEntries.Add(new TypeaheadEntry
                    {
                        Region = region,
                        Row = i,
                        Column = c,
                        Band = 1,
                        IsAction = false,
                        Kind = TypeaheadCandidateKind.Text,
                    });
                    typeaheadLabels.Add(value);
                }
            }
        }

        /// <summary>
        /// The search-space index of the row the cursor is on, or -1 to let the helper pick.
        /// </summary>
        private int CurrentTypeaheadEntryIndex()
        {
            int region = Model.RegionIndex;
            ListModel list = Model.CurrentRegion;
            if (list == null)
                return -1;
            TableModel table = Model.CurrentTable;
            int row = table != null ? list.Index - 1 : list.Index;
            int column = table != null ? table.ColumnIndex : -1;
            if (lastTypeaheadJumpEntry >= 0 && lastTypeaheadJumpEntry < typeaheadEntries.Count
                && EntryMatchesCursor(typeaheadEntries[lastTypeaheadJumpEntry], region, row, column))
                return lastTypeaheadJumpEntry;
            for (int i = 0; i < typeaheadEntries.Count; i++)
            {
                if (typeaheadEntries[i].Region == region && typeaheadEntries[i].Column < 0
                    && typeaheadEntries[i].Row == row)
                    return i;
            }
            return -1;
        }

        private static bool EntryMatchesCursor(TypeaheadEntry entry, int region, int row, int column)
        {
            if (entry.Region != region)
                return false;
            if (entry.Column >= 0 && entry.Column != column)
                return false;
            if (entry.Row >= 0 && entry.Row != row)
                return false;
            return true;
        }

        /// <summary>
        /// Move the cursor to a match. A cross-region jump fires <see cref="OnRegionChanged"/> so
        /// screens mirroring the region into the game stay in sync, and the announcement leads
        /// with the destination ("Now at {region}").
        /// </summary>
        private void JumpToTypeaheadEntry(int entryIndex)
        {
            TypeaheadEntry entry = typeaheadEntries[entryIndex];
            bool crossed = entry.Region != Model.RegionIndex;
            if (crossed)
            {
                MoveResult result = Model.MoveToRegion(entry.Region);
                if (result.Changed)
                {
                    OnRegionChanged(result);
                }
            }
            ListModel list = Model.CurrentRegion;
            if (list == null)
                return;
            TableModel table = Model.CurrentTable;
            CellAxis axis = CellAxis.Row;
            if (entry.Column >= 0 && table != null && entry.Column < table.ColumnCount)
            {
                if (entry.Row >= 0)
                {
                    list.MoveTo(entry.Row + 1);
                    axis = CellAxis.Entry;
                }
                else
                {
                    axis = CellAxis.Column;
                }
                table.MoveToColumn(entry.Column);
            }
            else
            {
                list.MoveTo(table != null ? entry.Row + 1 : entry.Row);
            }
            lastTypeaheadJumpEntry = entryIndex;
            NotifyCursorSettled();
            AnnounceTypeaheadLanding(crossed, axis);
        }

        private void AnnounceTypeaheadLanding(bool crossed, CellAxis axis)
        {
            ListModel list = Model.CurrentRegion;
            string body;
            if (Model.CurrentTable != null && !InActionsRegion())
            {
                body = ComposeCurrentCellText(axis);
            }
            else
            {
                // Walking matches is a search, not a walk through the hierarchy: consecutive
                // results sit at unrelated depths, so a depth fragment on each one is noise
                // rather than orientation. The row's depth is stated when the player settles
                // on it (Enter or Shift+Enter, both of which re-announce through the gated
                // path), which is the moment it starts describing where they are.
                ElementDescription described = DescribeCurrent(list);
                described.Level = null;
                body = AnnouncementComposer.ComposeFocus(
                    described, TranslatedShellVocabulary.Instance,
                    TextDialogShared.StandardComposeOptions());
            }
            string announcement = (body ?? "") + typeahead.BuildSearchContextSuffix();
            if (crossed)
            {
                announcement = "RimWorldAccess.Shell.Search.NowAt"
                    .Translate(TypeaheadRegionName(Model.RegionIndex)) + ". " + announcement;
            }
            TolkHelper.SpeakData(announcement);
        }

        /// <summary>The jump announcement's region name; unnamed regions on generic screens locate as "tab {n}" (1-based).</summary>
        private string TypeaheadRegionName(int region)
        {
            string name = RegionNameFor(region);
            if (!string.IsNullOrEmpty(name))
                return name;
            return "RimWorldAccess.Shell.Search.UnnamedTab".Translate(region + 1).ToString();
        }
    }
}
