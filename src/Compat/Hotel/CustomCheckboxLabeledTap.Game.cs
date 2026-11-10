using System;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Hospitality and CashRegister each ship a CustomWidget.CheckboxLabeled that copies vanilla
    /// Widgets.CheckboxLabeled line for line (same signature, Label plus ToggleInvisibleDraggable
    /// plus CheckboxDraw), so their settings checkboxes get the exact bracket vanilla's gets.
    /// </summary>
    internal static class CustomCheckboxLabeledTap
    {
        public static void Patch(Harmony harmony, string customWidgetTypeName, string alternateTypeName = null)
        {
            Type customWidget = AccessTools.TypeByName(customWidgetTypeName)
                ?? (alternateTypeName == null ? null : AccessTools.TypeByName(alternateTypeName));
            if (customWidget == null)
            {
                return;
            }
            MethodInfo checkboxLabeled = AccessTools.Method(customWidget, "CheckboxLabeled",
                new[] { typeof(Rect), typeof(string), typeof(bool).MakeByRefType(), typeof(bool), typeof(Texture2D), typeof(Texture2D), typeof(bool), typeof(bool) });
            if (checkboxLabeled == null)
            {
                ModLogger.Error($"{customWidgetTypeName}.CheckboxLabeled not found, settings checkbox tap skipped.");
                return;
            }
            harmony.Patch(checkboxLabeled,
                prefix: new HarmonyMethod(typeof(WidgetCaptureCheckboxLabeledPatch), nameof(WidgetCaptureCheckboxLabeledPatch.Prefix)),
                postfix: new HarmonyMethod(typeof(WidgetCaptureCheckboxLabeledPatch), nameof(WidgetCaptureCheckboxLabeledPatch.Postfix)));
        }
    }
}
