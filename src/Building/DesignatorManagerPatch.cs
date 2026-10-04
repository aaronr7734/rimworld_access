using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Cleans up accessibility state when the game truly deselects a designator (Escape, a click
    /// elsewhere, or placement completing). Switching between designators is NOT a true deselect:
    /// <see cref="DesignatorManagerPatch.IsInSelectOperation"/> distinguishes the two.
    /// </summary>
    [HarmonyPatch(typeof(Designator))]
    [HarmonyPatch("Deselected")]
    public static class DesignatorDeselectedPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // The harvest's restoring Deselect is not a true deselect (see MaterialMenuHarvest).
            if (MaterialMenuHarvest.InFlight)
                return;

            if (DesignatorManagerPatch.IsInSelectOperation)
            {
                return;
            }

            if (ShapePlacementState.CurrentPhase != PlacementPhase.Inactive)
            {
                ShapePlacementState.Reset();
            }

            if (ArchitectState.CurrentMode != ArchitectMode.Inactive)
            {
                ArchitectState.Reset();
            }

            if (GizmoZoneEditState.IsActive)
            {
                GizmoZoneEditState.Reset();
            }

            // Prevents a stale area selection outliving the designator.
            if (Designator_AreaAllowed.selectedArea != null)
            {
                Designator_AreaAllowed.ClearSelectedArea();
            }
        }
    }

    /// <summary>Speaks the exit when vanilla drops a keyboard placement whose designator fails
    /// CanRemainSelected; every other exit deselects directly and announces itself.</summary>
    [HarmonyPatch(typeof(DesignatorManager), "CheckSelectedDesignatorValid")]
    public static class DesignatorDroppedSpeechPatch
    {
        [HarmonyPrefix]
        public static void Prefix(DesignatorManager __instance, out bool __state)
        {
            __state = __instance.SelectedDesignator != null
                && (ArchitectState.IsInPlacementMode || ShapePlacementState.IsActive);
        }

        [HarmonyPostfix]
        public static void Postfix(bool __result, bool __state)
        {
            if (__state && !__result)
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.ExitedPlacement".Loc());
        }
    }

    /// <summary>
    /// The single entry point for accessible placement mode: every placement, from the architect
    /// menu, a gizmo or anywhere else, flows through DesignatorManager.Select() and is routed
    /// here into <see cref="ShapePlacementState"/>. A Select a mouse click drove is left entirely
    /// to vanilla — see <see cref="MouseOriginatedSelect"/>.
    /// </summary>
    [HarmonyPatch(typeof(DesignatorManager))]
    [HarmonyPatch("Select")]
    public static class DesignatorManagerPatch
    {
        /// <summary>True inside a Select() operation, so DesignatorDeselectedPatch can tell a designator switch from a true deselect.</summary>
        public static bool IsInSelectOperation { get; private set; } = false;

        [HarmonyPrefix]
        public static void Prefix()
        {
            IsInSelectOperation = true;
        }

        /// <summary>Clears the flag even if Select() throws, so it cannot stick true.</summary>
        [HarmonyFinalizer]
        public static void Finalizer()
        {
            IsInSelectOperation = false;
        }

        [HarmonyPostfix]
        public static void Postfix(Designator des)
        {
            IsInSelectOperation = false;

            // A harvest's transient Select must not enter placement (see MaterialMenuHarvest).
            if (MaterialMenuHarvest.InFlight)
                return;

            if (MouseOriginatedSelect())
                return;

            if (ShapePlacementState.IsActive)
            {
                if (ShapePlacementState.ActiveDesignator == des)
                    return;

                // A different tool selected under live placement (an eyedropper's pick): placement
                // restarts on it; the architect flow re-enters through its own mode so its tracked
                // designator follows, and that nested Select lands back here with placement inactive.
                ShapePlacementState.Reset();
                if (ArchitectState.IsInPlacementMode)
                {
                    ArchitectState.EnterPlacementMode(des);
                    return;
                }
            }

            if (des is Designator_Place)
            {
                RouteToAccessiblePlacement(des);
                return;
            }

            if (ShapeHelper.IsZoneDesignator(des))
            {
                RouteToAccessiblePlacement(des);
                return;
            }

            // Paint designators also pop a visual color-swatch grid the keyboard flow never
            // triggers, so open the accessible color picker after entering placement.
            if (PaintColorHelper.IsPaintDesignator(des))
            {
                // Hold the entry announcement so the picker speaks first; it replays afterwards.
                ShapePlacementState.SuppressNextEntryAnnouncement = true;
                RouteToAccessiblePlacement(des);
                PaintColorHelper.OpenColorPicker((Designator_Paint)des);
                return;
            }

            // The plan tool works like paint. Gated on the game's own CanSelectColor flag, so the
            // Expand variant — which adopts an existing plan's color — falls to the generic
            // routing below.
            if (PlanColorHelper.IsPlanColorDesignator(des))
            {
                ShapePlacementState.SuppressNextEntryAnnouncement = true;
                RouteToAccessiblePlacement(des);
                PlanColorHelper.OpenColorPicker((Designator_Plan_Add)des);
                return;
            }

            if (ShapeHelper.IsOrderDesignator(des))
            {
                RouteToAccessiblePlacement(des);
                return;
            }

            if (ShapeHelper.IsCellsDesignator(des))
            {
                RouteToAccessiblePlacement(des);
                return;
            }

            if (BuildingReflection.IsGravshipDesignator(des))
            {
                RouteToAccessiblePlacement(des);

                var marker = BuildingReflection.GetGravshipMarker(des);
                if (marker?.gravship != null)
                {
                    CellRect bounds = marker.gravship.Bounds;
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.GravshipLandingPrompt".Loc(bounds.Width, bounds.Height));
                }
                return;
            }

            // Everything the manager selects is a cell tool (ProcessInputEvents clicks cells into
            // CanDesignateCell/DesignateSingleCell), so an unrecognized designator -- the
            // eyedroppers carry no draw-style category -- still gets manual placement.
            RouteToAccessiblePlacement(des);
        }

        /// <summary>
        /// True when this Select is a sighted player's mouse click and nothing else: the shell is
        /// not running a keyboard action, and a live mouse event is driving the GUI pass. Purely
        /// observational — the event is never Used.
        ///
        /// Must read rawType, not type: GUI.DoControl calls Use() before ButtonInvisible returns
        /// true, so by the time ProcessInput runs, type is already Used while rawType still
        /// carries the MouseUp. A vanilla gizmo hotkey is Used the same way but stays rawType
        /// KeyDown, so it keeps the accessible route.
        ///
        /// Deliberately NOT "unmarked means mouse": vanilla also selects designators with no user
        /// input at all (GravshipUtility's arrival select runs from a LongEventHandler callback),
        /// and standing down there would drop the placement announcement.
        /// </summary>
        private static bool MouseOriginatedSelect()
        {
            // Must precede any event inspection: shell claim handlers spoof Event.current here.
            if (ShellKeyboardOrigin.Active)
                return false;

            // Our own surfaces execute an activation inside the row's mouse event, which would
            // read as a sighted click under rawType.
            if (WindowlessFloatMenuState.IsExecutingOption || GizmoNavigationState.IsExecutingGizmo)
                return false;

            Event ev = Event.current;
            if (ev == null)
                return false;

            return ev.rawType == EventType.MouseDown
                || ev.rawType == EventType.MouseUp
                || ev.rawType == EventType.MouseDrag;
        }

        /// <summary>
        /// Routes a designator into <see cref="ShapePlacementState"/>, even for single-cell
        /// buildings. Area designators prompt for area selection first, unless one is already
        /// selected.
        /// </summary>
        private static void RouteToAccessiblePlacement(Designator designator)
        {
            if (ShapeHelper.IsAreaDesignator(designator))
            {
                if (Designator_AreaAllowed.selectedArea != null)
                {
                    EnterPlacementWithDesignator(designator);
                    return;
                }

                AreaSelectionMenuState.Open(designator, (area) => {
                    Designator_AreaAllowed.selectedArea = area;
                    EnterPlacementWithDesignator(designator);
                });
                return;
            }

            EnterPlacementWithDesignator(designator);
        }

        /// <summary>
        /// Enters shape placement, defaulting the shape from the game's own selected draw style
        /// so RimWorld's "Remember Draw Styles" setting is honored.
        /// </summary>
        private static void EnterPlacementWithDesignator(Designator designator)
        {
            var availableShapes = ShapeHelper.GetAvailableShapes(designator);
            ShapeType defaultShape = ShapeType.Manual;

            var designatorManager = Find.DesignatorManager;
            if (designatorManager?.SelectedStyle != null)
            {
                ShapeType rememberedShape = ShapeHelper.DrawStyleDefToShapeType(designatorManager.SelectedStyle);

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
