using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Compositable Loadouts' tag editor (<c>Dialog_TagEditor</c>). Regions follow the window: the
    /// tag controls (select, create, delete, enable by default with its state, and the name box);
    /// the required items, each followed by its ranked alternatives; the def browser's category
    /// icons, alternatives banner, search, sort and show-only controls; the def list the browser
    /// filters; and the mass-assign panel. The list and its filters mirror DrawDefList step for
    /// step, as the draw builds them inline with nothing to read back.
    /// </summary>
    internal sealed partial class ClTagEditorScope : ScreenScope
    {
        private const int TagRegion = 0;
        private const int ItemsRegion = 1;
        private const int BrowserRegion = 2;
        private const int ThingsRegion = 3;
        private const int MassAssignRegion = 4;
        private const int RegionCount = 5;

        private const string DeleteActionId = "clTagEditor.delete";
        private const string CopyActionId = "clTagEditor.copy";
        private const string PasteActionId = "clTagEditor.paste";
        private const string EditActionId = "clTagEditor.edit";
        private const string AddAlternativeActionId = "clTagEditor.addAlternative";

        // The mod's InputField caps at 999 characters, and both the name and the search may be blank.
        private static readonly TextFieldSpec FreeTextSpec = new TextFieldSpec(labelKey: null, minLength: 0);

        private static readonly List<ClTagEditorScope> live = new List<ClTagEditorScope>();

        private enum RowKind
        {
            SelectTag, CreateTag, DeleteTag, DefaultEnabled, DefaultState, DefaultCondition, Name,
            Item, Member,
            Category, AlternativesBanner, AlternativesDone, Search, SortBy, SortOrder, ShowOnly,
            Preset, Thing,
            PanelToggle, PanelState, PanelCondition, Pawn,
        }

        private sealed class Row
        {
            public RowKind Kind;
            public object Item;
            public object Member;
            public int Index;
            public ThingDef Def;
            public Pawn Pawn;
            public object Preset;
        }

        private readonly Window dialog;
        private readonly List<Row>[] rows = new List<Row>[RegionCount];
        private readonly List<ThingDef> things = new List<ThingDef>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly TextFieldEditSession edit = new TextFieldEditSession();
        private object tag;
        private object prostheticSlots;
        private object addingTo;
        private string pendingPrefix;

        public ClTagEditorScope(Window dialog)
        {
            this.dialog = dialog;
            for (int i = 0; i < RegionCount; i++)
            {
                rows[i] = new List<Row>();
            }
            Claim(SharedMenuGrammar.Info, e => OpenInfoCard(), when: () => InfoTarget(CurrentRow()) != null);
            Claim(DeleteActionId, e => RemoveCurrent(), when: () => IsItemRow(CurrentRow()) || CurrentRow()?.Kind == RowKind.Member);
            Claim(CopyActionId, e => CopyCurrent(), when: () => IsItemRow(CurrentRow()));
            Claim(PasteActionId, e => PasteCurrent(), when: () => IsItemRow(CurrentRow()));
            Claim(EditActionId, e => EditCurrent(), when: () => IsItemRow(CurrentRow()) && ClCompat.TagEditor.SpecifiableMember(CurrentRow().Item) != null);
            Claim(AddAlternativeActionId, e => BeginAlternatives(), when: () => IsItemRow(CurrentRow()) && !ClCompat.ItemIsProsthetic(CurrentRow().Item));
            RegisterPopTeardown(edit.CancelIfActive);
        }

        public override string Name => "clTagEditor";

        protected override bool EnableTypeahead => true;

        protected internal override Window OwnedWindow => dialog;

        protected override bool CaptureWindowButtons => false;

        protected override int ContentRegionCount => RegionCount;

        internal static ClTagEditorScope For(Window window)
        {
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i].dialog == window)
                {
                    return live[i];
                }
            }
            return null;
        }

        public override void OnPush()
        {
            base.OnPush();
            live.Add(this);
        }

        public override void OnPop()
        {
            base.OnPop();
            live.Remove(this);
            pendingPrefix = null;
        }

        /// <summary>Posts the live edit buffer each draw, so the mod renders and filters from what is being typed.</summary>
        internal void MirrorLive()
        {
            edit.MirrorLive();
        }

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case TagRegion:
                    return CompatText.ModText("CompositableLoadouts.EditOrCreateTags");
                case ItemsRegion:
                    return "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.ModifyTag".Translate(ClCompat.TagName(tag) ?? "");
                case BrowserRegion:
                    return "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.BrowserRegion".Translate();
                case ThingsRegion:
                    return CategoryLabel(ClCompat.TagEditor.Category(dialog));
                default:
                    return CompatText.ModText("CompositableLoadouts.MassAssign");
            }
        }

        /// <summary>Without a tag the window draws only its top buttons.</summary>
        protected override bool ContentRegionAlwaysNavigable(int region)
        {
            return region == TagRegion || tag != null;
        }

        protected override string ComposeOpenAnnouncement()
        {
            return CompatText.JoinSentences(new List<string>
            {
                CompatText.ModText("CompositableLoadouts.EditOrCreateTags"),
                ClCompat.TagName(ClCompat.TagEditor.CurTag(dialog)),
            });
        }

        protected override string AnnouncePrefix(int region, int index)
        {
            string prefix = pendingPrefix;
            pendingPrefix = null;
            return prefix;
        }

        protected override int ContentItemCount(int region)
        {
            return rows[region].Count;
        }

        protected override string ContentRowSearchText(int region, int row)
        {
            Row r = RowAt(region, row);
            return r == null ? "" : RowLabel(r);
        }

        protected override TypeaheadCandidateKind ContentRowSearchKind(int region, int row)
        {
            return region == ThingsRegion ? TypeaheadCandidateKind.Item : base.ContentRowSearchKind(region, row);
        }

        // ---- The row model ----

        protected override void RefreshContent()
        {
            foreach (List<Row> list in rows)
            {
                list.Clear();
            }
            things.Clear();
            tag = ClCompat.TagEditor.CurTag(dialog);
            prostheticSlots = null;
            addingTo = null;

            List<Row> top = rows[TagRegion];
            top.Add(new Row { Kind = RowKind.SelectTag });
            top.Add(new Row { Kind = RowKind.CreateTag });
            if (tag == null)
            {
                return;
            }
            top.Add(new Row { Kind = RowKind.DeleteTag });
            top.Add(new Row { Kind = RowKind.DefaultEnabled });
            if (ClCompat.TagEditor.TagDefaultEnabled(tag))
            {
                top.Add(new Row { Kind = RowKind.DefaultState });
                if (DefaultState() != null)
                {
                    top.Add(new Row { Kind = RowKind.DefaultCondition });
                }
            }
            top.Add(new Row { Kind = RowKind.Name });

            prostheticSlots = ClCompat.TagEditor.ProstheticSlots(tag);
            List<object> items = ClCompat.TagItems(tag);
            // DrawDefList drops a stale target before anything reads it.
            object adding = ClCompat.TagEditor.AddingAlternativesTo(dialog);
            addingTo = items.Contains(adding) ? adding : null;
            for (int i = 0; i < items.Count; i++)
            {
                rows[ItemsRegion].Add(new Row { Kind = RowKind.Item, Item = items[i], Index = i });
                if (!ClCompat.ItemHasAlternatives(items[i]))
                {
                    continue;
                }
                List<object> members = ClCompat.ItemMembers(items[i]);
                for (int m = 0; m < members.Count; m++)
                {
                    rows[ItemsRegion].Add(new Row { Kind = RowKind.Member, Item = items[i], Member = members[m], Index = m });
                }
            }

            BuildBrowserRows(items);
            BuildPanelRows();
        }

        private void BuildBrowserRows(List<object> items)
        {
            List<Row> browser = rows[BrowserRegion];
            for (int c = 0; c < ClCompat.TagEditor.CategoryCount; c++)
            {
                browser.Add(new Row { Kind = RowKind.Category, Index = c });
            }
            if (addingTo != null)
            {
                browser.Add(new Row { Kind = RowKind.AlternativesBanner, Item = addingTo });
                browser.Add(new Row { Kind = RowKind.AlternativesDone, Item = addingTo });
            }
            browser.Add(new Row { Kind = RowKind.Search });
            browser.Add(new Row { Kind = RowKind.SortBy });
            browser.Add(new Row { Kind = RowKind.SortOrder });
            browser.Add(new Row { Kind = RowKind.ShowOnly });

            int category = ClCompat.TagEditor.Category(dialog);
            things.AddRange(BuildDefList(category, items));
            object preset = ClCompat.TagEditor.CategoryPreset(category);
            if (preset != null && addingTo == null && PresetShown(preset, items))
            {
                rows[ThingsRegion].Add(new Row { Kind = RowKind.Preset, Preset = preset });
            }
            foreach (ThingDef def in things)
            {
                rows[ThingsRegion].Add(new Row { Kind = RowKind.Thing, Def = def });
            }
        }

        /// <summary>DrawDefList's gate: hidden while any of its defs is already in the tag.</summary>
        private static bool PresetShown(object preset, List<object> items)
        {
            List<ThingDef> defs = ClCompat.TagEditor.PresetDefs(preset);
            if (defs.Count == 0)
            {
                return false;
            }
            var inTag = new HashSet<ThingDef>(items.SelectMany(ClCompat.TagEditor.ItemDefs));
            return !defs.Any(inTag.Contains);
        }

        private void BuildPanelRows()
        {
            List<Row> panel = rows[MassAssignRegion];
            panel.Add(new Row { Kind = RowKind.PanelToggle });
            object element = ClCompat.TagEditor.MassAssignElement(dialog);
            if (ClCompat.TagEditor.PanelCollapsed(dialog) || element == null)
            {
                return;
            }
            panel.Add(new Row { Kind = RowKind.PanelState });
            if (ClCompat.ElementState(element) != null)
            {
                panel.Add(new Row { Kind = RowKind.PanelCondition });
            }
            if (Current.Game == null)
            {
                return;
            }
            IEnumerable<Pawn> pawns = Find.Maps
                .SelectMany(map => map.mapPawns.AllPawns.Where(ClCompat.IsValidLoadoutHolder))
                .OrderByDescending(p => p.Name?.ToString() ?? "");
            foreach (Pawn pawn in pawns)
            {
                panel.Add(new Row { Kind = RowKind.Pawn, Pawn = pawn });
            }
        }

        private Row RowAt(int region, int index)
        {
            return region >= 0 && region < RegionCount && index >= 0 && index < rows[region].Count
                ? rows[region][index]
                : null;
        }

        private Row CurrentRow()
        {
            ListModel region = Model.CurrentRegion;
            return region == null || region.IsEmpty ? null : RowAt(Model.RegionIndex, region.Index);
        }

        private static bool IsItemRow(Row row)
        {
            return row != null && row.Kind == RowKind.Item;
        }

        private object DefaultStateElement()
        {
            return ClCompat.TagEditor.DefaultStateElement(dialog);
        }

        private int RowIndexOf(int region, RowKind kind)
        {
            return rows[region].FindIndex(r => r.Kind == kind);
        }
    }
}
