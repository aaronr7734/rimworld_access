using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Accessibility state for CompPlantable seed planting (Gauranlen seeds and any modded
    /// plantable item). Gauranlen seeds in particular are picky about placement (fertile soil,
    /// no roof, spacing from other Gauranlen trees and pending seed cells, no sow blockers), so
    /// sighted players rely on the ghost overlay and rings to find a spot. This state surfaces
    /// the same information to screen readers:
    ///   - a terse per-tile "Can plant" / "Blocked" prefix during arrow navigation, and
    ///   - a temporary scanner category listing every plantable spot (adjacency-grouped,
    ///     nearest first) so the user can step straight to a valid location via Page Down + Home.
    ///
    /// The Enter confirm already reports why a cell can or can't be planted (the game's own
    /// CanPlantAt message), so there is no separate key for that.
    ///
    /// Opened by a prefix on CompPlantable.BeginTargeting (see PlantTargetingPatch) and closed
    /// by Targeter.StopTargeting. All validation defers to CompPlantable.CanPlantAt so the
    /// wording and rules always match vanilla and any mods that extend planting.
    /// </summary>
    public static class PlantTargetingState
    {
        private static bool isActive = false;
        private static CompPlantable comp = null;
        private static Map map = null;

        // Tracks a building-proximity confirmation dialog opened from a target confirmation, so we
        // can re-open placement if the user cancels it (rather than kicking them out). Independent
        // of the active-session fields above, which Close() clears when the dialog opens.
        private static CompPlantable pendingDialogComp = null;
        private static int pendingDialogPlantCellCount = 0;
        private static Window pendingDialog = null;

        private static readonly MethodInfo BeginTargetingMethod =
            AccessTools.Method(typeof(CompPlantable), "BeginTargeting");

        private static readonly MethodInfo ConnectionStrengthReducedMethod =
            AccessTools.Method(typeof(CompPlantable), "ConnectionStrengthReducedByNearbyBuilding");

        /// <summary>
        /// Gets whether seed-planting placement mode is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Opens (or refreshes) planting placement state for the given plantable comp.
        /// Re-entry on an invalid-cell retry calls BeginTargeting again with the same comp; in
        /// that case we keep the session and stay silent so the start announcement isn't repeated.
        /// </summary>
        public static void Open(CompPlantable plantable)
        {
            if (plantable == null)
                return;

            bool sameSession = isActive && comp == plantable;

            comp = plantable;
            map = plantable.parent?.MapHeld;
            isActive = true;

            if (sameSession || map == null)
                return;

            string treeLabel = plantable.Props?.plantDefToSpawn?.LabelCap
                ?? "RimWorldAccess.Abilities.Label.Location".Translate().ToString();

            // Build the temporary scanner category first (every plantable spot, adjacency-grouped
            // and nearest first) so the start announcement can report how many areas were found —
            // which also confirms to the user that the scanner category is populated.
            int areaCount = BuildScannerCategory(treeLabel, out int clearAreaCount, out bool buildingsReduceStrength);

            string start = areaCount > 0
                ? "RimWorldAccess.Abilities.Plant.Start".Translate(treeLabel, areaCount)
                : "RimWorldAccess.Abilities.Plant.StartNoAreas".Translate(treeLabel);

            // When building proximity matters for this plant (Gauranlen connection strength),
            // tell the user about the clear-of-buildings subcategory — or warn that no such
            // spot exists, which a sighted player would infer from the red rings.
            if (areaCount > 0 && buildingsReduceStrength)
            {
                start += " " + (clearAreaCount > 0
                    ? (string)"RimWorldAccess.Abilities.Plant.StartClearSuffix".Translate(clearAreaCount)
                    : (string)"RimWorldAccess.Abilities.Plant.StartNoneClearSuffix".Translate());
            }

            TolkHelper.SpeakData(start, SpeechPriority.Normal);
        }

        /// <summary>
        /// Closes planting placement state.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            comp = null;
            map = null;

            // Tear down the temporary "plantable spots" category and restore the user's previous
            // scanner focus.
            ScannerState.RemoveTemporaryCategory();
            ScannerState.RestoreFocus();
        }

        /// <summary>
        /// Clears all session and pending-dialog state without side effects. Called on game load
        /// (GameStartPatch): an in-game load fires no Close path, and a stale pending dialog would
        /// otherwise make WatchConfirmationDialog re-open targeting on a comp whose parent is an
        /// orphaned Thing from the previous session (which still reports Spawned == true).
        /// </summary>
        public static void Reset()
        {
            isActive = false;
            comp = null;
            map = null;
            pendingDialog = null;
            pendingDialogComp = null;
            pendingDialogPlantCellCount = 0;
        }

        /// <summary>
        /// Called by TargetingPatch — while planting is still active — when confirming a cell opened
        /// a confirmation dialog (e.g. the Gauranlen "planting near buildings reduces connection
        /// strength" warning). Records what's needed to detect, once that dialog closes, whether the
        /// user confirmed (a plant cell was added) or cancelled (nothing changed). Deliberately does
        /// not touch the active-session fields, which Close() is about to clear.
        /// </summary>
        public static void NotifyConfirmationDialogOpened()
        {
            if (comp == null)
                return;

            // DialogInterceptionPatch swallows WindowStack.Add for Dialog_MessageBox and presents
            // it through WindowlessDialogState, so the dialog is normally found there, not in the
            // WindowStack. Keep the stack lookup as a fallback for a non-intercepted dialog.
            Window dialog = WindowlessDialogState.CurrentDialog as Dialog_MessageBox
                ?? (Window)Find.WindowStack?.WindowOfType<Dialog_MessageBox>();
            if (dialog == null)
                return;

            pendingDialogComp = comp;
            pendingDialogPlantCellCount = comp.PlantCells?.Count ?? 0;
            pendingDialog = dialog;
        }

        /// <summary>
        /// Per-frame check (called from UnifiedKeyboardPatch). Once a tracked confirmation dialog
        /// has closed, re-opens planting placement if the user cancelled it (no new plant cell was
        /// added), so they can immediately try another spot. Confirming ends placement as normal.
        /// </summary>
        public static void WatchConfirmationDialog()
        {
            if (pendingDialog == null)
                return;

            // Dialog still open — keep waiting. An intercepted dialog lives in
            // WindowlessDialogState rather than the WindowStack, so check both.
            if (WindowlessDialogState.CurrentDialog == pendingDialog)
                return;
            if (Find.WindowStack != null && Find.WindowStack.IsOpen(pendingDialog))
                return;

            CompPlantable reopenComp = pendingDialogComp;
            int beforeCount = pendingDialogPlantCellCount;

            // Clear pending state before any re-open so we never double-fire.
            pendingDialog = null;
            pendingDialogComp = null;
            pendingDialogPlantCellCount = 0;

            if (reopenComp == null)
                return;

            // Confirmed: a plant cell was queued — placement ended as normal, nothing to do.
            if ((reopenComp.PlantCells?.Count ?? 0) > beforeCount)
                return;

            // Cancelled: re-open placement so the user can choose another spot — but only if the
            // seed is still around to plant.
            if (reopenComp.parent == null || reopenComp.parent.Destroyed || reopenComp.parent.MapHeld == null)
                return;

            BeginTargetingMethod?.Invoke(reopenComp, null);
        }

        /// <summary>
        /// Returns a terse prefix for per-tile announcements during arrow navigation.
        /// Called by MapArrowKeyHandler.AddContextPrefix() on every cursor move. Kept short so
        /// sweeping is fast; the R key gives the full reason for a blocked cell.
        /// </summary>
        public static string GetPlantValidityPrefix(IntVec3 position)
        {
            if (!isActive || comp == null || map == null || !position.IsValid || !position.InBounds(map))
                return "";

            if (!comp.CanPlantAt(position, map).Accepted)
                return "RimWorldAccess.Abilities.Plant.PrefixBlocked".Translate();

            // Sighted players see red lines to nearby artificial buildings while hovering a
            // plantable cell; surface the same warning so the user knows Enter here will raise
            // the reduced-connection-strength confirmation dialog.
            return IsNearArtificialBuilding(position)
                ? "RimWorldAccess.Abilities.Plant.PrefixCanPlantNearBuildings".Translate()
                : "RimWorldAccess.Abilities.Plant.PrefixCanPlant".Translate();
        }

        /// <summary>
        /// Whether planting at the given cell would trigger the game's "reduced connection
        /// strength" building-proximity warning. Invokes CompPlantable's own private check so
        /// the answer always matches the dialog the game would raise (it returns false for
        /// plants without CompProperties_TreeConnection). Fine for single-cell queries — one
        /// cell at a time is exactly vanilla's hover usage of the underlying per-cell cache.
        /// Never call this in a map-wide sweep; see FilterCellsClearOfArtificialBuildings.
        /// </summary>
        private static bool IsNearArtificialBuilding(IntVec3 cell)
        {
            if (comp == null || ConnectionStrengthReducedMethod == null)
                return false;

            var args = new object[] { cell, null }; // args[1] is the out buildings parameter
            return ConnectionStrengthReducedMethod.Invoke(comp, args) is bool reduced && reduced;
        }

        /// <summary>
        /// Builds a temporary scanner category containing every cell where the seed can be
        /// planted, grouped into contiguous areas by adjacency (a grass field collapses to one
        /// entry; scattered single cells stay as one-tile entries) and sorted nearest first.
        /// The user navigates it with the normal scanner keys (Page Down through areas, Home to
        /// jump the cursor onto the nearest plantable tile of the selected area). The scanner
        /// re-sorts by live distance as the cursor moves, so "nearest" stays correct.
        ///
        /// Runs once per session (the BeginTargeting prefix re-fires on invalid-cell retries, but
        /// Open() short-circuits those before reaching here). The map-wide CanPlantAt sweep is
        /// the cost; CanPlantAt is the same validation the game uses, so the set always matches.
        ///
        /// When the plant has CompProperties_TreeConnection (Gauranlen trees), a second
        /// "clear of buildings" subcategory lists only the spots whose confirmation won't raise
        /// the reduced-connection-strength warning (Shift+Page Down to switch). It is omitted
        /// when it would be redundant (no spot is affected by buildings) or empty (every spot
        /// is; the start announcement warns instead).
        ///
        /// Returns the number of plantable areas found (0 if none). clearAreaCount is the
        /// clear-of-buildings subcategory's area count; buildingsReduceStrength reports whether
        /// building proximity affects at least one plantable spot (i.e. the split matters).
        /// </summary>
        private static int BuildScannerCategory(string treeLabel, out int clearAreaCount, out bool buildingsReduceStrength)
        {
            clearAreaCount = 0;
            buildingsReduceStrength = false;

            if (comp == null || map == null)
                return 0;

            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            if (!cursor.IsValid)
                cursor = comp.parent?.PositionHeld ?? map.Center;

            var validCells = new List<IntVec3>();
            foreach (IntVec3 cell in map.AllCells)
            {
                if (cell.Fogged(map))
                    continue;
                if (comp.CanPlantAt(cell, map).Accepted)
                    validCells.Add(cell);
            }

            if (validCells.Count == 0)
                return 0;

            List<TerrainRegion> regions = ScannerHelper.GroupTerrainByAdjacency(validCells, cursor);
            if (regions == null || regions.Count == 0)
                return 0;

            string itemLabel = "RimWorldAccess.Abilities.Plant.ScannerItemLabel".Translate();
            var allSub = new ScannerSubcategory("RimWorldAccess.Abilities.Plant.SubcatAll".Translate());
            foreach (TerrainRegion region in regions)
                allSub.Items.Add(new ScannerItem(new List<TerrainRegion> { region }, itemLabel, cursor));
            var subcategories = new List<ScannerSubcategory> { allSub };

            // Building-proximity split. Only plants with CompProperties_TreeConnection can
            // trigger the warning, and the radius comes from that comp — same source the game's
            // own check reads.
            var treeConnection = comp.Props?.plantDefToSpawn?.GetCompProperties<CompProperties_TreeConnection>();
            if (treeConnection != null)
            {
                List<IntVec3> clearCells = FilterCellsClearOfArtificialBuildings(
                    validCells, treeConnection.radiusToBuildingForConnectionStrengthLoss);

                // If every plantable cell is clear, the split would just duplicate "All" — skip
                // it (and the start-announcement suffix) as redundant.
                if (clearCells.Count < validCells.Count)
                {
                    buildingsReduceStrength = true;

                    if (clearCells.Count > 0)
                    {
                        // Group the clear subset independently: a field straddling a building's
                        // radius correctly splits into separate clear patches.
                        List<TerrainRegion> clearRegions = ScannerHelper.GroupTerrainByAdjacency(clearCells, cursor);
                        if (clearRegions != null && clearRegions.Count > 0)
                        {
                            var clearSub = new ScannerSubcategory("RimWorldAccess.Abilities.Plant.SubcatClear".Translate());
                            foreach (TerrainRegion region in clearRegions)
                                clearSub.Items.Add(new ScannerItem(new List<TerrainRegion> { region }, itemLabel, cursor));
                            subcategories.Add(clearSub);
                            clearAreaCount = clearRegions.Count;
                        }
                    }
                }
            }

            string categoryName = "RimWorldAccess.Abilities.Plant.ScannerCategory".Translate(treeLabel);

            // The scanner has a single temporary-category slot. If a search filter is active it
            // owns that slot AND RefreshItems rebuilds it from the filter on every navigation —
            // which would clobber our planting category on the user's first Page Down. Clear the
            // filter (silently; our own announcement follows) so planting takes over the slot.
            if (ScannerSearchState.HasActiveFilter)
                ScannerSearchState.ClearSearchSilent();

            // Save the user's current scanner focus so Close() can restore it after removing
            // this category.
            ScannerState.SaveFocus();
            ScannerState.CreateTemporaryCategory(categoryName, subcategories);
            return regions.Count;
        }

        /// <summary>
        /// Returns the subset of cells NOT within the given radius of any spawned artificial
        /// building — the cells where confirming a plant won't raise the reduced-connection-
        /// strength warning. Mirrors CompPlantable.ConnectionStrengthReducedByNearbyBuilding,
        /// but that path queries ListerArtificialBuildingsForMeditation.GetForCell, which
        /// permanently caches a building list PER QUERIED CELL — sweeping every plantable cell
        /// through it would bloat the game's cache with thousands of entries. Instead, collect
        /// the same building set once using the lister's own predicate
        /// (MeditationUtility.CountsAsArtificialBuilding) and stamp the radius around each
        /// occupied cell, matching the radial-distance semantics of the game's check.
        /// </summary>
        private static List<IntVec3> FilterCellsClearOfArtificialBuildings(List<IntVec3> cells, float radius)
        {
            var nearBuilding = new bool[map.cellIndices.NumGridCells];
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (!MeditationUtility.CountsAsArtificialBuilding(thing))
                    continue;

                foreach (IntVec3 occupied in thing.OccupiedRect())
                {
                    foreach (IntVec3 cell in GenRadial.RadialCellsAround(occupied, radius, useCenter: true))
                    {
                        if (cell.InBounds(map))
                            nearBuilding[map.cellIndices.CellToIndex(cell)] = true;
                    }
                }
            }

            var clear = new List<IntVec3>();
            foreach (IntVec3 cell in cells)
            {
                if (!nearBuilding[map.cellIndices.CellToIndex(cell)])
                    clear.Add(cell);
            }
            return clear;
        }
    }
}
