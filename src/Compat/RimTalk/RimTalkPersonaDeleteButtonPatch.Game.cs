using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Bracket on <c>PersonaEditorWindow.DrawFlatDeleteButton</c>: a "×" label under a
    /// ButtonInvisible, drawn straight after the memory text it removes. The bracket records it
    /// once as a button named for that text, and the click is still the mod's own branch.
    /// </summary>
    internal static class RimTalkPersonaDeleteButtonPatch
    {
        public static void Prefix(Rect rect, out int __state)
        {
            __state = -1;
            if (!GUI.enabled || !WidgetCapture.IsPassOpen)
            {
                return;
            }
            string label = "RimWorldAccess.Compat.RimTalk.PersonaEditor.DeleteMemory".Translate(PrecedingLabel());
            __state = WidgetCapture.RecordButton(rect, label, false);
            WidgetCapture.EnterSelfCaptioned(label, "×");
        }

        public static void Postfix(int __state, ref bool __result)
        {
            if (__state < 0)
            {
                return;
            }
            WidgetCapture.ExitSelfCaptioned();
            WidgetCapture.MaybeForceActivate(__state, ref __result);
        }

        private static string PrecedingLabel()
        {
            IReadOnlyList<CapturedWidget> items = WidgetCapture.Items;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].Kind == WidgetKind.Label && !string.IsNullOrEmpty(items[i].Label))
                {
                    return items[i].Label;
                }
            }
            return "";
        }
    }
}
