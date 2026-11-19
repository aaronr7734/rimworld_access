using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The flags <see cref="Shell.CashRegisterShiftsScopeMirror"/> watches: the shift list is open
    /// on <see cref="Register"/>, and while <see cref="EditingShift"/> is set, that one shift's hours
    /// are open above it. The tab stays drawn because the inspection tree's focused row is still the
    /// Shifts category, which InspectPaneLink keeps open.
    /// </summary>
    internal static class CashRegisterShiftsState
    {
        internal static bool IsActive
        {
            get { return Register != null; }
        }

        internal static Thing Register { get; private set; }

        internal static object EditingShift { get; private set; }

        /// <summary>Bumped per open, so a singleton scope can tell a fresh open from a re-push after a dialog.</summary>
        internal static int OpenSerial { get; private set; }

        internal static int EditSerial { get; private set; }

        internal static void Open(Thing register)
        {
            Register = register;
            EditingShift = null;
            OpenSerial++;
        }

        internal static void EditShift(object shift)
        {
            EditingShift = shift;
            EditSerial++;
        }

        internal static void EndEdit()
        {
            EditingShift = null;
        }

        internal static void Close()
        {
            Register = null;
            EditingShift = null;
        }
    }
}
