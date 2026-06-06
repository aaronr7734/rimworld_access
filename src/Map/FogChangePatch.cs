using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Marks the scanner's cell-walk cache dirty whenever a cell's fog state changes,
    /// so the next scanner navigation refreshes the Unexplored category (and the rest
    /// of the cell-derived categories that share the same walk).
    /// </summary>
    [HarmonyPatch(typeof(MapEvents), "Notify_CellFogChanged")]
    public static class FogChangePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            ScannerHelper.MarkFogDirty();
        }
    }
}
