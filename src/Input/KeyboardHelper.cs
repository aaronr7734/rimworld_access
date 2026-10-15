using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class KeyboardHelper
    {
        /// <summary>True when the last RemapCharacterToKeyCode remapped a character event to a KeyCode; ctrl/alt flags may then be AltGr artifacts and should be ignored.</summary>
        public static bool WasCharacterRemapped { get; private set; }

#if DEBUG
        // Dev-bridge injection seam (ShellDev.Inject): the modifier helpers read PHYSICAL key state
        // via Input.GetKey, which a synthetic Event cannot fake, so these flags stand in while an
        // injected event is replayed. DEBUG-only; Release keeps the bare physical reads.
        internal static bool InjectionOverrideActive;
        internal static bool InjectedCtrl;
        internal static bool InjectedAlt;
#else
        // Release stub for the injection seam: a const false, so callers outside DEBUG blocks still
        // compile and the compiler folds every `!InjectionOverrideActive` term away.
        internal const bool InjectionOverrideActive = false;
#endif

        /// <summary>
        /// True if either ALT key is physically held. Use instead of Event.current.alt: on some layouts
        /// Windows intercepts Left Alt for menu acceleration before Unity sees it.
        /// </summary>
        public static bool IsAltHeld
        {
            get
            {
#if DEBUG
                if (InjectionOverrideActive)
                    return InjectedAlt;
#endif
                return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            }
        }

        /// <summary>
        /// True if a Ctrl-equivalent key is physically held, including Cmd on macOS. Use instead of
        /// Input.GetKey(KeyCode.LeftControl) for cross-platform compatibility.
        /// Tab special case: on macOS neither Cmd+Tab (OS app switcher) nor physical Ctrl+Tab reaches
        /// Unity OnGUI, only Alt+Tab, so while the current event's key is Tab this treats Alt as the
        /// Ctrl substitute — `key == KeyCode.Tab &amp;&amp; IsCtrlHeld` then works on every platform.
        /// </summary>
        public static bool IsCtrlHeld
        {
            get
            {
#if DEBUG
                // Injected chords express Ctrl intent directly; the Mac Tab substitution models
                // physical keyboards only.
                if (InjectionOverrideActive)
                    return InjectedCtrl;
#endif
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    return true;
                if (!NativeLibraryLoader.IsMacOS)
                    return false;
                // Mac Tab substitution: Option stands in for Ctrl, which is undeliverable with Tab.
                if (Event.current != null && Event.current.keyCode == KeyCode.Tab)
                    return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                // Outside the Tab special case, Cmd substitutes for Ctrl as usual.
                return Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            }
        }

        /// <summary>
        /// User-facing label for the Ctrl modifier of a Tab chord: "Option" on macOS, where Ctrl+Tab
        /// maps to Option+Tab (see IsCtrlHeld). <see cref="CtrlLabelFor"/> names it for any key.
        /// </summary>
        public static string CtrlLabel =>
            NativeLibraryLoader.IsMacOS ? "Option" : "Ctrl";

        public static string CtrlLabelFor(KeyCode key)
        {
            if (!NativeLibraryLoader.IsMacOS)
                return "Ctrl";
            return key == KeyCode.Tab ? "Option" : "Command";
        }

        /// <summary>Twin detection's order-free half: 1.7 sends a keypress's character before its keyCode event, which the frame stamps assume comes first.</summary>
        internal static bool IsPhysicallyDown(KeyCode key)
        {
            return !InjectionOverrideActive && (Input.GetKey(key) || Input.GetKeyDown(key));
        }

        // The frame a real KeyCode.RightBracket arrived, so its follow-up character event is not remapped too.
        private static int lastRightBracketFrame = -1;

        /// <summary>
        /// True if this frame's character is the twin of a physical ] keypress, so the character handler
        /// can discard it; on non-US layouts it can be a letter leaking into typeahead.
        /// </summary>
        public static bool WasRightBracketThisFrame =>
            lastRightBracketFrame == Time.frameCount || IsPhysicallyDown(KeyCode.RightBracket);

        // Frame tracking for KeypadMultiply: US numpad * sends keyCode then character='*' in one
        // frame, while an AZERTY dedicated * key sends only character='*' with keyCode=None.
        private static int lastKeypadMultiplyFrame = -1;

        // Frame tracking for Shift+Slash: US sends keyCode=Slash+shift then character='?' in one
        // frame, while on other layouts ? can be a direct key sending only the character.
        private static int lastSlashShiftFrame = -1;

        // Frame tracking for real letter/digit keyCodes, so the follow-up character event for the SAME
        // key (Unity's twin keyCode=None event) is not also remapped — which would fire a modifier
        // shortcut twice on layouts that send both.
        private static KeyCode lastAlphaNumKeyCode = KeyCode.None;
        private static int lastAlphaNumFrame = -1;

        /// <summary>
        /// Remaps character-only KeyDown events to their equivalent KeyCode: on non-US layouts,
        /// characters like ] arrive via AltGr as keyCode=None with only the character set. US layouts
        /// send both events in one frame, which the frame tracking above discards. Call after reading
        /// Event.current.keyCode, before any KeyCode.None early-return guard.
        /// </summary>
        public static KeyCode RemapCharacterToKeyCode(KeyCode key)
        {
            WasCharacterRemapped = false;

            // If we see a real RightBracket keyCode (US layout), record the frame
            if (key == KeyCode.RightBracket)
            {
                lastRightBracketFrame = Time.frameCount;
                return key;
            }

            // If we see a real KeypadMultiply keyCode (numpad *), record the frame
            if (key == KeyCode.KeypadMultiply)
            {
                lastKeypadMultiplyFrame = Time.frameCount;
                return key;
            }

            // Shift+Alpha8 (US main-row *): record the frame like numpad *, so the follow-up
            // character='*' event is not remapped.
            if (key == KeyCode.Alpha8 && Event.current.shift)
            {
                lastKeypadMultiplyFrame = Time.frameCount;
                return key;
            }

            // Shift+Slash (US ?): record the frame so the follow-up character='?' is not remapped.
            if (key == KeyCode.Slash && Event.current.shift)
            {
                lastSlashShiftFrame = Time.frameCount;
                return key;
            }

            if (key != KeyCode.None)
            {
                // Record real letter/digit keyCodes so the twin character event isn't double-remapped.
                if ((key >= KeyCode.A && key <= KeyCode.Z) || (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9))
                {
                    lastAlphaNumKeyCode = key;
                    lastAlphaNumFrame = Time.frameCount;
                }
                return key;
            }

            // Recover letter/digit SHORTCUTS arriving as character-only events: on layouts like AZERTY
            // a modifier+letter combo such as Alt+R comes through with keyCode=None, so every handler
            // gating on `key == KeyCode.None` bails before the shortcut runs. Gated on a held Alt/Ctrl
            // so ordinary typing still flows through as character events, restricted to ASCII a-z/0-9
            // so AltGr symbols and accented characters are untouched, and skipped when the same key
            // already produced a real keyCode this frame.
            if (IsAltHeld || IsCtrlHeld)
            {
                char ch = Event.current.character;
                KeyCode mapped = KeyCode.None;
                if (ch >= 'a' && ch <= 'z')
                    mapped = KeyCode.A + (ch - 'a');
                else if (ch >= 'A' && ch <= 'Z')
                    mapped = KeyCode.A + (ch - 'A');
                else if (ch >= '0' && ch <= '9')
                    mapped = KeyCode.Alpha0 + (ch - '0');

                if (mapped != KeyCode.None &&
                    !(Time.frameCount == lastAlphaNumFrame && lastAlphaNumKeyCode == mapped))
                {
                    WasCharacterRemapped = true;
                    return mapped;
                }
            }

            switch (Event.current.character)
            {
                case ']':
                    // Skip the twin of a real RightBracket keypress (US layouts).
                    // German layouts send Alpha9 instead, so the frame will not match and we remap.
                    if (WasRightBracketThisFrame)
                        return key;
                    WasCharacterRemapped = true;
                    return KeyCode.RightBracket;
                case '*':
                    // AZERTY sends a dedicated * as keyCode=None + character='*'. US Shift+8 sends
                    // Alpha8, so there is no frame conflict; numpad * is covered by frame tracking.
                    if (Time.frameCount == lastKeypadMultiplyFrame
                        || IsPhysicallyDown(KeyCode.KeypadMultiply)
                        || (Event.current.shift && IsPhysicallyDown(KeyCode.Alpha8)))
                        return key;
                    WasCharacterRemapped = true;
                    return KeyCode.KeypadMultiply;
                case '?':
                    // Non-US layouts can send ? as keyCode=None + character='?'; the US Shift+/ pair is
                    // covered by frame tracking.
                    if (Time.frameCount == lastSlashShiftFrame
                        || (Event.current.shift && IsPhysicallyDown(KeyCode.Slash)))
                        return key;
                    WasCharacterRemapped = true;
                    return KeyCode.Slash;
                default:
                    return key;
            }
        }

        /// <summary>RimWorld 1.7 stores Event.shift/alt apart from modifiers (Unity derived them), so all three are written.</summary>
        public static void SetEventModifiers(Event e, EventModifiers modifiers)
        {
            e.modifiers = modifiers;
            e.shift = (modifiers & EventModifiers.Shift) != 0;
            e.alt = (modifiers & EventModifiers.Alt) != 0;
        }

        /// <summary>Applies <see cref="RemapCharacterToKeyCode"/> globally by writing back to Event.current.keyCode, so every downstream patch sees the remapped key.</summary>
        public static void ApplyGlobalRemap()
        {
            // macOS: remap Cmd to Ctrl before any other keyboard processing, so downstream checks of
            // Event.current.control see Cmd presses. Tab is unaffected — neither Cmd+Tab nor Ctrl+Tab
            // reaches Unity on macOS, so Tab shortcuts use Option instead.
            if (NativeLibraryLoader.IsMacOS && (Event.current.modifiers & EventModifiers.Command) != 0)
            {
                Event.current.modifiers |= EventModifiers.Control;
                Event.current.modifiers &= ~EventModifiers.Command;
            }

            if (Event.current.type != EventType.KeyDown)
                return;

            var original = Event.current.keyCode;
            var remapped = RemapCharacterToKeyCode(original);
            if (remapped != original)
                Event.current.keyCode = remapped;
        }

    }

    /// <summary>
    /// Highest-priority UIRootOnGUI prefix, remapping layout-dependent characters to their canonical
    /// KeyCodes before any other patch reads Event.current.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class KeyRemapPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First + 100)]
        public static void Prefix()
        {
            KeyboardHelper.ApplyGlobalRemap();
        }
    }
}
