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
        private static readonly Type[] DrawGhostParams =
        {
            typeof(ThingDef), typeof(IntVec3), typeof(Rot4), typeof(Color), typeof(Thing),
        };

        // CompSurfaceWaterInlet.Kind's codes.
        private const int River = 1;
        private const int Lake = 2;
        private const int Marsh = 3;

        // PlaceWorker_SurfaceWater.DrawGhost's fallback for a def without inlet props.
        private const float DefaultRadius = 15f;

        private static Type surfaceWorker;
        private static Type inletPropsType;
        private static FieldInfo radiusField;
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
            inletPropsType = surface.Type("DubsBadHygiene.CompProperties_WaterInlet");
            MethodInfo cells = surface.Method(inletComp, "WaterCells", new[] { typeof(Map), typeof(IntVec3), typeof(Rot4), typeof(float) });
            MethodInfo kind = surface.Method(inletComp, "KindAt", new[] { typeof(Map), typeof(IntVec3) });
            FieldInfo river = surface.Field(inletComp, "RiverPerCell");
            FieldInfo lake = surface.Field(inletComp, "LakePerCell");
            FieldInfo marsh = surface.Field(inletComp, "MarshPerCell");
            MethodInfo ghost = surface.Method(surfaceWorker, "DrawGhost", DrawGhostParams);
            radiusField = surface.Field(inletPropsType, "Radius");
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
                def => def.placeWorkers != null && def.placeWorkers.Contains(surfaceWorker),
                MakeRating);
        }

        // Positional binding: the DrawGhost override is free to rename its parameters.
        internal static void Postfix([HarmonyArgument(0)] ThingDef def, [HarmonyArgument(1)] IntVec3 center, [HarmonyArgument(2)] Rot4 rot)
        {
            try
            {
                if (PlacementHelpSpeech.CollectingOutlines)
                    PlacementHelpSpeech.CollectOutline(Rate(def, Find.CurrentMap, center, rot, wells.PumpCapacity()).Phrase.CapitalizeFirst());
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Bad Hygiene surface water note error", ex);
            }
        }

        private static Func<IntVec3, Rot4, PlacementQuality> MakeRating(ThingDef def, Map map)
        {
            float pumpCapacity = wells.PumpCapacity();
            return (cell, rotation) => Rate(def, map, cell, rotation, pumpCapacity);
        }

        private static PlacementQuality Rate(ThingDef def, Map map, IntVec3 center, Rot4 rotation, float pumpCapacity)
        {
            float radius = Radius(def);
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
            var phrases = new List<string> { DubsWellCapacity.Phrase(drawn * pumpCapacity) };
            // CompSurfaceWaterInlet.Stagnant: marsh outweighing clean water fouls the supply.
            if (marshes > rivers + lakes)
                phrases.Add(CompatText.Flatten(CompatText.ModText("DBH.SurfaceWaterStagnant")));

            // The ceiling is a reach that is all river, the richest kind.
            float score = Mathf.Clamp01(drawn / (GenRadial.NumCellsInRadius(radius) * riverPerCell));
            return new PlacementQuality(score, CompatText.JoinSentences(phrases));
        }

        private static float Radius(ThingDef def)
        {
            if (def.comps != null)
            {
                for (int i = 0; i < def.comps.Count; i++)
                {
                    if (inletPropsType.IsInstanceOfType(def.comps[i]))
                        return (float)radiusField.GetValue(def.comps[i]);
                }
            }
            return DefaultRadius;
        }
    }
}
