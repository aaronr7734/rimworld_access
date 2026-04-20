using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for transport pod accessibility.
    /// Injects custom "Select pods to group" gizmo and handles dialog lifecycle.
    /// </summary>
    public static class TransportPodPatch
    {
        /// <summary>
        /// Patch to add custom "Select pods to group" gizmo to transport pods.
        /// </summary>
        [HarmonyPatch(typeof(CompTransporter))]
        [HarmonyPatch("CompGetGizmosExtra")]
        public static class CompTransporter_GetGizmos_Patch
        {
            [HarmonyPostfix]
            public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, CompTransporter __instance)
            {
                // Return all original gizmos first
                foreach (var gizmo in __result)
                {
                    yield return gizmo;
                }

                // Only add our custom gizmo if:
                // 1. The pod is not currently loading or ready to launch
                // 2. Pod selection mode is not already active
                if (__instance.LoadingInProgressOrReadyToLaunch)
                    yield break;

                if (TransportPodSelectionState.IsActive)
                    yield break;

                // Capture instance for the delegate
                CompTransporter transporter = __instance;

                // Check if there are any pods that can be grouped with this one
                var groupablePods = TransportPodHelper.GetGroupablePodsFor(transporter, transporter.Map);
                bool hasGroupablePods = groupablePods.Count > 0;

                // Total pods available (this one + all groupable)
                int totalPods = groupablePods.Count + 1;

                // Add "Group all available pods" gizmo - selects all and opens loading dialog
                var groupAllGizmo = new Command_Action
                {
                    defaultLabel = $"Group all available pods ({totalPods})",
                    defaultDesc = $"Select all {totalPods} available transport pods and open the loading dialog.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/SelectAllTransporters", true),
                    action = delegate
                    {
                        GroupAllPodsAndLoad(transporter, groupablePods);
                    }
                };

                if (!hasGroupablePods)
                {
                    groupAllGizmo.Disable("No other transport pods with connected launchers to group with.");
                }

                yield return groupAllGizmo;

                // Add "Select pods to group" gizmo for manual selection (disabled if no pods to group with)
                var selectPodsGizmo = new Command_Action
                {
                    defaultLabel = "Select pods to group",
                    defaultDesc = "Enter pod selection mode to choose which transport pods to group together for loading.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter", true),
                    action = delegate
                    {
                        TransportPodSelectionState.Open(transporter);
                    }
                };

                if (!hasGroupablePods)
                {
                    selectPodsGizmo.Disable("No other transport pods with connected launchers to group with.");
                }

                yield return selectPodsGizmo;
            }
        }

        /// <summary>
        /// Selects all available pods and opens the loading dialog.
        /// </summary>
        private static void GroupAllPodsAndLoad(CompTransporter sourceTransporter, List<CompTransporter> groupablePods)
        {
            if (sourceTransporter?.parent == null)
                return;

            // Select all pods (source + all groupable)
            Find.Selector.ClearSelection();
            Find.Selector.Select(sourceTransporter.parent, playSound: false, forceDesignatorDeselect: false);

            foreach (var pod in groupablePods)
            {
                if (pod?.parent != null)
                {
                    Find.Selector.Select(pod.parent, playSound: false, forceDesignatorDeselect: false);
                }
            }

            int totalSelected = groupablePods.Count + 1;

            // Find the load gizmo from the source transporter
            Command_LoadToTransporter loadCommand = null;
            foreach (var gizmo in sourceTransporter.CompGetGizmosExtra())
            {
                if (gizmo is Command_LoadToTransporter cmd)
                {
                    loadCommand = cmd;
                    break;
                }
            }

            if (loadCommand == null)
            {
                TolkHelper.Speak("Could not find load command", SpeechPriority.High);
                Find.Selector.ClearSelection();
                return;
            }

            // Inherit from other selected pods' gizmos (like TransportPodSelectionState does)
            foreach (var pod in groupablePods)
            {
                if (pod?.parent != null)
                {
                    foreach (var otherGizmo in pod.CompGetGizmosExtra())
                    {
                        if (otherGizmo is Command_LoadToTransporter otherLoadCmd)
                        {
                            loadCommand.InheritInteractionsFrom(otherLoadCmd);
                            break;
                        }
                    }
                }
            }

            TolkHelper.Speak($"Grouping {totalSelected} pods for loading");

            // Open the loading dialog
            loadCommand.ProcessInput(null);
        }

        /// <summary>
        /// Patch for Dialog_LoadTransporters.PostOpen to initialize accessibility state.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_LoadTransporters))]
        [HarmonyPatch("PostOpen")]
        public static class Dialog_LoadTransporters_PostOpen_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_LoadTransporters __instance)
            {
                TransportPodLoadingState.Open(__instance);
            }
        }

        /// <summary>
        /// Patch for Window.PostClose to clean up accessibility state when Dialog_LoadTransporters closes.
        /// Note: We patch Window.PostClose (not Dialog_LoadTransporters.PostClose) because
        /// Dialog_LoadTransporters does NOT override PostClose - it inherits from Window.
        /// Patching a non-existent method on the derived class silently fails.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_LoadTransporters_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                // Only handle Dialog_LoadTransporters
                if (!(__instance is Dialog_LoadTransporters))
                    return;

                // Capture accept state before Close() resets it
                bool wasAccepted = TransportPodLoadingState.AcceptAttempted;

                TransportPodLoadingState.Close();

                // Only announce cancellation if user didn't accept
                // (game announces successful loading initiation)
                if (!wasAccepted)
                {
                    TolkHelper.Speak("Transport pod loading cancelled", SpeechPriority.Normal);
                }
            }
        }

        /// <summary>
        /// Patch for Window.OnCancelKeyPressed to block game's Escape handling
        /// when our overlay menus are active over the loading dialog.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        public static class Window_OnCancelKeyPressed_LoadTransporters_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                // Only intercept for loading transporters dialog
                if (!(__instance is Dialog_LoadTransporters))
                    return true;

                // Block the game's Cancel handling when our overlay menus are active
                if (QuantityMenuState.IsActive || WindowlessInspectionState.IsActive || StatBreakdownState.IsActive)
                {
                    return false; // Skip original method - let our overlay handle the Escape
                }

                // Block if typeahead search is active
                if (TransportPodLoadingState.HasActiveTypeahead)
                {
                    return false;
                }

                return true; // Let original method run
            }
        }

        /// <summary>
        /// Patch for Dialog_LoadTransporters.OnAcceptKeyPressed to block the game's Enter key handling
        /// when our keyboard navigation is active.
        /// Without this, Enter triggers onAcceptButton() which starts loading - even when the user
        /// is just confirming a quantity in an overlay menu.
        /// Note: Event.current.Use() does NOT block RimWorld's KeyBindingDef.Accept handling!
        /// </summary>
        [HarmonyPatch(typeof(Dialog_LoadTransporters))]
        [HarmonyPatch("OnAcceptKeyPressed")]
        public static class Dialog_LoadTransporters_OnAcceptKeyPressed_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                // Allow through if our Accept() method is calling OnAcceptKeyPressed
                if (TransportPodLoadingState.AcceptingFromOurCode)
                {
                    return true; // Let original method run
                }

                // Block the game's Accept handling when our keyboard nav is active
                // Our code handles Enter for item selection, quantity menus, etc.
                if (TransportPodLoadingState.IsActive)
                {
                    return false; // Skip original method
                }
                return true; // Let original method run
            }
        }

        /// <summary>
        /// Patch for Dialog_LoadTransporters.DoWindowContents to draw visual indicator.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_LoadTransporters))]
        [HarmonyPatch("DoWindowContents")]
        public static class Dialog_LoadTransporters_DoWindowContents_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_LoadTransporters __instance, Rect inRect)
            {
                if (!TransportPodLoadingState.IsActive)
                    return;

                // Draw visual indicator that keyboard mode is active
                DrawKeyboardModeIndicator(inRect);
            }

            private static void DrawKeyboardModeIndicator(Rect inRect)
            {
                // Draw indicator in top-left corner
                float indicatorWidth = 250f;
                float indicatorHeight = 30f;
                Rect indicatorRect = new Rect(inRect.x + 10f, inRect.y + 10f, indicatorWidth, indicatorHeight);

                // Draw background
                Color backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.85f);
                Widgets.DrawBoxSolid(indicatorRect, backgroundColor);

                // Draw border
                Widgets.DrawBox(indicatorRect, 1);

                // Draw text
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(indicatorRect, "Keyboard Mode Active");

                // Reset text settings
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;

                // Draw instructions below the indicator
                float instructionsY = indicatorRect.yMax + 5f;
                float instructionsWidth = 400f;
                float instructionsHeight = 45f;
                Rect instructionsRect = new Rect(inRect.x + 10f, instructionsY, instructionsWidth, instructionsHeight);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;

                string instructions = "Left/Right: Tabs | Up/Down: Items | Enter/Space: Quantity\n" +
                                    "Tab: Mass Summary | Alt+I: Inspect | Alt+A: Accept | Esc: Cancel";

                Widgets.Label(instructionsRect, instructions);

                // Reset text settings
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
        }

        /// <summary>
        /// Patch for SelectionDrawer to draw visual feedback for pod selection mode.
        /// </summary>
        [HarmonyPatch(typeof(SelectionDrawer))]
        [HarmonyPatch("DrawSelectionOverlays")]
        public static class SelectionDrawer_PodSelection_Patch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (!TransportPodSelectionState.IsActive)
                    return;

                Map map = Find.CurrentMap;
                if (map == null)
                    return;

                // Draw highlights for selected pods (green)
                foreach (var transporter in TransportPodSelectionState.GetSelectedTransporters())
                {
                    if (transporter?.parent == null)
                        continue;

                    IntVec3 position = transporter.parent.Position;
                    if (position.InBounds(map))
                    {
                        GenDraw.DrawFieldEdges(new List<IntVec3> { position }, Color.green);
                    }
                }

                // Draw highlight at cursor position (yellow) - uses map cursor like other selection modes
                IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
                if (cursorPos.InBounds(map))
                {
                    GenDraw.DrawFieldEdges(new List<IntVec3> { cursorPos }, Color.yellow);
                }
            }
        }

        /// <summary>
        /// Patch for CompLaunchable.StartChoosingDestination to initialize launch targeting state.
        /// </summary>
        [HarmonyPatch(typeof(CompLaunchable))]
        [HarmonyPatch("StartChoosingDestination")]
        public static class CompLaunchable_StartChoosingDestination_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CompLaunchable __instance)
            {
                TransportPodLaunchState.Open(__instance);
            }
        }

        /// <summary>
        /// Patch for Targeter.BeginTargeting to announce instructions when entering drop pod landing mode.
        /// </summary>
        [HarmonyPatch(typeof(Targeter))]
        [HarmonyPatch("BeginTargeting")]
        [HarmonyPatch(new System.Type[] { typeof(TargetingParameters), typeof(System.Action<LocalTargetInfo>), typeof(Pawn), typeof(System.Action), typeof(Texture2D), typeof(bool) })]
        public static class Targeter_BeginTargeting_Announce_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Texture2D mouseAttachment)
            {
                // Only announce for drop pod landing targeting
                if (mouseAttachment == CompLaunchable.TargeterMouseAttachment)
                {
                    TolkHelper.Speak("Select landing spot. Use arrow keys to navigate, Space to confirm.", SpeechPriority.High);
                }
            }
        }

        /// <summary>
        /// Patch for WorldTargeter.BeginTargeting to detect shuttle launches that bypass CompLaunchable.
        /// Permit shuttles (ShipJob_Wait.LaunchAction) and caravan shuttles (CallShuttleToCaravan)
        /// call WorldTargeter.BeginTargeting directly with CompLaunchable.TargeterMouseAttachment,
        /// but never call CompLaunchable.StartChoosingDestination, so TransportPodLaunchState
        /// never activates. This patch catches those cases.
        /// </summary>
        [HarmonyPatch(typeof(WorldTargeter))]
        [HarmonyPatch("BeginTargeting")]
        public static class WorldTargeter_BeginTargeting_ShuttleLaunch_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Texture2D mouseAttachment)
            {
                // Only handle launch targeting (identified by the launch mouse attachment icon)
                if (mouseAttachment != CompLaunchable.TargeterMouseAttachment)
                    return;

                // If TransportPodLaunchState is already active (from CompLaunchable.StartChoosingDestination),
                // don't double-activate
                if (TransportPodLaunchState.IsActive)
                    return;

                // Extract origin tile and max range from context
                PlanetTile originTile = PlanetTile.Invalid;
                int maxRange = 0;

                // Try to find the shuttle on the current map to get its actual max launch distance
                Map currentMap = Find.CurrentMap;
                if (currentMap != null)
                {
                    originTile = currentMap.Tile;
                    maxRange = GetShuttleMaxRangeFromMap(currentMap);
                }
                else
                {
                    // Caravan context: no current map, try to get origin from world selector
                    var selectedObjects = Find.WorldSelector?.SelectedObjects;
                    if (selectedObjects != null)
                    {
                        foreach (var obj in selectedObjects)
                        {
                            if (obj is Caravan caravan)
                            {
                                originTile = caravan.Tile;
                                break;
                            }
                        }
                    }
                    // For caravan shuttles, use the shuttle ship def's max range
                    if (TransportShipDefOf.Ship_Shuttle != null)
                        maxRange = TransportShipDefOf.Ship_Shuttle.maxLaunchDistance;
                }

                TransportPodLaunchState.Open(originTile, maxRange);
            }

            /// <summary>
            /// Finds a shuttle on the map and returns its TransportShipDef's maxLaunchDistance.
            /// Looks for things with CompShuttle that have an active TransportShip parent.
            /// </summary>
            private static int GetShuttleMaxRangeFromMap(Map map)
            {
                // Look for the Shuttle ThingDef on the map
                if (ThingDefOf.Shuttle != null)
                {
                    foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Shuttle))
                    {
                        CompShuttle shuttle = thing.TryGetComp<CompShuttle>();
                        if (shuttle?.shipParent != null)
                        {
                            return shuttle.shipParent.def.maxLaunchDistance;
                        }
                    }
                }

                // Fallback: use the standard shuttle def
                if (TransportShipDefOf.Ship_Shuttle != null)
                    return TransportShipDefOf.Ship_Shuttle.maxLaunchDistance;

                return 0;
            }
        }
    }
}
