using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class DubsModule : CompatModule
    {
        public override string TargetPackageId => "Dubwise.DubsBadHygiene";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("DubsBadHygiene.Gizmo_BoilerStatus") != null;

        public override void Activate(Harmony harmony)
        {
            CompatRegistration.GizmoHandler("DubsBadHygiene.Gizmo_BoilerStatus", t => new DubsBoilerStatusGizmoHandler(t));
            DubsWindPumpGhostNote.Register(harmony);

            var groundwater = new DubsGroundwater();
            if (groundwater.Ready)
            {
                TileResourceReadouts.Register(groundwater.TileReadout);
                ScannerCellLayerRegistry.Register(groundwater.ScannerLayer());
                var wellCapacity = new DubsWellCapacity(groundwater);
                DubsPlacementGhostNotes.Register(harmony, groundwater, wellCapacity);
                if (wellCapacity.Ready)
                {
                    PlacementUsefulnessRegistry.Register(wellCapacity.AppliesTo, wellCapacity.MakeRating);
                    DubsSurfaceWaterCapacity.Register(harmony, wellCapacity);
                }
            }
        }
    }
}
