using System;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Which key families a host offers on its world map, and under what
    /// conditions. Every field is the host's OWN gate, passed in rather than
    /// re-derived; a null gate means the host does not offer that family at all
    /// and its claims are never registered.
    /// </summary>
    public sealed class WorldMapElementProfile
    {
        /// <summary>Plain arrows: move the tile cursor one step on the compass.</summary>
        public Func<bool> CursorLive;

        /// <summary>Ctrl+arrows: jump to the next tile of a different biome in that direction.</summary>
        public Func<bool> BiomeJumpLive;

        /// <summary>Digits 1-5: read one category of the current tile's detail.</summary>
        public Func<bool> TileInfoLive;

        /// <summary>The scanner's browse/jump cluster (PageUp/PageDown families, Home, Alt+Home, End, Alt+J).</summary>
        public Func<bool> ScannerLive;

        /// <summary>Re-read the current tile.</summary>
        public Func<bool> ReadTileLive;

        /// <summary>Alt+End with no caravans to jump to (world generation): consume silently.</summary>
        public bool JumpToNearestCaravanIsSilentNoOp;
    }

    /// <summary>
    /// The world map as a focusable element — the planet surface with a tile
    /// cursor on it, which a screen hosts the way it hosts a slider or a combo
    /// box. Shared by the in-game world view (<see cref="WorldScope"/>,
    /// ambient) and the starting-site screen. The scanner keys here are only
    /// the scanner AS THE MAP USES IT: the structured browse that moves this
    /// same tile cursor, hence both hosts claiming the same
    /// <c>world.scanner.*</c> ids.
    ///
    /// The element owns the action ids and the handlers; the host passes its
    /// own gates in through <see cref="WorldMapElementProfile"/>. Enter stays
    /// with the host — activating a tile means opening the world-object picker
    /// in game, confirming the landing site during world generation.
    ///
    /// Not a scope, so claims register through
    /// <see cref="FocusScope.RegisterExternalClaim"/>.
    /// </summary>
    public sealed class WorldMapElement
    {
        private readonly WorldMapElementProfile profile;

        public WorldMapElement(WorldMapElementProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }
            this.profile = profile;
        }

        public void RegisterClaims(FocusScope scope)
        {
            if (scope == null)
            {
                throw new ArgumentNullException("scope");
            }

            // ---- Cursor arrows ----
            if (profile.CursorLive != null)
            {
                scope.RegisterExternalClaim("world.cursor.north", delegate (KeyEventSnapshot e) { WorldNavigationState.HandleArrowKey(KeyCode.UpArrow); }, when: profile.CursorLive);
                scope.RegisterExternalClaim("world.cursor.south", delegate (KeyEventSnapshot e) { WorldNavigationState.HandleArrowKey(KeyCode.DownArrow); }, when: profile.CursorLive);
                scope.RegisterExternalClaim("world.cursor.west", delegate (KeyEventSnapshot e) { WorldNavigationState.HandleArrowKey(KeyCode.LeftArrow); }, when: profile.CursorLive);
                scope.RegisterExternalClaim("world.cursor.east", delegate (KeyEventSnapshot e) { WorldNavigationState.HandleArrowKey(KeyCode.RightArrow); }, when: profile.CursorLive);
            }

            // ---- Ctrl+arrow biome jump ----
            if (profile.BiomeJumpLive != null)
            {
                scope.RegisterExternalClaim("world.cursor.biomeJumpNorth", delegate (KeyEventSnapshot e) { WorldNavigationState.JumpToNextBiomeInDirection(KeyCode.UpArrow); }, when: profile.BiomeJumpLive);
                scope.RegisterExternalClaim("world.cursor.biomeJumpSouth", delegate (KeyEventSnapshot e) { WorldNavigationState.JumpToNextBiomeInDirection(KeyCode.DownArrow); }, when: profile.BiomeJumpLive);
                scope.RegisterExternalClaim("world.cursor.biomeJumpWest", delegate (KeyEventSnapshot e) { WorldNavigationState.JumpToNextBiomeInDirection(KeyCode.LeftArrow); }, when: profile.BiomeJumpLive);
                scope.RegisterExternalClaim("world.cursor.biomeJumpEast", delegate (KeyEventSnapshot e) { WorldNavigationState.JumpToNextBiomeInDirection(KeyCode.RightArrow); }, when: profile.BiomeJumpLive);
            }

            // ---- Tile-info digits 1-5 ----
            // The host's gate must exclude every menu that reads digits itself
            // (typeahead, numeric entry, scanner search). RoutePlannerState is
            // deliberately not excluded: it never consumed digits.
            if (profile.TileInfoLive != null)
            {
                scope.RegisterExternalClaim("world.tileInfo.growing", delegate (KeyEventSnapshot e) { WorldNavigationState.AnnounceTileInfoCategory(1); }, when: profile.TileInfoLive);
                scope.RegisterExternalClaim("world.tileInfo.movement", delegate (KeyEventSnapshot e) { WorldNavigationState.AnnounceTileInfoCategory(2); }, when: profile.TileInfoLive);
                scope.RegisterExternalClaim("world.tileInfo.health", delegate (KeyEventSnapshot e) { WorldNavigationState.AnnounceTileInfoCategory(3); }, when: profile.TileInfoLive);
                scope.RegisterExternalClaim("world.tileInfo.location", delegate (KeyEventSnapshot e) { WorldNavigationState.AnnounceTileInfoCategory(4); }, when: profile.TileInfoLive);
                scope.RegisterExternalClaim("world.tileInfo.features", delegate (KeyEventSnapshot e) { WorldNavigationState.AnnounceTileInfoCategory(5); }, when: profile.TileInfoLive);
            }

            // ---- Re-read the current tile ----
            // Belongs to this shared element; its "startingSite" id is kept.
            if (profile.ReadTileLive != null)
            {
                scope.RegisterExternalClaim("startingSite.announceTile", delegate (KeyEventSnapshot e) { WorldNavigationState.AnnounceTile(); }, when: profile.ReadTileLive);
            }

            // ---- Scanner browse / jump cluster ----
            // The host's ScannerLive must exclude the TreeNavigationHelper-backed
            // states (object selection, stat breakdown), whose tail consumes every
            // real KeyCode. It must NOT exclude ScannerSearchState: that state
            // passes these keys through deliberately, so excluding it would leave
            // PageUp dead while a scanner search is open.
            //
            // world.scanner.jumpToNearestCaravan needs no Context check in game:
            // WorldScope is the ambient base only in ProgramState.Playing, and
            // WorldGen exists only during Entry.
            if (profile.ScannerLive != null)
            {
                scope.RegisterExternalClaim("world.scanner.nextItem", delegate (KeyEventSnapshot e) { WorldScannerState.NextItem(); }, when: profile.ScannerLive);
                scope.RegisterExternalClaim("world.scanner.nextSubcategory", delegate (KeyEventSnapshot e) { WorldScannerState.NextSubcategory(); }, when: profile.ScannerLive);
                scope.RegisterExternalClaim("world.scanner.nextCategory", delegate (KeyEventSnapshot e) { WorldScannerState.NextCategory(); }, when: profile.ScannerLive);
                scope.RegisterExternalClaim("world.scanner.nextInstance", delegate (KeyEventSnapshot e) { WorldScannerState.NextInstance(); }, when: profile.ScannerLive);
                scope.RegisterExternalClaim("world.scanner.previousItem", delegate (KeyEventSnapshot e) { WorldScannerState.PreviousItem(); }, when: profile.ScannerLive);
                scope.RegisterExternalClaim("world.scanner.previousSubcategory", delegate (KeyEventSnapshot e) { WorldScannerState.PreviousSubcategory(); }, when: profile.ScannerLive);
                scope.RegisterExternalClaim("world.scanner.previousCategory", delegate (KeyEventSnapshot e) { WorldScannerState.PreviousCategory(); }, when: profile.ScannerLive);
                scope.RegisterExternalClaim("world.scanner.previousInstance", delegate (KeyEventSnapshot e) { WorldScannerState.PreviousInstance(); }, when: profile.ScannerLive);
                scope.RegisterExternalClaim("world.scanner.jumpToCurrent", delegate (KeyEventSnapshot e)
                {
                    // Home ends the Z search field silently; see MapScope's colony twin.
                    ScannerSearchState.DismissSilently();
                    WorldScannerState.JumpToCurrent(manual: true);
                }, when: profile.ScannerLive);
                scope.RegisterExternalClaim("world.scanner.jumpToHome", delegate (KeyEventSnapshot e) { WorldNavigationState.JumpToHome(); }, when: profile.ScannerLive);
                scope.RegisterExternalClaim("world.scanner.readDistance", delegate (KeyEventSnapshot e) { WorldScannerState.ReadDistanceAndDirection(); }, when: profile.ScannerLive);
                if (profile.JumpToNearestCaravanIsSilentNoOp)
                {
                    scope.RegisterExternalClaim("world.scanner.jumpToNearestCaravan", delegate (KeyEventSnapshot e) { }, when: profile.ScannerLive);
                }
                else
                {
                    scope.RegisterExternalClaim("world.scanner.jumpToNearestCaravan", delegate (KeyEventSnapshot e) { WorldNavigationState.JumpToNearestCaravan(); }, when: profile.ScannerLive);
                }
                scope.RegisterExternalClaim("world.scanner.toggleAutoJump", delegate (KeyEventSnapshot e) { WorldScannerState.ToggleAutoJumpMode(); }, when: profile.ScannerLive);
            }
        }

        /// <summary>
        /// The element as a focusable item: the live tile under the cursor,
        /// read through the game's own summary so the spoken text matches every
        /// other tile announcement.
        /// </summary>
        public ElementDescription Describe()
        {
            ElementDescription d = new ElementDescription();
            d.Role = ElementRole.Map;
            PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
            d.Label = tile.Valid
                ? WorldInfoHelper.GetTileSummary(tile,
                    includeRouteInfo: WorldNavigationState.Context == WorldNavContext.InGame)
                : (string)"RimWorldAccess.World.Tile.Invalid".Translate();
            return d;
        }
    }
}
