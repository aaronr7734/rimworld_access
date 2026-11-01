using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Synthetic "Rename" category adapter: opens the rename dialog vanilla's inspect pane opens
    /// for a zone or a pen-marker building (MainTabWindow_Inspect via RenameUIUtility).
    /// </summary>
    internal sealed class RenameAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Rename";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (obj is Zone zone)
            {
                Find.WindowStack.Add(new Dialog_RenameBuildingStorage(zone));
                return;
            }

            if (obj is Building building)
            {
                var penMarker = building.TryGetComp<CompAnimalPenMarker>();
                if (penMarker != null)
                {
                    Find.WindowStack.Add(new Dialog_RenameAnimalPen(penMarker.parent.Map, penMarker));
                }
            }
        }
    }
}
