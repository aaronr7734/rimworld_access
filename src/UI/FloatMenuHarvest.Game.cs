using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Captures the FloatMenu a delegate would open instead of letting it reach the window
    /// stack, so a mod's right-click branch can be listed as keyboard options without the menu
    /// ever anchoring at the mouse. DialogInterceptionPatch hands the menu over while a capture
    /// is in flight. A delegate that opens no menu yields null; HarvestSilencePatch mutes its sounds.
    /// </summary>
    internal static class FloatMenuHarvest
    {
        private static readonly FieldInfo optionsField = AccessTools.Field(typeof(FloatMenu), "options");

        private static List<FloatMenuOption> captured;

        internal static bool InFlight { get; private set; }

        /// <summary>DialogInterceptionPatch hands over the FloatMenu it suppressed.</summary>
        internal static void NotifyIntercepted(FloatMenu menu)
        {
            captured = optionsField?.GetValue(menu) as List<FloatMenuOption>;
        }

        internal static List<FloatMenuOption> Capture(Action body)
        {
            captured = null;
            InFlight = true;
            try
            {
                body();
            }
            finally
            {
                InFlight = false;
            }

            List<FloatMenuOption> result = captured;
            captured = null;
            return result;
        }
    }
}
