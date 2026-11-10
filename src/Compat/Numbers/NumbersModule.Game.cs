using System;
using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Numbers' overview tabs subclass <c>MainTabWindow_PawnTable</c>, so the generic pawn-table
    /// tier already drives them; this module teaches that tier Numbers' own column workers and
    /// its remove and reorder header gestures. Each registration resolves independently.
    /// </summary>
    internal sealed class NumbersModule : CompatModule
    {
        public override string TargetPackageId => "Mehni.Numbers";

        public override bool ShouldActivate =>
            ModsConfig.IsActive(TargetPackageId) || AccessTools.TypeByName("Numbers.PawnTable_NumbersMain") != null;

        internal static void RegisterWorker(string workerTypeName, IPawnColumnHandler handler)
        {
            CompatRegistration.PawnColumnHandler(workerTypeName, _ => handler);
        }

        public override void Activate(Harmony harmony)
        {
            NumbersColumnExtension extension = NumbersColumnExtension.TryResolve();
            if (extension != null)
            {
                NumbersNeedColumnHandler.Register(extension);
                NumbersSkillColumnHandler.Register(extension);
                NumbersAbilityColumnHandler.Register(extension);
            }
            NumbersPsyfocusColumnHandler.Register();
            NumbersEntropyColumnHandler.Register();
            NumbersBandwidthColumnHandler.Register();
            NumbersIdeologyColumnHandler.Register();
            NumbersDiseaseColumnHandler.Register();
            NumbersCarriedItemsColumnHandler.Register();
            NumbersPrisonerInteractionColumnHandler.Register();
            NumbersOperationsColumnHandler.Register();
            NumbersNeedsTreatmentColumnHandler.Register();
            NumbersDropAllColumnHandler.Register();
            NumbersColumnEditor.Register();
        }
    }
}
