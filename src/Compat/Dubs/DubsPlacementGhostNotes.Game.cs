using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Speaks what Bad Hygiene's well ghosts show at the placement cursor: the water-table, ice and
    /// sewage overlays its grid place workers switch on (as the level under the cursor), and the capacity label its
    /// user-grid place workers float beside it. Both arrive through the placement-help outline
    /// harvest, which runs the ghost draw at the keyboard cursor and keeps a note per cell. A new
    /// well's capacity comes from <see cref="DubsWellCapacity"/>, the calculation its placement-spot
    /// rating uses.
    /// </summary>
    internal static class DubsPlacementGhostNotes
    {
        private static DubsGroundwater groundwater;
        // Bound once: the ghost postfixes run every frame.
        private static Func<Map, DubsGroundwater.Grid> waterGrid;
        private static Func<Map, DubsGroundwater.Grid> deepGrid;
        private static Func<Map, DubsGroundwater.Grid> iceGrid;
        private static DubsWellCapacity wellCapacity;
        private static FieldInfo wellCapacityText;
        private static FieldInfo iceCapacityText;

        public static void Register(Harmony harmony, DubsGroundwater source, DubsWellCapacity capacity)
        {
            var surface = new ReflectionSurface("DubsPlacementGhostNotes");
            Type waterWorker = surface.Type("DubsBadHygiene.PlaceWorker_WaterGrid");
            Type deepWorker = surface.Type("DubsBadHygiene.PlaceWorker_DeepWaterGrid");
            Type iceWorker = surface.Type("DubsBadHygiene.PlaceWorker_IceGrid");
            Type sewageWorker = surface.Type("DubsBadHygiene.PlaceWorker_SewageGrid");
            Type wellUsersWorker = surface.Type("DubsBadHygiene.PlaceWorker_UserGrid");
            Type iceUsersWorker = surface.Type("DubsBadHygiene.PlaceWorker_IceUserGrid");
            MethodInfo waterGhost = surface.Method(waterWorker, "DrawGhost", PlaceWorkerGhostAuthorPatch.DrawGhostParams);
            MethodInfo deepGhost = surface.Method(deepWorker, "DrawGhost", PlaceWorkerGhostAuthorPatch.DrawGhostParams);
            MethodInfo iceGhost = surface.Method(iceWorker, "DrawGhost", PlaceWorkerGhostAuthorPatch.DrawGhostParams);
            MethodInfo sewageGhost = surface.Method(sewageWorker, "DrawGhost", PlaceWorkerGhostAuthorPatch.DrawGhostParams);
            MethodInfo wellUsersGhost = surface.Method(wellUsersWorker, "DrawGhost", PlaceWorkerGhostAuthorPatch.DrawGhostParams);
            MethodInfo iceUsersGhost = surface.Method(iceUsersWorker, "DrawGhost", PlaceWorkerGhostAuthorPatch.DrawGhostParams);
            FieldInfo wellText = surface.Field(wellUsersWorker, "strongk");
            FieldInfo iceText = surface.Field(iceUsersWorker, "capacityDisplayText");
            if (!surface.Ready)
                return;

            groundwater = source;
            waterGrid = source.Water;
            deepGrid = source.DeepWater;
            iceGrid = source.Ice;
            wellCapacity = capacity.Ready ? capacity : null;
            wellCapacityText = wellText;
            iceCapacityText = iceText;
            harmony.Patch(waterGhost, postfix: new HarmonyMethod(typeof(DubsPlacementGhostNotes), nameof(WaterPostfix)));
            harmony.Patch(deepGhost, postfix: new HarmonyMethod(typeof(DubsPlacementGhostNotes), nameof(DeepPostfix)));
            harmony.Patch(iceGhost, postfix: new HarmonyMethod(typeof(DubsPlacementGhostNotes), nameof(IcePostfix)));
            harmony.Patch(sewageGhost, postfix: new HarmonyMethod(typeof(DubsPlacementGhostNotes), nameof(SewagePostfix)));
            harmony.Patch(wellUsersGhost, postfix: new HarmonyMethod(typeof(DubsPlacementGhostNotes), nameof(WellCapacityPostfix)));
            harmony.Patch(iceUsersGhost, postfix: new HarmonyMethod(typeof(DubsPlacementGhostNotes), nameof(IceCapacityPostfix)));
        }

        // Positional binding: the DrawGhost overrides are free to rename their parameters.
        internal static void WaterPostfix([HarmonyArgument(1)] IntVec3 center)
        {
            CollectLevel(center, waterGrid,
                "RimWorldAccess.Compat.Dubs.Groundwater.Shallow", "RimWorldAccess.Compat.Dubs.Groundwater.ShallowNone");
        }

        internal static void DeepPostfix([HarmonyArgument(1)] IntVec3 center)
        {
            CollectLevel(center, deepGrid,
                "RimWorldAccess.Compat.Dubs.Groundwater.Deep", "RimWorldAccess.Compat.Dubs.Groundwater.DeepNone");
        }

        internal static void IcePostfix([HarmonyArgument(1)] IntVec3 center)
        {
            CollectLevel(center, iceGrid,
                "RimWorldAccess.Compat.Dubs.Groundwater.Ice", "RimWorldAccess.Compat.Dubs.Groundwater.IceNone");
        }

        // The sewage overlay is mostly empty, so only contamination actually under the cursor is spoken.
        internal static void SewagePostfix([HarmonyArgument(1)] IntVec3 center)
        {
            try
            {
                if (!PlacementHelpSpeech.CollectingOutlines)
                    return;
                Map map = Find.CurrentMap;
                PlacementHelpSpeech.CollectOutline(DubsGroundwater.PresentPhrase(
                    groundwater.Sewage(map), map, center, "RimWorldAccess.Compat.Dubs.Groundwater.Sewage", tintOnly: true).CapitalizeFirst());
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Bad Hygiene ghost level error", ex);
            }
        }

        internal static void WellCapacityPostfix([HarmonyArgument(0)] ThingDef def, [HarmonyArgument(1)] IntVec3 center, [HarmonyArgument(4)] Thing thing)
        {
            CollectCapacity(def, center, thing, wellCapacityText);
        }

        internal static void IceCapacityPostfix([HarmonyArgument(0)] ThingDef def, [HarmonyArgument(1)] IntVec3 center, [HarmonyArgument(4)] Thing thing)
        {
            CollectCapacity(def, center, thing, iceCapacityText);
        }

        private static void CollectLevel(IntVec3 center, Func<Map, DubsGroundwater.Grid> grid, string levelKey, string noneKey)
        {
            try
            {
                if (!PlacementHelpSpeech.CollectingOutlines)
                    return;
                Map map = Find.CurrentMap;
                PlacementHelpSpeech.CollectOutline(DubsGroundwater.LevelPhrase(grid(map), map, center, levelKey, noneKey).CapitalizeFirst());
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Bad Hygiene ghost level error", ex);
            }
        }

        // A reinstalled well (thing set) takes DrawGhost's other branch, which nets out the well's own
        // draw against the pointer's reach, so only its label carries that figure.
        private static void CollectCapacity(ThingDef def, IntVec3 center, Thing thing, FieldInfo ghostText)
        {
            try
            {
                // Lite mode returns from DrawGhost before the label is written, leaving a stale one.
                Map map = Find.CurrentMap;
                if (!PlacementHelpSpeech.CollectingOutlines || !groundwater.Water(map).IsValid)
                    return;
                PlacementHelpSpeech.CollectOutline(thing == null && wellCapacity != null
                    ? wellCapacity.PhraseAt(def, map, center)
                    : CapacityPhrase(ghostText.GetValue(null) as string));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Bad Hygiene ghost capacity error", ex);
            }
        }

        /// <summary>
        /// The ghost's capacity exists only as its formatted label ("Cap: 1234.5L"): the preview rule,
        /// each cell's water split with the wells already drawing on it, is a local inside DrawGhost.
        /// The number is lifted out and spoken through the mod's own inspect-string key, as a built
        /// well reports the same quantity; an unexpected shape is spoken as drawn.
        /// </summary>
        private static string CapacityPhrase(string label)
        {
            if (string.IsNullOrEmpty(label))
                return null;
            string number = label.Trim();
            int colon = number.IndexOf(':');
            if (colon >= 0)
                number = number.Substring(colon + 1);
            number = number.Trim().TrimEnd('L').Trim();
            return float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float liters)
                ? DubsWellCapacity.Phrase(liters)
                : label;
        }
    }
}
