using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>The wind pump ghost's obstructions, through <see cref="WindPathGhostNote"/>, from the pump's own wind cells and penalty.</summary>
    internal static class DubsWindPumpGhostNote
    {
        private static readonly Type[] DrawGhostParams =
        {
            typeof(ThingDef), typeof(IntVec3), typeof(Rot4), typeof(Color), typeof(Thing),
        };

        private static Func<IntVec3, Rot4, IntVec2, IEnumerable<IntVec3>> windCells;
        private static float reductionPerObstacle;

        public static void Register(Harmony harmony)
        {
            var surface = new ReflectionSurface("DubsWindPumpGhostNote");
            Type pumpComp = surface.Type("DubsBadHygiene.CompWindPump");
            Type pumpWorker = surface.Type("DubsBadHygiene.PlaceWorker_WindPump");
            MethodInfo cells = surface.Method(pumpComp, "CalculateWindCells", new[] { typeof(IntVec3), typeof(Rot4), typeof(IntVec2) });
            FieldInfo reduction = surface.Field(pumpComp, "PowerReductionPercentPerObstacle");
            MethodInfo ghost = surface.Method(pumpWorker, "DrawGhost", DrawGhostParams);
            if (!surface.Ready || !reduction.IsLiteral)
                return;

            windCells = (Func<IntVec3, Rot4, IntVec2, IEnumerable<IntVec3>>)Delegate.CreateDelegate(
                typeof(Func<IntVec3, Rot4, IntVec2, IEnumerable<IntVec3>>), cells);
            reductionPerObstacle = (float)reduction.GetRawConstantValue();
            harmony.Patch(ghost, postfix: new HarmonyMethod(typeof(DubsWindPumpGhostNote), nameof(Postfix)));
        }

        // Positional binding: the DrawGhost override is free to rename its parameters.
        internal static void Postfix([HarmonyArgument(0)] ThingDef def, [HarmonyArgument(1)] IntVec3 center, [HarmonyArgument(2)] Rot4 rot)
        {
            try
            {
                if (PlacementHelpSpeech.CollectingOutlines)
                    WindPathGhostNote.Collect(windCells(center, rot, def.size), reductionPerObstacle);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Bad Hygiene wind pump ghost note error", ex);
            }
        }
    }
}
