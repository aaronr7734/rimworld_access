using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The card is a real visible window, so this class's whole job is to tell VANILLA
    /// where the keyboard cursor is: set the card's own tab through
    /// <see cref="Dialog_InfoCard.SetTab"/> and vanilla's own selected stat entry through
    /// <see cref="StatsReportUtility"/>'s own selection method. After that, vanilla paints
    /// the selected-row highlight, the explanation panel and the auto-scroll itself. This
    /// class draws nothing of its own.
    ///
    /// It is idempotent and runs every pass, because vanilla wipes its own statics
    /// whenever a nested card is constructed (<see cref="Dialog_InfoCard"/>'s constructor
    /// calls <c>StatsReportUtility.Reset()</c>), so the outer card's cursor has to be
    /// re-asserted on the very next draw.
    /// </summary>
    internal static class InfoCardVisualDriver
    {
        // decompiled RimWorld/StatsReportUtility.cs:367 — vanilla's own selection method.
        // The public int overload (:346) defaults playSound to true and would chirp
        // Tick_High on every arrow press; the public StatDef overload (:354) posts a
        // Messages.Message on a miss. This one takes the exact entry and the sound flag.
        private static readonly MethodInfo selectEntryMethod =
            AccessTools.Method(typeof(StatsReportUtility), "SelectEntry",
                new[] { typeof(StatDrawEntry), typeof(bool) });

        // decompiled RimWorld/StatsReportUtility.cs:12 and :18 — read-only, so the driver
        // can tell "already selected" from "needs selecting", and can arm the positioner
        // on a change (Verse/ScrollPositioner.cs:11).
        private static readonly FieldInfo selectedEntryField =
            AccessTools.Field(typeof(StatsReportUtility), "selectedEntry");
        private static readonly FieldInfo scrollPositionerField =
            AccessTools.Field(typeof(StatsReportUtility), "scrollPositioner");

        // decompiled Verse/Dialog_InfoCard.cs:359 — private, no public getter. SetTab
        // (:493-496) and the tab-strip click delegates (:530-561) both just assign this
        // field; there is no other side effect to reproduce (no scroll reset, no cache
        // wipe), so reading it directly tells us exactly what the card is showing.
        private static readonly FieldInfo tabField =
            AccessTools.Field(typeof(Dialog_InfoCard), "tab");

        private static readonly bool ready;

        // Frame-to-frame edge detector for DriveTab, NOT a shadow copy of vanilla state:
        // every decision still reads tabField live. This only remembers what the live tab
        // was the last time it was checked, so a live tab that moved since then (a mouse
        // click landed) can be told apart from our own cursor asking for a different tab.
        // Keyed by dialog identity so a nested-card swap starts fresh rather than reusing
        // the outgoing card's history.
        private static Dialog_InfoCard lastTabDialog;
        private static Dialog_InfoCard.InfoCardTab lastObservedTab;

        static InfoCardVisualDriver()
        {
            ready = selectEntryMethod != null && selectedEntryField != null && scrollPositionerField != null
                && tabField != null;
            if (!ready)
            {
                ModLogger.LimitedError("Info card visual driver error", new Exception("could not resolve one or more StatsReportUtility/Dialog_InfoCard selection members; declining the info card visual driver"));
            }
        }

        internal static void Drive(Dialog_InfoCard dialog)
        {
            if (!ready)
            {
                return;
            }
            if (dialog == null)
            {
                return;
            }

            // A Vehicle Framework card draws Vehicles.VehicleStatDrawEntry from VF's own
            // static cache (src/Compat/VfInfoCardCompat.Game.cs:62-102), so
            // StatsReportUtility's selection controls nothing there.
            if (VfInfoCardCompat.OwnsCard(dialog))
            {
                return;
            }

            // Live resolves InfoCardState.CurrentDialog and Owns confirms instance
            // identity. Without both, a nested card's draw pass would push the OUTER
            // card's cursor into the shared statics.
            Shell.InfoCardScope scope = Shell.InfoCardScope.Live;
            if (scope == null || !scope.Owns(dialog))
            {
                return;
            }

            InspectionTreeItem row = scope.FocusedRow();
            if (row == null)
            {
                return;
            }

            DriveTab(dialog, scope, row);
            DriveStatSelection(row);
        }

        /// <summary>
        /// Walks the row and its Parent chain for the first node whose Data is a tab
        /// (tab nodes are created with that Data at
        /// src/Inspection/InfoCardTreeBuilder.cs:168-176). A single-tab card builds its
        /// contents straight under the root, so no tab node exists — the tab is left alone.
        ///
        /// Only pushes our tab onto the dialog when OUR side is what changed (the cursor
        /// moved to a row under a different tab since the tab was last observed). When the
        /// live tab changed on its own — a mouse click on the tab strip — the cursor follows
        /// it instead, so the click sticks rather than being fought on the next pass. No
        /// announcement here: the tab-strip click is already spoken by the generic
        /// WidgetCaptureTabRecordDrawPatch hover/click capture, so speaking it again here would
        /// double it up.
        /// </summary>
        private static void DriveTab(Dialog_InfoCard dialog, Shell.InfoCardScope scope, InspectionTreeItem row)
        {
            InspectionTreeItem tabNode = null;
            Dialog_InfoCard.InfoCardTab desiredTab = default;
            for (InspectionTreeItem node = row; node != null; node = node.Parent)
            {
                if (node.Data is Dialog_InfoCard.InfoCardTab t)
                {
                    tabNode = node;
                    desiredTab = t;
                    break;
                }
            }
            if (tabNode == null)
            {
                return;
            }

            var liveTab = (Dialog_InfoCard.InfoCardTab)tabField.GetValue(dialog);

            if (!ReferenceEquals(dialog, lastTabDialog))
            {
                lastTabDialog = dialog;
                lastObservedTab = liveTab;
            }

            if (liveTab == desiredTab)
            {
                lastObservedTab = liveTab;
                return;
            }

            if (liveTab == lastObservedTab)
            {
                // Live tab hasn't moved since we last checked — our cursor is the thing that
                // changed, so push it onto the dialog.
                dialog.SetTab(desiredTab);
                lastObservedTab = desiredTab;
                return;
            }

            // Live tab moved since we last checked and it wasn't us asking — a mouse click.
            // Follow it: move the keyboard cursor onto that tab's node instead of forcing it back.
            lastObservedTab = liveTab;
            InspectionTreeItem targetNode = FindTabNode(scope?.TreeRoot, liveTab);
            scope?.SyncCursorToTabNode(targetNode);
        }

        private static InspectionTreeItem FindTabNode(InspectionTreeItem root, Dialog_InfoCard.InfoCardTab tab)
        {
            if (root == null)
            {
                return null;
            }
            foreach (InspectionTreeItem child in root.Children)
            {
                if (child.Data is Dialog_InfoCard.InfoCardTab t && t == tab)
                {
                    return child;
                }
            }
            return null;
        }

        /// <summary>
        /// Walks the row and its Parent chain for the first node carrying a StatDrawEntry —
        /// either directly, or wrapped in an <see cref="EmptyValueStatDatum"/>, whose row
        /// vanilla draws and selects on a click like any other (decompiled
        /// RimWorld/StatDrawEntry.cs:197-244 highlights and clicks every drawn entry,
        /// value or no value). Character/Health/Records/Permits rows have no vanilla per-row
        /// selection (decision I3), so a miss leaves the previous selection untouched
        /// rather than clearing it.
        /// </summary>
        private static void DriveStatSelection(InspectionTreeItem row)
        {
            StatDrawEntry rowEntry = null;
            for (InspectionTreeItem node = row; node != null; node = node.Parent)
            {
                if (node.Data is StatDrawEntry entry)
                {
                    rowEntry = entry;
                    break;
                }
                if (node.Data is EmptyValueStatDatum emptyValueEntry)
                {
                    rowEntry = emptyValueEntry.Entry;
                    break;
                }
            }
            if (rowEntry == null)
            {
                return;
            }

            // Dialog_InfoCard.Setup() calls StatsReportUtility.Reset() from the
            // CONSTRUCTOR (decompiled Verse/Dialog_InfoCard.cs:480-490), so opening a
            // nested card wipes the outer card's cache and the outer card refills it with
            // fresh instances on its next draw — vanilla itself re-resolves the same way
            // at decompiled RimWorld/StatsReportUtility.cs:290. Re-resolve the row's entry
            // against the live cache every pass rather than trusting reference identity.
            List<StatDrawEntry> live = InfoCardDataExtractor.GetStatEntries();
            StatDrawEntry resolved = null;
            for (int i = 0; i < live.Count; i++)
            {
                if (ReferenceEquals(live[i], rowEntry))
                {
                    resolved = live[i];
                    break;
                }
            }
            if (resolved == null)
            {
                for (int i = 0; i < live.Count; i++)
                {
                    if (live[i].Same(rowEntry))
                    {
                        resolved = live[i];
                        break;
                    }
                }
            }
            if (resolved == null)
            {
                return;
            }

            try
            {
                object current = selectedEntryField.GetValue(null);
                if (ReferenceEquals(current, resolved))
                {
                    return;
                }

                selectEntryMethod.Invoke(null, new object[] { resolved, false });

                // Arming ONLY on a change is load-bearing: an armed positioner every pass
                // pins the list to the selected row and the player can never see anything
                // else (decompiled RimWorld/StatsReportUtility.cs:425-430).
                ScrollPositioner positioner = scrollPositionerField.GetValue(null) as ScrollPositioner;
                if (positioner != null)
                {
                    positioner.Arm();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Info card visual driver error", ex);
            }
        }
    }
}
