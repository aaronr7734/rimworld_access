using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// <see cref="TreeRegionScope"/> plus the <see cref="RimWorldAccess.ThingFilterSessionCore"/>
    /// integration the ThingFilter-adjacent screens share. Every TREE NODE here is exclusively
    /// SpecialFilter/Category/ThingDef; Priority, ClearAll, AllowAll and the range openers are
    /// PREFIX rows above the tree in the same region, not tree nodes, so top-level category nodes
    /// have different siblings than a synthetic-root tree would give them.
    ///
    /// Prefix rows compose in two tiers so a screen-specific row (Storage's Priority stepper) can
    /// sit before this layer's fixed filter-action rows: the <c>SubclassLeadingRow*</c> hooks (0
    /// rows by default), then ClearAll and AllowAll, then the HitPoints, Quality and
    /// MentalBreakChance range openers each subject to their own visibility hook. That is vanilla's
    /// own <c>ThingFilterUI.DoThingFilterConfigWindow</c> draw order.
    ///
    /// Space shares Enter's activation path. Alt+I opens the focused ThingDef's info card, claimed
    /// only while the cursor is in a tree region so a subclass with non-tree regions can carry its
    /// own Alt+I there. Escape clears an active search first, else calls
    /// <see cref="OnFilterTreeClose"/>.
    /// </summary>
    public abstract class FilterTreeScopeBase : TreeRegionScope
    {
        protected FilterTreeScopeBase()
        {
            // Gated on the cursor being in a tree region so this claim, registered earlier than a
            // subclass's, cannot shadow a non-tree region's own Alt+I.
            Claim("filterTree.infoCard", e => ActivateFilterTreeInfoCard(), when: () => CursorInTreeRegion);
            // TreeRegionScope leaves tree.jumpTo*Section for subclasses to claim; all the filter
            // screens want the same behavior, so it is claimed once here.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));
            Claim(SharedMenuGrammar.Cancel, e => OnFilterTreeClose(),
                when: () => !TypeaheadHasActiveSearch && ClaimsFilterTreeCancel);
        }

        /// <summary>A scope that CLAIMS Escape must also OWN it: <see cref="ShellKeyOwnership.OwnerOf"/>
        /// reads this, not the claim table, so reporting false here lets vanilla's GUI pass run first,
        /// close the window BENEATH a windowless filter tree and <c>Use()</c> the key — leaving the
        /// tree orphaned over a dead window.</summary>
        public override bool OwnsCancel
        {
            get { return base.OwnsCancel || ClaimsFilterTreeCancel; }
        }

        /// <summary>Whether Escape closes this filter tree: true for the standalone windowless filter
        /// screens, false for a tree hosted inside a real vanilla window, where Escape belongs to the
        /// window.</summary>
        protected virtual bool ClaimsFilterTreeCancel
        {
            get { return true; }
        }

        // The filter-session contract a subclass fills in.

        /// <summary>The shared per-session filter context (current filter, parent filter, force-hidden special filters).</summary>
        protected virtual ThingFilterSessionCore.FilterContext BuildFilterContext()
        {
            return default(ThingFilterSessionCore.FilterContext);
        }

        /// <summary>The filter context of one content region. Only a screen whose vanilla window draws
        /// two filter panels side by side overrides this rather than the single-context form.</summary>
        protected virtual ThingFilterSessionCore.FilterContext BuildFilterContext(int region)
        {
            return BuildFilterContext();
        }

        /// <summary>The context a region's tree is BUILT from: the subclass's context plus the openMask
        /// vanilla was last seen drawing that filter with. Vanilla's open bits outlive our model, so a
        /// rebuilt tree adopts them rather than starting collapsed under an open-looking panel.</summary>
        protected ThingFilterSessionCore.FilterContext ContextForBuild(int region)
        {
            ThingFilterSessionCore.FilterContext ctx = BuildFilterContext(region);
            ctx.OpenMask = ThingFilterTreeSync.OpenMaskFor(ctx.CurrentFilter);
            return ctx;
        }

        /// <summary>Just the filter a region edits. Separate from <see cref="BuildFilterContext(int)"/>
        /// because <see cref="ThingFilterTreeSync"/> asks per drawn panel per GUI pass, so a subclass
        /// whose context build allocates must answer this without allocating.</summary>
        protected virtual ThingFilter FilterForRegion(int region)
        {
            return BuildFilterContext(region).CurrentFilter;
        }

        // Visual sync with vanilla's own drawn panel — see ThingFilterTreeSync.

        public override void OnPush()
        {
            base.OnPush();
            ThingFilterTreeSync.Register(this);
        }

        public override void OnPop()
        {
            base.OnPop();
            ThingFilterTreeSync.Unregister(this);
        }

        /// <summary>These screens are mirror-pushed with no window of their own, so the surface is
        /// whichever window last drew one of this scope's panels.</summary>
        protected override Window PointerSurface
        {
            get
            {
                Window own = base.PointerSurface;
                if (own != null)
                {
                    return own;
                }
                for (int region = 0; region < ContentRegionCount; region++)
                {
                    Window host = ThingFilterTreeSync.HostWindowFor(FilterForRegion(region));
                    if (host != null)
                    {
                        return host;
                    }
                }
                return null;
            }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            for (int region = 0; region < ContentRegionCount; region++)
            {
                TreePanel panel = PanelFor(region);
                ThingFilter filter = FilterForRegion(region);
                if (panel == null || filter == null)
                {
                    continue;
                }
                int prefix = PrefixRowCountFor(region);
                IReadOnlyList<InspectionTreeItem> visible = panel.Tree.Visible;
                for (int i = 0; i < visible.Count; i++)
                {
                    if (visible[i].Data is ThingFilterSessionCore.FilterNodeData data)
                    {
                        ThingFilterTreeSync.AddRouteCandidates(
                            filter, data.Reference, region, prefix + i, candidates, targets);
                    }
                }
            }
        }

        /// <summary>For a filter panel vanilla is about to draw: whether this scope owns it and, if so,
        /// its model root plus the vanilla object under the cursor. <paramref name="focusedReference"/>
        /// is null unless the cursor is in this panel's own region and on a tree row, so prefix rows and
        /// Tabbed-away panels ring nothing.</summary>
        internal bool TryResolveVisualPanel(ThingFilter filter, out InspectionTreeItem root,
            out object focusedReference)
        {
            root = null;
            focusedReference = null;
            if (filter == null)
            {
                return false;
            }
            for (int region = 0; region < ContentRegionCount; region++)
            {
                TreePanel panel = PanelFor(region);
                if (panel == null || !ReferenceEquals(FilterForRegion(region), filter))
                {
                    continue;
                }
                root = panel.Tree.Root;
                if (region == Model.RegionIndex)
                {
                    InspectionTreeItem current = CurrentTreeItem();
                    if (current != null
                        && current.Data is ThingFilterSessionCore.FilterNodeData data)
                    {
                        focusedReference = data.Reference;
                    }
                }
                return true;
            }
            return false;
        }

        /// <summary>Escape with no active search: close the screen and announce the return. Never called
        /// when <see cref="ClaimsFilterTreeCancel"/> is false.</summary>
        protected virtual void OnFilterTreeClose()
        {
        }

        /// <summary>HitPoints range row visibility, from the parent filter's <c>allowedHitPointsConfigurable</c>.</summary>
        protected virtual bool ShowHitPointsRange
        {
            get { return true; }
        }

        /// <summary>Quality range row visibility, from the parent filter's <c>allowedQualitiesConfigurable</c>.</summary>
        protected virtual bool ShowQualityRange
        {
            get { return true; }
        }

        /// <summary>MentalBreakChance range row visibility; only the Reading Policy "Book Effects" panel
        /// sets this true.</summary>
        protected virtual bool ShowMentalBreakChanceRange
        {
            get { return false; }
        }

        // Region-parameterized twins of the hooks above, defaulting to the single-region form. Only
        // reading policy, whose two panels differ from each other, overrides these.

        protected virtual bool ShowHitPointsRangeFor(int region)
        {
            return ShowHitPointsRange;
        }

        protected virtual bool ShowQualityRangeFor(int region)
        {
            return ShowQualityRange;
        }

        protected virtual bool ShowMentalBreakChanceRangeFor(int region)
        {
            return ShowMentalBreakChanceRange;
        }

        protected virtual int SubclassLeadingRowCountFor(int region)
        {
            return SubclassLeadingRowCount;
        }

        /// <summary>Screen-specific rows BEFORE ClearAll/AllowAll/ranges.</summary>
        protected virtual int SubclassLeadingRowCount
        {
            get { return 0; }
        }

        protected virtual ElementDescription DescribeSubclassLeadingRow(int index)
        {
            return new ElementDescription();
        }

        protected virtual void ActivateSubclassLeadingRow(int index)
        {
        }

        protected virtual bool CanAdjustSubclassLeadingRow(int index)
        {
            return false;
        }

        protected virtual void AdjustSubclassLeadingRow(int index, int direction)
        {
        }

        // Prefix-row composition: [subclass leading rows][ClearAll][AllowAll][HitPoints?][Quality?]

        private int FilterActionRowCount(int region)
        {
            return 2 + (ShowHitPointsRangeFor(region) ? 1 : 0) + (ShowQualityRangeFor(region) ? 1 : 0)
                + (ShowMentalBreakChanceRangeFor(region) ? 1 : 0);
        }

        protected override int PrefixRowCount
        {
            get { return PrefixRowCountFor(TreeRegionIndex); }
        }

        protected override int PrefixRowCountFor(int region)
        {
            return SubclassLeadingRowCountFor(region) + FilterActionRowCount(region);
        }

        protected override ElementDescription DescribePrefixRow(int region, int index)
        {
            int leading = SubclassLeadingRowCountFor(region);
            if (index < leading)
            {
                return DescribeSubclassLeadingRow(index);
            }
            int i = index - leading;
            if (i == 0)
            {
                return DescribeClearAllRow();
            }
            if (i == 1)
            {
                return DescribeAllowAllRow();
            }
            i -= 2;
            if (ShowHitPointsRangeFor(region))
            {
                if (i == 0)
                {
                    return DescribeHitPointsRangeRow(region);
                }
                i -= 1;
            }
            if (ShowQualityRangeFor(region))
            {
                if (i == 0)
                {
                    return DescribeQualityRangeRow(region);
                }
                i -= 1;
            }
            if (ShowMentalBreakChanceRangeFor(region) && i == 0)
            {
                return DescribeMentalBreakChanceRangeRow(region);
            }
            return new ElementDescription();
        }

        protected override void ActivatePrefixRow(int region, int index)
        {
            int leading = SubclassLeadingRowCountFor(region);
            if (index < leading)
            {
                ActivateSubclassLeadingRow(index);
                return;
            }
            int i = index - leading;
            if (i == 0)
            {
                ActivateClearAllRow(region);
                return;
            }
            if (i == 1)
            {
                ActivateAllowAllRow(region);
                return;
            }
            i -= 2;
            if (ShowHitPointsRangeFor(region))
            {
                if (i == 0)
                {
                    ActivateHitPointsRangeRow(region);
                    return;
                }
                i -= 1;
            }
            if (ShowQualityRangeFor(region))
            {
                if (i == 0)
                {
                    ActivateQualityRangeRow(region);
                    return;
                }
                i -= 1;
            }
            if (ShowMentalBreakChanceRangeFor(region) && i == 0)
            {
                ActivateMentalBreakChanceRangeRow(region);
            }
        }

        protected override bool CanAdjustPrefixRow(int region, int index)
        {
            if (index < SubclassLeadingRowCountFor(region))
            {
                return CanAdjustSubclassLeadingRow(index);
            }
            // ClearAll/AllowAll/range openers are plain Button rows: no Left/Right function.
            return false;
        }

        protected override void AdjustPrefixRow(int region, int index, int direction)
        {
            if (index < SubclassLeadingRowCountFor(region))
            {
                AdjustSubclassLeadingRow(index, direction);
            }
        }

        private ElementDescription DescribeClearAllRow()
        {
            return new ElementDescription
            {
                Label = "ClearAll".Translate().ToString(),
                Role = ElementRole.Button,
            };
        }

        private ElementDescription DescribeAllowAllRow()
        {
            return new ElementDescription
            {
                Label = "AllowAll".Translate().ToString(),
                Role = ElementRole.Button,
            };
        }

        private ElementDescription DescribeHitPointsRangeRow(int region)
        {
            FloatRange range = BuildFilterContext(region).CurrentFilter.AllowedHitPointsPercents;
            return new ElementDescription
            {
                Label = "HitPointsBasic".Translate().CapitalizeFirst(),
                Role = ElementRole.Slider,
                Value = "RimWorldAccess.Shell.FilterTree.RangeValue".Translate(
                    range.min.ToString("P0"), range.max.ToString("P0")),
                EntersEditOnAccept = true,
            };
        }

        private ElementDescription DescribeQualityRangeRow(int region)
        {
            QualityRange range = BuildFilterContext(region).CurrentFilter.AllowedQualityLevels;
            return new ElementDescription
            {
                Label = "Quality".Translate(),
                Role = ElementRole.Slider,
                Value = "RimWorldAccess.Shell.FilterTree.RangeValue".Translate(
                    range.min.GetLabel(), range.max.GetLabel()),
                EntersEditOnAccept = true,
            };
        }

        private ElementDescription DescribeMentalBreakChanceRangeRow(int region)
        {
            FloatRange range = BuildFilterContext(region).CurrentFilter.AllowedMentalBreakChance;
            return new ElementDescription
            {
                Label = "BookMentalBreakChance".Translate(),
                Role = ElementRole.Slider,
                Value = "RimWorldAccess.Shell.FilterTree.RangeValue".Translate(
                    range.min.ToString("P0"), range.max.ToString("P0")),
                EntersEditOnAccept = true,
            };
        }

        /// <summary>Also reached directly by ThingFilterScope's <c>thingFilter.disallowAll</c> shortcut
        /// chord, which is muscle-memory parity for the same vanilla button, not a second
        /// implementation.</summary>
        protected void ActivateClearAllRow()
        {
            ActivateClearAllRow(TreeRegionIndex);
        }

        protected void ActivateClearAllRow(int region)
        {
            ThingFilterSessionCore.FilterContext ctx = BuildFilterContext(region);
            TreePanel panel = PanelFor(region);
            // forceHiddenDefs is null on every call site here, as it is on vanilla's storage tab.
            ctx.CurrentFilter.SetDisallowAll(null, ctx.ForceHiddenFilters);
            ThingFilterSessionCore.RefreshAllowanceStates(ctx, panel != null ? panel.Tree.Root : Tree.Root);
            // Vanilla's own button sound (Verse/ThingFilterUI.cs:36-37).
            SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            TolkHelper.Speak("ClearAll".Loc());
        }

        /// <summary>Also reached directly by a shortcut chord; see <see cref="ActivateClearAllRow(int)"/>.</summary>
        protected void ActivateAllowAllRow()
        {
            ActivateAllowAllRow(TreeRegionIndex);
        }

        protected void ActivateAllowAllRow(int region)
        {
            ThingFilterSessionCore.FilterContext ctx = BuildFilterContext(region);
            TreePanel panel = PanelFor(region);
            ctx.CurrentFilter.SetAllowAll(ctx.ParentFilter);
            ThingFilterSessionCore.RefreshAllowanceStates(ctx, panel != null ? panel.Tree.Root : Tree.Root);
            // Vanilla's own button sound (Verse/ThingFilterUI.cs:41-42).
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            TolkHelper.Speak("AllowAll".Loc());
        }

        /// <summary>Opens the shared HitPoints range sub-editor, which writes through this accessor pair
        /// on every step; closing it commits nothing further.</summary>
        protected virtual void ActivateHitPointsRangeRow(int region)
        {
            RangeEditMenuState.OpenHitPointsRange(
                () => BuildFilterContext(region).CurrentFilter?.AllowedHitPointsPercents ?? FloatRange.ZeroToOne,
                v => { ThingFilter f = BuildFilterContext(region).CurrentFilter; if (f != null) f.AllowedHitPointsPercents = v; });
            RangeEditMenuState.OfferTypeInBox(FilterForRegion(region));
        }

        /// <summary>Opens the shared Quality range sub-editor. See <see cref="ActivateHitPointsRangeRow"/>'s remarks.</summary>
        protected virtual void ActivateQualityRangeRow(int region)
        {
            RangeEditMenuState.OpenQualityRange(
                () => BuildFilterContext(region).CurrentFilter?.AllowedQualityLevels ?? QualityRange.All,
                v => { ThingFilter f = BuildFilterContext(region).CurrentFilter; if (f != null) f.AllowedQualityLevels = v; });
        }

        /// <summary>Opens the shared MentalBreakChance range sub-editor; see <see cref="ActivateHitPointsRangeRow"/>.</summary>
        protected virtual void ActivateMentalBreakChanceRangeRow(int region)
        {
            RangeEditMenuState.OpenMentalBreakChanceRange(
                () => BuildFilterContext(region).CurrentFilter?.AllowedMentalBreakChance ?? FloatRange.ZeroToOne,
                v => { ThingFilter f = BuildFilterContext(region).CurrentFilter; if (f != null) f.AllowedMentalBreakChance = v; });
            RangeEditMenuState.OfferTypeInBox(FilterForRegion(region));
        }

        // Tree node describe/activate: SpecialFilter/Category/ThingDef only.

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var data = item.Data as ThingFilterSessionCore.FilterNodeData;
            var d = new ElementDescription();
            if (data == null)
            {
                d.Label = item.Label;
                return d;
            }
            // Vanilla draws these rows with Widgets.Checkbox and its tri-state siblings, so they
            // announce as checkboxes rather than as a silent tree-item role. Level and Position are
            // composed by the base.
            d.Role = ElementRole.Checkbox;
            d.Label = item.Label;
            switch (data.Type)
            {
                case ThingFilterSessionCore.NodeKind.Category:
                    d.Check = data.State;
                    d.Expanded = item.IsExpanded;
                    d.Extras = ThingFilterSessionCore.CategoryExtras(BuildFilterContext(DescribingRegion), item, data);
                    break;
                case ThingFilterSessionCore.NodeKind.SpecialFilter:
                case ThingFilterSessionCore.NodeKind.ThingDef:
                case ThingFilterSessionCore.NodeKind.UndiscoveredGroup:
                    d.Check = data.State;
                    d.Extras = item.Description;
                    break;
                default:
                    // No other kind reaches the tree; present the bare label rather than fake a state.
                    break;
            }
            return d;
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            var data = item.Data as ThingFilterSessionCore.FilterNodeData;
            if (data == null)
            {
                AnnounceCurrentItem();
                return;
            }
            ThingFilterSessionCore.FilterContext ctx = BuildFilterContext(Model.RegionIndex);
            if (ThingFilterSessionCore.TryToggleCommonNode(ctx, item, data, Tree.Root, out CheckState result))
            {
                // Vanilla's own checkbox sound: TurnedOff only for a fully-off result.
                (result == CheckState.Unchecked
                    ? SoundDefOf.Checkbox_TurnedOff
                    : SoundDefOf.Checkbox_TurnedOn).PlayOneShotOnCamera();
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                    new ElementDescription { Check = result }, TranslatedShellVocabulary.Instance));
            }
            else
            {
                // Unreachable in practice; re-announce rather than stay silent.
                AnnounceCurrentItem();
            }
        }

        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            var data = item.Data as ThingFilterSessionCore.FilterNodeData;
            return data != null && data.Type == ThingFilterSessionCore.NodeKind.Category;
        }

        /// <summary>Page Up/Down sections are the top-level tree rows: the root special filters and the
        /// top <c>ThingCategoryDef</c> tier. Prefix Button rows cannot be jump targets by construction,
        /// since <see cref="TreeRegionScope.PerformJumpToAdjacentSection"/> scans tree nodes only.</summary>
        protected override bool IsSectionBoundary(InspectionTreeItem item)
        {
            return item.IndentLevel == 0;
        }

        private void ActivateFilterTreeInfoCard()
        {
            InspectionTreeItem current = CurrentTreeItem();
            if (current != null && current.LinkedDef != null)
            {
                InfoCardState.OpenInfoCardForDef(current.LinkedDef);
                return;
            }
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Shell.FilterTree.NoInfoCard".Loc());
        }
    }
}
