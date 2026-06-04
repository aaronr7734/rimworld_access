using System;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Modal text-edit session for renaming an animal pen marker. Routes through
    /// <see cref="TextInputController"/> via the unified pipeline.
    /// </summary>
    public static class PenRenameState
    {
        private static readonly TextInputController Controller = new TextInputController();
        private static CompAnimalPenMarker currentMarker;
        private static string originalName;

        public static bool IsActive => TextInputManager.Active == Controller;

        public static void Open(CompAnimalPenMarker marker)
        {
            if (marker == null)
            {
                Log.Error("Cannot open rename dialog: pen marker is null");
                return;
            }
            currentMarker = marker;
            originalName = marker.RenamableLabel;
            var spec = TextFieldSpec.ForIRenameable(marker, "RimWorldAccess.TextInput.LabelPen");
            Controller.Begin(originalName, spec, OnConfirm, OnCancel, replaceOnType: true);
        }

        private static void OnConfirm(string newName)
        {
            try
            {
                currentMarker.RenamableLabel = newName;
                TolkHelper.Speak("RimWorldAccess.UI.Name.Renamed".Loc(newName), SpeechPriority.High);
                Log.Message($"Renamed pen marker from '{originalName}' to '{newName}'");
            }
            catch (Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Rename.PenError".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"Error renaming pen marker: {ex}");
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
            currentMarker = null;
            originalName = null;
        }
    }
}
