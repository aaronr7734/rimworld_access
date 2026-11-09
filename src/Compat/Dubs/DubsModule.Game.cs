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
        }
    }
}
