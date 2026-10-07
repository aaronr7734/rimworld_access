using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard scope for the starting-pawn editor, window-attached to both hosts sharing
    /// <see cref="StartingPawnState"/>: <see cref="Page_ConfigureStartingPawns"/> and
    /// <see cref="Dialog_ChooseNewWanderers"/>. Regions: the pawn tree (region 0), page options
    /// (GameStart only, present only while it has rows), and read-only team skills (both hosts —
    /// Dialog_ChooseNewWanderers builds its own GameInitData in PreOpen and draws
    /// StartingPawnUtility.DrawSkillSummaries unconditionally). In GameStart the tree's top level is
    /// vanilla's two group headers (StartingPawnsSelected, plus StartingPawnsLeftBehind when
    /// <c>startingPawnCount &lt; startingAndOptionalPawns.Count</c>); that boundary is a display
    /// grouping over ONE flat roster, so Ctrl+Up/Ctrl+Down reorder across it freely, as vanilla's own
    /// reorder delegate does. Wanderer mode draws no headers, hence no group nodes.
    /// <see cref="CaptureWindowButtons"/> is false because both hosts also draw non-toolbar buttons,
    /// so the toolbar actions are declared here.
    /// A group node speaks its bare name plus the expansion suffix carrying its child count, Role and
    /// Expanded left unset so the state word is spoken once; a pawn folds its verbose
    /// age/gender/traits/skills summary into <see cref="ElementDescription.Extras"/> only while
    /// collapsed. Combo rows are Enter-open pickers — Left/Right belong to tree expansion.
    /// The tree is built once and rebuilt only on a known mutation
    /// (<see cref="RebuildTreePreservingState"/>), which is what lets expansion state and the cursor
    /// survive a name edit, a randomize or a roster change; a mutation path bypassing those hooks
    /// would leave a row speaking stale card text, so the volatile values (current name, collapsed
    /// pawn summary, combo value) are live-read regardless of the tree's age.
    /// Back and Start/Confirm keep their confirmation dialogs
    /// (StartingPawnState.RequestBackConfirm/ConfirmStartGame/ConfirmWandererStart) instead of riding
    /// CanDoBack/DoBack directly. Those confirms reflect DoBack/DoNext themselves, so no request flag
    /// is needed and <see cref="StartingPawnScreenScopePatch_CanDoBack"/> can block Page's deferred
    /// raw Escape poll unconditionally while this scope is Top. No CanDoNext twin is needed:
    /// doNextOnKeypress:false means Page.DoBottomButtons' Accept poll never fires by keyboard.
    /// </summary>
    public sealed class StartingPawnScreenScope : TreeRegionScope
    {
        private enum RegionKind { Pawns, PageOptions, TeamSkills }
        private enum PageOptionKind { ShowHeadgear, ShowApparel, XenotypeEditor, Warning }

        private sealed class PageOptionRow
        {
            public PageOptionKind Kind;
            public string WarningText;
        }

        private static readonly AccessTools.FieldRef<Page_ConfigureStartingPawns, bool> RenderHeadgearField =
            AccessTools.FieldRefAccess<Page_ConfigureStartingPawns, bool>("renderHeadgear");
        private static readonly AccessTools.FieldRef<Page_ConfigureStartingPawns, bool> RenderClothesField =
            AccessTools.FieldRefAccess<Page_ConfigureStartingPawns, bool>("renderClothes");
        private static readonly System.Reflection.PropertyInfo ExtraCanDoNextReportProperty =
            AccessTools.Property(typeof(Page_ConfigureStartingPawns), "ExtraCanDoNextReport");
        private static readonly AccessTools.FieldRef<Page_ConfigureStartingPawns, Vector2> ScrollField =
            AccessTools.FieldRefAccess<Page_ConfigureStartingPawns, Vector2>("scroll");

        /// <summary>
        /// The visible list height DrawPawnList's own scroll view uses. Not cheaply closed-form from
        /// page layout, so StartingPawnPatch's prefix captures it live every non-Layout GUI pass
        /// this scope is Top.
        /// </summary>
        private static float capturedVisibleHeight;
        private static int capturedVisibleHeightFrame = -1;

        /// <summary>Written by the DrawPawnList capture prefix (StartingPawnPatch.cs).</summary>
        internal static void CaptureVisibleHeight(float height)
        {
            capturedVisibleHeight = height;
            capturedVisibleHeightFrame = Time.frameCount;
        }

        /// <summary>The one live instance (only one host is ever open at a time), so StartingPawnState's static mutators can notify the scope.</summary>
        internal static StartingPawnScreenScope Active { get; private set; }

        private readonly Window host;
        private readonly PawnEditorContext context;

        private readonly List<RegionKind> activeRegions = new List<RegionKind>();
        private readonly List<PageOptionRow> pageOptionRows = new List<PageOptionRow>();
        private readonly List<string> teamSkillRows = new List<string>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly TextFieldEditSession nameEditSession = new TextFieldEditSession();

        private bool announcedOpen;
        private int editingLeafPawnIndex = -1;
        private NameFieldKind editingNameField;

        public StartingPawnScreenScope(Window host, PawnEditorContext context)
        {
            this.host = host;
            this.context = context;

            Claim("startingPawns.openFilter", e => PawnFilterState.Open(), when: PawnsRegionLive);
            Claim("startingPawns.randomize", e => ActivateRandomize(), when: PawnsRegionLive);
            Claim("startingPawns.rename", e => ActivateRename(), when: PawnsRegionLive);
            Claim(SharedMenuGrammar.Info, OnInfo, when: PawnsRegionLive);
            Claim("startingPawns.addPawn", e => ActivateAddPawn(), when: WandererPawnsRegionLive);
            Claim("startingPawns.removePawn", e => ActivateRemovePawn(), when: WandererPawnsRegionLive);
            Claim(SharedMenuGrammar.ReorderUp, e => ActivateReorder(-1), when: PawnsRegionLive);
            Claim(SharedMenuGrammar.ReorderDown, e => ActivateReorder(1), when: PawnsRegionLive);
            Claim("startingPawns.previousPawn", e => SwitchPawn(-1), when: PawnsRegionLive);
            Claim("startingPawns.nextPawn", e => SwitchPawn(1), when: PawnsRegionLive);
            Claim("startingPawns.contextMenu", e => ActivateContextMenu(), when: PawnsRegionLive);
            // Claimed from every region, like the creation flow's other toolbar shortcuts.
            Claim("startingPawns.confirm", e => ActivateConfirm());
            // tree.jumpToPreviousSection/tree.jumpToNextSection are deliberately NOT claimed: Page
            // Up/Down already belong to previousPawn/nextPawn.

            if (context == PawnEditorContext.GameStart)
            {
                Claim(SharedMenuGrammar.Cancel, e =>
                {
                    ShellFrameStamps.MarkCancelConsumed();
                    StartingPawnState.RequestBackConfirm();
                }, when: () => !TypeaheadHasActiveSearch);
            }

            RegisterPopTeardown(nameEditSession.CancelIfActive);
        }

        public override string Name
        {
            get { return "starting-pawns"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return host; }
        }

        /// <summary>
        /// The pawn the cursor rests on, climbing up from any row; the hosts' curPawnIndex mirrors
        /// this so the portrait/skills area renders the right pawn. Outside the Pawns region, and on
        /// a group row, the last resolved pawn sticks — a 0 fallback would snap the portrait back to
        /// the first pawn, which vanilla's own curPawnIndex never does.
        /// </summary>
        internal int CurrentPawnIndex
        {
            get
            {
                RefreshModel();
                int resolved = PawnIndexFor(CurrentTreeItem());
                if (resolved >= 0)
                {
                    lastResolvedPawnIndex = resolved;
                }
                return lastResolvedPawnIndex;
            }
        }

        /// <summary>
        /// The pawn a node belongs to: its own PawnIndex, else the nearest ancestor's. -1 for a
        /// group header (which carries PawnIndex -1) and for a cursor outside the tree region.
        /// </summary>
        private static int PawnIndexFor(InspectionTreeItem item)
        {
            for (InspectionTreeItem node = item; node != null; node = node.Parent)
            {
                PawnTreeData ptd = StartingPawnHelper.GetPawnData(node);
                if (ptd != null && ptd.PawnIndex >= 0)
                {
                    return ptd.PawnIndex;
                }
            }
            return -1;
        }

        private int lastResolvedPawnIndex;

        /// <summary>The content draws non-toolbar buttons (xenotype editor, "+", per-row Delete), so the toolbar is declared instead.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        /// <summary>
        /// Pawns is always activeRegions[0], so the base's TreeRegionIndex of 0 holds. The other two
        /// regions are matched by KIND, never by a fixed index — Page options is present only while
        /// it has rows.
        /// </summary>
        private bool PawnsRegionLive()
        {
            RefreshModel();
            return Model.RegionIndex == 0 && Tree.Count > 0;
        }

        private bool WandererPawnsRegionLive()
        {
            return PawnsRegionLive() && context == PawnEditorContext.Wanderer;
        }

        // Content model. The tree is NOT rebuilt here (see the class remarks) — only the two
        // non-tree regions are re-derived every cycle, and the tree is built lazily once.

        protected override void RefreshContent()
        {
            // Non-negotiable: this is where the base restores pre-search expansion state.
            base.RefreshContent();

            RefreshPageOptionsRows();
            teamSkillRows.Clear();
            teamSkillRows.AddRange(TeamSkillSummaryBuilder.BuildTeamSkillSummary());

            activeRegions.Clear();
            activeRegions.Add(RegionKind.Pawns);
            if (pageOptionRows.Count > 0) activeRegions.Add(RegionKind.PageOptions);
            activeRegions.Add(RegionKind.TeamSkills);

            if (Tree.Root == null)
            {
                BuildTreeFresh();
            }
        }

        private void BuildTreeFresh()
        {
            SetTreeRoot(StartingPawnHelper.BuildTree(JumpToRelatedPawn, RebuildTreePreservingState));
        }

        /// <summary>
        /// Rebuilds the tree after a mutation, keeping expansion state and landing the cursor on the
        /// same logical node. Identity survives through <see cref="PawnTreeData"/>'s value equality
        /// for pawn/category rows (their labels are not stable) and through the label path for the
        /// two group nodes. A vanished node clamps the cursor numerically instead. Never announces —
        /// the caller owns the one utterance.
        /// </summary>
        private void RebuildTreePreservingState()
        {
            RefreshModel();
            ListModel region = Model.Region(0);
            int before = region != null && !region.IsEmpty ? region.Index : 0;
            int restored = SetTreeRootPreservingState(
                StartingPawnHelper.BuildTree(JumpToRelatedPawn, RebuildTreePreservingState), before);
            RefreshModel();
            ListModel after = Model.Region(0);
            if (after != null && !after.IsEmpty)
            {
                after.MoveTo(Mathf.Clamp(restored >= 0 ? restored : before, 0, after.Count - 1));
            }
        }

        private void RefreshPageOptionsRows()
        {
            pageOptionRows.Clear();
            if (context != PawnEditorContext.GameStart || !(host is Page_ConfigureStartingPawns page))
                return;

            if (ModsConfig.IdeologyActive)
            {
                pageOptionRows.Add(new PageOptionRow { Kind = PageOptionKind.ShowHeadgear });
                pageOptionRows.Add(new PageOptionRow { Kind = PageOptionKind.ShowApparel });
            }
            if (ModsConfig.BiotechActive)
            {
                pageOptionRows.Add(new PageOptionRow { Kind = PageOptionKind.XenotypeEditor });
            }
            var report = (AcceptanceReport)ExtraCanDoNextReportProperty.GetValue(page, null);
            if (!report.Accepted && !string.IsNullOrEmpty(report.Reason))
            {
                pageOptionRows.Add(new PageOptionRow { Kind = PageOptionKind.Warning, WarningText = report.Reason });
            }
        }

        protected override string TreeRegionLabel
        {
            get { return (string)"RimWorldAccess.StartingPawn.PawnsRegion".Translate(); }
        }

        protected override int ContentRegionCount
        {
            get { return activeRegions.Count; }
        }

        protected override string ContentRegionName(int region)
        {
            switch (activeRegions[region])
            {
                case RegionKind.Pawns: return base.ContentRegionName(0);
                case RegionKind.PageOptions: return "RimWorldAccess.StartingPawn.PageOptionsRegion".Translate();
                default: return "TeamSkills".Translate();
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (activeRegions[region])
            {
                case RegionKind.Pawns: return base.ContentItemCount(0);
                case RegionKind.PageOptions: return pageOptionRows.Count;
                default: return teamSkillRows.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch (activeRegions[region])
            {
                case RegionKind.Pawns: return base.DescribeContentItem(0, index);
                case RegionKind.PageOptions: return DescribePageOptionRow(index);
                default: return DescribeTeamSkillRow(index);
            }
        }

        // Pawns region: the tree hooks. PositionIndex is deliberately left unset — the base fills
        // it, sibling-relative.

        /// <summary>True in GameStart mode, where the roster hangs under the two group nodes.</summary>
        private bool HasGroupNodes
        {
            get { return context == PawnEditorContext.GameStart; }
        }

        /// <summary>
        /// Spoken tree level. GameStart's group nodes sit at IndentLevel 0, so IndentLevel+1 is
        /// correct there; Wanderer mode has no group headers but the shared builder still starts
        /// pawns at IndentLevel 1, so its levels shift down by one.
        /// </summary>
        private int SpokenLevel(InspectionTreeItem item)
        {
            return HasGroupNodes ? item.IndentLevel + 1 : item.IndentLevel;
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            PawnTreeData ptd = StartingPawnHelper.GetPawnData(item);
            var d = new ElementDescription { Level = SpokenLevel(item) };
            if (ptd == null)
            {
                // Every node the builder makes carries PawnTreeData; anything else reads as text.
                d.Label = item.Label;
                return d;
            }

            switch (ptd.NodeType)
            {
                case PawnNodeType.GroupHeader:
                {
                    // The expansion suffix is the ONE channel carrying the child count, so Role and
                    // Expanded stay unset and the state word is spoken once.
                    d.Label = item.Label + TreeNavigationHelper.FormatExpansionSuffix(item, includeChildCount: true);
                    return d;
                }
                case PawnNodeType.Pawn:
                {
                    d.Label = item.ExpandedLabel ?? item.Label;
                    d.Role = ElementRole.TreeItem;
                    d.Expanded = item.IsExpanded;
                    if (!item.IsExpanded)
                    {
                        // Computed from the live pawn, not the build-time node.Label, so the
                        // collapsed summary can never go stale against the pawn's current state.
                        Pawn livePawn = StartingPawnHelper.GetPawnAtIndex(ptd.PawnIndex);
                        d.Extras = StartingPawnHelper.BuildCollapsedPawnLabel(livePawn, null);
                    }
                    AppendInspectableHint(d, ptd);
                    return d;
                }
                case PawnNodeType.Category:
                {
                    d.Label = item.Label;
                    if (item.IsExpandable)
                    {
                        d.Role = ElementRole.TreeItem;
                        d.Expanded = item.IsExpanded;
                        if (!item.IsExpanded)
                        {
                            string summary = StartingPawnHelper.GetCategorySummary(item);
                            if (!string.IsNullOrEmpty(summary)) d.Extras = summary;
                        }
                    }
                    else
                    {
                        d.Role = ElementRole.None;
                        d.ReadOnly = true;
                    }
                    return d;
                }
                default: // Leaf
                {
                    switch (ptd.LeafKind)
                    {
                        case PawnLeafKind.NameField:
                        {
                            d.Label = item.Label;
                            d.Role = ElementRole.TextField;
                            string value = CurrentNameValue(ptd.PawnIndex, ptd.NameField.Value);
                            d.Value = value;
                            d.ValueBlank = string.IsNullOrEmpty(value);
                            d.Extras = item.Tooltip;
                            return d;
                        }
                        case PawnLeafKind.DevStageCombo:
                        case PawnLeafKind.XenotypeCombo:
                        {
                            d.Label = item.Label;
                            d.Role = ElementRole.ComboBox;
                            d.Value = ptd.ValueText;
                            d.Extras = item.Tooltip;
                            return d;
                        }
                        case PawnLeafKind.InfoAction:
                        {
                            d.Label = item.Label;
                            d.Extras = item.Tooltip;
                            if (ptd.Activate != null)
                            {
                                d.Role = ElementRole.Button;
                            }
                            else
                            {
                                d.Role = ElementRole.None;
                                d.ReadOnly = true;
                            }
                            AppendInspectableHint(d, ptd);
                            return d;
                        }
                        default: // PlainText
                        {
                            d.Label = item.Label;
                            d.Role = ElementRole.None;
                            d.ReadOnly = true;
                            d.Extras = item.Tooltip;
                            AppendInspectableHint(d, ptd);
                            return d;
                        }
                    }
                }
            }
        }

        private static void AppendInspectableHint(ElementDescription d, PawnTreeData ptd)
        {
            if (InspectableSubjectFor(ptd) != null)
            {
                d.Extras = (d.Extras ?? "") + (string)"RimWorldAccess.InfoCard.Inspectable".Translate();
            }
        }

        /// <summary>
        /// The subject vanilla's own info-card button on this row opens a card for, or null when
        /// vanilla draws no such button (Alt+I must then stay silent rather than fabricate a card):
        /// the pawn itself, a health condition, or a possession's ThingDef. The pawn is read live
        /// from <see cref="StartingPawnHelper.GetPawnAtIndex"/>, never the node's build-time
        /// DomainData, because a randomize replaces the Pawn instance. A leaf whose DomainData is a
        /// Pawn (a Relations row) is deliberately NOT inspectable — the vanilla relations panel draws
        /// no info-card button.
        /// </summary>
        private static object InspectableSubjectFor(PawnTreeData ptd)
        {
            if (ptd == null) return null;
            if (ptd.NodeType == PawnNodeType.Pawn) return StartingPawnHelper.GetPawnAtIndex(ptd.PawnIndex);
            if (ptd.DomainData is Def def) return def;
            if (ptd.DomainData is ThingDefCount tdc) return tdc.ThingDef;
            if (ptd.DomainData is Hediff hediff) return hediff;
            return null;
        }

        private string CurrentNameValue(int pawnIndex, NameFieldKind field)
        {
            Pawn pawn = StartingPawnHelper.GetPawnAtIndex(pawnIndex);
            if (!(pawn?.Name is NameTriple name)) return "";
            switch (field)
            {
                case NameFieldKind.First: return name.First ?? "";
                case NameFieldKind.Nick: return name.Nick ?? "";
                default: return name.Last ?? "";
            }
        }

        /// <summary>Left/Right in the Pawns region belong entirely to the tree; combo leaves are Enter-open pickers.</summary>
        protected override bool CanAdjustContentItem(int region, int index)
        {
            return activeRegions[region] == RegionKind.Pawns && base.CanAdjustContentItem(0, index);
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (activeRegions[region] == RegionKind.Pawns)
            {
                base.AdjustContentItem(0, index, direction);
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (activeRegions[region])
            {
                case RegionKind.Pawns: base.ActivateContentItem(0, index); break;
                case RegionKind.PageOptions: ActivatePageOptionRow(index); break;
                default: AnnounceCurrentItem(); break; // Team skills: read-only
            }
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            PawnTreeData ptd = StartingPawnHelper.GetPawnData(item);
            if (ptd == null || ptd.NodeType != PawnNodeType.Leaf)
            {
                if (item.IsExpandable)
                {
                    PerformActivateExpandToggle(item);
                    return;
                }
                AnnounceCurrentItem();
                return;
            }
            switch (ptd.LeafKind)
            {
                case PawnLeafKind.NameField:
                    BeginNameEdit(ptd.PawnIndex, ptd.NameField.Value, item);
                    break;
                case PawnLeafKind.DevStageCombo:
                case PawnLeafKind.XenotypeCombo:
                    ptd.OpenComboPicker?.Invoke();
                    break;
                case PawnLeafKind.InfoAction:
                    if (ptd.Activate != null) ptd.Activate();
                    else AnnounceCurrentItem();
                    break;
                default:
                    AnnounceCurrentItem();
                    break;
            }
        }

        /// <summary>
        /// True for a Pawns-region row whose Enter does nothing real (a plain-text leaf, a dead
        /// InfoAction), so it offers the proceed button through the double-press confirm instead of
        /// a silent re-announce.
        /// </summary>
        protected override bool DefaultAcceptRowInert(int region, int row)
        {
            if (region < 0 || region >= activeRegions.Count || activeRegions[region] != RegionKind.Pawns)
                return false;
            InspectionTreeItem item = TreeNodeAt(row);
            if (item == null || item.IsExpandable)
                return false;
            PawnTreeData ptd = StartingPawnHelper.GetPawnData(item);
            if (ptd == null || ptd.NodeType != PawnNodeType.Leaf)
                return false;
            switch (ptd.LeafKind)
            {
                case PawnLeafKind.PlainText:
                    return true;
                case PawnLeafKind.InfoAction:
                    return ptd.Activate == null;
                default:
                    return false;
            }
        }

        /// <summary>The two group nodes open on the first typed character so the pawns inside them stay reachable; a pawn's own card is not force-opened.</summary>
        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            PawnTreeData ptd = StartingPawnHelper.GetPawnData(item);
            return ptd != null && ptd.NodeType == PawnNodeType.GroupHeader;
        }

        /// <summary>
        /// Leaf rows stay navigable and spoken but out of the match set, keeping typeahead at the
        /// group/pawn/category level rather than burying targets under every stat and hediff line.
        /// </summary>
        protected override bool ContentRowSearchable(int region, int row)
        {
            if (activeRegions[region] != RegionKind.Pawns)
            {
                return base.ContentRowSearchable(region, row);
            }
            PawnTreeData ptd = StartingPawnHelper.GetPawnData(TreeNodeAt(row));
            return ptd == null || ptd.NodeType != PawnNodeType.Leaf;
        }

        /// <summary>
        /// Typeahead matches a node's identity text, not its composed announcement: a group's spoken
        /// label carries the expansion suffix and child count, and a pawn's raw Label is the verbose
        /// summary while its ExpandedLabel is the name and title people search by.
        /// </summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            if (activeRegions[region] == RegionKind.Pawns)
            {
                InspectionTreeItem item = TreeNodeAt(row);
                if (item != null)
                {
                    return item.ExpandedLabel ?? item.Label ?? "";
                }
            }
            return base.ContentRowSearchText(region, row);
        }

        /// <summary>The tree node behind a Pawns-region row index, or null when the index is out of range.</summary>
        private InspectionTreeItem TreeNodeAt(int row)
        {
            int treeIndex = row - PrefixRowCount;
            return treeIndex >= 0 && treeIndex < Tree.Count ? Tree.Visible[treeIndex] : null;
        }

        private void BeginNameEdit(int pawnIndex, NameFieldKind field, InspectionTreeItem node)
        {
            editingLeafPawnIndex = pawnIndex;
            editingNameField = field;
            int maxLength = field == NameFieldKind.Nick ? 16 : 12;
            var spec = new TextFieldSpec(
                labelKey: "RimWorldAccess.TextInput.LabelDefault",
                maxLength: maxLength,
                minLength: field == NameFieldKind.Nick ? 0 : 1,
                allowedChars: ValidNameRegex);
            string current = CurrentNameValue(pawnIndex, field);
            nameEditSession.EnterEdit(current, spec, node.Label, ApplyNameEdit, FinishNameEdit);
        }

        private static readonly System.Text.RegularExpressions.Regex ValidNameRegex =
            new System.Text.RegularExpressions.Regex("^[\\p{L}0-9 '\\-.]*$");

        private void ApplyNameEdit(string value)
        {
            Pawn pawn = StartingPawnHelper.GetPawnAtIndex(editingLeafPawnIndex);
            if (!(pawn?.Name is NameTriple name)) return;
            // MUTATION-C: mirrors CharacterCardUtility.DoNameInputRect (CharacterCardUtility.cs:1441-1448)
            // exactly — the length/regex gate is enforced by TextFieldSpec/TextInputController before this
            // callback runs, so this just composes the new NameTriple the same way vanilla's ref-assign does.
            switch (editingNameField)
            {
                case NameFieldKind.First: pawn.Name = new NameTriple(value, name.Nick, name.Last); break;
                case NameFieldKind.Nick: pawn.Name = new NameTriple(name.First, value, name.Last); break;
                default: pawn.Name = new NameTriple(name.First, name.Nick, value); break;
            }
        }

        private void FinishNameEdit()
        {
            editingLeafPawnIndex = -1;
            // Category labels and the collapsed summary are built from the name, so rebuild first.
            RebuildTreePreservingState();
            AnnounceCurrentItem();
        }

        private void JumpToRelatedPawn(Pawn target)
        {
            var pawns = Find.GameInitData?.startingAndOptionalPawns;
            int targetIdx = pawns?.IndexOf(target) ?? -1;
            if (targetIdx < 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            // Collapse the pawn we're leaving so the jump lands in a clean top-level list.
            int sourceIdx = CurrentPawnIndex;
            if (sourceIdx != targetIdx)
            {
                CollapsePawnNode(FindPawnNode(sourceIdx));
            }
            CollapsePawnNode(FindPawnNode(targetIdx));
            RevealAndSync(FindPawnNode(targetIdx));
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        private static void CollapsePawnNode(InspectionTreeItem pawnNode)
        {
            if (pawnNode == null) return;
            pawnNode.IsExpanded = false;
            for (int i = 0; i < pawnNode.Children.Count; i++)
            {
                pawnNode.Children[i].IsExpanded = false;
            }
        }

        /// <summary>Every pawn node in roster order: the group nodes' children in GameStart mode, the root's children in Wanderer mode.</summary>
        private IEnumerable<InspectionTreeItem> EnumeratePawnNodes()
        {
            if (Tree.Root == null)
            {
                yield break;
            }
            foreach (InspectionTreeItem child in Tree.Root.Children)
            {
                PawnTreeData ptd = StartingPawnHelper.GetPawnData(child);
                if (ptd != null && ptd.NodeType == PawnNodeType.Pawn)
                {
                    yield return child;
                    continue;
                }
                foreach (InspectionTreeItem grandChild in child.Children)
                {
                    PawnTreeData childPtd = StartingPawnHelper.GetPawnData(grandChild);
                    if (childPtd != null && childPtd.NodeType == PawnNodeType.Pawn)
                    {
                        yield return grandChild;
                    }
                }
            }
        }

        private InspectionTreeItem FindPawnNode(int pawnIndex)
        {
            foreach (InspectionTreeItem node in EnumeratePawnNodes())
            {
                PawnTreeData ptd = StartingPawnHelper.GetPawnData(node);
                if (ptd != null && ptd.PawnIndex == pawnIndex)
                {
                    return node;
                }
            }
            return null;
        }

        private InspectionTreeItem FindCategoryNode(int pawnIndex, PawnCategoryType category)
        {
            InspectionTreeItem pawnNode = FindPawnNode(pawnIndex);
            if (pawnNode == null)
            {
                return null;
            }
            foreach (InspectionTreeItem child in pawnNode.Children)
            {
                PawnTreeData ptd = StartingPawnHelper.GetPawnData(child);
                if (ptd != null && ptd.NodeType == PawnNodeType.Category && ptd.CategoryType == category)
                {
                    return child;
                }
            }
            return null;
        }

        /// <summary>
        /// Lands the region cursor on <paramref name="node"/>, re-expanding ancestors so it is
        /// reachable. Silent — the caller owns the announcement. RefreshModel runs between reveal and
        /// sync so the row count already reflects the reflattened tree; otherwise a move to a row
        /// past the old count is dropped.
        /// </summary>
        private void RevealAndSync(InspectionTreeItem node)
        {
            if (node == null || !TryRevealAndSelect(node))
            {
                return;
            }
            RefreshModel();
            SyncRegionFromCurrentTree();
        }

        /// <summary>
        /// Vanilla's own <c>DrawOptionBackground(curPawnIndex == num4)</c> highlight covers the focus
        /// ring, so this only keeps the focused row inside DrawPawnList's scroll view.
        /// Dialog_ChooseNewWanderers is excluded structurally — it draws no scroll view, so
        /// <see cref="capturedVisibleHeightFrame"/> is never written there and this self-heals to a
        /// no-op.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            if (region < 0 || region >= activeRegions.Count || activeRegions[region] != RegionKind.Pawns)
            {
                return;
            }
            FollowPawnStripIntoView();
        }

        private void FollowPawnStripIntoView()
        {
            if (capturedVisibleHeightFrame < 0 || !(host is Page_ConfigureStartingPawns page))
            {
                return;
            }
            int pawnIdx = PawnIndexFor(CurrentTreeItem());
            if (pawnIdx < 0)
            {
                return;
            }
            // Mirrors DrawPawnList's row accumulation: a 22f label precedes the list, plus another
            // 22f once the "left behind" divider row is crossed.
            float top = 22f + pawnIdx * 60f
                + (pawnIdx >= Find.GameInitData.startingPawnCount ? 22f : 0f);
            Vector2 scroll = ScrollField(page);
            scroll.y = Mathf.Clamp(scroll.y, top + 60f - capturedVisibleHeight, top);
            ScrollField(page) = scroll;
        }

        /// <summary>The pawn the cursor is inside, or -1 on a group row or outside the tree.</summary>
        private int ResolveCurrentPawnIndex()
        {
            RefreshModel();
            return PawnIndexFor(CurrentTreeItem());
        }

        private void ActivateRandomize()
        {
            int pawnIdx = ResolveCurrentPawnIndex();
            if (pawnIdx < 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            StartingPawnState.RandomizePawnAt(pawnIdx);
        }

        private void ActivateRename()
        {
            int pawnIdx = ResolveCurrentPawnIndex();
            if (pawnIdx < 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            StartingPawnState.RenamePawnAt(pawnIdx);
        }

        private void ActivateContextMenu()
        {
            int pawnIdx = ResolveCurrentPawnIndex();
            StartingPawnState.OpenContextMenuFor(pawnIdx, RefreshAndReannounceAfterExternalChange);
        }

        private void ActivateAddPawn()
        {
            if (StartingPawnState.AddWandererPawn(out string announcement))
            {
                RebuildTreePreservingState();
                RevealAndSync(FindPawnNode(StartingPawnHelper.GetPawnCount() - 1));
                TolkHelper.SpeakData(announcement);
                AnnounceCurrentItem();
            }
        }

        private void ActivateRemovePawn()
        {
            int pawnIdx = ResolveCurrentPawnIndex();
            if (pawnIdx < 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            if (StartingPawnState.RemoveWandererPawn(pawnIdx, out string announcement))
            {
                // The removed node is gone, so the preserver's numeric clamp lands the cursor on the
                // row that took its place, or the new last row.
                RebuildTreePreservingState();
                TolkHelper.SpeakData(announcement);
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// Ctrl+Up/Ctrl+Down. The group boundary never constrains this;
        /// <see cref="StartingPawnState.ReorderPawnAt"/> speaks the group crossed into, so the tree
        /// just re-finds the pawn at its new index.
        /// </summary>
        private void ActivateReorder(int direction)
        {
            int pawnIdx = ResolveCurrentPawnIndex();
            if (pawnIdx < 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            if (StartingPawnState.ReorderPawnAt(pawnIdx, direction, out string announcement, out int newIndex))
            {
                RebuildTreePreservingState();
                RevealAndSync(FindPawnNode(newIndex));
                TolkHelper.SpeakData(announcement);
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// Page Up/Down: the same row on the previous/next pawn, carrying the current pawn's (and
        /// category's) expansion state onto the target. No rebuild — only expansion flags move.
        /// </summary>
        private void SwitchPawn(int direction)
        {
            RefreshModel();
            InspectionTreeItem current = CurrentTreeItem();
            int currentPawnIdx = PawnIndexFor(current);
            if (currentPawnIdx < 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            PawnTreeData ptd = StartingPawnHelper.GetPawnData(current);
            PawnCategoryType? category = ptd != null ? ptd.CategoryType : null;
            InspectionTreeItem currentPawnNode = FindPawnNode(currentPawnIdx);
            bool wasPawnExpanded = currentPawnNode != null && currentPawnNode.IsExpanded;
            InspectionTreeItem currentCategoryNode = category.HasValue
                ? FindCategoryNode(currentPawnIdx, category.Value)
                : null;
            bool wasCategoryExpanded = currentCategoryNode != null && currentCategoryNode.IsExpanded;

            // A leaf's row within its category, so the same row lands on the target pawn: skill
            // lists share one order across pawns, so the index maps like-for-like. -1 when the
            // cursor rests on the pawn or category node itself.
            int leafIndex = ptd != null && ptd.NodeType == PawnNodeType.Leaf && currentCategoryNode != null
                ? currentCategoryNode.Children.IndexOf(current)
                : -1;

            int targetIdx = currentPawnIdx + direction;
            InspectionTreeItem targetPawn = targetIdx >= 0 ? FindPawnNode(targetIdx) : null;
            if (targetPawn == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            targetPawn.IsExpanded = wasPawnExpanded;
            InspectionTreeItem targetCategory = category.HasValue
                ? FindCategoryNode(targetIdx, category.Value)
                : null;
            if (targetCategory != null && targetCategory.IsExpandable)
            {
                targetCategory.IsExpanded = wasCategoryExpanded;
            }

            // Land on the same leaf when the target category holds one at that index, else on the
            // category (or pawn) node the reveal already resolves.
            InspectionTreeItem targetNode = targetCategory ?? targetPawn;
            if (targetCategory != null && leafIndex >= 0 && leafIndex < targetCategory.Children.Count)
            {
                targetNode = targetCategory.Children[leafIndex];
            }

            RevealAndSync(targetNode);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        private void OnInfo(KeyEventSnapshot e)
        {
            RefreshModel();
            PawnTreeData ptd = StartingPawnHelper.GetPawnData(CurrentTreeItem());
            object subject = InspectableSubjectFor(ptd);
            if (subject == null)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            if (subject is Def def)
            {
                InfoCardState.OpenInfoCardForDef(def);
                return;
            }
            if (subject is Hediff hediff)
            {
                // The card vanilla's per-hediff info-card button opens.
                Find.WindowStack.Add(new Dialog_InfoCard(hediff));
                return;
            }
            if (subject is Pawn pawn)
            {
                // Vanilla's character-card info button passes the pawn as a Thing.
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));
            }
        }

        private ElementDescription DescribePageOptionRow(int index)
        {
            if (index < 0 || index >= pageOptionRows.Count) return new ElementDescription();
            PageOptionRow row = pageOptionRows[index];
            var page = host as Page_ConfigureStartingPawns;
            var d = new ElementDescription();
            switch (row.Kind)
            {
                case PageOptionKind.ShowHeadgear:
                    d.Label = "ShowHeadgear".Translate();
                    d.Role = ElementRole.Checkbox;
                    d.Check = page != null && RenderHeadgearField(page) ? CheckState.Checked : CheckState.Unchecked;
                    return d;
                case PageOptionKind.ShowApparel:
                    d.Label = "ShowApparel".Translate();
                    d.Role = ElementRole.Checkbox;
                    d.Check = page != null && RenderClothesField(page) ? CheckState.Checked : CheckState.Unchecked;
                    return d;
                case PageOptionKind.XenotypeEditor:
                    d.Label = "XenotypeEditor".Translate();
                    d.Role = ElementRole.Button;
                    return d;
                default:
                    d.Label = row.WarningText;
                    d.Role = ElementRole.None;
                    d.ReadOnly = true;
                    return d;
            }
        }

        private void ActivatePageOptionRow(int index)
        {
            if (index < 0 || index >= pageOptionRows.Count) return;
            PageOptionRow row = pageOptionRows[index];
            var page = host as Page_ConfigureStartingPawns;
            switch (row.Kind)
            {
                case PageOptionKind.ShowHeadgear:
                    if (page != null)
                    {
                        // MUTATION-C: mirrors Page_ConfigureStartingPawns.DrawApparelOptions'
                        // CheckboxLabeled write (decompiled body, no gated setter for the private field).
                        RenderHeadgearField(page) = !RenderHeadgearField(page);
                        AnnounceCurrentCellStateChangeSimple(RenderHeadgearField(page));
                    }
                    break;
                case PageOptionKind.ShowApparel:
                    if (page != null)
                    {
                        // MUTATION-C: mirrors Page_ConfigureStartingPawns.DrawApparelOptions'
                        // CheckboxLabeled write (decompiled body, no gated setter for the private field).
                        RenderClothesField(page) = !RenderClothesField(page);
                        AnnounceCurrentCellStateChangeSimple(RenderClothesField(page));
                    }
                    break;
                case PageOptionKind.XenotypeEditor:
                    int pawnIdx = CurrentPawnIndex;
                    Find.WindowStack.Add(new Dialog_CreateXenotype(pawnIdx, delegate
                    {
                        // Vanilla's callback regenerates the pawn to carry the chosen xenotype; the
                        // reroll is announced rather than left silent under a cursor on the button.
                        CharacterCardUtility.cachedCustomXenotypes = null;
                        StartingPawnUtility.RandomizePawn(pawnIdx);
                        RebuildTreePreservingState();
                        AnnounceRegeneratedPawn(pawnIdx);
                    }));
                    break;
                default:
                    AnnounceCurrentItem();
                    break;
            }
        }

        private void AnnounceCurrentCellStateChangeSimple(bool @checked)
        {
            var d = new ElementDescription { Check = @checked ? CheckState.Checked : CheckState.Unchecked };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private ElementDescription DescribeTeamSkillRow(int index)
        {
            if (index < 0 || index >= teamSkillRows.Count) return new ElementDescription();
            return new ElementDescription { Label = teamSkillRows[index], Role = ElementRole.None, ReadOnly = true };
        }

        /// <summary>Start/Confirm is this screen's proceed button — same id in both contexts, only the label differs.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "startingPawns.confirm"; }
        }

        /// <summary>The proceed action, one vehicle for its toolbar row, its Alt+S chord and the Enter double-press seam.</summary>
        private void ActivateConfirm()
        {
            if (context == PawnEditorContext.GameStart)
            {
                StartingPawnState.ConfirmStartGame();
            }
            else
            {
                StartingPawnState.ConfirmWandererStart();
            }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                if (context == PawnEditorContext.GameStart)
                {
                    actions.Add(new ScreenAction("Back".Translate(), () => StartingPawnState.RequestBackConfirm(), SharedMenuGrammar.Cancel));
                    actions.Add(new ScreenAction("Start".Translate(), ActivateConfirm, "startingPawns.confirm"));
                }
                else
                {
                    actions.Add(new ScreenAction("Confirm".Translate(), ActivateConfirm, "startingPawns.confirm"));
                    // The 6-pawn cap mirrors vanilla's own add-button gate.
                    if (StartingPawnHelper.GetPawnCount() < 6)
                    {
                        actions.Add(new ScreenAction("RimWorldAccess.StartingPawn.AddPawn".Translate(), ActivateAddPawn, "startingPawns.addPawn"));
                    }
                }
                return actions;
            }
        }

        /// <summary>
        /// Folds into the captured-extras diff the strings this scope presents some other way, or
        /// that vanilla draws unconditionally without a row-for-row mirror here.
        /// The collapsed-state trap: BuildPresentedHaystacks only scans rows currently reachable
        /// through ContentItemCount/DescribeContentItem, i.e. only what this scope has expanded,
        /// while vanilla draws the selected pawn's whole character card and every pawn's list column
        /// regardless. So these entries read off the whole built tree (<see cref="Tree"/>'s root,
        /// always populated in full) rather than the expansion-filtered visible list.
        /// </summary>
        protected override IEnumerable<string> AdditionalPresentedTexts
        {
            get
            {
                // A group row speaks its label WITH the expansion suffix, so its announcement is not
                // a superstring of the bare vanilla header these yields cover.
                yield return (string)"StartingPawnsSelected".Translate();
                yield return (string)"StartingPawnsLeftBehind".Translate();
                yield return (string)"DragToReorder".Translate();

                // Controls this scope mirrors through a hotkey rather than a row, so their vanilla
                // labels never appear in DescribeContentItem output: Randomize (Alt+R) and the
                // info-card buttons (Alt+I). Every info-card button captures under ONE accessible
                // name, so all of them fold to one indistinguishable extras row; vouching for that
                // name is honest only because Alt+I reaches all three subjects from their own typed
                // rows (see InspectableSubjectFor). "InfoButton" is the texture-asset fallback, kept
                // so a capture that misses the label bracket still folds away.
                yield return (string)"Randomize".Translate();
                yield return (string)"RimWorldAccess.UI.GenericWindow.InfoCardButton".Translate();
                yield return "InfoButton";

                // BuildPresentedHaystacks folds in a region's ITEMS but never its header, so the
                // Team Skills header needs its own entry.
                yield return (string)"TeamSkills".Translate();

                if (Tree.Root == null) yield break;

                // Pawn-list column: every pawn's portrait/name/title is drawn, not just the selected
                // one. A long title renders as story.TitleShortCap, which is not a substring of the
                // full title, so both forms need entries.
                foreach (InspectionTreeItem pawnNode in EnumeratePawnNodes())
                {
                    if (!string.IsNullOrEmpty(pawnNode.ExpandedLabel))
                    {
                        yield return pawnNode.ExpandedLabel;
                    }
                    PawnTreeData listPtd = StartingPawnHelper.GetPawnData(pawnNode);
                    Pawn listPawn = listPtd == null ? null : StartingPawnHelper.GetPawnAtIndex(listPtd.PawnIndex);
                    if (listPawn?.story != null)
                    {
                        yield return listPawn.story.TitleShortCap;
                    }
                }

                // A hediff-free pawn contributes no Health leaf text containing vanilla's centered
                // empty state, so it needs an unconditional entry.
                yield return "(" + "NoHealthConditions".Translate() + ")";

                // Vanilla draws only the selected pawn's card but draws ALL of it, whatever this
                // scope has expanded — so walk that one pawn's full tree, unfiltered by expansion.
                foreach (InspectionTreeItem pawnNode in EnumeratePawnNodes())
                {
                    PawnTreeData pawnPtd = StartingPawnHelper.GetPawnData(pawnNode);
                    if (pawnPtd == null || pawnPtd.PawnIndex != lastResolvedPawnIndex)
                    {
                        continue;
                    }
                    foreach (InspectionTreeItem catNode in pawnNode.Children)
                    {
                        if (!string.IsNullOrEmpty(catNode.Label))
                        {
                            yield return catNode.Label;
                        }
                        foreach (InspectionTreeItem leafNode in catNode.Children)
                        {
                            if (!string.IsNullOrEmpty(leafNode.Label))
                            {
                                yield return leafNode.Label;
                            }
                            if (!string.IsNullOrEmpty(leafNode.Tooltip))
                            {
                                yield return leafNode.Tooltip;
                            }
                            PawnTreeData leafPtd = StartingPawnHelper.GetPawnData(leafNode);
                            if (!string.IsNullOrEmpty(leafPtd?.ValueText))
                            {
                                yield return leafPtd.ValueText;
                            }
                        }
                    }
                    break;
                }

                // Vanilla fuses several widgets into ONE captured row, and a fused row is judged as
                // one contiguous string, so these composites must follow vanilla's draw order and
                // format — the per-leaf walk above only covers each widget's text separately.
                Pawn selectedPawn = StartingPawnHelper.GetPawnAtIndex(lastResolvedPawnIndex);
                if (selectedPawn != null)
                {
                    foreach (string composite in BuildVanillaCardFusedComposites(selectedPawn))
                    {
                        yield return composite;
                    }
                }
            }
        }

        /// <summary>
        /// The fused vanilla composites, each rebuilt in vanilla's own draw order: the main-desc chip
        /// fused with the xenotype chip (writeGender is FALSE under Biotech, where a separate
        /// gender-icon chip covers it); the skill grid column-fused as all labels in SkillUI order,
        /// then each enabled skill's fill-bar percent, then every level or "-" when totally disabled;
        /// and one row per relation as "{relation}: {full name}, {my opinion}, ({their opinion})"
        /// (SocialCardUtility.DrawPawnRow's non-Playing branch, which uses Name.ToStringFull).
        /// </summary>
        private static IEnumerable<string> BuildVanillaCardFusedComposites(Pawn pawn)
        {
            bool omitGenderWord = ModsConfig.BiotechActive;
            string mainDesc = pawn.MainDesc(writeFaction: false, writeGender: !omitGenderWord);
            if (ModsConfig.BiotechActive && pawn.genes != null && pawn.genes.GenesListForReading.Count > 0)
            {
                yield return mainDesc + ", " + pawn.genes.XenotypeLabelCap;
            }
            else
            {
                yield return mainDesc;
            }

            if (!pawn.DevelopmentalStage.Baby() && pawn.skills?.skills != null)
            {
                var orderedSkills = pawn.skills.skills.OrderByDescending(s => s.def.listOrder).ToList();
                var labels = new List<string>();
                var percents = new List<string>();
                var levels = new List<string>();
                foreach (SkillRecord skill in orderedSkills)
                {
                    labels.Add(skill.def.skillLabel.CapitalizeFirst());
                    if (skill.TotallyDisabled)
                    {
                        levels.Add("-");
                    }
                    else
                    {
                        float fillPercent = Mathf.Max(0.01f, skill.GetLevel() / 20f);
                        percents.Add(fillPercent.ToStringPercent());
                        levels.Add(skill.GetLevel().ToStringCached());
                    }
                }
                var parts = new List<string>();
                parts.AddRange(labels);
                parts.AddRange(percents);
                parts.AddRange(levels);
                yield return string.Join(", ", parts);
            }

            // Chargen and wanderers are never ProgramState.Playing, so DrawPawnRow's non-Playing
            // branch is the only one live here.
            if (pawn.relations != null && !pawn.relations.hidePawnRelations)
            {
                foreach (SocialTabHelper.RelationInfo info in SocialTabHelper.GetRelations(pawn))
                {
                    if (info.OtherPawn?.relations == null
                        || !info.OtherPawn.relations.everSeenByPlayer
                        || info.OtherPawn.relations.hidePawnRelations)
                    {
                        continue;
                    }
                    string relationString;
                    if (info.Relations.Count == 0)
                    {
                        relationString = info.MyOpinion < -20 ? (string)"Rival".Translate()
                            : info.MyOpinion > 20 ? (string)"Friend".Translate()
                            : (string)"Acquaintance".Translate();
                    }
                    else
                    {
                        relationString = string.Join(", ", info.Relations);
                    }
                    string otherName = info.OtherPawn.Name != null ? info.OtherPawn.Name.ToStringFull : info.OtherPawn.LabelCapNoCount;
                    yield return relationString + ": " + otherName + ", " + info.MyOpinion.ToStringWithSign() + ", (" + info.TheirOpinion.ToStringWithSign() + ")";
                }
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            Active = this;
            announcedOpen = false;
            // Drop the tree; the first RefreshContent builds it fresh for this host's roster.
            ResetTree();
            lastResolvedPawnIndex = 0;
            // Skip the scroll-follow write until a fresh capture lands for this host.
            capturedVisibleHeightFrame = -1;
        }

        public override void OnPop()
        {
            base.OnPop();
            if (ReferenceEquals(Active, this)) Active = null;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (!announcedOpen)
            {
                announcedOpen = true;
                string tabCount = TabCountFragment();
                string title = context == PawnEditorContext.Wanderer
                    ? (string)"RimWorldAccess.StartingPawn.WandererTitle".Translate((string)"ChooseNewWanderers".Translate())
                    : (string)"CreateCharacters".Translate();
                string opening = string.IsNullOrEmpty(tabCount) ? title : tabCount + ". " + title;
                TolkHelper.SpeakData(opening);
                AnnounceCurrentItem();
            }
            else if (!nameEditSession.Editing)
            {
                RefreshSilentlyAfterExternalChange();
            }
        }

        /// <summary>Rebuilds the tree and re-announces the current row after an external mutation lands.</summary>
        internal void RefreshAndReannounceAfterExternalChange()
        {
            RebuildTreePreservingState();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Speaks a regenerated pawn's own tree-node summary — the exact text navigating to that
        /// node announces — for a reroll landing while the cursor rests elsewhere (Save-and-apply
        /// in the xenotype editor). Falls back to the current row when the node is not found.
        /// </summary>
        private void AnnounceRegeneratedPawn(int pawnIdx)
        {
            InspectionTreeItem pawnNode = FindPawnNode(pawnIdx);
            if (pawnNode == null)
            {
                AnnounceCurrentItem();
                return;
            }
            string summary = AnnouncementComposer.ComposeFocus(
                DescribeTreeNode(pawnNode), TranslatedShellVocabulary.Instance,
                TextDialogShared.StandardComposeOptions());
            TolkHelper.SpeakData("RimWorldAccess.StartingPawn.XenotypeApplied".Translate(summary));
        }

        /// <summary>
        /// Silent counterpart of <see cref="RefreshAndReannounceAfterExternalChange"/> for edits by
        /// external pawn editors detected on refocus. Also fires whenever
        /// <see cref="FocusStackCore.RefocusTop"/> drives OnFocus for unrelated reasons, so it runs
        /// often and must stay cheap and silent.
        /// </summary>
        internal void RefreshSilentlyAfterExternalChange()
        {
            RebuildTreePreservingState();
        }

        /// <summary>
        /// Called from the host's DoWindowContents postfix every GUI pass to mirror a live name-edit
        /// buffer into the pawn's Name. There is no vanilla TextField to piggyback on here, so the
        /// mirror must be ticked explicitly rather than riding the captured-extras widget pass.
        /// </summary>
        internal void OnHostDrawPass()
        {
            nameEditSession.MirrorLive();
        }
    }

    /// <summary>
    /// Back-navigation guard twin. Page.DoBottomButtons' deferred Escape poll is unconditional on
    /// KeyDownEvent regardless of doNextOnKeypress, so it must be blocked while this scope owns
    /// Escape or a window-pass-first race (QA R6) runs DoBack before the scope's Cancel claim opens
    /// its confirmation dialog. No BackRequested flag is needed: the confirm dialog's accept action
    /// reflects DoBack directly, so this scope never legitimately calls the real CanDoBack while
    /// live. A mouse click on Back is unaffected — Cancel.KeyDownEvent is false for it.
    /// </summary>
    [HarmonyPatch(typeof(Page), "CanDoBack")]
    public class StartingPawnScreenScopePatch_CanDoBack
    {
        [HarmonyPrefix]
        static bool Prefix(Page __instance, ref bool __result)
        {
            if (!(__instance is Page_ConfigureStartingPawns))
                return true;
            if (KeyBindingDefOf.Cancel.KeyDownEvent && FocusStack.Top is StartingPawnScreenScope)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Shared stand-down for the pawn-overlay mirrors, keyed on the WINDOW type rather than the
    /// scope class: true while a real (non-Immediate) window sits above whichever pawn-editor host
    /// is on the stack, so a mirror's per-pass Push never re-floats a windowless overlay scope above
    /// a scoped real window and scopeless vanilla dialogs keep vanilla routing.
    /// </summary>
    internal static class PawnScopeGuards
    {
        internal static bool WindowAbovePawnHost()
        {
            WindowStack stack = Find.WindowStack;
            if (stack == null)
            {
                return false;
            }
            IList<Window> windows = stack.Windows;
            bool above = false;
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (window is Page_ConfigureStartingPawns || window is Dialog_ChooseNewWanderers)
                {
                    above = true;
                    continue;
                }
                if (above && !(window is ImmediateWindow))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
