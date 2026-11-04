using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The loadout editor switches pawns from inside DoWindowContents on any key press while an
    /// arrow, comma or period is held, which would also fire on the arrows the scope navigates
    /// with. While a scope drives the window the event's key is masked around the draw, so only the
    /// scope's own chord switches pawns, whichever of the window pass and the dispatcher runs first.
    /// </summary>
    internal static class ClLoadoutEditorDrawPatch
    {
        public static void Install(Harmony harmony)
        {
            MethodInfo target = ClCompat.Editor.DoWindowContents;
            if (target != null)
            {
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(ClLoadoutEditorDrawPatch), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(ClLoadoutEditorDrawPatch), nameof(Postfix)));
            }
        }

        public static void Prefix(Window __instance, out KeyCode __state)
        {
            __state = KeyCode.None;
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown || e.keyCode == KeyCode.None
                || !(ScreenScopeDrawPatch.For(__instance) is ClLoadoutEditorScope))
            {
                return;
            }
            __state = e.keyCode;
            e.keyCode = KeyCode.None;
            FlightRecorder.Record("mask", "Dialog_LoadoutEditor masked " + __state);
        }

        public static void Postfix(Window __instance, KeyCode __state)
        {
            Event e = Event.current;
            if (e == null)
            {
                return;
            }
            if (__state != KeyCode.None)
            {
                e.keyCode = __state;
            }
            if (e.type != EventType.Layout && ScreenScopeDrawPatch.For(__instance) is ClLoadoutEditorScope scope)
            {
                scope.AfterDraw();
            }
        }
    }
}
