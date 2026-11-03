using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Captures every <see cref="Widgets.TextField(Rect, string)"/> call made
    /// during one armed surface's own draw pass: the rect in the surface's
    /// GUI-group space and the text as rendered. The maxLength/validator
    /// overload funnels through this core one (decompiled Widgets.cs:1775),
    /// so a single tap sees every text field the dialogs draw. Scopes
    /// use the pass result to place focus rings on field rows without
    /// re-deriving vanilla's layout math.
    ///
    /// Recording is BRACKETED to the armed surface's draw exactly like <see cref="ButtonTextCapture"/>:
    /// BeginPass from the surface's draw prefix, EndPass from its postfix or finalizer. Outside a
    /// pass the tap is one static bool check.
    /// </summary>
    public static class TextFieldCapture
    {
        public struct CapturedField
        {
            public Rect Rect;
            public Rect ScreenRect;

            public string Text;
        }

        private static bool passOpen;
        private static int passOpenFrame;
        private static readonly List<CapturedField> items = new List<CapturedField>();

        /// <summary>Fields recorded by the current/most recent pass, in draw order.</summary>
        public static IReadOnlyList<CapturedField> Items
        {
            get { return items; }
        }

        /// <summary>Start recording; clears the previous pass. Call from the armed surface's draw prefix.</summary>
        public static void BeginPass()
        {
            items.Clear();
            passOpen = true;
            passOpenFrame = Time.frameCount;
        }

        internal static void EndLeakedPass()
        {
            if (!CapturePasses.Leaked(passOpen, passOpenFrame))
            {
                return;
            }
            EndPass();
            CapturePasses.Release(items);
        }

        /// <summary>Stop recording. Call from the armed surface's draw postfix.</summary>
        public static void EndPass()
        {
            passOpen = false;
        }

        internal static void Record(Rect rect, string text)
        {
            if (!passOpen)
            {
                return;
            }
            CapturedField field;
            field.Rect = rect;
            field.ScreenRect = GuiSpace.ToScreen(rect);
            field.Text = text ?? "";
            items.Add(field);
        }
    }

    /// <summary>The button-and-field pass pair every text-entry dialog scope brackets its draw with.</summary>
    internal static class TextDialogCapture
    {
        internal static void BeginPass(int focusedButtonIndex = -1)
        {
            ButtonTextCapture.BeginPass(focusedButtonIndex);
            TextFieldCapture.BeginPass();
        }

        internal static void EndPass()
        {
            ButtonTextCapture.EndPass();
            TextFieldCapture.EndPass();
        }

        internal static void EndMaskedPass()
        {
            TextFieldRawPollGuard.RestoreAcceptPoll();
            EndPass();
        }
    }

    /// <summary>The capture tap on the core TextField overload.</summary>
    [HarmonyPatch(typeof(Widgets), "TextField", new Type[] { typeof(Rect), typeof(string) })]
    public static class WidgetsTextFieldCapturePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string text)
        {
            TextFieldCapture.Record(rect, text);
        }
    }
}
