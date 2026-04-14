using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State for "Go To" coordinate input feature (Ctrl+G).
    /// Allows typing X,Z coordinates to jump the map cursor.
    /// Supports absolute (10,20), partial (10 or ,20), and relative (+10,-20) coordinates.
    /// </summary>
    public static class GoToState
    {
        // Core state
        private static bool isActive = false;

        // Input buffers - one for each coordinate field
        private static string xBuffer = "";
        private static string zBuffer = "";

        // Track which field we're currently editing (false = X, true = Z)
        private static bool isInZField = false;

        /// <summary>
        /// Returns true if coordinate input mode is active (user is typing).
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Activates coordinate input mode (Ctrl+G pressed).
        /// </summary>
        public static void Activate()
        {
            // Clear any previous state
            xBuffer = "";
            zBuffer = "";
            isInZField = false;
            isActive = true;

            // Announce with current position for context
            IntVec3 current = MapNavigationState.CurrentCursorPosition;
            TolkHelper.Speak($"Go to. Currently at {current.x}, {current.z}", SpeechPriority.Normal);
        }

        /// <summary>
        /// Returns true if a menu UI is currently showing that should receive Enter/Escape.
        /// Go To yields to actual menu UI (tree menus, float menus), but NOT to placement mode.
        /// </summary>
        public static bool ShouldYieldToOverlayMenu()
        {
            if (!isActive) return false;

            // Only yield to actual menu UI, not placement mode
            // ArchitectTreeState = category/tool selection tree menu
            // WindowlessFloatMenuState = material selection menu (or other float menus)
            // ShapeSelectionMenuState = shape selection menu (Rectangle, Line, Oval, etc.)
            if (ArchitectTreeState.IsActive || WindowlessFloatMenuState.IsActive || ShapeSelectionMenuState.IsActive)
                return true;

            // Note: We deliberately do NOT yield to:
            // - ArchitectState.IsActive alone (includes placement mode where Go To should work)
            // - ShapePlacementState.IsActive (placement should coexist with Go To)
            // - ArchitectState.IsInPlacementMode (user wants Go To during placement)

            return false;
        }

        /// <summary>
        /// Handles a character input (0-9, +, -).
        /// </summary>
        /// <param name="c">The character to add to current field buffer</param>
        public static void HandleCharacter(char c)
        {
            // Validate: only allow 0-9, +, -
            // + and - only valid at the start of the buffer
            if (c == '+' || c == '-')
            {
                string currentBuffer = isInZField ? zBuffer : xBuffer;
                if (!string.IsNullOrEmpty(currentBuffer))
                {
                    // +/- not at start - ignore silently
                    return;
                }
            }
            else if (c < '0' || c > '9')
            {
                return; // Invalid character
            }

            // Add to appropriate buffer
            if (isInZField)
            {
                zBuffer += c;
            }
            else
            {
                xBuffer += c;
            }

            TolkHelper.Speak(c.ToString(), SpeechPriority.Low);
        }

        /// <summary>
        /// Handles field separator (comma or space pressed).
        /// Switches from X field to Z field.
        /// </summary>
        public static void HandleFieldSeparator()
        {
            if (!isInZField)
            {
                isInZField = true;
                TolkHelper.Speak("Z", SpeechPriority.Normal);
            }
            // If already in Z field, ignore
        }

        /// <summary>
        /// Removes the last character from the current field buffer.
        /// If Z field is empty, switches back to X field.
        /// </summary>
        public static void HandleBackspace()
        {
            if (isInZField)
            {
                if (string.IsNullOrEmpty(zBuffer))
                {
                    // Z buffer empty, go back to X field
                    isInZField = false;
                    TolkHelper.Speak("X", SpeechPriority.Normal);
                }
                else
                {
                    char deleted = zBuffer[zBuffer.Length - 1];
                    zBuffer = zBuffer.Substring(0, zBuffer.Length - 1);
                    TolkHelper.Speak($"Deleted {deleted}", SpeechPriority.Low);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(xBuffer))
                {
                    // X buffer already empty, cancel the input
                    Cancel();
                }
                else
                {
                    char deleted = xBuffer[xBuffer.Length - 1];
                    xBuffer = xBuffer.Substring(0, xBuffer.Length - 1);
                    TolkHelper.Speak($"Deleted {deleted}", SpeechPriority.Low);
                }
            }
        }

        /// <summary>
        /// Confirms the input and moves cursor to the target coordinates.
        /// </summary>
        public static void ConfirmGoTo()
        {
            if (!isActive) return;

            Map map = Find.CurrentMap;
            if (map == null)
            {
                Cancel();
                return;
            }

            IntVec3 current = MapNavigationState.CurrentCursorPosition;

            // Parse X coordinate
            if (!ParseCoordinate(xBuffer, current.x, out int targetX))
            {
                TolkHelper.Speak("Invalid X coordinate", SpeechPriority.Normal);
                return; // Don't close - let user fix it
            }

            // Parse Z coordinate
            if (!ParseCoordinate(zBuffer, current.z, out int targetZ))
            {
                TolkHelper.Speak("Invalid Z coordinate", SpeechPriority.Normal);
                return;
            }

            // Clamp to map bounds
            targetX = Mathf.Clamp(targetX, 0, map.Size.x - 1);
            targetZ = Mathf.Clamp(targetZ, 0, map.Size.z - 1);

            IntVec3 targetPos = new IntVec3(targetX, 0, targetZ);

            // Move cursor
            MapNavigationState.CurrentCursorPosition = targetPos;

            // Update zone/area previews if applicable
            if (ZoneCreationState.IsInCreationMode && ZoneCreationState.HasRectangleStart)
            {
                ZoneCreationState.UpdatePreview(targetPos);
            }
            if (AreaPaintingState.IsActive && AreaPaintingState.HasRectangleStart)
            {
                AreaPaintingState.UpdatePreview(targetPos);
            }

            // Move camera to center on new cursor position
            Find.CameraDriver.JumpToCurrentMapLoc(targetPos);

            // Switch to Cursor mode - camera follows cursor, blocks pawn following
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Cursor;

            // Clear pawn selection context flag
            GizmoNavigationState.PawnJustSelected = false;

            // Play terrain audio feedback
            TerrainDef terrain = targetPos.GetTerrain(map);
            TerrainAudioHelper.PlayTerrainAudio(terrain, 0.5f);

            // Announce position with all contextual prefixes (deep ore, "in area", shape dimensions, etc.)
            // This uses the same announcement path as arrow key movement
            MapArrowKeyHandler.AnnouncePosition(targetPos, map);

            // Close state
            Close();
        }

        /// <summary>
        /// Cancels coordinate input mode (Escape pressed).
        /// </summary>
        public static void Cancel()
        {
            Close();
            TolkHelper.Speak("Cancelled", SpeechPriority.Normal);
        }

        /// <summary>
        /// Closes the coordinate input mode silently.
        /// </summary>
        private static void Close()
        {
            xBuffer = "";
            zBuffer = "";
            isInZField = false;
            isActive = false;
        }

        /// <summary>
        /// Parses a coordinate buffer into an integer value.
        /// Supports absolute values, empty (returns current), and relative (+/-offset).
        /// </summary>
        /// <param name="buffer">The input buffer to parse</param>
        /// <param name="currentValue">The current coordinate value for empty/relative</param>
        /// <param name="result">The parsed result</param>
        /// <returns>True if parsing succeeded, false otherwise</returns>
        private static bool ParseCoordinate(string buffer, int currentValue, out int result)
        {
            result = currentValue; // Default to current if empty

            if (string.IsNullOrEmpty(buffer) || string.IsNullOrWhiteSpace(buffer))
            {
                return true; // Empty = use current value
            }

            buffer = buffer.Trim();

            // Check for relative (starts with + or -)
            if (buffer.StartsWith("+") || buffer.StartsWith("-"))
            {
                if (int.TryParse(buffer, out int offset))
                {
                    result = currentValue + offset;
                    return true;
                }
                return false; // Invalid relative format
            }

            // Absolute value
            if (int.TryParse(buffer, out int absolute))
            {
                result = absolute;
                return true;
            }

            return false; // Invalid format
        }
    }
}
