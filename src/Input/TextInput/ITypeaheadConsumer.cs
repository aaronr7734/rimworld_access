using System;
using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Delegate-based typeahead consumer registration. We use delegates rather than an
    /// interface because most typeahead-using States in the mod are static classes, which
    /// can't implement interfaces. Priority is a small floating-point number; lower runs
    /// first when multiple consumers are simultaneously active (rare in practice — most
    /// menus are modal, so IsActive is mutually exclusive).
    /// </summary>
    public sealed class TypeaheadConsumer
    {
        public double Priority { get; }
        public Func<bool> IsActive { get; }
        public Action<char> HandleChar { get; }
        public Action HandleBackspace { get; }

        /// <summary>
        /// When false, digit characters (0-9) are not routed to this consumer. Used by menus
        /// where the number row is an action shortcut rather than search text — e.g. the work
        /// menu (0-4 set priority), the schedule menu (1-9/0 pick a brush), and the assign
        /// table (whose rows are pawn names, never digits). Without this, Unity's follow-up
        /// character event for the digit keypress would land in the menu's typeahead buffer
        /// even as the digit's KeyCode event performs its action. Letters still pass through.
        /// </summary>
        public bool AcceptsDigits { get; }

        public TypeaheadConsumer(double priority, Func<bool> isActive, Action<char> handleChar, Action handleBackspace = null, bool acceptsDigits = true)
        {
            Priority = priority;
            IsActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
            HandleChar = handleChar ?? throw new ArgumentNullException(nameof(handleChar));
            HandleBackspace = handleBackspace;
            AcceptsDigits = acceptsDigits;
        }
    }

    /// <summary>
    /// Central registry. UnifiedKeyboardPatch's priority -1.5 character handler walks
    /// consumers in registration priority order and dispatches the layout-aware
    /// <c>Event.current.character</c> to the first one whose <see cref="TypeaheadConsumer.IsActive"/>
    /// returns true.
    ///
    /// Works on any keyboard layout. The previous KeyCode-based pre-registration mechanism
    /// (now removed) failed for keys producing letters where Unity reported a non-letter
    /// KeyCode (e.g. Cyrillic "б" reported as <c>KeyCode.Comma</c> on a US-layout-shaped key).
    /// The dispatcher reads the character directly with no KeyCode gate.
    /// </summary>
    public static class TypeaheadDispatcher
    {
        private static readonly List<TypeaheadConsumer> Consumers = new List<TypeaheadConsumer>();
        private static readonly Dictionary<TypeaheadConsumer, bool> lastSeenActive = new Dictionary<TypeaheadConsumer, bool>();
        private static int suppressedFrame = -1;

        public static void Register(TypeaheadConsumer consumer)
        {
            if (consumer == null || Consumers.Contains(consumer)) return;
            Consumers.Add(consumer);
            Consumers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>
        /// True if any registered consumer is currently active. Used by <see cref="ImeInputHost"/>
        /// to decide whether a typeahead text sink is live (so the IME funnel should engage).
        /// </summary>
        public static bool AnyActive()
        {
            for (int i = 0; i < Consumers.Count; i++)
            {
                if (Consumers[i].IsActive()) return true;
            }
            return false;
        }

        /// <summary>
        /// The first active consumer in priority order, or null if none is active. This is the one
        /// that owns input right now (menus are modal, so it's unique in practice). Used by
        /// <see cref="MenuSearchState"/> to detect when the menu under an open search prompt has gone
        /// away or changed, so the prompt can auto-close instead of leaking the IME funnel into it.
        /// </summary>
        public static TypeaheadConsumer ActiveConsumer
        {
            get
            {
                for (int i = 0; i < Consumers.Count; i++)
                {
                    if (Consumers[i].IsActive()) return Consumers[i];
                }
                return null;
            }
        }

        public static TypeaheadConsumer Register(double priority, Func<bool> isActive, Action<char> handleChar, Action handleBackspace = null, bool acceptsDigits = true)
        {
            var c = new TypeaheadConsumer(priority, isActive, handleChar, handleBackspace, acceptsDigits);
            Register(c);
            return c;
        }

        /// <summary>
        /// Manually suppress character dispatch for the remainder of the current Unity
        /// frame. Rarely needed now that <see cref="PollActivations"/> auto-detects
        /// consumer activations — kept for explicit override cases (e.g. after a text
        /// input session closes and we don't want the twin char event leaking).
        /// </summary>
        public static void SuppressDispatchForThisFrame()
        {
            suppressedFrame = Time.frameCount;
        }

        /// <summary>
        /// Walks every registered consumer and checks whether its <c>IsActive</c> just
        /// flipped from false to true since the last poll. If so, marks the current
        /// frame for dispatch suppression — this is what prevents the twin
        /// <c>KeyCode.None</c>/char event that accompanies a letter-key opener (pressing
        /// 'I' to open the inventory, 'G' for gizmos, 'Z' for the scanner, etc.) from
        /// being routed straight into the freshly-active consumer's typeahead buffer.
        ///
        /// Called from two places:
        ///   (a) A Harmony prefix on <c>UIRoot.UIRootUpdate</c> — once per Unity frame,
        ///       so close-then-reopen cycles are caught even when no char events fire
        ///       between them.
        ///   (b) The top of <see cref="TryDispatchChar"/> — catches activations that
        ///       happen WITHIN a single frame's OnGUI pass (e.g. the Z-key handler
        ///       opens the scanner, then the 'z' char event arrives later in the same
        ///       frame, before <c>UIRootUpdate</c> runs again).
        /// </summary>
        public static void PollActivations()
        {
            int currentFrame = Time.frameCount;
            for (int i = 0; i < Consumers.Count; i++)
            {
                var consumer = Consumers[i];
                bool nowActive = consumer.IsActive();
                bool wasActive = lastSeenActive.TryGetValue(consumer, out bool prev) && prev;
                if (nowActive && !wasActive)
                {
                    suppressedFrame = currentFrame;
                }
                lastSeenActive[consumer] = nowActive;
            }
        }

        /// <summary>
        /// Forward a layout-aware character to the first active consumer.
        /// Only letters and digits qualify. Returns true if a consumer accepted.
        /// </summary>
        public static bool TryDispatchChar(char c)
        {
            PollActivations();
            if (Time.frameCount == suppressedFrame) return false;
            bool isDigit = char.IsDigit(c);
            if (!char.IsLetter(c) && !isDigit) return false;
            for (int i = 0; i < Consumers.Count; i++)
            {
                if (!Consumers[i].IsActive()) continue;

                // The first active consumer is the one that owns input (menus are modal, so
                // IsActive is mutually exclusive in practice). If it doesn't accept digits,
                // swallow the digit here rather than letting it fall through to a lower-priority
                // consumer — the active menu has claimed this keypress for its own number-key
                // action and never wants it as search text.
                if (isDigit && !Consumers[i].AcceptsDigits)
                    return false;

                Consumers[i].HandleChar(c);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Forward a backspace to the first active consumer with a backspace handler.
        /// </summary>
        public static bool TryDispatchBackspace()
        {
            for (int i = 0; i < Consumers.Count; i++)
            {
                if (Consumers[i].IsActive() && Consumers[i].HandleBackspace != null)
                {
                    Consumers[i].HandleBackspace();
                    return true;
                }
            }
            return false;
        }
    }
}
