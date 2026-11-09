using System.Collections.Generic;
using Verse;
using UnityEngine;
using Verse.Sound;
using RimWorld;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// The architect tree's activation callbacks: designator selection
    /// routing (zone / material-needing build / plain designator), the
    /// material-selection float menu, and the tree-open entry point.
    ///
    /// No longer a Harmony patch: this class used to
    /// own ALL architect-tree keyboard input via a Priority.High UIRootOnGUI
    /// prefix (the Tab open/close/cancel opener, tree-navigation dispatch,
    /// ']' right-click options, Alt+I info card, and Escape). That input
    /// routing is now driven by the shell — <see cref="ArchitectTreeState"/>'s
    /// own router methods for tree navigation (called from
    /// <c>ArchitectTreeScope</c>), the Tab opener as the ambient
    /// <c>map.architect.toggle</c> claim on <c>MapScope</c> (see
    /// <c>MapScope.Architect.Game.cs</c>), and the per-frame stale-tree
    /// cleanup (world-view force-close) relocated into
    /// <c>ArchitectTreeScopeMirror.Reconcile</c>. What remains here is pure
    /// domain logic with no Harmony attributes: the designator-activation
    /// callbacks or picker helpers other input paths still need to call.
    /// <see cref="OpenArchitectTreeMenu"/>, <see cref="OpenDesignatorRightClickOptions"/>,
    /// and <see cref="OpenDesignatorInfoCard"/> are <c>internal</c> for that reason;
    /// everything else is only ever called from within this class.
    /// </summary>
    public static class ArchitectMenuPatch
    {
        /// <summary>
        /// Opens the architect tree menu with categories and tools.
        /// </summary>
        internal static void OpenArchitectTreeMenu()
        {
            ArchitectState.EnterCategorySelection();

            // Open the tree menu with callback for when a designator is selected
            ArchitectTreeState.Open(OnDesignatorSelected);

            // The real architect panel is this tree's visual host; MainTabWindowLink
            // owns the pairing from here on, closing one when the other goes. Gated on the
            // tree actually staying open: Open() closes itself again when the colony has no
            // categories to build from, and a panel with no tree behind it would linger.
            if (ArchitectTreeState.IsActive)
            {
                MainTabWindowLink.EnsureTabOpen(MainTabWindowLink.Architect);
            }

        }

        /// <summary>
        /// Opens the right-click options for the currently selected designator.
        /// </summary>
        internal static void OpenDesignatorRightClickOptions()
        {
            Designator designator = ArchitectTreeState.GetSelectedDesignator();
            DesignatorOptionsOpener.Open(designator);
        }

        /// <summary>
        /// Opens the info card for the currently selected designator's building/terrain def.
        /// Only available for Designator_Build items; non-build designators show a message.
        /// </summary>
        internal static void OpenDesignatorInfoCard()
        {
            Designator designator = ArchitectTreeState.GetSelectedDesignator();
            if (designator == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Architect.SelectBuildingForInfoCard".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            BuildableDef placingDef = (designator as Designator_Build)?.PlacingDef;
            InfoCardState.TryOpenInfoCardForDef(placingDef);
        }

        /// <summary>
        /// Called when a designator (tool) is selected from the menu.
        /// </summary>
        private static void OnDesignatorSelected(Designator designator)
        {
            // Check if this is a zone designator - enter zone placement directly
            if (IsZoneDesignator(designator))
            {
                EnterZonePlacement(designator);
                return;
            }

            // Check if this is a build designator that needs material selection
            if (designator is Designator_Build buildDesignator)
            {
                BuildableDef buildable = buildDesignator.PlacingDef;

                if (ArchitectHelper.RequiresMaterialSelection(buildable))
                {
                    ShowMaterialMenu(buildable, buildDesignator);
                    return;
                }
            }

            // A mod designator's own ProcessInput may set it up first (a target menu, a map-wide
            // action) before selecting itself; run it as a click would. Its Select routes into
            // placement through DesignatorManagerPatch, and any FloatMenu goes windowless.
            if (ModOverridesProcessInput(designator))
            {
                designator.ProcessInput(new Event { type = EventType.Used });
                return;
            }

            // No material selection needed - go straight to placement
            ArchitectState.EnterPlacementMode(designator);
        }

        private static bool ModOverridesProcessInput(Designator designator)
        {
            System.Type declaring = designator.GetType().GetMethod(nameof(Designator.ProcessInput), new[] { typeof(Event) })?.DeclaringType;
            return declaring != null && declaring.Assembly != typeof(Designator).Assembly;
        }

        /// <summary>
        /// Shows the material selection menu for a buildable.
        /// </summary>
        private static void ShowMaterialMenu(BuildableDef buildable, Designator_Build originalDesignator)
        {
            MaterialHarvestOutcome outcome = MaterialMenuHarvest.TryBuildOptions(
                originalDesignator,
                (material, vanillaAction) => ArchitectState.EnterPlacementMode(originalDesignator, material, vanillaAction),
                out List<FloatMenuOption> options,
                out System.Action vanillaOnClose);

            if (outcome == MaterialHarvestOutcome.Menu)
            {
                ArchitectState.EnterMaterialSelection(buildable, originalDesignator);
                WindowlessFloatMenuState.Open(options, false, playOpenSound: false,
                    onClose: _ => vanillaOnClose?.Invoke());
                return;
            }

            if (outcome == MaterialHarvestOutcome.NoMenu)
            {
                // Vanilla already messaged "NoStuffsToBuildWith" (spoken by the message
                // patch) or the tutor gate refused; just unwind our state.
                ArchitectState.Reset();
                return;
            }

            // Fallback: the legacy hand-copied list (no map, or a mod's ProcessInput threw).
            List<FloatMenuOption> legacy = ArchitectHelper.CreateMaterialOptions(
                buildable,
                (material) => OnMaterialSelected(originalDesignator, material));

            if (legacy.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Architect.NoMaterialsAvailable".Loc(buildable.label));
                ArchitectState.Reset();
                return;
            }

            ArchitectState.EnterMaterialSelection(buildable, originalDesignator);
            WindowlessFloatMenuState.Open(legacy, false);
        }

        /// <summary>
        /// Called when a material is selected.
        /// Sets the material on the original designator and enters placement mode.
        /// </summary>
        private static void OnMaterialSelected(Designator_Build designator, ThingDef material)
        {
            designator.SetStuffDef(material);
            // mirrors the stuff float menu in Designator_Build.ProcessInput; SetStuffDef alone leaves the label in pre-material form
            BuildingReflection.SetWriteStuff(designator, true);

            ArchitectState.EnterPlacementMode(designator, material);
        }

        /// <summary>
        /// Checks if a designator is a zone/area/cell-based designator.
        /// This includes zones (stockpiles, growing zones), areas (home, roof), and other multi-cell designators.
        /// Delegates to ShapeHelper for the type hierarchy check.
        /// </summary>
        private static bool IsZoneDesignator(Designator designator)
        {
            return ShapeHelper.IsCellsDesignator(designator) || ShapeHelper.IsZoneDesignator(designator);
        }

        /// <summary>
        /// Enters zone placement mode with rectangle selection.
        /// </summary>
        private static void EnterZonePlacement(Designator designator)
        {
            ArchitectState.EnterPlacementMode(designator);
            string zoneName = designator.Label ?? "zone";
        }
    }
}
