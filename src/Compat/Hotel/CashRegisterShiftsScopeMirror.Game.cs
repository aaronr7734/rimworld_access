using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keeps <see cref="CashRegisterShiftsScope"/> and, above it, <see cref="CashRegisterShiftScope"/>
    /// in lockstep with <see cref="RimWorldAccess.CashRegisterShiftsState"/>. Both pop under an info
    /// card or a dialog carrying its own scope (the Assign dialog), so that window owns the keyboard.
    /// The scopes are built on first open, so the mirror costs nothing while the mod is absent.
    /// </summary>
    internal static class CashRegisterShiftsScopeMirror
    {
        private static CashRegisterShiftsScope listScope;
        private static CashRegisterShiftScope shiftScope;

        public static void Reconcile()
        {
            if (!RimWorldAccess.CashRegisterShiftsState.IsActive)
            {
                Pop();
                return;
            }
            Thing register = RimWorldAccess.CashRegisterShiftsState.Register;
            if (register == null || register.Destroyed)
            {
                RimWorldAccess.CashRegisterShiftsState.Close();
                Pop();
                return;
            }
            if (listScope == null)
            {
                listScope = new CashRegisterShiftsScope();
                shiftScope = new CashRegisterShiftScope();
            }
            if (InfoCardState.IsActive || ShellGuards.ForeignDialogWindowAbove())
            {
                Pop();
                return;
            }
            FocusStack.Push(listScope);
            if (RimWorldAccess.CashRegisterShiftsState.EditingShift != null)
            {
                FocusStack.Push(shiftScope);
            }
            else
            {
                FocusStack.Pop(shiftScope);
            }
        }

        private static void Pop()
        {
            if (shiftScope != null)
            {
                FocusStack.Pop(shiftScope);
            }
            if (listScope != null)
            {
                FocusStack.Pop(listScope);
            }
        }
    }
}
