using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The ground capacity a Bad Hygiene well would draw at a spot, the number its user-grid ghosts
    /// float beside the cursor. That number is a local inside <c>PlaceWorker_UserGrid.DrawGhost</c>
    /// and <c>PlaceWorker_IceUserGrid.DrawGhost</c>, so their build-placement branch (no existing
    /// thing) is mirrored here: each water cell in reach counts its level, divided among the wells
    /// already drawing on it plus this one, times the mod's pump capacity option. The placement
    /// cursor and the placement-spot rating both read this one calculation.
    /// </summary>
    internal sealed class DubsWellCapacity
    {
        private readonly DubsGroundwater groundwater;
        private readonly Type wellUsersWorker;
        private readonly Type iceUsersWorker;
        private readonly Type inletPropsType;
        private readonly FieldInfo radiusField;
        private readonly FieldInfo deepField;
        private readonly FieldInfo pumpCapacityOption;
        private readonly FieldInfo optionValue;

        public bool Ready { get; }

        public DubsWellCapacity(DubsGroundwater groundwater)
        {
            this.groundwater = groundwater;
            var surface = new ReflectionSurface("DubsWellCapacity");
            wellUsersWorker = surface.Type("DubsBadHygiene.PlaceWorker_UserGrid");
            iceUsersWorker = surface.Type("DubsBadHygiene.PlaceWorker_IceUserGrid");
            inletPropsType = surface.Type("DubsBadHygiene.CompProperties_WaterInlet");
            Type optionsType = surface.Type("DubsBadHygiene.ModOption");
            Type optionType = surface.Type("DubsBadHygiene.DubsModOptions");
            radiusField = surface.Field(inletPropsType, "Radius");
            deepField = surface.Field(inletPropsType, "Deep");
            pumpCapacityOption = surface.Field(optionsType, "WaterPumpCapacity");
            optionValue = surface.Field(optionType, "Val");
            Ready = surface.Ready && groundwater.Ready;
        }

        public bool AppliesTo(ThingDef def)
        {
            return def.placeWorkers != null
                && (def.placeWorkers.Contains(wellUsersWorker) || def.placeWorkers.Contains(iceUsersWorker));
        }

        public Func<IntVec3, Rot4, PlacementQuality> MakeRating(ThingDef def, Map map)
        {
            if (!TryGetLayers(def, map, out DubsGroundwater.Grid water, out DubsGroundwater.Grid users, out float radius))
                return null;
            float pumpCapacity = PumpCapacity();
            int cellsInReach = GenRadial.NumCellsInRadius(radius);
            return (cell, rotation) =>
            {
                float drawn = Drawn(map, cell, radius, water, users);
                // The ceiling is every cell in reach at the layer's tint limit (100 for the water
                // layers) with no other well sharing it; user grids count wells, not levels.
                float score = water.Limit > 0f ? Mathf.Clamp01(drawn / (cellsInReach * water.Limit)) : 0f;
                return new PlacementQuality(score, Phrase(drawn * pumpCapacity));
            };
        }

        /// <summary>The capacity phrase at <paramref name="center"/>, or null where the well draws on no layer (Lite mode).</summary>
        public string PhraseAt(ThingDef def, Map map, IntVec3 center)
        {
            if (!TryGetLayers(def, map, out DubsGroundwater.Grid water, out DubsGroundwater.Grid users, out float radius))
                return null;
            return Phrase(Drawn(map, center, radius, water, users) * PumpCapacity());
        }

        /// <summary>The figure through the mod's own inspect-string key, as a built well reports the same quantity.</summary>
        public static string Phrase(float liters)
        {
            return CompatText.ModArgs("GroundCapacity", liters.ToString("0"));
        }

        // Mirrors PlaceWorker_UserGrid.DrawGhost / PlaceWorker_IceUserGrid.CalculateCapacity (thing == null);
        // read-only, and the figure exists nowhere else: DrawGhost keeps it in a local and draws as it goes.
        private static float Drawn(Map map, IntVec3 center, float radius, DubsGroundwater.Grid water, DubsGroundwater.Grid users)
        {
            float drawn = 0f;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, useCenter: true))
            {
                if (!cell.InBounds(map))
                    continue;
                int level = water.IntAt(map, cell);
                if (level <= 0)
                    continue;
                int sharing = users.IntAt(map, cell);
                drawn += sharing > 0 ? (float)level / (sharing + 1) : level;
            }
            return drawn;
        }

        private bool TryGetLayers(ThingDef def, Map map, out DubsGroundwater.Grid water, out DubsGroundwater.Grid users, out float radius)
        {
            water = default(DubsGroundwater.Grid);
            users = default(DubsGroundwater.Grid);
            radius = 0f;
            CompProperties props = InletProps(def);
            if (props == null)
                return false;

            radius = (float)radiusField.GetValue(props);
            if (def.placeWorkers.Contains(iceUsersWorker))
            {
                water = groundwater.Ice(map);
                users = groundwater.IceUsers(map);
            }
            else if ((bool)deepField.GetValue(props))
            {
                water = groundwater.DeepWater(map);
                users = groundwater.DeepWaterUsers(map);
            }
            else
            {
                water = groundwater.Water(map);
                users = groundwater.WaterUsers(map);
            }
            return water.IsValid && users.IsValid;
        }

        // GetCompProperties<CompProperties_WaterInlet>'s own match, without a typeref to the mod.
        private CompProperties InletProps(ThingDef def)
        {
            List<CompProperties> comps = def.comps;
            if (comps == null)
                return null;
            for (int i = 0; i < comps.Count; i++)
            {
                if (inletPropsType.IsInstanceOfType(comps[i]))
                    return comps[i];
            }
            return null;
        }

        /// <summary>The mod's pump capacity option, which scales every ground capacity it shows.</summary>
        public float PumpCapacity()
        {
            return (float)optionValue.GetValue(pumpCapacityOption.GetValue(null));
        }
    }
}
