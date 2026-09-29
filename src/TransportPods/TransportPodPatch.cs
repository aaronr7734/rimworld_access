using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Transport pod patches: the pod-grouping gizmos, and the loading dialog's lifecycle and key
    /// blockers.
    /// </summary>
    public static class TransportPodPatch
    {
        [HarmonyPatch(typeof(CompTransporter))]
        [HarmonyPatch("CompGetGizmosExtra")]
        public static class CompTransporter_GetGizmos_Patch
        {
            [HarmonyPostfix]
            public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, CompTransporter __instance)
            {
                foreach (var gizmo in __result)
                {
                    yield return gizmo;
                }

                if (__instance.LoadingInProgressOrReadyToLaunch)
                    yield break;

                if (TransportPodSelectionState.IsActive)
                    yield break;

                CompTransporter transporter = __instance;

                var groupablePods = TransportPodHelper.GetGroupablePodsFor(transporter, transporter.Map);
                bool hasGroupablePods = groupablePods.Count > 0;
                int totalPods = groupablePods.Count + 1;

                var groupAllGizmo = new Command_Action
                {
                    defaultLabel = (string)"RimWorldAccess.TransportPods.Gizmo.GroupAllLabel".Translate(totalPods),
                    defaultDesc = (string)"RimWorldAccess.TransportPods.Gizmo.GroupAllDesc".Translate(totalPods),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/SelectAllTransporters", true),
                    action = delegate
                    {
                        GroupAllPodsAndLoad(transporter, groupablePods);
                    }
                };

                if (!hasGroupablePods)
                {
                    groupAllGizmo.Disable((string)"RimWorldAccess.TransportPods.Gizmo.NoGroupable".Translate());
                }

                yield return groupAllGizmo;

                var selectPodsGizmo = new Command_Action
                {
                    defaultLabel = (string)"RimWorldAccess.TransportPods.Gizmo.SelectPodsLabel".Translate(),
                    defaultDesc = (string)"RimWorldAccess.TransportPods.Gizmo.SelectPodsDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter", true),
                    action = delegate
                    {
                        TransportPodSelectionState.Open(transporter);
                    }
                };

                if (!hasGroupablePods)
                {
                    selectPodsGizmo.Disable((string)"RimWorldAccess.TransportPods.Gizmo.NoGroupable".Translate());
                }

                yield return selectPodsGizmo;
            }
        }

        /// <summary>Selects every groupable pod and opens the loading dialog through vanilla's own load gizmo.</summary>
        private static void GroupAllPodsAndLoad(CompTransporter sourceTransporter, List<CompTransporter> groupablePods)
        {
            if (sourceTransporter?.parent == null)
                return;

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
                TolkHelper.Speak("RimWorldAccess.TransportPods.Gizmo.LoadCommandMissing".Loc(), SpeechPriority.High);
                Find.Selector.ClearSelection();
                return;
            }

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

            TolkHelper.Speak("RimWorldAccess.TransportPods.Gizmo.GroupingForLoading".Loc(totalSelected));
            loadCommand.ProcessInput(null);
        }

        /// <summary>
        /// Cleans up when Dialog_LoadTransporters closes. Patches Window.PostClose, the declaring
        /// type: Dialog_LoadTransporters does not override it, and patching a method a derived class
        /// does not declare silently fails.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_LoadTransporters_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_LoadTransporters))
                    return;

                // Captured before Close resets it.
                bool wasAccepted = TransportPodLoadingState.AcceptAttempted;

                TransportPodLoadingState.Close();

                // The game announces a successful loading start itself.
                if (!wasAccepted)
                {
                    TolkHelper.Speak("RimWorldAccess.TransportPods.Loading.Cancelled".Loc(), SpeechPriority.Normal);
                }
            }
        }

        /// <summary>Blocks vanilla's Escape handling while a typeahead search is live over the loading dialog.</summary>
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        public static class Window_OnCancelKeyPressed_LoadTransporters_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (!(__instance is Dialog_LoadTransporters))
                    return true;

                // Deliberately narrow: the quantity and stat-breakdown overlays are modal shell
                // scopes whose Cancel claims already stamp the frame for WindowCancelKeyRouterPatch,
                // and an inspection term here would leave Escape dead for a dialog opened from the
                // inspection tree, which stands inspection down beneath it.
                if (TransportPodLoadingState.HasActiveTypeahead)
                {
                    return false;
                }

                return true; // Let original method run
            }
        }

        /// <summary>
        /// Blocks vanilla's Enter handling while this screen is live: it would otherwise start
        /// loading even as the user confirms a quantity in an overlay. Event.current.Use() does NOT
        /// stop KeyBindingDef.Accept handling, so a prefix is the only way.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_LoadTransporters))]
        [HarmonyPatch("OnAcceptKeyPressed")]
        public static class Dialog_LoadTransporters_OnAcceptKeyPressed_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                // This screen's own Accept path calls straight into OnAcceptKeyPressed.
                if (TransportPodLoadingState.AcceptingFromOurCode)
                {
                    return true;
                }

                if (TransportPodLoadingState.IsActive)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>Draws the keyboard-mode indicator over the loading dialog.</summary>
        [HarmonyPatch(typeof(Dialog_LoadTransporters))]
        [HarmonyPatch("DoWindowContents")]
        public static class Dialog_LoadTransporters_DoWindowContents_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_LoadTransporters __instance, Rect inRect)
            {
                if (!TransportPodLoadingState.IsActive)
                    return;

                DrawKeyboardModeIndicator(inRect);
            }

            private static void DrawKeyboardModeIndicator(Rect inRect)
            {
                float indicatorWidth = 250f;
                float indicatorHeight = 30f;
                Rect indicatorRect = new Rect(inRect.x + 10f, inRect.y + 10f, indicatorWidth, indicatorHeight);

                Color backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.85f);
                Widgets.DrawBoxSolid(indicatorRect, backgroundColor);

                Widgets.DrawBox(indicatorRect, 1);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(indicatorRect, (string)"RimWorldAccess.TransportPods.Overlay.KeyboardModeActive".Translate());

                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;

                float instructionsY = indicatorRect.yMax + 5f;
                float instructionsWidth = 400f;
                float instructionsHeight = 45f;
                Rect instructionsRect = new Rect(inRect.x + 10f, instructionsY, instructionsWidth, instructionsHeight);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;

                string instructions = (string)"RimWorldAccess.TransportPods.Overlay.InstructionsLine1".Translate() + "\n" +
                                    (string)"RimWorldAccess.TransportPods.Overlay.InstructionsLine2".Translate();

                Widgets.Label(instructionsRect, instructions);

                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
        }

        /// <summary>Draws the selection overlay for pod-selection mode.</summary>
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

                IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
                if (cursorPos.InBounds(map))
                {
                    GenDraw.DrawFieldEdges(new List<IntVec3> { cursorPos }, Color.yellow);
                }
            }
        }

        /// <summary>
        /// Opens the launch-targeting state when a pod starts choosing its destination. Vanilla
        /// begins world targeting inside this method, so the shuttle fallback below must stand
        /// down for its duration or it speaks a second, wrong range first.
        /// </summary>
        [HarmonyPatch(typeof(CompLaunchable))]
        [HarmonyPatch("StartChoosingDestination")]
        public static class CompLaunchable_StartChoosingDestination_Patch
        {
            internal static bool Running;

            [HarmonyPrefix]
            public static void Prefix()
            {
                Running = true;
            }

            [HarmonyPostfix]
            public static void Postfix(CompLaunchable __instance, float? overrideFuelLevel)
            {
                Running = false;
                TransportPodLaunchState.Open(__instance, overrideFuelLevel);
            }

            [HarmonyFinalizer]
            public static void Finalizer()
            {
                Running = false;
            }
        }

        /// <summary>Announces the landing-spot prompt when drop-pod landing targeting begins.</summary>
        [HarmonyPatch(typeof(Targeter))]
        [HarmonyPatch("BeginTargeting")]
        [HarmonyPatch(new System.Type[] { typeof(TargetingParameters), typeof(System.Action<LocalTargetInfo>), typeof(Pawn), typeof(System.Action), typeof(Texture2D), typeof(bool) })]
        public static class Targeter_BeginTargeting_Announce_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Texture2D mouseAttachment)
            {
                if (mouseAttachment == CompLaunchable.TargeterMouseAttachment)
                {
                    TolkHelper.Speak("RimWorldAccess.TransportPods.Landing.SelectLandingSpot".Loc(), SpeechPriority.High);
                }
            }
        }

        /// <summary>
        /// Catches shuttle launches that bypass CompLaunchable: permit and caravan shuttles call
        /// WorldTargeter.BeginTargeting directly with the launch mouse attachment but never
        /// StartChoosingDestination, so the launch state would otherwise never activate.
        /// </summary>
        [HarmonyPatch(typeof(WorldTargeter))]
        [HarmonyPatch("BeginTargeting")]
        public static class WorldTargeter_BeginTargeting_ShuttleLaunch_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Texture2D mouseAttachment)
            {
                // The mouse attachment icon is what identifies launch targeting.
                if (mouseAttachment != CompLaunchable.TargeterMouseAttachment)
                    return;

                if (TransportPodLaunchState.IsActive || CompLaunchable_StartChoosingDestination_Patch.Running)
                    return;

                PlanetTile originTile = PlanetTile.Invalid;
                int maxRange = 0;

                Map currentMap = Find.CurrentMap;
                if (currentMap != null)
                {
                    originTile = currentMap.Tile;
                    maxRange = GetShuttleMaxRangeFromMap(currentMap);
                }
                else
                {
                    // Caravan context: no current map, so the origin comes from the world selector.
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
                    if (TransportShipDefOf.Ship_Shuttle != null)
                        maxRange = TransportShipDefOf.Ship_Shuttle.maxLaunchDistance;
                }

                TransportPodLaunchState.Open(originTile, maxRange);
            }

            /// <summary>The maxLaunchDistance of a shuttle on the map with an active TransportShip parent, else the standard shuttle def's.</summary>
            private static int GetShuttleMaxRangeFromMap(Map map)
            {
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

                if (TransportShipDefOf.Ship_Shuttle != null)
                    return TransportShipDefOf.Ship_Shuttle.maxLaunchDistance;

                return 0;
            }
        }
    }
}
