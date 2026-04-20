using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Rewrites Command.GizmoOnGUIInt so gizmo hotkeys require the Shift modifier.
    /// Also updates the visual hotkey badge to read "Shift+K" instead of "K".
    /// </summary>
    [HarmonyPatch(typeof(Command), "GizmoOnGUIInt")]
    public static class GizmoHotkeyShiftPatch
    {
        public const string ShiftPrefix = "Shift+";

        // Gizmo hotkeys that are exempt from the shift requirement (keep vanilla behavior).
        public static bool IsShiftExempt(KeyBindingDef hotKey)
        {
            return hotKey != null && hotKey == KeyBindingDefOf.Command_ColonistDraft;
        }

        public static bool RequireShift(KeyBindingDef hotKey)
        {
            if (hotKey == null) return false;
            if (IsShiftExempt(hotKey)) return hotKey.KeyDownEvent;
            if (!Event.current.shift) return false;
            return hotKey.KeyDownEvent;
        }

        public static string FormatShiftedKeyLabel(KeyCode k)
        {
            // Visual gizmo badge: keep vanilla label for the draft key since it doesn't require Shift.
            var draft = KeyBindingDefOf.Command_ColonistDraft;
            if (draft != null && k == draft.MainKey)
                return k.ToStringReadable();
            return ShiftPrefix + k.ToStringReadable();
        }

        private static readonly MethodInfo KeyDownEventGetter =
            AccessTools.PropertyGetter(typeof(KeyBindingDef), nameof(KeyBindingDef.KeyDownEvent));
        private static readonly MethodInfo ToStringReadableMethod =
            AccessTools.Method(typeof(GenText), nameof(GenText.ToStringReadable), new[] { typeof(KeyCode) });
        private static readonly FieldInfo HotKeyField =
            AccessTools.Field(typeof(Command), nameof(Command.hotKey));
        private static readonly MethodInfo RequireShiftMethod =
            AccessTools.Method(typeof(GizmoHotkeyShiftPatch), nameof(RequireShift));
        private static readonly MethodInfo FormatShiftedKeyLabelMethod =
            AccessTools.Method(typeof(GizmoHotkeyShiftPatch), nameof(FormatShiftedKeyLabel));

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                var ci = codes[i];

                // Only rewrite hotKey.KeyDownEvent, not the Steam-Deck Accept.KeyDownEvent branch.
                // Disambiguate by requiring the preceding instruction to load Command::hotKey.
                if (ci.Calls(KeyDownEventGetter) && i > 0 && codes[i - 1].LoadsField(HotKeyField))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, RequireShiftMethod) { labels = ci.labels, blocks = ci.blocks };
                }
                else if (ci.Calls(ToStringReadableMethod))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, FormatShiftedKeyLabelMethod) { labels = ci.labels, blocks = ci.blocks };
                }
            }
            return codes;
        }
    }
}
