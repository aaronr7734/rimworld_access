using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The inspection tree's entry to CashRegister's Shifts tab: activating the category opens the
    /// shift list (<see cref="Shell.CashRegisterShiftsScope"/>).
    /// </summary>
    internal sealed class CashRegisterShiftsAdapter : InspectNodeAdapter
    {
        public static void TryRegister()
        {
            CompatRegistration.TabAdapter("CashRegister.Shifts.ITab_Register_Shifts",
                t => new CashRegisterShiftsAdapter(),
                "CashRegister shifts tab compat");
        }

        public override bool Ready => CashRegisterShiftsCompat.Ready;

        // Dispatch token, never displayed raw.
        public override string CategoryKey => "CashRegister Shifts";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override string DisplayName(InspectTabBase tab) => CashRegisterShiftsCompat.TabName();

        public override string CategoryDisplayName(object obj) => CashRegisterShiftsCompat.TabName();

        public override void ExecuteAction(object obj)
        {
            if (CashRegisterShiftsCompat.IsRegister(obj))
            {
                CashRegisterShiftsState.Open((Thing)obj);
            }
        }
    }
}
