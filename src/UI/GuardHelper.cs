using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Centralizes precondition checks that pair a null/state check with a
    /// canonical screen-reader announcement. Every method returns true when
    /// the guard is satisfied (caller may proceed) and false when it is not
    /// (announcement has already been spoken; caller should return).
    ///
    /// Usage pattern:
    ///   if (!GuardHelper.RequirePawn(pawn)) return;
    ///   if (!GuardHelper.RequireMap(out Map map)) return;
    ///
    /// Priority parameter forwards to TolkHelper so callers that need
    /// SpeechPriority.High can preserve it.
    /// </summary>
    public static class GuardHelper
    {
        /// <summary>
        /// Requires that a game is currently being played (not main menu).
        /// Announces "Not in game" on failure.
        /// </summary>
        public static bool RequireInGame(SpeechPriority priority = SpeechPriority.Normal)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NotInGame".Loc(), priority);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Requires a loaded map (Find.CurrentMap). Announces "No map loaded"
        /// on failure. Unifies the previously-split "No map loaded" /
        /// "No map available" phrasings onto one canonical form.
        /// </summary>
        public static bool RequireMap(out Map map, SpeechPriority priority = SpeechPriority.Normal)
        {
            map = Find.CurrentMap;
            if (map == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoMapLoaded".Loc(), priority);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Requires a loaded map. Announces "No map loaded" on failure.
        /// Use when the caller only needs the presence check, not the map itself.
        /// </summary>
        public static bool RequireMap(SpeechPriority priority = SpeechPriority.Normal)
        {
            return RequireMap(out _, priority);
        }

        /// <summary>
        /// Requires a non-null pawn reference. Announces "No pawn selected"
        /// on failure.
        /// </summary>
        public static bool RequirePawn(Pawn pawn, SpeechPriority priority = SpeechPriority.Normal)
        {
            if (pawn == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoPawnSelected".Loc(), priority);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Requires a non-null item reference (trade item, transferable,
        /// inventory entry, etc.). Announces "No item selected" on failure.
        /// </summary>
        public static bool RequireItem<T>(T item, SpeechPriority priority = SpeechPriority.Normal) where T : class
        {
            if (item == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoItemSelected".Loc(), priority);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Requires a non-null building reference. Announces
        /// "No building to configure" on failure.
        /// </summary>
        public static bool RequireBuilding(Thing building, SpeechPriority priority = SpeechPriority.Normal)
        {
            if (building == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoBuildingToConfigure".Loc(), priority);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Requires that world navigation is active. Announces
        /// "World navigation not active" on failure. Only checks IsActive —
        /// callers that also need IsInitialized can check that inline.
        /// </summary>
        public static bool RequireWorldNav(SpeechPriority priority = SpeechPriority.Normal)
        {
            if (!WorldNavigationState.IsActive)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.WorldNavigationNotActive".Loc(), priority);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Requires a valid selected world tile. Announces
        /// "No valid tile selected" on failure.
        /// </summary>
        public static bool RequireValidTile(PlanetTile tile, SpeechPriority priority = SpeechPriority.Normal)
        {
            if (!tile.Valid)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoValidTileSelected".Loc(), priority);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Requires a valid cursor position on the map. Announces
        /// "Invalid cursor position" on failure. Unifies the previously-split
        /// "Invalid position" / "Invalid cursor position" phrasings onto one
        /// canonical form.
        /// </summary>
        public static bool RequireValidCursor(IntVec3 pos, SpeechPriority priority = SpeechPriority.Normal)
        {
            if (!pos.IsValid)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.InvalidCursorPosition".Loc(), priority);
                return false;
            }
            return true;
        }
    }
}
