using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch for UIRoot.UIRootOnGUI to add accessible order-giving with the Enter key.
    /// When a pawn is selected and Enter is pressed, shows a menu of interaction options
    /// for objects at the current map cursor position.
    /// Implements a two-stage flow: first select target (if multiple), then select action.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class OrderGivingPatch
    {
        /// <summary>
        /// Prefix patch that intercepts Enter key to trigger order-giving mode.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix()
        {
            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            KeyCode key = Event.current.keyCode;

            // If windowless menu is active, handle navigation keys
            if (WindowlessFloatMenuState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    WindowlessFloatMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessFloatMenuState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessFloatMenuState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessFloatMenuState.Close();
                    ClipboardHelper.CopyToClipboard("Menu closed");
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                }
                return;
            }

            // Only process Enter key for opening menu
            if (key != KeyCode.Return && key != KeyCode.KeypadEnter)
                return;

            // Only process during normal gameplay with a valid map
            if (Find.CurrentMap == null)
                return;

            // Don't process if any dialog or window that prevents camera motion is open
            if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                return;

            // Check if map navigation is initialized
            if (!MapNavigationState.IsInitialized)
                return;

            // Check if any pawns are selected
            if (Find.Selector == null || !Find.Selector.SelectedPawns.Any())
            {
                ClipboardHelper.CopyToClipboard("No pawn selected");
                Event.current.Use();
                return;
            }

            // Get the cursor position
            IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;

            // Validate cursor position
            if (!cursorPosition.IsValid || !cursorPosition.InBounds(map))
            {
                ClipboardHelper.CopyToClipboard("Invalid position");
                Event.current.Use();
                return;
            }

            // Get all things at the cursor position
            List<Thing> thingsAtPosition = cursorPosition.GetThingList(map);

            // Filter to interactable things (pawns, buildings, items - exclude plants, terrain, etc.)
            List<Thing> interactableThings = GetInteractableThings(thingsAtPosition);

            // Get selected pawns
            List<Pawn> selectedPawns = Find.Selector.SelectedPawns.ToList();

            // Get all available actions for this position using RimWorld's built-in system
            Vector3 clickPos = cursorPosition.ToVector3Shifted();
            List<FloatMenuOption> options = FloatMenuMakerMap.GetOptions(
                selectedPawns,
                clickPos,
                out FloatMenuContext context
            );

            if (options != null && options.Count > 0)
            {
                // Open the windowless menu with these options
                WindowlessFloatMenuState.Open(options, true); // true = gives colonist orders
            }
            else
            {
                ClipboardHelper.CopyToClipboard("No available actions");
            }

            // Consume the event
            Event.current.Use();
        }

        /// <summary>
        /// Filters a list of things to only interactable items.
        /// Excludes plants, terrain, blueprints, and other non-interactable objects.
        /// </summary>
        private static List<Thing> GetInteractableThings(List<Thing> things)
        {
            var interactable = new List<Thing>();

            foreach (Thing thing in things)
            {
                // Skip plants (they're usually not directly interactable)
                if (thing is Plant)
                    continue;

                // Skip blueprints and frames (they're under construction)
                if (thing is Blueprint || thing is Frame)
                    continue;

                // Skip filth
                if (thing is Filth)
                    continue;

                // Skip gas clouds
                if (thing.def.category == ThingCategory.Gas)
                    continue;

                // Skip motes and projectiles
                if (thing is Mote || thing is Projectile)
                    continue;

                // Include pawns, buildings, and items
                if (thing is Pawn || thing is Building || thing.def.category == ThingCategory.Item)
                {
                    interactable.Add(thing);
                }
            }

            return interactable;
        }

        /// <summary>
        /// Shows a menu to select which target to interact with when multiple things are present.
        /// This is the first stage of the two-stage flow.
        /// </summary>
        private static void ShowTargetSelectionMenu(List<Thing> targets, List<Pawn> selectedPawns, IntVec3 position, Map map)
        {
            var options = new List<FloatMenuOption>();

            // Sort targets by priority: pawns first, then buildings, then items
            var sortedTargets = targets.OrderBy(t =>
            {
                if (t is Pawn) return 0;
                if (t is Building) return 1;
                return 2;
            }).ToList();

            foreach (Thing target in sortedTargets)
            {
                // Create label with thing name and category
                string label = GetThingLabel(target);

                // Create option that leads to action menu for this target
                FloatMenuOption option = new FloatMenuOption(label, delegate
                {
                    // Show action menu for selected target
                    ShowActionMenuForTarget(target, selectedPawns, position, map);
                });

                options.Add(option);
            }

            // Add "Cancel" option
            options.Add(new FloatMenuOption("Cancel", delegate
            {
                ClipboardHelper.CopyToClipboard("Cancelled");
            }));

            if (options.Count > 0)
            {
                FloatMenu menu = new FloatMenu(options, "Select target:", false);
                Find.WindowStack.Add(menu);
                FloatMenuNavigationState.IsTargetSelectionMode = true;
                FloatMenuNavigationState.CurrentMenu = menu;
            }
        }

        /// <summary>
        /// Shows the action menu for a specific target using RimWorld's FloatMenuMakerMap.
        /// This is the second stage of the two-stage flow, or the only stage if there's a single target.
        /// </summary>
        private static void ShowActionMenuForTarget(Thing target, List<Pawn> selectedPawns, IntVec3 position, Map map)
        {
            // Use RimWorld's built-in system to generate options for this target
            Vector3 clickPos = position.ToVector3Shifted();

            // Get options from FloatMenuMakerMap
            List<FloatMenuOption> options = FloatMenuMakerMap.GetOptions(
                selectedPawns,
                clickPos,
                out FloatMenuContext context
            );

            // Filter options to those relevant to the specific target
            // (if we have a specific target and not just showing general options)
            if (target != null)
            {
                // The options system already filters based on what's at the position,
                // so we can use the options as-is
            }

            if (options != null && options.Count > 0)
            {
                FloatMenuMap menu = new FloatMenuMap(options, target?.LabelShort ?? "Orders", clickPos);
                Find.WindowStack.Add(menu);
                FloatMenuNavigationState.IsTargetSelectionMode = false;
                FloatMenuNavigationState.CurrentMenu = menu;
            }
            else
            {
                ClipboardHelper.CopyToClipboard("No available actions");
            }
        }

        /// <summary>
        /// Shows general action menu when no specific target is selected.
        /// Typically shows options like "Go here" for movement orders.
        /// </summary>
        private static void ShowGeneralActionMenu(List<Pawn> selectedPawns, IntVec3 position, Map map)
        {
            Vector3 clickPos = position.ToVector3Shifted();

            // Get all options for this position
            List<FloatMenuOption> options = FloatMenuMakerMap.GetOptions(
                selectedPawns,
                clickPos,
                out FloatMenuContext context
            );

            if (options != null && options.Count > 0)
            {
                FloatMenuMap menu = new FloatMenuMap(options, "Orders", clickPos);
                Find.WindowStack.Add(menu);
                FloatMenuNavigationState.IsTargetSelectionMode = false;
                FloatMenuNavigationState.CurrentMenu = menu;
            }
            else
            {
                ClipboardHelper.CopyToClipboard("No available actions");
            }
        }

        /// <summary>
        /// Gets a descriptive label for a thing, including its type/category.
        /// </summary>
        private static string GetThingLabel(Thing thing)
        {
            string label = thing.LabelShort;
            string category = "";

            if (thing is Pawn pawn)
            {
                // Add pawn type info
                if (pawn.IsColonist)
                    category = "colonist";
                else if (pawn.IsPrisonerOfColony)
                    category = "prisoner";
                else if (pawn.HostileTo(Faction.OfPlayer))
                    category = "hostile";
                else if (pawn.RaceProps.Animal)
                    category = "animal";
                else
                    category = "pawn";
            }
            else if (thing is Building)
            {
                category = "building";
            }
            else if (thing.def.category == ThingCategory.Item)
            {
                if (thing.stackCount > 1)
                    label += " x" + thing.stackCount;
                category = "item";
            }

            if (!string.IsNullOrEmpty(category))
                return $"{label} ({category})";
            else
                return label;
        }
    }
}
