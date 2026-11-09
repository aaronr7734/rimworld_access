using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Every LearnPsycasts gizmo shares one label and description; only its icon, a level badge,
    /// tells them apart, so the status speaks the level it offers. Label, description and the
    /// Enter vehicle stay with the generic Command handler.
    /// </summary>
    internal sealed class CopLearnPsycastsGizmoHandler : GizmoHandlerBase
    {
        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!CopReflection.Gate.Ensure() || !CopReflection.LearnPsycastsType.IsInstanceOfType(gizmo))
            {
                return false;
            }
            CopReflection.LevelsOf(gizmo, out int low, out int high);
            status = DescribeLevels(low, high);
            return status != null;
        }

        internal static string DescribeLevels(int low, int high)
        {
            if (low <= 0)
            {
                return null;
            }
            return low == high
                ? "RimWorldAccess.Compat.ChoiceOfPsycasts.Level".Translate(low).ToString()
                : "RimWorldAccess.Compat.ChoiceOfPsycasts.LevelRange".Translate(low, high).ToString();
        }
    }
}
