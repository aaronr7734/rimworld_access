using System;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Bridges Unity IMGUI's two-event-per-keystroke model for typeahead search.
    /// Unity sends: (1) KeyDown with keyCode but no character, then (2) KeyDown with
    /// keyCode=None and the layout-aware character. The KeyCode handler registers a
    /// callback via RequestCharacter; the character handler dispatches via TryConsume.
    /// This enables typeahead to work with any keyboard layout (Cyrillic, etc.).
    /// </summary>
    public static class TypeaheadCharacterBuffer
    {
        private static Action<char> pendingCallback;
        private static int pendingFrame;

        /// <summary>
        /// Register a callback to receive the layout-aware character from the next
        /// character event. Called from KeyCode event handlers that consume alpha/number keys.
        /// </summary>
        public static void RequestCharacter(Action<char> callback)
        {
            pendingCallback = callback;
            pendingFrame = Time.frameCount;
        }

        /// <summary>
        /// Check if the current event is a character event and forward it to the pending callback.
        /// Call this at the top of any high-priority Harmony patch that might consume character
        /// events before they reach UnifiedKeyboardPatch's priority -1.5 handler.
        /// Returns true if the event was consumed (caller should call Event.current.Use()).
        /// </summary>
        public static bool TryForwardCharacterEvent()
        {
            var e = Event.current;
            if (e.keyCode == KeyCode.None && e.character != '\0' && !char.IsControl(e.character))
            {
                return TryConsumePendingCharacter(e.character);
            }
            return false;
        }

        /// <summary>
        /// Dispatch a character to the pending callback if one was registered this frame.
        /// Called from the priority -1.5 section when keyCode=None, character!='\0'.
        /// Returns true if the character was consumed.
        /// </summary>
        public static bool TryConsumePendingCharacter(char c)
        {
            if (pendingCallback != null && Time.frameCount == pendingFrame)
            {
                var cb = pendingCallback;
                pendingCallback = null;
                cb(c);
                return true;
            }
            pendingCallback = null;
            return false;
        }
    }
}
