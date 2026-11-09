using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Choice Of Psycasts replaces the random psycast of a new psylink level with a gizmo that
    /// opens a menu of psycasts to learn. Picking runs the mod's own option action; this module
    /// adds the gizmo's level and each option's stats, description and info card.
    /// </summary>
    internal sealed class ChoiceOfPsycastsModule : CompatModule
    {
        public override string TargetPackageId => "azuraal.choiceofpsycasts";

        public override void Activate(Harmony harmony)
        {
            if (!CopReflection.Gate.Ensure())
            {
                return;
            }
            CompatRegistration.GizmoHandler("ChoiceOfPsycasts.LearnPsycasts", _ => new CopLearnPsycastsGizmoHandler());
            FloatMenuOptionDetails.Register(new CopPsycastOptionDetails());
        }
    }
}
