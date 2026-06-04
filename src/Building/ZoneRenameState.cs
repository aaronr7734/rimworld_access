using System;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Modal text-edit session for renaming a Zone. Routes through
    /// <see cref="TextInputController"/> via the unified pipeline.
    /// </summary>
    public static class ZoneRenameState
    {
        private static readonly TextInputController Controller = new TextInputController();
        private static Zone currentZone;
        private static string originalName;

        public static bool IsActive => TextInputManager.Active == Controller;

        public static void Open(Zone zone)
        {
            if (zone == null)
            {
                Log.Error("Cannot open rename dialog: zone is null");
                return;
            }
            currentZone = zone;
            originalName = zone.label;
            var spec = TextFieldSpec.ForIRenameable(zone, "RimWorldAccess.TextInput.LabelZone");
            Controller.Begin(originalName, spec, OnConfirm, OnCancel, replaceOnType: true);
        }

        private static void OnConfirm(string newName)
        {
            try
            {
                currentZone.label = newName;
                TolkHelper.Speak("RimWorldAccess.UI.Name.Renamed".Loc(newName), SpeechPriority.High);
                Log.Message($"Renamed zone from '{originalName}' to '{newName}'");
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Rename.ZoneError".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"Error renaming zone: {ex}");
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
            currentZone = null;
            originalName = null;
        }
    }
}
