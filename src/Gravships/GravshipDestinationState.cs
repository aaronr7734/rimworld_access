using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class GravshipDestinationState
    {
        private static bool isActive = false;
        private static bool isLaunching = false;
        private static CompPilotConsole currentConsole = null;
        private static Building_GravEngine currentEngine = null;
        private static float cachedTotalFuel = 0f;
        private static int cachedMaxRange = 0;
        private static float cachedFuelPerTile = 0f;

        // Cached reflection fields for TilePicker
        private static FieldInfo tilePickerValidatorField;
        private static FieldInfo tilePickerTileChosenField;

        public static bool IsActive => isActive;

        public static void Open(CompPilotConsole console, bool launching = true)
        {
            if (console == null) return;

            currentConsole = console;
            isLaunching = launching;

            // Get engine via reflection (engine field is on CompGravshipFacility base class)
            var engineField = AccessTools.Field(typeof(CompGravshipFacility), "engine");
            currentEngine = engineField?.GetValue(console) as Building_GravEngine;

            if (currentEngine == null)
            {
                Log.Warning("[GravshipDestinationState] Could not get engine from pilot console");
                return;
            }

            isActive = true;

            // Cache fuel info
            cachedTotalFuel = currentEngine.TotalFuel;
            cachedMaxRange = currentEngine.MaxLaunchDistance;
            cachedFuelPerTile = currentEngine.FuelPerTile;

            if (launching)
            {
                TolkHelper.Speak(
                    $"Gravship destination targeting. {cachedTotalFuel:F0} chemfuel available, " +
                    $"max range {cachedMaxRange} tiles. " +
                    "Use scanner to browse destinations, Enter to confirm, Escape to cancel.");
            }
            else
            {
                TolkHelper.Speak(
                    $"Viewing gravship range. {cachedTotalFuel:F0} chemfuel available, " +
                    $"max range {cachedMaxRange} tiles. " +
                    "Use scanner to browse destinations, Escape to close.");
            }
        }

        public static void Close()
        {
            isActive = false;
            isLaunching = false;
            currentConsole = null;
            currentEngine = null;
            cachedTotalFuel = 0f;
            cachedMaxRange = 0;
            cachedFuelPerTile = 0f;
        }

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive) return false;

            // If TilePicker stopped, close our state
            if (Find.TilePicker == null || !Find.TilePicker.Active)
            {
                Close();
                return false;
            }

            // Enter - confirm current destination (launch mode) or close (view mode)
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                if (WindowlessFloatMenuState.IsActive)
                    return false;

                if (isLaunching)
                {
                    ConfirmCurrentDestination();
                }
                else
                {
                    // View range mode — Enter just closes like Escape
                    CancelTargeting();
                }
                return true;
            }

            // Escape - cancel targeting
            if (key == KeyCode.Escape)
            {
                if (WindowlessFloatMenuState.IsActive)
                    return false;

                CancelTargeting();
                return true;
            }

            // F - announce fuel status
            if (key == KeyCode.F && !shift && !ctrl && !alt)
            {
                AnnounceFuelStatus();
                return true;
            }

            return false;
        }

        private static void ConfirmCurrentDestination()
        {
            if (!WorldNavigationState.IsActive)
            {
                TolkHelper.Speak("World navigation not active", SpeechPriority.High);
                return;
            }

            PlanetTile selectedTile = WorldNavigationState.CurrentSelectedTile;
            if (!selectedTile.Valid)
            {
                // Try getting from world selector's single selected object
                WorldObject singleSelected = Find.WorldSelector?.SingleSelectedObject;
                if (singleSelected != null && singleSelected.Tile.Valid)
                {
                    selectedTile = singleSelected.Tile;
                }
                else
                {
                    TolkHelper.Speak("No valid tile selected", SpeechPriority.High);
                    return;
                }
            }

            // Use TilePicker's validator and tileChosen callbacks via reflection
            try
            {
                if (tilePickerValidatorField == null)
                    tilePickerValidatorField = AccessTools.Field(typeof(TilePicker), "validator");
                if (tilePickerTileChosenField == null)
                    tilePickerTileChosenField = AccessTools.Field(typeof(TilePicker), "tileChosen");

                var validator = tilePickerValidatorField?.GetValue(Find.TilePicker) as Func<PlanetTile, bool>;
                var tileChosen = tilePickerTileChosenField?.GetValue(Find.TilePicker) as Action<PlanetTile>;

                if (validator == null || tileChosen == null)
                {
                    TolkHelper.Speak("Cannot access tile picker callbacks", SpeechPriority.High);
                    return;
                }

                // Validate the tile (this shows game messages on failure)
                if (!validator(selectedTile))
                {
                    // Validator already showed error message via Messages.Message
                    return;
                }

                // Stop TilePicker internally (without calling noTileChosen)
                var stopIntMethod = AccessTools.Method(typeof(TilePicker), "StopTargetingInt");
                stopIntMethod?.Invoke(Find.TilePicker, null);

                // Invoke the tileChosen callback (this triggers the launch confirmation flow)
                tileChosen(selectedTile);
            }
            catch (Exception ex)
            {
                Log.Warning($"[GravshipDestinationState] Error confirming destination: {ex}");
                TolkHelper.Speak("Error selecting destination", SpeechPriority.High);
            }
        }

        private static void CancelTargeting()
        {
            // Cache return target before closing
            Thing returnTarget = currentConsole?.parent;

            if (Find.TilePicker != null && Find.TilePicker.Active)
            {
                Find.TilePicker.StopTargeting();
            }

            Close();
            TolkHelper.Speak("Gravship targeting cancelled", SpeechPriority.Normal);

            // Return to map view
            if (returnTarget != null)
            {
                CameraJumper.TryJump(returnTarget);
            }
        }

        private static void AnnounceFuelStatus()
        {
            if (currentEngine == null) return;

            // Refresh fuel data
            cachedTotalFuel = currentEngine.TotalFuel;

            TolkHelper.Speak(
                $"Fuel: {cachedTotalFuel:F0} of {currentEngine.MaxFuel:F0} chemfuel. " +
                $"Max range: {cachedMaxRange} tiles. " +
                $"Fuel per tile: {cachedFuelPerTile:F1}.",
                SpeechPriority.Normal);
        }

        /// <summary>
        /// Called by WorldNavigationState/WorldScannerState to check if fuel costs should be announced.
        /// </summary>
        public static bool ShouldAnnounceFuelCosts()
        {
            return isActive && currentEngine != null;
        }

        /// <summary>
        /// Gets fuel cost announcement for a world tile at the given distance.
        /// </summary>
        public static string GetFuelCostAnnouncement(PlanetTile destinationTile)
        {
            if (!isActive || currentEngine == null || currentConsole?.parent?.Map == null)
                return "";

            PlanetTile originTile = currentConsole.parent.Map.Tile;

            float cost;
            int distance;
            if (GravshipUtility.TryGetPathFuelCost(originTile, destinationTile, out cost, out distance,
                    10f, currentEngine.FuelUseageFactor))
            {
                if (cost > cachedTotalFuel)
                {
                    return $"{cost:F0} chemfuel, NOT ENOUGH FUEL";
                }
                if (distance > cachedMaxRange)
                {
                    return "TransportPodDestinationBeyondMaximumRange".Translate();
                }
                return $"{cost:F0} chemfuel";
            }
            else
            {
                return "CannotLaunchDestination".Translate();
            }
        }

        /// <summary>
        /// Gets the origin tile for distance calculations.
        /// </summary>
        public static PlanetTile GetOriginTile()
        {
            if (currentConsole?.parent?.Map == null)
                return PlanetTile.Invalid;

            return currentConsole.parent.Map.Tile;
        }
    }
}
