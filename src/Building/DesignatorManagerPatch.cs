using HarmonyLib;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch on Designator.Deselected() to automatically clean up
    /// accessibility state when the game truly deselects a designator.
    ///
    /// This fires when:
    /// - User presses Escape to cancel
    /// - User clicks elsewhere on the map
    /// - Placement completes and the designator is done
    ///
    /// It does NOT reset state when switching between designators, because
    /// DesignatorManagerPatch uses a flag to track when we're inside a Select() operation.
    /// </summary>
    [HarmonyPatch(typeof(Designator))]
    [HarmonyPatch("Deselected")]
    public static class DesignatorDeselectedPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Skip cleanup if we're in the middle of a Select() operation
            // (i.e., switching to a new designator, not truly deselecting)
            if (DesignatorManagerPatch.IsInSelectOperation)
            {
                return;
            }

            // Truly deselecting - clean up accessibility state
            if (ShapePlacementState.CurrentPhase != PlacementPhase.Inactive)
            {
                Log.Message("[DesignatorDeselectedPatch] Cleaning up ShapePlacementState on true deselect");
                ShapePlacementState.Reset();
            }

            if (ArchitectState.CurrentMode != ArchitectMode.Inactive)
            {
                Log.Message("[DesignatorDeselectedPatch] Cleaning up ArchitectState on true deselect");
                ArchitectState.Reset();
            }

            // Clean up gizmo zone edit state
            if (GizmoZoneEditState.IsActive)
            {
                Log.Message("[DesignatorDeselectedPatch] Cleaning up GizmoZoneEditState on true deselect");
                GizmoZoneEditState.Reset();
            }

            // Clean up area designator selected area to prevent stale selection
            if (Designator_AreaAllowed.selectedArea != null)
            {
                Designator_AreaAllowed.ClearSelectedArea();
            }
        }
    }

    /// <summary>
    /// Harmony patch on DesignatorManager.Select() to intercept ALL placements
    /// and route them through the accessible placement system.
    ///
    /// This is the SINGLE entry point for accessible placement mode. All placements
    /// (whether from architect menu, gizmos, or other sources) flow through here.
    ///
    /// This patch provides:
    /// - Screen reader announcements for what is being placed
    /// - Keyboard navigation via ShapePlacementState
    /// - Proper integration with the accessible architect system
    /// </summary>
    [HarmonyPatch(typeof(DesignatorManager))]
    [HarmonyPatch("Select")]
    public static class DesignatorManagerPatch
    {
        /// <summary>
        /// Flag indicating we're inside a Select() operation.
        /// Used by DesignatorDeselectedPatch to distinguish between
        /// switching designators vs truly deselecting.
        /// </summary>
        public static bool IsInSelectOperation { get; private set; } = false;

        /// <summary>
        /// Prefix patch that sets the flag before Select() runs.
        /// This lets DesignatorDeselectedPatch know that Deselected() is being
        /// called as part of switching to a new designator, not a true deselect.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix()
        {
            IsInSelectOperation = true;
        }

        /// <summary>
        /// Finalizer ensures the flag is cleared even if Select() throws an exception.
        /// This prevents the flag from getting stuck in the true state.
        /// </summary>
        [HarmonyFinalizer]
        public static void Finalizer()
        {
            IsInSelectOperation = false;
        }

        /// <summary>
        /// Postfix patch that intercepts designator selection and routes placement
        /// designators through the accessible system.
        /// </summary>
        /// <param name="des">The designator being selected</param>
        [HarmonyPostfix]
        public static void Postfix(Designator des)
        {
            // Clear the flag now that Select() is complete
            IsInSelectOperation = false;

            // Skip if ShapePlacementState is already active
            if (ShapePlacementState.IsActive)
            {
                return;
            }

            // Handle Designator_Place (Build, Install, etc.)
            if (des is Designator_Place)
            {
                RouteToAccessiblePlacement(des);
                return;
            }

            // Handle zone designators (ZoneAdd for expand, ZoneDelete for shrink)
            // These support shape-based placement just like building designators
            if (ShapeHelper.IsZoneDesignator(des))
            {
                RouteToAccessiblePlacement(des);
                return;
            }

            // Handle paint designators (Paint Floor / Paint Building, plus modded Designator_Paint).
            // These are order-type designators for cell placement, but vanilla also pops a visual
            // color-swatch grid the keyboard flow never triggers. Route to placement, then auto-open
            // the accessible color picker so the user can pick a color before painting.
            if (PaintColorHelper.IsPaintDesignator(des))
            {
                // Hold the placement entry announcement so the color picker speaks first; the picker
                // replays it after the user picks a color (or cancels).
                ShapePlacementState.SuppressNextEntryAnnouncement = true;
                RouteToAccessiblePlacement(des);
                PaintColorHelper.OpenColorPicker((Designator_Paint)des);
                return;
            }

            // Handle the plan-add designator (architect "Plan" tool). Like paint, it is an
            // order-type cell designator, but vanilla also pops a visual color-swatch grid the
            // keyboard flow never triggers (and whose options carry no text label anyway). Route to
            // placement, then auto-open the accessible color picker so the user can pick a plan
            // color before drawing. Gated on the game's CanSelectColor flag so the Expand variant
            // (which adopts an existing plan's color) is left to the generic routing below.
            if (PlanColorHelper.IsPlanColorDesignator(des))
            {
                ShapePlacementState.SuppressNextEntryAnnouncement = true;
                RouteToAccessiblePlacement(des);
                PlanColorHelper.OpenColorPicker((Designator_Plan_Add)des);
                return;
            }

            // Handle order designators (Hunt, Haul, Tame, etc.)
            if (ShapeHelper.IsOrderDesignator(des))
            {
                RouteToAccessiblePlacement(des);
                return;
            }

            // Handle cells designators (Mine, Cut Plants, etc.)
            if (ShapeHelper.IsCellsDesignator(des))
            {
                RouteToAccessiblePlacement(des);
                return;
            }

            // Handle gravship landing placement designator
            if (des.GetType().Name == "Designator_MoveGravship")
            {
                RouteToAccessiblePlacement(des);

                // Announce gravship footprint size
                var markerField = AccessTools.Field(des.GetType(), "marker");
                if (markerField != null)
                {
                    var marker = markerField.GetValue(des);
                    if (marker != null)
                    {
                        var gravshipField = AccessTools.Field(marker.GetType(), "gravship");
                        var gravship = gravshipField?.GetValue(marker);
                        if (gravship != null)
                        {
                            var boundsField = AccessTools.Property(gravship.GetType(), "Bounds");
                            if (boundsField != null)
                            {
                                var bounds = (CellRect)boundsField.GetValue(gravship);
                                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.GravshipLandingPrompt".Loc(bounds.Width, bounds.Height));
                            }
                        }
                    }
                }
                return;
            }
        }

        /// <summary>
        /// Routes a designator to the accessible placement system.
        /// Always uses ShapePlacementState for consistent UX, even for single-cell buildings.
        /// Works with both placement designators (Build, Install) and zone designators.
        /// Area designators (Expand/Clear allowed area) prompt for area selection first.
        /// </summary>
        /// <param name="designator">The designator to route</param>
        private static void RouteToAccessiblePlacement(Designator designator)
        {
            if (ShapeHelper.IsAreaDesignator(designator))
            {
                // If an area is already selected (e.g., from WindowlessAreaState),
                // skip the selection menu and go directly to placement
                if (Designator_AreaAllowed.selectedArea != null)
                {
                    EnterPlacementWithDesignator(designator);
                    return;
                }

                // No area selected yet - show selection menu
                AreaSelectionMenuState.Open(designator, (area) => {
                    Designator_AreaAllowed.selectedArea = area;
                    EnterPlacementWithDesignator(designator);
                });
                return;
            }

            EnterPlacementWithDesignator(designator);
        }

        /// <summary>
        /// Enters the shape placement system with the given designator.
        /// Determines the default shape based on RimWorld's "Remember Draw Styles" setting
        /// and starts ShapePlacementState.
        /// </summary>
        /// <param name="designator">The designator to place with</param>
        private static void EnterPlacementWithDesignator(Designator designator)
        {
            var availableShapes = ShapeHelper.GetAvailableShapes(designator);
            ShapeType defaultShape = ShapeType.Manual;

            // Read game's remembered/selected style (set by DesignatorManager.Select())
            // This respects RimWorld's "Remember Draw Styles" setting automatically
            var designatorManager = Find.DesignatorManager;
            if (designatorManager?.SelectedStyle != null)
            {
                ShapeType rememberedShape = ShapeHelper.DrawStyleDefToShapeType(designatorManager.SelectedStyle);

                // Use remembered shape if available for this designator
                if (availableShapes.Contains(rememberedShape))
                {
                    defaultShape = rememberedShape;
                }
                else if (availableShapes.Count > 0)
                {
                    defaultShape = availableShapes[0];
                }
            }
            else if (availableShapes.Count > 0)
            {
                defaultShape = availableShapes[0];
            }

            ShapePlacementState.Enter(designator, defaultShape);
        }
    }
}
