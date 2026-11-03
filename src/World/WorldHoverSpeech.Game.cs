using System;
using RimWorld;
using RimWorld.Planet;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The world twin of <see cref="MapHoverSpeech"/>: the mouse pointer as a second planet cursor.
    /// Crossing onto another tile reads it with the keyboard cursor's grammar
    /// (<see cref="WorldNavigationState.ComposeTileAnnouncement"/>), gated by the same
    /// <c>HoverSpeech</c> setting, in the in-game world view and on the starting-site page alike.
    ///
    /// Announce-only: it never moves the keyboard cursor, the camera or the game's selection, never
    /// touches the keyboard's biome memory or pole status, and never consumes the event. The world
    /// has no terrain sound, so nothing plays.
    /// </summary>
    internal static class WorldHoverSpeech
    {
        private static PlanetTile spokenTile = PlanetTile.Invalid;

        /// <summary>The hover reader's own change-only biome memory.</summary>
        private static BiomeDef spokenBiome;

        private static readonly PointerHoverGate Gate =
            new PointerHoverGate(SurfaceLive, PointerOverChrome, DragInFlight, Reset);

        /// <summary>Called once per world interface OnGUI pass, in both the entry and the play root.</summary>
        internal static void Evaluate()
        {
            try
            {
                RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
                bool enabled = settings != null && settings.HoverSpeech && WorldNavigationState.IsInitialized;
                if (!enabled)
                {
                    spokenBiome = null;
                }
                if (!Gate.ShouldResolve(enabled))
                {
                    return;
                }

                PlanetTile tile = TileUnderPointer();
                if (!tile.Valid)
                {
                    spokenTile = PlanetTile.Invalid;
                    return;
                }
                if (tile == spokenTile)
                {
                    return;
                }

                string biomeDesc = BiomeDescriptionTracker.GetBiomeDescriptionIfNew(tile, ref spokenBiome);
                string text = WorldNavigationState.ComposeTileAnnouncement(tile, biomeDesc);
                if (!string.IsNullOrEmpty(text))
                {
                    TolkHelper.SpeakData(text, SpeechPriority.High);
                }
                spokenTile = tile;
                FlightRecorder.Record("world-hover", tile.ToString() + " -> " + text);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("World hover speech error", ex);
            }
        }

        private static bool WorldGen => WorldNavigationState.Context == WorldNavContext.WorldGen;

        private static bool SurfaceLive()
        {
            return WorldGen
                ? StartingSiteScreenScope.WorldSurfaceLive() && !ShellGuards.MenuOwnsInput()
                : WorldScope.WorldSurfaceLive();
        }

        /// <summary>The play-root test casts to UIRoot_Play; the site page draws its buttons outside any window, and their click never reaches the planet.</summary>
        private static bool PointerOverChrome()
        {
            return WorldGen ? StartingSiteScreenScope.PointerOverChrome() : MapHoverSpeech.PointerOwnedByChrome();
        }

        /// <summary>The drag box owns the speech channel while it grows; see WorldDragSelectSpeech.</summary>
        private static bool DragInFlight()
        {
            WorldSelector selector = Find.WorldSelector;
            return selector != null && selector.dragBox.IsValidAndActive;
        }

        /// <summary>
        /// The tile the pointer is over, or Invalid over open space. Not
        /// <c>GenWorld.MouseTile</c>: a dev-tool override patches it to answer with the keyboard
        /// cursor, which would make the pointer read a tile it is not over.
        /// </summary>
        internal static PlanetTile TileUnderPointer()
        {
            return GenWorld.TileAt(UI.MousePositionOnUI);
        }

        /// <summary>Forgets the spoken tile, so re-entering the planet re-announces the tile under the pointer on the next movement.</summary>
        private static void Reset()
        {
            spokenTile = PlanetTile.Invalid;
        }
    }
}
