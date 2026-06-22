using System;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Modal text-edit session for renaming a <see cref="Plan"/>. Routes through
    /// <see cref="TextInputController"/> via the unified pipeline, mirroring
    /// <see cref="ZoneRenameState"/>.
    /// </summary>
    public static class PlanRenameState
    {
        private static readonly TextInputController Controller = new TextInputController();
        private static Plan currentPlan;
        private static string originalName;

        public static bool IsActive => TextInputManager.Active == Controller;

        public static void Open(Plan plan)
        {
            if (plan == null)
            {
                Log.Error("Cannot open rename dialog: plan is null");
                return;
            }
            currentPlan = plan;
            originalName = plan.RenamableLabel;
            var spec = TextFieldSpec.ForIRenameable(plan, "RimWorldAccess.TextInput.LabelPlan");
            Controller.Begin(originalName, spec, OnConfirm, OnCancel, replaceOnType: true);
        }

        private static void OnConfirm(string newName)
        {
            try
            {
                currentPlan.RenamableLabel = newName;
                TolkHelper.Speak("RimWorldAccess.UI.Name.Renamed".Loc(newName), SpeechPriority.High);
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Rename.PlanError".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"Error renaming plan: {ex}");
            }
            finally
            {
                ClearTarget();
            }
        }

        private static void OnCancel()
        {
            TolkHelper.Speak("RimWorldAccess.Building.Rename.Cancelled".Loc());
            ClearTarget();
        }

        private static void ClearTarget()
        {
            currentPlan = null;
            originalName = null;
        }
    }
}
