using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The Auto-Combat gizmo's right-click runs DraftedActionData.ToggleAutoForAll, whose result
    /// shows only as icons on the ability gizmos; this speaks how many abilities it left on auto-cast.
    /// </summary>
    internal sealed class AutoCombatRightClickDescriber : IRightClickOutcomeDescriber
    {
        public bool TryCapture(Gizmo gizmo, ISelectable owner, out object snapshot)
        {
            snapshot = null;
            if (!AutoCombatReflection.ToggleWithRClickType.IsInstanceOfType(gizmo) || !(owner is Pawn pawn))
                return false;
            object data = AutoCombatReflection.GetData(pawn);
            if (data == null)
                return false;
            snapshot = AutoCombatReflection.AutoCastCount(data);
            return true;
        }

        public string DescribeChange(Gizmo gizmo, ISelectable owner, object snapshot)
        {
            object data = AutoCombatReflection.GetData((Pawn)owner);
            int count = data == null ? 0 : AutoCombatReflection.AutoCastCount(data);
            if (count == (int)snapshot)
                return null;
            if (count == 0)
                return "RimWorldAccess.Compat.AutoCombat.AllAutoCastOff".Translate();
            if (count == 1)
                return "RimWorldAccess.Compat.AutoCombat.AutoCastOnForOne".Translate();
            return "RimWorldAccess.Compat.AutoCombat.AutoCastOnForMany".Translate(count);
        }
    }
}
