using RimWorld.Planet;
using UnityEngine;
using Verse;
using RimWorldAccess.Platform;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Bridges keyboard map navigation into mouse hovering: Alt+J puts the real OS pointer on the
    /// keyboard cursor's tile, Alt+Shift+J pulls the cursor onto the hovered tile, Alt+Shift+K
    /// toggles the pointer following every cursor move, and regaining OS focus snaps the pointer
    /// back silently.
    /// The warp is computed as a DELTA — target minus pointer in Unity screen pixels, converted to
    /// OS units and applied to the pointer's current global position — so window origin, display
    /// origin and fullscreen-versus-windowed all cancel out instead of needing measurement.
    /// The settle pass is closed-loop: Input.mousePosition lags a warp by at least a frame and
    /// around a refocus can be a multi-frame-stale placeholder, so nothing is measured or corrected
    /// until the reading has changed since the warp — firing twice off one stale reading applies
    /// the same delta twice and lands tiles past the target. It ends when the measured position
    /// agrees with the target or the correction budget runs out.
    /// Every frame from issue to settle reports through <see cref="SuppressingHover"/>, and the
    /// hover channels swallow pointer motion for exactly those frames: the warp is not hand
    /// movement, so it must neither speak nor play the tile sound, however long it takes.
    /// </summary>
    internal static class PointerWarpState
    {
        private const int MaxCorrections = 12;
        private const float SettledPixels = 2f;

        /// <summary>Frames a warp may wait for Input.mousePosition to register it before the
        /// stale reading is measured anyway (covers a warp whose delta was ~zero).</summary>
        private const int WarpRegisterFrameBudget = 8;

        /// <summary>
        /// Focused frames the refocus snap always waits before warping. Unity walks
        /// Input.mousePosition to the real pointer over the first frames back, and macOS can ignore
        /// a warp issued while app activation is still completing — Application.isFocused flips a
        /// frame or two early — so a snap fired on the first focused frame computes its delta from
        /// a placeholder and lands tiles off the cursor.
        /// </summary>
        private const int SnapFrameFloor = 6;

        /// <summary>Frames after which the snap fires without two agreeing readings — a pointer
        /// still travelling when focus returns never produces them, and the closed-loop settle
        /// corrects whatever error the unstable reading causes.</summary>
        private const int SnapFrameCeiling = 30;

        private static bool settling;
        private static bool silent;
        private static int corrections;

        /// <summary>
        /// Persistent multiplier on the display mode's points-to-pixels quotient, calibrated by the
        /// settle pass. Session state on purpose: the first measurable correction fixes it and every
        /// later warp lands in one step.
        /// </summary>
        private static float warpGain = 1f;
        private static Vector2 targetPixels;
        private static Vector2 readingAtWarp;
        private static int warpWaitFrames;
        private static bool wasFocused = true;
        private static bool snapPending;
        private static int snapWaitFrames;
        private static Vector2 snapReading;
        private static IntVec3 lastFollowedCell = IntVec3.Invalid;
        private static int lastFollowedMapId = -1;

        /// <summary>
        /// True for every frame between a warp's issue and its settle, the refocus snap's pre-warp
        /// wait included. The hover channels treat these frames as stillness.
        /// </summary>
        internal static bool SuppressingHover
        {
            get { return settling || snapPending; }
        }

        /// <summary>Alt+J: warp the OS pointer onto the keyboard cursor's tile.</summary>
        internal static void WarpPointerToCursor()
        {
            settling = false;
            snapPending = false;
            silent = false;
            Map map = Find.CurrentMap;
            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            if (!SystemPointer.HostFocused || map == null || !cursor.InBounds(map) || Find.Camera == null)
                return;

            // An off-screen cursor takes the offset announcement instead — see BeginWarp.
            if (!BeginWarp(cursor))
                AnnounceOffset(cursor);
        }

        /// <summary>
        /// Regaining OS focus leaves the pointer wherever another application dropped it, so every
        /// pointer-derived reading disagrees with where the player actually is. Snap it to the
        /// keyboard cursor's tile and say nothing: no offset announcement when the warp is
        /// impossible, and the settle pass's landing announcement is suppressed too.
        /// </summary>
        private static void SnapPointerToCursorSilently()
        {
            settling = false;
            Map map = Find.CurrentMap;
            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            if (map == null || !cursor.InBounds(map) || Find.Camera == null)
                return;

            if (BeginWarp(cursor))
                silent = true;
        }

        /// <summary>
        /// Issues the warp toward <paramref name="cursor"/>'s tile and arms the settle pass. False
        /// when the tile is off the game's own surface, where warping would drop the pointer on
        /// another application, or the platform warp is unavailable.
        /// </summary>
        private static bool BeginWarp(IntVec3 cursor)
        {
            Vector3 screenPoint = Find.Camera.WorldToScreenPoint(cursor.ToVector3Shifted());
            if (!BeginWarpAtPixels(new Vector2(screenPoint.x, screenPoint.y)))
                return false;

            lastFollowedCell = cursor;
            lastFollowedMapId = Find.CurrentMap != null ? Find.CurrentMap.uniqueID : -1;
            return true;
        }

        /// <summary>
        /// Issues the warp toward <paramref name="screenPixels"/> and arms the settle pass. False
        /// when the target is off the game's own surface, where warping would drop the pointer on
        /// another application, or the platform warp is unavailable.
        /// </summary>
        private static bool BeginWarpAtPixels(Vector2 screenPixels)
        {
            bool onScreen = screenPixels.x >= 0f && screenPixels.x <= Screen.width
                && screenPixels.y >= 0f && screenPixels.y <= Screen.height;
            if (!onScreen || !TryWarpTo(screenPixels))
                return false;

            targetPixels = screenPixels;
            Vector3 mouse = Input.mousePosition;
            readingAtWarp = new Vector2(mouse.x, mouse.y);
            warpWaitFrames = 0;
            corrections = 0;
            settling = true;
            FlightRecorder.Record("warp", "begin target=" + (int)screenPixels.x + "," + (int)screenPixels.y
                + " reading=" + (int)mouse.x + "," + (int)mouse.y + " gain=" + warpGain.ToString("0.00"));
            return true;
        }

        /// <summary>The UI follow's warp: silent, and it leaves <see cref="lastFollowedCell"/> alone — the map follow's memory is the map's.</summary>
        internal static bool BeginUiWarp(Vector2 screenPixels)
        {
            if (!BeginWarpAtPixels(screenPixels))
                return false;

            silent = true;
            return true;
        }

        /// <summary>
        /// Drives the settle pass on passes where <see cref="Tick"/> cannot: its only call site runs
        /// while a map is drawing, so a UI warp issued on a menu would never settle and would
        /// suppress hover for good.
        /// </summary>
        internal static void TickUiWarpSettle()
        {
            // Exact complement of the map tick site's guard, so exactly one site runs TickSettle
            // on any frame.
            if (settling && (Find.CurrentMap == null || !WorldRendererUtility.DrawingMap
                || !MapNavigationState.IsInitialized))
                TickSettle();
        }

        /// <summary>Alt+Shift+J: pull the keyboard cursor onto the hovered tile. The planet is tested first: Find.CurrentMap stays set while it renders.</summary>
        internal static void PullCursorToPointer()
        {
            if (WorldNavigationState.IsActive)
            {
                PullWorldCursorToPointer();
                return;
            }
            Map map = Find.CurrentMap;
            if (!SystemPointer.HostFocused || map == null)
                return;

            // A pointer off the game's surface still resolves to an in-bounds cell by camera ray,
            // so the bounds test alone would hand the cursor a cell nobody is pointing at.
            if (!PointerSurface.PointerInside)
            {
                TolkHelper.Speak("RimWorldAccess.Input.Cursor.PointerOffWindow".Loc());
                return;
            }

            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map))
            {
                TolkHelper.Speak("RimWorldAccess.Input.Map.Boundary".Loc());
                return;
            }

            MapNavigationState.CurrentCursorPosition = cell;
            // The pointer is already here; without this the follow poll would yank it to the
            // cell's centre the frame after a deliberate hand placement.
            lastFollowedCell = cell;
            lastFollowedMapId = map.uniqueID;
            // Composed rather than announced through AnnouncePosition: a deliberate jump must
            // speak even when it lands on the last-announced tile.
            TolkHelper.SpeakData(MapArrowKeyHandler.ComposePositionAnnouncement(cell, map, null, allowStateWrites: true));
        }

        private static void PullWorldCursorToPointer()
        {
            if (!SystemPointer.HostFocused)
                return;

            if (!PointerSurface.PointerInside)
            {
                TolkHelper.Speak("RimWorldAccess.Input.Cursor.PointerOffWindow".Loc());
                return;
            }

            PlanetTile tile = WorldHoverSpeech.TileUnderPointer();
            if (!tile.Valid)
            {
                PointerRouting.RejectNoTarget();
                return;
            }

            WorldNavigationState.PullCursorToTile(tile);
        }

        /// <summary>Alt+Shift+M: toggle reading what the mouse pointer moves over.</summary>
        internal static void ToggleMouseTracking()
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null)
                return;
            settings.HoverSpeech = !settings.HoverSpeech;
            LoadedModManager.GetMod<RimWorldAccessMod_Settings>()?.WriteSettings();
            // Re-seed so the resting pointer is the baseline; otherwise the first hover poll reads
            // the stale baseline as hand movement and interrupts the toggle announcement below.
            if (settings.HoverSpeech)
                PointerMotion.Rebase();
            TolkHelper.Speak((settings.HoverSpeech
                ? "RimWorldAccess.Input.Cursor.MouseTrackingOn"
                : "RimWorldAccess.Input.Cursor.MouseTrackingOff").Loc());
        }

        /// <summary>Alt+Shift+K: toggle the pointer following every keyboard cursor move.</summary>
        internal static void ToggleFollowKeyboard()
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null)
                return;
            settings.PointerFollowsKeyboard = !settings.PointerFollowsKeyboard;
            LoadedModManager.GetMod<RimWorldAccessMod_Settings>()?.WriteSettings();
            // Forgotten so the poll warps to the current cell straight away when turning on.
            lastFollowedCell = IntVec3.Invalid;
            TolkHelper.Speak((settings.PointerFollowsKeyboard
                ? "RimWorldAccess.Input.Cursor.PointerFollowsKeyboardOn"
                : "RimWorldAccess.Input.Cursor.PointerFollowsKeyboardOff").Loc());
        }

        /// <summary>
        /// Called once per map OnGUI pass. The OS applies a warp before the next frame's input poll,
        /// so the residual is only measurable on a later pass than the one that asked for it.
        /// </summary>
        internal static void Tick()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            RefocusClock.NotePass();
            // An app switch mid-warp abandons everything silently: the pointer is no longer ours to
            // measure, and warping it would drag it out from under the application switched to.
            if (!SystemPointer.HostFocused)
            {
                wasFocused = false;
                settling = false;
                snapPending = false;
                return;
            }
            if (!wasFocused)
            {
                wasFocused = true;
                snapPending = true;
                snapWaitFrames = 0;
                snapReading = new Vector2(float.NaN, float.NaN);
            }
            if (snapPending)
            {
                TickPendingSnap();
                return;
            }
            if (settling)
            {
                TickSettle();
                return;
            }
            TickFollowKeyboard();
        }

        /// <summary>
        /// The refocus snap's pre-warp wait: no warp until Input.mousePosition has held still for two
        /// consecutive frames past the floor, or the ceiling runs out. See
        /// <see cref="SnapFrameFloor"/> for why firing earlier lands tiles off the cursor.
        /// </summary>
        private static void TickPendingSnap()
        {
            // No warp of any kind inside the refocus turbulence window: macOS can cancel the
            // Command+Tab switch outright over a warp issued while activation is still completing,
            // and Input.mousePosition is a fabricated placeholder walk for over a second, so a delta
            // computed from it lands the pointer anywhere. A frame floor cannot bound this — six
            // frames is ~44ms on a fast map — so the wait is wall-clock, and the stability count
            // starts only once the window has passed.
            if (!RefocusClock.SettledFor(RefocusClock.WarpQuietSeconds))
            {
                snapWaitFrames = 0;
                snapReading = new Vector2(float.NaN, float.NaN);
                return;
            }
            snapWaitFrames++;
            Vector3 mouse = Input.mousePosition;
            Vector2 reading = new Vector2(mouse.x, mouse.y);
            bool stable = reading == snapReading && snapWaitFrames >= SnapFrameFloor;
            if (stable || snapWaitFrames >= SnapFrameCeiling)
            {
                snapPending = false;
                SnapPointerToCursorSilently();
                return;
            }
            snapReading = reading;
        }

        /// <summary>
        /// One closed-loop settle step: wait for the last warp to register in
        /// Input.mousePosition, then measure the residual and correct or finish.
        /// </summary>
        private static void TickSettle()
        {
            Vector3 mouse = Input.mousePosition;
            Vector2 reading = new Vector2(mouse.x, mouse.y);
            if (reading == readingAtWarp && warpWaitFrames < WarpRegisterFrameBudget)
            {
                warpWaitFrames++;
                return;
            }

            Vector2 requested = targetPixels - readingAtWarp;
            Vector2 observed = reading - readingAtWarp;
            warpGain = WarpGainMath.Next(warpGain, requested.magnitude, observed.magnitude,
                Vector2.Dot(requested, observed));

            float residual = Vector2.Distance(reading, targetPixels);
            if (residual > SettledPixels && corrections < MaxCorrections && TryWarpTo(targetPixels))
            {
                corrections++;
                FlightRecorder.Record("warp", "correct residual=" + (int)residual + " gain=" + warpGain.ToString("0.00")
                    + " " + corrections + "/" + MaxCorrections);
                readingAtWarp = reading;
                warpWaitFrames = 0;
                return;
            }

            FlightRecorder.Record("warp", "settled residual=" + (int)residual + " corrections=" + corrections);
            settling = false;
            if (!silent)
                AnnounceLanding();
        }

        /// <summary>
        /// The Alt+Shift+K follow: whenever the keyboard cursor moves to a new cell, warp the pointer
        /// there silently, the screen-reader convention of the mouse shadowing the review cursor.
        /// Stands down while the hand owns the pointer: a held button, a drag in flight, or a shape
        /// the pointer is stretching.
        /// </summary>
        private static void TickFollowKeyboard()
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null || !settings.PointerFollowsKeyboard)
                return;
            // Same turbulence hold as the refocus snap. Nothing is recorded during the hold, so the
            // first poll after it warps to the current cell.
            if (!RefocusClock.SettledFor(RefocusClock.WarpQuietSeconds))
                return;
            // A scope publishing a focused rect owns the pointer: the review cursor is in the UI,
            // not on the map. Map follow resumes when the rect stops publishing.
            if (UiPointerFollow.HasFreshTarget)
                return;
            Map map = Find.CurrentMap;
            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            if (map == null || !cursor.IsValid || !cursor.InBounds(map) || Find.Camera == null)
                return;
            if (cursor == lastFollowedCell && map.uniqueID == lastFollowedMapId)
                return;
            if (!SystemPointer.Current.Available)
            {
                lastFollowedCell = cursor;
                lastFollowedMapId = map.uniqueID;
                return;
            }
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2))
                return;
            if (Find.DesignatorManager != null && Find.DesignatorManager.Dragger.Dragging)
                return;
            if (ShapePlacementState.ShouldUpdatePreviewOnMove())
                return;

            // BeginWarp records the cell on success; on failure the poll retries next pass, once
            // the camera's own jump has landed.
            if (BeginWarp(cursor))
                silent = true;
        }

        private static bool TryWarpTo(Vector2 screenPixels)
        {
            if (!SystemPointer.HostFocused)
                return false;

            ISystemPointer pointer = SystemPointer.Current;
            double globalX, globalY;
            if (!pointer.Available || !pointer.TryGetPosition(out globalX, out globalY))
                return false;

            double scale = pointer.PointsToPixelsAt(globalX, globalY);
            if (scale <= 0.0)
                scale = 1.0;
            // The display mode's quotient is a guess some configurations misreport; the settle pass
            // measures what each warp actually moved and warpGain absorbs the difference.
            scale *= warpGain;

            Vector3 mouse = Input.mousePosition;
            // Unity's y grows upward, every OS global space here grows downward.
            double dx = (screenPixels.x - mouse.x) / scale;
            double dy = -(screenPixels.y - mouse.y) / scale;
            return pointer.TryWarp(globalX + dx, globalY + dy);
        }

        private static void AnnounceLanding()
        {
            Map map = Find.CurrentMap;
            IntVec3 landed = UI.MouseCell();
            if (map == null || !landed.InBounds(map))
            {
                AnnounceOffset(MapNavigationState.CurrentCursorPosition);
                return;
            }
            TolkHelper.Speak("RimWorldAccess.Input.Cursor.PointerMoved".Loc(landed.x, landed.z));
        }

        /// <summary>The no-warp answer: where the keyboard cursor lies relative to the pointer, so it can be moved there by hand.</summary>
        private static void AnnounceOffset(IntVec3 cursor)
        {
            IntVec3 pointerCell = UI.MouseCell();
            string direction = ScannerDirectionHelper.GetCompassDirection(pointerCell, cursor);
            if (direction == null)
            {
                TolkHelper.Speak("RimWorldAccess.Input.Cursor.PointerAtCursor".Loc());
                return;
            }
            int tiles = Mathf.RoundToInt((cursor - pointerCell).LengthHorizontal);
            TolkHelper.Speak("RimWorldAccess.Input.Cursor.PointerOffset".Loc(tiles, direction));
        }
    }
}
