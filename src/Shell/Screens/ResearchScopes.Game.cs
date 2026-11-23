using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// A scope that can name the research project the keyboard is on, for the real Research
    /// window's own selection and auto-scroll. Two scopes drive that one window — the tree menu
    /// and a project's detail view, which sits ON TOP of the menu while it is open — so the
    /// window asks the topmost of them rather than either one by name.
    /// </summary>
    internal interface IResearchFocusSource
    {
        ResearchProjectDef FocusedProject { get; }
    }

    /// <summary>
    /// The keyboard focus scope for the windowless research menu
    /// (<see cref="WindowlessResearchMenuState"/>). The mod owns no window here, so the scope rides
    /// the focus stack through <see cref="ResearchScopeMirror"/> rather than a WindowStack mirror.
    ///
    /// DELIBERATELY PRESERVED (do not simplify): the hand-copied <c>VisibleResearchProjects</c>
    /// filter (windowless architecture; the drift-risk comment stays on the facade); the
    /// transient-window <c>AttemptBeginResearch</c> reflection invocation (vehicle A); the full
    /// <c>CanStartNow</c> gate; the menu-only dev context menu with its sanctioned raw dev
    /// literals; <see cref="WindowlessResearchMenuState.OpenAndSelectProject"/>'s exact signature
    /// and behavior for its two external callers.
    ///
    /// Up/Down/Left/Right/Home/End/typeahead come from <see cref="TreeRegionScope"/>; only the
    /// three per-node hooks, Alt+I and the dev context menu are local. The shared <c>tree.*</c> ids
    /// are registered under this screen's own <c>"research"</c> id in
    /// <see cref="ShellActionInventory"/>, since the registry has no cross-screen id sharing.
    ///
    /// PRESERVED ASYMMETRY: Enter on a Category node is a genuine no-op here, while
    /// <see cref="ResearchDetailScope"/> toggles its own Category nodes. Preserved rather than
    /// silently unified.
    /// </summary>
    public sealed class ResearchMenuScope : TreeRegionScope, IResearchFocusSource
    {
        public ResearchMenuScope()
        {
            Claim("research.infoCard", delegate
            {
                InspectionTreeItem item = CurrentTreeItem();
                if (item != null)
                {
                    WindowlessResearchMenuState.HandleInfoCard(item);
                }
            });
            // DEV-mode debug actions on the selected project, mirroring the window's own embedded
            // debug buttons. Gated so a non-dev RightBracket falls through.
            Claim("research.contextMenu", delegate
            {
                InspectionTreeItem item = CurrentTreeItem();
                WindowlessResearchMenuState.OpenDevContextMenu(item != null ? item.Data as ResearchProjectDef : null);
            }, when: () => Prefs.DevMode);
            // TreeRegionScope leaves the shared tree.jumpTo*Section ids for subclasses to claim.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));
            // The Menu has no back-stack of its own, so Escape closes outright.
            Claim(SharedMenuGrammar.Cancel, delegate { WindowlessResearchMenuState.Close(); },
                when: () => !TypeaheadHasActiveSearch);
        }

        /// <summary>
        /// Without this the window's own GUI pass runs Window.OnCancelKeyPressed before the
        /// dispatcher and closes the whole tab, so this scope's Cancel claim never fires.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        public override string Name
        {
            get { return "research-menu"; }
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.Shell.Tree.ResearchMenuRegionName".Translate(); }
        }

        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            // Every expandable node auto-expands during search, unlike the filter family's
            // Category-only gate.
            return true;
        }

        /// <summary>
        /// Sections are the level-0 rows BuildCategoryTree emits: one per research tab, or one per
        /// status group in single-tab colonies, where the tab wrapper is skipped.
        /// </summary>
        protected override bool IsSectionBoundary(InspectionTreeItem item)
        {
            return item.IndentLevel == 0;
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription();
            d.Role = ElementRole.TreeItem;
            if (item.Type != InspectionTreeItem.ItemType.Category)
            {
                d.Label = item.Label;
                return d;
            }
            string label = item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel)
                ? item.ExpandedLabel
                : item.Label;
            d.Label = label + TreeNavigationHelper.FormatExpansionSpaceSuffix(item);
            return d;
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item.Type == InspectionTreeItem.ItemType.Item && item.Data is ResearchProjectDef project)
            {
                WindowlessResearchDetailState.Open(project);
                return;
            }
            // Category/DetailText: a deliberate no-op, per the preserved asymmetry above.
        }

        /// <summary>The project under the keyboard cursor right now; null on a category/status/"not discovered" row.</summary>
        internal ResearchProjectDef FocusedProject
        {
            get { return CurrentTreeItem()?.Data as ResearchProjectDef; }
        }

        ResearchProjectDef IResearchFocusSource.FocusedProject
        {
            get { return FocusedProject; }
        }

        // Bridge targets wired once by ResearchScopeMirror's static constructor. The facade calls
        // straight into these rather than relying on OnPush/OnFocus timing, so both entry points
        // behave identically whether or not the scope is pushed yet.

        /// <summary>Rebuilds the category tree fresh and announces the focused row.</summary>
        public void RebuildFresh()
        {
            SetTreeRoot(WindowlessResearchMenuState.BuildCategoryTree());
            RefreshModel();
            SyncRegionFromCurrentTree();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Builds the tree, expands every ancestor of the target leaf so it is not hidden inside a
        /// collapsed category, and focuses it. A project that cannot be located speaks ONLY the
        /// "not found" message: no title, no row announcement.
        /// </summary>
        public void FocusOnProject(ResearchProjectDef project)
        {
            InspectionTreeItem root = WindowlessResearchMenuState.BuildCategoryTree();
            InspectionTreeItem target = WindowlessResearchMenuState.FindProjectNode(root, project);
            if (target != null)
            {
                for (InspectionTreeItem p = target.Parent; p != null; p = p.Parent)
                {
                    if (p.IsExpandable)
                    {
                        p.IsExpanded = true;
                    }
                }
            }
            SetTreeRoot(root);
            RefreshModel();
            int idx = target != null ? Tree.IndexOf(target) : -1;
            if (idx < 0)
            {
                TolkHelper.Speak("RimWorldAccess.Research.Menu.ProjectNotFound".Loc(project.LabelCap));
                return;
            }
            Tree.SetSelectedIndex(idx);
            SyncRegionFromCurrentTree();
            TolkHelper.Speak("RimWorldAccess.Research.Menu.Title".Loc());
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Silent rebuild after research state changed in the detail view, which moves a project
        /// between the status groups. Re-homes the cursor by project identity (the reorder voids the
        /// old index) and speaks nothing, since the detail owns focus when this fires.
        /// </summary>
        public void RefreshPreservingSelection()
        {
            ResearchProjectDef keep = FocusedProject;
            InspectionTreeItem root = WindowlessResearchMenuState.BuildCategoryTree();
            InspectionTreeItem target = keep != null
                ? WindowlessResearchMenuState.FindProjectNode(root, keep)
                : null;
            for (InspectionTreeItem p = target?.Parent; p != null; p = p.Parent)
            {
                if (p.IsExpandable)
                {
                    p.IsExpanded = true;
                }
            }
            SetTreeRoot(root);
            RefreshModel();
            int idx = target != null ? Tree.IndexOf(target) : -1;
            if (idx >= 0)
            {
                Tree.SetSelectedIndex(idx);
            }
            SyncRegionFromCurrentTree();
        }
    }

    /// <summary>
    /// The keyboard focus scope for the windowless research project detail view
    /// (<see cref="WindowlessResearchDetailState"/>); same shape as
    /// <see cref="ResearchMenuScope"/>, differing in its state and per-node hooks.
    ///
    /// Both scopes are live at once: opening a detail view does NOT close the menu, and
    /// <see cref="ResearchScopeMirror"/> pushes menu first so detail lands above it.
    ///
    /// PRESERVED: <see cref="WindowlessResearchDetailState.Close"/> pops its own navigation stack
    /// and RE-OPENS the previous project whenever that stack is non-empty, deactivating only once
    /// it drains; the Cancel claim calls that same method. Enter on a Category node toggles
    /// expand/collapse here (unlike the menu), routed through the shared
    /// <c>AdjustContentItem</c> machinery so sounds, reflatten and announce match Left/Right.
    /// </summary>
    public sealed class ResearchDetailScope : TreeRegionScope, IResearchFocusSource
    {
        public ResearchDetailScope()
        {
            Claim("researchDetail.infoCard", delegate
            {
                InspectionTreeItem item = CurrentTreeItem();
                if (item != null)
                {
                    WindowlessResearchDetailState.HandleInfoCard(item);
                }
            });
            // TreeRegionScope leaves the shared tree.jumpTo*Section ids for subclasses to claim.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));
            Claim(SharedMenuGrammar.Cancel, delegate
            {
                WindowlessResearchDetailState.Close();
            }, when: () => !TypeaheadHasActiveSearch);
        }

        /// <summary>Vanilla's tab close must not preempt the Cancel claim, or Escape skips the drill-down stack entirely.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        public override string Name
        {
            get { return "research-detail"; }
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.Shell.Tree.ResearchDetailRegionName".Translate(); }
        }

        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return true;
        }

        /// <summary>Sections are the level-0 rows BuildDetailTree emits, Description through the Start/Stop action.</summary>
        protected override bool IsSectionBoundary(InspectionTreeItem item)
        {
            return item.IndentLevel == 0;
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            DetailNodeType nodeType = item.Data is DetailNodeType dt ? dt : DetailNodeType.Info;
            var d = new ElementDescription();
            d.Role = ElementRole.TreeItem;
            string label = item.Label.TrimEnd('.', '!', '?');
            if (nodeType == DetailNodeType.Category)
            {
                label += TreeNavigationHelper.FormatExpansionSpaceSuffix(item);
            }
            d.Label = label;
            if (nodeType == DetailNodeType.Info && !string.IsNullOrEmpty(item.Description))
            {
                d.Extras = item.Description;
            }
            return d;
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            DetailNodeType nodeType = item.Data is DetailNodeType dt ? dt : DetailNodeType.Info;
            if (nodeType == DetailNodeType.Category)
            {
                // No custom activate for a category, so toggle expand/collapse through the same
                // machinery (and sounds) Left/Right use.
                AdjustContentItem(0, PrefixRowCount + Tree.SelectedIndex, item.IsExpanded ? -1 : 1);
                return;
            }
            WindowlessResearchDetailState.HandleActivate(item);
        }

        /// <summary>The project this detail view is currently showing.</summary>
        internal ResearchProjectDef FocusedProject
        {
            get { return WindowlessResearchDetailState.CurrentProject; }
        }

        ResearchProjectDef IResearchFocusSource.FocusedProject
        {
            get { return FocusedProject; }
        }

        // Bridge targets wired once by ResearchScopeMirror's static constructor. They fire
        // repeatedly while the scope stays pushed (drilling from one project into another), so the
        // facade calls into the live singleton every time rather than hanging on a lifecycle hook.

        /// <summary>Rebuilds the detail tree fresh for a project and announces the focused row.</summary>
        public void BuildAndAnnounce(ResearchProjectDef project)
        {
            SetTreeRoot(WindowlessResearchDetailHelper.BuildDetailTree(project));
            RefreshModel();
            SyncRegionFromCurrentTree();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// The post-start/stop-research refresh. The cursor MUST survive the rebuild: starting or
        /// stopping research only relabels the Action node, so the player's position in
        /// Prerequisites/Unlocks/Dependents has to be preserved.
        /// </summary>
        public void RefreshTreePreservingCursor(ResearchProjectDef project)
        {
            int previousIndex = Tree.SelectedIndex;
            SetTreeRoot(WindowlessResearchDetailHelper.BuildDetailTree(project), previousIndex);
            RefreshModel();
            SyncRegionFromCurrentTree();
            AnnounceCurrentItem();
            // The same change re-sorts the menu's status groups; keep that tree current too.
            MenuRefreshHook?.Invoke();
        }

        /// <summary>Wired by ResearchScopeMirror to the menu scope's silent section rebuild.</summary>
        internal Action MenuRefreshHook;
    }

    /// <summary>
    /// Reconciles the research menu/detail pair: menu pushed first, detail second, so detail lands
    /// ABOVE menu whenever both are active. Both gates are bare IsActive. Its position in
    /// ShellDispatcher's mirror pass is free: the only keyboard openers are this pair's own ambient
    /// <c>map.menu.research</c> claim and EntityCodexState's drill-in, whose scope stands its own
    /// IsLive term down when this menu opens.
    /// </summary>
    internal static class ResearchScopeMirror
    {
        private static readonly ResearchMenuScope menu = new ResearchMenuScope();
        private static readonly ResearchDetailScope detail = new ResearchDetailScope();

        static ResearchScopeMirror()
        {
            WindowlessResearchMenuState.RebuildCallback = menu.RebuildFresh;
            WindowlessResearchMenuState.FocusProjectCallback = menu.FocusOnProject;
            WindowlessResearchDetailState.BuildAndAnnounceCallback = detail.BuildAndAnnounce;
            WindowlessResearchDetailState.RefreshTreeCallback = detail.RefreshTreePreservingCursor;
            detail.MenuRefreshHook = menu.RefreshPreservingSelection;
        }

        public static void Reconcile()
        {
            ReconcileOne(menu, WindowlessResearchMenuState.IsActive);
            ReconcileOne(detail, WindowlessResearchDetailState.IsActive);
        }

        private static void ReconcileOne(FocusScope scope, bool live)
        {
            if (live)
            {
                // Push re-floats an already-stacked scope, so an unconditional per-frame Push
                // would hoist this scope back above a window-attached InfoCardScope one frame
                // after Alt+I opened a card, masking it.
                if (!FocusStack.Contains(scope))
                {
                    FocusStack.Push(scope);
                }
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }

    /// <summary>
    /// Drives the vanilla Research window's own selection every pass, so its DoWindowContents
    /// highlights, scrolls to and shows the detail pane for the keyboard cursor's project. The
    /// project comes from whichever scope is TOPMOST (<see cref="IResearchFocusSource"/>): detail
    /// sits above the menu it was opened from, so asking the menu first would strand the window on
    /// the menu's row. <see cref="MainTabWindow_Research.Select"/> is idempotent, so calling it
    /// every pass is deliberate — it also heals a mouse click that selected something else.
    /// <see cref="ScrollPositioner.Arm"/> fires only on an actual project change, or vanilla's
    /// positioner re-centres the tech tree every frame.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.DoWindowContents))]
    internal static class ResearchWindowSelectionPatch
    {
        private static readonly AccessTools.FieldRef<MainTabWindow_Research, ScrollPositioner> scrollPositionerField =
            AccessTools.FieldRefAccess<MainTabWindow_Research, ScrollPositioner>("scrollPositioner");

        private static ResearchProjectDef lastArmed;

        [HarmonyPrefix]
        public static void Prefix(MainTabWindow_Research __instance)
        {
            try
            {
                ResearchProjectDef focused = FocusStackLookup.TopmostOfType<IResearchFocusSource>()?.FocusedProject;
                if (focused == null)
                {
                    return;
                }
                __instance.Select(focused);
                if (focused != lastArmed)
                {
                    lastArmed = focused;
                    scrollPositionerField(__instance).Arm();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Research window selection error", ex);
            }
        }
    }

    /// <summary>
    /// Paints the keyboard focus ring over vanilla's tech tree, reusing the interest rect
    /// vanilla's ScrollPositioner already computed for auto-scroll, so no coordinate conversion is
    /// needed. Patches the private ListProjects rather than DoWindowContents because that postfix
    /// still runs inside the same Widgets.BeginGroup the interest rect was registered in.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Research), "ListProjects")]
    internal static class ResearchWindowFocusRingPatch
    {
        private static readonly AccessTools.FieldRef<MainTabWindow_Research, ScrollPositioner> scrollPositionerField =
            AccessTools.FieldRefAccess<MainTabWindow_Research, ScrollPositioner>("scrollPositioner");
        private static readonly AccessTools.FieldRef<MainTabWindow_Research, QuickSearchWidget> quickSearchWidgetField =
            AccessTools.FieldRefAccess<MainTabWindow_Research, QuickSearchWidget>("quickSearchWidget");
        private static readonly AccessTools.FieldRef<ScrollPositioner, Rect?> interestRectField =
            AccessTools.FieldRefAccess<ScrollPositioner, Rect?>("interestRect");

        [HarmonyPostfix]
        public static void Postfix(MainTabWindow_Research __instance)
        {
            try
            {
                if (Event.current.type != EventType.Repaint)
                {
                    return;
                }
                if (FocusStackLookup.TopmostOfType<IResearchFocusSource>() == null)
                {
                    return;
                }
                QuickSearchWidget search = quickSearchWidgetField(__instance);
                if (search != null && search.filter.Active)
                {
                    // A quick-search match unions into this same interest rect, so ringing it
                    // would highlight the wrong node.
                    return;
                }
                Rect? interest = interestRectField(scrollPositionerField(__instance));
                if (interest.HasValue)
                {
                    FocusRing.Draw(interest.Value.ContractedBy(2f));
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Research window focus ring error", ex);
            }
        }
    }

    /// <summary>
    /// Gives the tech tree its second scroll axis. The pane is a two-axis scroll view whose content
    /// is routinely taller than it is, but the window's only positioning call is ScrollHorizontally,
    /// so a keyboard drill-in centres a project left-to-right and leaves it clipped above or below
    /// the pane, taking <see cref="ResearchWindowFocusRingPatch"/>'s ring with it.
    /// <see cref="ScrollPositioner.Scroll"/> is the same object, interest rect and outer size,
    /// differing only in the bool it defaults to true. Replacing the call rather than adding a
    /// second is forced by the positioner: <c>Scroll</c> disarms itself, so a second call would
    /// leave one axis unserved. The prefix declines any positioner that is not the open Research
    /// window's own, and declines entirely unless a research scope is driving.
    /// </summary>
    [HarmonyPatch(typeof(ScrollPositioner), nameof(ScrollPositioner.ScrollHorizontally))]
    internal static class ResearchTreeVerticalScrollPatch
    {
        private static readonly AccessTools.FieldRef<MainTabWindow_Research, ScrollPositioner> scrollPositionerField =
            AccessTools.FieldRefAccess<MainTabWindow_Research, ScrollPositioner>("scrollPositioner");

        [HarmonyPrefix]
        public static bool Prefix(ScrollPositioner __instance, ref Vector2 scrollPos, Vector2 outRectSize)
        {
            try
            {
                if (FocusStackLookup.TopmostOfType<IResearchFocusSource>() == null)
                {
                    return true;
                }
                if (!(Find.MainTabsRoot?.OpenTab?.TabWindow is MainTabWindow_Research window)
                    || !ReferenceEquals(scrollPositionerField(window), __instance))
                {
                    return true;
                }
                __instance.Scroll(ref scrollPos, outRectSize);
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Research window vertical scroll error", ex);
                return true;
            }
        }
    }
}
