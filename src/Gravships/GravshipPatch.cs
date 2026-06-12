using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    public static class GravshipPatch
    {
        /// <summary>
        /// Activate gravship destination targeting when CompPilotConsole starts choosing destination.
        /// Captures the launching parameter to distinguish launch vs view-range mode.
        /// </summary>
        [HarmonyPatch(typeof(CompPilotConsole), "StartChoosingDestination_NewTemp")]
        public static class CompPilotConsole_StartChoosingDestination_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CompPilotConsole __instance, bool launching)
            {
                try
                {
                    GravshipDestinationState.Open(__instance, launching);
                }
                catch (Exception ex)
                {
                    Log.Error($"[GravshipPatch] Error in StartChoosingDestination postfix: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Close gravship destination state when TilePicker stops targeting.
        /// </summary>
        [HarmonyPatch(typeof(TilePicker), "StopTargeting")]
        public static class TilePicker_StopTargeting_Patch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (GravshipDestinationState.IsActive)
                {
                    GravshipDestinationState.Close();
                }
            }
        }

        /// <summary>
        /// Add a rename gizmo to Building_GravEngine.
        /// The game has no rename gizmo — renaming is only through the inspect panel name field.
        /// </summary>
        [HarmonyPatch(typeof(Building_GravEngine), "GetGizmos")]
        public static class Building_GravEngine_GetGizmos_Patch
        {
            [HarmonyPostfix]
            public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Building_GravEngine __instance)
            {
                foreach (var gizmo in __result)
                {
                    yield return gizmo;
                }

                // Only show rename if engine has been inspected and is owned by player
                if (__instance.Spawned && __instance.Faction == Faction.OfPlayer &&
                    Find.ResearchManager.gravEngineInspected)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "Rename".Translate(),
                        defaultDesc = "RimWorldAccess.Gravships.Engine.RenameDesc".Translate(),
                        icon = ContentFinder<Texture2D>.Get("UI/Buttons/Rename", true),
                        action = delegate
                        {
                            Find.WindowStack.Add(new Dialog_RenameGravship(__instance));
                        }
                    };
                }
            }
        }

        /// <summary>
        /// Add accessible gizmos to GravshipLandingMarker for Abort, Move, and Confirm Landing.
        /// The game draws these as mouse-only buttons in WorldComponent_GravshipController.WorldComponentOnGUI().
        /// We expose them as standard gizmos so they appear in GizmoNavigationState.
        /// </summary>
        [HarmonyPatch(typeof(Thing), "GetGizmos")]
        public static class GravshipLandingMarker_GetGizmos_Patch
        {
            [HarmonyPostfix]
            public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Thing __instance)
            {
                // Yield all original gizmos first
                foreach (var gizmo in __result)
                {
                    yield return gizmo;
                }

                // Only add gravship landing gizmos for GravshipLandingMarker
                if (!(__instance is GravshipLandingMarker marker))
                    yield break;

                var controller = Find.GravshipController;
                if (controller == null)
                    yield break;

                // "Abort Landing" gizmo — only available when landing on an existing map
                // Mirrors the vanilla AbortLandGravship button which only shows when landingMap != null
                var landingMapField = AccessTools.Field(typeof(WorldComponent_GravshipController), "landingMap");
                var landingMap = landingMapField?.GetValue(controller) as Map;
                if (landingMap != null)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "AbortLandGravship".Translate(),
                        defaultDesc = "AbortLandGravshipDesc".Translate(),
                        action = delegate
                        {
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                                "AbortLandConfirmation".Translate(),
                                delegate
                                {
                                    var abortMethod = AccessTools.Method(typeof(WorldComponent_GravshipController), "AbortLanding");
                                    abortMethod?.Invoke(Find.GravshipController, null);
                                }));
                        }
                    };
                }

                // "Move Gravship" gizmo — enters placement designator
                yield return new Command_Action
                {
                    defaultLabel = "DesignatorMoveGravship".Translate(),
                    defaultDesc = "DesignatorMoveGravshipDesc".Translate(),
                    action = delegate
                    {
                        var ctrl = Find.GravshipController;
                        if (ctrl != null)
                        {
                            var designator = ctrl.MoveDesignator();
                            if (designator != null)
                            {
                                Find.DesignatorManager.Select(designator);
                            }
                        }
                    }
                };

                // "Confirm Landing" gizmo — finalizes gravship landing
                yield return new Command_Action
                {
                    defaultLabel = "ConfirmLandGravship".Translate(),
                    defaultDesc = "ConfirmLandGravshipDesc".Translate(),
                    action = delegate
                    {
                        var ctrl = Find.GravshipController;
                        if (ctrl == null) return;

                        var designator = ctrl.MoveDesignator();

                        if (marker.Spawned && Find.DesignatorManager.SelectedDesignator != designator)
                        {
                            marker.BeginLanding(ctrl);
                            // Clear landingMarker to dismiss the confirmation UI (mirrors vanilla behavior)
                            var landingMarkerField = AccessTools.Field(typeof(WorldComponent_GravshipController), "landingMarker");
                            landingMarkerField?.SetValue(ctrl, null);
                            SoundDefOf.Gravship_Land.PlayOneShotOnCamera();
                            TolkHelper.Speak("ConfirmLandGravship".Loc());
                        }
                        else if (Find.DesignatorManager.SelectedDesignator == designator)
                        {
                            TolkHelper.Speak("GravshipLandingMarkerNotPlaced".Loc());
                        }
                    }
                };
            }
        }

        /// <summary>
        /// Announce guidance when GravshipLandingMarker spawns (not on load).
        /// Tells the user to navigate to the landing zone and use gizmos.
        /// </summary>
        [HarmonyPatch(typeof(GravshipLandingMarker), "SpawnSetup")]
        public static class GravshipLandingMarker_SpawnSetup_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(GravshipLandingMarker __instance, bool respawningAfterLoad)
            {
                if (respawningAfterLoad)
                    return;

                LongEventHandler.ExecuteWhenFinished(delegate
                {
                    TolkHelper.Speak(
                        "RimWorldAccess.Gravships.Landing.MarkerPlacedGuidance".Loc(),
                        SpeechPriority.High);
                });
            }
        }
    }
}
