using System.Collections;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for ExpandMemory's memory main tab,
    /// <c>RimTalk.Memory.UI.TabWindow.MemoryTabWindowBridge</c>. The tab is four elements over one
    /// shared <c>MemoryTabWindow.Context</c>: a header of plain buttons, a chronicle strip of the
    /// archive layer on a zoomable time axis, a virtualized timeline of the other three layers,
    /// and a details pane for the focused memory, with a selection bar under them while anything
    /// is selected.
    ///
    /// HAND-MODELED, because the draw cannot carry them:
    /// <list type="bullet">
    /// <item>Timeline (0): the context's own <c>TreatedTimelineMemories</c>, in its own order,
    /// already filtered by the header's layer and type pickers. The timeline only draws the
    /// cards scrolled into view, so the captured pass never holds the whole list.</item>
    /// <item>Chronicle (1): the archive layer, oldest first, as the strip lays it out. Its cards
    /// are placed by tick on a zoomable axis and shrink to unlabeled slivers when zoomed out, so
    /// the list is built from the comp rather than the draw.</item>
    /// <item>Details (2): the focused memory. The pane draws its content with a raw
    /// <c>GUI.Label</c>, which no capture sees, so its lines are rebuilt from the memory in the
    /// pane's own order.</item>
    /// </list>
    ///
    /// CAPTURED EXTRAS carry the rest unchanged: the layer, type, personalize and tools buttons
    /// with their menus, Edit, Save and Cancel, the selection bar, and while editing the content
    /// box, the two sliders and the tag and note boxes (the editable lines leave Details then, so
    /// the presented-text dedupe cannot swallow the live boxes). Card hotspots are excluded (the tables own them) and the pawn button is
    /// re-declared with a name, since it draws only the pawn's bare name.
    ///
    /// Moving onto a timeline or chronicle row scrolls the mod's own views to that memory, so a
    /// viewer sees the card the cursor is on.
    /// </summary>
    public sealed class RimTalkMemoryMainTabScope : ScreenScope
    {
        private const int TimelineRegion = 0;
        private const int ChronicleRegion = 1;
        private const int DetailsRegion = 2;

        private const int ColMemory = 0;
        private const int ColLayer = 1;
        private const int ColContent = 2;
        private const int ColSelected = 3;
        private const int ColPinned = 4;
        private const int ColSummary = 5;
        private const int ColumnCount = 6;

        private enum DetailRow
        {
            Guide,
            LayerAndType,
            Age,
            Content,
            Importance,
            Activity,
            Tags,
            Notes,
            Status,
        }

        private readonly Window dialog;

        private readonly List<object> timeline = new List<object>();
        private readonly List<object> chronicle = new List<object>();
        private readonly List<DetailRow> details = new List<DetailRow>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        private object context;
        private object focused;

        public RimTalkMemoryMainTabScope(Window dialog)
        {
            this.dialog = dialog;
        }

        public override string Name
        {
            get { return "rimtalk-memory-maintab"; }
        }

        protected override int ContentRegionCount
        {
            get { return 3; }
        }

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case TimelineRegion: return "RimWorldAccess.Compat.RimTalk.Memory.TimelineRegionName".Translate();
                case ChronicleRegion: return "RimWorldAccess.Compat.RimTalk.Memory.ChronicleRegionName".Translate();
                default: return Translator.Translate("RimTalk.Memory.UI.TabWindow.Details");
            }
        }

        protected override string ContentRegionEntryDetail(int region)
        {
            object comp = RimTalkMemoryCompat.ContextMemoryComp(context);
            if (region == ChronicleRegion)
            {
                return comp == null ? null : (string)TranslatorFormattedStringExtensions.Translate("RimTalk.Memory.UI.TabWindow.ChronicleTitle",
                    (RimTalkMemoryCompat.ArchiveMemories(comp)?.Count ?? 0).Named("COUNT"));
            }
            if (region != TimelineRegion)
            {
                return null;
            }
            if (comp == null)
            {
                return "RimWorldAccess.Compat.RimTalk.Memory.NoPawnSelected".Translate();
            }
            string counts = "RimWorldAccess.Compat.RimTalk.Memory.StatsValue".Translate(
                RimTalkMemoryCompat.ActiveMemories(comp)?.Count ?? 0,
                RimTalkMemoryCompat.SituationalMemories(comp)?.Count ?? 0,
                RimTalkMemoryCompat.EventLogMemories(comp)?.Count ?? 0,
                RimTalkMemoryCompat.ArchiveMemories(comp)?.Count ?? 0);
            int selected = RimTalkMemoryCompat.SelectionCount(context);
            return selected == 0
                ? counts
                : (string)"RimWorldAccess.Compat.RimTalk.Memory.StatsWithSelection".Translate(
                    counts, TranslatorFormattedStringExtensions.Translate("RimTalk.Memory.UI.TabWindow.Selected", selected));
        }

        protected override void RefreshContent()
        {
            timeline.Clear();
            chronicle.Clear();
            details.Clear();

            context = RimTalkMemoryCompat.TabContext(dialog);
            object comp = RimTalkMemoryCompat.ContextMemoryComp(context);
            AddAll(timeline, RimTalkMemoryCompat.ContextTimelineMemories(context));
            AddAll(chronicle, RimTalkMemoryCompat.ArchiveMemories(comp));
            chronicle.Sort((a, b) => RimTalkMemoryCompat.MemGameTick(a).CompareTo(RimTalkMemoryCompat.MemGameTick(b)));

            focused = RimTalkMemoryCompat.ContextFocuse(context);
            if (focused == null)
            {
                details.Add(DetailRow.Guide);
                return;
            }
            details.Add(DetailRow.LayerAndType);
            details.Add(DetailRow.Age);
            if (!RimTalkMemoryCompat.DetailsEditing(RimTalkMemoryCompat.TabDetails(dialog)))
            {
                details.Add(DetailRow.Content);
                details.Add(DetailRow.Importance);
                details.Add(DetailRow.Activity);
                details.Add(DetailRow.Tags);
                details.Add(DetailRow.Notes);
            }
            details.Add(DetailRow.Status);
        }

        private static void AddAll(List<object> into, IList from)
        {
            if (from == null)
            {
                return;
            }
            foreach (object entry in from)
            {
                if (entry != null)
                {
                    into.Add(entry);
                }
            }
        }

        private List<object> Rows(int region)
        {
            return region == ChronicleRegion ? chronicle : timeline;
        }

        private object MemoryAt(int region, int row)
        {
            List<object> rows = Rows(region);
            return row >= 0 && row < rows.Count ? rows[row] : null;
        }

        protected override int ContentItemCount(int region)
        {
            return region == DetailsRegion ? details.Count : Rows(region).Count;
        }

        protected override int ContentColumnCount(int region)
        {
            return region == DetailsRegion ? 0 : ColumnCount;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region == DetailsRegion)
            {
                return null;
            }
            switch (column)
            {
                case ColMemory: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Memory.ColumnMemory".Translate());
                case ColLayer: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Memory.ColumnLayer".Translate());
                case ColContent: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Memory.ColumnContent".Translate());
                case ColSelected: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Memory.ColumnSelected".Translate());
                case ColPinned: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Memory.ColumnPinned".Translate());
                default: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Memory.ColumnSummary".Translate());
            }
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            object entry = region == DetailsRegion ? null : MemoryAt(region, row);
            if (entry == null)
            {
                return "";
            }
            switch (column)
            {
                case ColMemory: return CardTitle(region, entry);
                case ColLayer: return RimTalkMemoryCompat.MemLayerLabel(entry);
                case ColContent: return RimTalkMemoryCompat.MemContent(entry);
                case ColSelected: return RimTalkMemoryCompat.IsSelected(context, entry)
                    ? "RimWorldAccess.Compat.RimTalk.Memory.Selected".Translate()
                    : "RimWorldAccess.Compat.RimTalk.Memory.NotSelected".Translate();
                case ColPinned: return Translator.Translate(RimTalkMemoryCompat.MemIsPinned(entry)
                    ? "RimTalk.Memory.UI.TabWindow.Pinned" : "RimTalk.Memory.UI.TabWindow.NotPinned");
                default: return SummaryText(entry);
            }
        }

        /// <summary>The card's own title: type and age on the timeline (TimelineCard.GetTitle), the covered dates on the chronicle (ChronicleCard.GetTitle).</summary>
        private static string CardTitle(int region, object entry)
        {
            if (region == ChronicleRegion)
            {
                return "RimWorldAccess.Compat.RimTalk.Memory.ChronicleCardTitle".Translate(
                    GenDate.DateFullStringAt(GenDate.TickGameToAbs(RimTalkMemoryCompat.MemGameTick(entry)), Vector2.zero),
                    GenDate.DateFullStringAt(GenDate.TickGameToAbs(RimTalkMemoryCompat.MemEndGameTick(entry)), Vector2.zero));
            }
            string related = RimTalkMemoryCompat.MemRelatedPawnName(entry);
            return string.IsNullOrEmpty(related)
                ? (string)"RimWorldAccess.Compat.RimTalk.Memory.CardTitle".Translate(
                    RimTalkMemoryCompat.MemTypeLabel(entry), RimTalkMemoryCompat.MemAgeString(entry))
                : (string)"RimWorldAccess.Compat.RimTalk.Memory.CardTitleWithPawn".Translate(
                    RimTalkMemoryCompat.MemTypeLabel(entry), RimTalkMemoryCompat.MemAgeString(entry), related);
        }

        /// <summary>MemoryDetails' summary state words; a card shows the summarizing state as a pulsing outline.</summary>
        private string SummaryText(object entry)
        {
            object comp = RimTalkMemoryCompat.ContextMemoryComp(context);
            string key = RimTalkMemoryCompat.IsSummarizing(comp, entry) ? "RimTalk.Memory.UI.TabWindow.Summarizing"
                : RimTalkMemoryCompat.IsSummarized(comp, entry) ? "RimTalk.Memory.UI.TabWindow.Summarized"
                : "RimTalk.Memory.UI.TabWindow.NotSummarized";
            return Translator.Translate(key);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region != DetailsRegion)
            {
                object entry = MemoryAt(region, index);
                d.Label = entry == null ? "" : CardTitle(region, entry);
                return d;
            }
            if (index < 0 || index >= details.Count)
            {
                return d;
            }
            DescribeDetail(d, details[index]);
            return d;
        }

        private void DescribeDetail(ElementDescription d, DetailRow row)
        {
            d.ReadOnly = true;
            switch (row)
            {
                case DetailRow.Guide:
                    d.Label = SpeechFlatten.ToSentences(Translator.Translate("RimTalk.Memory.UI.TabWindow.Guide")) ?? "";
                    return;
                case DetailRow.LayerAndType:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Memory.LayerAndType".Translate(
                        RimTalkMemoryCompat.MemLayerLabel(focused), RimTalkMemoryCompat.MemTypeLabel(focused));
                    return;
                case DetailRow.Age:
                    d.Label = RimTalkMemoryCompat.MemAgeString(focused);
                    return;
                case DetailRow.Content:
                    d.Label = SpeechFlatten.ToSentences(RimTalkMemoryCompat.MemContent(focused)) ?? "";
                    return;
                case DetailRow.Importance:
                    d.Label = TranslatorFormattedStringExtensions.Translate("RimTalk.Memory.UI.TabWindow.Importance",
                        RimTalkMemoryCompat.MemImportance(focused).ToString("F2").Named("IMPORTANCE"));
                    return;
                case DetailRow.Activity:
                    d.Label = TranslatorFormattedStringExtensions.Translate("RimTalk.Memory.UI.TabWindow.Activity",
                        RimTalkMemoryCompat.MemActivity(focused).ToString("F2").Named("ACTIVITY"));
                    return;
                case DetailRow.Tags:
                    // The pane draws the mod's caption key directly followed by the joined tags.
                    IList<string> tags = RimTalkMemoryCompat.MemTags(focused);
                    d.Label = Translator.Translate("RimTalk.Memory.UI.TabWindow.Tags")
                        + (tags == null ? "" : string.Join(", ", tags));
                    return;
                case DetailRow.Notes:
                    // Same caption-then-value draw as the tags line.
                    d.Label = Translator.Translate("RimTalk.Memory.UI.TabWindow.Notes") + RimTalkMemoryCompat.MemNote(focused);
                    return;
                default:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Memory.DetailStatus".Translate(
                        Translator.Translate(RimTalkMemoryCompat.MemIsPinned(focused)
                            ? "RimTalk.Memory.UI.TabWindow.Pinned" : "RimTalk.Memory.UI.TabWindow.NotPinned"),
                        SummaryText(focused));
                    return;
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == DetailsRegion)
            {
                AnnounceCurrentItem();
                return;
            }
            object entry = MemoryAt(region, index);
            if (entry == null || context == null)
            {
                return;
            }
            RimTalkMemoryCompat.SelectMemory(context, entry, addToSelection: false);
            RefreshModel();
            AnnounceCurrentItem();
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            object entry = region == DetailsRegion ? null : MemoryAt(region, row);
            if (entry == null || context == null)
            {
                return false;
            }
            switch (column)
            {
                case ColSelected:
                    RimTalkMemoryCompat.SelectMemory(context, entry, addToSelection: true);
                    break;
                case ColPinned:
                    RimTalkMemoryCompat.TogglePin(RimTalkMemoryCompat.ContextMemoryComp(context), entry);
                    break;
                default:
                    return false;
            }
            RefreshModel();
            AnnounceCurrentItem();
            return true;
        }

        protected override void OnCursorSettled(int region, int index)
        {
            // Table regions carry their header at model row 0.
            object entry = region == TimelineRegion || region == ChronicleRegion ? MemoryAt(region, index - 1) : null;
            if (entry != null && context != null)
            {
                RimTalkMemoryCompat.RevealMemory(context, entry);
            }
        }

        // Focus ring: a card draws its content with Widgets.Label and covers itself with one
        // ButtonInvisible, so the hotspot holding the content label is the card on screen.

        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index < 0)
            {
                return default(Rect);
            }
            switch (Model.RegionIndex)
            {
                case TimelineRegion:
                case ChronicleRegion:
                    object entry = MemoryAt(Model.RegionIndex, region.Index - 1);
                    return entry == null ? default(Rect) : CardRect(RimTalkMemoryCompat.MemContent(entry));
                case DetailsRegion:
                    return LabelRect(DescribeContentItem(DetailsRegion, region.Index).Label);
                default:
                    return default(Rect);
            }
        }

        private static Rect CardRect(string content)
        {
            IReadOnlyList<CapturedWidget> pass = WidgetCapture.Items;
            for (int i = 0; i < pass.Count; i++)
            {
                if (pass[i].Kind != WidgetKind.Label || pass[i].Label != content)
                {
                    continue;
                }
                Vector2 center = pass[i].ScreenRect.center;
                for (int j = i + 1; j < pass.Count; j++)
                {
                    if (pass[j].Kind == WidgetKind.InvisibleButton && pass[j].ScreenRect.Contains(center))
                    {
                        return pass[j].VisibleScreenRect;
                    }
                }
            }
            return default(Rect);
        }

        private static Rect LabelRect(string text)
        {
            IReadOnlyList<CapturedWidget> pass = WidgetCapture.Items;
            for (int i = 0; i < pass.Count; i++)
            {
                if (pass[i].Kind == WidgetKind.Label && pass[i].Label == text)
                {
                    return pass[i].VisibleScreenRect;
                }
            }
            return default(Rect);
        }

        // The pawn button draws only the pawn's bare name, so it is re-declared with a caption and
        // still activated through the same captured widget, keeping the mod's own side window.

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Compat.RimTalk.Memory.PawnSelector".Translate(PawnButtonLabel()),
                    delegate { WidgetCapture.RequestActivate(WidgetKind.Button, PawnButtonLabel(), 0); },
                    "rimTalkMemory.pawnSelector"));
                return actions;
            }
        }

        /// <summary>MemoryTabHeader's own choice of button text.</summary>
        private string PawnButtonLabel()
        {
            Pawn pawn = RimTalkMemoryCompat.ContextPawn(RimTalkMemoryCompat.TabContext(dialog));
            return pawn != null ? pawn.LabelShort : (string)Translator.Translate("RimTalk.Memory.UI.TabWindow.Need_SelectPawn");
        }

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        protected override bool ExcludeFromCapturedExtras(CapturedWidget widget)
        {
            if (widget.Kind == WidgetKind.InvisibleButton)
            {
                return true;
            }
            if (widget.Kind == WidgetKind.Button && widget.Label == PawnButtonLabel())
            {
                return true;
            }
            return false;
        }

        protected override string ComposeOpenAnnouncement()
        {
            MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail("RimTalk_Memory");
            return def != null ? def.LabelCap.ToString() : null;
        }
    }
}
