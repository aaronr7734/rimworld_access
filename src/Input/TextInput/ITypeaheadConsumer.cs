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

        public TypeaheadConsumer(double priority, Func<bool> isActive, Action<char> handleChar, Action handleBackspace = null)
        {
            Priority = priority;
            IsActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
            HandleChar = handleChar ?? throw new ArgumentNullException(nameof(handleChar));
            HandleBackspace = handleBackspace;
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

        public static TypeaheadConsumer Register(double priority, Func<bool> isActive, Action<char> handleChar, Action handleBackspace = null)
        {
            var c = new TypeaheadConsumer(priority, isActive, handleChar, handleBackspace);
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
            if (!char.IsLetter(c) && !char.IsDigit(c)) return false;
            for (int i = 0; i < Consumers.Count; i++)
            {
                if (Consumers[i].IsActive())
                {
                    Consumers[i].HandleChar(c);
                    return true;
                }
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
