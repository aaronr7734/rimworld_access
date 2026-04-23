using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static class EntityCodexPatch
    {
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_EntityCodex_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_EntityCodex dialog))
                    return;
                try
                {
                    EntityCodexState.Open(dialog);
                }
                catch (Exception ex)
                {
                    Log.Error($"[EntityCodexPatch] Error in PostOpen: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_EntityCodex_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_EntityCodex))
                    return;
                try
                {
                    if (EntityCodexState.IsActive)
                    {
                        EntityCodexState.Close();
                        TolkHelper.Speak($"{"EntityCodex".Translate()} closed.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[EntityCodexPatch] Error in PostClose: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        public static class Window_OnCancelKeyPressed_EntityCodex_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (__instance is Dialog_EntityCodex && EntityCodexState.IsActive)
                    return false;
                return true;
            }
        }

        // Vanilla Window.HandleEventsHighPriority fires OnAcceptKeyPressed on Enter
        // independently of Event.current.Use(). Without this block, pressing Enter on
        // the Alt+I drill-in float menu would close the codex (vanilla Accept = Close)
        // even though our WindowlessFloatMenuState already invoked the option.
        // Same defensive pattern as AnomalySettingsDialogPatch.
        [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        public static class Window_OnAcceptKeyPressed_EntityCodex_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (__instance is Dialog_EntityCodex && EntityCodexState.IsActive)
                    return false;
                return true;
            }
        }
    }
}
