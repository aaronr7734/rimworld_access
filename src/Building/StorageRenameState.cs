using System;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Modal text-edit session for naming/renaming a storage group. Routes through
    /// <see cref="TextInputController"/> via the unified pipeline. Custom validator
    /// rejects names already used by zones or other storage groups.
    /// </summary>
    public static class StorageRenameState
    {
        private static readonly TextInputController Controller = new TextInputController();
        private static IStorageGroupMember currentMember;
        private static string originalName;
        private static bool isCreatingNewGroup;

        public static bool IsActive => TextInputManager.Active == Controller;

        public static void Open(IStorageGroupMember member)
        {
            if (member == null)
            {
                Log.Error("Cannot open storage rename dialog: member is null");
                return;
            }
            currentMember = member;
            isCreatingNewGroup = member.Group == null;
            originalName = isCreatingNewGroup ? string.Empty : (member.Group.RenamableLabel ?? string.Empty);

            var spec = new TextFieldSpec(
                labelKey: "RimWorldAccess.TextInput.LabelStorage",
                maxLength: 28,
                minLength: 1,
                customValidator: ValidateUnique);

            Controller.Begin(originalName, spec, OnConfirm, OnCancel, replaceOnType: true);
        }

        private static AcceptanceReport ValidateUnique(string name)
        {
            Map map = currentMember is Thing thing ? thing.Map : null;
            if (map == null)
                return AcceptanceReport.WasAccepted; // commit will fail loudly

            if (map.zoneManager.AllZones.Any(z => z.label == name))
                return new AcceptanceReport($"name {name} already used by a zone");

            StorageGroup currentGroup = currentMember.Group;
            foreach (var building in map.listerBuildings.allBuildingsColonist)
            {
                if (building is IStorageGroupMember member && member.Group != null
                    && member.Group.RenamableLabel == name && member.Group != currentGroup)
                {
                    return new AcceptanceReport($"name {name} already used by another storage group");
                }
            }
            return AcceptanceReport.WasAccepted;
        }

        private static void OnConfirm(string newName)
        {
            Map map = currentMember is Thing thing ? thing.Map : null;
            if (map == null)
            {
                TolkHelper.Speak("Error: Cannot find map.", SpeechPriority.High);
                ClearTarget();
                return;
            }

            try
            {
                if (isCreatingNewGroup)
                {
                    StorageGroup newGroup = map.storageGroups.NewGroup();
                    newGroup.InitFrom(currentMember);
                    currentMember.SetStorageGroup(newGroup);
                    newGroup.RenamableLabel = newName;
                    TolkHelper.Speak($"Created storage group {newName}", SpeechPriority.High);
                }
                else
                {
                    currentMember.Group.RenamableLabel = newName;
                    TolkHelper.Speak($"Renamed to {newName}", SpeechPriority.High);
                }
            }
            catch (Exception ex)
            {
                TolkHelper.Speak($"Error: {ex.Message}", SpeechPriority.High);
                Log.Error($"Error in storage rename: {ex}");
            }
            finally
            {
                ClearTarget();
            }
        }

        private static void OnCancel()
        {
            TolkHelper.Speak("Cancelled");
            ClearTarget();
        }

        private static void ClearTarget()
        {
            currentMember = null;
            originalName = null;
            isCreatingNewGroup = false;
        }
    }
}
