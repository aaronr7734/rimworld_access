using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Controls what type of inspection tree to build.</summary>
    public enum InspectionMode
    {
        /// <summary>Full inspection with all actions (operations, drop/consume, job cancellation).</summary>
        Full,

        /// <summary>Read-only inspection: data only, no modifying actions (e.g. caravan formation).</summary>
        ReadOnly
    }

    /// <summary>Builds the inspection tree for objects.</summary>
    public static class InspectionTreeBuilder
    {
        private static void AddChild(InspectionTreeItem parent, InspectionTreeItem child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }

        /// <summary>
        /// The pawn behind a thing (pawn, or a corpse's inner pawn), else null. Shared with Adapters/.
        /// </summary>
        internal static Pawn GetPawnFromThing(object obj)
        {
            if (obj is Pawn pawn)
                return pawn;

            if (obj is Corpse corpse)
                return corpse.InnerPawn;

            return null;
        }

        /// <summary>Builds the root tree for all objects at a position.</summary>
        public static InspectionTreeItem BuildTree(List<object> objects, InspectionMode mode = InspectionMode.Full)
        {
            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = "RimWorldAccess.Inspection.Tree.Root".Translate(),
                IsExpandable = true,
                IsExpanded = true,
                IndentLevel = -1  // Root is not shown
            };

            foreach (var obj in objects)
            {
                AddChild(root, BuildObjectItem(obj, 0, mode));
            }

            return root;
        }

        /// <summary>Builds a tree item for a single object (pawn, building, etc.).</summary>
        private static InspectionTreeItem BuildObjectItem(object obj, int indent, InspectionMode mode)
        {
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = InspectionInfoHelper.GetObjectSummary(obj),
                Data = obj,
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };

            item.OnActivate = () => BuildObjectChildren(item, mode);

            return item;
        }

        /// <summary>Builds category children for an object when it's expanded, discovering tabs dynamically for Things.</summary>
        private static void BuildObjectChildren(InspectionTreeItem objectItem, InspectionMode mode)
        {
            if (objectItem.Children.Count > 0)
                return; // Already built

            var obj = objectItem.Data;

            // Many tabs (ITab_Storage and friends) check IsVisible via
            // Find.Selector.SingleSelectedThing, and the selection may have moved since the panel opened.
            if (obj is Thing thingToSelect && !Find.Selector.IsSelected(thingToSelect))
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(thingToSelect, playSound: false, forceDesignatorDeselect: false);
            }
            else if (obj is Zone zoneToSelect && !Find.Selector.IsSelected(zoneToSelect))
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(zoneToSelect, playSound: false, forceDesignatorDeselect: false);
            }
            else if (obj is Plan planToSelect && !Find.Selector.IsSelected(planToSelect))
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(planToSelect, playSound: false, forceDesignatorDeselect: false);
            }

            var dynamicCategories = InspectionInfoHelper.GetDynamicCategories(obj);

            // Capture must run on a Repaint frame while this code runs at key-dispatch time, so
            // requests are posted here — at tab discovery — and the rows are cached by the time the
            // user reaches the category node. Unknown (typically modded) tabs are captured for their
            // whole content; known tabs only so the parity diff can surface visual rows the adapter
            // does not present. The shadow-tree build must never post its own capture requests.
            if ((obj is Thing || obj is Zone) && !CaptureParityDiff.IsBuildingShadowTree)
            {
                foreach (var categoryInfo in dynamicCategories)
                {
                    if (categoryInfo.Tab == null)
                        continue;
                    // Social and Log are excluded from the known-tab parity capture: viewport culling
                    // makes their capture systematically partial and both have complete data models.
                    if (categoryInfo.IsKnown && IsParityExcludedTab(categoryInfo.Tab))
                        continue;
                    InspectTabCaptureService.RequestCapture(obj, categoryInfo.Tab);
                }
            }

            foreach (var categoryInfo in dynamicCategories)
            {
                if (mode == InspectionMode.ReadOnly && categoryInfo.Handler == TabHandlerType.Action)
                    continue;

                // The parity-diff shadow tree omits OTHER tabs' categories: its text is the
                // containment basis for one tab's unmirrored test, and a row authored under a
                // different tab must not vouch for this tab's missing one. Synthetic categories stay,
                // since vanilla cards paint content the tree files under them.
                if (CaptureParityDiff.IsBuildingShadowTree && categoryInfo.Tab != null
                    && !ReferenceEquals(categoryInfo.Tab, CaptureParityDiff.ShadowTreeTab))
                    continue;

                AddChild(objectItem, BuildCategoryItemFromInfo(obj, categoryInfo, objectItem.IndentLevel + 1, mode));
            }

            // Info Card is read-only, so it is available in all modes.
            if (obj is Thing thing)
            {
                var infoCardItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = ConceptDefOf.InfoCard.label.CapitalizeFirst(),
                    Data = thing,
                    IndentLevel = objectItem.IndentLevel + 1,
                    IsExpandable = false
                };
                infoCardItem.OnActivate = () =>
                {
                    // Leave inspection active underneath: the drill-in scope mirrors stand down while
                    // InfoCardState.IsActive, and Window_PostClose_Patch re-announces the row on close.
                    var dialog = new Dialog_InfoCard(thing);
                    Find.WindowStack.Add(dialog);
                };
                AddChild(objectItem, infoCardItem);
            }
        }

        /// <summary>Builds a tree item from a TabCategoryInfo (dynamic tab discovery).</summary>
        private static InspectionTreeItem BuildCategoryItemFromInfo(object obj, TabCategoryInfo categoryInfo, int indent, InspectionMode mode = InspectionMode.Full)
        {
            // Use OriginalCategoryName (English) for internal logic
            string categoryKey = categoryInfo.OriginalCategoryName ?? categoryInfo.Name;

            // Every presentation decision below resolves through the category's registered adapter;
            // categories without one get the localizer display name and fallback presentations.
            InspectNodeRegistry.TryResolveCategory(categoryKey, out InspectNodeAdapter adapter);
            string displayName = adapter != null
                ? adapter.CategoryDisplayName(obj)
                : InspectionCategoryLocalizer.Localize(categoryKey);

            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = adapter != null ? adapter.CategoryLabel(obj, displayName) : displayName,
                ExpandedLabel = displayName, // Short form for submenu mode section announcements
                Data = obj,
                SourceTab = categoryInfo.Tab,
                IndentLevel = indent
            };

            // Inline categories (Overview, Work Priorities) render as one "Name: content" line.
            if (adapter != null && adapter.IsInline)
            {
                string content = adapter.InlineContent(obj);
                if (!string.IsNullOrEmpty(content))
                {
                    item.Label = $"{displayName}: {content}";
                }
                else
                {
                    item.Label = displayName;
                }
                item.IsExpandable = false;
                return item;
            }

            switch (categoryInfo.Handler)
            {
                case TabHandlerType.Action:
                    // Actionable category (Bills, Storage, etc.) - opens separate menu
                    item.IsExpandable = false;
                    item.OnActivate = () =>
                    {
                        if (InspectNodeRegistry.TryResolveCategory(categoryKey, out InspectNodeAdapter actionAdapter))
                            actionAdapter.ExecuteAction(obj);
                    };
                    item.OpensOverlayMenu = true;
                    break;

                case TabHandlerType.RichNavigation:
                    // Rich navigation with sub-items (Health, Gear, Skills, etc.)
                    if (adapter != null && adapter.CanExpand(obj))
                    {
                        item.IsExpandable = true;
                        item.IsExpanded = false;
                        item.OnActivate = () => BuildCategoryChildren(item, obj, categoryKey, mode, categoryInfo.Tab);
                    }
                    else
                    {
                        string noExpandLabel = adapter?.NoExpandLabel(obj);
                        if (noExpandLabel != null)
                        {
                            // e.g. Meditation Focus with no focus objects nearby.
                            item.Label = noExpandLabel;
                            item.IsExpandable = false;
                        }
                        else
                        {
                            item.IsExpandable = true;
                            item.IsExpanded = false;
                            item.OnActivate = () => BuildDetailedInfoChildren(item, obj, categoryKey, categoryInfo.Tab);
                        }
                    }
                    break;

                case TabHandlerType.BasicInspectString:
                    // Basic fallback - show GetInspectString content or tab info
                    item.IsExpandable = true;
                    item.IsExpanded = false;
                    if (categoryInfo.Tab != null)
                    {
                        // This is an actual game tab - use dynamic tab info
                        item.OnActivate = () => BuildDynamicTabChildren(item, obj, categoryInfo);
                    }
                    else
                    {
                        // Synthetic category - use existing detailed info
                        item.OnActivate = () => BuildDetailedInfoChildren(item, obj, categoryKey, categoryInfo.Tab);
                    }
                    break;

                default:
                    item.IsExpandable = true;
                    item.IsExpanded = false;
                    item.OnActivate = () => BuildDetailedInfoChildren(item, obj, categoryKey, categoryInfo.Tab);
                    break;
            }

            return item;
        }

        /// <summary>Builds children for a tab discovered from the game but not explicitly supported, falling back to GetInspectString.</summary>
        private static void BuildDynamicTabChildren(InspectionTreeItem parentItem, object obj, TabCategoryInfo categoryInfo)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            // Captured presentation: the rows the tab actually draws, read via the detached capture
            // pass and folded into visual lines, tooltips riding each line. Things and zones alike —
            // zone-hosted mod tabs have no other content path.
            if (categoryInfo?.Tab != null
                && InspectTabCaptureService.TryGetRows(obj, categoryInfo.Tab, out var capturedRows))
            {
                EmitCapturedChildren(parentItem, obj, categoryInfo.Tab, capturedRows);
                return;
            }

            // Defensive null checks, plus the zone cold-cache case: zones have no inspect-string fallback.
            if (categoryInfo == null || categoryInfo.Tab == null || !(obj is Thing thing))
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.NoTabInfo".Translate(),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });
                return;
            }

            string info = TabRegistry.GetFallbackInfo(thing, categoryInfo.Tab);

            if (string.IsNullOrEmpty(info) || info == (string)"RimWorldAccess.Inspection.Category.NoInfo".Translate())
            {
                AddChild(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.TabNoKeyboardContent".Translate(InspectionCategoryLocalizer.Localize(categoryInfo.OriginalCategoryName ?? categoryInfo.Name)),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });

                // Add a hint if tab is known but not rich-supported
                if (!categoryInfo.IsKnown)
                {
                    AddChild(parentItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = "RimWorldAccess.Inspection.Tree.UnrecognizedTab".Translate(),
                        IndentLevel = parentItem.IndentLevel + 1,
                        IsExpandable = false
                    });
                }
                return;
            }

            InspectNodeFactory.DetailLines(parentItem, info);
        }

        /// <summary>
        /// Emits a captured tab's folded rows as <paramref name="parentItem"/>'s children and folds
        /// them into its collapsed label; shared by first expansion and the post-activation rebuild.
        /// </summary>
        private static void EmitCapturedChildren(InspectionTreeItem parentItem, object obj, InspectTabBase tab, List<FoldedRow> rows)
        {
            foreach (FoldedRow row in rows)
            {
                EmitCapturedRow(parentItem, row, obj, tab);
            }
            FoldChildrenIntoLabel(parentItem);
        }

        /// <summary>
        /// Rebuilds a CAPTURED tab category in place after one of its own controls was activated
        /// through the armed capture channel — the captured-path twin of
        /// <see cref="RebuildAdapterCategory"/>. Items built from the old rows would otherwise stay
        /// stale until the player left and re-entered. Silent: the activation already announced its
        /// outcome; this only re-emits children and preserves the cursor via
        /// <see cref="TreeStatePreserve"/>.
        /// </summary>
        internal static void RebuildCapturedCategory(object obj, InspectTabBase tab)
        {
            if (!WindowlessInspectionState.IsActive || obj == null || tab == null)
                return;
            InspectionTreeItem categoryItem = FindBuiltCategoryNode(WindowlessInspectionState.CurrentTreeRoot, obj, tab);
            if (categoryItem == null)
                return;
            // Fresh rows first, clear second — a failed recapture must not empty the page.
            if (!InspectTabCaptureService.TryGetRows(obj, tab, out var rows))
                return;

            RebuildBranchInPlace(categoryItem, () =>
            {
                // FoldChildrenIntoLabel appends the children's text to Label, so the short form must
                // be restored first or every rebuild grows the label.
                if (!string.IsNullOrEmpty(categoryItem.ExpandedLabel))
                    categoryItem.Label = categoryItem.ExpandedLabel;
                categoryItem.Children.Clear();
                EmitCapturedChildren(categoryItem, obj, tab, rows);
            });
        }

        /// <summary>
        /// The already-built Category node presenting <paramref name="tab"/> for
        /// <paramref name="obj"/>, or null. An unexpanded category is deliberately not returned: its
        /// first expansion reads the fresh cache anyway.
        /// </summary>
        private static InspectionTreeItem FindBuiltCategoryNode(InspectionTreeItem node, object obj, InspectTabBase tab)
        {
            if (node == null)
                return null;
            if (node.Type == InspectionTreeItem.ItemType.Category
                && ReferenceEquals(node.SourceTab, tab)
                && ReferenceEquals(node.Data, obj)
                && node.Children.Count > 0)
            {
                return node;
            }
            foreach (InspectionTreeItem child in node.Children)
            {
                InspectionTreeItem found = FindBuiltCategoryNode(child, obj, tab);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Builds children for expandable categories through the category's registered adapter.
        /// Categories without a registered child-builder use the fallbacks wired in
        /// <see cref="BuildCategoryItemFromInfo"/>.
        /// </summary>
        private static void BuildCategoryChildren(InspectionTreeItem categoryItem, object obj, string category, InspectionMode mode, InspectTabBase tab)
        {
            if (categoryItem.Children.Count > 0)
                return; // Already built

            if (InspectNodeRegistry.TryResolveCategory(category, out InspectNodeAdapter adapter))
            {
                try
                {
                    adapter.BuildChildren(categoryItem, obj, mode);
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"Inspection adapter '{adapter.CategoryKey}' build failed: {ex.Message}");
                }
                // Registered extenders (e.g. a mod-compat action row) run after the category's own
                // rows. Once only, guarded by the Children.Count check above.
                InspectNodeRegistry.InvokeCategoryExtenders(category, categoryItem, obj);
            }

            AppendUnmirroredSection(categoryItem, obj, tab);
        }

        /// <summary>
        /// Rebuilds an adapter-owned category in place after one of the adapter's own actions mutates
        /// state: clears the children and re-runs all three passes first expansion runs (the adapter's
        /// build, registered category extenders, and the "Also shown on this tab" parity section, which
        /// waits for a fresh capture of the changed tab).
        /// Adapters must route rebuilds here — calling their own BuildChildren directly silently drops
        /// the latter two passes. The caller announces the outcome; this only rebuilds and reflattens.
        /// Cursor and expansion state survive via <see cref="TreeStatePreserve"/>'s branch-scoped
        /// capture/restore, without which the tree's by-reference selection match always fails against
        /// freshly-built rows.
        /// </summary>
        internal static void RebuildAdapterCategory(InspectionTreeItem categoryItem, object obj, InspectionMode mode, InspectNodeAdapter adapter, InspectTabBase tab)
        {
            RebuildBranchInPlace(categoryItem, () =>
            {
                categoryItem.Children.Clear();
                try
                {
                    adapter.BuildChildren(categoryItem, obj, mode);
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"Inspection adapter '{adapter.CategoryKey}' build failed: {ex.Message}");
                }
                InspectNodeRegistry.InvokeCategoryExtenders(adapter.CategoryKey, categoryItem, obj);
            });
            if (tab != null && !IsParityExcludedTab(tab))
                InspectTabCaptureService.RecaptureForParity(obj, tab);
        }

        /// <summary>The parity section a post-action rebuild deferred until its fresh capture landed.</summary>
        internal static void AppendUnmirroredSectionAfterRecapture(object obj, InspectTabBase tab)
        {
            if (!WindowlessInspectionState.IsActive)
                return;
            InspectionTreeItem categoryItem = FindBuiltCategoryNode(WindowlessInspectionState.CurrentTreeRoot, obj, tab);
            if (categoryItem == null)
                return;
            int before = categoryItem.Children.Count;
            AppendUnmirroredSection(categoryItem, obj, tab);
            if (categoryItem.Children.Count != before)
                WindowlessInspectionState.RefreshVisibleList();
        }

        /// <summary>
        /// Rebuilds ONE branch's children via <paramref name="rebuildChildren"/> while preserving
        /// expansion and the cursor by the tree law: the same logical node when it survives, else its
        /// nearest surviving ancestor, else the branch root. Silent — the caller announces its own
        /// outcome. The shared vehicle for every adapter action that mutates rows in place.
        /// </summary>
        internal static void RebuildBranchInPlace(InspectionTreeItem branchRoot, Action rebuildChildren)
        {
            InspectionScope live = InspectionScope.Live;
            InspectionTreeItem cursor = live?.CurrentItemForRebuild();
            TreeStatePreserve.BranchMemo memo = TreeStatePreserve.CaptureBranch(branchRoot, cursor);

            try
            {
                rebuildChildren();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Inspection branch '{branchRoot.Label}' rebuild failed: {ex.Message}");
            }

            InspectionTreeItem landing = TreeStatePreserve.RestoreBranch(branchRoot, memo, RunLazyExpandHook);
            if (landing == null && memo.CursorWasInBranch)
            {
                landing = branchRoot;
            }
            if (live != null)
            {
                live.RefreshTreeRowsLandingOn(landing);
            }
            else
            {
                WindowlessInspectionState.RefreshVisibleList();
            }
        }

        /// <summary>
        /// The inspection tree's lazy-population convention (mirroring <see cref="InspectionScope"/>'s
        /// <c>OnBeforeExpandNode</c>): a section built on first expand has an OnActivate delegate and
        /// no children until it runs. Passed to <see cref="TreeStatePreserve.RestoreBranch"/> so a
        /// previously-expanded lazy section rebuilds before the walk marks it expanded again.
        /// </summary>
        private static void RunLazyExpandHook(InspectionTreeItem item)
        {
            if (item.OnActivate != null && item.Children.Count == 0)
            {
                item.OnActivate();
            }
        }

        /// <summary>Builds detailed info children for a category.</summary>
        internal static void BuildDetailedInfoChildren(InspectionTreeItem categoryItem, object obj, string category, InspectTabBase tab = null)
        {
            if (categoryItem.Children.Count > 0)
                return; // Already built

            string info = InspectionInfoHelper.GetCategoryInfo(obj, category);

            if (!string.IsNullOrEmpty(info))
            {
                info = info.StripTags();

                var lines = info.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    var detailItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = line.Trim(),
                        IndentLevel = categoryItem.IndentLevel + 1,
                        IsExpandable = false
                    };

                    AddChild(categoryItem, detailItem);
                }

                FoldChildrenIntoLabel(categoryItem);
            }

            AppendUnmirroredSection(categoryItem, obj, tab);
        }

        /// <summary>
        /// Known tabs whose parity capture is deliberately skipped: viewport culling makes it
        /// systematically partial and both already have complete readable data models. Kept in sync
        /// with the request-loop exclusion in <see cref="BuildObjectChildren"/> and the DEBUG oracle.
        /// </summary>
        private static bool IsParityExcludedTab(InspectTabBase tab)
        {
            return tab is ITab_Pawn_Social || tab is ITab_Pawn_Log;
        }

        /// <summary>
        /// Appends an "Also shown on this tab" section listing visual rows the sighted tab draws that
        /// the accessible tree does not present (dev-mode controls, mod-injected rows). No-op when the
        /// category has no backing tab, the tab is parity-excluded, the shadow-tree build is running,
        /// or nothing is unmirrored. Rows whose control kind supports armed activation are operable;
        /// the rest stay read-only.
        /// </summary>
        private static void AppendUnmirroredSection(InspectionTreeItem categoryItem, object obj, InspectTabBase tab)
        {
            if (tab == null || IsParityExcludedTab(tab) || CaptureParityDiff.IsBuildingShadowTree)
                return;

            List<FoldedRow> rows = CaptureParityDiff.ComputeUnmirroredRows(obj, tab);
            if (rows.Count == 0)
                return;

            InspectNodeFactory.Section(categoryItem, "RimWorldAccess.Inspection.Tree.AlsoShown".Translate(), obj, section =>
            {
                foreach (FoldedRow row in rows)
                {
                    EmitCapturedRow(section, row, obj, tab);
                }
            });
        }

        /// <summary>
        /// Emits one captured visual row under <paramref name="parent"/>. No interactive control: a
        /// read-only detail line (text plus tooltip). Exactly one: presented as that control, spoken
        /// with the row's tooltip. Several: the whole row reads first for context, then one node per
        /// control labeled by its own fragment — except when the row is nothing but those controls, in
        /// which case the context line would repeat them verbatim and is dropped
        /// (<see cref="CapturedRowResidue"/>).
        /// </summary>
        private static void EmitCapturedRow(InspectionTreeItem parent, FoldedRow row, object obj, InspectTabBase tab)
        {
            int emittedFrom = parent.Children.Count;
            EmitCapturedRowNodes(parent, row, obj, tab);
            StampCapturedRow(parent, emittedFrom, row, obj, tab);
        }

        private static void EmitCapturedRowNodes(InspectionTreeItem parent, FoldedRow row, object obj, InspectTabBase tab)
        {
            string label = string.IsNullOrEmpty(row.Tip) ? row.Text : $"{row.Text}. {row.Tip}";

            if (row.Interactives == null || row.Interactives.Count == 0)
            {
                InspectNodeFactory.DetailLine(parent, label);
                return;
            }

            if (row.Interactives.Count == 1)
            {
                EmitInteractiveMember(parent, row.Interactives[0], obj, tab, label);
                return;
            }

            if (CapturedRowResidue.HasContext(row.Text, MemberFragments(row)))
            {
                // Real text sits alongside the controls (a label, a read-out):
                // the whole row still reads first, exactly as before, because
                // that context is what a sighted player sees on the line.
                InspectNodeFactory.DetailLine(parent, label);
                for (int i = 0; i < row.Interactives.Count; i++)
                {
                    EmitInteractiveMember(parent, row.Interactives[i], obj, tab, PresentationLabelFor(row, i));
                }
                return;
            }

            for (int i = 0; i < row.Interactives.Count; i++)
            {
                EmitInteractiveMember(parent, row.Interactives[i], obj, tab, PresentationLabelFor(row, i));
            }
        }

        /// <summary>
        /// Points every node one captured row just emitted back at the band it was folded from, so the
        /// focus ring can find that band's rect on the real open tab (<see cref="InspectTabRowRing"/>).
        /// A control's own children inherit it by walking up, so only the top level is stamped.
        /// </summary>
        private static void StampCapturedRow(InspectionTreeItem parent, int emittedFrom, FoldedRow row, object obj, InspectTabBase tab)
        {
            if (row.BandIndex < 0)
                return;
            var reference = new CapturedRowRef { Target = obj, Tab = tab, BandIndex = row.BandIndex };
            for (int i = emittedFrom; i < parent.Children.Count; i++)
            {
                parent.Children[i].CapturedRow = reference;
            }
        }

        /// <summary>
        /// The presentation label the tree bakes for one interactive member of a folded row, shared by
        /// first emission and the post-write relabel. One control: the whole row (text plus tip).
        /// Controls beside real text: each by its own fragment. A band of pure controls has no summary
        /// line, so the row's tooltip rides the FIRST control, the rule CapturedExtrasRows.Expand uses.
        /// </summary>
        internal static string PresentationLabelFor(FoldedRow row, int memberIndex)
        {
            if (row.Interactives == null || memberIndex < 0 || memberIndex >= row.Interactives.Count)
                return null;
            if (row.Interactives.Count == 1)
                return string.IsNullOrEmpty(row.Tip) ? row.Text : $"{row.Text}. {row.Tip}";
            InteractiveMember member = row.Interactives[memberIndex];
            if (CapturedRowResidue.HasContext(row.Text, MemberFragments(row)))
                return member.Fragment;
            return memberIndex == 0 && !string.IsNullOrEmpty(row.Tip)
                ? $"{member.Fragment}. {row.Tip}"
                : member.Fragment;
        }

        /// <summary>
        /// Re-bakes one captured control's node label from its FRESH folded row after an armed
        /// slider/text write. Labels only: the branch shape is unchanged, so no reflatten, no cursor
        /// movement, nothing spoken. The parent's folded summary is re-baked too, or the collapsed
        /// category keeps speaking the old value.
        /// </summary>
        internal static void RelabelCapturedControl(InspectionTreeItem node, FoldedRow row, int memberIndex)
        {
            if (node == null || row == null)
                return;
            string fresh = PresentationLabelFor(row, memberIndex);
            if (string.IsNullOrEmpty(fresh) || string.Equals(fresh, node.Label, StringComparison.Ordinal))
                return;
            node.Label = fresh;
            InspectionTreeItem parent = node.Parent;
            if (parent != null && !string.IsNullOrEmpty(parent.ExpandedLabel))
            {
                parent.Label = parent.ExpandedLabel;
                AppendChildrenToLabel(parent);
            }
        }

        /// <summary>
        /// The title ThingFilterMenuState's screen speaks as its region name: the owning tab's display
        /// name when one is cheaply available, else a generic localized fallback.
        /// </summary>
        private static string FilterPanelTitle(InspectTabBase tab)
        {
            if (tab != null)
            {
                string name = TabRegistry.GetCategoryNameForTab(tab);
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            return "RimWorldAccess.Inspection.Tree.FilterPanelTitle".Translate();
        }

        private static List<string> MemberFragments(FoldedRow row)
        {
            var fragments = new List<string>(row.Interactives.Count);
            for (int i = 0; i < row.Interactives.Count; i++)
            {
                fragments.Add(row.Interactives[i].Fragment);
            }
            return fragments;
        }

        /// <summary>
        /// Emits one interactive captured control under <paramref name="parent"/>, labeled
        /// <paramref name="presentationLabel"/>. Button/Checkbox/RadioButton/Tab become a single
        /// action row whose Enter fires the control's own vanilla handler via the armed capture pass.
        /// A slider becomes an expandable stepper node; a text field an action row opening an edit
        /// session seeded with the captured text. All writes ride the game's handlers via Request*.
        /// </summary>
        private static void EmitInteractiveMember(InspectionTreeItem parent, InteractiveMember member,
            object obj, InspectTabBase tab, string presentationLabel)
        {
            InteractiveMember captured = member;
            // Filter-panel hand-off, checked ahead of Kind since the underlying captured widget stays
            // WidgetKind.Label: there is no vanilla widget to re-fire, only our own filter screen to
            // open, so this bypasses the armed re-activation path entirely.
            if (captured.Source != null && captured.Source.Composite == CompositeMember.FilterPanelHandoff
                && captured.Source.Payload is CapturedFilterPanel filterPayload)
            {
                InspectNodeFactory.ActionRow(parent, presentationLabel, obj,
                    () => ThingFilterMenuState.Open(filterPayload.Filter, filterPayload.ParentFilter,
                        FilterPanelTitle(tab), filterPayload.ForceHideHitPointsConfig,
                        filterPayload.ForceHideQualityConfig, filterPayload.ForceHiddenFilters),
                    opensOverlayMenu: true);
                return;
            }
            if (member.Kind == WidgetKind.Slider)
            {
                EmitSliderNode(parent, captured, obj, tab, presentationLabel);
                return;
            }
            if (member.Kind == WidgetKind.TextField)
            {
                // The closure reads identity, seed text and row label at FIRE time: the post-write
                // control refresh renews both, so a second edit opens seeded with the committed value.
                InspectionTreeItem fieldNode = null;
                fieldNode = InspectNodeFactory.ActionRow(parent, presentationLabel, obj,
                    () => CapturedFieldEditController.EditText(obj, tab, captured.Ordinal, fieldNode.Label,
                        captured.Source?.Text ?? "", captured.Source != null && captured.Source.MultiLine,
                        captured, fieldNode));
                return;
            }
            // Button, Checkbox, RadioButton, Tab: opensOverlayMenu stays false — the outcome is
            // unknown until vanilla's handler runs (it may toggle in place rather than open a menu).
            InspectNodeFactory.ActionRow(parent, presentationLabel, obj,
                () => InspectTabCaptureService.RequestActivation(obj, tab, captured.Kind, captured.RawLabel, captured.Ordinal, presentationLabel));
        }

        /// <summary>
        /// Emits a captured slider as an expandable node with three action rows: Increase and Decrease
        /// step one unit via the game's own grid step, Set value opens a numeric edit session. The
        /// children are attached immediately rather than lazily so the parity-diff shadow-tree
        /// expansion never runs into an edit session.
        /// </summary>
        private static void EmitSliderNode(InspectionTreeItem parent, InteractiveMember member,
            object obj, InspectTabBase tab, string presentationLabel)
        {
            var node = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = presentationLabel,
                Data = obj,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false,
            };
            InspectNodeFactory.Attach(parent, node);

            // Each closure reads identity, value seed and node label at FIRE time: the post-write
            // control refresh renews them, so repeated adjusts keep matching even when the slider's
            // raw label embeds the value.
            InteractiveMember captured = member;
            InspectNodeFactory.ActionRow(node, "RimWorldAccess.Inspection.Parity.Increase".Translate(), obj,
                () => InspectTabCaptureService.RequestAdjust(obj, tab, captured.RawLabel, captured.Ordinal, +1, node.Label, captured, node));
            InspectNodeFactory.ActionRow(node, "RimWorldAccess.Inspection.Parity.Decrease".Translate(), obj,
                () => InspectTabCaptureService.RequestAdjust(obj, tab, captured.RawLabel, captured.Ordinal, -1, node.Label, captured, node));
            InspectNodeFactory.ActionRow(node, "RimWorldAccess.Inspection.Parity.SetValue".Translate(), obj,
                () => CapturedFieldEditController.EditSliderValue(obj, tab, captured.RawLabel, captured.Ordinal,
                    node.Label, captured.Source?.SliderValue ?? 0f, captured, node));
        }

        /// <summary>
        /// Folds a category's detail lines into its collapsed label so navigating to — or
        /// typeahead-matching — the category speaks its full content and is searchable. ExpandedLabel
        /// keeps the short name for the expanded view.
        /// </summary>
        private static void FoldChildrenIntoLabel(InspectionTreeItem categoryItem)
        {
            if (string.IsNullOrEmpty(categoryItem.ExpandedLabel))
                categoryItem.ExpandedLabel = categoryItem.Label;
            AppendChildrenToLabel(categoryItem);
        }

        private static void AppendChildrenToLabel(InspectionTreeItem categoryItem)
        {
            var detailLabels = categoryItem.Children.Select(c => c.Label).ToList();
            if (detailLabels.Count > 0)
                categoryItem.Label += $": {string.Join(". ", detailLabels)}";
        }

    }
}
