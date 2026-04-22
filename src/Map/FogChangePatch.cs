using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    public static class FogChangePatch
    {
        [HarmonyPatch(typeof(MapEvents), "Notify_CellFogChanged")]
        public static class MapEvents_CellFogChanged_Patch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                ScannerHelper.FogDirty = true;
            }
        }
    }
}
