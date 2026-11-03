using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Carry Different Medicine Types replaces the Assign tab's Carry cell with a button that opens
    /// its own per-medicine slider dialog. The column handler reads and drives that cell; the dialog
    /// sets absorbInputAroundWindow=false, so it needs an explicit generic-reader registration.
    /// </summary>
    internal sealed class CarryDifferentMedicineTypesModule : CompatModule
    {
        public override string TargetPackageId => "Kopp.CarryDifferentMedicineTypes";

        public override void Activate(Harmony harmony)
        {
            CdmtCarryColumnHandler.Register();
            if (!ScopeForWindow.TryRegisterGenericReaderForWindow(CdmtCarryColumnHandler.DialogTypeName))
            {
                Log.Warning("[RimWorld Access] Carry Different Medicine Types: medicine dialog not found; it will not be read.");
            }
        }
    }
}
