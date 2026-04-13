using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_ChangeDryadCaste lifecycle.
    /// Keeps DryadCasteState synchronized with dialog open/close.
    /// </summary>
    public static class DryadCastePatch
    {
        [HarmonyPatch(typeof(Dialog_ChangeDryadCaste), "PreOpen")]
        public static class Dialog_ChangeDryadCaste_PreOpen_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_ChangeDryadCaste __instance)
            {
                DryadCasteState.Open(__instance);
            }
        }

        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_DryadCaste_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (__instance is Dialog_ChangeDryadCaste && DryadCasteState.IsActive)
                {
                    DryadCasteState.Close();
                }
            }
        }
    }
}
