using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The ground capacity a Bad Hygiene surface water inlet would draw at a spot, and whether that
    /// water would be stagnant. The inlet's place worker only requires water in front of it; the
    /// capacity follows from the water body its ghost outlines, which
    /// <c>CompSurfaceWaterInlet.UpdateCap</c> prices per river, lake and marsh cell. The mod's own
    /// static cell walk and per-kind constants are called here, and the same reading feeds the
    /// placement cursor's note and the placement-spot rating.
    /// </summary>
    internal static class DubsSurfaceWaterCapacity
    {
        // CompSurfaceWaterInlet.Kind's codes.
        private const int River = 1;
        private const int Lake = 2;
        private const int Marsh = 3;

        // PlaceWorker_SurfaceWater.DrawGhost's fallback for a def without inlet props.
        private const float DefaultRadius = 15f;

        private static Type surfaceWorker;
        private static Func<Map, IntVec3, Rot4, float, List<IntVec3>> waterCells;
        private static Func<Map, IntVec3, int> kindAt;
        private static float riverPerCell;
        private static float lakePerCell;
        private static float marshPerCell;
        private static DubsWellCapacity wells;

        /// <param name="capacity">Supplies the mod's pump capacity option and the capacity phrase.</param>
        public static void Register(Harmony harmony, DubsWellCapacity capacity)
        {
            var surface = new ReflectionSurface("DubsSurfaceWaterCapacity");
            Type inletComp = surface.Type("DubsBadHygiene.CompSurfaceWaterInlet");
            surfaceWorker = surface.Type("DubsBadHygiene.PlaceWorker_SurfaceWater");
            MethodInfo cells = surface.Method(inletComp, "WaterCells", new[] { typeof(Map), typeof(IntVec3), typeof(Rot4), typeof(float) });
            MethodInfo kind = surface.Method(inletComp, "KindAt", new[] { typeof(Map), typeof(IntVec3) });
            FieldInfo river = surface.Field(inletComp, "RiverPerCell");
            FieldInfo lake = surface.Field(inletComp, "LakePerCell");
            FieldInfo marsh = surface.Field(inletComp, "MarshPerCell");
            MethodInfo ghost = surface.Method(surfaceWorker, "DrawGhost", PlaceWorkerGhostAuthorPatch.DrawGhostParams);
            if (!surface.Ready || !river.IsLiteral || !lake.IsLiteral || !marsh.IsLiteral)
                return;

            wells = capacity;
            waterCells = (Func<Map, IntVec3, Rot4, float, List<IntVec3>>)Delegate.CreateDelegate(
                typeof(Func<Map, IntVec3, Rot4, float, List<IntVec3>>), cells);
            kindAt = (Func<Map, IntVec3, int>)Delegate.CreateDelegate(typeof(Func<Map, IntVec3, int>), kind);
            riverPerCell = (float)river.GetRawConstantValue();
            lakePerCell = (float)lake.GetRawConstantValue();
            marshPerCell = (float)marsh.GetRawConstantValue();
            harmony.Patch(ghost, postfix: new HarmonyMethod(typeof(DubsSurfaceWaterCapacity), nameof(Postfix)));
            PlacementUsefulnessRegistry.Register(
                def => PlacementUsefulnessRegistry.HasPlaceWorker(def, surfaceWorker),
                MakeRating);
        }

        // Positional binding: the DrawGhost override is free to rename its parameters.
        internal static void Postfix([HarmonyArgument(0)] ThingDef def, [HarmonyArgument(1)] IntVec3 center, [HarmonyArgument(2)] Rot4 rot)
        {
            try
            {
                if (!PlacementHelpSpeech.CollectingOutlines)
                    return;
                float radius = wells.InletRadius(def, DefaultRadius);
                PlacementHelpSpeech.CollectOutline(Rate(Find.CurrentMap, center, rot, radius, Ceiling(radius),
                    wells.PumpCapacity(), describe: true).Phrase.CapitalizeFirst());
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Bad Hygiene surface water note error", ex);
            }
        }

        private static PlacementRating MakeRating(ThingDef def, Map map)
        {
            float pumpCapacity = wells.PumpCapacity();
            float radius = wells.InletRadius(def, DefaultRadius);
            float ceiling = Ceiling(radius);
            return (cell, rotation, describe) => Rate(map, cell, rotation, radius, ceiling, pumpCapacity, describe);
        }

        // The ceiling is a reach that is all river, the richest kind.
        private static float Ceiling(float radius)
        {
            return GenRadial.NumCellsInRadius(radius) * riverPerCell;
        }

        private static PlacementQuality Rate(Map map, IntVec3 center, Rot4 rotation, float radius, float ceiling,
            float pumpCapacity, bool describe)
        {
            int rivers = 0, lakes = 0, marshes = 0;
            foreach (IntVec3 cell in waterCells(map, center, rotation, radius))
            {
                switch (kindAt(map, cell))
                {
                    case River: rivers++; break;
                    case Lake: lakes++; break;
                    case Marsh: marshes++; break;
                }
            }

            float drawn = rivers * riverPerCell + lakes * lakePerCell + marshes * marshPerCell;
            float score = Mathf.Clamp01(drawn / ceiling);
            if (!describe)
                return new PlacementQuality(score, null);

            var phrases = new List<string> { DubsWellCapacity.Phrase(drawn * pumpCapacity) };
            // CompSurfaceWaterInlet.Stagnant: marsh outweighing clean water fouls the supply.
            if (marshes > rivers + lakes)
                phrases.Add(CompatText.Flatten(CompatText.ModText("DBH.SurfaceWaterStagnant")));
            return new PlacementQuality(score, CompatText.JoinSentences(phrases));
        }
    }
}
