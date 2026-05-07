using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Single-active-session model. UnifiedKeyboardPatch checks <see cref="IsActive"/>
    /// at priority -1.5 and routes keyboard events to <see cref="Active"/>. Only one
    /// editing session at a time — matches RimWorld's actual modality.
    /// </summary>
    public static class TextInputManager
    {
        public static TextInputController Active { get; private set; }
        public static bool IsActive => Active != null;

        private static int lastHandledFrame = -1;

        /// <summary>
        /// True if the modal controller handled an event during this Unity frame. Used to
        /// defend against RimWorld code paths (Window.OnAcceptKeyPressed, OnCancelKeyPressed)
        /// that fire independently of <c>Event.current.Use()</c> — the handler may have
        /// just closed the session by the time those fire, so IsActive alone isn't enough.
        /// </summary>
        public static bool HandledEventThisFrame => Time.frameCount == lastHandledFrame;

        public static void MarkHandledThisFrame()
        {
            lastHandledFrame = Time.frameCount;
        }

        public static void SetActive(TextInputController controller)
        {
            Active = controller;
        }

        public static void Clear()
        {
            Active = null;
        }
    }
}
