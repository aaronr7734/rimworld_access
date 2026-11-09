using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// The main-menu settings page draws its defaults header, defaults text box and save button
    /// with raw GUI calls no widget tap sees. Each call keeps running as the mod wrote it; the
    /// transpiler only brackets it with a record before and, for the box and the button, the
    /// shared edit session's write-back or the activation after.
    /// </summary>
    internal static class TechAdvancingSettingsCapture
    {
        private static readonly MethodInfo GuiLabel = AccessTools.Method(typeof(GUI), nameof(GUI.Label), new[] { typeof(Rect), typeof(string) });
        private static readonly MethodInfo GuiTextArea = AccessTools.Method(typeof(GUI), nameof(GUI.TextArea), new[] { typeof(Rect), typeof(string) });
        private static readonly MethodInfo GuiButton = AccessTools.Method(typeof(GUI), nameof(GUI.Button), new[] { typeof(Rect), typeof(string) });

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            LocalBuilder rect = generator.DeclareLocal(typeof(Rect));
            LocalBuilder text = generator.DeclareLocal(typeof(string));
            LocalBuilder index = generator.DeclareLocal(typeof(int));
            foreach (CodeInstruction instruction in instructions)
            {
                string before = null;
                string after = null;
                if (instruction.Calls(GuiLabel))
                {
                    before = nameof(RecordLabel);
                }
                else if (instruction.Calls(GuiTextArea))
                {
                    before = nameof(RecordTextArea);
                    after = nameof(OverrideText);
                }
                else if (instruction.Calls(GuiButton))
                {
                    before = nameof(RecordButton);
                    after = nameof(ForceActivate);
                }
                if (before == null)
                {
                    yield return instruction;
                    continue;
                }

                // The call's two arguments are on the stack: park them, record, then replay them.
                var park = new CodeInstruction(OpCodes.Stloc, text);
                park.MoveLabelsFrom(instruction);
                yield return park;
                yield return new CodeInstruction(OpCodes.Stloc, rect);
                yield return new CodeInstruction(OpCodes.Ldloc, rect);
                yield return new CodeInstruction(OpCodes.Ldloc, text);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TechAdvancingSettingsCapture), before));
                if (after != null)
                {
                    yield return new CodeInstruction(OpCodes.Stloc, index);
                }
                yield return new CodeInstruction(OpCodes.Ldloc, rect);
                yield return new CodeInstruction(OpCodes.Ldloc, text);
                yield return instruction;
                if (after != null)
                {
                    yield return new CodeInstruction(OpCodes.Ldloc, index);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TechAdvancingSettingsCapture), after));
                }
            }
        }

        public static void RecordLabel(Rect position, string label)
        {
            WidgetCapture.RecordLabel(position, label);
        }

        public static int RecordTextArea(Rect position, string text)
        {
            return WidgetCapture.RecordTextField(position, text, multiLine: true);
        }

        public static string OverrideText(string result, int index)
        {
            WidgetCapture.MaybeOverrideText(index, ref result);
            return result;
        }

        public static int RecordButton(Rect position, string label)
        {
            return WidgetCapture.RecordButton(position, label, false);
        }

        public static bool ForceActivate(bool result, int index)
        {
            WidgetCapture.MaybeForceActivate(index, ref result);
            return result;
        }
    }
}
