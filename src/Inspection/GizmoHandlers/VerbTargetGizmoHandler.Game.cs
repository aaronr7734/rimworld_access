using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Command_VerbTarget: labels a pawn weapon's attack "weapon: verb", since vanilla leaves it generic.
    /// </summary>
    internal sealed class VerbTargetGizmoHandler : GizmoHandlerBase
    {
        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            // A turret's command carries no owner weapon and its own vanilla label.
            if (!(gizmo is Command_VerbTarget verbTarget) || verbTarget.ownerThing == null)
                return false;

            string weaponName = verbTarget.ownerThing.LabelCap;
            string verbLabel = verbTarget.verb?.ReportLabel ?? "RimWorldAccess.Inspection.Gizmo.AttackFallback".Translate().ToString();
            label = "RimWorldAccess.Inspection.Gizmo.WeaponVerbLabel".Translate(weaponName, verbLabel);
            return true;
        }

        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = true;

            if (!(gizmo is Command_VerbTarget verbTarget))
                return false;

            try
            {
                GizmoNavigationState.PropagateToGroupedGizmos(gizmo, ctx.FakeEvent);
                gizmo.ProcessInput(ctx.FakeEvent);
                GizmoNavigationState.ProcessGroupInput(gizmo, ctx.FakeEvent);

                // GenericTargetingState already announced the session it started.
                if (!GenericTargetingState.IsActive)
                {
                    string weaponName = verbTarget.ownerThing?.LabelCap ?? verbTarget.LabelCap;
                    string verbLabel = verbTarget.verb?.ReportLabel ?? "RimWorldAccess.Inspection.Gizmo.AttackFallback".Translate().ToString();
                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.WeaponVerbTargeting".Loc(weaponName, verbLabel));
                }
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception in Command_VerbTarget execution: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorExecuting".Loc(ctx.GizmoLabel, ex.Message), SpeechPriority.High);
            }
            return true;
        }
    }
}
