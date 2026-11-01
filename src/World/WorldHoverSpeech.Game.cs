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
        private static bool spokenOffSurface;

        /// <summary>The hover reader's own change-only biome memory.</summary>
        private static BiomeDef spokenBiome;

        /// <summary>Called once per world interface OnGUI pass, in both the entry and the play root.</summary>
        internal static void Evaluate()
        {
            try
            {
                RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
                if (settings == null || !settings.HoverSpeech || !WorldNavigationState.IsInitialized)
                {
                    Reset();
                    spokenBiome = null;
                    return;
                }
                if (Event.current == null || Event.current.type != EventType.Repaint)
                {
                    return;
                }
                // Re-armed here for the reason MapHoverSpeech gives.
                if (PointerSurface.PointerInside)
                {
                    spokenOffSurface = false;
                }
                bool worldGen = WorldNavigationState.Context == WorldNavContext.WorldGen;
                bool surfaceLive = worldGen
                    ? StartingSiteScreenScope.WorldSurfaceLive() && !ShellGuards.MenuOwnsInput()
                    : WorldScope.WorldSurfaceLive();
                if (!surfaceLive)
                {
                    Reset();
                    return;
                }
                // The play-root chrome test casts to UIRoot_Play, so world generation tests its own:
                // vanilla's site buttons draw outside any window, and their click never reaches the planet.
                bool chrome = worldGen
                    ? StartingSiteScreenScope.PointerOverBottomButtons()
                    : MapHoverSpeech.PointerOwnedByChrome();
                if (ShellGuards.NonImmediateWindowUnderPointer() || chrome)
                {
                    Reset();
                    return;
                }
                // The drag box owns the speech channel while it grows; see WorldDragSelectSpeech.
                WorldSelector selector = Find.WorldSelector;
                if (selector != null && selector.dragBox.IsValidAndActive)
                {
                    return;
                }

                if (!PointerMotion.MovedThisFrame())
                {
                    return;
                }
                if (!PointerSurface.PointerInside)
                {
                    if (!spokenOffSurface)
                    {
                        spokenOffSurface = true;
                        TolkHelper.SpeakData("RimWorldAccess.Input.Cursor.PointerOffWindow".Loc().ToString(),
                            SpeechPriority.High);
                    }
                    Reset();
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
