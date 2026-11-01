using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Captures every <see cref="Widgets.ButtonText(Rect, string, bool, bool, bool, TextAnchor?)"/>
    /// call made during one armed surface's own draw pass: the label as actually rendered and the rect
    /// in the surface's GUI-group space. Scopes that drive real windows whose actions are plain text
    /// buttons (message boxes now; the save/load and options dialogs when they land) read the pass
    /// result instead of re-deriving vanilla's layout math.
    ///
    /// Recording is BRACKETED to the armed surface's draw (<see cref="BeginPass"/> from its prefix,
    /// <see cref="EndPass"/> from its postfix or finalizer) for the same reasons as TooltipCapture:
    /// buttons drawn by other UI in the same frame must not collide with the surface's own, and the
    /// bracket pins the coordinate space to the surface's group. Outside a pass the prefix is one
    /// static bool check.
    /// </summary>
    public static class ButtonTextCapture
    {
        public struct CapturedButton
        {
            /// <summary>The surface's own group space — what the focus ring draws against.</summary>
            public Rect Rect;
            public string Label;

            /// <summary>Absolute UI points (the one GuiSpace space), for pointer matching.</summary>
            public Rect ScreenRect;

            /// <summary>ScreenRect clipped to what is actually on screen — empty when scrolled out.</summary>
            public Rect VisibleScreenRect;

            /// <summary>The clip the button was drawn under; its depth breaks pointer-routing ties.</summary>
            public GuiSpace.ClipKey Clip;
        }

        private const float FocusRingExpand = 2f;
        private static readonly Color FocusRingColor = new Color(0.45f, 0.78f, 1f);

        private static bool passOpen;
        private static int passOpenFrame;
        private static readonly List<CapturedButton> items = new List<CapturedButton>();
        private static int? pendingClickIndex;

        // A pending click is consumed — and dropped — only by the pass of the arming scope.
        private static object passOwner;
        private static object pendingClickOwner;

        // The capture index the arming scope's cursor sits on (from the
        // immediately preceding pass), or -1 for no ring. Drawn inline in the
        // recording prefix — the only moment the coordinate space is right —
        // mirroring WidgetCapture's own ring mechanism.
        private static int focusedIndex = -1;

        /// <summary>Buttons recorded by the current/most recent pass, in draw order.</summary>
        public static IReadOnlyList<CapturedButton> Items
        {
            get { return items; }
        }

        /// <summary>
        /// Start recording; clears the previous pass. Call from the armed
        /// surface's draw prefix. <paramref name="focusedIndex"/> draws the
        /// shared focus ring on that capture index (sighted-companion parity);
        /// -1 (the default, preserving every pre-existing caller) draws none.
        /// </summary>
        public static void BeginPass(int focusedIndex = -1, object owner = null)
        {
            items.Clear();
            ButtonTextCapture.focusedIndex = focusedIndex;
            passOwner = owner;
            passOpen = true;
            passOpenFrame = Time.frameCount;
        }

        /// <summary>Closes a pass still open on a later frame; same contract as <see cref="WidgetCapture.EndLeakedLivePass"/>.</summary>
        internal static void EndLeakedPass()
        {
            if (!passOpen || Time.frameCount == passOpenFrame)
            {
                return;
            }
            EndPass();
            items.Clear();
            items.TrimExcess();
        }

        /// <summary>Stop recording. Call from the armed surface's draw postfix.</summary>
        public static void EndPass()
        {
            passOpen = false;
            focusedIndex = -1;
            // An unmatched click request dies with the pass it targeted —
            // letting it linger would hit an unrelated button if the surface's
            // layout changed between passes.
            if (ReferenceEquals(passOwner, pendingClickOwner))
            {
                pendingClickIndex = null;
                pendingClickOwner = null;
            }
            passOwner = null;
            InjectedClickGuard.InFlight = false;
        }

        /// <summary>
        /// Result injection: make the button at this
        /// capture index (as recorded by the LAST completed pass) read as
        /// clicked on the NEXT armed pass, so vanilla's own inline click
        /// handling runs unmodified. The request is consumed by the tap's
        /// postfix when the pass reaches that index; <paramref name="owner"/> must match BeginPass's.
        /// </summary>
        public static void RequestClick(int captureIndex, object owner = null)
        {
            pendingClickIndex = captureIndex;
            pendingClickOwner = owner;
        }

        internal static void Record(Rect rect, string label)
        {
            if (!passOpen)
            {
                return;
            }
            CapturedButton button;
            button.Rect = rect;
            button.Label = label ?? "";
            button.ScreenRect = GuiSpace.ToScreen(rect);
            button.VisibleScreenRect = GuiSpace.VisibleScreenRect(rect);
            button.Clip = GuiSpace.CurrentClip();
            items.Add(button);
            if (items.Count - 1 == focusedIndex && rect.width > 0f)
            {
                Rect expanded = rect.ExpandedBy(FocusRingExpand);
                Color previous = GUI.color;
                GUI.color = FocusRingColor;
                Widgets.DrawBox(expanded, 2);
                GUI.color = previous;
            }
        }

        internal static bool TryConsumeInjectedClick()
        {
            if (!passOpen || !pendingClickIndex.HasValue)
            {
                return false;
            }
            if (!ReferenceEquals(passOwner, pendingClickOwner))
            {
                return false;
            }
            if (items.Count - 1 != pendingClickIndex.Value)
            {
                return false;
            }
            pendingClickIndex = null;
            pendingClickOwner = null;
            InjectedClickGuard.InFlight = true;
            return true;
        }
    }

    /// <summary>
    /// The capture tap. Both public ButtonText overloads funnel through this
    /// one (the shorter overload forwards here with NormalOptionColor), so a
    /// single prefix sees every text button in the game.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "ButtonText",
        new Type[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(Color), typeof(bool), typeof(TextAnchor?) })]
    public static class WidgetsButtonTextCapturePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string label)
        {
            ButtonTextCapture.Record(rect, label);
        }

        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (ButtonTextCapture.TryConsumeInjectedClick())
            {
                __result = true;
            }
        }
    }
}
