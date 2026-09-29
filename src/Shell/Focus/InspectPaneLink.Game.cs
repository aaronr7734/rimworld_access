using System;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// While the windowless inspection tree drives, the real inspect pane is the tree's
    /// visual twin: it stays open, its selection follows the cursor's owning object, and a
    /// category row that came from a real <see cref="InspectTabBase"/> opens that tab. There
    /// is deliberately NO reverse direction — closing the pane must never close the tree.
    /// A DIFFERENT main tab, though, outranks the twin: this link yields the pane entirely
    /// while one is open, else the switch is reverted within the frame it happens.
    ///
    /// The row-family ruling, all falling out of one
    /// ancestor walk for the nearest node carrying a non-null
    /// <see cref="InspectionTreeItem.SourceTab"/>:
    ///
    /// 1. Object row (pawn/building/item/zone/plan) — no SourceTab; select it, close the tab.
    /// 2. Overview (synthetic) — no SourceTab; close the tab, the pane's base contents ARE it.
    /// 3. Gizmos (synthetic) and its child rows — no SourceTab; close the tab (an open tab
    ///    crowds the gizmo bar). The ring on the real gizmo is drawn separately by
    ///    <see cref="GizmoNavigationPatch"/>'s branch 2.
    /// 4. A real-tab category (SourceTab != null) and every descendant row — open that tab.
    ///    This is the 80% pawn case: Health, Needs, Gear, Social. A row captured from that
    ///    tab's own draw also gets the focus ring on the real row it was folded from
    ///    (<see cref="InspectTabRowRing"/>).
    /// 5. A synthetic aggregation with no vanilla surface at all (Mood, Skills, Appearance,
    ///    Work Priorities, Job Queue, Temperature, Bed Assignment, ...) — no SourceTab; close
    ///    the tab, and no ring: these rows aggregate data vanilla spreads across several
    ///    surfaces, so there is no drawn row to ring. A stale open tab would tell a sighted
    ///    viewer the player is somewhere they are not, which is worse than the object.
    /// 6. The Info Card action row — no SourceTab; close the tab. Activating it opens the real
    ///    Dialog_InfoCard, which is a different surface entirely.
    /// 7. "Also shown on this tab" captured rows — they hang under a category that has a
    ///    SourceTab, so rule 4 already has the right tab open, and they ring exactly like
    ///    every other captured row.
    ///
    /// Rules 1, 2, 3, 5, 6 and 7 all fall out of the same walk returning null, which is why
    /// the close-tab branch below needs no special cases.
    /// </summary>
    internal static class InspectPaneLink
    {
        internal static void Reconcile()
        {
            try
            {
                if (Current.ProgramState != ProgramState.Playing)
                {
                    return;
                }
                if (!WindowlessInspectionState.IsActive)
                {
                    return;
                }
                if (Find.MainTabsRoot == null || Find.Selector == null)
                {
                    return;
                }

                InspectionScope scope = InspectionScope.Live;
                if (scope == null)
                {
                    return;
                }

                InspectionTreeItem row = scope.FocusedRow();
                if (row == null)
                {
                    return;
                }

                MainButtonDef openTab = Find.MainTabsRoot.OpenTab;
                if (ShellGuards.NonInspectMainTabOpen())
                {
                    // Another main tab owns the screen: re-asserting the pane here would revert
                    // that switch within the frame (Character Development's Add quirks button
                    // calls SetCurrentTab from inside its ITab). The tree resumes when it closes.
                    return;
                }

                if (TryRehomeOnReplacement(row))
                {
                    return;
                }
                SyncSelection(row);

                if (openTab == null)
                {
                    // Vanilla's own vehicle (decompiled RimWorld/MainTabsRoot.cs:37-43) and the
                    // same call Selector.SelectorOnGUI makes when a selection appears with no
                    // tab open (decompiled RimWorld/Selector.cs:167-170).
                    Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, playSound: false);
                }

                DriveTab(row);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Inspect pane link reconcile error", ex);
            }
        }

        /// <summary>Vanilla moves the selection onto the thing that replaces a finished frame or
        /// blueprint in place; the tree follows it as the drawn pane does.</summary>
        private static bool TryRehomeOnReplacement(InspectionTreeItem row)
        {
            InspectionTreeItem node = row;
            while (node != null && node.Type != InspectionTreeItem.ItemType.Object)
            {
                node = node.Parent;
            }
            if (!(node?.Data is Thing dead) || !dead.Destroyed)
            {
                return false;
            }
            Thing replacement = Find.Selector.SingleSelectedThing;
            if (replacement == null || replacement == dead || !replacement.Spawned
                || !replacement.OccupiedRect().Contains(dead.Position))
            {
                return false;
            }
            WindowlessInspectionState.RehomeOnReplacement(replacement);
            return true;
        }

        /// <summary>
        /// Decision C2: BuildObjectList puts a zone first
        /// (src/Inspection/WindowlessInspectionState.cs:341-353) but Open only selects
        /// objects[0] is Thing (:123-127), so a cell holding a growing zone plus a plant
        /// selects nothing and the pane is blank until a row is expanded. Walking to the
        /// focused row's owning Object node and re-running BuildObjectChildren's own select
        /// expression (src/Inspection/InspectionTreeBuilder.cs:118-132) — duplicated rather
        /// than refactored, so the expansion path keeps working unchanged if this link ever
        /// no-ops — closes that hole. The IsSelected guard makes this a no-op on every pass
        /// but the one that changes.
        /// </summary>
        private static void SyncSelection(InspectionTreeItem row)
        {
            InspectionTreeItem node = row;
            while (node != null && node.Type != InspectionTreeItem.ItemType.Object)
            {
                node = node.Parent;
            }
            if (node == null)
            {
                return;
            }

            object obj = node.Data;
            if (obj is Thing thing)
            {
                // A finished frame is destroyed under the open tree; Select would log every pass.
                if (!thing.Destroyed && !Find.Selector.IsSelected(thing))
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(thing, playSound: false, forceDesignatorDeselect: false);
                }
            }
            else if (obj is Zone zone)
            {
                if (!Find.Selector.IsSelected(zone))
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(zone, playSound: false, forceDesignatorDeselect: false);
                }
            }
            else if (obj is Plan plan)
            {
                if (!Find.Selector.IsSelected(plan))
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(plan, playSound: false, forceDesignatorDeselect: false);
                }
            }
        }

        /// <summary>
        /// Opens the focused row's nearest SourceTab, or closes whatever tab is open when no
        /// ancestor carries one. See the class remarks for the full row-family ruling.
        /// </summary>
        private static void DriveTab(InspectionTreeItem row)
        {
            MainTabWindow_Inspect pane = MainButtonDefOf.Inspect.TabWindow as MainTabWindow_Inspect;
            if (pane == null)
            {
                return;
            }

            InspectTabBase tab = NearestSourceTab(row);
            if (tab == null)
            {
                // Vanilla's own reset (decompiled RimWorld/MainTabWindow_Inspect.cs's
                // CloseOpenTab). UpdateTabs (decompiled RimWorld/InspectPaneUtility.cs:183-214)
                // self-heals a type that no longer matches a visible tab, so re-asserting this
                // every pass is safe.
                pane.CloseOpenTab();
                return;
            }

            if (pane.OpenTabType != tab.GetType())
            {
                // NOT InspectPaneUtility.OpenTab: it is postfixed by
                // src/Biotech/GeneInspectionPatch.cs:19-54, which SPEAKS for the two gene tabs,
                // and it routes through ToggleTab, which plays SoundDefOf.TabOpen (decompiled
                // RimWorld/InspectPaneUtility.cs:343-356). These two statements are exactly
                // ToggleTab's open branch minus the sound line.
                tab.OnOpen();
                pane.OpenTabType = tab.GetType();
            }
        }

        private static InspectTabBase NearestSourceTab(InspectionTreeItem row)
        {
            for (InspectionTreeItem node = row; node != null; node = node.Parent)
            {
                if (node.SourceTab != null)
                {
                    return node.SourceTab;
                }
            }
            return null;
        }
    }
}
