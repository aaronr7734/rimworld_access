using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Why a scanning jump found nowhere to land.</summary>
    internal enum JumpScanFailure
    {
        MapBoundary,
        NoOpenTile,
        NoChange
    }

    /// <summary>
    /// Finds the tile a scanning Ctrl+Arrow jump lands on. Blueprints and frames read as the
    /// finished thing, so a colony under construction navigates like the one being built. Unseen
    /// tiles all share one identity and no scan crosses the fog line, so a jump never reports what
    /// lies inside unexplored ground.
    /// </summary>
    internal static class MapJumpScanner
    {
        /// <summary>The target tile for a jump mode, and how many tiles away it is.</summary>
        internal static bool TryScan(JumpMode mode, IntVec3 start, IntVec3 direction, Map map,
            out IntVec3 destination, out int distance, out JumpScanFailure failure)
        {
            bool found = mode == JumpMode.Impassable
                ? ScanToImpassable(start, direction, map, out destination, out failure)
                : ScanToChange(mode, start, direction, map, out destination, out failure);

            distance = found
                ? System.Math.Abs(destination.x - start.x) + System.Math.Abs(destination.z - start.z)
                : 0;

            return found;
        }

        /// <summary>The last tile a walking pawn could reach before something stops it.</summary>
        private static bool ScanToImpassable(IntVec3 start, IntVec3 direction, Map map,
            out IntVec3 destination, out JumpScanFailure failure)
        {
            bool startUnseen = start.Fogged(map);

            destination = start;
            failure = JumpScanFailure.MapBoundary;

            while (true)
            {
                IntVec3 next = destination + direction;

                if (!next.InBounds(map))
                {
                    failure = JumpScanFailure.MapBoundary;
                    break;
                }

                if (Blocks(next, map, startUnseen))
                {
                    failure = JumpScanFailure.NoOpenTile;
                    break;
                }

                destination = next;
            }

            return destination != start;
        }

        /// <summary>The first tile whose terrain or structure differs from the starting tile's.</summary>
        private static bool ScanToChange(JumpMode mode, IntVec3 start, IntVec3 direction, Map map,
            out IntVec3 destination, out JumpScanFailure failure)
        {
            destination = start;
            failure = JumpScanFailure.NoChange;

            IntVec3 cell = start + direction;
            if (!cell.InBounds(map))
            {
                failure = JumpScanFailure.MapBoundary;
                return false;
            }

            TileIdentity origin = IdentityAt(mode, start, map);
            IntVec3 runEnd = start;

            for (; cell.InBounds(map); cell += direction)
            {
                TileIdentity identity = IdentityAt(mode, cell, map);
                if (identity.Matches(origin))
                {
                    runEnd = cell;
                    continue;
                }

                if (mode == JumpMode.Structure && identity.Matches(TileIdentity.Bare))
                    return StructureAcrossBareGround(runEnd, start, cell, direction, map, out destination);

                destination = cell;
                return true;
            }

            return mode == JumpMode.Structure && !origin.Matches(TileIdentity.Bare)
                && TryLandOnRunEnd(runEnd, start, out destination);
        }

        /// <summary>Stops on the crossed run's last tile, or from that tile on the next structure.</summary>
        private static bool StructureAcrossBareGround(IntVec3 runEnd, IntVec3 start, IntVec3 firstBare,
            IntVec3 direction, Map map, out IntVec3 destination)
        {
            if (TryLandOnRunEnd(runEnd, start, out destination))
                return true;

            for (IntVec3 cell = firstBare; cell.InBounds(map); cell += direction)
            {
                if (IdentityAt(JumpMode.Structure, cell, map).Matches(TileIdentity.Bare))
                    continue;

                destination = cell;
                return true;
            }

            destination = start;
            return false;
        }

        private static bool TryLandOnRunEnd(IntVec3 runEnd, IntVec3 start, out IntVec3 destination)
        {
            destination = runEnd;
            return runEnd != start;
        }

        /// <summary>Whether a tile stops a walking pawn, read from the game's own path grid.</summary>
        private static bool Blocks(IntVec3 cell, Map map, bool startUnseen)
        {
            bool unseen = cell.Fogged(map);

            // The fog line always stops the scan, and nothing inside the fog is known to block:
            // where the walls are in unexplored ground is not the player's to hear yet.
            if (unseen != startUnseen)
                return true;
            if (unseen)
                return false;

            if (!cell.WalkableByNormal(map))
                return true;

            foreach (Thing thing in cell.GetThingList(map))
            {
                if (PlannedBuild(thing) is ThingDef planned && planned.passability == Traversability.Impassable)
                    return true;
            }

            return false;
        }

        private static TileIdentity IdentityAt(JumpMode mode, IntVec3 cell, Map map)
        {
            if (cell.Fogged(map))
                return TileIdentity.Unseen;

            return mode == JumpMode.Terrain
                ? TerrainIdentity(cell, map)
                : StructureIdentity(cell, map);
        }

        private static TileIdentity TerrainIdentity(IntVec3 cell, Map map)
        {
            foreach (Thing thing in cell.GetThingList(map))
            {
                if (PlannedBuild(thing) is TerrainDef planned)
                    return TileIdentity.Of(planned, null);
            }

            return TileIdentity.Of(cell.GetTerrain(map), null);
        }

        private static TileIdentity StructureIdentity(IntVec3 cell, Map map)
        {
            Building edifice = cell.GetEdifice(map);
            if (edifice != null)
                return TileIdentity.Of(edifice.def, edifice.Stuff);

            foreach (Thing thing in cell.GetThingList(map))
            {
                if (PlannedBuild(thing) is ThingDef planned && planned.IsEdifice())
                    return TileIdentity.Of(planned, (thing as IConstructible)?.EntityToBuildStuff());
            }

            return TileIdentity.Bare;
        }

        /// <summary>The def a blueprint or frame will become, or null for anything else.</summary>
        private static BuildableDef PlannedBuild(Thing thing)
        {
            return thing.def.IsBlueprint || thing.def.IsFrame ? thing.def.entityDefToBuild : null;
        }

        /// <summary>What makes two tiles the same tile for a scanning jump.</summary>
        private readonly struct TileIdentity
        {
            private readonly bool unseen;
            private readonly Def def;
            private readonly ThingDef stuff;

            private TileIdentity(bool unseen, Def def, ThingDef stuff)
            {
                this.unseen = unseen;
                this.def = def;
                this.stuff = stuff;
            }

            internal static readonly TileIdentity Unseen = new TileIdentity(true, null, null);

            internal static readonly TileIdentity Bare = new TileIdentity(false, null, null);

            internal static TileIdentity Of(Def def, ThingDef stuff) => new TileIdentity(false, def, stuff);

            internal bool Matches(TileIdentity other)
            {
                if (unseen || other.unseen)
                    return unseen == other.unseen;

                return def == other.def && stuff == other.stuff;
            }
        }
    }
}
